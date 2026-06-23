/**
 * MineflayerAdapter — Node.js bridge between the MemorySmith.Agent C# host
 * and a Minecraft server.
 *
 * Sprint 2a: craft case now pathfinds to the nearest crafting table before
 *   calling bot.craft() for recipes that require one.
 *
 * Phase 5b additions:
 *   - chat event: includes playerX/Y/Z.
 *
 * Sprint 9 (flat-area scanner):
 *   - A1: vertical scan window widened.
 *   - A2: compactness scoring added.
 *   - A5: slope/roughness penalty.
 *
 * Sprint 18 (house-building MVP):
 *   - toVec3(x,y,z): helper that creates a position object with .floored() so
 *     bot.blockAt() doesn't crash. Mineflayer now calls pos.floored() internally;
 *     plain {x,y,z} objects no longer work. All blockAt calls in findFlatArea updated.
 *   - Emergency stop: handleStop() clears cmdQueue, stops pathfinder, sets _stopRequested.
 *     The "stop" action bypasses the command queue and executes immediately in the ws
 *     message handler — so "leo stop" now truly stops in-progress mining/pathfinding.
 *   - Mine/wander/findFlatArea check _stopRequested at the start of each iteration.
 *
 * Sprint 25 (action correlation):
 *   - correlationId: extracted from incoming action message and echoed in all result
 *     events. Enables C#-side end-to-end action lifecycle tracking.
 */

import mineflayer from 'mineflayer';
import mflPathfinder from 'mineflayer-pathfinder';
const { pathfinder, Movements, goals: pfGoals } = mflPathfinder;
import { WebSocketServer } from 'ws';
import { appendFileSync, mkdirSync, existsSync } from 'node:fs';
import { emitGameModeEvent, normalizeGameMode } from './gameModeState.js';

// ── Environment / connection ──────────────────────────────────────────────────

const WS_PORT  = parseInt(process.env.WS_PORT   ?? '3000',  10);
const MC_HOST  = process.env.MC_HOST   ?? 'localhost';
const MC_PORT  = parseInt(process.env.MC_PORT   ?? '25565', 10);
const MC_USER  = process.env.MC_USERNAME ?? 'Leo';
const MC_VER   = process.env.MC_VERSION;
const WS_TOKEN = process.env.WS_TOKEN ?? null;

// ── Tunable constants ─────────────────────────────────────────────────────────

const MINE_SEARCH_RADIUS_NEAR    = 64;
const MINE_SEARCH_RADIUS_FAR     = 128;
const MAX_MINE_PATH_FAILURES     = 3;
const CRAFT_TABLE_SEARCH_RADIUS  = 8;
const CRAFT_TABLE_REACH_DISTANCE = 2;
const FURNACE_SEARCH_RADIUS      = 16;
const FURNACE_REACH_DISTANCE     = 2;
const SMELT_TIMEOUT_MS           = 40_000;

// Sprint 40 P0-B: item pickup tuning.
// After mining a block, the bot waits this long for the item entity to appear
// and be auto-collected. Then moves to the block position and waits again.
const MINE_ITEM_PICKUP_WAIT_MS           = 1000;  // wait for auto-pickup after dig
const MINE_ITEM_PICKUP_MOVE_WAIT_MS      = 1500;  // wait after moving to block pos
const MINE_ITEM_PICKUP_REMOVE_BLOCK_MS   = 300;   // wait after removing obstruction

// Sprint 40 P0-B: reachable block search tuning.
const REACHABLE_BLOCK_MAX_CANDIDATES     = 20;    // max blocks to reachability-check
const REACHABLE_BLOCK_PATH_TIMEOUT_MS    = 5000;  // pathfinding timeout per candidate
const REACHABLE_BLOCK_GOTO_TOLERANCE     = 2;     // pathfinder GoalNear tolerance

// Sprint 9: flat-area scan defaults.
// Sprint 19: increased default radius from 20 to 32 for better initial coverage.
// C# planner sends radius=48 on retry after a zero-area result.
const FLAT_AREA_SCAN_RADIUS      = 32;
const FLAT_AREA_MIN_SIZE         = 25;
const FLAT_AREA_Y_ABOVE          = 10;
const FLAT_AREA_Y_BELOW          = 16;
const FLAT_AREA_MAX_SLOPE        = 3;

// Sprint 37: added proximity weight so closer flat areas (ground-level) are
// preferred over far-away ones (tower tops, distant platforms).
const FLAT_SCORE_WEIGHTS = Object.freeze({
  area:        0.35,
  compactness: 0.20,
  flatness:    0.15,
  proximity:   0.30,
});

const LIQUID_BLOCK_NAMES = new Set(['water', 'lava', 'flowing_water', 'flowing_lava']);

// ── Sprint 19: Structured file logging ────────────────────────────────────────
// Writes JSON lines to a daily rolling log file alongside the C# host's Serilog output.
// Console stays concise (summary lines only); the file captures full structured context
// for post-hoc diagnostics: block names, counts, coordinates, timing, args.

const LOG_DIR = process.env.LOG_DIR ?? './logs';
try { if (!existsSync(LOG_DIR)) mkdirSync(LOG_DIR, { recursive: true }); } catch { /* best-effort */ }

/**
 * Writes a structured JSON line to the daily adapter log file.
 * @param {'debug'|'info'|'warn'|'error'} level
 * @param {string} category - action category (mine, wander, findFlatArea, craft, smelt, dispatch)
 * @param {string} message - human-readable summary
 * @param {Object} [data] - structured context (merged into the JSON entry)
 */
function logStructured(level, category, message, data = {}) {
  const entry = JSON.stringify({
    t: new Date().toISOString(),
    l: level,
    c: category,
    m: message,
    ...data,
  });
  const dateStr = new Date().toISOString().split('T')[0];
  try {
    appendFileSync(`${LOG_DIR}/adapter-${dateStr}.log`, entry + '\\n');
  } catch { /* best-effort — never crash the bot on log I/O failure */ }
}

// ── Sprint 18: Emergency stop state ───────────────────────────────────────────
// Set by handleStop(), cleared at the start of each action case.
// Checked inside long-running loops (mine while, findFlatArea column scan) to
// allow the C# "stop" command to abort in-progress actions immediately.

