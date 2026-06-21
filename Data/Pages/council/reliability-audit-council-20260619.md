# Reliability Audit Council Review

**Topic**: External Reliability Report — Action lifecycle, default drift, adapter resilience  
**Source**: `Data/Pages/council/reliability-audit-pre-council-20260619.md`  
**Codebase ref**: `sprint-5-tool-safety` head `2fe07082a5` (Sprint 23 complete, v0.23.0)  
**Date**: 2026-06-19  
**Council seats**: 5 named + 2 anonymous peer reviewers  
**Verdict**: **APPROVED with 2 blocking findings** (B-1, B-2 — quick corrections; D-1 through D-5 deferred to Sprint 24+)  
**Average confidence**: 87%

---

## Pre-council summary

The external audit reviewed the sprint-5-tool-safety branch (current head Sprint 23 complete) and identified five reliability risk categories: false progress (dispatch ≠ world completion), default drift across C# and JS layers, LLM blocking the world-event loop, adapter brittleness (no reconnection), and completion ambiguity for long-running actions. The audit praises the chat pipeline, tool schema validation, and flat-area scanner as solid foundations, then recommends six engineering priorities ordered by reliability impact.

**Codebase state at time of review**: Sprint 23 complete — damage interrupt with per-goal override, World KB routing, health rate-limit guard, 15 new tests, v0.23.0. Several audit concerns have been partially addressed by Sprints 5–23 (per-action timeout Sprint 5, LLM timeout Sprint 11, Serilog + governor Sprints 19–21, inventory freshness Sprints 21–22, damage interrupt Sprint 23). The core lifecycle and default-alignment issues remain open.

---

## Seat 1 — Source-Grounded Archivist

**Confidence**: 88%  
**Role**: Validates each audit claim against actual source at `2fe07082a5`.

### Verified claims (confirmed accurate at Sprint 23 head)

**FindFlatAreaTool defaults are stale** — `Agent.Tools/Tools/FindFlatAreaTool.cs` (SHA `6d5b148`):

```csharp
var radius      = arguments.TryGetProperty("radius",      out var r) ? r.GetInt32() : 20;
var minFlatArea = arguments.TryGetProperty("minFlatArea", out var m) ? m.GetInt32() : 9;
```

The description string reads: *"Use 'radius' (default 20) to control search radius and 'minFlatArea' (default 9, i.e. 3×3) for the minimum number of contiguous flat blocks required."*

The JS adapter `index.js` uses `FLAT_AREA_DEFAULT_RADIUS = 32` and `FLAT_AREA_MIN_SIZE = 25` (set in Sprint 19 per AGENTS.md). **Confirmed default mismatch: C# documents and defaults 20/9; JS runtime uses 32/25.** Any LLM or developer relying on the C# tool description gets wrong values. **Rating: B-1 — Blocking.**

**StatusTool and GetStatusTool are both registered as separate classes** — `Agent.Tools/Tools/StatusTool.cs` (SHA `dacf223`) names itself `"Status"` and dispatches `ActionProtocol.Status`. `Agent.Tools/Tools/GetStatusTool.cs` (SHA `f2634bb`) is commented as a "compatibility alias" but is a fully independent class with identical body dispatching the same `ActionProtocol.Status`. Any change to description, schema, or logging must be manually mirrored. **Rating: B-2 — Blocking.**

**No correlation ID mechanism** — `Agent.Tools/ToolDispatcher.cs` (SHA `2ee78b8`): `CallAsync` returns `ToolResult(bool Success, string Message)`. No correlation ID is generated at dispatch, no pending-action dictionary exists, and no mechanism connects a returned `ToolResult(true)` to a subsequent world event such as `blockMined` or `craftComplete`. The audit claim is accurate. **Rating: D-1 — Deferred, Sprint 24 P0.**

### Claims partially addressed since audit scope

**Per-action timeout**: Sprint 5 added a 30s `CancellationTokenSource` in `DispatchActionsAsync`. This prevents infinite blocking but does not close the loop — an action can timeout at 30s while the world event arrives at 31s and is silently dropped. **Partial mitigation; D-1 remains open.**

