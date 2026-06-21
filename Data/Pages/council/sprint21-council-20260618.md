# Sprint 21 Council Review — Inventory Freshness Gate, Governor Pre-Plan Check, Truncation Recovery

**Date:** 2026-06-18
**Reviewer Council:** 6-seat MemorySmith review panel
**Subject:** Sprint 21 — P0-A IsInventoryStale gate, P0-B governor pre-plan check, P0-C doc fix, P1-A/B/C/D
**CI status at review:** GREEN — b0bf0f23944a4e58d024c5843e586282c1a80158 (13 commits, all tests passing)
**Deferred from Sprint 20:** B-1 (stale inventory gate — now P0-A DELIVERED), D-2 test (now P1-B DELIVERED)

---

## Changes Under Review

| Area | File | Change |
|------|------|--------|
| P0-A | `Agent.Core/Models/WorldState.cs` | Add `IsInventoryStale` bool property + `SetInventoryStale` builder method |
| P0-A | `Agent.Core/WorldStateProjector.cs` | `ApplyStatus`: add `b.SetInventoryStale(false)` to clear stale flag on StatusEvent |
| P0-A | `Agent.Planning/Goals/GenericGatherGoal.cs` | `IsComplete`: return false when `state.IsInventoryStale` |
| P0-A | `WebUI.Blazor/AgentBackgroundService.cs` | `SetGoal`: mark `_worldState.IsInventoryStale = true` |
| P0-B | `WebUI.Blazor/AgentBackgroundService.cs` | `DispatchActionsAsync`: check `replanGovernor?.IsStalled` BEFORE `PlanAsync`; delay 10s when stalled |
| P0-C | `AGENTS.md` | Remove false "inventory freshness gate" from Sprint 20 completed list |
| P1-A | `WebUI.Blazor/AgentBackgroundService.cs` | Progress log: `LogDebug` → `LogInformation` |
| P1-B | `MemorySmith.Agent.Tests/Sprint21Tests.cs` | `Sprint21BlockNotFoundFactTests`: 4 tests verifying D-2 fact producer |
| P1-C | `Agent.Planning/LlmChatInterpreter.cs` | `TryParseTruncatedJson`: extract item/count for gather, blueprint for build |
| P1-D | `MineflayerAdapter/index.js` | `^Cleared\s+\S+` → `^Cleared\s+(?:\d+|\S+'s\|the\s+inventory)` |
| P1-D | `MemorySmith.Agent.Tests/Sprint20Tests.cs` | Update pattern + remove stale `Assert.Ignore` for Cleared edge case |
| Tests | `MemorySmith.Agent.Tests/Sprint21Tests.cs` | 20 new tests across 4 fixtures |

---

## Seat 1: Source-Grounded Archivist (Confidence: 91%)

All factual claims verified against the CI-green commit.

**P0-A correctness:** The data flow is clean and complete:
- `AgentBackgroundService.SetGoal` → `_worldState = _worldState.With(b => b.SetInventoryStale(true))`
- `WorldStateProjector.ApplyStatus` → `b.SetInventoryStale(false)` (only StatusEvent resets it)
- `GenericGatherGoal.IsComplete` → `if (state.IsInventoryStale) return false`

This means GetStatus (emitted at the END of every gather plan) will always clear the flag before the second-cycle IsComplete check. The happy path:
1. SetGoal → stale=true
2. Plan emitted: [SearchMemory, MineBlock, GetStatus]
3. GetStatus dispatch → StatusEvent arrives → stale=false
4. Next cycle: IsComplete checks fresh inventory → true if enough items

The key question was whether the flag would be cleared BEFORE IsComplete is checked. Confirmed: the settle block at the end of cycle 1 waits 300ms for events (including StatusEvent from GetStatus). By the time cycle 2 starts, `ApplyStatus` has already cleared the stale flag.

**One gap found:** `GenericGatherGoal.IsComplete` is called in TWO places in `DispatchActionsAsync`: (1) the goal-complete check at the top of the planning block, and (2) `TryCompleteCurrentGoalFromWorldUpdate` called from `ProcessEventsAsync`. Both use `_worldState`, so the stale flag propagates to both paths correctly. Confirmed.

**Finding A-1 (DEFERRED):** The sprint required 3 post-push fix commits (brace fix, missing using, verbatim backslash). All three were in Sprint21Tests.cs and LlmChatInterpreter.cs. Rule E-1 compliance was maintained (paramsFile used throughout). However, the test file could have been pre-validated locally. Future sessions should consider a local build sanity check before pushing.

---

## Seat 2: Data Model Architect (Confidence: 89%)

