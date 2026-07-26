# TSK-NEW: Migrate AgentBackgroundService onto ExecutionContext + the Manager Services

**Status:** Proposed (not yet filed as a `Data/Tasks/*.json` record — this document is the implementation-ready spec; copy the "Task record" section at the bottom into the tracker as-is, or adjust to house style first).
**Supersedes/closes, once done:** the wiring gap behind Report #1 F1 (god class), F2 (dead `AgentRuntime`/Managers), F3 (`WorldModel.Reconcile` never called), Report #6 J1 (`IGoalPrecondition` unreachable), and directly advances `TSK-0293` (remove legacy fallback/shim paths). Also unblocks an honest resolution — fix or delete — of `IGoalPostcondition`/`IRemediationPolicy` (currently zero implementations/consumers either way).
**Explicitly satisfies:** all five bullets in `Data/Pages/user-requirements.md`'s "Architecture hard requirements" section (see Delta Audit #7 for the point-by-point mapping).

---

## Why this is one task, not five

The target interfaces (`IPlanningManager`, `IExecutionManager`, `IRecoveryManager`, `IStateManager`, `IDashboardPublisher`), their concrete implementations, and the DI registrations wiring them **to each other** already exist and are already correct:
```csharp
// WebUI.Blazor/Program.cs:388-409 — already present, already correct
builder.Services.AddSingleton<IPlanningManager>(sp => new PlanningManagerImpl(...));
builder.Services.AddSingleton<IExecutionManager>(sp => new ExecutionManagerImpl(...));
builder.Services.AddSingleton<IRecoveryManager>(sp => new RecoveryManagerImpl(...));
builder.Services.AddSingleton<IStateManager>(sp => new StateManagerImpl(...));
builder.Services.AddSingleton<IDashboardPublisher>(sp => new DashboardPublisherImpl(
    sp.GetRequiredService<IStateManager>(), ...));
builder.Services.AddSingleton<AgentRuntime>(sp => new AgentRuntime(
    sp.GetRequiredService<IPlanningManager>(), sp.GetRequiredService<IExecutionManager>(),
    sp.GetRequiredService<IRecoveryManager>(), sp.GetRequiredService<IStateManager>(),
    sp.GetRequiredService<IDashboardPublisher>()));
```
**The only missing piece is `AgentBackgroundService` itself never resolving any of these.** That means this is not a design task or a "figure out the architecture" task — it's a mechanical migration task with a known-correct target already sitting in the codebase. The risk is entirely in the *sequencing* (don't break a 4,000-line hot loop in one PR), not in *what* to build.

---

## Scope

**In scope:** changing `AgentBackgroundService` to accept and use the five manager interfaces (and, through them, `ExecutionContext`), moving the corresponding inline logic out of `AgentBackgroundService.cs` slice by slice, and removing the now-redundant fields/methods once each slice is confirmed working.

**Out of scope (explicitly — don't let this task's scope creep):**
- Changing any of the five interfaces' method signatures (they're already fit for purpose — `PlanningManagerImpl`'s `IGoalPrecondition` check from Report #6 is a good example of code that's *already correct* and just needs to be reachable).
- Deciding the fate of `IGoalPostcondition`/`IRemediationPolicy` (zero implementations/consumers today) — that's a follow-up decision *after* this migration makes `PlanningManagerImpl`/`RecoveryManagerImpl` live, at which point it'll be obvious whether they're needed or should be deleted (see Report #6, J1, recommendation #3).
- Any behavior change beyond "same behavior, now living in the right place." This is a refactor, not a feature change — resist the temptation to fix unrelated bugs found along the way; file them separately (several are already filed — see "Known adjacent issues" below).

---

## Sequencing: five vertical slices, each independently shippable

Per Report #1's original recommendation, migrate one responsibility at a time rather than attempting a single rewrite. Suggested order, easiest/lowest-risk first:

### Slice 1 — Dashboard push → `IDashboardPublisher` (do this one first; fully scoped below)
### Slice 2 — World state → `IStateManager`
### Slice 3 — Action dispatch → `IExecutionManager`
### Slice 4 — Planning/replanning → `IPlanningManager`
### Slice 5 — Recovery → `IRecoveryManager`

Slices 2–5 build on each other (dispatch and planning both need `ExecutionContext`, which `IStateManager.BuildContext(...)` produces — so Slice 2 should land before 3/4/5). Slice 1 has no such dependency, which is why it's the recommended starting point.

---

## Slice 1, fully scoped: Dashboard push → `IDashboardPublisher`

### Current state (as of `dev/round-3` @ `0f27af1b`)

`AgentBackgroundService.cs` has 15 call sites invoking one of three private methods:
```
_ = PushStatusToDashboardAsync(ct)   — 4 call sites (lines 624, 691, 1149, and inside itself)
_ = PushChatToDashboardAsync(...)    — 4 call sites (lines 1325, 1410, 1436, 1585, 1713)
_ = PushGoalToDashboardAsync()       — 3 call sites (lines 404, 439, 1741, 1772)
```
plus the three method definitions themselves (lines 3665–3746, ~82 lines).