**LLM latency**: Sprint 11 added `CancelAfter(options.LlmTimeoutSeconds)` (default 10s). This bounds worst-case delay but does not decouple; a 10s LLM stall still delays health and damage events by up to 10s. **Partial mitigation; D-2 remains open.**

**Flat-area retry**: Sprint 19 updated `DecomposeBuild` to pass `radius=48` on retry. The explicit value bypasses the stale default on the retry path. However, the first attempt (and any LLM-driven invocation) uses the C# default of 20. **B-1 stands; the retry path is correct but the initial-call contract is wrong.**

### Archivist dissent

None. All source-grounded claims in the audit are verified accurate as of Sprint 23. Some concerns are partially mitigated; none are fully resolved.

---

## Seat 2 — Systems Reliability Architect

**Confidence**: 92%  
**Role**: Assesses the action lifecycle gap and proposes a concrete implementation.

### Analysis: the false-progress failure path

The false-progress failure mode (Audit §1) is the most impactful open risk. It is invisible in unit tests because tests mock the world adapter and inject synthetic events. It manifests in live play as follows:

1. Agent dispatches `MineBlock(diamond_ore)`.
2. `ToolDispatcher` returns `ToolResult(true)` — dispatch-only success.
3. `blockMined` world event is delayed or lost (server lag, pathfinder blocked, chunk not loaded).
4. `GenericGatherGoal.IsComplete()` checks inventory — still false.
5. Agent replans with identical mine action.
6. Governor detects 3 identical fingerprints → STALL state, 10s delay.
7. If the event arrives during STALL delay, it applies to world state but `IsComplete` does not re-run until governor exits STALL.

This path exists today and was observed in hands-on sessions (Sprint 20 audit doc: "loop failure"). The governor catches infinite loops but cannot distinguish "event arriving late" from "action genuinely failed."

### Proposed design: Action Correlation IDs

Minimal addition that does not restructure the existing interface:

```csharp
// Agent.Core/PendingAction.cs  (~25 lines)
public sealed record PendingAction(
    Guid CorrelationId, string ToolName, string GoalId, DateTimeOffset DispatchedAt)
{
    public ActionLifecycle State { get; set; } = ActionLifecycle.Dispatched;
}

public enum ActionLifecycle { Dispatched, Acknowledged, Completed, Failed, TimedOut }
```

In `ToolDispatcher.CallAsync`: generate `Guid corrId = Guid.NewGuid()`, inject into `action.Context["correlationId"]` before `SendActionAsync`.

In `AgentBackgroundService`: `ConcurrentDictionary<Guid, PendingAction> _pending`. On dispatch → add. On matching world event → update State → Completed or Failed, remove. On 30s timeout → State = TimedOut, LogWarning, remove. Clear all on `ShutdownAsync`.

In `index.js`: echo `args.correlationId` (or the action-level correlationId field) in every result event payload. This is a single-line addition per event handler.

**Total scope**: ~120 lines C#, ~15 lines JS, 2 new tests. No interface changes required.

### On the blocking findings

**B-1** (FindFlatAreaTool defaults): Must block. The tool description is the LLM's and developer's contract. A factually wrong contract that advertises 20/9 when the runtime uses 32/25 will cause systematically worse first-attempt flat-area searches. Fix is 3 lines C# + 2 description string edits.

**B-2** (StatusTool/GetStatusTool duplicate): Must block. Recommended fix: delete `StatusTool.cs`. In `GetStatusTool`, keep `Name = "GetStatus"`. If backward compatibility for plans that send "Status" is required, add a `ToolDispatcher` alias mapping "Status" → the same `GetStatusTool` instance rather than a full second class. This is a 1-file deletion + 0–2 lines DI change.

### Architect dissent

The correlation ID dictionary must be bounded and cleaned up. Without cleanup on reconnect or shutdown, a session crash leaves dangling entries. Mitigation: implement `ShutdownAsync` on `AgentBackgroundService` that calls `_pending.Clear()` and logs any outstanding entries as TimedOut. This is required before the feature ships.

---

