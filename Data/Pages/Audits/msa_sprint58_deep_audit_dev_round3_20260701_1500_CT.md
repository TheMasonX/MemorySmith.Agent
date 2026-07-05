# MemorySmith.Agent — Deep Code Audit (Sprint 58 Wave D / dev/round-3)

Report: msa_sprint58_deep_audit_dev_round3_20260701_1500_CT.md

Generated: 2026-07-01 15:00 CT
Repository: TheMasonX/MemorySmith.Agent
Branch: dev/round-3
Commit: `57b8fdf79ff7ba6f877e0fcac2211934e0f3279c` ("Sprint 58 Wave D — thread safety, cancellation, inventory diff")

Focus Areas:
* Autonomous multi-step sequencing (`TaskSequenceGoal`) and the observe→evaluate→replan loop
* `WorldModel`/`WorldStateDiff` prediction pipeline wired in Sprint 58 (TSK-0309/TSK-0334)
* Dynamic replanning fidelity and evaluator diagnosis feedback
* Extensible tool surface / goal-factory coverage
* Legacy fallback and dead-code consolidation (manager layer, HtnPlanner type-switch)

---

## 0. Method & Scope

Full clone of `dev/round-3` at the target commit. Read every file under `Agent.Core`, `Agent.Planning` (incl. `Decomposition/`, `Router/`, `Llm/`, `Goals/`, `Interfaces/`), `Agent.Tools` (incl. `Tools/`), `Agent.Construction`, `Agent.Memory`, `Agent.Personality`, `Agent.Vision`, `Agent.World.Minecraft`, `WebUI.Blazor` (incl. `Managers/`, `Dashboard/`, `Options/`, `Program.cs`, `AgentBackgroundService.cs`), and `MineflayerAdapter/index.js`. Cross-referenced against `Data/Pages/Audits/internal-audit-57-20260701.md`, `Data/Pages/Audits/llm-adaptapbility-sprint-57-audit-7-1-26.md`, the Sprint 58 Wave A/B/C/D handoffs, and open items in `Data/Tasks/*.json` to avoid duplicating already-tracked work. Every new finding below was verified by reading the actual implementation, not inferred from comments or docs. Test files were consulted to check whether a code path was actually exercised.

**All P0/P1/P2 findings from `internal-audit-57-20260701.md` are Sprint-58-resolved except those explicitly re-confirmed as still-open in §4.** This report does not re-litigate those; it reports on the current commit's residual and newly-discovered issues.

---

## 1. Executive Summary

The codebase is in noticeably better shape than the audit trail from earlier today (`internal-audit-57-20260701.md`) suggested — Sprint 58 Waves C and D closed 13 of that audit's 16 tracked P0–P2 findings with real fixes (verified by re-reading the code, not just trusting the handoff notes). However, this audit surfaces **five new, previously-undocumented issues**, one of which (`WorldModel.Predict` tool-name mismatch) is a **Critical/near-certain** bug that silently defeats the exact feature Sprint 58 Wave B just finished wiring in (TSK-0309), and by extension undermines TSK-0334 (unexpected-inventory detection) shipped in this very commit.

**Top-line finding:** the newest, most actively-developed code (this commit and the one before it) contains the newest bugs. The thread-safety and cancellation fixes in Wave D look solid on inspection. The prediction/diff pipeline wired in Waves B–D has a fundamental naming-domain bug that appears to have shipped without being caught by tests, because `WorldModel` has **zero unit tests** covering `Predict()`.

**Confirmed still-open (tracked, not duplicated here in detail):** ExecutionManagerImpl JSON round-trip (TSK-0322), dead manager layer (TSK-0292/0293), `PlaceBlockGoalDecomposer` same-coordinate placement (P1-4), `BuildGoalDecomposer` origin mislabeling (P1-5), entity-fact eviction (P2-2), missing tool surface (TSK-0311), `ThinkAndPlan` (TSK-0313).

**Corrected from prior audit framing:** the "chat routing over-permissive / hardcoded onlinePlayers=1" finding in the Sprint 57 Wave B audit applies **only to `IntentManagerImpl`**, which is dead code (see A-1). The live path (`AgentBackgroundService.HandleChatEventAsync`) already passes real `chat.OnlinePlayers` / `chat.PlayerPos` — confirmed at `AgentBackgroundService.cs:1288-1289`. This is not a live-path bug; it should be removed from future audit summaries or explicitly scoped to the dead manager layer.

