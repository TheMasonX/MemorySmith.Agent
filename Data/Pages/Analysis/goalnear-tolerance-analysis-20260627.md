# Analysis: GoalNear Tolerance for Roof-Level Block Placement

**Date:** 2026-06-27  
**Task:** TSK-0213 (adapter-roof-pathfinding-escape)  
**Context:** Bot gets stuck at Y=6, roof targets at Y=7, `GoalNear(x,y,z,2)` fails.

---

## 1. The Core Problem

```
bot:   (158, 6, 334)    ← botPos() returns floor(entity.y) = 6
goal:  (166, 7, 333)    ← roof slab placement target
```

The pathfinder uses `GoalNear(x, y, z, 2)` which has:

```js
// GoalNear.isEnd(node):
isEnd(node) {
    const dx = this.x - node.x
    const dy = this.y - node.y
    const dz = this.z - node.z
    return (dx * dx + dy * dy + dz * dz) <= this.rangeSq
}
```

For a `GoalNear(166, 7, 333, 2)` the check is:
```
dx² + dy² + dz² <= 4    (rangeSq = 2² = 4)
```

Even if the bot pathfinds to directly **under** the target at `(166, 6, 333)`:
```
(166-166)² + (7-6)² + (333-333)² = 0 + 1 + 0 = 1 <= 4  →  isEnd = true ✓
```

So `GoalNear` with tolerance 2 **should** consider Y=7 reachable from Y=6. But the pathfinder can't find any route at all because the walls block horizontal movement — the `isEnd` check never fires because no path is computed.

**Conclusion:** `GoalNear` tolerance is NOT the bottleneck for this scenario. The pathfinder can't find *any* path to a position within 2 blocks of the target, because it's inside a walled structure.

However, there IS a related problem with the *later* placement step.

---

## 2. The Real Tolerance Bug: `placeBlock` Interaction Distance

After the bot navigates to within `GoalNear(x,y,z,2)` of the target, it needs to call `bot.placeBlock(ref, faceVec)`. Minecraft's interaction reach is **~4.5 blocks** from the player's eye position (eye height = 1.6 blocks above feet).

### Example Scenario: Bot stands at ground level, target at Y=7

```
bot entity position:  (158, 6, 334)     ← feet Y
eye position:         (158, 7.6, 334)   ← feet + 1.6
target:               (166, 7, 333)
reference block:      (166, 6, 333)     ← ground below target

Distance from eye to reference block face:
  dx = 166 - 158 = 8
  dz = 333 - 334 = -1
  dy = 6 - 7.6 = -1.6
  dist = sqrt(8² + 1² + 1.6²) = sqrt(64 + 1 + 2.56) = 8.2 blocks
```

8.2 blocks > 4.5 reach → **cannot interact**. This is why the bot fails to place — it navigated to within 2 blocks horizontally of the `GoalNear` goal, but the *reference block* is beyond reach.

### Current Tolerances vs. Interaction Reach

| Call site | Tolerance | 2D horiz ok? | 3D vertical ok? | Can reach ref block? |
|-----------|-----------|-------------|-----------------|---------------------|
| `place` goto (line 897) | GoalNear(,,,2) | ✅ Yes for ground targets | Borderline for Y+1 | ❌ No for roof (Y+1) |
| `place` scaffold (line 988) | GoalNear(,,,2) | ✅ | Borderline | N/A (scaffold) |
| `move` (line 379) | GoalNear(,,,1) | ✅ | Good | N/A |
| `mine` (line 596) | GoalNear(,,,2) | ✅ | Borderline | ✅ (dig reach) |

The pathfinder navigates to **block-level coordinates** (floored), and `GoalNear` uses the node's floored position for `isEnd`. But `bot.placeBlock()` uses **entity eye position** for interaction reach — a very different metric.

---

## 3. Options Analysis

### Option A: Increase `GoalNear` tolerance for `place` (e.g., 2→3)

Changing `GoalNear(x, y, z, 2)` → `GoalNear(x, y, z, 3)` for the final approach in `case 'place':`.

```js
// isEnd check:
rangeSq = 9
dx² + dy² + dz² <= 9
```

**Pros:**
- Simple one-line change
- Gives pathfinder more room to satisfy the goal

**Cons:**
- ❌ **Doesn't solve the confinement problem** — if pathfinder can't find ANY path, higher tolerance doesn't help
- ❌ **Makes reachability WORSE** — bot may be satisfied being farther from target, reducing chance of successful `placeBlock`
- Sprint 42 (TSK-0076) **explicitly reduced** tolerance from 3→2 for this reason

**Verdict: ❌ REJECTED** — TSK-0076 was correct, reverting it would reintroduce the original placement distance bug.

### Option B: Use `GoalBlock` instead of `GoalNear` for `place`