## Seat 3 — World Integration Specialist

**Confidence**: 85%  
**Role**: Focused on adapter resilience, event model, and Node.js integration.

### Open risks

**No reconnection strategy**: `MinecraftAdapter.ConnectAsync` is called once at startup; there is no re-connect loop. Bot disconnects happen regularly in production (server restart, kick, timeout). Failure mode: C# receives no events → governor enters STALL → agent is stuck until process restart. The `DisconnectAsync` SIGTERM/SIGKILL path (Sprint 5) handles clean shutdown but not unplanned disconnects.

**Move event flood**: Every `bot.on('move')` fires a WebSocket message to C#. Mineflayer emits 10–20 move events per second during pathfinding. The C# event loop processes hundreds of position updates per minute during any navigation, most of which are noise. Sprint 19's Serilog at Debug level amplifies this into very dense log files.

**FurnaceTool completion model**: `smelt` is dispatched; C# gets `ToolResult(true)` immediately. The Node handler awaits `furnace.on('update')` internally, but C# has no visibility into this wait. If smelting takes 10s and the WebSocket drops at second 8, C# never receives the completion event, the 30s timeout fires, and the action is marked timed out — even though smelting may have completed in the world. Correlation IDs (D-1) would surface this; adapter reconnection (D-3) would prevent it.

### Addressed since audit scope

- Sprint 5: SIGTERM→5s wait→SIGKILL handles clean adapter termination.
- Sprint 23: Damage interrupt with `SendEmergencyStop + ClearAndEnqueue` handles health-critical pre-emption. This is the closest thing to adapter-level resilience but operates on the C# side, not the WebSocket connection.

### Recommendations

**D-3 — Reconnection (Sprint 25)**: Reconnect loop in `MinecraftAdapter`. On WebSocket `Close` event: wait `ReconnectDelaySeconds` (configurable, default 5), call `ConnectAsync`. Cap at `MaxReconnectAttempts` (default 5, then emit `botDisconnected` fatal event). Emit synthetic `botReconnecting` / `botReconnected` C# events so `AgentBackgroundService` re-enqueues GetStatus on recovery.

**D-5 — Move event throttling (Sprint 24 P2)**: `const MOVE_EMIT_THROTTLE_MS = 250` in `index.js`. Emit at most one position update per 250ms, drop intermediates. Reduces move events from ~15/s to ~4/s with no meaningful loss of positional accuracy for planning.

### Specialist dissent

The audit ranks adapter brittleness as Priority 5. For unattended production use, I would elevate it to Priority 3. A bot that cannot reconnect after a server restart requires a human operator to be on call at all times. Given the current sprint velocity and the higher-impact D-1 work, maintaining Sprint 24 focus on lifecycle is a reasonable trade. Reconnection should be Sprint 25 P0.

---

## Seat 4 — Debuggability & Observability Lead

**Confidence**: 87%  
**Role**: Focused on making the system observable and failure-diagnosable.

### Positive findings (Sprints 6–23)

The sprint history shows genuine observability investment:
- **AgentJournal** (Sprint 6): 11 event types, bounded 1000 entries, `/api/agent/journal` endpoint.
- **Serilog** (Sprint 19): Debug-level file sink, ms precision, action timing via Stopwatch, inventory context on goal set/complete/fail.
- **Damage interrupt logging** (Sprint 23): LogWarning on interrupt trigger, LogDebug on suppression. Distinguishes interrupt from rate-limit.
- **Intent log** (Sprint 11): Logs resolved chat intent post-parse (LLM vs. CraftRegex fast-path).

A developer can trace a failure from chat → intent → goal → plan → action with reasonable fidelity across these logs.

### Remaining gaps

**Gap 1 — No per-action correlation in logs**: When `FindFlatArea(radius=20)` dispatches, the log shows "dispatched." When `flatAreaFound` arrives 3 seconds later, it logs separately. No shared token connects them. In a multi-action log stream, these look like unrelated events. Correlation IDs (D-1) close this gap entirely — each log line for dispatch and completion would carry the same `{CorrelationId}` structured property.

