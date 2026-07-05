# MemorySmith.Agent Deep Code Audit

**Target:** `TheMasonX/MemorySmith.Agent`  
**Branch / commit context:** `dev/round-3`, commit `57b8fdf79ff7ba6f877e0fcac2211934e0f3279c`  
**Scope note:** I audited the repo’s exposed solution/docs and the core runtime path files directly reachable through the GitHub connector. The connector does not expose a full tree walk, so this is a deep audit of the enumerated codepaths and the sprint/task artifacts, not a literal byte-for-byte scan of every auxiliary file.

## Executive summary

The repo is already much healthier than a typical greenfield system: the architecture docs are explicit, the planner/runtime boundaries are typed, and several former silent-failure paths have been converted into observable outcomes. The biggest remaining risk is not “missing features”; it is **split ownership and transitional bridging**. The code still carries legacy paths, temporary fallbacks, and duplicated policy enforcement that can drift independently. fileciteturn5file0turn10file0turn33file0

The most important correctness risk I found is that **some failure paths still intentionally swallow or collapse detail**. That includes the queue’s stop-callback swallow, tool-registration overwrites, and the creative provisioning loop’s goal-change race window. These are the kinds of issues that stay invisible until the system is under load or a network/tool edge case happens. fileciteturn11file0turn38file0turn44file0

The most important architectural risk is that **policy is split across prompt-time, runtime config, and the background service**, while the docs explicitly say the next phase should remove fallback paths and consolidate runtime state into `ExecutionContext`. That split makes “safe by default” depend on multiple layers staying in sync. fileciteturn5file0turn18file0turn40file0turn41file0turn10file0

There is also a meaningful observability/accuracy issue in the world projection layer: `BlockMinedEvent` still increments inventory even though `ItemCollectedEvent` can also increment inventory, and the code explicitly accepts possible double-count drift as an expedient tradeoff. That is a pragmatic stopgap, but it is still a correctness debt item for any agent that uses inventory as truth for planning/completion. fileciteturn24file0

Finally, the sprint roadmap already identifies several high-value fixes that should be treated as **active work items, not new findings**. In particular: inventory sync, LLM evaluator fast-path/circuit-breaker, JSON round-trip removal, sync-over-async in `HtnPlanner`, safety config merge, and SignalR event-name drift are already queued in Sprint 59. Avoid duplicating those in the next implementation pass. fileciteturn8file0turn9file0

## Highest-risk findings

### 1) Stop-callback failures are silently swallowed in `ActionQueue.ClearAndEnqueueAsync`
**Severity:** High  
**Confidence:** 94%

`ClearAndEnqueueAsync` intentionally catches and discards any exception from `stopCallback`, then proceeds to clear/enqueue anyway. The comment says no logger is available at that layer, but the result is that a failed emergency-stop signal becomes invisible unless a higher layer independently notices it. That is a classic “system kept going, but the safety signal didn’t actually happen” failure mode. fileciteturn11file0

**Why it matters:** damage interrupts and stop/replan recovery are safety-critical. If stop dispatch fails, the queue still advances, and the operator may believe the bot halted when it did not.

**Recommendation:** pass a logger (or an error sink) into `ActionQueue`, or return an explicit result that tells the caller whether the stop signal succeeded. At minimum, emit a structured warning with enough context to correlate the swallowed failure with the current goal/cycle.

### 2) Tool registration overwrite is still silently unsafe on the primary overload
**Severity:** High  
**Confidence:** 91%

`ToolDispatcher.Register(ITool tool)` overwrites existing entries with no warning. Only the alias overload logs when a name collision happens. That means a duplicate registration bug can replace a tool implementation invisibly depending on which overload the caller used. fileciteturn38file0

**Why it matters:** this is a hidden contract in the dispatcher boundary. It can route the planner or runtime to the wrong implementation without any telemetry signal, which is exactly the sort of bug that becomes painful once the tool surface grows.

**Recommendation:** make the non-alias overload warn or throw on collision unless the caller explicitly opts into overwrite semantics. The alias overload can keep overwrite behavior, but it should be the only intentional escape hatch.

### 3) “Safe-by-default” command execution is split across prompt gating, config normalization, and runtime assumptions
**Severity:** High  
**Confidence:** 88%

