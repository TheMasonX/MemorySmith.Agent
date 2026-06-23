# Sprint 41 — Dig Crash Fixes (2026-06-22)

## Bugs Fixed

### P0: `point.minus is not a function` — Every `bot.dig()` crashes

**Root Cause:** `toVec3()` returned a custom object with only `.floored()` and `.offset()`. Mineflayer's `bot.dig(block)` internally calls `block.position.minus(otherVec)` to compute the dig direction vector. Without `.minus()`, every dig crashed.

**Impact:** Zero blocks could ever be mined. The dig failure counter hit `MAX_DIG_FAILURES=3` on every block, the adapter skipped all blocks, sent `mineComplete` with `mined=0`, and the C# governor detected stall (3 identical plans with zero inventory change). The graduated auto-retry loop kicked in but produced the same plan.

**Fix:** Added full Vec3 API to `toVec3()`:
- `.minus(other)` — returns new `toVec3(this.x - other.x, ...)`
- `.plus(other)` — returns new `toVec3(this.x + other.x, ...)`
- `.distanceTo(other)` — Euclidean distance
- `.equals(other)` — coordinate comparison
- `.toString()` — `"(x, y, z)"` format
- `.clone()` — returns new `toVec3`

**Files changed:** `MineflayerAdapter/index.js` — `toVec3()` function (lines ~170-185)

### P0: Infinite dig loop when all nearby blocks are exhausted

**Root Cause:** After `MAX_DIG_FAILURES` (3) consecutive dig failures at a position, the skip logic logged "skipping block" and `continue`d the while loop. But `findBestBlock()` didn't track which positions were exhausted — it kept returning the same blocked position, creating an infinite loop.

**Impact:** The mine action never completed. No `mineComplete` or `blockMined` events were sent. The C# 30s action timeout eventually swept the pending action, but the adapter process accumulated orphaned mine operations.

**Fix:** Three changes to `findBestBlock()`:
1. Moved `digFailures` Map declaration **before** `findBestBlock()` so it's accessible in the closure
2. Added `isDigExhausted(pos)` helper: returns true when `(digFailures.get(key) ?? 0) >= MAX_DIG_FAILURES`
3. Added `excludeExhausted(candidates)` filter applied to all three passes (same-Y, nearby-Y, fallback)
4. When all passes return empty (all positions exhausted), returns `null` → triggers `blockNotFound` event → mine loop exits cleanly

**Files changed:** `MineflayerAdapter/index.js` — `findBestBlock()` function and mine loop

### Vec3 Extracted to Standalone Module

`toVec3()` moved from inline in `index.js` to `MineflayerAdapter/vec3.js` as a proper ESM export. Implements the **complete prismarine-vector Vec3 API** — all 46 methods verified.

**Impact:** Any code path that creates or receives a Vec3 now gets the full method set. No more "X is not a function" crashes from missing Vec3 methods.

### Non-Vec3 Interop Risk: `placeBlock` Face Vector

Confirmed from Mineflayer source (`generic_place.js`):
- `refBlock.position.plus(faceVec)` — calls `.plus()` on refBlock (real Vec3), faceVec only provides `.x/.y/.z`
- `vectorToDirection(faceVec)` — only reads `.x/.y/.z`

**Risk:** LOW today — no methods called on the face vector. But if a future Mineflayer version calls `.floored()` or `.clone()` on it, it would crash the same way `bot.dig()` did.

**Fix:** Wrapped the face vector in `toVec3(fx, fy, fz)` at index.js line 831.

### `\n` Literal in Structured Logs

**Root cause:** `appendFileSync` used `'\\n'` (literal backslash-n) instead of `'\n'` (real newline). Log file entries were all on one line separated by literal `\n` text.

**Fix:** Changed `'\\n'` to `'\n'` at index.js line 133.

### `blockTargetPos is not defined` ReferenceError

**Root cause:** `const blockTargetPos = {...}` was declared INSIDE the while loop body. In JavaScript, `const` is block-scoped — not accessible outside the `{}` block. The `mineComplete` event outside the loop referenced `blockTargetPos.bx`, causing a `ReferenceError` after the loop completed.

**Impact:** After a goal completed, the emergency StopNow fired while a mine action was still in-flight. The mine loop exited (via `return` from _stopRequested check BEFORE blockTargetPos was declared in that iteration), then the post-loop `mineComplete` event referenced an undefined `blockTargetPos`, sending a `ReferenceError` to C#.

**Fix:** Declared `blockTargetPos` with `let` outside the while loop, reassigned inside each iteration.

## Remaining P1 Tasks (not addressed)

| Task | Description | Priority |
|------|-------------|----------|
| TSK-0062 | Add `goto()` timeout with `Promise.race()` | P1 High |
| TSK-0070 | Correct `path_reset` → `path_update` | P1 High |
| TSK-0067 | Stale-inventory guard at goal-creation time | P1 High |

## Wiki Memory Updated

- `agent-mineflayer-adapter-state.json` — Lesson #2 expanded (Vec3 API requirements + standalone module), Lesson #4 added (findBestBlock exhaustion), Non-Vec3 Interop Risks section added (placeBlock face vector)

## Key Files

- `MineflayerAdapter/vec3.js` — **New** standalone Vec3 module (46 methods)
- `MineflayerAdapter/index.js` — imports from `./vec3.js`, `placeBlock` face vector wrapped in `toVec3()`
- `Data/Memories/Core/agent-mineflayer-adapter-state.json` — wiki memory
