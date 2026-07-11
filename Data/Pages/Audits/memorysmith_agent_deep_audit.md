# MemorySmith.Agent Deep Code Audit

**Repo:** `TheMasonX/MemorySmith.Agent`  
**Branch:** `dev/round-3`  
**Commit:** `eceb01e2a226e3a3445f2e02d019cb3b86b97e7d`  
**Date:** 2026-07-05

## Executive summary

This codebase is moving in the right direction architecturally. The docs clearly define a canonical execution context, explicit safety layers, and a removal-first policy for transitional bridges. The active sprint notes also show real correctness work landing around stale inventory, placement confirmation, and safety normalization. The strongest evidence is in the architecture doc, sprint handoffs, and current host/runtime code. fileciteturn12file0 fileciteturn13file0 fileciteturn17file0

The primary risk is residual legacy complexity. `AgentBackgroundService` still owns a large amount of runtime policy and mutable state, several bridge layers still rely on mutable dictionaries and implicit merge behavior, and at least one critical stop/recovery path still swallows failures without emitting an observable signal. fileciteturn19file0 fileciteturn38file0 fileciteturn44file0

The highest-value next step is to finish collapsing the bridge layers: push policy into typed runtime objects, replace prefix-based context carry with typed plan context, remove dead fallback branches, and make interrupt/recovery failures observable. That is the fastest way to reduce hidden-state bugs in a greenfield system that wants to avoid technical debt. fileciteturn12file0 fileciteturn13file0 fileciteturn31file0

## Priority findings

| Severity | Finding | Why it matters | Confidence |
|---|---|---|---:|
| High | Stop failures are swallowed in `ActionQueue.ClearAndEnqueueAsync` | A failed emergency stop can disappear without telemetry while the queue still mutates as if recovery succeeded. | 92% |
| High | Host service still concentrates too much policy | `AgentBackgroundService` still owns goal lifecycle, replan policy, freshness gates, creative provisioning, correlation state, and evaluator suppression. | 88% |
| Medium-High | Planner fallback and context preservation still feel transitional | `HtnPlanner` still has compatibility-style fallback behavior and prefix-based context preservation. | 79% |
| Medium | Action payloads are mutable dictionaries | `ActionData.Arguments` and `Context` are mutable and merged at runtime, which is a hidden-contract hazard. | 84% |
| Medium | Goal failure contracts are inconsistent | `SmeltGoal.HasFailed()` is hardcoded false, while other goals use fact-based failure detection. | 68% |
| Medium | Legacy fact/state duality remains | `WorldState` still carries both legacy `Facts` and `StructuredFacts`. | 76% |

## Detailed findings

### 1) Silent stop failures in `ActionQueue`

`ActionQueue.ClearAndEnqueueAsync` intentionally swallows exceptions from the stop callback. That preserves queue mutation, but it also means the caller cannot tell whether the adapter actually received the stop signal. This is especially risky because the method is used in the damage-interrupt path. fileciteturn19file0

**Recommendation:** keep the atomic queue behavior, but surface stop failures through a warning log or a lightweight result object.  
**Confidence:** 92%

### 2) `AgentBackgroundService` still owns too much policy

The architecture says `ExecutionContext` should be canonical and the host should stay orchestration-only. The runtime file still carries many mutable fields and policy decisions, including health interrupts, inventory freshness gating, plan sequencing, creative provisioning, command deny-list handling, and evaluator circuit breaking. fileciteturn12file0 fileciteturn31file0 fileciteturn38file0 fileciteturn44file0 fileciteturn51file0 fileciteturn54file0

**Recommendation:** extract runtime policy into typed helpers and reduce the host to event orchestration. Start with the inventory freshness gate, recovery state, and action lifecycle state.  
**Confidence:** 88%

### 3) Planner fallback still preserves stale context by prefix

