# Proposal: MineflayerAdapter Pathfinding for Roof-Level Block Placement

**Date:** 2026-06-27
**Author:** SteveBot (agent repo maintenance)
**Status:** Phase 1 in progress
**Related:** TSK-0077 (scaffolding), TSK-0123 (step-aside), TSK-0125 (per-block status), TSK-0213 (roof pathfinding)

> **Update 2026-06-27:** Phase 1 implemented — see implementation summary below.

---

## 1. Problem Statement

After the C# planner (`HtnTaskLibrary`) finishes placing wall blocks (Y=5/Y=6 in world
coordinates), the ~79 remaining blocks at Y=7 (roof level) cannot be reached. The bot
is trapped inside the partially built structure and pathfinding to exterior roof targets
repeatedly fails:

```
[action] PlaceBlock oak_planks → (166,7,333) (0ms) bot=(158,6,334)
[build] cycle complete: 1 blocks placed | remaining: 0
...
[correlation] place 90b5c9ee TIMED OUT after 10.3s — no result event received
```

### 1.1 Root Cause Analysis

**Blueprint ordering (BlueprintExecutor.cs):** Blocks are emitted Y-ascending
(floor Y=0 → walls Y=1/Y=2/Y=3 → roof Y=4 relative). This means ALL wall blocks,
including the Y=3 layer that seals the door opening, are placed **before** any
roof blocks. Once the Y=3 walls are complete, the bot is fully enclosed.

**Wall layer Y=3 is solid — no door opening:**
```
small-house.md Y=3 (Upper walls):
PPPPPPPPP   ← Z=6 (south face, where the door is): ALL planks, no gap
P.......P
... (entire perimeter is solid planks)
```

**Pathfinder cannot navigate through closed doors:**
`Movements` objects are created with default settings — `canOpenDoors` defaults
to `false`. Even if the door at Y=1/Y=2 is open, the pathfinder treats the upper
half as an obstacle and cannot plan a route through the doorway.

**Vertical gap:** Roof target at Y=7 — bot at Y=6 (feet). The pathfinder's
`GoalNear(166,7,333,2)` requires being within 2 blocks Euclidean. From ground
level at Y=5 (feet Y=6), distance to Y=7 is 2 blocks vertically, which is within
tolerance. However, the pathfinder cannot find a route through the walls to reach
any position from which the Euclidean check passes.

**C# timeout (5 seconds) too short for escape+placement:** The `place` tool has
a 5-second timeout (`ToolTimeoutOverrides["place"] = 5`). If the pathfinder
needs to navigate out through a door (2-3s) and then reach the roof target
(2-3s+), it may timeout before the placement succeeds.

---

## 2. Investigated Approaches

### Approach A (Preferred): Open-Door Pathfinding + Confinement Escape
Configure `Movements.canOpenDoors = true` so the pathfinder can navigate through
the door opening. Add confinement detection for cases where the door is blocked
or the bot is fully walled in.

**Pros:** Natural pathfinding flow, no special cases, works in survival mode.
**Cons:** Only works if the door is within pathfinder range and open. Doesn't
solve the Y=7 reachability from ground level if the target is purely vertical.

### Approach B: Navigate to Exterior via Known Door Coordinates
When pathfinding to a roof target fails, identify the door position from
blueprint data (passed in action args), then pathfind to a point outside the
door first, then retry the original target.

**Pros:** Explicit and reliable, works with any door position.
**Cons:** Requires blueprint metadata in action dispatch. Only works if the
door opening is unobstructed.

### Approach C: Pillar-Up from Inside
Bot detects it's inside a confined space with a target above, builds a
temporary pillar at its current position to reach the target Y-level, then
places the roof block from above.

**Pros:** Works in survival mode, no door needed.
**Cons:** Complex to implement correctly (pillar material selection, pillar
construction, pillar removal). The bot is still confined after pillaring —
it can only place blocks directly above its position.

### Approach D: Creative-Mode Flight (if applicable)
When the bot is in creative mode (game mode 1) and pathfinding to a roof
block fails, use `bot.creative.startFlying()` and `bot.creative.flyTo()` to
navigate directly to the target position from above.

**Pros:** Simple and reliable, solves both confinement and height problems.
**Cons:** Creative mode only. The `flyTo` API may not be available in all
Mineflayer versions.