**Gap 2 — Stale tool description actively misleads**: `FindFlatAreaTool`'s description saying "default 20" is the kind of wrong documentation that costs hours to debug. A developer writing a test fixture, a wiki doc, or a prompt template reads "default 20" and silently gets worse results. B-1 is as much an observability fix as a runtime fix.

**Gap 3 — Move event log noise**: At Serilog Debug level, uncapped move events during a 30-second pathfinding sequence produce hundreds of log lines that bury action-level diagnostics. D-5 throttling reduces this to ~120 lines per 30s, which is tractable.

**Gap 4 — No startup constant summary**: The C#/JS default mismatch (B-1) would be caught immediately if agent startup logged the effective values of the 5–6 most important tunable constants at `LogInformation`. Cost: 5 lines. Benefit: makes future mismatches surface in logs automatically without requiring a code review.

### Lead dissent

None on the findings. Recommended addition for Sprint 24: `LogInformation` at agent startup listing `LlmTimeoutSeconds`, `PerActionTimeoutSeconds`, `FlatAreaDefaultRadius`, `FlatAreaMinSize`, `DamageInterruptThresholdHp`, `ReplanGovernorThreshold`. This is a P2 item but high value-to-effort ratio.

---

## Seat 5 — Skeptical Reviewer / Synthesizer

**Confidence**: 82%  
**Role**: Challenges assumptions and synthesizes the final verdict.

### Challenge 1: "Does the governor already solve false progress?"

The governor (Sprints 19–21) uses inventory hash as the progress signal. An action that dispatches and whose world event is lost would not change inventory, so the governor would see 3 identical plan fingerprints and enter STALL. STALL recovery issues GetStatus, which updates world state, and replanning follows.

**Counter**: This works for gather goals. For `FindFlatArea`, inventory never changes regardless of success or failure — the governor STALLs, GetStatus confirms no inventory change, and the planner re-issues FindFlatArea. Functionally correct, but by accident rather than design. For `PlaceBlock`, inventory decreases on success; the governor would see progress. But if a placed block is immediately destroyed (e.g., by another player), inventory has already been credited as consumed — the governor thinks progress was made when the world effect was reversed. Correlation IDs make the lifecycle explicit and testable rather than implicit and coincidental.

### Challenge 2: "Is LLM decoupling urgently needed given the 10s timeout?"

A 10s LLM timeout bounds the worst-case delay on world events during an LLM call. The damage interrupt (Sprint 23) pre-empts health-critical events regardless of LLM state. Health events that arrive during an LLM call are buffered in the WebSocket receive queue and applied after the call returns.

**Counter**: The damage interrupt threshold is 6HP (default). A bot at 7HP + 10s LLM stall + aggressive mob = potential death within the stall window. The 2s health-check cooldown (Sprint 23) further limits passive GetStatus during stalls. A Channel<WorldEvent> consumer with dedicated Task truly decouples this, but the Sprint 23 safeguards have significantly reduced the urgency. Defer to Sprint 25.

### Challenge 3: "Is B-2 (StatusTool/GetStatusTool) really blocking?"

Both tools work. Neither causes test failures or user-facing errors.

**Counter-challenge**: A "compatibility alias" implemented as a full duplicate class is a maintenance trap with zero runtime benefit. Every future change to tool description, schema, or logging must be made twice. The fix (delete one file) is ~5 minutes of work. Given how small the fix is relative to the risk of silent divergence, classify as blocking.

### Synthesis

The audit is accurate, well-sourced, and prioritized correctly. Sprint 23's safety features (damage interrupt, rate-limit guard) have reduced the urgency of several concerns, but the core lifecycle and default-alignment issues are untouched. The right framing for Sprint 24 is "make the existing machinery correct and traceable" rather than "add more features."

The governor catches most stall modes; correlation IDs catch the ones the governor misses and make every catch diagnosable. That is the right next investment.

---

## Anonymous Peer Reviews

### Anonymous Reviewer A — Systems Engineering Perspective

The audit's "dispatch success ≠ world success" observation is the single most important finding. Every reliability mechanism built on the assumption that `ToolResult(true)` means the world changed is suspect.