**WorldState.IsInventoryStale design:** Clean. The property is:
- `init`-only (immutable record semantics)
- Defaults to `false` (no behavior change for existing code that doesn't call SetGoal)
- Not written by any event EXCEPT StatusEvent (correct — only GetStatus resets freshness)
- Not persisted in Facts dictionary (correct — this is a transient runtime flag, not a durable fact)

**Builder pattern correctness:** `SetInventoryStale(bool stale)` returns `Builder` for chaining. The `_state = _state with { IsInventoryStale = stale }` is correct immutable-record update.

**P0-B governor architecture:** The pre-plan check `if (replanGovernor?.IsStalled == true)` is placed correctly:
- BEFORE the MinReplanInterval check
- BEFORE the `PlanAsync` call
- Inside the `_queue.IsEmpty && _currentGoal is not null && !_actionDispatchedThisCycle` block

This means stall-detection logic executes in this order during a stall:
1. Cycle N: `PlanAsync` → `Evaluate(fingerprint)` → `Stalled` → log + `continue`
2. Cycle N+1: `IsStalled == true` → delay 10s → `continue` (PlanAsync NOT called)
3. (60s later) Auto-recovery: `IsStalled` becomes false → normal path resumes

The 10s delay uses `ct` cancellation, so a `CancelGoal` during STALL will interrupt it cleanly. Good.

**Finding B-1 (DEFERRED):** The `_lastReplanAt = DateTimeOffset.UtcNow` is set in the IsStalled delay path. This means after the 10s stall delay, when IsStalled clears (via RecordProgress or timeout), the MinReplanInterval check would immediately block for 2s before allowing PlanAsync. Slightly redundant but harmless — the 2s wait after a 10s stall is imperceptible.

---

## Seat 3: Retrieval Specialist (Confidence: 86%)

**P1-C TryParseTruncatedJson extension:**

The new gather/build branches are clean:
```csharp
case "gather":
{
    var itemM  = Regex.Match(json, @"""item""\s*:\s*""(?<v>[^""]+)""",  RegexOptions.IgnoreCase);
    var countM = Regex.Match(json, @"""count""\s*:\s*(?<v>\d+)",         RegexOptions.IgnoreCase);
    if (itemM.Success)
    {
        goalName   = $"GatherItem:{itemM.Groups["v"].Value}";
        var cnt    = countM.Success && int.TryParse(countM.Groups["v"].Value, out var c) ? c : 10;
        parameters = new Dictionary<string, object?> { ["count"] = cnt };
    }
    break;
}
```

The `goalName is not null ? ChatIntentType.CreateGoal : ChatIntentType.Unknown` intent-type selection correctly handles the case where a gather intent is present but item extraction failed (truncated before the item field).

**Regression check:** The original `TryParseTruncatedJson` tests (Sprint20LlmTruncationTests) all pass in CI. The responseM regex was restored byte-for-byte from the original. ✓

**Finding C-1 (DEFERRED, carried from Sprint 20):** `TryParseTruncatedJson` still does not handle `navigate` intent (extract x/y/z coordinates from partial JSON). This is a niche case (most navigate commands use deterministic pattern matching). Defer to Sprint 22.

---

## Seat 4: Human Learning Advocate (Confidence: 92%)

This sprint directly addresses the three most operator-visible failures from the Sprint 20 runtime session.

**P0-A: Stale inventory false-completion** — FIXED. After `/clear`, new gather goals no longer instantly complete. The operator will see the bot correctly plan a gather sequence instead of claiming immediate success.

**P0-B: STALL semantics** — IMPROVED. Previously operators saw `[governor] STALLED` in the logs but then immediately saw `[plan] Build:small-house: 18 actions [...]` 2 seconds later, which was contradictory. Now during STALL, no plan logs appear for 10s intervals. Much clearer.

**P1-A: Recovery log elevation** — DONE. Operators watching console will now see `[governor] progress detected — inventory Σ N→M` when the stall clears. Previously this was file-only. High value for real-time monitoring.

**P1-D: Cleared pattern tightened** — DONE. "Cleared out the chest for you" (player message) will no longer be filtered as a system message.

One small observation: `GenericGatherGoal.IsComplete` returns false silently when `IsInventoryStale`. The `LogDebug` call suggested in Seat 4's pre-implementation council note was NOT included. Low impact (it's a debug-level log), but operators might find it useful to confirm the gate fired.