`HtnPlanner.ReplanAsync` preserves context entries by prefix (`CraftItem:`, `FindFlatArea:`, `Build:`, `MoveTo:`). That is a pragmatic bridge, but it is also easy to over-retain stale values across replans. `PlanAsync` also keeps a generic fallback path after typed goal handling. fileciteturn24file0 fileciteturn25file0

**Recommendation:** replace prefix-based carry with a typed `PlanContext` and keep only the minimal, explicitly required values.  
**Confidence:** 79%

### 4) Mutable action payloads remain a hidden contract

`ActionData.Arguments` and `Context` are mutable dictionaries. The dispatch loop merges schema-permitted context keys into arguments at runtime. That gives flexibility, but it also makes state flow implicit and easy to misuse. fileciteturn20file0 fileciteturn53file0

**Recommendation:** move toward immutable action arguments and a typed plan context. Let the runtime explicitly project context into tool arguments instead of inferring it from dictionary keys.  
**Confidence:** 84%

### 5) Goal failure semantics are inconsistent

`GenericGatherGoal`, `CraftItemGoal`, and `BuildGoal` use fact-based completion/failure checks. `SmeltGoal` returns `false` from `HasFailed()` unconditionally, which is acceptable only if the runtime has an explicit separate failure channel and that is the intended contract. `PlaceBlockGoal` uses a dispatch-count completion rule and inventory-based failure rule. fileciteturn21file0 fileciteturn22file0 fileciteturn23file0 fileciteturn35file0 fileciteturn37file0

**Recommendation:** formalize whether some goals are “completion-only” and document that in the interface contract. Otherwise add a real failure write path for `SmeltGoal`.  
**Confidence:** 68%

### 6) Legacy fact/state duality remains a correctness risk

`WorldState` still supports both `Facts` and `StructuredFacts`, and many checks still read the legacy map. The builder supports both old and new write paths. This is fine during migration, but it is also a split-brain hazard if new code starts writing one path and reading the other. fileciteturn34file0

**Recommendation:** set a removal target for legacy-only writes and add tests that reject new production paths writing only to the old map.  
**Confidence:** 76%

## What already looks good

- `CommandExecutionEnabled` now defaults to `false`, which is safer for command execution. fileciteturn60file0
- `SafetyOptions` is explicitly framed as a hard safety floor, and runtime deny-list handling merges built-in defaults rather than replacing them. fileciteturn59file0 fileciteturn57file0
- `PlaceBlockGoal` completion is now tied to confirmed placement events rather than decomposition-time assumptions. fileciteturn36file0 fileciteturn37file0 fileciteturn50file0
- Inventory freshness gating is explicit in both the goal types and the dispatch loop. fileciteturn21file0 fileciteturn22file0 fileciteturn23file0 fileciteturn52file0

## Assumptions

- The fetched branch content is the code intended for review, and the docs in `Data/Pages` are the current intended architecture. fileciteturn12file0 fileciteturn14file0
- Sprint notes are treated as intended direction unless the code contradicts them.
- Greenfield cleanup is prioritized over backward compatibility unless the architecture doc explicitly marks a bridge as permanent. fileciteturn13file0

## Open questions

- Is the remaining fallback code in `HtnPlanner` still intentionally reachable, or should it be deleted after direct decomposers cover the current goals?
- Are there still production paths writing only legacy `Facts` without `StructuredFacts`?
- Should `SmeltGoal` gain a real failure write path, or should failure be documented as runtime-only for that goal?
- Can stop failures be surfaced from `ActionQueue` without forcing logger dependencies into the queue type?
- Are all temporary bridge entries in the architecture doc still accurate, or have some become stale themselves? fileciteturn13file0

## Recommended implementation order

1. Make stop failures observable in `ActionQueue`.
2. Replace prefix-based context carry with typed plan context.
3. Extract more runtime policy from `AgentBackgroundService` into typed runtime helpers.
4. Finish collapsing the legacy fact/state duality.
5. Normalize goal failure semantics across all goals.
