/**
 * movements.js — Movements factory for the MineflayerAdapter.
 *
 * Sprint 55 (TSK-0213): Centralizes creation of bot.pathfinder Movements instances
 * with consistent settings across all action handlers (move, mine, place, craft,
 * smelt, wander, findFlatArea, findReachableBlock).
 *
 * Previously each handler created bare `new Movements(bot)` with default settings.
 * The factory ensures all pathfinding uses the same tuned defaults:
 *   - canOpenDoors = true  — navigate through open doors and fence gates
 *   - All other defaults from mineflayer-pathfinder's Movements class remain
 *     as-is unless explicitly overridden.
 *
 * Uses a lazy singleton — the first call constructs and caches one Movements
 * instance. Subsequent calls return the cached instance. The pathfinder calls
 * updateCollisionIndex() internally before each path computation, so a shared
 * instance is safe (entityIntersections are refreshed per-path).
 *
 * Add new shared settings here rather than patching individual action handlers.
 */

import { Movements } from 'mineflayer-pathfinder';

/** @type {Movements|null} */
let _instance = null;

/**
 * Returns the adapter's shared Movements instance.
 *
 * Lazily constructed on first call. The bot argument is only used during
 * construction to access bot.registry; subsequent calls ignore it.
 *
 * @param {object} bot - Mineflayer bot instance (required for first call)
 * @returns {Movements} Configured Movements singleton
 */
export function createMovements(bot) {
  if (!_instance) {
    const m = new Movements(bot);

    // ── Door / fence-gate navigation ────────────────────────────────────────
    // Enable pathfinding through open doors and fence gates.
    // Default: false (mineflayer-pathfinder v2.4.3+ changed default because it
    // can be unreliable on non-Paper servers). We enable it because the bot
    // frequently needs to navigate through door openings in built structures.
    m.canOpenDoors = true;

    _instance = m;
  }
  return _instance;
}
