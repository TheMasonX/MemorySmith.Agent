/**
 * chatFilter.js — System message filtering and chat event forwarding.
 *
 * Extracted from index.js Sprint 52 modularization (TSK-0166).
 * Server-generated messages (teleport confirmations, join/leave, /give, /clear,
 * gamemode changes) must never reach the LLM chat pipeline. Even in solo play
 * all messages pass the IsDirectedAtBot heuristic, causing expensive Ollama calls
 * that return null. Filter them at the source.
 *
 * Usage:
 *   import { registerChatFilter } from './chatFilter.js';
 *   registerChatFilter(bot, sendEvent, logStructured, botPos, normalizeGameMode);
 */

// ── System message patterns ───────────────────────────────────────────────────

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
  /^Removed\s+\d+\s+items?\s+from\s+/i, // /clear response
  /^Cleared\s+(?:\d+|\S+'s|the\s+inventory)/i, // /clear variants
  /^Gave\s+\S+\s+\d+\s+/i,             // /give alt: "Gave TheMasonX23 64 [Dirt]"
];

// ── Public API ────────────────────────────────────────────────────────────────

/**
 * Returns true if the message is a Minecraft server system message that should
 * not be forwarded to the C# chat pipeline.
 */
function isSystemMessage(username, message) {
  if (!username || username.trim() === '') return true;
  return SYSTEM_MESSAGE_PATTERNS.some(re => re.test(message));
}

/**
 * Registers the chat event handler on the bot. Call once during bot setup.
 * @param {import('mineflayer').Bot} bot
 * @param {Function} sendEvent - adapter's sendEvent function
 * @param {Function} logStructured - structured logger
 * @param {Function} botPos - returns {x, y, z} of bot position
 * @param {Function} normalizeGameMode - game mode normalizer from gameModeState.js
 */
export function registerChatFilter(bot, sendEvent, logStructured, botPos, normalizeGameMode) {
  bot.on('chat', (username, message) => {
    // Sprint 38 P0-D (BUG-D): defensive try/catch — any uncaught error in the
    // chat handler would crash the adapter process.
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
}