### Approach E: Last Resort — Dig Out
After N consecutive pathfinding failures, mine the nearest natural-terrain
wall block to create an exit, then navigate out.

**Pros:** Always works regardless of door/door state.
**Cons:** Destructive, violates the "don't destroy placed blocks" principle.
Only works for natural terrain walls.

---

## 3. Recommended Solution: Hybrid Multi-Tier Approach

Implement a **pathfinding fallback chain** in the adapter's `case 'place':` block
with escalating strategies. Each tier only activates if the previous tier fails.

### Tier 1: Enable Door-Aware Pathfinding (Low Risk, High Impact)

Set `movements.canOpenDoors = true` on all `Movements` instances used for
`place` actions. This lets the pathfinder plan routes through open doors and
attempt to open closed doors.

```js
const movements = new Movements(bot);
movements.canOpenDoors = true;  // ← ADD THIS
bot.pathfinder.setMovements(movements);
```

**Risk:** Very low. The pathfinder only opens doors that are on the computed
path. If the door is closed and the server doesn't allow door interaction,
the pathfinder falls back gracefully (throws, same as current behavior).

### Tier 2: Confinement Detection + Escape Navigation (Medium Complexity)

Add a confinement check after the first pathfinding failure. If the bot appears
to be surrounded by non-air blocks within a small radius, attempt to navigate
to a known exterior point before retrying the placement.

**Detection heuristic:**
```js
function isConfined(bot) {
  const pos = botPos();
  let solidCount = 0;
  const checkPositions = [
    [pos.x+1, pos.y, pos.z], [pos.x-1, pos.y, pos.z],
    [pos.x, pos.y, pos.z+1], [pos.x, pos.y, pos.z-1],
    [pos.x+1, pos.y+1, pos.z], [pos.x-1, pos.y+1, pos.z],
    [pos.x, pos.y+1, pos.z+1], [pos.x, pos.y+1, pos.z-1],
  ];
  for (const [cx, cy, cz] of checkPositions) {
    const block = bot.blockAt(toVec3(cx, cy, cz));
    if (block && block.type !== 0) solidCount++;
  }
  return solidCount >= 6; // ≥75% of nearby positions are solid
}
```

**Escape strategy:** If confined, scan for the nearest air block at the bot's
Y-level within a small radius (5 blocks). Navigate to that air block (which is
likely a door opening or window), then retry the original pathfinding goal.

### Tier 3: Creative-Mode Flight (Creative Mode Only)

If the bot is in creative mode (game mode 1) and Tier 1+2 fail, use creative
flight to reach the target:

```js
if (bot.game?.gameMode === 1) {
  try {
    await bot.creative.startFlying();
    await bot.creative.flyTo(toVec3(x, y + 2, z)); // fly above target
    // Place block from above...
  } catch (e) {
    logStructured('warn', 'place', 'creative flight failed', { error: e.message });
  }
}
```

**Note:** `flyTo` may need a polyfill or alternative on Mineflayer versions
that don't support it. Fall back to pillar-up if unavailable.

### Tier 4: Pillar-Up from Current Position (Survival Mode Fallback)

For survival mode (or when creative flight is unavailable), build a temporary
pillar to reach the target Y-level:

1. Find a suitable pillar material from inventory (dirt, cobblestone, planks)
2. Place blocks at `(botX, currentFeetY, botZ)`, then `(botX, currentFeetY+1, botZ)`,
   climbing as each block is placed
3. Once at target Y, navigate along walls/roof edge to place roof blocks
4. After the build phase, optionally remove pillars

### Tier 5 (Future): Pre-Build Scaffolding Phase

A longer-term solution is to add a scaffolding phase to `BlueprintExecutor` or
`DecomposeBuild` that identifies Y-levels requiring elevation and emits
preliminary `PlaceBlock` actions for scaffold columns **before** the main build
sequence.

This would be a C#-side change in `HtnTaskLibrary.cs` or `BlueprintExecutor.cs`.

---

## 4. Implementation Plan

### Phase 1 (Immediate): Tier 1 — Door-Aware Pathfinding

**Files:** `MineflayerAdapter/index.js`

**Changes:**
1. Add `movements.canOpenDoors = true` to all `Movements` instances created
   in the `case 'place':` block (lines 891, 987). Also consider adding to
   `case 'move':` (line 376) for consistency.