I agree with Seat 5 that the governor catches most stall cases. But I want to raise one failure mode not covered by the existing review: `IWorldModel.Reconcile` updates `_uncertaintyScore` by comparing predicted vs. actual state after a GetStatus. The WorldModel predictions assume each dispatched action succeeded. If an action dispatches but its world event is lost, the prediction is never reconciled against reality, and `_uncertaintyScore` drifts toward 0 (high confidence) when it should be rising. Correlation IDs would surface this second-order effect: a TimedOut action could trigger a forced GetStatus reconciliation.

**Assessment**: B-1 and B-2 are legitimate Sprint 24 gates. D-1 is the right P0 architectural work. The council review is thorough and source-grounded.

### Anonymous Reviewer B — Test Engineering Perspective

The test suite at 200+ is a solid base. The seam tests (schema validation, governor state machine, world state projection) cover contracts well. But the audit is right that no test exercises the full chain from chat message to world event projection.

Concrete gap: no test exists that (1) creates a fake `IWorldAdapter`, (2) sends a `chatMessage` event to `AgentBackgroundService`, (3) asserts the correct `ActionData` was buffered, (4) injects a `blockMined` world event, and (5) asserts `GenericGatherGoal.IsComplete()` returns true. This test would simultaneously catch correlation ID gaps, inventory projection bugs, and any regression in the chat→goal→plan→action pipeline.

The StatusTool/GetStatusTool duplication is a real maintenance hazard in long-running projects. I've seen this pattern cause silent divergences where one alias gets updated and the other doesn't. B-2 is correctly classified as blocking.

---

## Verdict

**Overall confidence**: 87%  
**Blocking findings**: 2  
**Deferred findings**: 5  
**Sprint 24 recommendation**: Proceed — fix B-1 + B-2 as immediate corrections (small PRs); D-1 (correlation IDs) as Sprint 24 P0; D-4 (end-to-end integration test) as Sprint 24 P1.

---

## Blocking Findings

### B-1 — FindFlatAreaTool default mismatch (High severity, quick fix)

**Source**: `Agent.Tools/Tools/FindFlatAreaTool.cs`, C# fallback lines and description string.  
**Evidence**: C# defaults `radius = 20, minFlatArea = 9`. Description says "default 20" and "default 9 (i.e. 3×3)". JS adapter uses `FLAT_AREA_DEFAULT_RADIUS = 32`, `FLAT_AREA_MIN_SIZE = 25` (per AGENTS.md, Sprint 19).  
**Impact**: LLM-driven invocations without explicit args use the C# defaults and get worse flat-area results. Developer documentation is factually wrong.  
**Fix**:
1. Change C# fallback `radius` from `20` → `32`.
2. Change C# fallback `minFlatArea` from `9` → `25`.
3. Update description string: "default 32" and "default 25 (i.e. 5×5 area)".
4. Update InputSchema description fields to match.

**Effort**: ~10 lines C#.  
**Testable acceptance criterion**: `FindFlatAreaDefaults_MatchJsAdapter` — calling `ExecuteAsync` with empty `{}` arguments dispatches `ActionData` with `radius=32, minFlatArea=25`.

---

### B-2 — StatusTool / GetStatusTool duplicate classes (Medium severity, quick fix)

**Source**: `Agent.Tools/Tools/StatusTool.cs` (`Name = "Status"`) and `Agent.Tools/Tools/GetStatusTool.cs` (`Name = "GetStatus"`).  
**Evidence**: Two separate files with identical bodies dispatching `ActionProtocol.Status`. GetStatusTool is documented as a "compatibility alias" but is a full class.  
**Impact**: Any future change to tool description, schema, or dispatch logic must be manually mirrored. Silent divergence risk on future sprint changes.  
**Fix**: Delete `StatusTool.cs`. Keep `GetStatusTool.cs` with `Name = "GetStatus"`. If backward compatibility for plans dispatching "Status" is required, add a second `Program.cs` registration that registers the same `GetStatusTool` instance under the alias "Status" in `ToolDispatcher`, rather than a full duplicate class.  
**Effort**: 1 file deletion, 0–2 DI lines.  
**Testable acceptance criterion**: `ToolDispatcher_StatusDispatchedByOneClass` — `ToolDispatcher` contains exactly one class instance whose `ExecuteAsync` sends `ActionProtocol.Status`. Both "Status" and "GetStatus" tool names dispatch via that same instance.

