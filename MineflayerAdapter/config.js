/**
 * config.js — Tunable constants for the MineflayerAdapter.
 *
 * Extracted from index.js Sprint 52 modularization (TSK-0166).
 * All numeric thresholds, radii, timeouts, and scoring weights live here.
 * Do not embed magic numbers in action handlers — add them here instead.
 */

// ── Mining ────────────────────────────────────────────────────────────────────

/** Search radius for first findBlock pass (same Y-level). */
export const MINE_SEARCH_RADIUS_NEAR    = 64;
/** Search radius for second findBlock pass (any Y-level). */
export const MINE_SEARCH_RADIUS_FAR     = 128;
/** Max consecutive pathfinder failures before aborting a mine operation. */
export const MAX_MINE_PATH_FAILURES     = 3;
/** Max consecutive dig failures before skipping a block. */
export const MAX_DIG_FAILURES          = 3;

// ── Item pickup ───────────────────────────────────────────────────────────────
// Sprint 40 P0-B: after mining a block, the bot waits for the item entity to
// appear and be auto-collected, then moves to the block position and waits again.

/** Wait after completing a dig for auto-pickup. */
export const MINE_ITEM_PICKUP_WAIT_MS           = 1000;
/** Wait after moving to the mined block's position. */
export const MINE_ITEM_PICKUP_MOVE_WAIT_MS      = 1500;
/** Wait after removing an obstruction above the mined block. */
export const MINE_ITEM_PICKUP_REMOVE_BLOCK_MS   = 300;

// ── Block scoring ─────────────────────────────────────────────────────────────
// Sprint 40 P0-C: Y-level preference scoring for findBestBlock.

/** Score penalty per Y-level away from the expected Y. */
export const MINE_Y_PENALTY_WEIGHT    = 5;
/** Max candidates in the same-Y-level pass. */
export const MINE_FIRST_PASS_COUNT    = 10;
/** Max candidates in the nearby-Y-level pass. */
export const MINE_SECOND_PASS_COUNT   = 20;

// ── Block mining aliases ──────────────────────────────────────────────────────
// Sprint 40 P0-C: when asked to mine a block, also accept blocks that drop the
// same item when mined. Key = target block, Value = acceptable block names.

export const BLOCK_MINING_ALIASES = Object.freeze({
  dirt: ['dirt', 'grass_block'],
});

// ── Reachability search ───────────────────────────────────────────────────────
// Sprint 40 P0-B: when looking for a reachable variant of a block.

export const REACHABLE_BLOCK_MAX_CANDIDATES     = 20;
export const REACHABLE_BLOCK_PATH_TIMEOUT_MS    = 5000;
export const REACHABLE_BLOCK_GOTO_TOLERANCE     = 2;

// ── Crafting / smelting ───────────────────────────────────────────────────────

export const CRAFT_TABLE_SEARCH_RADIUS  = 8;
export const CRAFT_TABLE_REACH_DISTANCE = 2;
export const FURNACE_SEARCH_RADIUS      = 16;
export const FURNACE_REACH_DISTANCE     = 2;
/** Total timeout for smelt operations (all items). */
export const SMELT_TIMEOUT_MS           = 40_000;

// ── Flat-area scan ────────────────────────────────────────────────────────────
// Sprint 19: increased default radius from 20 to 32. C# planner sends radius=48
// on retry after a zero-area result.

export const FLAT_AREA_SCAN_RADIUS      = 32;
export const FLAT_AREA_MIN_SIZE         = 25;
export const FLAT_AREA_Y_ABOVE          = 10;
export const FLAT_AREA_Y_BELOW          = 16;
export const FLAT_AREA_MAX_SLOPE        = 3;

// Sprint 37: weighted scoring prefers closer flat areas over distant platforms.
export const FLAT_SCORE_WEIGHTS = Object.freeze({
  area:        0.35,
  compactness: 0.20,
  flatness:    0.15,
  proximity:   0.30,
});

// ── Misc ──────────────────────────────────────────────────────────────────────

export const LIQUID_BLOCK_NAMES = new Set([
  'water', 'lava', 'flowing_water', 'flowing_lava',
]);