let _stopRequested = false;

/**
 * Immediately aborts the current operation:
 *   1. Sets _stopRequested so in-progress loops exit on next iteration.
 *   2. Clears cmdQueue so no more queued commands run.
 *   3. Calls bot.pathfinder.setGoal(null) to cancel active pathfinding.
 *
 * Called directly from the WebSocket message handler (bypasses enqueueCommand)
 * so it takes effect immediately without waiting for the queue to drain.
 */
function handleStop() {
  console.log('[stop] emergency stop — clearing queue, stopping pathfinder');
  _stopRequested = true;
  cmdQueue.length = 0; // Drain pending commands
  try { bot.pathfinder.setGoal(null); } catch { /* ignore — bot may not be connected */ }
  sendEvent('stopComplete', {});
  console.log('[stop] done');
}

// ── Sprint 18: Vec3 compatibility helper ──────────────────────────────────────
// Mineflayer's bot.blockAt() and world.getBlock() call pos.floored() internally.
// Plain {x,y,z} objects no longer work in current Mineflayer versions.
// This helper creates a minimal Vec3-compatible object for integer coordinates.
// .floored() is a no-op (values are already integers from Math.round/Math.floor).
// .offset() returns another toVec3 so chained calls also work.

function toVec3(x, y, z) {
  const ix = Math.floor(x), iy = Math.floor(y), iz = Math.floor(z);
  return {
    x: ix, y: iy, z: iz,
    floored() { return this; },
    offset(dx, dy, dz) { return toVec3(ix + dx, iy + dy, iz + dz); },
  };
}

// ── WebSocket server ─────────────────────────────────────────────────────────

const wss = new WebSocketServer({ port: WS_PORT });
let agentSocket = null;
let spawnPos = null;

function sendEvent(event, data = {}) {
  if (agentSocket?.readyState === 1 /* OPEN */) {
    agentSocket.send(JSON.stringify({ event, ...data }));
  }
}

// ── Sequential command queue ─────────────────────────────────────────────────

const cmdQueue = [];
let dispatching = false;

function enqueueCommand(msg) {
  cmdQueue.push(msg);
  if (!dispatching) drainQueue();
}

async function drainQueue() {
  dispatching = true;
  while (cmdQueue.length > 0) {
    const msg = cmdQueue.shift();
    await dispatch(msg).catch(e => {
      console.error(`[dispatch] ${msg.action} failed:`, e.message);
      sendEvent('error', { action: msg.action, message: e.message });
    });
  }
  dispatching = false;
}

// ── WebSocket connection handling ────────────────────────────────────────────

wss.on('listening', () => console.log(`[ws] listening on port ${WS_PORT}`));

// Sprint 32 SEC-02: connection-level authentication state.
// When WS_TOKEN is set, the first message from each connection must be a
// handshake message: {"type":"handshake","secret":"<WS_TOKEN>"}.
// Commands arriving before a successful handshake are rejected and the
// connection is closed. When WS_TOKEN is null, all connections are trusted
// (dev/localhost mode). The secret value is never logged.

wss.on('connection', (ws) => {
  console.log('[ws] C# agent connected');
  agentSocket = ws;

  // Track per-connection auth state. No secret configured → pre-authenticated.
  let isAuthenticated = !WS_TOKEN;

  ws.on('message', (raw) => {
    let msg;
    try { msg = JSON.parse(raw.toString()); }
    catch (e) { console.error('[ws] bad JSON:', e.message); return; }

    // Sprint 32 SEC-02: handle handshake message type first.
    if (msg.type === 'handshake') {
      if (WS_TOKEN && msg.secret !== WS_TOKEN) {
        console.warn('[ws] handshake rejected: invalid secret');
        ws.close(1008, 'Unauthorized');
        return;
      }
      isAuthenticated = true;
      console.log('[ws] handshake accepted');
      return;
    }

    // Reject any command arriving before a successful handshake.
    if (!isAuthenticated) {
      console.warn('[ws] command rejected: not authenticated (missing handshake)');
      ws.close(1008, 'Unauthorized');
      return;
    }

    // Sprint 18: emergency stop bypasses the command queue — execute immediately.
    // This ensures "leo stop" actually stops in-progress mining/pathfinding
    // rather than being queued behind the current action.
    if (msg.action === 'stop' || msg.action === 'StopNow' || msg.action === 'EmergencyStop') {
      handleStop();
      return;
    }

    enqueueCommand(msg);
  });

  ws.on('close', () => {
    if (agentSocket === ws) agentSocket = null;
    console.log('[ws] C# agent disconnected');
  });

  ws.on('error', (e) => console.error('[ws] socket error:', e.message));

  if (bot?.entity) {
    sendBotStatus();
    emitGameModeEvent(bot, sendEvent, logStructured);
  }
});

// ── Mineflayer bot ────────────────────────────────────────────────────────────

const botOpts = {
  host: MC_HOST,
  port: MC_PORT,
  username: MC_USER,
  ...(MC_VER ? { version: MC_VER } : {}),
};

console.log(`[mc] connecting to ${MC_HOST}:${MC_PORT} as ${MC_USER}`);
const bot = mineflayer.createBot(botOpts);
bot.loadPlugin(pathfinder);

function botPos() {
  const p = bot.entity?.position ?? { x: 0, y: 64, z: 0 };
  return { x: Math.round(p.x), y: Math.round(p.y), z: Math.round(p.z) };
}

function sendBotStatus() {
  const invItems = bot.inventory?.items() ?? [];
  const invMap = {};
  for (const item of invItems) {
    invMap[item.name] = (invMap[item.name] ?? 0) + item.count;
  }
  // Sprint 37: include game mode so C# ApplyStatus can set it from the status
  // response. Previously game mode was only set via GameModeChangedEvent which
  // fires asynchronously and can be missed on startup.
  const rawMode = bot?.game?.gameMode;
  const gameMode = rawMode != null ? normalizeGameMode(rawMode) : undefined;
  sendEvent('status', {
    ...botPos(),
    hp:   bot.health ?? 20,
    food: bot.food   ?? 20,
    inventory: invMap,
    ...(gameMode ? { gameMode } : {}),
  });
}

// ── Bot event forwarding ──────────────────────────────────────────────────────

