/**
 * MemorySmith.Agent — Mineflayer Adapter
 *
 * Bridges the C# AgentHost and Minecraft via WebSocket.
 * Spawned as a subprocess by MinecraftAdapter.cs (Process.Start).
 *
 * Flow:
 *   1. C# host starts this process and connects on ws://localhost:PORT.
 *   2. On bot.once('spawn'): send initial state event.
 *   3. Listen for JSON commands from C# (action messages).
 *   4. Execute via Mineflayer API; send back JSON events (results, world state).
 *
 * Command (C# → Node):
 *   {"action":"mine","block":"minecraft:iron_ore","quantity":5}
 *
 * Event (Node → C#):
 *   {"event":"blockMined","block":"iron_ore","count":3,"position":{"x":10,"y":64,"z":20}}
 */

import mineflayer from 'mineflayer';
import { WebSocketServer } from 'ws';

const PORT       = parseInt(process.env.WS_PORT  || '3000', 10);
const MC_HOST    = process.env.MC_HOST            || 'localhost';
const MC_PORT    = parseInt(process.env.MC_PORT   || '25565', 10);
const MC_USER    = process.env.MC_USERNAME        || 'AgentBot';
const AUTH_TOKEN = process.env.AGENT_TOKEN        || '';

// --- WebSocket server (C# host connects here) ---
const wss = new WebSocketServer({ port: PORT });
let agentSocket = null;

function sendEvent(event) {
  if (agentSocket && agentSocket.readyState === 1 /* OPEN */) {
    agentSocket.send(JSON.stringify(event));
  }
}

wss.on('connection', (ws) => {
  console.log('[adapter] C# host connected');
  agentSocket = ws;

  ws.on('message', (data) => {
    let cmd;
    try { cmd = JSON.parse(data.toString()); } catch { return; }
    handleCommand(cmd).catch(err => sendEvent({ event: 'error', message: String(err) }));
  });

  ws.on('close', () => {
    console.log('[adapter] C# host disconnected');
    agentSocket = null;
  });
});

// --- Mineflayer bot ---
const bot = mineflayer.createBot({
  host: MC_HOST,
  port: MC_PORT,
  username: MC_USER,
  auth: 'offline',
  version: false,
});

bot.once('spawn', () => {
  console.log('[bot] spawned');
  sendEvent({
    event: 'spawn',
    position: bot.entity.position,
    health: bot.health,
    food: bot.food,
  });
});

bot.on('health', () => {
  sendEvent({ event: 'health', hp: bot.health, food: bot.food });
});

bot.on('death', () => {
  sendEvent({ event: 'death' });
});

bot.on('error', (err) => {
  console.error('[bot] error:', err.message);
  sendEvent({ event: 'error', message: err.message });
});

// --- Command dispatcher ---
async function handleCommand(cmd) {
  switch (cmd.action) {
    case 'move':
      // TODO: use pathfinder plugin for navigation
      sendEvent({ event: 'moveResult', success: false, message: 'pathfinder not yet wired' });
      break;

    case 'mine':
      // TODO: implement mining via bot.dig
      sendEvent({ event: 'mineResult', success: false, message: 'mining not yet implemented' });
      break;

    case 'place':
      // TODO: implement block placement via bot.placeBlock
      sendEvent({ event: 'placeResult', success: false, message: 'placement not yet implemented' });
      break;

    case 'status':
      sendEvent({
        event: 'status',
        position: bot.entity?.position ?? null,
        health: bot.health,
        food: bot.food,
        inventory: bot.inventory?.items().map(i => ({ name: i.name, count: i.count })) ?? [],
      });
      break;

    default:
      sendEvent({ event: 'error', message: `Unknown action: ${cmd.action}` });
  }
}

console.log(`[adapter] WebSocket server listening on ws://localhost:${PORT}`);
console.log(`[adapter] Connecting bot to ${MC_HOST}:${MC_PORT} as ${MC_USER}`);