**Corrected/closed:** the creative-mode blindness in `GatherItemDecompose`/`DecomposeCraftItem`/`DecomposeSmeltItem`/`DecomposeBuild` referenced in earlier memory/audits is fully resolved as of Sprint 57 Wave D — all four now branch on `state.IsCreativeMode` and short-circuit to `/give` or the creative block-executor (`HtnTaskLibrary.cs:212,308,480,731`).

---

## 2. Findings Table

| # | Finding | Class | Impact | Likelihood | Severity | Confidence |
|---|---|---|---|---|---|---|
| F1 | `WorldModel.Predict` tool-name domain mismatch defeats TSK-0309/TSK-0334 | Verified Issue | 4 | 5 | **20 (Critical)** | 97% |
| F2 | `IGoalPrecondition` (TSK-0310, this sprint) wired only into dead `ExecutionContext` path — zero production effect | Verified Issue | 3 | 5 | **15 (High)** | 95% |
| F3 | LLM evaluator has no `TaskSequenceGoal` context branch | Verified Issue | 3 | 4 | **12 (High)** | 95% |
| F4 | Replanning never receives evaluator diagnosis (reason/suggestion) | Architectural Concern | 3 | 4 | **12 (High)** | 90% |
| F5 | Chained "navigate" step in `nextSteps` is silently dropped | Verified Issue | 3 | 3 | **9 (Moderate)** | 92% |
| F6 | `HtnPlanner` class doc claims type-switches were removed; they were not — P2-5 (planner/decomposer overlap) still open | Verified Issue / Doc Drift | 2 | 4 | **8 (Moderate)** | 96% |
| F7 | `TaskSequenceGoalDecomposer` hard-throws (abandons whole sequence) instead of HTN-style graceful fallback | Architectural Concern | 2 | 2 | **4 (Low)** | 85% |
| F8 | Dead `"Navigate:"` branch in `LlmEvaluatorImpl.AppendGoalContext` — no `IGoal` ever has that name pattern | Probable Issue (dead code) | 1 | 5 | 3 (Low, cleanup) | 88% |
| F9 | `WorldModel` has zero unit tests — enabled F1 to ship undetected | Test Coverage Gap | — | — | — | 98% |

---

## 3. New Findings — Detail

### F1. `WorldModel.Predict` tool-name mismatch silently defeats the Sprint 58 prediction pipeline
**Classification:** Verified Issue · **Severity 20/25 (Critical)** · **Confidence: 97%**

`WorldModel.Predict` switches on **wire-protocol-style lowercase names** (`Agent.Core/Models/WorldModel.cs:75-87`):
```csharp
return toolName switch
{
    "move" => PredictMove(current, args),
    "status" => PredictStatus(current),
    "mine" => PredictMine(current, args),
    "craft" => PredictCraft(current, args),
    "place" => PredictPlace(current, args),
    "smelt" => PredictSmelt(current, args),
    "wander" => PredictWander(current, args),
    "chat" => PredictNoChange(current, toolName, args),
    "findFlatArea" => PredictNoChange(current, toolName, args),
    _ => PredictUnknown(current, toolName, args),
};
```
But it is called with the **C# tool-registry name** (`ITool.Name`), which is PascalCase, from `AgentBackgroundService.cs:2188`:
```csharp
_preDispatchPrediction = _worldModel?.Predict(action.Tool, action.Arguments);
```
`action.Tool` is populated by decomposers/`HtnTaskLibrary` and is never case-normalized before this call (confirmed by direct trace: dequeue at line 2085 → `Predict` at line 2188, no transformation in between). The actual `ITool.Name` values registered in `Program.cs` and used as `ActionData.Tool` throughout the planning layer are: `MineBlock`, `CraftItem`, `SmeltItem` (registered by `FurnaceTool`), `MoveTo`, `Wander`, `FindFlatArea`, `GetStatus`, `PlaceBlock`, `Chat` (`Agent.Tools/Tools/*.cs:Name` — grep confirms all 14 tools). The wire-protocol names in `ActionProtocol.cs` (`move`, `mine`, `craft`, `place`, `smelt`, `wander`, `status`, `findFlatArea`, `chat`) are a **deliberately separate namespace** used only inside each tool's `ExecuteAsync` when it builds the outbound WebSocket message (see `ActionProtocol.cs:1-9`, referencing ADR-010).