bot.once('spawn', () => {
  spawnPos = { x: bot.entity.position.x, y: bot.entity.position.y, z: bot.entity.position.z };
  console.log('[mc] bot spawned at', botPos());
  sendEvent('spawn', { ...botPos(), hp: bot.health, food: bot.food });
  emitGameModeEvent(bot, sendEvent, logStructured);
});

bot.on('health', () => sendEvent('health', { hp: bot.health, food: bot.food }));
bot.on('move',   () => sendEvent('move',   botPos()));
bot.on('death',  () => { console.warn('[mc] bot died'); sendEvent('death', botPos()); });
bot.on('kicked', (reason) => { console.warn('[mc] kicked:', reason); sendEvent('kicked', { reason }); });
bot.on('error',  (e)      => { console.error('[mc] error:', e.message); sendEvent('error', { message: e.message }); });
bot.on('game', () => emitGameModeEvent(bot, sendEvent, logStructured));

// Sprint 35 P0-A: emit itemCollected when the bot picks up a dropped item entity.
// Guard: only fire for the bot's own collections, not other players picking up items.
// Provides the TRUE item drop name (e.g. "diamond" from diamond_ore, "cobblestone" from stone).
// WorldStateProjector.ApplyItemCollected uses this as the authoritative inventory source.
// Periodic GetStatus reconciles any drift.
bot.on('playerCollect', (collector, entity) => {
  if (collector.username !== bot.username) return;
  // entity.metadata.name is the item type (bare name, e.g. "diamond"); entity.count is quantity.
  const itemName = entity?.metadata?.name ?? entity?.displayName ?? 'unknown';
  const count = entity?.count ?? 1;
  sendEvent('itemCollected', { item: itemName, count });
  logStructured('debug', 'collect', 'item collected', { item: itemName, count });
});

// ── Sprint 19: System message filtering ───────────────────────────────────────
// Server-generated messages (teleport confirmations, join/leave, time set, etc.)
// must never reach the LLM chat pipeline. In solo play, all messages pass the
// IsDirectedAtBot heuristic, so a teleport like "Teleported TheMasonX23 to Leo"
// triggers a 15-second Ollama call that returns null. Filter them at the source.