`WebUI.Blazor/Managers/DashboardPublisherImpl.cs` already implements `IDashboardPublisher.PublishStatusAsync()` — **but it is not a drop-in replacement as-is**. Two concrete gaps to close as part of this slice (found by directly diffing the two implementations while writing this spec):

1. **`DashboardPublisherImpl.PublishStatusAsync` hard-codes `QueuedActions: 0` with a `// Sprint 40+: wire from ActionQueue` comment.** The live `AgentBackgroundService.PushStatusToDashboardAsync` correctly reports `QueuedActions: _queue.Count`. `DashboardPublisherImpl` needs either a reference to the queue (via a new constructor parameter, or by reading it off `IStateManager`/`ExecutionContext` if queue depth gets threaded through there — see Slice 2) before this slice can ship without a regression.
2. **`IDashboardPublisher` only declares `PublishStatusAsync()`.** There's no interface method for chat-push or goal-push — `PushChatToDashboardAsync`/`PushGoalToDashboardAsync`'s SignalR calls (`"ChatMessage"`, `"GoalUpdate"` events) have no home in the target architecture yet. Add `Task PublishChatAsync(string type, string? who, string text, CancellationToken ct = default)` and `Task PublishGoalAsync(string? goalName, string? goalDescription, CancellationToken ct = default)` to `IDashboardPublisher`, implement both in `DashboardPublisherImpl` (trivial — same shape as the existing `PublishStatusAsync`, same `hubContext` null-check-and-swallow pattern), before removing the ABS-local versions.

### Step-by-step

