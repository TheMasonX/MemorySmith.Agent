# Agent Handoff — Sprint 36 Complete (session 2026-06-22 part 2)

**Date:** 2026-06-22  
**Branch:** sprint-35-llm-first (HEAD: `cb1c56fefae7e785640d8f3602dca84588a7ce82`)  
**Previous handoff:** Data/Pages/Tasks/agent-handoff-sprint36-impl.md  
**Version:** v0.36.0 (bumped this session)

---

## What this session delivered

### External audit validation

Reviewed `Data/Pages/Audit/MemorySmith.Agent_Audit_2026-06-22.md` against all prior commits.
Three high-severity findings confirmed and addressed:

| # | Finding | Fix |
|---|---------|-----|
| 1 | P1-C wired in LlmChatInterpreter but NOT in Program.cs DI | commit cb1c56fe |
| 2 | ToolDispatcher.All nondeterministic + drops aliases | commit 5c26e4b9 RegisteredNames |
| 4 | CallWithOutcomeAsync would double-log when wired in Sprint 37 | commit 5c26e4b9 removed LogOutcome call |

Audit findings NOT addressed this sprint (by design):
- **Finding #3** (CallWithOutcomeAsync not in dispatch loop) — Sprint 37 scope. Handoff explicitly deferred.
- **Finding #5** (parser creates goal names) — Sprint 37 IntentManager work. Comment in LlmChatInterpreter.
- **Finding #6** (ItemConsumedEvent not wired) — Sprint 37. Documented as provisional inventory.
- **Finding #9** (silent world model coupling) — architecture note, Sprint 37.

### Commits this session (4)

| SHA | What |
|-----|------|
| `5c26e4b9` | `Agent.Tools/ToolDispatcher.cs` — add `RegisteredNames` property (sorted keys, includes aliases); remove `_journal?.LogOutcome(outcome)` from `CallWithOutcomeAsync` to prevent future double-logging |
| `e231d412` | `Agent.Core/Models/ActionOutcome.cs` — add `IObservationSummary` interface stub (P2-B part 1) |
| `c1ea0cd7` | `MemorySmith.Agent.Tests/Sprint36Tests.cs` — 5 new/updated tests: P0-B ×2, P1-B ×2, updated P1-C |
| `cb1c56fe` | `WebUI.Blazor/Program.cs` — wire `RegisteredNames` into `LlmChatInterpreter` DI; bump version to 0.36.0; fix `/api/agent/command` error to use `RegisteredNames` |

### New tests (5)

| Test | Validates |
|------|-----------|
| `CallWithOutcomeAsync_Success_ReturnsOutcomeAndDoesNotDoubleLog` | Returns correct (ToolResult, ActionOutcome) tuple; journal has exactly 1 entry (no dup from LogOutcome) |
| `CallWithOutcomeAsync_ToolFailure_ReturnsFailedOutcome` | Failure path threads GoalId + ToolName correctly |
| `ItemCraftedEvent_UpdatesInventory` | ApplyItemCrafted adds crafted item to WorldState.Inventory |
| `ItemCraftedEvent_StripsMinecraftPrefix` | minecraft: prefix stripped, bare key stored |
| `ToolDispatcher_RegisteredNames_IncludesAliasesAndIsSorted` | Replaces old All-based scaffold; verifies "Status" alias + alphabetical order |

---

## Full Sprint 36 commit log

```
cb1c56fe  feat: P1-C DI wiring + version bump 0.36.0
c1ea0cd7  test: P0-B + P1-B + updated P1-C
e231d412  feat: P2-B — IObservationSummary stub on ActionOutcome
5c26e4b9  feat: RegisteredNames + audit fix — no-dup-log in CallWithOutcomeAsync
29533024  Sprint 36 Audit (user-committed external audit doc)
691a9f2a  docs: Sprint 36 implementation handoff (session 2026-06-22)
20fe0151  feat: Sprint 36 P2-A — AgentRuntime record
c0968d1e  feat: Sprint 36 P2-A — IAgentRuntimeComponent interfaces
a7377f51  feat: P1-C — BuildSystemPrompt tool names
9b2948f5  feat: P1-B — ApplyItemCrafted wires ItemCraftedEvent to inventory
32ff8bb9  feat: P1-A — FactSource expansion
a7bbd1e2  feat: P0-B — ToolDispatcher.CallWithOutcomeAsync
a139643b  feat: P0-B — IAgentJournal.LogOutcome DIM
4b40998f  test: Sprint36Tests.cs
a0fe4546  feat: P0-C — DecomposeBuild SearchedRadius retry gate
8b62f98b  feat: P0-A — TryInterruptOnDamageAsync
bb3f19b5  docs: Sprint 36 AGENTS.md
be8142b0  test: fix CI regressions (BLK-S36-03)
```

