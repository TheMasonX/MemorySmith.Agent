/**
 * MineflayerAdapter — Node.js bridge between the MemorySmith.Agent C# host
 * and a Minecraft server.
 *
 * Sprint 2a: craft case now pathfinds to the nearest crafting table before
 *   calling bot.craft() for recipes that require one. Search radius expanded
 *   from 4 to CRAFT_TABLE_SEARCH_RADIUS (8) blocks. Radius is also overridable
 *   per-call via args.tableSearchRadius so C# can pass a custom value.
 *
 * Phase 5b additions:
 *   - chat event: includes playerX/Y/Z (bot.players[username].entity.position)
 *     so C# can compute bot-to-player distance for the "closest agent" heuristic.
 */

import mineflayer from 'mineflayer';
import mflPathfinder from 'mineflayer-pathfinder';
const { pathfinder, Movements, goals: pfGoals } = mflPathfinder;
import { WebSocketServer } from 'ws';

// ── Environment / connection ──────────────────────────────────────────────────

const WS_PORT  = parseInt(process.env.WS_PORT   ?? '3000',  10);
const MC_HOST  = process.env.MC_HOST   ?? 'localhost';
const MC_PORT  = parseInt(process.env.MC_PORT   ?? '25565', 10);
const MC_USER  = process.env.MC_USERNAME ?? 'AgentBot';
const MC_VER   = process.env.MC_VERSION;
const WS_TOKEN = process.env.WS_TOKEN ?? null;

// ── Tunable constants ─────────────────────────────────────────────────────────
// All search radii, distances, and retry counts are named here.
// Override per-call via args where supported.

const MINE_SEARCH_RADIUS_NEAR    = 64;   // blocks — first findBlock pass
const MINE_SEARCH_RADIUS_FAR     = 128;  // blocks — second findBlock pass
const MAX_MINE_PATH_FAILURES     = 3;    // consecutive pathfinder failures before abort
const CRAFT_TABLE_SEARCH_RADIUS  = 8;    // blocks — findBlock for crafting_table (Sprint 2a)
const CRAFT_TABLE_REACH_DISTANCE = 2;    // blocks — GoalNear tolerance when pathfinding to table
const FURNACE_SEARCH_RADIUS      = 16;   // blocks — findBlock for furnace
const FURNACE_REACH_DISTANCE     = 2;    // blocks — GoalNear tolerance when pathfinding to furnace
const SMELT_TIMEOUT_MS           = 40_000; // ms — max wait for smelting output

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

    enqueueCommand(msg);
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

