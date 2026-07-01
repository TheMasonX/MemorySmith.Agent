# MemorySmith.Agent — Audit Report
**Branch:** `dev/round-3` | **Commits:** `5af0d749` (Sprint 56 B+C) → `27428d21` (Sprint 57 Wave C handoff)  
**Auditor:** External | **Date:** 2026-07-01 | **Tests at HEAD:** 808 pass, 0 fail

---

## Executive Summary

Sprint 56 B+C and Sprint 57 Wave C delivered genuine, well-tested fixes to critical stability bugs (sequence completion, `/give` injection, command deny list, spawn inventory, stop-during-place). The new architectural scaffolding (`ExecutionContext`, `ExecutionCapabilities`, `ActionRegistry`, `PlanningPolicy`, `RemediationPolicies`) is the most significant design progress in the codebase's history and directly enables the user's autonomy goal — but it is **not yet wired into the live execution path**. The 3,649-line `AgentBackgroundService` god class remains the runtime, operating in parallel to these models without using them.

**The creative-mode regression (gather/craft/smelt unconditionally emit `MineBlock`) is unresolved.** It is not addressed by any task in either sprint and will recur on any gather/craft/smelt command issued in creative mode.

**Three findings require immediate action before the next wave:** the `/summon` prompt-vs-denylist conflict (F-NEW-1), the dual `TryCompleteCurrentGoalFromWorldUpdate` sequence advancement path that silently skips resets (F-NEW-2), and the stale guard's `continue` path leaving the queue populated with a `GetStatus` action while skipping `await Task.Delay` (F-NEW-3).

---

## Verified Fixes — What Landed Correctly

| Task | Finding | Verdict |
|------|---------|---------|
| TSK-0274 | `TaskSequenceGoal.IsComplete` structural bug | ✅ Fixed — now delegates to `_steps[_currentStep].IsComplete(state)` |
| TSK-0275 | `/give` command injection via `SAFE_BLOCK_RE` in JS | ✅ Fixed in adapter; C# side still builds raw string (see F-OLD-3) |
| TSK-0276 | Retry counter type mismatch | ✅ Functionally resolved — read side made robust; write still stores string |
| TSK-0277/0286/0287 | DeniedCommands + SafetyOptions | ✅ Clean — config-authoritative, fallback correct, no duplicate list |
| TSK-0278 | LLM parse failure `IsSuccess`/`FailureReason` signal | ✅ Interface and impl correct; caller handling not yet wired (see F-NEW-4) |
| TSK-0279 | Delete dead `chatFilter.js` | ✅ Deleted |
| TSK-0296 | PlaceBlock "goal was changed" stop-race fix | ✅ Fixed — `_dispatchingAction` tracking suppresses pathfinder cancel during place/move |
| TSK-0301 | Spawn inventory + stale guard rewrite | ✅ `sendBotStatus()` on spawn; two-branch guard; blocks plan gen while stale |

---

## Bug Findings

### F-NEW-1 — [CONFIRMED, 95%] `/summon` is in DeniedCommands but LLM system prompt tells the bot to use it for combat; silently fails with no player feedback

**Location:** `LlmChatInterpreter.cs:357-364` (prompt), `AgentBackgroundService.cs:489` (deny list), `AgentBackgroundService.cs:1236-1240` (block-and-break path)