`GoalBlock(x, y, z)` requires the bot to stand **inside** the target block at foot level.

```js
// GoalBlock.isEnd:
isEnd(node) {
    return node.x === this.x && node.y === this.y && node.z === this.z
}
```

**Pros:**
- More precise positioning — bot is closer to the target
- Eliminates off-by-one reachability issues

**Cons:**
- ❌ **Bot must stand AT the target position** — for a roof block at Y=7, the bot needs to be at Y=7, which means it must already be on the roof or have pillared up
- ❌ **Extremely brittle** — if the bot can't reach the exact block, pathfinding fails with no fallback
- ❌ Can cause pathfinder to try to walk through walls to stand inside a solid block

**Verdict: ❌ REJECTED** — Too strict, would cause more failures than it fixes.

### Option C: Use `GoalNearXZ` + `GoalY` composite for vertical separation

Separate the horizontal navigation (to get close to the target) from the vertical requirement (to get to the right Y level). Use `GoalCompositeAll([GoalNearXZ(x, z, 2), GoalY(y)])`.

```js
const goal = new pfGoals.GoalCompositeAll([
    new pfGoals.GoalNearXZ(x, 2),   // within 2 blocks horizontally
    new pfGoals.GoalY(y)             // at the target Y level (feet)
]);
```

**How it works:**
- `GoalNearXZ.isEnd`: `dx² + dz² <= rangeSq` (ignores Y)
- `GoalY.isEnd`: `node.y === this.y` (exact Y match)
- `GoalCompositeAll.isEnd`: ALL children must be satisfied

**Pros:**
- Forces the pathfinder to get the bot to the correct Y level (roof height)
- Separates horizontal vs. vertical navigation concerns
- Works with `allow1by1towers = true` — the pathfinder WILL pillar up to reach Y=7
- No change to interaction distance (bot ends up at the target block, easily within reach)

**Cons:**
- ❌ **Currently `GoalNear` treats Y tolerance loosely** — the pathfinder may be happy with Y=6 when target is Y=7. `GoalY` removes this slack, making pathfinding *harder* in some cases
- ❌ **Pathfinder may not know how to pillar up to Y=7 from inside the structure** — if the ceiling is Y=7 and the bot is at Y=6, it needs 1 block headroom for `allow1by1towers` to work
- Medium complexity (constructing composite goals vs. simple `GoalNear`)
- Risk: pathfinder could try to break through the Y=7 roof blocks to reach `GoalY(7)` from below

**Verdict: ⚠️ HIGH RISK without escape-first navigation** — CompositeAll is the correct *theoretical* fix (pillar to Y=7, then place) but it interacts dangerously with the confinement problem. The pathfinder might try to dig through the roof or break walls.

### Option D: Use `GoalPlaceBlock` (the dedicated placement goal)

Mineflayer-pathfinder ships with `GoalPlaceBlock(pos, world, options)` which is specifically designed for block placement — it pathfinds to a position where the bot can see and reach a reference block face.

```js
const goal = new pfGoals.GoalPlaceBlock(
    toVec3(x, y, z),
    bot.world,
    { range: 4.5, faces: [...], LOS: true }
);
```

**The GoalPlaceBlock `isEnd` check:**
1. Computes all valid reference faces from the target position
2. For each face, checks if the bot's eye position can see it (`raycast`)  
3. If any face is visible and within range, `isEnd = true`

This is EXACTLY what the bot needs — it positions itself where placement works.

**Pros:**
- ✅ Purpose-built for this exact use case
- ✅ Handles interaction distance natively (default range = 5 blocks)
- ✅ Works with any Y-level (pathfinder finds a position, not a block)
- ✅ No manual face iteration needed
- ✅ Eliminates the disconnect between "GoalNear says close enough" and "placeBlock can't reach"
- No change to C# timeout needed — goal is precise enough that placement succeeds immediately

**Cons:**
- ❌ **Not tested in this codebase** — unknown compatibility with current adapter patterns
- ❌ `GoalPlaceBlock` requires a `world` reference (`bot.world`) and may not work correctly with the `toVec3` compatibility layer
- ❌ If face detection fails (target has no valid reference faces), the goal can never be satisfied
- ❌ Requires careful setup of the `options.faces` array (should match the current 6-face reference detection in the `place` handler)
- ⚠️ Might not work in all Mineflayer versions (depends on version-specific raycast API)

**Verdict: ⚠️ BEST LONG-TERM SOLUTION** but needs verification/testing before adoption.

### Option E: Hybrid — `GoalNearXZ` + retry with Y-targeting on failure