**Finding D-1 (DEFERRED):** Add `logger.LogDebug("[goal] {Goal} IsComplete skipped — inventory stale (awaiting GetStatus)")` to GenericGatherGoal.IsComplete. However, GenericGatherGoal has no logger access (it's a plain model class). The log would need to be added in DispatchActionsAsync or TryCompleteCurrentGoalFromWorldUpdate at the IsComplete call site. Sprint 22 if desired.

---

## Seat 5: Skeptical Reviewer (Confidence: 79%)

**P0-A test coverage:** The 6 freshness gate tests are comprehensive:
- `GatherGoal_WithStaleInventory_DoesNotFalseComplete` — core failing test ✓
- `GatherGoal_WithFreshInventory_CompletesNormally` — non-regression ✓
- `GatherGoal_DefaultWorldState_IsNotStale` — default behavior ✓
- `WorldState_SetInventoryStale_RoundTrips` — builder correctness ✓
- `WorldStateProjector_ApplyStatus_ClearsStaleness` — projector integration ✓
- `GatherGoal_RequiresSmelting_AlsoRespectsStaleness` — smeltable items ✓

One gap: no test verifying that `AgentBackgroundService.SetGoal` actually sets `_worldState.IsInventoryStale = true`. This is an ABS integration path not covered by any test. The Sprint21FreshnessGateTests test `WorldStateProjector_ApplyStatus_ClearsStaleness` directly but doesn't go through ABS. Low risk (the code is one-line and obvious), but worth noting.

**P0-B test coverage:** `StalledGovernor_BlocksPlanAsync_BeforeItIsEvenCalled` tests the intended behavior correctly. The `AlwaysStalledGovernor` returns `IsStalled=true` immediately, which exercises the pre-check path.

**Process concern:** Three fix commits were needed after the initial push. Cause: Python string escaping in verbatim C# regex patterns. Rule E-1 prevents agent intermediaries from corrupting verbatim strings during reads, but it doesn't prevent the AUTHOR from getting the string right the first time. The LlmChatInterpreter change was high-risk specifically because it involved modifying the method that contains the verbatim regex patterns. Future sessions should use the fetch-and-patch-from-raw approach (as was eventually used for the responseM fix) rather than writing Python string literals for C# verbatim content.

**Finding E-1 (DEFERRED):** Add a AGENTS.md note: when modifying methods that contain verbatim string regex patterns, always fetch the original bytes, patch in Python using raw bytes (not string literals), and verify the bytes match before pushing.

---

## Seat 6: Synthesizer (Overall Confidence: 87%)

### Sprint 21 delivers the primary sprint goal

The inventory freshness gate (P0-A) is the most impactful single fix of Sprint 21. The data flow is correct, the tests are comprehensive, and CI confirms all 20 new tests pass alongside the existing test suite.

P0-B (governor pre-plan check) correctly addresses the RC-2 audit finding: PlanAsync is no longer called every 2s during STALL.

### Blocking findings: none

No blocking findings from any seat. All concerns are deferred.

### Deferred findings

| ID | Finding | Seat | Priority |
|----|---------|------|---------|
| A-1 | Future sessions: pre-validate tests locally before pushing | 1 | Process |
| B-1 | Post-stall _lastReplanAt causes extra 2s MinReplanInterval delay | 2 | Low |
| C-1 | TryParseTruncatedJson: navigate intent not handled | 3 | Low |
| D-1 | GenericGatherGoal.IsComplete: add staleness debug log at ABS call site | 4 | Low |
| E-1 | AGENTS.md: document verbatim-string fetch-and-patch pattern for future modifications | 5 | Medium |

### Acceptance criteria verification

| # | Criterion | Status |
|---|-----------|--------|
| T1 | After admin /clear, gather goal does NOT false-complete | VERIFIED (Sprint21FreshnessGateTests) |
| T2 | Fresh inventory (after GetStatus) allows IsComplete to complete | VERIFIED (Sprint21FreshnessGateTests) |
| T3 | Default WorldState.IsInventoryStale = false | VERIFIED (Sprint21FreshnessGateTests) |
| T4 | ApplyStatus clears IsInventoryStale | VERIFIED (Sprint21FreshnessGateTests) |
| T5 | During STALL, PlanAsync not called | VERIFIED (Sprint21GovernorPrePlanTests) |
| T6 | Non-stalled governor allows PlanAsync | VERIFIED (Sprint21GovernorPrePlanTests) |
| T7 | BlockNotFoundEvent sets event:BlockNotFound:Block | VERIFIED (Sprint21BlockNotFoundFactTests) |
| T8 | Wander inserted when BlockNotFound fact matches spec | VERIFIED (Sprint21BlockNotFoundFactTests) |
| T9 | No Wander on first gather attempt | VERIFIED (Sprint21BlockNotFoundFactTests) |
| T10 | Truncated gather JSON extracts item name | VERIFIED (Sprint21TruncatedJsonGatherTests) |
| T11 | Truncated build JSON extracts blueprint | VERIFIED (Sprint21TruncatedJsonGatherTests) |
| T12 | Existing Sprint20 truncation tests still pass | VERIFIED (Sprint20LlmTruncationTests) |

---

## Council Verdict

**APPROVED**

No blocking findings. Sprint 21 is complete and CI-green. Proceed to handoff.

Deferred items (D-1, E-1) are low-medium priority and do not block Sprint 22.

---

*Council review conducted per MemorySmith Agent development protocol.*
*CI verification commit: b0bf0f23944a4e58d024c5843e586282c1a80158*