**Mechanism:** The system prompt explicitly instructs the LLM: *"Use intent='command' with the /summon lightning_bolt command at the entity's coordinates."* When the player says "punch the creeper," the LLM generates `intent="command", item="/summon minecraft:lightning_bolt X Y Z"`. The command handler (line 1232) extracts `/summon`, finds it in `DefaultDeniedCommands` (line 489), logs a warning, and `break`s — **without sending any chat response**. `pendingResponse` (the LLM's acknowledgement) is recorded to `_chatHistory` but never enqueued. The player receives silence. This happens 100% of the time with default config since `AllowDestructiveCommands` defaults to false.

**Two options:**
1. Remove `/summon` from `DefaultDeniedCommands` and add a scoped allowlist for specific summon uses (lightning bolt at coordinates only, never `/summon @e` variants)
2. Change the combat workaround to use a different mechanism (`AttackEntity` tool once implemented, or a `/execute` variant — though `/execute` is also denied)

**Immediate patch:** At minimum, the block path should enqueue the `pendingResponse` to chat so the player knows the action was refused, not ignored. Currently `pendingResponse` is discarded even though it may say "I can't attack directly — try using commands."

---

### F-NEW-2 — [CONFIRMED, 85%] Dual `TaskSequenceGoal` advancement paths with inconsistent reset semantics

**Location:** `AgentBackgroundService.cs:1553-1565` (`TryCompleteCurrentGoalFromWorldUpdate`) vs `AgentBackgroundService.cs:3183-3237` (`TryAdvanceSequence`)

**Mechanism:** There are two code paths that advance a `TaskSequenceGoal` to the next step:
1. **Event path** (`TryCompleteCurrentGoalFromWorldUpdate`, line 1553): calls `seq.TryAdvance()` directly, clears queue/pendingActions, then returns. Does NOT reset: `_consecutiveFailures`, `_lastFailureReason`, `replanGovernor`, `_cycleInventorySnapshot`, `_placeBlockContexts`, `_cycleOutcomes`, `_lastReplanAt`, `_lastStallWarnedAt`, or `_lastActionDispatchedAt`. Does NOT announce the next step in chat.
2. **Dispatch loop path** (`TryAdvanceSequence`, line 3183): performs the full reset of all 10+ fields listed above, announces the next step via a `Chat` action, and resets the stall timer.

If the event-path advancement fires (inventory update triggers `IsComplete` on step N before the dispatch loop gets there), step N+1 starts with stale failure counters, a potentially stalled governor, and no chat announcement. This can cause step N+1 to be immediately declared failed due to a non-zero `_consecutiveFailures` inherited from step N, or to skip the chat announcement the user expects.

**Fix:** Extract the full reset logic from `TryAdvanceSequence` into a private `ResetForNextSequenceStep(TaskSequenceGoal seq)` method. Call it from both advancement paths. The chat announcement (which requires queue access) can be conditional: enqueue it from the dispatch path but skip it from the event path (or vice-versa — pick one canonical place for the announcement).

---

### F-NEW-3 — [CONFIRMED, 80%] Stale guard continues without `await Task.Delay` when `GetStatus` is already in-flight, burning CPU at ~20 iterations/sec

**Location:** `AgentBackgroundService.cs:1823-1849`

**Mechanism:** The stale guard has two branches:
```
if (!HasPendingActionOfTool("GetStatus"))  → enqueue GetStatus, fall through to await Task.Delay(50)
else                                        → LogDebug, then immediately continue  ← no delay
```
The `else` branch (GetStatus already in-flight, just waiting) hits `continue` without any `await Task.Delay`. The outer dispatch loop has a 50ms delay at lines 2279+, but that path is only reached at the bottom of the loop body — `continue` jumps back to the top. The loop runs at maximum CPU speed (~3–20k iterations/second) burning one core while waiting for the `StatusEvent` to arrive. This is a busy-wait spin loop wearing the CPU thread pool.

**Fix (one line):** Add `await Task.Delay(50, ct);` immediately before the `continue` in the `else` branch, matching the `if` branch's behavior.

---

### F-NEW-4 — [CONFIRMED, 75%] `EvaluationResult.IsSuccess = false` is never checked by callers; parse failures are silently treated as "continue"

**Location:** `AgentBackgroundService.cs` — calls to `EvaluateAsync` around lines 2060–2085 and `TryLlmReplanOnStallAsync` (~line 3320)

**Mechanism:** `ILlmEvaluator.EvaluateAsync` now returns `EvaluationResult` with `IsSuccess` and `FailureReason` (TSK-0278 added these). But the call sites do:
```csharp
var evalResult = await llmEvaluator.EvaluateAsync(goal, outcomes, state, ct, diff: diff);
if (evalResult.ShouldReplan) { /* trigger replan */ }
```
`IsSuccess` is never checked. A parse failure (LLM returns malformed JSON), a `NullResponse`, or a `ProviderUnavailable` all return `IsSuccess=false, ShouldReplan=false` — indistinguishable at the call site from a genuine "continue current plan" verdict. The evaluator logs a warning, but the caller treats every non-replan result identically. This means: if the LLM provider goes offline, the agent silently continues its failing plan indefinitely rather than escalating to a stall or requesting human input.

**Fix:** Check `!evalResult.IsSuccess` at the call site. On repeated failures (`ProviderUnavailable`, N consecutive `ParseFailure`), increment a `_consecutiveLlmEvalFailures` counter and either force a governor stall or log at Error level so the operator knows the evaluator loop is broken.

---

### F-OLD-1 — [CONFIRMED, 90%] `GatherItemDecompose`, `DecomposeCraftItem`, `DecomposeSmeltItem` — zero creative-mode awareness; unconditionally emit `MineBlock`

**Status: UNRESOLVED from prior audit. Not addressed in Sprint 56 or 57.**

**Location:** `Agent.Planning/HtnTaskLibrary.cs:708–767` (gather), `207–283` (craft), `296–328` (smelt)

`IsCreativeMode` is checked in exactly one place in this file: `DecomposeBuild` (line 466). All three sibling methods produce `MineBlock` actions regardless of game mode. `GatherGoalDecomposer`, `CraftItemGoalDecomposer`, and `SmeltGoalDecomposer` are thin pass-throughs with no creative logic.

`AgentBackgroundService.ProvisionGoalIfCreativeAsync` (line 401) fires `/give` pre-provisioning only for `IBuildGoal` — never for gather/craft/smelt goals. `GenericGatherGoal.IsComplete`'s creative shortcut was deliberately removed. The net result: any "gather X", "craft Y", or "smelt Z" command in creative mode generates a plan of `MineBlock` actions that never complete because creative drops nothing.

**Now directly fixable without scattered if/else, using Sprint 57's new infrastructure:**

`ExecutionCapabilities.CanSpawnItems` (true in creative) is now part of `ExecutionContext`, which `PlanningManagerImpl.PlanAsync(context)` already receives. The correct fix is:
1. In `GatherItemDecompose`: `if (state.IsCreativeMode) return [ActionFactory.Create("Chat", ("message", $"/give @p {spec.ItemId} {count}"))]` — or better, route through `ensureCreativeItems` via a new `SpawnItem` tool action.
2. Do the same for `DecomposeCraftItem` and `DecomposeSmeltItem` when their inputs are gatherable in creative.
3. Register this as a single `IExecutionModeStrategy` (or use `ActionDescriptor.RequiresSurvival = true` on `MineBlock` entries in `ActionRegistry`) so the planner can filter mode-incompatible actions at the dispatch boundary rather than inside each decomposer.

The `ActionDescriptor.RequiresCreative` / `RequiresSurvival` flags in the new `ActionRegistry` were designed exactly for this — but the registry is not yet populated or consulted by the planner.

---

### F-OLD-2 — [CONFIRMED, 90%] `Debug.WriteLine` silently drops exceptions in Release builds

**Status: UNRESOLVED. Not addressed in Sprint 56 or 57.**

**Locations:**
- `Agent.Core/Models/ActionQueue.cs:140` — catch block for failed `stopCallback()`, no `ILogger` available
- `Agent.Planning/HtnPlanner.cs:270` — static `ParseLlmActions` method, instance logger not accessible from static context

Both are no-ops in Release builds. `ActionQueue` has no DI logger; `ParseLlmActions` is static. **Fix:** Convert `ParseLlmActions` to an instance method so `_logger` is accessible. Inject `ILogger<ActionQueue>` into `ActionQueue` constructor — or, simpler, convert the `stopCallback` invocation to `try { } catch (Exception ex) { /* swallow intentionally */ }` with a comment explaining why, making it deliberate rather than accidentally silent.

---

### F-OLD-3 — [CONFIRMED, 75%] C# `ProvisionGoalIfCreativeAsync` builds raw `/give` string without server-side sanitization

**Status: PARTIALLY resolved. JS adapter now sanitized; C# side is not.**

**Location:** `AgentBackgroundService.cs:~422`: `$"/give @p {block} {need}"` where `block` comes from `Blueprint.Materials[].Block` — parsed from wiki frontmatter with no validation.

The JS `creativeProvider.js` now has `SAFE_BLOCK_RE` guard before its `/give` fallback. But the C# side's `ProvisionGoalIfCreativeAsync` still sends raw strings through the `Chat` tool action without any sanitization. A blueprint page with `block: "cobblestone;ban-ip 0.0.0.0"` (containing a semicolon) would pass the JS `SAFE_BLOCK_RE` check (which strips `minecraft:` prefix but runs the regex on the full material name field) — wait, actually `SAFE_BLOCK_RE = /^[a-zA-Z0-9_]+$/` would reject the semicolon. The real risk is a newline-embedded value: `"cobblestone\n/ban-ip 0.0.0.0"` would pass a single-line field check in the frontmatter parser but split into two chat messages in Mineflayer's `bot.chat()`.

**Fix:** Add `Regex.IsMatch(block, @"^[a-zA-Z0-9_]+$")` validation in `ProvisionGoalIfCreativeAsync` before building the command string. Reject and log any block name that fails.

---

### F-OLD-4 — [CONFIRMED, 70%] `IntentAssessment.cs` and dead `HtnPlanner` branches — unresolved dead code

**Status: UNRESOLVED.**

- `Agent.Planning/IntentAssessment.cs` — complete `IntentAssessment` record + `RiskLevel` enum, zero consumers anywhere. The `IGoalPrecondition`/`IGoalPostcondition` interfaces in `PlanningPolicy.cs` cover the same design space more cleanly — `IntentAssessment` should be deleted.
- `Agent.Planning/HtnPlanner.cs:56–84` — `BuildGoal`/`CraftItemGoal`/`IItemSpecGoal` branches are unreachable via `PlannerRouter` since all three decomposers are registered. Dead since Sprint 37.
- `Agent.Planning/HtnPlanner.cs:126–153` — `CreateCreativeBuildActions` private static method, zero call sites.

---

## Architecture Assessment — Autonomy Readiness

### What's Working

The codebase now has all the **data models** needed for genuine autonomy:

| Model | Status | Notes |
|-------|--------|-------|
| `ExecutionContext` | ✅ Defined, partially wired | Built by `StateManagerImpl.BuildContext`; used in new `PlanningManagerImpl.PlanAsync(context)` overload |
| `ExecutionCapabilities` | ✅ Defined | `CanSpawnItems`, `CanFly`, `IsInvulnerable` — directly useful for F-OLD-1 fix |
| `IGoalPrecondition` / `IGoalPostcondition` | ✅ Defined | Not yet implemented by any goal class |
| `IRemediationPolicy` + `RemediationPolicies` | ✅ Defined | `RetryThenAbandon`, `WanderThenRetry`, `RefreshThenRetry` — not yet consumed |
| `ActionRegistry` + `ActionDescriptor` | ✅ Defined, registered in DI, not used | `RequiresCreative`/`RequiresSurvival` flags exist but planner doesn't filter on them |
| `RecoveryContext` | ✅ Defined, in `ExecutionContext` | `RecoveryManagerImpl.TryRecoverAsync(ExecutionContext)` stub reads it but always returns false |
| `WorldStateDiff` + `ComputeWorldStateDiff` | ✅ Live | Computed per-action in dispatch loop; passed to `LlmEvaluatorImpl` |
| `TaskSequenceGoal` | ✅ Fixed, live | Linear up to 5 steps; no branching |
| `LlmEvaluatorImpl` | ✅ Live | Observation-driven replan trigger works |
| `ReplanGovernor` | ✅ Live | Stall detection, graduated backoff |
| `WorldModel.Predict/Reconcile` | ❌ Models exist, zero wiring | Registered in DI, exposed via REST; never called in execution loop |

### The Gap Between Models and Runtime

`AgentBackgroundService` (3,649 lines) is the actual runtime. It operates **entirely independently** of the Sprint 57 architectural models. The `AgentRuntime` record and all six manager implementations are constructed in DI but `AgentBackgroundService` does not hold or call them — it was built before they existed and has not been updated to use them.

This creates a structural two-track system:
- **Track A (live):** ABS's 30+ mutable fields, inline state management, inline recovery logic, inline planning loop
- **Track B (dormant):** `ExecutionContext`, `AgentRuntime`, manager interfaces, policy objects

Every sprint that adds a new model to Track B without wiring it to Track A accumulates dormant infrastructure. The extraction program (TSK-0292/0293, deferred to Sprint 59+) is the right plan — the concern is prioritization. Until extraction happens, the autonomy goal is limited to what fits inside ABS's existing dispatch loop.

### Critical Path to Genuine Multi-Step Autonomous Replanning

The following gaps, in execution order, block genuine autonomy. Each depends on the previous:

**Gap 1 — No creative-mode provisioning for gather/craft/smelt (F-OLD-1)**
Blocks any creative-mode multi-step sequence involving resource acquisition. Since the user's primary test environment is creative mode (per task history), this blocks most sequence testing immediately.

**Gap 2 — `WorldModel.Predict/Reconcile` is unwired**
`ComputeWorldStateDiff` computes actual inventory delta (pre/post dispatch), but `expectedGained`/`expectedLost` are populated from `ActionOutcome.Effects` — which are never populated for async fire-and-forget tools (MineBlock, PlaceBlock, Wander). The diff therefore has no expectation half, only an observation half. The LLM evaluator receives: "you gained 5 oak_log" but not "you were expected to gain 5 oak_log." It has to infer expectations from goal context alone. Wiring `WorldModel.Predict` before each dispatch and `Reconcile` after would provide the expectation half automatically.

**Gap 3 — Flat `ActionPlan` with no conditional branching**
The planner returns a flat ordered list. If step 3 fails, there is no "else" branch — the entire plan is abandoned and regenerated from scratch. This makes multi-step sequences brittle: any single-step failure triggers a full replan rather than targeted recovery. The `IRemediationPolicy`/`RemediationStep` models define the right shape for structured recovery, but no goal implements `IGoalPrecondition` and no plan uses `IRemediationPolicy`.

**Gap 4 — `RecoveryManagerImpl` is a stub**
The actual recovery logic (`TryRecoverFromGameErrorAsync`, ~200 lines in ABS) has no access to `ExecutionContext`, `ActionRegistry`, or `RemediationPolicies`. It string-parses error messages and calls `SetGoal` directly — the circular dependency that blocks extraction.

**Gap 5 — No `ThinkAndPlan` or recursive sub-planning tool**
The LLM can only act on a single pre-planned sequence. It cannot pause mid-execution, assess the situation, and generate a sub-plan for an unexpected obstacle. Adding a `ThinkAndPlan` tool (accepts a sub-goal description string, calls `PlannerRouter.PlanAsync` internally, injects resulting actions at the front of the queue) would unlock genuine mid-execution adaptation without requiring the full ABS refactor.

**Gap 6 — Tool surface gaps for rich ad-hoc action**
Missing tools that block common autonomous behaviors: `EquipItem` (change held item without mining), `AttackEntity` (direct melee), `ActivateBlock` (open doors, press buttons, levers), `UseItem` (right-click with held item), `DropItem`, `LookAt` (orient toward target before other actions). Each is ~30 lines JS + ~50 lines C# and dramatically expands what ad-hoc LLM plans can express.

---

## Implementation Roadmap

### Immediate (this sprint, unblocked)

**1. Fix F-NEW-1 (summon/denylist conflict)** — 2 options:
   - Quick: Remove `/summon` from `DefaultDeniedCommands`; add a `SummonCommand` validator that only allows `minecraft:lightning_bolt` and `minecraft:arrow` with coordinate args (not `@e`/`@a` targets).
   - Better: Implement `AttackEntity` tool (see Gap 6). Remove the lightning-bolt workaround from the prompt entirely.

**2. Fix F-NEW-2 (dual sequence advancement)** — Extract reset into `ResetForNextSequenceStep()`, call from both paths. ~20 lines.

**3. Fix F-NEW-3 (stale guard busy-wait)** — Add `await Task.Delay(50, ct)` before `continue` in the else branch. 1 line.

**4. Fix F-OLD-1 (creative gather/craft/smelt)** — The cleanest fix given current architecture:
   ```csharp
   // In GatherItemDecompose, before any other logic:
   if (state.IsCreativeMode)
       return [ActionFactory.Create("Chat", ("message", $"/give @p {spec.ItemId} {count}"))];
   ```
   Do the same in `DecomposeCraftItem` and `DecomposeSmeltItem` for their pre-gather phases. This is 3 guard clauses in one file (`HtnTaskLibrary.cs`). Not architecturally ideal (still a scattered check), but unblocks the user's immediate regression.

   **The architectural version** (wire `ActionDescriptor.RequiresSurvival = true` to `MineBlock` in `ActionRegistry`, filter in `PlanningManagerImpl.PlanAsync(context)` using `context.Capabilities.CanSpawnItems`) is correct but requires ABS to use the `ExecutionContext` overload of `PlanAsync` — deferred to Sprint 59 extraction.

**5. Fix F-NEW-4 (EvaluationResult.IsSuccess not checked)** — Add `_consecutiveLlmEvalFailures` counter, escalate after 3 consecutive non-success results.

**6. Fix F-OLD-2 (Debug.WriteLine)** — Convert `ParseLlmActions` to instance method; inject logger into `ActionQueue` or make the swallow explicit.

### Short-Term (Sprint 58)

**Wire `WorldModel.Predict` before dispatch and `Reconcile` after completion:**
```csharp
// Before dispatching action:
var prediction = worldModel.Predict(action.Tool, action.Arguments);
// After StatusEvent confirms completion:
worldModel.Reconcile(prediction, newObservation);
// Use prediction.ExpectedInventoryDelta to populate ComputeWorldStateDiff's expectedGained
```
This closes the expectation half of the observation loop, giving `LlmEvaluatorImpl` structured expected-vs-actual data for every action type.

**Implement `IGoalPrecondition` on `GenericGatherGoal` and `CraftItemGoal`:**
```csharp
public bool CanAttempt(ExecutionContext ctx, out string? reason)
{
    if (ctx.Capabilities.CanSpawnItems) { reason = null; return true; } // creative — always ok, will use /give
    if (ctx.State.IsInventoryStale) { reason = "Inventory stale"; return false; }
    reason = null; return true;
}
```
This gives the planner early-exit for infeasible goals before generating any actions.

**Implement `EquipItem` and `ActivateBlock` tools** — highest ROI for ad-hoc capability. Door interaction and hotbar management are prerequisite for most non-trivial autonomous behavior.

### Medium-Term (Sprint 59 — Extraction)

**Extract `TryRecoverFromGameErrorAsync` → `RecoveryManagerImpl`.** The circular dependency (`recovery → SetGoal → ABS`) breaks when ABS is refactored to expose `SetGoal` via an `IGoalController` interface injected into `RecoveryManagerImpl`. This is the single most impactful extraction for autonomy: structured, policy-driven recovery instead of string-parsing in a god method.

**Wire `PlanningManagerImpl.PlanAsync(ExecutionContext)` as the canonical planning path** in ABS. This makes `ExecutionCapabilities`, precondition checks, and `ActionRegistry` filtering live in the production loop for the first time.

**Add `ThinkAndPlan` tool:**
```csharp
public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
{
    var subGoalDesc = args.GetProperty("goal").GetString();
    var subRequest = IntentManager.ParseCommandString(subGoalDesc);
    if (subRequest is null) return ToolResult(false, "Could not parse sub-goal");
    var subGoal = await goalFactory.CreateAsync(subRequest.GoalName, subRequest.Parameters, ct);
    var subPlan = await plannerRouter.PlanAsync(subGoal, worldState, ct);
    // Inject subPlan.Actions at the front of the current queue
    foreach (var action in subPlan.Actions.Reverse()) queue.EnqueueFront(action);
    return ToolResult(true, $"Sub-plan queued: {subPlan.Actions.Count} actions for {subGoalDesc}");
}
```
This is the key tool that converts the system from "pre-planned sequences" to "dynamic mid-execution replanning" — the LLM can call `ThinkAndPlan("gather materials for crafting table")` as an action when it discovers it lacks materials, rather than failing and restarting from scratch.

**Replace `TaskSequenceGoal` string-based `NextSteps` parsing** with structured `GoalRequest` objects. The current path re-runs `IntentManager.ParseCommandString` on each next-step string, discarding the LLM's structured output. Instead, serialize `GoalRequest` directly from the intent and deserialize at sequence creation — eliminates the round-trip re-interpretation.

**Increase `TaskSequenceGoal.MaxSteps` from 5 to 12** once the extraction makes step-reset reliable. 5 is too low for even moderately complex tasks ("gather wood, craft planks, craft crafting table, craft sticks, craft wooden pickaxe, mine stone, smelt stone, craft stone pickaxe" = 8 steps).

---

## New Backlog Tasks (for agent handoff)

These are net-new, not duplicating existing tasks:

| Proposed Key | Priority | Title | Blocks |
|---|---|---|---|
| TSK-0305 | P0 | Fix `/summon` prompt/denylist conflict — either whitelist scoped summon or implement `AttackEntity` tool | User-facing combat requests silently fail |
| TSK-0306 | P0 | Fix creative gather/craft/smelt: add `IsCreativeMode` guard to `GatherItemDecompose`, `DecomposeCraftItem`, `DecomposeSmeltItem` | Creative-mode regression still live |
| TSK-0307 | P1 | Fix dual `TaskSequenceGoal` advancement paths — extract `ResetForNextSequenceStep()` | Sequence steps start with dirty state |
| TSK-0308 | P1 | Fix stale-guard busy-wait: add `await Task.Delay(50, ct)` to `else` branch | CPU spin during inventory wait |
| TSK-0309 | P1 | Check `EvaluationResult.IsSuccess` at call sites; add `_consecutiveLlmEvalFailures` counter | Evaluator failures silently treated as "continue" |
| TSK-0310 | P2 | Wire `WorldModel.Predict` pre-dispatch and `Reconcile` post-completion | LLM evaluator lacks expected-vs-actual diff |
| TSK-0311 | P2 | Implement `IGoalPrecondition` on gather/craft/smelt goals | Precondition framework unused |
| TSK-0312 | P2 | Implement `EquipItem` and `ActivateBlock` tools | Blocks autonomous door/hotbar interaction |
| TSK-0313 | P2 | Implement `ThinkAndPlan` tool (mid-execution recursive sub-planning) | Core mechanism for true autonomy |
| TSK-0314 | P2 | Fix `Debug.WriteLine` in `ActionQueue.cs` and `HtnPlanner.ParseLlmActions` | Silent exception swallowing in Release |
| TSK-0315 | P3 | Delete `IntentAssessment.cs`, dead `HtnPlanner` branches, `CreateCreativeBuildActions` | Dead code cleanup |
| TSK-0316 | P3 | Add C#-side sanitization in `ProvisionGoalIfCreativeAsync` | Defense-in-depth for TSK-0275 |

---

## Open Questions

1. **TSK-0289/0290/0291/0294/0295** are listed as complete in the roadmap but `RecoveryManagerImpl.TryRecoverAsync(ExecutionContext)` still returns false with a "stub" comment. Was the intent that these tasks only produce the *models* (not the wiring)? If so, the roadmap entry should say "models complete, wiring deferred to Sprint 59."

2. **`IsInventoryStale` stale-guard scope:** The guard at line 1823 only applies when `_currentGoal is IItemSpecGoal`. Build goals and navigate goals skip the guard entirely — is this intentional? A build goal with a stale inventory could still generate a plan believing it has materials it doesn't.

3. **`sendBotStatus` on spawn (TSK-0301):** The inventory arrives on spawn now, but `StatusEvent.GameMode` from spawn may arrive before `gameModeEvent` fires (race condition between `spawn` handler's `sendBotStatus()` and `emitGameModeEvent()`). Confirmed order in code is `sendEvent('spawn') → emitGameModeEvent() → sendBotStatus()` (lines 330, 334, 336) — so gameMode event fires before status, which is correct. But if `sendBotStatus` at line 336 fires before `emitGameModeEvent`'s async WS send completes (they both call `sendEvent` which posts to the WS synchronously), the C# `StatusEvent` will be processed before the `GameModeEvent`, and `_worldState.IsCreativeMode` will be false when the status arrives — causing the initial `IsInventoryStale = true` to be set during a creative-mode session. Need to verify ordering on the C# event-processing queue. Low-risk in practice since `SetGoal` re-queries `IsCreativeMode` at planning time.

4. **Player coordinates in LLM prompt (TSK-0299):** The `IntentManagerImpl` passes `playerPosition: null` to `IChatInterpreter.InterpretAsync` (line in `IntentManagerImpl.cs`). The system prompt includes entity positions from `state.Facts["nearbyEntities"]` but not the speaking player's coordinates. TSK-0303 mentions adding commands like `/tp Leo TheMasonX23` — these require knowing the player's name, which the chat handler has (`username` parameter) but does not inject into the system prompt. Worth adding `username` to the prompt's addressing context.