Keep `GoalNear(x, y, z, 2)` as the primary goal. When it fails (catches error), retry with:
1. First attempt: `GoalNear(x, y, z, 2)` — original behavior
2. First retry: `GoalNear(x, y, z, 2)` — but with `canOpenDoors=true` (already done in Phase 1)
3. Second retry: `GoalNearXZ(x, z, 2)` — navigate within 2 blocks XZ of the target, ignoring Y
4. Third retry: `GoalCompositeAll([GoalNearXZ(x, z, 2), GoalY(y)])` — force pillar-up to reach Y

**Pros:**
- ✅ Graceful degradation — most placements succeed on first try
- ✅ Falls back to Y-targeting only when standard navigation fails
- ✅ Easy to implement (add to the existing `case 'place':` retry loop)
- ✅ No change to successful code path

**Cons:**
- ⚠️ Multiple retries increase per-block latency
- ❌ Still doesn't solve the confinement *itself* — only the consequence
- ❌ GoalY doesn't tell the pathfinder HOW to get to Y=7 from inside

**Verdict: ✅ BEST PRACTICAL CHOICE** — combines well with Phase 1 (canOpenDoors) and future confinement detection.

### Option F: Fix the real issue — add MoveTo exterior before roof phase (C# side)

Instead of changing GoalNear at all, inject an explicit `MoveTo(exteriorDoorPosition)` action in `HtnTaskLibrary.cs` when the build transitions from walls (Y=3) to roof (Y=4 relative). This positions the bot outside the structure before any roof blocks are attempted.

```csharp
// In HtnTaskLibrary, detect Y-layer transition:
if (currentBlockY == originY + 4 && previousBlockY == originY + 3)
{
    // Insert a MoveTo outside the door
    actions.Insert(insertIndex, ActionFactory.Create("MoveTo",
        ("x", originX + 4), ("y", originY), ("z", originZ + 6)));
}
```

**Pros:**
- ✅ Solves the confinement at the root — bot is outside before roof phase starts
- ✅ No adapter changes needed beyond `canOpenDoors` (Phase 1)
- ✅ Works with any tolerance value
- ✅ Predictable and deterministic

**Cons:**
- ❌ Requires hardcoded knowledge of door position (depends on blueprint)
- ❌ Doesn't handle the case where the bot is already inside due to past failures
- ❌ C# change (different repo)

**Verdict: ✅ BEST COMPLEMENTARY FIX** — combined with Option E for robustness.

---

## 4. Recommendation

### Immediate (Phase 1, already done):
- ✅ `canOpenDoors = true` — pathfinder can use doors

### Phase 2 — Adapter-side improvements:

**Primary: Add `GoalPlaceBlock` as the goto target (Option D + E)**

Replace the single `GoalNear(x, y, z, 2)` call at line 897 with a retry chain:

```
1st try:  GoalPlaceBlock(x,y,z, bot.world)        ← precise placement positioning
2nd try:  GoalNear(x, y, z, 2)                     ← fallback: original behavior
3rd try:  GoalCompositeAll([GoalNearXZ(x,z,2), GoalY(y)])  ← force correct Y-level
4th try:  GoalNearXZ(x, z, 2)                      ← survival: get close horizontally
```

The key insight: `GoalPlaceBlock` knows about interaction reach (4.5 blocks) and reference faces. It positions the bot where `placeBlock` will actually succeed — eliminating the tolerance vs. reach mismatch entirely.

**Secondary: Confinement escape** — When ALL goto attempts fail, use simple `isConfined()` heuristic: if >75% of adjacent blocks are solid, attempt to navigate to nearest air block (door/window opening) before retrying.

### Phase 3 — C# planner improvement:

**Insert explicit `MoveTo` before roof phase** — Have `HtnTaskLibrary` detect the Y=3→Y=4 transition and insert a `MoveTo` action that positions the bot at the exterior door coordinates. This prevents the confinement issue entirely for the standard build flow.

---

## 5. Risk Assessment of Changes

| Change | Risk | Mitigation |
|--------|------|------------|
| GoalPlaceBlock (new goal) | Medium — untested in this codebase | Wrap in try-catch with GoalNear fallback |
| GoalCompositeAll | Low — standard mineflayer-pathfinder API | Test with mock world first |
| confiment escape | Low — only activates after all retries fail | Conservative threshold (≥6/8 solid) |
| MoveTo before roof | Low — C# planner change with zero adapter impact | Blueprint-specific door position lookup |

---

## 6. Summary

**Don't touch `GoalNear` tolerance.** The reverted Sprint 42 change (3→2) was correct — higher tolerance makes reachability worse, not better. The real fix is:

1. ✅ Already done: `canOpenDoors = true`  
2. **Adopt `GoalPlaceBlock`** as the primary goto goal for placement actions — it's purpose-built for this  
3. **Retry chain** with `GoalCompositeAll([GoalNearXZ, GoalY])` as fallback to force Y-level  
4. **Confinement escape** as last resort  
5. **C# MoveTo before roof** to prevent confinement entirely
