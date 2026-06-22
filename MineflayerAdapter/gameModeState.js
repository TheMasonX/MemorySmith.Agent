export function emitGameModeEvent(bot, sendEvent, logStructured) {
  const mode = bot?.game?.gameMode;
  // Sprint 37 fix: Mineflayer stores game mode as a number (0=survival, 1=creative, etc.)
  // The old `if (!mode)` guard treated `0` (survival) as falsy, so survival was NEVER emitted.
  if (mode === undefined || mode === null) return false;

  const normalizedMode = normalizeGameMode(mode);
  if (!normalizedMode) return false;

  sendEvent('gameMode', { mode: normalizedMode });
  logStructured?.('info', 'game', 'gamemode detected', { mode: normalizedMode });
  return true;
}

/** Sprint 37: handles numeric game mode values (0=survival, 1=creative, etc.) */
const GAME_MODE_NUMBERS = Object.freeze({
  0: 'survival',
  1: 'creative',
  2: 'adventure',
  3: 'spectator',
});

function normalizeGameMode(rawValue) {
  if (rawValue === undefined || rawValue === null) return null;
  // Handle numeric game mode (Mineflayer stores as number in MC 1.21+)
  if (typeof rawValue === 'number') return GAME_MODE_NUMBERS[rawValue] ?? null;
  // Handle string game mode (older Mineflayer or chat-detected)
  const mode = String(rawValue).trim().toLowerCase();
  if (!mode) return null;
  if (mode.includes('creative')) return 'creative';
  if (mode.includes('survival')) return 'survival';
  if (mode.includes('adventure')) return 'adventure';
  if (mode.includes('spectator')) return 'spectator';
  return null;
}

export { normalizeGameMode };