---

## Deferred Findings

| ID | Finding | Severity | Target Sprint |
|----|---------|----------|---------------|
| D-1 | Action correlation IDs: `PendingAction` record, `ConcurrentDictionary<Guid,PendingAction>` in `AgentBackgroundService`, JS echo of `correlationId` in result events | High | Sprint 24 P0 |
| D-2 | LLM decoupling from world-event loop via `Channel<WorldEvent>` + dedicated consumer Task | Medium | Sprint 25 |
| D-3 | Adapter reconnection: reconnect loop in `MinecraftAdapter` on WebSocket close, `botReconnecting`/`botReconnected` synthetic events | High (unattended) | Sprint 25 P0 |
| D-4 | End-to-end integration tests: fake adapter, full chat→goal→action→event→projection chain | Medium | Sprint 24 P1 |
| D-5 | Move event throttling: `MOVE_EMIT_THROTTLE_MS = 250` constant in `index.js`, drop intermediate move events | Low | Sprint 24 P2 |

---

## Sprint 24 Recommendations

**Theme**: Action Lifecycle Fidelity + Constant Unification

### P0 (must-ship)
- B-1 fix: Sync `FindFlatAreaTool` C# defaults to JS adapter (radius=32, minFlatArea=25)
- B-2 fix: Remove `StatusTool.cs`; single canonical status class, alias if needed in DI
- D-1: `ActionCorrelationId` (Guid) in `ActionData.Context`; `PendingAction` record + `ActionLifecycle` enum; `ConcurrentDictionary<Guid, PendingAction>` in `AgentBackgroundService`; JS adapter echoes `correlationId` in result events; cleanup in `ShutdownAsync`
- Sprint 23 D-6: Integration test for `TryInterruptOnDamage` (carried forward from Sprint 23)

### P1 (should-ship)
- Sprint 23 priority: `GatherGoalDecomposer` TargetCount fix (carried forward from Sprint 23)
- D-4 start: One end-to-end integration test (fake adapter, full gather cycle: chat→goal→MineBlock dispatch→blockMined event→IsComplete=true)
- Startup constant log: `LogInformation` at agent start listing top 6 tunable constants

### P2 (nice-to-have)
- Sprint 23 D-8: `ITimeProvider` / `SystemTimeProvider` / `FakeTimeProvider` abstraction
- D-5: Move event throttling (`MOVE_EMIT_THROTTLE_MS = 250` in `index.js`)
- Sprint 23 D-5: `IWorldObservationGateway` interface note in architecture doc

---

## Testable Acceptance Criteria

1. `FindFlatAreaDefaults_MatchJsAdapter`: `FindFlatAreaTool.ExecuteAsync({})` dispatches `radius=32, minFlatArea=25`.
2. `ToolDispatcher_StatusDispatchedByOneClass`: Exactly one tool class instance sends `ActionProtocol.Status`; both "Status" and "GetStatus" names resolve to it.
3. `PendingAction_CompletedOnMatchingEvent`: Dispatch fake action with `correlationId` → inject world event with matching ID → assert `_pending` dictionary empty.
4. `PendingAction_TimedOut_LogWarning`: Dispatch fake action → no world event → at timeout assert `LogWarning` emitted and `_pending` cleared.
5. `GatherCycle_EndToEnd_FakeAdapter`: Chat "gather 5 iron_ore" → `MineBlockTool` dispatched → inject `blockMined` (×5) events → `GenericGatherGoal.IsComplete()` returns true.
6. `TryInterruptOnDamage_HealthDropsBelowThreshold_InterruptsGoal` (Sprint 23 D-6 integration test).

---

*Council review authored: 2026-06-19. Reviewed against sprint-5-tool-safety head `2fe07082a5`.*