---

## Self-reflection

**What went well:**
- Audit synthesis was clean. The external audit independently confirmed the exact same gaps the prior session's handoff already flagged — which is good signal. Implementing fixes was straightforward.
- `RegisteredNames` is strictly better than the handoff's proposed `All.Select(t=>t.Name)`: same single LINQ expression, zero extra code, catches aliases and is deterministic. Good catch in the audit.
- The Program.cs DI wiring was the only actually user-visible change (the LLM now gets tool names). All other changes were safety/correctness fixes that matter for Sprint 37 wiring.
- `SpyJournal` file-local test double is clean and reusable within the test class.

**What was deliberately skipped:**
- `AgentBackgroundService.DispatchActionsAsync` P2-B comment — the file is 80KB. A one-line comment isn't worth reading/re-encoding 80KB. Sprint 37 will modify this file substantially (wiring `CallWithOutcomeAsync`), so the comment will be added then.
- No council review per user instruction. Self-reflection only.

**Remaining risk:**
- `LlmChatInterpreter.ParseDecision` still maps LLM intent strings directly to `goalName` strings (e.g. `GatherItem:oak_log`). This violates CRITICAL Rule A-1 (parsers never create goals). The comment in the code calls this the "Sprint 35 transition layer." Sprint 37's IntentManager must fully remove this. Do NOT let another sprint pass without fixing it.
- `CallWithOutcomeAsync` is infrastructure only. It returns a correct `ActionOutcome` but nothing consumes it yet. The value materializes in Sprint 37 when `DispatchActionsAsync` is switched over.
- `IObservationSummary` is a one-line stub. Sprint 37 should make `ActionOutcome` implement it via `ObservationSummary` property.

---

## Sprint 37 priorities

**P0 — Wire the new infrastructure**
- [ ] `DispatchActionsAsync` → `CallWithOutcomeAsync` (replace the `toolCaller.CallAsync` call)
  - After switching: remove the now-redundant per-action journal entries from `CallAsync`
  - Instead: call `_journal?.LogOutcome(outcome)` explicitly in the dispatch loop
  - Add P2-B comment at same time (no longer need to fetch full 80KB separately)
- [ ] `ActionOutcome implements IObservationSummary` — one-line: `string IObservationSummary.Summary => ObservationSummary;`

**P1 — Intent→Goal separation (PRINCIPLE-1 enforcement)**
- [ ] Extract `ParseDecision` goal-mapping block into `IntentManager` class
- [ ] `LlmChatInterpreter` returns pure `IntentDraft` (no `GoalName`, no `Parameters`)
- [ ] `AgentBackgroundService.HandleChatEventAsync` receives `IntentDraft`, passes to `IntentManager`
- [ ] `IntentAssessment` wrapper: `{ IntentDraft, RiskLevel, RequiresConfirmation, ReasoningSummary }`

**P2 — AgentRuntime decomposition**
- [ ] `AgentBackgroundService` → `AgentRuntime.Tick()` pattern
- [ ] `ExecutionManager.EvaluateAsync(ActionOutcome[])` → LLM replanning
- [ ] Observation-driven loop: Plan → Execute → ActionOutcome → LLM Evaluate → Replan?

**Infrastructure health:**
- [ ] `ItemConsumedEvent` full wiring (ingredient deduction during craft)
- [ ] `AgentBackgroundService.cs` is 80KB — plan the split as part of P2 decomposition

---

## Key file paths changed this session

| File | Change |
|------|--------|
| `Agent.Tools/ToolDispatcher.cs` | `RegisteredNames` property; removed `_journal?.LogOutcome` from `CallWithOutcomeAsync` |
| `Agent.Core/Models/ActionOutcome.cs` | `IObservationSummary` stub interface added |
| `MemorySmith.Agent.Tests/Sprint36Tests.cs` | +5 tests; `ToolDispatcher_RegisteredNames_IncludesAliasesAndIsSorted` replaces old scaffold |
| `WebUI.Blazor/Program.cs` | `IChatInterpreter` DI uses `RegisteredNames`; v0.36.0; `/api/agent/command` uses `RegisteredNames` |