Net effect: with the sole exception of `"place"` — which matches only by coincidence, because `PlaceBlockGoalDecomposer.cs:41` happens to set `Tool = "place"` (lowercase) instead of the tool registry's actual name `PlaceBlock`, and `Program.cs:289` separately registers `"place"` as a wire-protocol alias — **every other tool call falls through to `PredictUnknown`**, which returns the belief-state inventory unchanged (`Agent.Core/Models/WorldModel.cs:199-201`). This includes `MineBlock`, `CraftItem`, `SmeltItem`, `Wander`, `MoveTo`, `GetStatus`, `FindFlatArea` — i.e., essentially all real dispatch traffic.

**Downstream compounding effect (verified, `ComputeWorldStateDiff`, `AgentBackgroundService.cs:3255-3268`):** the Sprint 58 fallback logic reads `prediction.PredictedInventory` to populate `expectedGained`/`expectedLost` only when `outcome.Effects` is empty (true for all fire-and-forget tools). Since `PredictUnknown` returns the belief's *existing* inventory unchanged, the predicted delta is always zero — `expectedGained`/`expectedLost` stay empty for MineBlock/CraftItem/SmeltItem/Wander. This means:
- `WorldStateDiff.HasInventoryMismatch`'s new TSK-0334 "unexpected inventory change" branch (`Agent.Core/Models/WorldStateDiff.cs:78-94`) treats **every legitimate inventory gain from mining/crafting/smelting as "unexpected"** (nothing is ever in `expectedKeys`), because the item was never predicted as expected.
- This flips `diff.HasMismatch` to `true` on almost every successful mine/craft/smelt action.
- Per the Sprint 58 Wave C fix (TSK-0320), `LlmEvaluatorImpl`'s fast-path is explicitly bypassed when `diff.HasMismatch` (`LlmEvaluatorImpl.cs:66`) — meaning **the "all succeeded, skip evaluation" fast-path is now effectively disabled for the majority of normal, successful gameplay**, not just genuine divergences.
- Practical consequence: substantially increased LLM evaluator call volume/cost/latency during ordinary gather/craft/smelt goals, and a non-zero chance the evaluator's LLM call misreads routine drops as anomalies worth replanning over (the prompt in `LlmEvaluatorImpl.cs:172-180` surfaces `diff.DescribeMismatches()` verbatim to the model).

**Why this shipped:** `WorldModel` has no unit tests at all (F9) — nothing exercises `Predict("MineBlock", ...)` or asserts it returns a mine-flavored prediction, so the mismatch is invisible to the test suite (815/815 green).

**Recommendation:**
1. Switch `WorldModel.Predict` to accept/compare against the same domain used at the call site. Simplest fix: change the switch to match on `ITool.Name` values (`"MineBlock"`, `"CraftItem"`, `"SmeltItem"`, `"PlaceBlock"`, `"Wander"`, `"MoveTo"`, `"GetStatus"`, `"FindFlatArea"`, `"Chat"`), using `StringComparer.OrdinalIgnoreCase` at minimum as a belt-and-suspenders measure even after aligning the literal casing.
2. Add unit tests asserting `Predict("MineBlock", {block:"oak_log"})` returns a `PredictionState` with `Confidence >= 0.9` and a non-empty predicted-inventory delta — this is the regression guard that should have caught this.
3. Re-run the TSK-0334 "unexpected inventory" logic manually against a captured mine/craft/smelt session once F1 is fixed, to confirm false positives disappear.

---

### F2. `IGoalPrecondition` (TSK-0310) has zero effect on the live agent
**Classification:** Verified Issue · **Severity 15/25 (High)** · **Confidence: 95%**

