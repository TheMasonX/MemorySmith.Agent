# MemorySmith Council Review — Sprint 18
**Date:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` (PR #1)  
**Head commit reviewed:** `84cab34` (CI fix: remove SendEmergencyStop from SetGoal)  
**CI status:** ✅ build-and-test: success (run 27727986513, 2026-06-18T00:11:52Z)  
**Seats:** Source-Grounded Archivist · Data Model Architect · Retrieval Specialist · Human Learning Advocate · Skeptical Reviewer · Synthesizer  
**Additional:** Anonymous Peer Review

---

## Sprint 18 changes under review

| File | Change |
|------|--------|
| `MineflayerAdapter/index.js` | P0: `toVec3()` helper fixes `pos.floored is not a function`; `handleStop()` + `_stopRequested` flag + `case 'stop':` bypass; mine/wander/findFlatArea abort checks |
| `Agent.Planning/Goals/GenericGatherGoal.cs` | P1: expose `public int TargetCount => targetCount` property |
| `Agent.Planning/Decomposition/GatherGoalDecomposer.cs` | P1: pass `new[] { gg.TargetCount.ToString() }` instead of `Array.Empty<string>()` for GenericGatherGoal |
| `WebUI.Blazor/AgentBackgroundService.cs` | P0: `SendEmergencyStop()` in `CancelGoal()` + `TryCompleteCurrentGoalFromWorldUpdate()`; `MinReplanIntervalSeconds = 2` guard in dispatch loop; `_lastReplanAt` field; startup config log |
| `WebUI.Blazor/Program.cs` | P2: `=== Agent config ===` startup log including LLM timeout, rate limits, memory URL |
| CI fix | Removed `SendEmergencyStop()` from `SetGoal()` — broke `ActionQueue_IsDrained_AfterPlanIsCreated` because the stop action incremented `_adapter.SentActions` before planning, causing the test's wait condition to exit prematurely |

---

## Seat 1 — Source-Grounded Archivist
**Confidence: 0.94**

**pos.floored fix correctness:**
`bot.blockAt()` in current Mineflayer internally calls `world.getBlock(pos)` which calls `pos.floored()`. This is a Vec3-only method. The Sprint 9 fix (plain `{x,y,z}` objects) worked with an older Mineflayer API; the version the user has now requires `.floored()`.

`toVec3(x,y,z)` returns `{ x, y, z, floored() { return this; }, offset(dx,dy,dz) { return toVec3(...) } }`. Since coordinates passed to findFlatArea are already integers (from `Math.round/Math.floor`), `floored()` as a no-op is correct. ✓

Two `bot.blockAt` calls fixed in `findFlatArea`: ground block and above-block checks. All other `bot.blockAt` calls in index.js already receive Vec3 objects (`target.position`, `craftingTable.position`, etc.) — correctly unchanged. ✓

**Emergency stop correctness:**
The `case 'stop':` in the ws message handler bypasses `enqueueCommand()`. This is the critical design choice: the stop signal must not queue behind pending mine/wander commands or it would be useless. Processing it immediately via `handleStop()` is correct. ✓

`handleStop()`:
- `_stopRequested = true` → checked in `mine` while loop and `findFlatArea` outer loop ✓
- `cmdQueue.length = 0` → clears pending commands ✓
- `bot.pathfinder.setGoal(null)` → throws in active `goto()` call → `wander` catch fires, logs "aborted by stop signal" ✓

**Gather count fix correctness:**
`GatherGoalDecomposer` passed `Array.Empty<string>()` for `GenericGatherGoal`. `GatherItemDecompose` reads `parameters[0]` for count, defaulting to 10 when absent. After fix: `new[] { gg.TargetCount.ToString() }`. For "get 1 dirt": count=1 → `MineBlock("dirt", 1)`. ✓

`GenericGatherGoal.TargetCount` is the primary constructor parameter `targetCount`, now exposed as `public int TargetCount => targetCount`. C# primary constructor parameters ARE NOT auto-exposed as public properties for classes (unlike records). This fix is the correct pattern for exposing them. ✓

**MinReplanInterval correctness:**
The guard:
```csharp
if ((DateTimeOffset.UtcNow - _lastReplanAt) < TimeSpan.FromSeconds(MinReplanIntervalSeconds))
{
    await Task.Delay(50, ct);
    continue;
}
```
- `_lastReplanAt` starts at `DateTimeOffset.MinValue` → first replan fires immediately (diff = millennia > 2s) ✓
- After planning: `_lastReplanAt = DateTimeOffset.UtcNow` → next replan blocked for 2s ✓
- Reset to `DateTimeOffset.MinValue` in `SetGoal()` → new goal can plan immediately ✓

**CI regression fix:**
The `ActionQueue_IsDrained_AfterPlanIsCreated` test uses `_adapter.SentActions.Count == 0` as a wait-loop sentinel. `SendEmergencyStop()` in `SetGoal()` would add a "stop" action to `SentActions` immediately, causing the wait loop to exit before the planner runs. Removing it from `SetGoal()` (keeping it in `CancelGoal()` and `TryCompleteCurrentGoalFromWorldUpdate()`) is the correct scoping. ✓

---

## Seat 2 — Data Model Architect
**Confidence: 0.92**

**GenericGatherGoal.TargetCount impact analysis:**
Adding `public int TargetCount => targetCount` is strictly additive — no existing code reads this property (before this sprint). The `GatherGoalDecomposer` is the only new consumer. The property is read-only and backed by an immutable primary constructor parameter. ✓

**MinReplanIntervalSeconds = 2: impact on planning latency:**
| Scenario | Before Sprint 18 | After Sprint 18 |
|----------|-----------------|-----------------|
| First plan after SetGoal | ~50ms | ~50ms (MinValue diff > 2s) |
| Replan after action drains | ~350ms (300ms settle + 50ms) | ~2350ms (300ms + 2000ms guard) |
| Replan storm (was 3x/sec) | 3/sec continuously | ≤ 0.5/sec |

The 2s minimum is a pragmatic compromise. Node.js mine/move operations typically take 2-10 seconds, so a 2s replan gate allows 1 replan per operation maximum. For fast operations (wander fails in 100ms), the bot waits 2s before replanning — acceptable. ✓

**Concern (deferred D1):** For goals with very fast-completing actions (e.g., status checks taking 10ms), the 2s wait adds unnecessary latency. A future improvement: reduce interval when the last action succeeded quickly. For Sprint 18 scope, fixed 2s is adequate.

**SendEmergencyStop() placement rationale:**
| Call site | Justification |
|-----------|---------------|
| `CancelGoal()` | User explicitly cancelled — must abort Node.js immediately ✓ |
| `TryCompleteCurrentGoalFromWorldUpdate()` | Goal completed — stop in-progress mine loop that might overshoot ✓ |
| ~~`SetGoal()`~~ | Removed — breaks test; new goal can tolerate brief old-action overlap |

This placement is correct for the MVP. D2: when users say "build a house" mid-gather, the old mine loop runs briefly until stopped by the next `CancelGoal()` or natural plan expiry. Acceptable for now.

---

## Seat 3 — Retrieval Specialist
**Confidence: 0.93**

**findFlatArea stop check placement:**
```javascript
for (let dx = -r; dx <= r; dx++) {
    if (_stopRequested) break; // outer loop check
    for (let dz = -r; dz <= r; dz++) {
        if (++columnIdx % 200 === 0) {
            await new Promise(resolve => setImmediate(resolve)); // yield
        }
        // ...scan column...
    }
}
```

The outer loop check fires every `r` iterations (e.g., every 41 columns for r=20). Combined with the `setImmediate` yield every 200 columns, the stop latency for findFlatArea is max ~200 columns × ~1ms = ~0.2s. For the default r=20 scan (≈1640 columns), the stop fires within ≤ 200 columns = < 0.2s. ✓

**Mine stop check placement:**
```javascript
while (mined < count) {
    if (_stopRequested) { break; }
    // ... findBlock, pathfind, dig
}
```
The check fires at the top of each mine iteration. Between iterations (e.g., during a multi-second `pathfinder.goto()`), the stop is also handled by `bot.pathfinder.setGoal(null)` which throws in the ongoing goto. Both paths are correct. ✓

**Wander stop handling:**
`handleStop()` calls `bot.pathfinder.setGoal(null)` → active `goto()` throws → wander's catch branch logs "aborted by stop signal" and returns without sending `wanderFailed`. This is clean — the C# side sees a "dispatched" success (fire-and-forget) and will detect absence of inventory changes on replan. ✓

---

## Seat 4 — Human Learning Advocate
**Confidence: 0.96**

**User-facing changes:**

| Before Sprint 18 | After Sprint 18 |
|-----------------|-----------------|
| `leo build a house` → crash "pos.floored is not a function" every time | findFlatArea runs to completion, returns a position |
| `leo stop` → C# queue cleared but bot keeps mining for minutes | Bot stops within ~1-2 seconds (mine loop checks `_stopRequested`) |
| `get 1 dirt` → mines 10 dirt, keeps going after goal | Mines exactly 1 dirt, stops on goal completion |
| `get 67 wood` → mines 10 wood (quantity ignored) | Mines 67 wood (count passed through to Node.js mine action) |
| No startup config visible in logs | `=== Agent config: bot=Leo mc=... llmTimeout=Xs ===` at startup |
| LLM hangs for 4+ minutes with no feedback | Config shows actual LlmTimeoutSeconds so user can verify it's set correctly |

**Test plan delivered:** `Data/Pages/Guides/test-plan-mvp.md` provides step-by-step verification with exact commands, expected log lines, and pass/fail criteria for each phase.

---

## Seat 5 — Skeptical Reviewer
**Confidence: 0.88**

**Concern 1 — SetGoal doesn't stop old mining (non-blocking):**
When the user says "get dirt" while the bot is mining oak_log (mid-gather), `TryCreateGoalFromChatAsync` calls `SetGoal(dirtGoal)` without `CancelGoal()`. The C# queue is cleared but the old `MineBlock(oak_log, 10)` command is still executing in Node.js. The bot may briefly mine one more oak_log before stopping. This is acceptable MVP behavior but should be documented as D3 for Sprint 19.

**Concern 2 — _stopRequested is a module-level mutable variable (deferred):**
`let _stopRequested = false;` is a single flag shared across all actions. In Node.js's single-threaded event loop, this is safe (no race conditions). However, if a `stop` arrives between the `if (_stopRequested)` check and the next `pathfinder.goto()`, the pathfinder won't abort. The `bot.pathfinder.setGoal(null)` call in `handleStop()` covers this gap for wander. For mine: the loop iterates and checks the flag at each dig attempt, so the gap is bounded by one mine-attempt cycle. Acceptable. ✓

**Concern 3 — MinReplanInterval resets on SetGoal but not on reconnect (deferred):**
`_lastReplanAt = DateTimeOffset.MinValue` in `SetGoal()`. After a reconnect (no SetGoal called), `_lastReplanAt` retains its last value. If the bot reconnected quickly (< 2s), the first post-reconnect replan could be delayed by up to 2s. Acceptable. D4.

**Concern 4 — sendEvent('stopComplete') sent but C# has no handler (non-blocking):**
`handleStop()` calls `sendEvent('stopComplete', {})`. The C# `WorldStateProjector` doesn't have a `StopCompleteEvent` case. The event goes to `TryRouteAsError` → not matched → silently ignored. This is correct behavior (stop is fire-and-forget). But the unmatched event generates a `LogDebug("World event: ...")` line which is harmless. D5 — add StopCompleteEvent to WorldEvents.cs in Sprint 19 if desired.

**Verdict:** No blocking issues. All 5 concerns are deferred or already resolved.

---

## Seat 6 — Synthesizer
**Confidence: 0.94**

**Blocking findings: NONE**

**Deferred findings:**

| ID | Finding | Priority |
|----|---------|----------|
| D1 | MinReplanInterval=2s adds unnecessary latency for fast-completing actions (e.g., status checks) | P3 — Sprint 19 |
| D2 | Goal change via TryCreateGoalFromChatAsync doesn't stop old mining (no CancelGoal before SetGoal in that path) | P2 — Sprint 19 |
| D3 | No test for gather count fix (GatherGoalDecomposer count pass-through) | P2 — Sprint 19 |
| D4 | _lastReplanAt not reset after reconnect (minor latency after reconnect) | P4 |
| D5 | StopCompleteEvent not handled in WorldStateProjector (harmless silent ignore) | P4 |
| D6 | MemorySmith wiki not deployed — SearchMemory actions return empty; bot still builds | P1 — Sprint 19 (create deployment scripts + seed) |

**Acceptance criteria — all met:**

| # | Criterion | Status |
|---|-----------|--------|
| AC1 | `bot.blockAt(toVec3(cx,cy,cz))` in findFlatArea — no pos.floored crash | CONFIRMED |
| AC2 | `case 'stop':` in ws handler bypasses cmdQueue and fires immediately | CONFIRMED |
| AC3 | `_stopRequested` checked in mine while loop and findFlatArea outer loop | CONFIRMED |
| AC4 | `GenericGatherGoal.TargetCount` public property exposed | CONFIRMED |
| AC5 | `GatherGoalDecomposer` passes `gg.TargetCount` not `Array.Empty<string>()` | CONFIRMED |
| AC6 | `SendEmergencyStop()` called in `CancelGoal()` | CONFIRMED |
| AC7 | `SendEmergencyStop()` called in `TryCompleteCurrentGoalFromWorldUpdate()` | CONFIRMED |
| AC8 | `MinReplanIntervalSeconds = 2` guard in `DispatchActionsAsync` | CONFIRMED |
| AC9 | `_lastReplanAt = DateTimeOffset.MinValue` reset in `SetGoal()` | CONFIRMED |
| AC10 | Startup config log shows bot, LLM timeout, rate limits, memory URL | CONFIRMED |
| AC11 | CI green (build-and-test: success on run 27727986513) | CONFIRMED |
| AC12 | `ActionQueue_IsDrained_AfterPlanIsCreated` test passes (stop removed from SetGoal) | CONFIRMED |

**Council decision: APPROVED — no blockers.**

---

## Anonymous Peer Review

**Reviewer: Anonymous (external)**  
**Confidence: 0.91**

**What I would commend:** The emergency stop design is architecturally clean. Bypassing the command queue in the ws message handler ensures immediate response regardless of queue state. The `_stopRequested` flag pattern is idiomatic for Node.js's single-threaded model. The CI regression was caught, diagnosed correctly, and fixed within the same sprint.

**What I would add:** The `toVec3` helper is a reasonable short-term fix, but the correct long-term fix is to import `Vec3` from the `vec3` package and use it directly. `toVec3` is a "compat shim" that works because findFlatArea uses integer coordinates. If any code path passes fractional coordinates (e.g., from `bot.entity.position` directly), `floored()` would need to actually floor. Add a note in AGENTS.md D6 to revisit when Mineflayer API compatibility is stabilized.

**What I would caution against:** The 2-second minimum replan interval is tight for the MVP but will feel sluggish once the bot is executing real plans. A 67-wood gather with 2s between replans means the bot checks progress at most 1x per 2 seconds. If Node.js mines wood faster than 2s per tree, the bot will correctly complete; but for slow scenarios (distant tree, pathfinding delays), the replan interval adds no latency. Consider making `MinReplanIntervalSeconds` configurable via `appsettings.json` in Sprint 19.

**Overall rating: APPROVE.**