**Validation:**
- Run build test with door-equipped blueprint
- Verify pathfinder navigates through open door
- Verify pathfinder attempts to open closed door (or skips gracefully)

### Phase 2 (Next Sprint): Tiers 2-4 — Fallback Chain

**Files:** `MineflayerAdapter/index.js`, `MineflayerAdapter/config.js`

**Changes:**
1. Add `isConfined()` helper function to `index.js`
2. Add confinement escape navigation after first `pathfinder.goto()` failure
   in `case 'place':`
3. Add creative flight fallback (Tier 3)
4. Add pillar-up fallback (Tier 4)
5. Add configuration constants: `PLACE_PATH_RETRY_COUNT`,
   `CONFINEMENT_CHECK_RADIUS`, `CONFINEMENT_SOLID_THRESHOLD`,
   `PILLAR_MATERIAL`
6. Wrap the main `pathfinder.goto()` call in a retry loop (up to 3 attempts)
   with progressive fallback strategies

**Validation:**
- Unit test `isConfined()` with mock block grid
- Integration test: bot placed inside structure with roof target
- Verify all 3 fallback paths produce correct events
- Verify no regression on normal ground-level placement

### Phase 3 (Future): Tier 5 — Pre-Build Scaffolding in C# Planner

**Files:** `Agent.Construction/BlueprintExecutor.cs`,
`Agent.Planning/HtnTaskLibrary.cs`

**Changes:**
1. `BlueprintExecutor` should optionally emit scaffold blocks for Y-levels
   above a configurable threshold (e.g., Y > origin + 4)
2. `HtnTaskLibrary` should pass `scaffoldMaterial` in action context
3. After roof completion, emit `MineBlock` actions to remove scaffolds

**Validation:**
- Run `small-house.md` blueprint end-to-end
- Verify all roof blocks are placed
- Verify scaffolds are cleaned up after build

---

## 5. C# Side Changes (AgentBackgroundService.cs)

### Increase Place Timeout for Roof-Level Actions

The 5-second timeout is too tight for escape + navigation + placement. Consider
a dynamic timeout based on target Y-level vs bot Y-level:

```csharp
// In dispatch loop: compute dynamic timeout for place actions
if (action.Tool == "place" && args contains y coordinate)
{
    var currentY = _worldState.Position?.Y ?? 0;
    var targetY = action.Arguments.GetValueOrDefault("y", 0);
    var heightDiff = Math.Abs(targetY - currentY);
    if (heightDiff > 2)
    {
        // More time needed for vertical navigation
        actionTimeoutSec = Math.Max(actionTimeoutSec, 15);
    }
}
```

Or simpler: increase the base `["place"]` timeout from 5s to 10-15s. The
current 5s was set at Sprint 43 for ground-level placement. Roof-level
placement has fundamentally different navigation requirements.

### Add Roof-Phase Detection

The C# planner could detect when the build transitions from walls to roof
and insert a special "MoveTo exterior" action before the first roof
PlaceBlock:

```csharp
// In HtnTaskLibrary, before first roof block:
if (currentYLayer != prevYLayer && currentYLayer > originY + 3)
{
    // Insert MoveTo(originX + doorX, originY, originZ + doorZ) to position
    // the bot outside before starting roof placement
    actions.Insert(i, ActionFactory.Create("MoveTo",
        ("x", originX + doorX), ("y", originY), ("z", originZ + doorZ)));
}
```

This gives the pathfinder a clear "exit to outside" goal before attempting
roof-level navigation.

---

## 6. Key Constants

New constants to add to `MineflayerAdapter/config.js`:

| Constant | Default | Purpose |
|----------|---------|---------|
| `PLACE_PATH_RETRY_COUNT` | 3 | Max pathfinding retries before fallback |
| `PLACE_CONFINEMENT_CHECK_RADIUS` | 3 | Block radius for confinement detection |
| `PLACE_CONFINEMENT_SOLID_THRESHOLD` | 6 | Solid block count (out of 8) to trigger confinement escape |
| `PLACE_ESCAPE_SEARCH_RADIUS` | 10 | Block radius to search for exterior air pocket |
| `PLACE_CREATIVE_FLY_ENABLED` | true | Whether to attempt creative flight fallback |
| `PLACE_PILLAR_MATERIAL` | "dirt" | Block type to use for pillar construction |

