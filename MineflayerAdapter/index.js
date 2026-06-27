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
import { emitGameModeEvent, normalizeGameMode } from './gameModeState.js';
import { toVec3 } from './vec3.js';
// Sprint 52 modularization (TSK-0166): extracted to separate modules.
import * as C from './config.js';
import { logStructured } from './logger.js';

// ── Environment / connection ──────────────────────────────────────────────────

const WS_PORT  = parseInt(process.env.WS_PORT   ?? '3000',  10);
const MC_HOST  = process.env.MC_HOST   ?? 'localhost';
const MC_PORT  = parseInt(process.env.MC_PORT   ?? '25565', 10);
const MC_USER  = process.env.MC_USERNAME ?? 'Leo';
const MC_VER   = process.env.MC_VERSION;
const WS_TOKEN = process.env.WS_TOKEN ?? null;

// ── Tunable constants ─────────────────────────────────────────────────────────
// Sprint 52 modularization: all constants live in ./config.js.
// Imported as `C` at the top of this file — reference as C.CONSTANT_NAME.
// See config.js for full documentation of each constant.

// ── Sprint 19: Structured file logging ────────────────────────────────────────
// Sprint 52 modularization: logStructured() lives in ./logger.js.
// See logger.js for the full implementation.

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

// ── Vec3 compatibility — see vec3.js for the full implementation ────────────
// toVec3(x,y,z) is imported from ./vec3.js. It creates a plain JS object
// implementing the complete Mineflayer/prismarine-vector Vec3 API (46 methods).
// Without it, bot.dig(block) crashes with "point.minus is not a function"
// because Mineflayer internally calls block.position.minus(otherVec).

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
      // Sprint 41: include action args (position, block, etc.) in the error event
      // so C# can log the exact context of the failure, not just the error message.
      const errorData = { action: msg.action, message: e.message };
      if (msg.arguments) {
        if (msg.arguments.x != null) errorData.x = msg.arguments.x;
        if (msg.arguments.y != null) errorData.y = msg.arguments.y;
        if (msg.arguments.z != null) errorData.z = msg.arguments.z;
        if (msg.arguments.block)   errorData.block = msg.arguments.block;
        if (msg.arguments.material) errorData.material = msg.arguments.material;
        if (msg.arguments.item)    errorData.item = msg.arguments.item;
      }
      console.error(`[dispatch] ${msg.action} failed at`, errorData);
      sendEvent('error', errorData);
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
  // Sprint 43 (P1-3): use Math.floor() instead of Math.round() so entity coordinates
  // map to the block the bot is actually standing ON (not the nearest block).
  // Entity (-231.4, 65.0, 151.2) → floor to (-232, 65, 151), which matches the
  // block the bot is standing above. round(-231.4) = -231 (off by 1).
  return { x: Math.floor(p.x), y: Math.floor(p.y), z: Math.floor(p.z) };
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
      // Sprint 51: early-exit when already at target. pathfinder.goto() with
      // GoalNear(1) handles this internally but still incurs goal setup cost.
      const { x: bx, y: by, z: bz } = botPos();
      const dist = Math.sqrt((x - bx) ** 2 + (y - by) ** 2 + (z - bz) ** 2);
      if (dist <= 1.0) {
        logStructured('debug', 'move', 'already at target', { x, y, z, botX: bx, botY: by, botZ: bz, dist });
        sendEvent('moveComplete', { ...botPos(), correlationId });
        break;
      }
      logStructured('info', 'move', 'navigating', { x, y, z, botX: bx, botY: by, botZ: bz, dist: dist.toFixed(1) });
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

      /**
       * Selects the best target block with Y-level preference and alias support.
       * Sprint 40 P0-C: Prefers blocks at the expected Y-level (botFeetY - 1) to avoid
       * off-by-one errors where the nearest block by Euclidean distance is at Y=62-63
       * instead of Y=64. Also accepts alias blocks that drop the same item (e.g.
       * grass_block for dirt, since grass drops dirt when mined without silk touch).
       *
       * Scoring constants are named at the top of this file for easy tuning:
       *   C.MINE_Y_PENALTY_WEIGHT, C.MINE_FIRST_PASS_COUNT, C.MINE_SECOND_PASS_COUNT
       *
       * @returns {{x:number, y:number, z:number, blockName:string}|null}
       *   The target position and the actual Minecraft block name at that position,
       *   or null if no acceptable block is within range.
       */
      function findBestBlock() {
        const expectedY = botPos().y - 1;
        const pos = botPos();

        // Build the set of acceptable block IDs (primary block + aliases).
        // Uses an array of IDs for findBlocks/findBlock matching — more widely
        // supported across Mineflayer versions than function matching.
        const aliasNames = C.BLOCK_MINING_ALIASES[shortName] ?? [shortName];
        const acceptableIds = aliasNames
          .map(n => bot.registry.blocksByName[n]?.id)
          .filter(id => id != null);

        if (acceptableIds.length === 0) {
          // No known IDs — fall back to the primary block only
          acceptableIds.push(blockId);
        }

        /**
         * Resolve a position to the actual Minecraft block name by checking which
         * alias name matches the block's type ID at that position.
         * Uses bot.registry.blocksByName (confirmed working) rather than the
         * nonexistent blocksById to avoid "Cannot read properties of undefined"
         * crashes in Mineflayer 4.x.
         */
        function actualBlockName(pos) {
          const block = bot.blockAt(pos);
          if (!block) return shortName;
          const match = aliasNames.find(n => bot.registry.blocksByName[n]?.id === block.type);
          return match ?? shortName;
        }

        // ── Helper: filter out positions that have exhausted dig retries ─────
        function excludeExhausted(candidates) {
          return candidates?.filter(c => !isDigExhausted(c)) ?? [];
        }

        // ── First pass: blocks at the expected Y-level ───────────────────────
        const sameLevelCandidates = bot.findBlocks({
          matching: [...acceptableIds],
          maxDistance: C.MINE_SEARCH_RADIUS_NEAR,
          count: C.MINE_FIRST_PASS_COUNT,
        });
        let sameLevel = excludeExhausted(sameLevelCandidates?.filter(c => c.y === expectedY));
        if (sameLevel.length > 0) {
          // Sort by XZ distance only (same Y-level, so no Y penalty)
          sameLevel.sort((a, b) => {
            const da = (a.x - pos.x) ** 2 + (a.z - pos.z) ** 2;
            const db = (b.x - pos.x) ** 2 + (b.z - pos.z) ** 2;
            return da - db;
          });
          const block = bot.blockAt(sameLevel[0]);
          if (block && acceptableIds.includes(block.type)) {
            return {
              x: sameLevel[0].x, y: sameLevel[0].y, z: sameLevel[0].z,
              blockName: actualBlockName(sameLevel[0]),
            };
          }
        }

        // ── Second pass: nearby Y-levels with scoring ────────────────────────
        const nearbyCandidates = bot.findBlocks({
          matching: [...acceptableIds],
          maxDistance: C.MINE_SEARCH_RADIUS_NEAR,
          count: C.MINE_SECOND_PASS_COUNT,
        });
        let nearby = excludeExhausted(nearbyCandidates);
        if (nearby.length > 0) {
          // Score: Euclidean (XZ) distance + Y-penalty for each level away from expectedY
          // Math.max(0, a.y - expectedY) penalizes only blocks BELOW the surface
          // (blocks above won't be dirt/grass, so treat them neutrally)
          nearby.sort((a, b) => {
            const scoreA = Math.sqrt(
              (a.x - pos.x) ** 2 + Math.max(0, a.y - expectedY) ** 2 + (a.z - pos.z) ** 2
            ) + Math.abs(a.y - expectedY) * C.MINE_Y_PENALTY_WEIGHT;
            const scoreB = Math.sqrt(
              (b.x - pos.x) ** 2 + Math.max(0, b.y - expectedY) ** 2 + (b.z - pos.z) ** 2
            ) + Math.abs(b.y - expectedY) * C.MINE_Y_PENALTY_WEIGHT;
            return scoreA - scoreB;
          });
          const best = nearby[0];
          const block = bot.blockAt(best);
          if (block && acceptableIds.includes(block.type)) {
            return {
              x: best.x, y: best.y, z: best.z,
              blockName: actualBlockName(best),
            };
          }
        }

        // ── Fallback: original findBlock behavior (any Y-level, nearest Euclidean) ──
        // Also excludes exhausted positions.
        const fallbackCandidates = bot.findBlocks({
          matching: [...acceptableIds],
          maxDistance: C.MINE_SEARCH_RADIUS_FAR,
          count: C.MINE_FIRST_PASS_COUNT,
        });
        const fallbackFiltered = excludeExhausted(fallbackCandidates);
        if (fallbackFiltered.length > 0) {
          fallbackFiltered.sort((a, b) => {
            const da = (a.x - pos.x) ** 2 + (a.z - pos.z) ** 2;
            const db = (b.x - pos.x) ** 2 + (b.z - pos.z) ** 2;
            return da - db;
          });
          const block = bot.blockAt(fallbackFiltered[0]);
          if (block && acceptableIds.includes(block.type)) {
            return {
              x: fallbackFiltered[0].x, y: fallbackFiltered[0].y, z: fallbackFiltered[0].z,
              blockName: actualBlockName(fallbackFiltered[0]),
            };
          }
        }

        // ── No valid (non-exhausted) blocks found ───────────────────────────
        // Returns null to trigger blockNotFound → mine loop exit.
        // Without this, returning an exhausted position causes an infinite loop:
        //   findBestBlock → skip exhausted → continue → findBestBlock (same pos) → ...
        return null;
      }

      // Sprint 40 P0-C (Fix): track dig failures per block position to prevent
      // infinite retry loops when bot.dig() encounters an unrecoverable error.
      // Key = "x,y,z", value = consecutive failure count.
      // Sprint 41 FIX: Defined BEFORE findBestBlock so it's accessible in the
      // closure. findBestBlock uses this to exclude positions that have already
      // exhausted C.MAX_DIG_FAILURES, preventing an infinite loop where the same
      // unbreakable block is selected → skipped → selected → skipped ...
      const digFailures = new Map();

      /**
       * Returns true if the given position has already exhausted its dig retries.
       * Used by findBestBlock to exclude positions that can't be mined.
       */
      function isDigExhausted(pos) {
        const key = `${pos.x},${pos.y},${pos.z}`;
        return (digFailures.get(key) ?? 0) >= C.MAX_DIG_FAILURES;
      }

      // Sprint 41 FIX: blockTargetPos declared OUTSIDE the while loop with
      // let (not const) so it's accessible in the mineComplete event below.
      // Previously it was const inside the while body — block-scoped to each
      // iteration and invisible outside the loop, causing "blockTargetPos is
      // not defined" ReferenceError after the loop completed.
      let blockTargetPos = null;

      while (mined < count) {
        // Sprint 18: check stop flag at start of each iteration
        if (_stopRequested) {
          console.log(`[mine] aborted by stop signal after ${mined}/${count} ${shortName}`);
          sendEvent('mineAborted', {
            block: shortName, mined, targetCount: count, correlationId,
          });
          return; // Exit dispatch — don't send a normal completion event
        }

        const targetPos = findBestBlock();
        if (!targetPos) {
          console.log(`[mine] no ${shortName} found (mined ${mined}/${count})`);
          logStructured('warn', 'mine', 'no blocks in range', {
            block: shortName, mined, targetCount: count,
            searchRadius: C.MINE_SEARCH_RADIUS_FAR, elapsedMs: Date.now() - _mineStart,
          });
          sendEvent('blockNotFound', { block: blockName, mined, correlationId });
          if (mined === 0) throw new Error(`No ${shortName} found within ${C.MINE_SEARCH_RADIUS_FAR} blocks`);
          break;
        }

        // Sprint 40 P0-C: capture block position and actual block name for logging
        blockTargetPos = {
          bx: targetPos.x,
          by: targetPos.y,
          bz: targetPos.z,
        };
        logStructured('debug', 'mine', 'target selected', {
          block: shortName, actualBlock: targetPos.blockName,
          ...blockTargetPos, botPos: botPos(), correlationId,
        });

        try {
          await bot.pathfinder.goto(
            new pfGoals.GoalNear(targetPos.x, targetPos.y, targetPos.z, 2)
          );
          pathFailures = 0;
        } catch (e) {
          if (_stopRequested) {
            console.log(`[mine] aborted during navigation after ${mined}/${count} ${shortName}`);
            sendEvent('mineAborted', {
              block: shortName, mined, targetCount: count, correlationId,
            });
            return;
          }
          pathFailures++;
          console.warn(`[mine] nav to ${shortName} failed (${pathFailures}/${C.MAX_MINE_PATH_FAILURES}): ${e.message}`);
          if (pathFailures >= C.MAX_MINE_PATH_FAILURES)
            throw new Error(`Pathfinding to ${shortName} failed ${C.MAX_MINE_PATH_FAILURES} times: ${e.message}`);
          await new Promise(r => setTimeout(r, 500));
          continue;
        }

        const fresh = bot.blockAt(toVec3(targetPos.x, targetPos.y, targetPos.z));
        // Sprint 40 P0-C (Fix): Check against all acceptable block IDs (primary + aliases),
        // not just the primary blockId. E.g. when mining "dirt", also accept "grass_block".
        const aliasNames = C.BLOCK_MINING_ALIASES[shortName] ?? [shortName];
        const acceptableIds = aliasNames
          .map(n => bot.registry.blocksByName[n]?.id)
          .filter(id => id != null);
        if (acceptableIds.length === 0) acceptableIds.push(blockId);
        if (!fresh || !acceptableIds.includes(fresh.type)) {
          await new Promise(r => setTimeout(r, 100));
          continue;
        }

        // Sprint 40 P0-C (Fix): skip this block if we've failed to dig it
        // too many times (prevents infinite retry loop).
        const digKey = `${targetPos.x},${targetPos.y},${targetPos.z}`;
        const prevDigFailures = digFailures.get(digKey) ?? 0;
        if (prevDigFailures >= C.MAX_DIG_FAILURES) {
          console.warn(`[mine] dig failed ${prevDigFailures}x at (${digKey}) — skipping block`);
          logStructured('warn', 'mine', 'dig retries exhausted, skipping block', {
            block: shortName, pos: digKey, failures: prevDigFailures, correlationId,
          });
          // Advance to next block — don't count this as mined
          await new Promise(r => setTimeout(r, 100));
          continue;
        }

        // Sprint 41: equip the best available tool before digging.
        // bot.bestHarvestTool(block) returns { item, time } or null if no tool helps.
        // If equip fails (e.g. tool is not in hotbar), dig bare-handed.
        try {
          const harvestTool = bot.bestHarvestTool(fresh);
          if (harvestTool) {
            try {
              await bot.equip(harvestTool.item, 'hand');
            } catch (equipErr) {
              logStructured('debug', 'mine', 'equip failed, digging bare-handed', {
                tool: harvestTool.item?.name ?? 'unknown',
                error: equipErr.message,
              });
            }
          }
        } catch (toolErr) {
          logStructured('debug', 'mine', 'bestHarvestTool error, digging bare-handed', {
            error: toolErr.message,
          });
        }

        try {
          await bot.dig(fresh);
          mined++;
          pathFailures = 0;
          // Reset dig failure count on success
          digFailures.delete(digKey);
          // Sprint 36: send delta count (1 per dig) instead of cumulative mined counter.
          // The C# ApplyBlockMined uses e.Count as an additive delta via AddInventoryItem.
          // Sending cumulative count caused inventory ballooning (e.g. count=1, then 2, then 3
          // was interpreted as +1, +2, +3 = 6 dirt instead of +1+1+1 = 3 dirt).
          // Sprint 40 P0-B: include block target position (blockPos) and bot position (pos).
          // Previously only botPos() was sent, making it impossible to know WHICH block was mined.
          // Sprint 40 P0-C (Fix): Report the ACTUAL mined block name (e.g. "grass_block") so
          // the C# ApplyBlockMined can map it to the correct drop ("dirt") via BlockToItemDrop.
          sendEvent('blockMined', {
            block: targetPos.blockName ?? shortName, count: 1,
            ...botPos(),                               // bot position (where the bot is standing)
            blockX: blockTargetPos.bx,                 // block position (where the block was)
            blockY: blockTargetPos.by,
            blockZ: blockTargetPos.bz,
            correlationId,
          });

          // Sprint 40 P0-B: Ensure the dropped item is collected.
          // After digging, the item entity appears at the block position. Wait briefly
          // for auto-pickup (bot within ~1 block range). If not collected, move to the
          // block position to force pickup. This prevents items falling through holes
          // or landing out of reach from being lost forever.
          await new Promise(r => setTimeout(r, C.MINE_ITEM_PICKUP_WAIT_MS));
          // Check if the item at this block position is still an entity (not collected)
          // by looking for a dropped item entity near the dig position.
          const nearbyEntity = bot.nearestEntity(e => {
            if (!e || e.type !== 'object' || e.objectType !== 'Item') return false;
            const dx = e.position.x - targetPos.x;
            const dy = e.position.y - targetPos.y;
            const dz = e.position.z - targetPos.z;
            return Math.abs(dx) <= 2 && Math.abs(dy) <= 2 && Math.abs(dz) <= 2;
          });
          if (nearbyEntity) {
            // Item entity still present — move closer to collect it
            try {
              await bot.pathfinder.goto(
                new pfGoals.GoalNear(
                  targetPos.x, targetPos.y, targetPos.z, 1)
              );
              // Wait a bit more for collection
              await new Promise(r => setTimeout(r, C.MINE_ITEM_PICKUP_MOVE_WAIT_MS));
            } catch {
              // Movement failed — item may still be picked up if bot is close enough
              await new Promise(r => setTimeout(r, C.MINE_ITEM_PICKUP_REMOVE_BLOCK_MS));
            }
          }
        } catch (e) {
          if (_stopRequested) {
            console.log(`[mine] aborted after dig error`);
            sendEvent('mineAborted', {
              block: shortName, mined, targetCount: count, correlationId,
            });
            return;
          }
          // Sprint 40 P0-C (Fix): track dig failures per position so we don't
          // retry the same unbreakable block forever.
          digFailures.set(digKey, (digFailures.get(digKey) ?? 0) + 1);
          console.warn(`[mine] dig failed (${digFailures.get(digKey)}/${C.MAX_DIG_FAILURES}): ${e.message}`);
          logStructured('warn', 'mine', 'dig failed', {
            block: shortName, pos: digKey, failures: digFailures.get(digKey),
            error: e.message, correlationId,
          });
          await new Promise(r => setTimeout(r, 500));
        }
      }
      logStructured('info', 'mine', 'complete', {
        block: shortName, mined, targetCount: count, elapsedMs: Date.now() - _mineStart,
      });
      // Sprint 35 P0-B: emit mineComplete at the end of the mining loop — definitive signal.
      // Consumed by AgentBackgroundService to transition correlated action to Completed state.
      // Sprint 40 P0-B: include block position from the LAST mined block.
      sendEvent('mineComplete', {
        block: shortName, mined, targetCount: count,
        blockX: blockTargetPos.bx,
        blockY: blockTargetPos.by,
        blockZ: blockTargetPos.bz,
        correlationId,
      });
      break;
    }

    case 'place': {
      const { x, y, z, material } = args;
      if (x == null || !material) throw new Error('place requires x, y, z, material');

      logStructured('info', 'place', 'start', { material, x, y, z, pos: botPos() });

      // Sprint 42 (TSK-0074): Check if target position already has the correct block.
      // If so, skip placement entirely — the block is already in the desired state.
      // If occupied by a different block, MINE IT first, then place the correct one.
      const targetPos = toVec3(x, y, z);
      const targetBlock = bot.blockAt(targetPos);
      const shortMat = material.replace('minecraft:', '');
      if (targetBlock && targetBlock.type !== 0) {
        const targetBlockName = targetBlock.name;
        if (targetBlockName === shortMat) {
          logStructured('info', 'place', 'already placed', { material: shortMat, x, y, z });
          sendEvent('blockPlaced', { x, y, z, block: shortMat, correlationId });
          break;
        }
        // Sprint 52: wrong block in the way — mine it, then place the correct one.
        // Thread carefully to avoid destroying blocks that shouldn't be touched:
        // - Skip unbreakable blocks (bedrock, barrier, command blocks, etc.)
        // - Log clearly so incorrect mines are traceable
        const UNBREAKABLE = new Set([
          'bedrock', 'barrier', 'command_block', 'chain_command_block',
          'repeating_command_block', 'structure_block', 'structure_void',
          'end_portal_frame', 'end_portal', 'nether_portal',
        ]);
        if (UNBREAKABLE.has(targetBlockName)) {
          logStructured('warn', 'place', 'terrain collision — unbreakable block, skipping', {
            material: shortMat, x, y, z, existingBlock: targetBlockName,
          });
          sendEvent('blockPlaceSkipped', { x, y, z, block: shortMat, existingBlock: targetBlockName, correlationId, reason: 'terrainOccupied' });
          break;
        }
        logStructured('info', 'place', 'mining wrong block to replace', {
          existingBlock: targetBlockName, desiredBlock: shortMat, x, y, z,
        });
        try {
          await bot.pathfinder.goto(new pfGoals.GoalNear(x, y, z, 2));
          // Equip best tool for the block (creative mode doesn't care, survival does)
          const bestTool = bot.pathfinder.bestHarvestTool(targetBlock);
          if (bestTool) await bot.equip(bestTool, 'hand');
          await bot.dig(targetBlock);
          logStructured('info', 'place', 'mined wrong block, continuing to place correct one', {
            existingBlock: targetBlockName, desiredBlock: shortMat,
          });
          // Target is now clear — fall through to normal placement flow below.
        } catch (e) {
          logStructured('error', 'place', 'failed to mine wrong block', {
            existingBlock: targetBlockName, desiredBlock: shortMat, error: e.message,
          });
          sendEvent('blockPlaceSkipped', { x, y, z, block: shortMat, existingBlock: targetBlockName, correlationId, reason: 'terrainOccupied' });
          break;
        }
      }

      // Sprint 52 (TSK-0123): When the bot is effectively standing at the target
      // position, step aside instead of permanently skipping. Use distance check
      // (< 0.6 blocks) rather than exact coordinate match — floating-point bot
      // position rarely equals the integer target exactly.
      const botPos2 = botPos();
      const dx = Math.abs(x - botPos2.x);
      const dy = Math.abs(y - botPos2.y);
      const dz = Math.abs(z - botPos2.z);
      if (dx < 0.6 && dy < 0.6 && dz < 0.6) {
        // Try cardinal offsets perpendicular to the build direction first
        // (Z axis), then fall back to X axis. This avoids stepping into the
        // line of the next block, which is typically along X.
        const offsets = [
          { dx: 0, dz: 2,  label: 'z+2' },
          { dx: 0, dz: -2, label: 'z-2' },
          { dx: -2, dz: 0, label: 'x-2' },
          { dx: 2,  dz: 0, label: 'x+2' },
        ];
        let steppedAside = false;
        for (const { dx: ox, dz: oz, label } of offsets) {
          const sx = x + ox;
          const sz = z + oz;
          const groundBelow = bot.blockAt(toVec3(sx, y - 1, sz));
          if (groundBelow && groundBelow.type !== 0 && groundBelow.name !== 'air') {
            logStructured('info', 'place', 'stepping aside from target to place', {
              x, y, z, botX: botPos2.x, botY: botPos2.y, botZ: botPos2.z,
              dx, dy, dz, stepTo: { x: sx, z: sz }, direction: label, material: shortMat,
              groundBlock: groundBelow.name,
              reason: 'bot too close to target — moving perpendicular to build direction',
            });
            await bot.pathfinder.goto(new pfGoals.GoalNear(sx, y, sz, 1));
            steppedAside = true;
            break;
          }
        }
        if (!steppedAside) {
          // All preferred offsets have no walkable ground — fall back to x+2
          logStructured('warn', 'place', 'stepping aside — no walkable ground, using fallback', {
            x, y, z, material: shortMat,
          });
          await bot.pathfinder.goto(new pfGoals.GoalNear(x + 2, y, z, 1));
        }
        // Fall through to normal placement below — GoalNear(x,y,z,2) will
        // position us correctly from the side.
      }

      const movements = new Movements(bot);
      bot.pathfinder.setMovements(movements);
      // Sprint 42 (TSK-0076): Reduce tolerance from 3 to 2 so the bot gets closer
      // to the target before placing. A 3-block tolerance meant the bot could be
      // outside the ~4.5 block interact range after pathfinding.
      await bot.pathfinder.goto(new pfGoals.GoalNear(x, y, z, 2));

      let item = bot.inventory.items().find(i => i.name === shortMat || i.name === material);
      // Sprint 52: creative inventory provisioned via reusable provider module.
      // Handles version-agnostic creative API (setInventorySlot) with /give fallback.
      if (!item && bot.game?.gameMode === 1) {
        const { ensureCreativeItem } = require('./creativeProvider');
        const ok = await ensureCreativeItem(bot, shortMat || material, 1);
        if (ok) {
          item = bot.inventory.items().find(i => i.name === shortMat || i.name === material);
        }
      }
      if (!item) throw new Error(`${material} not in inventory`);
      await bot.equip(item, 'hand');

      // Sprint 41: use target-relative reference block detection.
      // Check each of the 6 faces adjacent to the TARGET (not relative to bot position).
      // The old code computed refPos from botPos + offset + (target - botPos), which was
      // mathematically correct (simplifies to target + offset) but fragile when the bot's
      // entity.position changed between goto() and the blockAt() calls.
      const refFaces = [
        { rx: 0, ry: -1, rz: 0,  fx: 0,  fy: 1,  fz: 0  },  // ground below target
        { rx: 0, ry: 1,  rz: 0,  fx: 0,  fy: -1, fz: 0  },  // ceiling above target
        { rx: -1, ry: 0, rz: 0,  fx: 1,  fy: 0,  fz: 0  },  // west
        { rx: 1,  ry: 0, rz: 0,  fx: -1, fy: 0,  fz: 0  },  // east
        { rx: 0,  ry: 0, rz: -1, fx: 0,  fy: 0,  fz: 1  },  // north
        { rx: 0,  ry: 0, rz: 1,  fx: 0,  fy: 0,  fz: -1 },  // south
      ];

      let placed = false;
      let lastRefError = '';
      for (const { rx, ry, rz, fx, fy, fz } of refFaces) {
        const refPos = toVec3(x + rx, y + ry, z + rz);
        const ref = bot.blockAt(refPos);
        if (!ref || ref.type === 0) {
          logStructured('debug', 'place', 'ref face empty', {
            target: { x, y, z }, refPos: { x: refPos.x, y: refPos.y, z: refPos.z },
            face: { fx, fy, fz },
          });
          continue;
        }
        try {
          logStructured('debug', 'place', 'attempt ref face', {
            target: { x, y, z },
            refBlock: { name: ref.name, pos: { x: ref.position.x, y: ref.position.y, z: ref.position.z } },
            face: { fx, fy, fz },
            botPos: botPos(),
          });
          // Sprint 41: use toVec3 for the face vector to match the Vec3 type
          // contract.
          await bot.placeBlock(ref, toVec3(fx, fy, fz));
          placed = true;
          logStructured('info', 'place', 'success', {
            material: shortMat, x, y, z, refBlock: ref.name,
          });
          break;
        } catch (e) {
          lastRefError = e.message;
          logStructured('debug', 'place', 'ref face failed', {
            target: { x, y, z },
            refBlock: ref.name,
            face: { fx, fy, fz },
            error: e.message,
          });
        }
      }

      if (!placed) {
        // Sprint 52 (TSK-0123): scaffold fallback — when no adjacent reference
        // block exists (e.g. corner position, all 6 faces empty), place a
        // scaffold block below the target, then use it as reference.
        logStructured('warn', 'place', 'no reference block — attempting scaffold', {
          x, y, z, material: shortMat, lastRefError,
        });

        // Find solid ground below the target
        let groundY = y - 1;
        let groundBlock = bot.blockAt(toVec3(x, groundY, z));
        while (groundY > -64 && (!groundBlock || groundBlock.type === 0 || groundBlock.name === shortMat)) {
          groundY--;
          groundBlock = bot.blockAt(toVec3(x, groundY, z));
        }

        if (groundBlock && groundBlock.type !== 0) {
          const scaffoldY = groundY + 1;
          // Navigate near the scaffold position so we can reach it
          await bot.pathfinder.goto(new pfGoals.GoalNear(x, scaffoldY, z, 2));
          // Ensure we have the material equipped for the scaffold
          if (item) await bot.equip(item, 'hand');
          try {
            // Place scaffold on top of ground, clicking the ground's top face
            await bot.placeBlock(groundBlock, toVec3(0, 1, 0));
            logStructured('info', 'place', 'scaffold placed', {
              material: shortMat, scaffoldX: x, scaffoldY, scaffoldZ: z,
              groundY, groundBlock: groundBlock.name,
            });

            // Now place the target block against the scaffold's top face
            const scaffoldBlock = bot.blockAt(toVec3(x, scaffoldY, z));
            if (scaffoldBlock && scaffoldBlock.type !== 0) {
              await bot.equip(item, 'hand');
              await bot.placeBlock(scaffoldBlock, toVec3(0, 1, 0));
              placed = true;
              logStructured('info', 'place', 'success via scaffold', {
                material: shortMat, x, y, z, scaffoldY,
              });
            }
          } catch (e) {
            lastRefError = e.message;
            logStructured('error', 'place', 'scaffold failed', {
              x, y, z, material: shortMat, error: e.message,
            });
          }
        } else {
          logStructured('error', 'place', 'scaffold impossible — no ground below', {
            x, y, z, material: shortMat,
          });
        }
      }

      if (!placed) {
        throw new Error(
          `Cannot place ${material} at (${x},${y},${z}) — no solid reference block` +
          (lastRefError ? ` (last face error: ${lastRefError})` : ''));
      }
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
        radius      = C.FLAT_AREA_SCAN_RADIUS,
        minFlatArea = C.FLAT_AREA_MIN_SIZE,
        yAbove      = C.FLAT_AREA_Y_ABOVE,
        yBelow      = C.FLAT_AREA_Y_BELOW,
        maxSlope    = C.FLAT_AREA_MAX_SLOPE,
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
              if (chunkPosToCheck.size === 0 || _stopRequested) {
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
      if (groundBlock && groundBlock.boundingBox === 'block' && !C.LIQUID_BLOCK_NAMES.has(groundBlock.name)) {
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
                && !C.LIQUID_BLOCK_NAMES.has(block.name)) {
              // Sprint 18: use toVec3 for the above-block check too
              const above = bot.blockAt(toVec3(cx, cy + 1, cz));
              if ((!above || above.name === 'air' || above.boundingBox === 'empty')
                  && !C.LIQUID_BLOCK_NAMES.has(above?.name ?? '')) {
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
          C.FLAT_SCORE_WEIGHTS.area        +
          C.FLAT_SCORE_WEIGHTS.compactness * compactness +
          C.FLAT_SCORE_WEIGHTS.flatness    * flatness +
          C.FLAT_SCORE_WEIGHTS.proximity   * proximity
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
      // Sprint 40 P0-B: guard against bot.chat() being called before bot has spawned.
      // The startup announcement can arrive before bot.entity is initialized.
      if (!bot.entity) {
        console.warn('[chat] bot not spawned yet — retrying after 500ms');
        logStructured('warn', 'chat', 'deferred — bot not spawned', { message: args.message });
        await new Promise(r => setTimeout(r, 500));
        if (!bot.entity) {
          console.error('[chat] bot still not spawned after retry — dropping message');
          sendEvent('error', { action: 'chat', message: 'bot not spawned yet', correlationId });
          break;
        }
      }
      try {
        bot.chat(args.message ?? '');
      } catch (e) {
        console.error(`[chat] failed to send message: ${e.message}`);
        sendEvent('error', { action: 'chat', message: e.message, correlationId });
      }
      break;

    case 'craft': {
      const { item: itemName, count = 1, tableSearchRadius = C.CRAFT_TABLE_SEARCH_RADIUS } = args;
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
          C.CRAFT_TABLE_REACH_DISTANCE
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

      let furnaceBlock = bot.findBlock({ matching: furnaceId, maxDistance: C.FURNACE_SEARCH_RADIUS });
      if (!furnaceBlock) throw new Error(`No furnace found within ${C.FURNACE_SEARCH_RADIUS} blocks`);

      const movements = new Movements(bot);
      bot.pathfinder.setMovements(movements);
      await bot.pathfinder.goto(new pfGoals.GoalNear(
        furnaceBlock.position.x, furnaceBlock.position.y, furnaceBlock.position.z,
        C.FURNACE_REACH_DISTANCE
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
            () => reject(new Error(`Smelting timed out after ${C.SMELT_TIMEOUT_MS}ms`)),
            C.SMELT_TIMEOUT_MS
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
      const { block: blockName, maxDistance = C.MINE_SEARCH_RADIUS_NEAR } = args;
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
        maxDistance: Math.min(maxDistance, C.MINE_SEARCH_RADIUS_FAR),
        count: C.REACHABLE_BLOCK_MAX_CANDIDATES,
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
            new pfGoals.GoalNear(candidate.x, candidate.y, candidate.z, C.REACHABLE_BLOCK_GOTO_TOLERANCE),
            { timeout: C.REACHABLE_BLOCK_PATH_TIMEOUT_MS }
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
