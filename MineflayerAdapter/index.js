/**
 * MineflayerAdapter — Node.js bridge between the MemorySmith.Agent C# host
 * and a Minecraft server.
 *
 * Architecture:
 *   1. Starts a WebSocket server on WS_PORT (default: 3000).
 *   2. Creates a Mineflayer bot connecting to MC_HOST:MC_PORT.
 *   3. Forwards bot events (spawn, health, move, blockMined, chat…) to C# as JSON.
 *   4. Accepts JSON action commands from C# and executes them via Mineflayer.
 *
 * Phase 5 additions:
 *   - chat event: includes onlinePlayers count for directed-at-bot heuristics
 *   - craft action: uses bot.craft() to craft items from inventory
 *   - smelt action: uses bot.openFurnace() to smelt items in a nearby furnace
 *
 * Environment variables:
 *   WS_PORT      WebSocket server port for C# connection (default: 3000)
 *   MC_HOST      Minecraft server host (default: localhost)
 *   MC_PORT      Minecraft server port (default: 25565)
 *   MC_USERNAME  Bot username (default: AgentBot)
 *   MC_VERSION   Minecraft version e.g. "1.21.4" (optional; auto-detect if omitted)
 *   WS_TOKEN     Simple shared secret for auth (optional)
 *
 * Dependencies: mineflayer, mineflayer-pathfinder, ws
 * Run: node index.js
 */

import mineflayer from 'mineflayer';
import mflPathfinder from 'mineflayer-pathfinder';
const { pathfinder, Movements, goals: pfGoals } = mflPathfinder;
import { WebSocketServer } from 'ws';

const WS_PORT  = parseInt(process.env.WS_PORT   ?? '3000',  10);
const MC_HOST  = process.env.MC_HOST   ?? 'localhost';
const MC_PORT  = parseInt(process.env.MC_PORT   ?? '25565', 10);
const MC_USER  = process.env.MC_USERNAME ?? 'AgentBot';
const MC_VER   = process.env.MC_VERSION;
const WS_TOKEN = process.env.WS_TOKEN ?? null;

// ── WebSocket server ─────────────────────────────────────────────────────────

const wss = new WebSocketServer({ port: WS_PORT });
let agentSocket = null;
let spawnPos = null;       // recorded on first spawn for boundary checks

function sendEvent(event, data = {}) {
  if (agentSocket?.readyState === 1 /* OPEN */) {
    agentSocket.send(JSON.stringify({ event, ...data }));
  }
}

// ── Sequential command queue ─────────────────────────────────────────────────
// C# sends actions as fast as WebSocket allows. Without a queue, overlapping
// bot.dig() calls cause mineflayer to throw "Digging aborted" on every but
// the last command. This queue ensures actions run strictly one at a time.

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