---

## Appendix A: Movements Class Property Reference

All configurable properties on `mineflayer-pathfinder`'s `Movements` class,
with defaults and relevance to this adapter. Properties are set in
`MineflayerAdapter/movements.js` via `createMovements()`.

### Navigation & Movement

| Property | Default | Description | Relevant? |
|----------|---------|-------------|-----------|
| `canOpenDoors` | `false` | Navigate through open fence gates/doors (v2.4.3: buggy on non-Paper) | ✅ Enabled |
| `allow1by1towers` | `true` | Allow pillaring up on 1x1 towers | ✅ Already on — bot CAN pillar but doesn't choose to |
| `allowFreeMotion` | `false` | Walk straight to next node if terrain allows | Maybe — could speed up open-field travel |
| `allowParkour` | `true` | Jump over gaps > 1 block | ✅ Already on |
| `allowSprinting` | `true` | Sprint when moving | ✅ Already on |
| `maxDropDown` | `4` | Max fall distance the pathfinder will attempt | Default fine |
| `infiniteLiquidDropdownDistance` | `true` | Ignore maxDropDown when landing in liquid | Default fine |

### Digging & Placement

| Property | Default | Description | Relevant? |
|----------|---------|-------------|-----------|
| `canDig` | `true` | Break blocks during pathfinding | ✅ Already on |
| `digCost` | `1` | Cost multiplier for breaking blocks | Default fine |
| `placeCost` | `1` | Cost multiplier for placing scaffolds | Default fine |
| `dontCreateFlow` | `true` | Don't break blocks touching liquid | Default fine |
| `dontMineUnderFallingBlock` | `true` | Don't break blocks with gravity above | Default fine |

### Entity & Block Avoidance