1. Add `PublishChatAsync`/`PublishGoalAsync` to `IDashboardPublisher` and implement in `DashboardPublisherImpl` (see gap #2 above).
2. Resolve gap #1: pass queue depth into `DashboardPublisherImpl` — simplest is an added constructor parameter `Func<int> queueDepthProvider` or, cleaner, wait for Slice 2 and read it off `IStateManager`/`ExecutionContext` if that ends up carrying queue depth (it already has a `QueueDepth` field per `ExecutionContext`'s record definition — confirm `IStateManager.BuildContext(...)`'s `queueDepth` parameter is being fed correctly, and have `DashboardPublisherImpl` pull from there instead of needing its own reference).
3. Add `IDashboardPublisher dashboardPublisher` to `AgentBackgroundService`'s constructor parameter list.
4. Replace each of the 15 call sites: `_ = PushStatusToDashboardAsync(ct)` → `_ = dashboardPublisher.PublishStatusAsync(ct)`, etc. Before replacing, call the existing `SetCurrentGoal`/`SetConsecutiveFailures`/`SetNearbyEntities`/`SetBlockBelow` setters on `DashboardPublisherImpl` at the same points `AgentBackgroundService` currently updates the corresponding private fields (`_currentGoal`, `_consecutiveFailures`, the `nearbyEntitiesRaw`/`blockBelow` fact parsing) — these setters already exist on `DashboardPublisherImpl` specifically for this purpose (their doc comments say "Call from AgentBackgroundService... until the full ABS decomposition completes," i.e., this was anticipated).
5. Register `IDashboardPublisher` in `AgentBackgroundService`'s DI resolution (it's already registered in `Program.cs`; just needs to appear in the constructor and the call site that constructs `AgentBackgroundService`, likely `builder.Services.AddHostedService<AgentBackgroundService>()` or equivalent — confirm this resolves constructor parameters via DI already, which it should since ABS already takes several other DI-registered services today).
6. Delete `PushStatusToDashboardAsync`, `PushChatToDashboardAsync`, `PushGoalToDashboardAsync` from `AgentBackgroundService.cs` (~82 lines removed) and the now-unused `hubContext` constructor parameter if nothing else in the file uses it directly (double-check first — `hubContext` may be referenced elsewhere; grep before deleting).
7. Run the existing test suite (per Delta #4's G6 finding, tests are organized by sprint number rather than by class, so search across files rather than assuming one obvious test file — `grep -rl "PushStatusToDashboard\|PushChatToDashboard\|PushGoalToDashboard" MemorySmith.Agent.Tests/` first to find what needs updating).

### Acceptance criteria for Slice 1
- `grep -c "PushStatusToDashboardAsync\|PushChatToDashboardAsync\|PushGoalToDashboardAsync" WebUI.Blazor/AgentBackgroundService.cs` → 0.
- `IDashboardPublisher` has three methods (status/chat/goal), all implemented in `DashboardPublisherImpl`, no stubbed fields (`QueuedActions` reports a real value).
- Manual or automated check: connect a dashboard client, confirm status/chat/goal events still arrive with identical payload shape to before the change.
- `AgentBackgroundService.cs`'s line count drops by roughly 80–100 lines (the three methods plus related field bookkeeping).

---

## Slices 2–5: outline only (scope in detail when each is picked up)

- **Slice 2 (`IStateManager`):** `StateManagerImpl.Apply(WorldEvent)`/`.Current`/`.BuildContext(...)` should replace `AgentBackgroundService`'s direct `_worldState` field mutation via `WorldStateProjector`. This is the dependency Slice 3–5 need (`ExecutionContext` construction lives here). Note: this is also where the `Reconcile` wiring gap from Report #1 F3 naturally gets fixed — `IStateManager` or its caller is the right place to call `_worldModel?.Reconcile(...)` alongside the existing `Predict`/`ApplyOutcome` calls, since state management and world-model reconciliation are the same responsibility.
- **Slice 3 (`IExecutionManager`):** `ExecutionManagerImpl.DispatchAsync(ActionData, ct)` replaces the tool-calling portion of `DispatchActionsAsync` (currently 703 lines — the single largest method in the codebase per Report #1, F1). This slice alone should meaningfully shrink that method.
- **Slice 4 (`IPlanningManager`):** `PlanningManagerImpl.PlanAsync(ExecutionContext, ct)` replaces the inline `HtnPlanner`/`PlannerRouter` calls in `AgentBackgroundService`, and — this is the direct fix for Report #6's J1 — makes the `IGoalPrecondition.CanAttempt()` check (already correctly written inside `PlanningManagerImpl`) actually run for the first time.
- **Slice 5 (`IRecoveryManager`):** `RecoveryManagerImpl.TryRecoverAsync(...)` replaces `TryRecoverFromGameErrorAsync`. Note: `RecoveryManagerImpl`'s current body is a stub (`return Task.FromResult(false)` per Report #1, F2) — this slice necessarily includes *actually implementing* recovery logic in `RecoveryManagerImpl` by porting the real logic out of `AgentBackgroundService`, not just redirecting a call to an empty stub. Do not ship this slice by pointing at the stub as-is; that would be a regression (recovery currently works, even if inline).

---

## Known adjacent issues to fix *as part of* the relevant slice (not before, not as scope creep — just don't reintroduce them)

- Report #1, F3: wire `WorldModel.Reconcile` — do this inside Slice 2.
- Report #6, J1: `IGoalPrecondition` unreachable — resolves automatically as a side effect of Slice 4; no separate work needed beyond confirming it fires (add/extend a test asserting a precondition-failing goal produces an empty plan, since none currently exercises this path in production).
- Report #4, D3 / this-series' evaluator JSON-extraction gap: unrelated to this migration; don't bundle it in.

## Risks

- **Behavioral drift risk during Slice 5 specifically**, since `RecoveryManagerImpl` needs *real* logic ported in, not just a redirect — this is the one slice that's closer to "port a feature" than "move a call." Recommend extra test coverage here specifically (characterization tests on the current `TryRecoverFromGameErrorAsync` behavior before touching it, per this team's own documented six-phase refactor framework: blast-radius analysis → characterization tests → interface freeze → deliberate interface evolution → deletion → production validation).
- **DI resolution order**: `AgentRuntime`'s own registration already depends on all five managers resolving cleanly (per the `Program.cs` snippet above) — since that registration already works today (it just isn't consumed), there's no new DI-ordering risk introduced by this migration; `AgentBackgroundService` will simply become another consumer of already-working registrations.
- **Test suite findability** (Delta #4, G6): expect to spend extra time locating existing tests for the code being moved, since they're organized by sprint number rather than by class under test.

---

## Task record (paste into `Data/Tasks/` as a new `TSK-###.json`, adjusting to house schema)

```json
{
  "title": "Migrate AgentBackgroundService onto ExecutionContext + the Manager services (Slice 1: Dashboard)",
  "status": "Backlog",
  "description": "AgentBackgroundService has never been migrated onto the Sprint 36/39/57 target architecture (IPlanningManager/IExecutionManager/IRecoveryManager/IStateManager/IDashboardPublisher + ExecutionContext), despite those interfaces, their implementations, and their DI wiring-to-each-other already existing and being correct. This is Slice 1 of a 5-slice migration: move dashboard push (PushStatusToDashboardAsync/PushChatToDashboardAsync/PushGoalToDashboardAsync, ~82 lines, 15 call sites) onto IDashboardPublisher. Requires: (1) adding PublishChatAsync/PublishGoalAsync to IDashboardPublisher (currently only has PublishStatusAsync), (2) fixing DashboardPublisherImpl's hard-coded QueuedActions: 0 stub to report a real value, (3) wiring IDashboardPublisher into AgentBackgroundService's constructor, (4) calling the existing SetCurrentGoal/SetConsecutiveFailures/SetNearbyEntities/SetBlockBelow setters at the right points, (5) deleting the three ABS-local methods once verified. Subsequent slices (2-5, tracked separately once this lands) cover IStateManager, IExecutionManager, IPlanningManager, IRecoveryManager in that order. Source: this migration spec, and Reports #1 (F1/F2/F3), #4 (G4), #6 (J1), #7 (capstone) of the ongoing MSA audit series; explicitly satisfies Data/Pages/user-requirements.md's architecture hard requirements.",
  "priority": "P1",
  "labels": ["architecture", "tech-debt", "dashboard"]
}
```