wss.on('connection', (ws) => {
  console.log('[ws] C# agent connected');
  agentSocket = ws;

  ws.on('message', (raw) => {
    let msg;
    try { msg = JSON.parse(raw.toString()); }
    catch (e) { console.error('[ws] bad JSON:', e.message); return; }

    if (WS_TOKEN && msg.token !== WS_TOKEN) {
      ws.close(1008, 'Unauthorized');
      return;
    }

    enqueueCommand(msg);          // enqueue — do NOT await here
  });

  ws.on('close', () => {
    if (agentSocket === ws) agentSocket = null;
    console.log('[ws] C# agent disconnected');
  });

  ws.on('error', (e) => console.error('[ws] socket error:', e.message));

  if (bot?.entity) sendBotStatus();
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
  sendEvent('status', {
    ...botPos(),
    hp:   bot.health ?? 20,
    food: bot.food   ?? 20,
    inventory: invMap,
  });
}

// ── Bot event forwarding ──────────────────────────────────────────────────────

bot.once('spawn', () => {
  spawnPos = { x: bot.entity.position.x, y: bot.entity.position.y, z: bot.entity.position.z };
  console.log('[mc] bot spawned at', botPos());
  sendEvent('spawn', { ...botPos(), hp: bot.health, food: bot.food });
});

bot.on('health', () => sendEvent('health', { hp: bot.health, food: bot.food }));
bot.on('move',   () => sendEvent('move',   botPos()));
bot.on('death',  () => { console.warn('[mc] bot died'); sendEvent('death', botPos()); });
bot.on('kicked', (reason) => { console.warn('[mc] kicked:', reason); sendEvent('kicked', { reason }); });
bot.on('error',  (e)      => { console.error('[mc] error:', e.message); sendEvent('error', { message: e.message }); });

// Phase 5: emit chat events with onlinePlayers count so the C# ChatInterpreter
// can apply the directed-at-bot heuristic (onlinePlayers == 1 → always addressed).
bot.on('chat', (username, message) => {
  if (username === bot.username) return;
  const onlinePlayers = Object.keys(bot.players).filter(p => p !== bot.username).length;
  sendEvent('chat', { username, message, onlinePlayers });
});

// ── Action dispatcher ─────────────────────────────────────────────────────────

async function dispatch({ action, arguments: args = {} }) {
  switch (action) {

    case 'move': {
      const { x, y, z } = args;
      if (x == null || y == null || z == null)
        throw new Error('move requires x, y, z');
      const movements = new Movements(bot);
      bot.pathfinder.setMovements(movements);
      await bot.pathfinder.goto(new pfGoals.GoalNear(x, y, z, 1));
      sendEvent('moveComplete', botPos());
      break;
    }

    case 'mine': {
      // Navigate to each target block before digging.
      // Catches pathfinder timeouts per-block so one unreachable log
      // doesn't kill the entire mine operation.
      const { block: blockName, count = 1 } = args;
      if (!blockName) throw new Error('mine requires block name');

      const shortName = blockName.replace('minecraft:', '');
      const blockEntry = bot.registry.blocksByName[shortName];
      if (!blockEntry) throw new Error(`Unknown block: ${blockName}`);
      const blockId = blockEntry.id;

      const movements = new Movements(bot);
      bot.pathfinder.setMovements(movements);

      let mined = 0;
      let pathFailures = 0;
      const MAX_PATH_FAILURES = 3;

      while (mined < count) {
        // Search up to 64 blocks first, widen to 128 if empty
        let target = bot.findBlock({ matching: blockId, maxDistance: 64 });
        if (!target) target = bot.findBlock({ matching: blockId, maxDistance: 128 });

        if (!target) {
          console.log(`[mine] no ${shortName} found within 128 blocks after ${mined} mined`);
          sendEvent('blockNotFound', { block: blockName, mined });
          if (mined === 0) throw new Error(`No ${shortName} found within 128 blocks`);
          break;
        }

        try {
          await bot.pathfinder.goto(
            new pfGoals.GoalNear(target.position.x, target.position.y, target.position.z, 2)
          );
          pathFailures = 0;
        } catch (e) {
          pathFailures++;
          console.warn(`[mine] nav to ${shortName} failed (${pathFailures}/${MAX_PATH_FAILURES}): ${e.message}`);
          if (pathFailures >= MAX_PATH_FAILURES)
            throw new Error(`Pathfinding to ${shortName} failed ${MAX_PATH_FAILURES} times: ${e.message}`);
          await new Promise(r => setTimeout(r, 500));
          continue;
        }

        // Re-fetch — another player may have mined it during navigation
        const fresh = bot.blockAt(target.position);
        if (!fresh || fresh.type !== blockId) {
          await new Promise(r => setTimeout(r, 100));
          continue;
        }

        try {
          await bot.dig(fresh);
          mined++;
          pathFailures = 0;
          sendEvent('blockMined', { block: shortName, count: mined, ...botPos() });
        } catch (e) {
          console.warn(`[mine] dig failed: ${e.message}`);
          await new Promise(r => setTimeout(r, 500));
        }
      }
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
        const ref = bot.blockAt(bot.entity.position.offset(dx + (x - Math.round(bot.entity.position.x)),
                                                           dy + (y - Math.round(bot.entity.position.y)),
                                                           dz + (z - Math.round(bot.entity.position.z))));
        if (!ref || ref.type === 0) continue;
        try {
          await bot.placeBlock(ref, { x: fx, y: fy, z: fz });
          placed = true;
          break;
        } catch { /* try next face */ }
      }

      if (!placed) throw new Error(`Cannot place ${material} at (${x},${y},${z}) — no solid reference block found`);
      sendEvent('blockPlaced', { x, y, z, block: shortMat });
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

      const movements = new Movements(bot);
      bot.pathfinder.setMovements(movements);
      try {
        await bot.pathfinder.goto(
          new pfGoals.GoalNear(Math.round(tX), Math.round(bot.entity.position.y), Math.round(tZ), 2)
        );
        sendEvent('wanderComplete', { ...botPos(), targetX: Math.round(tX), targetZ: Math.round(tZ) });
      } catch (e) {
        console.warn(`[wander] pathfinding failed: ${e.message}`);
        sendEvent('wanderFailed', { message: e.message, ...botPos() });
      }
      break;
    }

    case 'status':
      sendBotStatus();
      break;

    case 'chat':
      bot.chat(args.message ?? '');
      break;

    // ── Phase 5: Craft ────────────────────────────────────────────────────────
    // Craft an item from materials already in inventory.
    // Finds the first available recipe; for 3×3 recipes requires a crafting_table
    // within 4 blocks.
    case 'craft': {
      const { item: itemName, count = 1 } = args;
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
        craftingTable = bot.findBlock({ matching: tableId, maxDistance: 4 });
        if (!craftingTable) throw new Error('No crafting_table within 4 blocks');
      }

      await bot.craft(recipe, count, craftingTable);
      sendEvent('craftComplete', { item: itemName, count });
      console.log(`[craft] crafted ${count}x ${itemName}`);
      break;
    }

    // ── Phase 5: Smelt ────────────────────────────────────────────────────────
    // Smelt an item in the nearest furnace (within 16 blocks).
    // Adds fuel if the furnace slot is empty, places input, waits for output.
    case 'smelt': {
      const { item: inputName, count = 1, fuel = 'coal' } = args;
      if (!inputName) throw new Error('smelt requires item');

      const furnaceId = bot.registry.blocksByName['furnace']?.id;
      if (furnaceId == null) throw new Error('furnace not found in registry');

      let furnaceBlock = bot.findBlock({ matching: furnaceId, maxDistance: 16 });
      if (!furnaceBlock) throw new Error('No furnace found within 16 blocks');

      // Navigate to furnace
      const movements = new Movements(bot);
      bot.pathfinder.setMovements(movements);
      await bot.pathfinder.goto(new pfGoals.GoalNear(
        furnaceBlock.position.x, furnaceBlock.position.y, furnaceBlock.position.z, 2
      ));

      // Re-fetch after navigation
      furnaceBlock = bot.blockAt(furnaceBlock.position);
      if (!furnaceBlock || furnaceBlock.type !== furnaceId)
        throw new Error('Furnace not found at expected position after navigation');

      const furnace = await bot.openFurnace(furnaceBlock);

      try {
        // Add fuel if empty
        if (!furnace.fuelItem()) {
          const fuelItem = bot.inventory.items().find(i => i.name === fuel);
          if (fuelItem) {
            await furnace.putFuel(fuelItem.type, null, Math.min(fuelItem.count, 8));
          } else {
            console.warn(`[smelt] no ${fuel} for fuel — furnace may already have fuel`);
          }
        }

        // Check input item
        const inputItem = bot.inventory.items().find(i => i.name === inputName);
        if (!inputItem) throw new Error(`${inputName} not found in inventory`);

        const toSmelt = Math.min(inputItem.count, count);
        await furnace.putInput(inputItem.type, null, toSmelt);

        // Wait for output (up to 40 seconds — one smelt cycle is ~10s)
        const outputName = await new Promise((resolve, reject) => {
          const timeout = setTimeout(() => reject(new Error('Smelting timed out after 40s')), 40_000);
          const check = () => {
            const out = furnace.outputItem();
            if (out) { clearTimeout(timeout); resolve(out.name); }
          };
          furnace.on('update', check);
          check(); // check immediately in case it's already done
        });

        if (furnace.outputItem()) await furnace.takeOutput();

        sendEvent('smeltComplete', {
          item:   inputName,
          result: outputName,
          count:  toSmelt,
        });
        console.log(`[smelt] smelted ${toSmelt}x ${inputName} → ${outputName}`);
      } finally {
        furnace.close();
      }
      break;
    }

    default:
      throw new Error(`Unknown action: ${action}`);
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
