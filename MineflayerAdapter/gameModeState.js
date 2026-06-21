export function emitGameModeEvent(bot, sendEvent, logStructured) {
  const mode = bot?.game?.gameMode;
  if (!mode) return false;

  const normalizedMode = normalizeGameMode(mode);
  if (!normalizedMode) return false;

  sendEvent('gameMode', { mode: normalizedMode });
  logStructured?.('info', 'game', 'gamemode detected', { mode: normalizedMode });
  return true;
}

function normalizeGameMode(rawValue) {
  const mode = (rawValue ?? '').trim().toLowerCase();
  if (!mode) return null;
  if (mode.includes('creative')) return 'creative';
  if (mode.includes('survival')) return 'survival';
  if (mode.includes('adventure')) return 'adventure';
  if (mode.includes('spectator')) return 'spectator';
  return null;
}