bot.on('chat', (username, message) => {
  if (username === bot.username) return;
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

      while (mined < count) {
        let target = bot.findBlock({ matching: blockId, maxDistance: MINE_SEARCH_RADIUS_NEAR });
        if (!target) target = bot.findBlock({ matching: blockId, maxDistance: MINE_SEARCH_RADIUS_FAR });

        if (!target) {
          console.log(`[mine] no ${shortName} found within ${MINE_SEARCH_RADIUS_FAR} blocks after ${mined} mined`);
          sendEvent('blockNotFound', { block: blockName, mined });
          if (mined === 0) throw new Error(`No ${shortName} found within ${MINE_SEARCH_RADIUS_FAR} blocks`);
          break;
        }

        try {
          await bot.pathfinder.goto(
            new pfGoals.GoalNear(target.position.x, target.position.y, target.position.z, 2)
          );
          pathFailures = 0;
        } catch (e) {
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

    case 'findFlatArea': {
      const { radius = 20, minFlatArea = 9 } = args;
      const botPosObj = botPos();
      const r = Math.max(1, Math.min(radius, 64));
      const minArea = Math.max(1, Math.min(minFlatArea, 256));

      // Build a height map: sample every column within radius, recording the
      // Y level of the highest solid (opaque) block that has air above it.
      const heightMap = new Map(); // key: "x,z", value: { x, z, y, isFlat }
      for (let dx = -r; dx <= r; dx++) {
        for (let dz = -r; dz <= r; dz++) {
          const cx = botPosObj.x + dx;
          const cz = botPosObj.z + dz;
          // Scan top-down from bot's Y level
          for (let cy = botPosObj.y + 4; cy >= botPosObj.y - 6; cy--) {
            const block = bot.blockAt(new Vec3(cx, cy, cz));
            if (!block) continue;
            // Solid ground: block with bounding box (not empty/plant) + air above
            if (block.name !== 'air' && block.boundingBox === 'block') {
              const above = bot.blockAt(new Vec3(cx, cy + 1, cz));
              if (!above || above.name === 'air' || above.boundingBox === 'empty') {
                heightMap.set(`${cx},${cz}`, { x: cx, z: cz, y: cy + 1 });
                break;
              }
            }
          }
        }
      }

      // Flood-fill to find the largest contiguous flat region
      // "Flat" = adjacent columns whose Y values differ by at most 1
      const visited = new Set();
      let bestCandidate = null;
      let bestArea = 0;

      const isFlatNeighbor = (a, b) => a && b && Math.abs(a.y - b.y) <= 1;

      for (const [key, col] of heightMap) {
        if (visited.has(key)) continue;

        // BFS to find the connected flat component
        const component = [];
        const queue = [col];
        visited.add(key);

        while (queue.length > 0) {
          const cur = queue.shift();
          component.push(cur);

          for (const [ndx, ndz] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
            const nk = `${cur.x + ndx},${cur.z + ndz}`;
            const nbr = heightMap.get(nk);
            if (nbr && !visited.has(nk) && isFlatNeighbor(cur, nbr)) {
              visited.add(nk);
              queue.push(nbr);
            }
          }
        }

        if (component.length >= minArea && component.length > bestArea) {
          bestArea = component.length;
          // Compute the centroid of the component
          const avgY = Math.round(component.reduce((s, c) => s + c.y, 0) / component.length);
          const minX = Math.min(...component.map(c => c.x));
          const maxX = Math.max(...component.map(c => c.x));
          const minZ = Math.min(...component.map(c => c.z));
          const maxZ = Math.max(...component.map(c => c.z));
          bestCandidate = {
            x: Math.round((minX + maxX) / 2),
            y: avgY,
            z: Math.round((minZ + maxZ) / 2),
            area: component.length,
            minX, maxX, minZ, maxZ,
          };
        }
      }

      if (bestCandidate) {
        sendEvent('flatAreaFound', bestCandidate);
        console.log(`[findFlatArea] best candidate at (${bestCandidate.x},${bestCandidate.y},${bestCandidate.z}) area=${bestCandidate.area}`);
      } else {
        console.warn(`[findFlatArea] no flat area >= ${minArea} found within radius ${r}`);
        sendEvent('flatAreaFound', { x: botPosObj.x, y: botPosObj.y + 1, z: botPosObj.z, area: 0 });
      }
      break;
    }

    case 'status':
      sendBotStatus();
      break;

    case 'chat':
      bot.chat(args.message ?? '');
      break;

    case 'craft': {
      // Sprint 2a: pathfind to crafting table before crafting.
      // tableSearchRadius can be overridden per-call; defaults to CRAFT_TABLE_SEARCH_RADIUS.
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

        // Sprint 2a: search with expanded radius and pathfind to the table.
        craftingTable = bot.findBlock({ matching: tableId, maxDistance: tableSearchRadius });
        if (!craftingTable)
          throw new Error(`No crafting_table within ${tableSearchRadius} blocks`);

        // Pathfind to the crafting table so bot.craft() can reach it.
        const movements = new Movements(bot);
        bot.pathfinder.setMovements(movements);
        await bot.pathfinder.goto(new pfGoals.GoalNear(
          craftingTable.position.x,
          craftingTable.position.y,
          craftingTable.position.z,
          CRAFT_TABLE_REACH_DISTANCE
        ));

        // Re-fetch after navigation: bot may have shifted the chunk slightly.
        craftingTable = bot.blockAt(craftingTable.position);
        if (!craftingTable || craftingTable.type !== tableId)
          throw new Error('Crafting table not found after navigation');
      }

      await bot.craft(recipe, count, craftingTable);
      sendEvent('craftComplete', { item: itemName, count });
      console.log(`[craft] crafted ${count}x ${itemName}`);
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
        sendEvent('smeltComplete', { item: inputName, result: outputName, count: toSmelt });
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