Sprint 58 Wave A/B (TSK-0310) added `IGoalPrecondition.CanAttempt` to `GenericGatherGoal`, `CraftItemGoal`, and `SmeltGoal`. The only call site is `PlanningManagerImpl.PlanAsync(ExecutionContext, ...)` (`WebUI.Blazor/Managers/PlanningManagerImpl.cs:70-81`). A repo-wide search confirms `IGoalPrecondition`/`CanAttempt` do not appear anywhere in `AgentBackgroundService.cs`, `HtnPlanner.cs`, or `PlannerRouter.cs` — the live dispatch loop calls `planner.PlanAsync(_currentGoal, _worldState, ct)` (the two-argument overload), never the `ExecutionContext` overload. `ExecutionContext` itself has zero usages in `AgentBackgroundService.cs` (grep confirms). The only test coverage for this feature is `Sprint57ExecutionContextTests.cs`, which tests the manager layer directly, not the live path.

**Impact:** an entire sprint task shipped, tested green, and documented as complete — but it has no effect on actual agent behavior. This is the same failure mode as A-1 (dead manager layer) recurring in fresh work, which raises the priority of TSK-0292/0293 beyond "someday cleanup" — new features are actively being built on top of a layer that doesn't run.

**Recommendation:** Either (a) call `precondition.CanAttempt(...)` directly from `AgentBackgroundService.DispatchActionsAsync` before invoking `planner.PlanAsync`, as a stopgap, or (b) treat this as evidence to accelerate TSK-0292 (ABS decomposition) — every sprint that adds logic to the manager layer without wiring it in compounds the eventual migration cost and the number of "phantom fixes" in handoff notes.

---

### F3. LLM evaluator has no context branch for `TaskSequenceGoal`
**Classification:** Verified Issue · **Severity 12/25 (High)** · **Confidence: 95%**