const SYSTEM_MESSAGE_PATTERNS = [
  /^Teleported\s+\S+\s+to\s+\S+/i,      // Teleport confirmations
  /^\S+\s+joined\s+the\s+game$/i,       // Join messages
  /^\S+\s+left\s+the\s+game$/i,         // Leave messages
  /^\[Server\]/i,                       // Server-prefixed messages
  /^Set\s+the\s+time\s+to\s+/i,         // Time set
  /^Set\s+\S+\s+game\s+mode\s+to\s+/i,  // Gamemode changes
  /^Killed\s+/i,                        // Kill notifications
  /^Gave\s+\d+\s+/i,                    // /give command confirmations
  /^Set\s+own\s+game\s+mode/i,          // Own gamemode change
  // Sprint 20: additional server-confirmation patterns observed in runtime
  /^Removed\s+\d+\s+items?\s+from\s+/i, // /clear response: "Removed 13 items from player Leo"
  /^Cleared\s+(?:\d+|\S+'s|the\s+inventory)/i, // /clear: "Cleared 64 items", "Cleared Leo's inventory", "Cleared the inventory of Leo"
  /^Gave\s+\S+\s+\d+\s+/i,             // /give alt: "Gave TheMasonX23 64 [Dirt]"
];

/**
 * Returns true if the message is a Minecraft server system message that should
 * not be forwarded to the C# chat pipeline.
 */
function isSystemMessage(username, message) {
  // No username or empty username = server message
  if (!username || username.trim() === '') return true;
  return SYSTEM_MESSAGE_PATTERNS.some(re => re.test(message));
}

bot.on('chat', (username, message) => {
  // Sprint 38 P0-D (BUG-D): defensive try/catch — any uncaught error in the
  // chat handler would crash the adapter process. Errors are logged and the
  // event is dropped rather than propagating.
  try {
    if (username === bot.username) return;

    // Sprint 19: filter system messages before they reach the LLM pipeline
    if (isSystemMessage(username, message)) {
      logStructured('debug', 'chat', 'system message filtered', { username, message });

      const gameModeMatch = message.match(/game mode to (.+)$/i);
      if (gameModeMatch) {
        const normalizedMode = normalizeGameMode(gameModeMatch[1]);
        if (normalizedMode) {
          sendEvent('gameMode', { mode: normalizedMode });
          logStructured('info', 'chat', 'gamemode detected', { mode: normalizedMode });
        }
      }

      // If the bot was teleported, emit a position update so WorldState stays current
      const teleportMatch = message.match(/^Teleported\s+(\S+)\s+to\s+(\S+)/i);
      if (teleportMatch && teleportMatch[1].toLowerCase() === bot.username.toLowerCase()) {
        setTimeout(() => {
          if (bot.entity) {
            sendEvent('move', botPos());
            logStructured('info', 'chat', 'bot teleport detected — position update sent', botPos());
          }
        }, 100);
      }
      return;
    }

    const onlinePlayers = Object.keys(bot.players).filter(p => p !== bot.username).length;
    const playerEntity  = bot.players[username]?.entity;
    const playerPos     = playerEntity?.position ?? null;
    sendEvent('chat', {
      username,
      message,
      onlinePlayers,
      playerX: playerPos ? Math.round(playerPos.x) : null,
      playerY: playerPos ? Math.round(playerPos.y) : null,
      playerZ: playerPos ? Math.round(playerPos.z) : null,
    });
  } catch (err) {
    console.error('[chat] handler error:', err.message);
    logStructured('error', 'chat', 'handler threw', { username, message, error: err.message });
  }
});

// ── Action dispatcher ─────────────────────────────────────────────────────────

async function dispatch({ action, arguments: args = {}, correlationId }) {
  const _dispatchStart = Date.now();
  logStructured('debug', 'dispatch', 'received', { action, args });
  try {
  switch (action) {

    case 'move': {
      const { x, y, z } = args;
      if (x == null || y == null || z == null)
        throw new Error('move requires x, y, z');
      const movements = new Movements(bot);
      bot.pathfinder.setMovements(movements);
      await bot.pathfinder.goto(new pfGoals.GoalNear(x, y, z, 1));
      sendEvent('moveComplete', { ...botPos(), correlationId });
      break;
    }

    case 'mine': {
      const { block: blockName, count = 1 } = args;
      if (!blockName) throw new Error('mine requires block name');

      const shortName  = blockName.replace('minecraft:', '');
      const blockEntry = bot.registry.blocksByName[shortName];
      if (!blockEntry) throw new Error(`Unknown block: ${blockName}`);
      const blockId = blockEntry.id;

      const movements = new Movements(bot);
      bot.pathfinder.setMovements(movements);

      let mined = 0;
      let pathFailures = 0;

      // Sprint 18: reset stop flag for this action
      _stopRequested = false;
      const _mineStart = Date.now();
      logStructured('info', 'mine', 'start', { block: shortName, targetCount: count, pos: botPos() });

      while (mined < count) {
        // Sprint 18: check stop flag at start of each iteration
        if (_stopRequested) {
          console.log(`[mine] aborted by stop signal after ${mined}/${count} ${shortName}`);
          sendEvent('mineAborted', { block: shortName, mined, correlationId });
          return; // Exit dispatch — don't send a normal completion event
        }

        let target = bot.findBlock({ matching: blockId, maxDistance: MINE_SEARCH_RADIUS_NEAR });
        if (!target) target = bot.findBlock({ matching: blockId, maxDistance: MINE_SEARCH_RADIUS_FAR });

        if (!target) {
          console.log(`[mine] no ${shortName} found (mined ${mined}/${count})`);
          logStructured('warn', 'mine', 'no blocks in range', {
            block: shortName, mined, targetCount: count,
            searchRadius: MINE_SEARCH_RADIUS_FAR, elapsedMs: Date.now() - _mineStart,
          });
          sendEvent('blockNotFound', { block: blockName, mined, correlationId });
          if (mined === 0) throw new Error(`No ${shortName} found within ${MINE_SEARCH_RADIUS_FAR} blocks`);
          break;
        }

        try {
          await bot.pathfinder.goto(
            new pfGoals.GoalNear(target.position.x, target.position.y, target.position.z, 2)
          );
          pathFailures = 0;
        } catch (e) {
          if (_stopRequested) {
            console.log(`[mine] aborted during navigation after ${mined}/${count} ${shortName}`);
            return;
          }
          pathFailures++;
          console.warn(`[mine] nav to ${shortName} failed (${pathFailures}/${MAX_MINE_PATH_FAILURES}): ${e.message}`);
          if (pathFailures >= MAX_MINE_PATH_FAILURES)
            throw new Error(`Pathfinding to ${shortName} failed ${MAX_MINE_PATH_FAILURES} times: ${e.message}`);
          await new Promise(r => setTimeout(r, 500));
          continue;
        }

        const fresh = bot.blockAt(target.position);
        if (!fresh || fresh.type !== blockId) {
          await new Promise(r => setTimeout(r, 100));
          continue;
        }

        try {
          await bot.dig(fresh);
          mined++;
          pathFailures = 0;
          // Sprint 36: send delta count (1 per dig) instead of cumulative mined counter.
          // The C# ApplyBlockMined uses e.Count as an additive delta via AddInventoryItem.
          // Sending cumulative count caused inventory ballooning (e.g. count=1, then 2, then 3
          // was interpreted as +1, +2, +3 = 6 dirt instead of +1+1+1 = 3 dirt).
          sendEvent('blockMined', { block: shortName, count: 1, ...botPos(), correlationId });

          // Sprint 40 P0-B: Ensure the dropped item is collected.
          // After digging, the item entity appears at the block position. Wait briefly
          // for auto-pickup (bot within ~1 block range). If not collected, move to the
          // block position to force pickup. This prevents items falling through holes
          // or landing out of reach from being lost forever.
          await new Promise(r => setTimeout(r, MINE_ITEM_PICKUP_WAIT_MS));
          // Check if the item at this block position is still an entity (not collected)
          // by looking for a dropped item entity near the dig position.
          const nearbyEntity = bot.nearestEntity(e => {
            if (!e || e.type !== 'object' || e.objectType !== 'Item') return false;
            const dx = e.position.x - target.position.x;
            const dy = e.position.y - target.position.y;
            const dz = e.position.z - target.position.z;
            return Math.abs(dx) <= 2 && Math.abs(dy) <= 2 && Math.abs(dz) <= 2;
          });
          if (nearbyEntity) {
            // Item entity still present — move closer to collect it
            try {
              await bot.pathfinder.goto(
                new pfGoals.GoalNear(
                  target.position.x, target.position.y, target.position.z, 1)
              );
              // Wait a bit more for collection
              await new Promise(r => setTimeout(r, MINE_ITEM_PICKUP_MOVE_WAIT_MS));
            } catch {
              // Movement failed — item may still be picked up if bot is close enough
              await new Promise(r => setTimeout(r, MINE_ITEM_PICKUP_REMOVE_BLOCK_MS));
            }
          }
        } catch (e) {
          if (_stopRequested) { console.log(`[mine] aborted after dig error`); return; }
          console.warn(`[mine] dig failed: ${e.message}`);
          await new Promise(r => setTimeout(r, 500));
        }
      }
      logStructured('info', 'mine', 'complete', {
        block: shortName, mined, targetCount: count, elapsedMs: Date.now() - _mineStart,
      });
      // Sprint 35 P0-B: emit mineComplete at the end of the mining loop — definitive signal.
      // Consumed by AgentBackgroundService to transition correlated action to Completed state.
      sendEvent('mineComplete', { block: shortName, mined, targetCount: count, correlationId });
      break;
    }

    case 'place': {
      const { x, y, z, material } = args;
      if (x == null || !material) throw new Error('place requires x, y, z, material');

      const movements = new Movements(bot);
      bot.pathfinder.setMovements(movements);
      await bot.pathfinder.goto(new pfGoals.GoalNear(x, y, z, 3));

      const shortMat = material.replace('minecraft:', '');
      const item = bot.inventory.items().find(i => i.name === shortMat || i.name === material);
      if (!item) throw new Error(`${material} not in inventory`);
      await bot.equip(item, 'hand');

      const offsets = [
        { dx: 0, dy: -1, dz: 0,  fx: 0,  fy: 1,  fz: 0  },
        { dx: 0, dy: 1,  dz: 0,  fx: 0,  fy: -1, fz: 0  },
        { dx: -1, dy: 0, dz: 0,  fx: 1,  fy: 0,  fz: 0  },
        { dx: 1,  dy: 0, dz: 0,  fx: -1, fy: 0,  fz: 0  },
        { dx: 0,  dy: 0, dz: -1, fx: 0,  fy: 0,  fz: 1  },
        { dx: 0,  dy: 0, dz: 1,  fx: 0,  fy: 0,  fz: -1 },
      ];

      let placed = false;
      for (const { dx, dy, dz, fx, fy, fz } of offsets) {
        const ref = bot.blockAt(bot.entity.position.offset(
          dx + (x - Math.round(bot.entity.position.x)),
          dy + (y - Math.round(bot.entity.position.y)),
          dz + (z - Math.round(bot.entity.position.z))));
        if (!ref || ref.type === 0) continue;
        try {
          await bot.placeBlock(ref, { x: fx, y: fy, z: fz });
          placed = true;
          break;
        } catch { /* try next face */ }
      }

      if (!placed) throw new Error(`Cannot place ${material} at (${x},${y},${z}) — no solid reference block`);
      sendEvent('blockPlaced', { x, y, z, block: shortMat, correlationId });
      break;
    }

    case 'wander': {
      const { radius = 20, maxDistanceFromSpawn = 100 } = args;
      const angle = Math.random() * 2 * Math.PI;
      const dist  = 5 + Math.random() * (radius - 5);

      const curX = bot.entity.position.x;
      const curZ = bot.entity.position.z;
      let tX = curX + Math.cos(angle) * dist;
      let tZ = curZ + Math.sin(angle) * dist;

      if (spawnPos && maxDistanceFromSpawn > 0) {
        const dxS = tX - spawnPos.x;
        const dzS = tZ - spawnPos.z;
        const distFromSpawn = Math.sqrt(dxS * dxS + dzS * dzS);
        if (distFromSpawn > maxDistanceFromSpawn) {
          const scale = maxDistanceFromSpawn / distFromSpawn;
          tX = spawnPos.x + dxS * scale;
          tZ = spawnPos.z + dzS * scale;
        }
      }

      // Sprint 18: reset stop flag; pathfinder.setGoal(null) in handleStop() will
      // cause goto() to throw, which is caught below — no extra check needed.
      _stopRequested = false;
      logStructured('info', 'wander', 'start', {
        radius, maxDistanceFromSpawn, targetX: Math.round(tX), targetZ: Math.round(tZ),
        fromPos: botPos(),
      });

      const movements = new Movements(bot);
      bot.pathfinder.setMovements(movements);
      try {
        await bot.pathfinder.goto(
          new pfGoals.GoalNear(Math.round(tX), Math.round(bot.entity.position.y), Math.round(tZ), 2)
        );
        sendEvent('wanderComplete', { ...botPos(), targetX: Math.round(tX), targetZ: Math.round(tZ), correlationId });
      } catch (e) {
        if (_stopRequested) {
          console.log('[wander] aborted by stop signal');
          return;
        }
        console.warn(`[wander] pathfinding failed: ${e.message}`);
        sendEvent('wanderFailed', { message: e.message, ...botPos(), correlationId });
      }
      break;
    }

    case 'findFlatArea': {
      // Sprint 9: all tuning values are named constants with per-call overrides.
      const {
        radius      = FLAT_AREA_SCAN_RADIUS,
        minFlatArea = FLAT_AREA_MIN_SIZE,
        yAbove      = FLAT_AREA_Y_ABOVE,
        yBelow      = FLAT_AREA_Y_BELOW,
        maxSlope    = FLAT_AREA_MAX_SLOPE,
        scanOriginX, scanOriginY, scanOriginZ,
      } = args;

      // Sprint 37: if scanOrigin is provided, center the scan there instead of at
      // the bot's current position. This lets the C# side say "find flat ground
      // near X,Y,Z" rather than "find flat ground near wherever the bot is".
      const scanCenterX = scanOriginX != null ? scanOriginX : null;
      const scanCenterZ = scanOriginZ != null ? scanOriginZ : null;

      let botPosObj = botPos();
      const r       = Math.max(1, Math.min(radius, 64));
      const minArea = Math.max(1, Math.min(minFlatArea, 256));

      // Sprint 18: reset stop flag for this scan
      _stopRequested = false;

      // Sprint 35: wait for chunks to load before scanning. The bot may have just
      // moved to this area and blockAt() returns null for unloaded chunks, which
      // produces an empty height map and a false "no flat area" result.
      // Uses a custom wait that covers the full scan radius (not just the default
      // 5x5 chunk window from bot.waitForChunksToLoad).
      const chunkRadius = Math.ceil(r / 16) + 2; // +2 for safety margin (Sprint 37: increased from +1 for boundary chunks)
      try {
        const pos = bot.entity?.position ?? { x: 0, y: 0, z: 0 };
        const chunkPosToCheck = new Set();
        const centerCX = Math.floor(pos.x / 16);
        const centerCZ = Math.floor(pos.z / 16);
        for (let cx = centerCX - chunkRadius; cx <= centerCX + chunkRadius; cx++) {
          for (let cz = centerCZ - chunkRadius; cz <= centerCZ + chunkRadius; cz++) {
            const col = bot.world.getColumn(cx, cz);
            if (!col) chunkPosToCheck.add(`${cx},${cz}`);
          }
        }
        if (chunkPosToCheck.size > 0) {
          await new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
              bot.world.off('chunkColumnLoad', waitForLoad);
              reject(new Error(`Timeout waiting for ${chunkPosToCheck.size} chunks`));
            }, 10000);
            function waitForLoad(columnCorner) {
              const cx = Math.floor(columnCorner.x / 16);
              const cz = Math.floor(columnCorner.z / 16);
              chunkPosToCheck.delete(`${cx},${cz}`);
              if (chunkPosToCheck.size === 0) {
                clearTimeout(timeout);
                bot.world.off('chunkColumnLoad', waitForLoad);
                resolve();
              }
            }
            bot.world.on('chunkColumnLoad', waitForLoad);
          });
        }
      } catch {
        // If chunks time out, log and continue — scan will use whatever is loaded.
        console.warn('[findFlatArea] chunk load wait timed out — scanning with loaded chunks only');
        logStructured('warn', 'findFlatArea', 'chunk load timeout', { radius: r });
      }

      // Re-read position after chunk loading (bot may have settled)
      botPosObj = botPos();
      const _scanStart = Date.now();

      // Sprint 37: use scanOrigin as the scan center if provided, otherwise use botPos.
      const scanCenter = {
        x: scanCenterX ?? botPosObj.x,
        y: scanOriginY ?? botPosObj.y,
        z: scanCenterZ ?? botPosObj.z,
      };

      logStructured('info', 'findFlatArea', 'start', {
        radius: r, minArea, botPos: botPosObj, scanCenter,
      });

      // ── Height map ─────────────────────────────────────────────────────────
      // Sprint 18 fix: bot.blockAt() now calls pos.floored() internally in current
      // Mineflayer. Plain {x,y,z} objects fail with "pos.floored is not a function".
      // Use toVec3() which provides a compatible .floored() pass-through.

      /** @type {Map<string, {x:number, z:number, y:number}>} */
      const heightMap = new Map();

      // Sprint 37: direct ground check — seeds the height map with the block below
      // the bot's current position, ensuring at least one entry even if chunks
      // aren't fully loaded yet. Prevents false "area=0" on flat ground.
      const groundCheckPos = toVec3(botPosObj.x, botPosObj.y - 1, botPosObj.z);
      const groundBlock = bot.blockAt(groundCheckPos);
      if (groundBlock && groundBlock.boundingBox === 'block' && !LIQUID_BLOCK_NAMES.has(groundBlock.name)) {
        const aboveGround = bot.blockAt(toVec3(botPosObj.x, botPosObj.y, botPosObj.z));
        if (!aboveGround || aboveGround.name === 'air' || aboveGround.boundingBox === 'empty') {
          heightMap.set(`${botPosObj.x},${botPosObj.z}`, {
            x: botPosObj.x, z: botPosObj.z, y: botPosObj.y,
          });
          console.log(
            `[findFlatArea] direct ground hit at (${botPosObj.x},${botPosObj.y},${botPosObj.z})` +
            ` block=${groundBlock.name}`
          );
        }
      }

      let columnIdx = 0;

      for (let dx = -r; dx <= r; dx++) {
        // Sprint 18: check stop flag every outer column strip
        if (_stopRequested) {
          console.log('[findFlatArea] aborted by stop signal');
          return;
        }

        for (let dz = -r; dz <= r; dz++) {
          if (++columnIdx % 200 === 0) {
            await new Promise(resolve => setImmediate(resolve));
          }

          const cx = scanCenter.x + dx;
          const cz = scanCenter.z + dz;

          for (let cy = scanCenter.y + yAbove; cy >= scanCenter.y - yBelow; cy--) {
            // Sprint 18: use toVec3 so bot.blockAt() gets .floored() method
            const block = bot.blockAt(toVec3(cx, cy, cz));
            if (!block) continue;

            if (block.name !== 'air'
                && block.boundingBox === 'block'
                && !LIQUID_BLOCK_NAMES.has(block.name)) {
              // Sprint 18: use toVec3 for the above-block check too
              const above = bot.blockAt(toVec3(cx, cy + 1, cz));
              if ((!above || above.name === 'air' || above.boundingBox === 'empty')
                  && !LIQUID_BLOCK_NAMES.has(above?.name ?? '')) {
                heightMap.set(`${cx},${cz}`, { x: cx, z: cz, y: cy + 1 });
                break;
              }
            }
          }
        }
      }

      // ── Flood-fill: find connected flat components ─────────────────────────

      /** @param {{y:number}|undefined} a @param {{y:number}|undefined} b */
      const isFlatNeighbour = (a, b) => a && b && Math.abs(a.y - b.y) <= 1;

      const visited      = new Set();
      let bestCandidate  = null;
      let bestScore      = 0;

      for (const [key, col] of heightMap) {
        if (visited.has(key)) continue;

        /** @type {Array<{x:number, z:number, y:number}>} */
        const component = [];
        const queue     = [col];
        visited.add(key);

        while (queue.length > 0) {
          const cur = queue.shift();
          component.push(cur);

          for (const [ndx, ndz] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
            const nk  = `${cur.x + ndx},${cur.z + ndz}`;
            const nbr = heightMap.get(nk);
            if (nbr && !visited.has(nk) && isFlatNeighbour(cur, nbr)) {
              visited.add(nk);
              queue.push(nbr);
            }
          }
        }

        if (component.length < minArea) continue;

        const yValues = component.map(c => c.y);
        const yMin    = Math.min(...yValues);
        const yMax    = Math.max(...yValues);
        const yRange  = yMax - yMin;

        if (yRange > maxSlope) continue;

        const minX  = Math.min(...component.map(c => c.x));
        const maxX  = Math.max(...component.map(c => c.x));
        const minZ  = Math.min(...component.map(c => c.z));
        const maxZ  = Math.max(...component.map(c => c.z));
        const bboxW = maxX - minX + 1;
        const bboxD = maxZ - minZ + 1;

        const compactness = component.length / (bboxW * bboxD);
        const flatness    = maxSlope > 0 ? 1 - yRange / maxSlope : 1;
        // Sprint 37: proximity penalty — closer to scan center is preferred.
        // This prevents the scanner from choosing a far-away tower top over
        // nearby ground-level flat patches.
        const compCenterX = (minX + maxX) / 2;
        const compCenterZ = (minZ + maxZ) / 2;
        const distFromCenter = Math.sqrt(
          (compCenterX - scanCenter.x) ** 2 +
          (compCenterZ - scanCenter.z) ** 2
        );
        const maxPossibleDist  = Math.sqrt(2) * r;
        const proximity        = Math.max(0, 1 - distFromCenter / maxPossibleDist);
        const score       = component.length * (
          FLAT_SCORE_WEIGHTS.area        +
          FLAT_SCORE_WEIGHTS.compactness * compactness +
          FLAT_SCORE_WEIGHTS.flatness    * flatness +
          FLAT_SCORE_WEIGHTS.proximity   * proximity
        );

        if (score > bestScore) {
          bestScore = score;
          const avgY = Math.round(yValues.reduce((s, y) => s + y, 0) / yValues.length);
          bestCandidate = {
            x: Math.round((minX + maxX) / 2),
            y: avgY,
            z: Math.round((minZ + maxZ) / 2),
            area: component.length,
            minX, maxX, minZ, maxZ,
            yRange,
            compactness: Math.round(compactness * 100) / 100,
          };
        }
      }

      if (bestCandidate) {
        sendEvent('flatAreaFound', { ...bestCandidate, searchedRadius: r, correlationId }); // Sprint 35 P0-C: include searchedRadius so C# DecomposeBuild can gate retry
        const scanElapsed = Date.now() - _scanStart;
        const distFromCenter = Math.sqrt(
          ((bestCandidate.minX + bestCandidate.maxX) / 2 - scanCenter.x) ** 2 +
          ((bestCandidate.minZ + bestCandidate.maxZ) / 2 - scanCenter.z) ** 2
        );
        console.log(
          `[findFlatArea] best at (${bestCandidate.x},${bestCandidate.y},${bestCandidate.z})` +
          ` area=${bestCandidate.area} yRange=${bestCandidate.yRange}` +
          ` compact=${bestCandidate.compactness} score=${bestScore.toFixed(1)}` +
          ` distFromCenter=${distFromCenter.toFixed(1)} (${scanElapsed}ms)`
        );
        logStructured('info', 'findFlatArea', 'found', {
          ...bestCandidate, score: +bestScore.toFixed(1), columns: columnIdx, elapsedMs: scanElapsed,
        });
      } else {
        const heightMapSize = heightMap.size;
        // Sprint 36: when heightMap is empty, the world may not be fully loaded.
        // Log diagnostics: bot position, loaded chunk count, and a sample blockAt
        // at the bot's feet to distinguish "chunks not loaded" from "no flat terrain".
        const feetBlock = bot.blockAt(toVec3(botPosObj.x, botPosObj.y - 1, botPosObj.z));
        const feetBlockName = feetBlock?.name ?? 'null';
        const loadedChunkCount = [...Array(chunkRadius * 2 + 1).keys()].reduce((count, dx) => {
          const cx = Math.floor(botPosObj.x / 16) + dx - chunkRadius;
          return count + [...Array(chunkRadius * 2 + 1).keys()].filter(dz => {
            const cz = Math.floor(botPosObj.z / 16) + dz - chunkRadius;
            return !!bot.world.getColumn(cx, cz);
          }).length;
        }, 0);
        console.warn(
          `[findFlatArea] no qualifying flat area found ` +
          `(min=${minArea}, maxSlope=${maxSlope}, radius=${r}, ` +
          `columns=${columnIdx}, heightMap=${heightMapSize})` +
          ` botPos=(${botPosObj.x},${botPosObj.y},${botPosObj.z})` +
          ` feetBlock=${feetBlockName} loadedChunks=${loadedChunkCount}`
        );
        logStructured('warn', 'findFlatArea', 'no qualifying area', {
          minArea, maxSlope, radius: r, columns: columnIdx,
          heightMapSize, elapsedMs: Date.now() - _scanStart,
          botPos: botPosObj, feetBlock: feetBlockName, loadedChunks: loadedChunkCount,
        });
        // Sprint 19: include searchedRadius so C# can distinguish "searched small area"
        // from "searched large area". DecomposeBuild uses this to expand radius on retry.
        sendEvent('flatAreaFound', {
          x: botPosObj.x, y: botPosObj.y + 1, z: botPosObj.z,
          area: 0, minX: botPosObj.x, maxX: botPosObj.x, minZ: botPosObj.z, maxZ: botPosObj.z,
          yRange: 0, compactness: 0, searchedRadius: r, correlationId,
        });
      }
      break;
    }

    case 'status':
      sendBotStatus();
      break;

    case 'chat':
      try {
        bot.chat(args.message ?? '');
      } catch (e) {
        console.error(`[chat] failed to send message: ${e.message}`);
        sendEvent('error', { action: 'chat', message: e.message, correlationId });
      }
      break;

    case 'craft': {
      const { item: itemName, count = 1, tableSearchRadius = CRAFT_TABLE_SEARCH_RADIUS } = args;
      if (!itemName) throw new Error('craft requires item');

      const itemEntry = bot.registry.itemsByName[itemName];
      if (!itemEntry) throw new Error(`Unknown item: ${itemName}`);

      const recipes = bot.recipesFor(itemEntry.id, null, null, null);
      if (!recipes || recipes.length === 0)
        throw new Error(`No recipe found for: ${itemName}`);

      const recipe = recipes[0];
      let craftingTable = null;

      if (recipe.requiresTable) {
        const tableId = bot.registry.blocksByName['crafting_table']?.id;
        if (tableId == null) throw new Error('crafting_table not found in registry');

        craftingTable = bot.findBlock({ matching: tableId, maxDistance: tableSearchRadius });
        if (!craftingTable)
          throw new Error(`No crafting_table within ${tableSearchRadius} blocks`);

        const movements = new Movements(bot);
        bot.pathfinder.setMovements(movements);
        await bot.pathfinder.goto(new pfGoals.GoalNear(
          craftingTable.position.x,
          craftingTable.position.y,
          craftingTable.position.z,
          CRAFT_TABLE_REACH_DISTANCE
        ));

        craftingTable = bot.blockAt(craftingTable.position);
        if (!craftingTable || craftingTable.type !== tableId)
          throw new Error('Crafting table not found after navigation');
      }

      await bot.craft(recipe, count, craftingTable);
      sendEvent('craftComplete', { item: itemName, count, correlationId });
      console.log(`[craft] crafted ${count}x ${itemName}`);
      sendBotStatus(); // Sprint 35 P0-A: refresh inventory after crafting (no craftedItem event yet)
      break;
    }

    case 'smelt': {
      const { item: inputName, count = 1, fuel = 'coal' } = args;
      if (!inputName) throw new Error('smelt requires item');

      const furnaceId = bot.registry.blocksByName['furnace']?.id;
      if (furnaceId == null) throw new Error('furnace not found in registry');

      let furnaceBlock = bot.findBlock({ matching: furnaceId, maxDistance: FURNACE_SEARCH_RADIUS });
      if (!furnaceBlock) throw new Error(`No furnace found within ${FURNACE_SEARCH_RADIUS} blocks`);

      const movements = new Movements(bot);
      bot.pathfinder.setMovements(movements);
      await bot.pathfinder.goto(new pfGoals.GoalNear(
        furnaceBlock.position.x, furnaceBlock.position.y, furnaceBlock.position.z,
        FURNACE_REACH_DISTANCE
      ));

      furnaceBlock = bot.blockAt(furnaceBlock.position);
      if (!furnaceBlock || furnaceBlock.type !== furnaceId)
        throw new Error('Furnace not found after navigation');

      const furnace = await bot.openFurnace(furnaceBlock);

      try {
        if (!furnace.fuelItem()) {
          const fuelItem = bot.inventory.items().find(i => i.name === fuel);
          if (fuelItem) await furnace.putFuel(fuelItem.type, null, Math.min(fuelItem.count, 8));
        }

        const inputItem = bot.inventory.items().find(i => i.name === inputName);
        if (!inputItem) throw new Error(`${inputName} not found in inventory`);

        const toSmelt = Math.min(inputItem.count, count);
        await furnace.putInput(inputItem.type, null, toSmelt);

        const outputName = await new Promise((resolve, reject) => {
          const timeout = setTimeout(
            () => reject(new Error(`Smelting timed out after ${SMELT_TIMEOUT_MS}ms`)),
            SMELT_TIMEOUT_MS
          );
          const check = () => {
            const out = furnace.outputItem();
            if (out) { clearTimeout(timeout); resolve(out.name); }
          };
          furnace.on('update', check);
          check();
        });

        if (furnace.outputItem()) await furnace.takeOutput();
        sendEvent('smeltComplete', { item: inputName, result: outputName, count: toSmelt, correlationId });
        console.log(`[smelt] smelted ${toSmelt}x ${inputName} → ${outputName}`);
        sendBotStatus(); // Sprint 35 P0-A: refresh inventory after smelting
      } finally {
        furnace.close();
      }
      break;
    }

    case 'findReachableBlock': {
      // Sprint 40 P0-B: Find the nearest DirtBlock that the bot can pathfind to.
      // Returns position + reachability info so the planner can make informed decisions.
      const { block: blockName, maxDistance = MINE_SEARCH_RADIUS_NEAR } = args;
      if (!blockName) throw new Error('findReachableBlock requires block name');

      const shortName  = blockName.replace('minecraft:', '');
      const blockEntry = bot.registry.blocksByName[shortName];
      if (!blockEntry) throw new Error(`Unknown block: ${blockName}`);
      const blockId = blockEntry.id;

      const botPosObj = botPos();
      const movements = new Movements(bot);
      bot.pathfinder.setMovements(movements);

      // Find all matching blocks within range
      const candidates = bot.findBlocks({
        matching: blockId,
        maxDistance: Math.min(maxDistance, MINE_SEARCH_RADIUS_FAR),
        count: REACHABLE_BLOCK_MAX_CANDIDATES,
      });

      if (!candidates || candidates.length === 0) {
        sendEvent('blockNotFound', { block: blockName, mined: 0, correlationId });
        throw new Error(`No ${shortName} found within ${maxDistance} blocks`);
      }

      // Sort by Euclidean distance from bot
      candidates.sort((a, b) => {
        const da = Math.sqrt(
          (a.x - botPosObj.x) ** 2 + (a.y - botPosObj.y) ** 2 + (a.z - botPosObj.z) ** 2);
        const db = Math.sqrt(
          (b.x - botPosObj.x) ** 2 + (b.y - botPosObj.y) ** 2 + (b.z - botPosObj.z) ** 2);
        return da - db;
      });

      // Check each candidate for pathfinding reachability (up to the limit)
      let reachableBlock = null;
      let reachableDistance = Infinity;
      let reachablePathDist = Infinity;

      for (const candidate of candidates) {
        if (_stopRequested) break;

        try {
          const pathResult = await bot.pathfinder.getPathTo(
            movements,
            new pfGoals.GoalNear(candidate.x, candidate.y, candidate.z, REACHABLE_BLOCK_GOTO_TOLERANCE),
            { timeout: REACHABLE_BLOCK_PATH_TIMEOUT_MS }
          );

          if (pathResult && pathResult.status === 'success') {
            const euclideanDist = Math.sqrt(
              (candidate.x - botPosObj.x) ** 2 +
              (candidate.y - botPosObj.y) ** 2 +
              (candidate.z - botPosObj.z) ** 2);
            const pathDist = pathResult.path.length;

            if (pathDist < reachablePathDist) {
              reachableBlock = candidate;
              reachableDistance = euclideanDist;
              reachablePathDist = pathDist;
            }
          }
        } catch {
          // Pathfinding failed for this candidate — try next
          continue;
        }
      }

      if (reachableBlock) {
        sendEvent('reachableBlockFound', {
          block: shortName,
          x: reachableBlock.x,
          y: reachableBlock.y,
          z: reachableBlock.z,
          euclideanDistance: Math.round(reachableDistance * 100) / 100,
          pathDistance: reachablePathDist,
          correlationId,
        });
        logStructured('info', 'findReachableBlock', 'found', {
          block: shortName,
          x: reachableBlock.x,
          y: reachableBlock.y,
          z: reachableBlock.z,
          euclideanDistance: Math.round(reachableDistance * 100) / 100,
          pathDistance: reachablePathDist,
        });
      } else {
        sendEvent('blockNotFound', { block: blockName, mined: 0, reason: 'unreachable', correlationId });
        throw new Error(`${shortName} found but none reachable within ${maxDistance} blocks`);
      }
      break;
    }

    default:
      throw new Error(`Unknown action: ${action}`);
  }
  } finally {
    logStructured('info', 'dispatch', 'done', { action, elapsedMs: Date.now() - _dispatchStart });
  }
}

// ── Graceful shutdown ─────────────────────────────────────────────────────────

function shutdown() {
  console.log('[adapter] shutting down');
  bot.quit?.();
  wss.close();
  process.exit(0);
}
process.on('SIGINT',  shutdown);
process.on('SIGTERM', shutdown);
