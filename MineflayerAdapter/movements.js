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
 * Add new shared settings here rather than patching individual action handlers.
 */

import { Movements } from 'mineflayer-pathfinder';

/**
 * Creates a Movements instance with the adapter's standard configuration.
 *
 * @param {object} bot - Mineflayer bot instance
 * @returns {Movements} Configured Movements instance
 */
export function createMovements(bot) {
  const m = new Movements(bot);

  // ── Door / fence-gate navigation ────────────────────────────────────────
  // Enable pathfinding through open doors and fence gates.
  // Default: false (mineflayer-pathfinder v2.4.3+ changed default because it
  // can be unreliable on non-Paper servers). We enable it because the bot
  // frequently needs to navigate through door openings in built structures.
  m.canOpenDoors = true;

  return m;
}