`LlmEvaluatorImpl.AppendGoalContext` (`Agent.Planning/LlmEvaluatorImpl.cs:191-232`) branches on `IBuildGoal`, `IItemSpecGoal`, `CraftItemGoal`, and a `goal.Name.StartsWith("Navigate:")` check (dead — see F8). **There is no branch for `TaskSequenceGoal`.** When the current goal is a sequence, none of the `if/else if` arms match, so the evaluator's prompt gets zero step-specific progress information — no step index, no total steps, no delegation to the current step's own type-specific context (e.g., if step 2 of 3 is a `BuildGoal`, the evaluator won't see block-placement progress at all, because `goal` here is the `TaskSequenceGoal` wrapper, not the `IBuildGoal` it wraps).

The only signal the evaluator receives about sequence state is the goal *name* — `TaskSequenceGoal.Name => $"Sequence:{_steps[_currentStep].Name}"` (`Agent.Core/Models/TaskSequenceGoal.cs:35`) — which is passed into the prompt as `Goal: {goal.Name} ({goal.GetType().Name})`. This tells the model which step is active by name, but not its numeric progress, target counts, or build-block status — exactly the detail `AppendGoalContext` exists to supply for every other goal type.

This directly corroborates the architectural gap already flagged qualitatively ("LLM evaluator blind to TaskSequenceGoal context") — this report adds the precise code location and confirms it is still unaddressed at this commit.

**Recommendation:** In `AppendGoalContext`, add:
```csharp
if (goal is TaskSequenceGoal seq)
{
    sb.AppendLine($"Sequence: step {seq.CurrentStepIndex + 1}/{seq.TotalSteps}");
    AppendGoalContext(sb, seq.CurrentStep, worldState); // recurse into the active step
    return;
}
```
placed as the first check in the method, before the `IBuildGoal`/`IItemSpecGoal` branches, so it delegates to the existing per-type logic for whatever goal type the active step is.

---

### F4. Replanning never receives the evaluator's diagnosis
**Classification:** Architectural Concern · **Severity 12/25 (High)** · **Confidence: 90%**

When `LlmEvaluatorImpl.EvaluateAsync` returns `ShouldReplan=true` with a `Reason`/`Suggestion`, the dispatch loop only logs it and `break`s out of the action loop so the outer `while` calls `planner.PlanAsync` again (`AgentBackgroundService.cs:2223-2229`). `IPlanner.PlanAsync(IGoal goal, WorldState state, CancellationToken ct)` (`Agent.Planning/Interfaces/IPlanner.cs:11`) has **no parameter for the evaluator's diagnosis** — the only call site is `planner.PlanAsync(_currentGoal, _worldState, ct)` (`AgentBackgroundService.cs:1991`), passing just goal and state. The stall-path variant (`TryLlmReplanOnStallAsync`) sends `evalResult.Suggestion` to in-game chat and clears the queue (`AgentBackgroundService.cs:3373-3384`), but this text never reaches `HtnTaskLibrary`, any `IGoalDecomposer`, or the LLM planning fallback prompt.

Since goal identity and `WorldState` are frequently unchanged (or only trivially changed) at the moment of replan, calling `PlanAsync` again with the same inputs is likely to regenerate the same or a very similar plan — which is exactly what the replan-governor's fingerprint check (`AgentBackgroundService.cs:2003-2038`) exists to detect and interrupt (`STALLED`). This creates a structural loop: evaluator says "replan, try X" → planner has no way to attempt X because it never receives the suggestion → governor detects the repeat → stall handler asks the LLM again → same suggestion, same non-delivery.

**Recommendation:** Extend the replan path (not necessarily the full `IPlanner` interface) with an optional diagnosis channel — e.g., a `string? ReplanHint` parameter threaded from `EvaluationResult.Suggestion` through to `HtnPlanner.TryLlmFallbackAsync`'s prompt when the deterministic decomposers can't act on it directly, and/or expose it as a `WorldState.Facts["evaluator:lastSuggestion"]` entry that `HtnTaskLibrary`/decomposers can read defensively (lower-effort, no interface break). At minimum, log a WARNING if the fingerprint immediately re-stalls after a suggested replan, to make the disconnect observable.

---

### F5. A chained "navigate" step is silently dropped from `TaskSequenceGoal`
**Classification:** Verified Issue · **Severity 9/25 (Moderate)** · **Confidence: 92%**

Multi-step chaining (`nextSteps`, TSK-0205) parses each subsequent command string via `IntentManager.ParseCommandString` (`Agent.Planning/IntentManager.cs:122-198`) and converts the resulting `GoalRequest` to an `IGoal` via `goalFactory.CreateAsync(stepRequest.GoalName, ...)` (`AgentBackgroundService.cs:1462-1471`). For a "move to X Y Z" / "navigate to X Y Z" step, `ParseCommandString` returns `NavigateGoalRequest`, whose `GoalName` is `"MoveTo"` (`IntentManager.cs:268-269`). `GoalFactory.CreateAsync`/`Create` has **no entry for `"MoveTo"`** — the `Creators` dictionary only contains `"GatherWood"` and `"SurviveNight"`, and none of the prefix branches (`GatherItem:`, `Build:`, `CraftItem:`, `SmeltItem:`, `PlaceBlock:`) match a bare `"MoveTo"` (`Agent.Planning/GoalFactory.cs:35-40,169`). The result is `stepGoal == null`, and the calling loop has **no else-branch or logging** for this case:
```csharp
var stepGoal = await goalFactory.CreateAsync(stepRequest.GoalName, stepRequest.Parameters, ct);
if (stepGoal is not null)
    allSteps.Add(stepGoal);
```
The step is dropped with zero diagnostics — no warning log, no chat message to the player. This is asymmetric with standalone (non-chained) navigate, which is handled directly by a dedicated `case "navigate":` branch in `HandleChatEventAsync` (`AgentBackgroundService.cs:1509`) that dispatches a `MoveTo` action without going through `IGoal`/`GoalFactory` at all — a second, disconnected code path that the chained-sequence flow doesn't reuse.

**Reproduction:** any compound player command whose LLM-generated `nextSteps` includes an entry matching `^(?:move|go|navigate|walk)\s+(?:to\s+)?<x>\s+<y>\s+<z>` (e.g., "gather 10 wood then navigate to 100 64 200") will silently produce a sequence missing that step, with no error surfaced.

**Recommendation:** Either (a) register a `"MoveTo"` creator in `GoalFactory` that returns a minimal navigate `IGoal` (would need a lightweight goal wrapper, since navigate currently has no dedicated `IGoal`/decomposer), or (b) special-case `NavigateGoalRequest` in the chaining loop the same way `HandleChatEventAsync`'s standalone `"navigate"` case does. Either way, add an else-branch that logs a warning (and ideally informs the player) whenever a chained step fails to resolve into a goal, for both the `stepRequest is null` and `stepGoal is null` cases.

---

### F6. `HtnPlanner`'s class doc is stale; the type-switch it says was removed is still present (P2-5 still open)
**Classification:** Verified Issue / Documentation Drift · **Severity 8/25 (Moderate)** · **Confidence: 96%**

`HtnPlanner.cs:15-16` states: *"Sprint 27 P0-D: type-switch branches for `IItemSpecGoal`, `BuildGoal`, and `CraftItemGoal` have been removed... `HtnPlanner` is now a pure fallback."* The code immediately below (`HtnPlanner.cs:56-84`) still contains exactly those branches:
```csharp
if (goal is BuildGoal buildGoal) { ... }
else if (goal is CraftItemGoal craftGoal) { ... }
else if (goal is IItemSpecGoal itemSpecGoal) { ... }
else if (_library.HasTask(goal.Name)) { ... }
else { /* phase-by-phase */ }
```
This is not just stale documentation — it is the exact mechanism described in the prior audit's P2-5 ("HtnPlanner and PlannerRouter have overlapping type handling"): because `HtnPlanner` still handles these types directly, a **missing decomposer registration for a new goal type is not surfaced as an error** — it silently falls through to this duplicate logic instead of the intended single-source-of-truth `DecomposerRegistry`. P2-5 was deferred in Sprint 58 as "architectural clean-up"; this audit confirms it is still fully present at this commit and additionally flags that the class doc actively misrepresents the current implementation, which risks misleading future contributors into assuming the dead branches are already gone.

**Recommendation:** No new remediation beyond what P2-5 already recommends (remove the redundant type-switch; let `HtnPlanner` be a pure phase/task-library fallback). Additionally: correct or delete the stale doc comment so it doesn't misdirect the next engineer who reads the class header and assumes the branches are gone.

---

### F7. `TaskSequenceGoalDecomposer` hard-fails instead of falling back gracefully
**Classification:** Architectural Concern · **Severity 4/25 (Low)** · **Confidence: 85%**

If a `TaskSequenceGoal`'s current step has no registered decomposer, `TaskSequenceGoalDecomposer.Decompose` throws `InvalidOperationException` (`Agent.Planning/Decomposition/TaskSequenceGoalDecomposer.cs:26-29`). This differs from the top-level `PlannerRouter.Select`, which gracefully falls back to `HtnPlanner` when no decomposer matches (`PlannerRouter.cs:100-105`). The exception is caught generically in `AgentBackgroundService.DispatchActionsAsync`'s outer `catch (Exception ex)` (`AgentBackgroundService.cs:2074-2079`), which sets `FailureReason.NoValidActions` and **abandons the entire sequence goal** (not just the failing step) — the player sees a generic planning-failure log, with no indication that only one step of a multi-step chain was the problem.

Currently unreachable in practice: every goal type constructible via `GoalFactory`/`ParseCommandString` (`GatherWoodGoal`, `GenericGatherGoal`, `BuildGoal`, `CraftItemGoal`, `SmeltGoal`, `PlaceBlockGoal`, `SurviveNightGoal`) has a registered decomposer (`Program.cs:334-344`). This is a landmine for future goal types, not an active bug.

**Recommendation:** When adding new chainable goal types, either register a decomposer up front, or change `TaskSequenceGoalDecomposer` to route a decomposer-less step through the same `IPlanner` fallback the top-level router uses (would require injecting `IPlanner`/`HtnPlanner` rather than just `DecomposerRegistry`), so a single missing registration degrades to one bad step rather than killing the whole sequence.

---

### F8. Dead `"Navigate:"` branch in the evaluator's goal-context builder
**Classification:** Probable Issue (dead code) · **Confidence: 88%**

`LlmEvaluatorImpl.AppendGoalContext` has an `else if (goal.Name.StartsWith("Navigate:", ...))` branch (`LlmEvaluatorImpl.cs:227-231`). No `IGoal` implementation in the codebase produces a name matching that pattern — a repo-wide search found no `NavigateGoal` class; navigation is either a direct `MoveTo` action dispatch outside the goal system (standalone chat case, `AgentBackgroundService.cs:1509`) or, per F5, a `GoalFactory.CreateAsync("MoveTo", ...)` call that returns `null`. This branch cannot currently execute. Low-cost cleanup; folding it into the F3 fix (which handles `TaskSequenceGoal` recursion into whatever the step actually is) is a natural place to also remove it, or replace it with real navigate-goal support once F5 is addressed.

---

### F9. `WorldModel` has no unit test coverage
**Classification:** Test Coverage Gap · **Confidence: 98%** (confirmed via file search — no `*WorldModelTest*` file exists; the only test file mentioning `WorldModel` is `Sprint25Tests.cs`, and only for the constructor defensive-copy fix, not `Predict`/`Reconcile`)

This is the direct enabler of F1 shipping undetected. `Reconcile()` is also never called anywhere outside its own definition and the `IWorldModel` interface — it remains fully unwired from the execution loop, consistent with the previously-known architectural gap, but worth re-confirming at this commit since Sprint 58 specifically extended `WorldModel`'s footprint (via `Predict`) without extending its test coverage or completing the `Reconcile` wiring.

**Recommendation:** Add a `WorldModelTests.cs` covering: (a) every `Predict(toolName, ...)` case with the *actual* `ITool.Name` strings used in production, asserting non-default confidence and correct predicted-inventory deltas; (b) `Reconcile` accuracy against known prediction/observation pairs. This is the cheapest structural fix that would have caught F1 before merge.

---

## 4. Confirmed Still-Open Items (Already Tracked — Not Duplicated)

Re-verified against current code; retained here only as a status confirmation, not a new writeup:

| Item | Task | Status confirmed at this commit |
|---|---|---|
| ExecutionManagerImpl JSON round-trip (type fidelity loss) | TSK-0322 (P0/Critical, Backlog) | Still present — `ExecutionManagerImpl.cs:56-57` unchanged |
| Six manager implementations + `AgentRuntime` unused by live path | TSK-0292/0293 (High, Ready) | Still present — see F2 for a fresh, concrete instance of the cost of leaving this unresolved |
| `PlaceBlockGoalDecomposer` places all N blocks at identical coordinates | (P1-4, deferred) | Still present — `PlaceBlockGoalDecomposer.cs:24-44` unchanged |
| `BuildGoalDecomposer` mislabels stored-fact origins as `AutoScanned` | (P1-5, deferred) | Still present — `BuildGoalDecomposer.cs:60` unchanged |
| Entity-related facts (`nearbyEntitiesRaw`, etc.) never evicted | (P2-2, deferred) | Not independently re-verified this pass; no evidence of a fix landing in Waves C/D |
| Missing tool surface (EquipItem, ActivateBlock, AttackEntity, UseItem, DropItem, LookAt) | TSK-0311 (Medium, Backlog) | Confirmed absent from `Agent.Tools/Tools/` |
| `ThinkAndPlan` recursive sub-planning tool | TSK-0313 (Medium, Backlog) | Confirmed absent |
| Safety config hidden startup-time coupling (`Program.cs` merge) | (P2-6, documented limitation) | Confirmed present, as documented |

---

## 5. Risks

* **Compounding phantom-fix risk (F2, A-1):** every sprint that adds logic to the manager/`ExecutionContext` layer without wiring it into `AgentBackgroundService` increases the eventual migration cost of TSK-0292 and increases the number of "shipped but inert" features. This is no longer a static architectural note — Sprint 58 added a fresh instance in the same cycle it also fixed six real bugs, which suggests the dev-agent's workflow doesn't currently check "does this call site exist in the live path?" before marking a task done.
* **Silent-failure bias in the chaining pipeline (F5, F7):** the pattern of "if X is not null, do Y; otherwise do nothing" recurs across the multi-step chaining code with no logging on the negative branch. This makes future regressions in chaining hard to detect from logs alone.
* **F1's blast radius:** because `ComputeWorldStateDiff`/`WorldStateDiff.HasMismatch` sits directly upstream of the LLM evaluator's replan decision, F1 doesn't just waste LLM calls — it changes the evaluator's effective sensitivity for the entire fleet of fire-and-forget tools, in a direction (constant false "mismatch") that the original TSK-0320/TSK-0334 authors did not intend and likely did not observe in manual testing (since a single LLM call recommending "continue" on a spurious mismatch is behaviorally indistinguishable from the fast path in casual play-testing).

---

## 6. Recommended Priorities

1. **F1** — fix `WorldModel.Predict`'s tool-name domain mismatch and add regression tests (F9). Cheap fix, highest-severity, actively degrading the newest shipped feature.
2. **F3** — add `TaskSequenceGoal` context to the evaluator prompt. Small, isolated, directly unblocks evaluator quality for the flagship multi-step-autonomy feature.
3. **F5** — stop silently dropping chained navigate steps; at minimum add logging so the failure is observable.
4. **F2** — either wire `IGoalPrecondition.CanAttempt` into the live dispatch loop as a stopgap, or use it as forcing-function evidence to prioritize TSK-0292.
5. **F4** — thread evaluator diagnosis into replanning (even a low-effort `WorldState.Facts` channel beats nothing).
6. **F6/F7** — low-cost cleanup, bundle with any future work that touches `HtnPlanner`/`TaskSequenceGoalDecomposer`.
7. Continue existing Sprint 59 backlog (TSK-0322, TSK-0311, TSK-0292/0293) as already prioritized — nothing in this audit changes their relative ranking, except elevating TSK-0292/0293 per the F2 evidence above.

---

## 7. Open Questions

* Is there a reason `WorldModel.Predict`'s switch uses the wire-protocol vocabulary rather than the tool-registry vocabulary? If the original intent was for `Predict` to be called from somewhere closer to the wire boundary (e.g., inside a tool's `ExecuteAsync`), the current call site in `AgentBackgroundService` may be the actual mismatch rather than the switch statement — worth confirming design intent before patching, though the fix is the same either way (align the two).
* Should navigate become a first-class `IGoal`/decomposer (fixing F5 and F8 at the root) now that multi-step chaining makes it a real gap, or is the standalone direct-dispatch path considered permanent by design?
* Is `TSK-0310`'s `IGoalPrecondition` work expected to be wired into the live path in Sprint 59, or was it always scoped as manager-layer-only groundwork? If the latter, this should be stated explicitly in the task/handoff so future audits don't flag it as "dead" — the ambiguity is what drove F2's severity here.