The repo now sets `CommandExecutionEnabled` to `false` by default, normalizes denied commands in `Program.cs`, and also normalizes `SafetyOptions.DeniedCommands` via `PostConfigure`. However, the handoff explicitly states that no runtime guard was added in `AgentBackgroundService`; the prompt gate is considered sufficient. That means command safety depends on the prompt path, the interpreter, and the runtime guard all staying aligned. fileciteturn40file0turn33file0turn37file0turn10file0

**Why it matters:** if the LLM prompt is malformed, truncated, bypassed, or misinterpreted, there is no obvious single enforcement point in the evidence reviewed. That is a brittle contract for any destructive command path.

**Recommendation:** add an explicit runtime enforcement check at the actual command dispatch boundary, using the same normalized denylist that the prompt path sees. Keep the prompt filter too, but do not rely on it alone.

### 4) Creative `/give` provisioning still has a goal-change race window
**Severity:** High  
**Confidence:** 87%

`ProvisionGoalIfCreativeAsync` checks `_currentGoal != goal` at the top of each material iteration, but it also awaits a 200 ms delay between commands and then enqueues the command afterward. That means a goal can change during the delay, and the stale `/give` can still be enqueued before the next iteration notices. The roadmap already has a task for this exact issue (`TSK-0326`). fileciteturn44file0turn9file0

**Why it matters:** this is cross-goal contamination. In a greenfield system, leaked provisioning commands are the kind of legacy behavior you want to eliminate rather than tolerate.

**Recommendation:** re-check the goal identity immediately before enqueueing each command, or capture a per-goal token/epoch and validate it at the last possible moment. Treat the roadmap task as required, not optional.

### 5) Inventory truth can drift because mined blocks and collected drops can both increment counts
**Severity:** High  
**Confidence:** 84%

`WorldStateProjector.ApplyBlockMined` intentionally increments inventory for mined blocks, while `ApplyItemCollected` also increments inventory when the adapter reports the drop collection. The comments explicitly say double-counting can happen and is tolerated because the alternative is a stuck-at-zero inventory. That is understandable as an emergency bridge, but it is still a correctness compromise in the main world model. fileciteturn24file0turn25file0

**Why it matters:** gather/craft/smelt completion and planning rely on inventory truth. If the inventory can overcount, you can see premature completion, inflated progress, or fewer replans than intended.

**Recommendation:** converge on one authoritative source per item flow and make the other path observational only. If you need a temporary fallback, gate it behind a narrow block/item whitelist and emit a drift metric so you know when it fires.

## Medium-risk findings

### 6) Failure signaling in the goal layer is still partly legacy-only
**Severity:** Medium  
**Confidence:** 90%

`GenericGatherGoal.HasFailed` still reads legacy fact keys, but the comments admit the current production path does not write them. `CraftItemGoal.HasFailed` and `SmeltGoal.HasFailed` are also effectively vestigial in the reviewed path. This is not a crash bug, but it is dead/legacy logic that obscures the current contract and makes the codebase harder to reason about. fileciteturn15file0turn16file0turn17file0

**Why it matters:** greenfield code should not carry zombie failure channels unless they are actively used. They are the sort of thing that creates “works in tests, not in runtime” confusion later.

**Recommendation:** either wire these methods to a real write site and document the lifecycle, or remove the legacy fact-based failure path entirely and let the runtime state own failure tracking.

### 7) `AgentBackgroundService` still owns too much policy for the next architecture phase
**Severity:** Medium  
**Confidence:** 92%

The architecture doc says `ExecutionContext` should become the canonical runtime state and `AgentBackgroundService` should remain an orchestration layer, but the service still holds a very large amount of mutable runtime policy and special-case state. The file contains goal lifecycle, creative provisioning, inventory freshness, damage interrupts, reconnect behavior, stall tracking, correlation, and more. That is a lot of policy surface for a single host class. fileciteturn5file0turn18file0turn42file0turn43file0turn44file0

**Why it matters:** the more policy stays in the host, the harder it is to remove legacy branches cleanly and the easier it is for behavior to diverge across entry points.

**Recommendation:** prioritize the ABS extraction tasks already deferred in the roadmap, and move toward a single runtime-state object that downstream managers consume directly. Treat the service as an orchestration shell, not the system of record.

### 8) Duplicated denylist normalization is a maintenance hazard
**Severity:** Medium  
**Confidence:** 86%