| Property | Default | Description | Relevant? |
|----------|---------|-------------|-----------|
| `allowEntityDetection` | `true` | Test for obstructing entities | Default fine |
| `entityCost` | `1` | Cost multiplier for entity-occupied tiles | Default fine |
| `entitiesToAvoid` | empty Set | Entities to completely avoid avoiding | Default fine |
| `passableEntities` | `passableEntities.json` | Entities to ignore in path cost | Default fine |
| `interactableBlocks` | `interactable.json` | Blocks to not right-click during pathfinding | Default fine |
| `blocksCantBreak` | registry-based | Unbreakable + chest blocks | Default fine |
| `blocksToAvoid` | fire, cobweb, lava | Blocks to path away from | Default fine |
| `liquids` | water, lava | Liquid block IDs | Default fine |
| `gravityBlocks` | sand, gravel | Blocks that fall on bot's head | Default fine |
| `climbables` | ladder | Climbable blocks (unused — pathfinder can't climb) | Default fine |
| `fences` | registry-based | Fence/fence gate blocks | Default fine |
| `carpets` | registry-based | Blocks with < 0.1 collision height | Default fine |

### Scaffolding

| Property | Default | Description | Relevant? |
|----------|---------|-------------|-----------|
| `scafoldingBlocks` | `[dirt, cobblestone]` | Item IDs used for pillar blocks | ✅ Already set — TSK-0077 may extend |
| `replaceables` | air, water, lava | Blocks the pathfinder can replace | Default fine |

### Exclusion Areas

| Property | Default | Description | Relevant? |
|----------|---------|-------------|-----------|
| `exclusionAreasStep` | `[]` | Functions to penalize stepping on blocks | Future |
| `exclusionAreasBreak` | `[]` | Functions to penalize breaking blocks | Future |
| `exclusionAreasPlace` | `[]` | Functions to penalize placing blocks | Future |

### Key Takeaways for Roof Pathfinding

1. **`allow1by1towers = true`** — The pathfinder CAN build pillars. If the bot
   isn't pillaring to reach Y=7, it's because the pathfinder thinks it's
   unnecessary (tolerance check succeeds) or the path is blocked entirely.
2. **`canOpenDoors = true`** — Now enabled. The pathfinder will try to
   navigate through open doors.
3. **`scafoldingBlocks`** — Includes dirt and cobblestone, the materials
   needed for pillar construction during roof phases.

---

## 7. Acceptance Criteria

1. **AC-1:** Bot builds small-house walls and transitions to roof phase
   without getting stuck — all ~79 roof blocks at Y=7 are placed.
2. **AC-2:** No place actions timeout due to confinement (5s limit may still
   apply for individual blocks, but the bot doesn't get stuck permanently).
3. **AC-3:** Door-aware pathfinding is enabled — pathfinder navigates through
   door openings.
4. **AC-4:** If pathfinding fails on the first attempt, at least one fallback
   strategy is tried before declaring failure.
5. **AC-5:** In creative mode, the bot uses flight as a fallback if ground
   pathfinding fails.
6. **AC-6:** In survival mode, the bot builds a pillar to reach roof height
   if ground pathfinding fails.
7. **AC-7:** No regressions on ground-level (Y=0-2) wall/floor placement.

---

## 8. Risk Assessment

| Risk | Severity | Likelihood | Mitigation |
|------|----------|------------|------------|
| `canOpenDoors` causes unexpected pathfinder behavior | Low | Low | Only enable on `Movements` for `place` actions; revert if issues |
| Creative flight not available in all Mineflayer versions | Medium | Medium | Check `bot.creative` availability; fall back to pillar-up |
| Pillar-up places blocks inside blueprint volume | Medium | Low | Scan target position before placing pillar; use same Y-footprint check |
| Confinement detection false positive (open field) | Low | Low | Tune threshold; default conservative (≥6/8 solid) |
| Timeout still fires during escape sequence | Medium | Medium | Increase timeout for roof-level actions (C# side) |

---

## 9. Related Work

| Reference | Description | Status |
|-----------|-------------|--------|
| TSK-0077 | Scaffolding for Roof/Upper Walls | Backlog |
| TSK-0123 | Step-aside from target position before placing | Done (Sprint 52) |
| TSK-0125 | Per-block status tracking (placedInFacts) | Done |
| Handoff-Sprint41 | Original placement hygiene analysis | Reference |
| Handoff-Sprint44 | P1-3 scaffolding deferred to backlog | Reference |

---

---

## Phase 1 Implementation (2026-06-27)

### What was implemented

**1. `MineflayerAdapter/movements.js` (new) — Movements factory**

Created a centralized factory function `createMovements(bot)` that all action
handlers use instead of `new Movements(bot)`. Currently configured with:

```js
m.canOpenDoors = true;  // navigate through open doors and fence gates
```

All other `Movements` properties retain their mineflayer-pathfinder defaults
(see §6 for the full list).

**2. `MineflayerAdapter/index.js` — 7 call sites updated**

Every `const movements = new Movements(bot)` was replaced with
`const movements = createMovements(bot)`:

| Line | Case | Purpose |
|------|------|---------|
| 377 | move | Navigate to coordinates |
| 393 | mine | Navigate to mine target |
| 892 | place | Navigate to placement target |
| 1077 | wander | Wander navigation |
| 1421 | craft | Navigate to crafting table |
| 1452 | smelt | Navigate to furnace |
| 1512 | findReachableBlock | Pathfinding reachability check |

### What was NOT implemented (per user feedback)

- Place timeout left at 5 seconds (no increase)
- Confinement detection / escape logic deferred
- Creative flight / pillar-up fallbacks deferred

### Next steps

1. **MoveTo before roof phase** — Insert an explicit `MoveTo(doorExterior)`
   action in the C# planner (`HtnTaskLibrary`) before the first roof-level
   PlaceBlock, ensuring the bot stands outside before attempting roof placement.
2. **Pillar-up / escape** — If pathfinding with `canOpenDoors=true` still
   fails, implement the confinement escape and pillar-up fallbacks (Phase 2).

---

## 10. Quick-Start: Minimal Viable Fix

If the above seems too ambitious for a single sprint, here is the
**minimal fix that addresses the core issue:**

1. **In `index.js`:** Add `movements.canOpenDoors = true` to the
   `case 'place':` block (line ~892). This alone may resolve the confinement
   issue if the door opening is passable.

2. **In `AgentBackgroundService.cs`:** Increase `["place"]` timeout from 5
   to 15 seconds to accommodate escape + navigation time.

3. **In `index.js`:** Wrap the main `pathfinder.goto()` call (line 896) in a
   try-catch with a simple retry: if the first goto fails, try navigating to
   `(x, y-2, z)` (ground position below target) before retrying the original.

These three changes cover the most common failure modes with minimal code
changes and zero new abstractions.