---

## 8. Confidence Summary

| Confidence band | Findings |
|---|---|
| 95–100% (verified, direct evidence) | F1 (97%), F2 (95%), F3 (95%), F9 (98%) |
| 80–94% (strong evidence, highly probable) | F4 (90%), F5 (92%), F6 (96%), F8 (88%) |
| 60–79% (probable, partial evidence) | F7 (85% — architecturally certain, but currently unreachable so real-world likelihood is contingent) |

All findings above are grounded in direct code citation (file + line) at commit `57b8fdf`, not inferred from comments, docstrings, or prior audit text alone. Where a prior audit's framing was found to be inaccurate relative to the live-path code (chat routing hardcoding), this is called out explicitly rather than silently repeated.

---

## 9. Files Read In Full (Primary Evidence Base)

`Agent.Core/Models/{TaskSequenceGoal,WorldModel,WorldStateDiff,ActionQueue,ActionOutcome,ExecutionContext}.cs`, `Agent.Core/WorldStateProjector.cs`, `Agent.Planning/{LlmEvaluatorImpl,HtnPlanner,GoalFactory,IntentManager,PlannerRouter*,ChatModels}.cs`, `Agent.Planning/Decomposition/*.cs`, `Agent.Planning/Interfaces/IPlanner.cs`, `Agent.Tools/ToolDispatcher.cs`, `Agent.Tools/ActionProtocol.cs`, `Agent.Tools/Tools/*.cs`, `WebUI.Blazor/AgentBackgroundService.cs` (full, 3856 lines), `WebUI.Blazor/Managers/*.cs`, `WebUI.Blazor/Program.cs`, `MineflayerAdapter/index.js` (structural pass), plus all Sprint 57/58 handoffs, the internal Sprint 57 audit, and severity/audit-preference docs under `Data/Pages/Preferences/`.