The denylist is normalized in `Program.cs` for `ChatOptions` and also normalized again via `PostConfigure<SafetyOptions>`. That does work, but it creates two places where the same policy can drift, and the code comments explicitly distinguish a “prompt path” from a “runtime path.” fileciteturn33file0turn34file0turn35file0turn37file0

**Why it matters:** duplicated policy normalization is exactly how greenfield projects accumulate legacy behavior by accident.

**Recommendation:** factor normalization into a single helper used by both binding paths, or normalize once at the options layer and pass the normalized value through everywhere else.

### 9) `ActionData` and context carry are still mutable bridge contracts
**Severity:** Medium  
**Confidence:** 83%

`ActionData.Arguments` and `ActionData.Context` are mutable dictionaries, and the architecture doc explicitly classifies context-carry and dispatch-loop merge behavior as temporary bridges. That means inter-action state is still being passed through a mutable bag rather than a typed plan context. fileciteturn46file0turn5file0

**Why it matters:** mutable bridge state makes it easy for later actions to depend on implicit side effects, which is exactly the sort of hidden contract that becomes technical debt.

**Recommendation:** move toward a typed `PlanContext` or equivalent as the doc suggests, then delete the context-merge bridge once the planner emits explicit arguments.

## Implementation-specific notes

`PlaceBlockGoal` itself is in better shape than earlier versions: the decomposer no longer pre-sets dispatched counts, and the goal is now completed by confirmed block placements rather than by decomposition-time assumptions. That is the right direction. The remaining risk is not the goal class alone; it is the surrounding runtime that still holds many mutable counters and recovery branches. fileciteturn50file0turn51file0turn43file0

`GoalFactory` and `IntentManager` are both reasonably explicit and are moving in the correct direction: typed goal requests, alias resolution, and a separation between interpretation and goal creation. The main thing to watch is that these layers do not become the place where old fallback semantics linger after the runtime is cleaned up. fileciteturn31file0turn48file0

The planner fallback path in `HtnPlanner` is now clearly documented as a fallback, not the primary route. That is good. The next step is to keep the fallback narrow and remove any vestigial branches that still exist for compatibility rather than behavior. fileciteturn12file0turn13file0

## Already-planned work that should not be duplicated

These are already on the roadmap or handoff chain and should be treated as existing work, not new tasks:

- `TSK-0320` LLM evaluator fast-path correctness.  
- `TSK-0321` inventory sync guard / timeout behavior.  
- `TSK-0322` `ExecutionManager` JSON round-trip removal.  
- `TSK-0323` `HtnPlanner` sync-over-async deadlock risk.  
- `TSK-0324` safety config merge bug.  
- `TSK-0325` evaluator circuit breaker.  
- `TSK-0326` goal-identity guard in creative provisioning.  
- `TSK-0327` safety normalization at runtime.  
- `TSK-0328` plan-raw log noise reduction.  
- `TSK-0329` SignalR event-name drift. fileciteturn8file0turn9file0

## Assumptions and open questions

I assumed the exposed `dev/round-3` files and roadmap/handoff docs are the intended audit target, because the connector did not expose a repository tree walk. That means there may be auxiliary files outside the paths I could directly fetch. Confidence is highest on the runtime, planning, and host codepaths listed above. 

Open questions that should be verified before the next refactor pass:

- Is there any runtime command-gating check outside the paths reviewed here, or is the prompt gate truly the only guard?
- Is the `BlockMinedEvent` + `ItemCollectedEvent` double-increment still the accepted contract, or is there a later authoritative source already planned?
- Does `SafetyOptions` normalization happen anywhere else besides `Program.cs` and the `PostConfigure` block?
- Is the current creative provisioning race acceptable for short-term behavior, or should it be fixed before the next sprint closes?
- Are the legacy `HasFailed` fact keys intentionally retained for compatibility, or are they dead code that should be removed once `ExecutionContext` fully owns failure state?

## Confidence snapshot

- Stop-callback swallow in `ActionQueue`: **94%**
- Tool registration overwrite hazard: **91%**
- Command gating split / runtime omission risk: **88%**
- Creative provisioning race window: **87%**
- Inventory double-count drift risk: **84%**
- Legacy failure-path cleanup need: **90%**
- ABS over-ownership / consolidation need: **92%**
