/**
 * MineflayerAdapter — Node.js bridge between the MemorySmith.Agent C# host
 * and a Minecraft server.
 *
 * Architecture:
 *   1. Starts a WebSocket server on WS_PORT (default: 3000).
 *   2. Creates a Mineflayer bot connecting to MC_HOST:MC_PORT.
 *   3. Forwards bot events (spawn, health, move, blockMined…) to C# as JSON.
 *   4. Accepts JSON action commands from C# and executes them via Mineflayer.
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
import { pathfinder, Movements, goals as pfGoals } from 'mineflayer-pathfinder';
import { WebSocketServer } from 'ws';

const WS_PORT  = parseInt(process.env.WS_PORT   ?? '3000',    10);
const MC_HOST  = process.env.MC_HOST   ?? 'localhost';
const MC_PORT  = parseInt(process.env.MC_PORT   ?? '25565',   10);
const MC_USER  = process.env.MC_USERNAME ?? 'AgentBot';
const MC_VER   = process.env.MC_VERSION;       // omit for auto-detect
const WS_TOKEN = process.env.WS_TOKEN ?? null; // optional auth

// ── WebSocket server (C# connects here) ──────────────────────────────────────

const wss = new WebSocketServer({ port: WS_PORT });
let agentSocket = null;

function sendEvent(event, data = {}) {
  if (agentSocket?.readyState === 1 /* OPEN */) {
    agentSocket.send(JSON.stringify({ event, ...data }));
  }
}

wss.on('listening', () => console.log(`[ws] server listening on port ${WS_PORT}`));

wss.on('connection', (ws) => {
  console.log('[ws] C# agent connected');
  agentSocket = ws;

  ws.on('message', async (raw) => {
    let msg;
    try { msg = JSON.parse(raw.toString()); }
    catch (e) { console.error('[ws] bad JSON:', e.message); return; }

    if (WS_TOKEN && msg.token !== WS_TOKEN) {
      ws.close(1008, 'Unauthorized');
      return;
    }

    await dispatch(msg).catch(e => {
      console.error(`[dispatch] ${msg.action} failed:`, e.message);
      sendEvent('error', { action: msg.action, message: e.message });
    });
  });

  ws.on('close', () => {
    if (agentSocket === ws) agentSocket = null;
    console.log('[ws] C# agent disconnected');
  });

  ws.on('error', (e) => console.error('[ws] socket error:', e.message));

  // Immediate status update so C# knows the bot state on connect
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
  sendEvent('status', {
    ...botPos(),
    hp:   bot.health ?? 20,
    food: bot.food   ?? 20,
    inventory: Object.fromEntries(
      (bot.inventory?.items() ?? []).map(item => [item.name, item.count])
    ),
  });
}

// Forward bot events to C#
bot.once('spawn', () => {
  console.log('[mc] bot spawned at', botPos());
  sendEvent('spawn', { ...botPos(), hp: bot.health, food: bot.food });
});
bot.on('health', () => sendEvent('health', { hp: bot.health, food: bot.food }));
bot.on('move',   () => sendEvent('move',   botPos()));
bot.on('death',  () => sendEvent('death',  botPos()));
bot.on('kicked', (reason) => { console.warn('[mc] kicked:', reason); sendEvent('kicked', { reason }); });
bot.on('error',  (e) => { console.error('[mc] error:', e.message); sendEvent('error', { message: e.message }); });

bot.on('blockBreakProgressEnd', (block) =>
  sendEvent('blockMined', { block: block.name, ...botPos() })
);

bot.on('chat', (username, message) => {
  if (username === bot.username) return;
  sendEvent('chat', { username, message });
});

// ── Action dispatcher ──────────────────────────────────────────────────────────

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
      const blockId = bot.registry.blocksByName[blockName.replace('minecraft:', '')]?.id;
      let mined = 0;
      while (mined < count) {
        const target = bot.findBlock({ matching: blockId, maxDistance: 32 });
        if (!target) { sendEvent('blockNotFound', { block: blockName }); break; }
        await bot.dig(target);
        mined++;
        sendEvent('blockMined', { block: blockName, count: mined, ...botPos() });
      }
      break;
    }

    case 'place': {
      const { x, y, z, material } = args;
      if (x == null || !material) throw new Error('place requires x, y, z, material');
      const refBlock = bot.blockAt(bot.entity.position.offset(0, -1, 0));
      if (!refBlock) throw new Error('No ground block to place on');
      const item = bot.inventory.items().find(i => i.name === material);
      if (!item) throw new Error(`${material} not in inventory`);
      await bot.equip(item, 'hand');
      await bot.placeBlock(refBlock, { x: 0, y: 1, z: 0 });
      sendEvent('blockPlaced', { x, y, z, block: material });
      break;
    }

    case 'status':
      sendBotStatus();
      break;

    case 'chat':
      bot.chat(args.message ?? '');
      break;

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
