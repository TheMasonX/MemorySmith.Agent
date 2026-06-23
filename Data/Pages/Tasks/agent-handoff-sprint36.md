# Agent Handoff — Sprint 36: AgentRuntime Decomposition + Observation-Driven Replanning

**Date:** 2026-06-22
**Branch:** sprint-35-llm-first (Sprint 35 complete, council APPROVED 0.857)
**Previous council doc:** Data/Pages/council/sprint35-impl-council-20260622.md (commit b7cc4145)
**Version target:** v0.36.0

---

## Context: what Sprint 35 locked in

Sprint 35 delivered the architectural foundation for Sprint 36's runtime decomposition:

1. **IntentDraft** — semantic intent with no GoalName. Planner maps intent → goal. (P1-A)
2. **ActionOutcome** — universal tool result scaffold. Wiring is partial (records exist, ToolDispatcher still returns ToolResult). (P1-E)
3. **Inventory event-sourced** — ItemCollectedEvent is the authority; BlockMined only stores facts. (P0-A)
4. **LLM owns intent** — gather/build/craft removed from fast-paths. (P1-B/D)
5. **FlatAreaFoundEvent.SearchedRadius** — BuildGoalDecomposer can gate retry. (P0-C)
6. **BuildOriginSource enum** — explicit/autoscanned/playerpos. Maps to Fact.Source in Sprint 36. (P0-C)

---

## Sprint 35 deferred items to resolve first

### BLK-S36-01 — AGENTS.md must be updated before implementation (P2-C from Sprint 35)
Before writing any new code, commit AGENTS.md with:
- "Parsers never create goals" as a CRITICAL rule
- IntentDraft schema + confidence threshold documentation
- ActionOutcome as universal tool result artifact
- playerCollect guard (`collector.username !== bot.username`)
- mineComplete event contract
- Sprint 36/37 deferred item references

This is the standing Rule: council doc first, then code.

---

## Blocking pre-conditions (resolve before P0)

### BLK-S36-02 — Read AgentBackgroundService.cs before ANY P0 work
AgentBackgroundService.cs is 80KB. Before Sprint 36, an agent MUST read it fully or in targeted sections:
- DispatchActionsAsync (for ActionOutcome wiring and ClearAndEnqueueAsync integration)
- ProcessEventsAsync (for FlatAreaFoundEvent handling and BuildGoalDecomposer retry)
- HandleChatEventAsync (for IntentDraftToGoal bridge location)
- TryRecoverFromGameErrorAsync (for Sprint 36 RecoveryManager spec)
Read it first. Document in handoff what you found.

### BLK-S36-03 — Verify CI green on sprint-35-llm-first
Run or check CI on sprint-35-llm-first HEAD (b7cc4145). All existing 276+ tests must pass.
The known risk: LlmChatInterpreterTests may have tests for NavigateTo fast-path that was removed in Sprint 35 P1-B. Investigate and fix if needed.

---

## P0 — Complete Sprint 35 partial implementations

### P0-A: Wire ClearAndEnqueueAsync in AgentBackgroundService
**Files:** WebUI.Blazor/AgentBackgroundService.cs (requires BLK-S36-02)
- Find all ClearAndEnqueue call sites in DispatchActionsAsync
- Replace with: `await _actionQueue.ClearAndEnqueueAsync(action, () => _bridge.SendAsync(stopAction, ct))`
- Add test: replan with active Dispatched correlation → stop event sent first

### P0-B: Wire ActionOutcome in ToolDispatcher
**Files:** Agent.Tools/ToolDispatcher.cs, Agent.Core/Interfaces/IAgentJournal.cs (if exists), Agent.Core/Journal/AgentJournal.cs
- Add `LogOutcome(ActionOutcome outcome)` to IAgentJournal interface
- Add `Task<(ToolResult, ActionOutcome)> CallWithOutcomeAsync(...)` to ToolDispatcher
- OR: change CallAsync to return ActionOutcome (wrapping ToolResult) — requires IToolCaller change
- AgentJournal records ActionOutcome per execution
- 3 tests: MineBlock → ItemCollected effect; failed tool → Failed outcome; journal records it

### P0-C: BuildGoalDecomposer SearchedRadius retry gate
**Files:** Agent.Planning/Decomposition/BuildGoalDecomposer.cs, Agent.Planning/HtnTaskLibrary.cs
- When FlatAreaFoundEvent.Area == 0 AND SearchedRadius < 48: retry with larger radius
- When SearchedRadius >= 48 and Area == 0: fail with "no flat ground found in range"
- Test: SearchedRadius=32, Area=0 → retry action emitted; SearchedRadius=48, Area=0 → no retry

---

## P1 — Fact provenance + inventory vocabulary expansion

### P1-A: Full Fact.Source enum expansion
**Files:** Agent.Core/WorldState.cs (FactSource enum)
- Add: PlayerInstruction | Memory | Scan | Recovery to existing Observed | Inferred | Durable
- BuildOriginSource.Explicit maps to PlayerInstruction; AutoScanned maps to Scan
- No runtime behavior changes — metadata only for diagnostics and Sprint 36 D reasoning

### P1-B: Complete inventory event vocabulary
**Files:** Agent.Core/Events/WorldEvents.cs, Agent.Core/WorldStateProjector.cs
Wire the Sprint 35 stubs:
- ItemCraftedEvent: on CraftCompleteEvent, infer items crafted (via WorldStateProjector)
  OR: have AgentBackgroundService produce ItemCraftedEvent from CraftCompleteEvent + known recipe
- ItemConsumedEvent: stub only — emit when craft recipe requires ingredients (Sprint 37)
- Add ApplyItemCrafted to WorldStateProjector: AddInventoryItem for crafted output
- 2 tests: CraftCompleteEvent → ItemCrafted; ItemCraftedEvent updates inventory

### P1-C: Tool names in LLM system prompt
**Files:** Agent.Planning/LlmChatInterpreter.cs
- BuildSystemPrompt: add comma-separated tool names from ToolDispatcher.RegisteredTools
- The interpreter currently doesn't have access to ToolDispatcher; may need DI injection
- OR: pass registered tool names as a BuildSystemPrompt parameter
- Test: BuildSystemPrompt contains registered tool names

### P1-D: AGENTS.md playerCollect version safety note
**Files:** AGENTS.md
- Add note: `entity?.metadata?.name` is Mineflayer version-specific
- Safe pattern: `entity?.metadata?.find(m => m?.value?.name)?.value?.name ?? entity?.name ?? 'unknown'`

---

## P2 — AgentRuntime decomposition (Sprint 36 architectural milestone)

### P2-A: Define AgentRuntime interface (do NOT split AgentBackgroundService yet)
**Files:** new Agent.Core/Runtime/AgentRuntime.cs, new Agent.Core/Runtime/IAgentRuntimeComponent.cs
- Define the 6 manager interfaces (not implementations):
  ```csharp
  interface IIntentManager { Task<IntentDraft?> ProcessChatAsync(...); }
  interface IPlanningManager { Task<ActionPlan> PlanAsync(...); void Replan(); }
  interface IExecutionManager { Task DispatchAsync(ActionData action, CancellationToken ct); }
  interface IRecoveryManager { Task<bool> TryRecoverAsync(ErrorEvent error, WorldState state); }
  interface IStateManager { WorldState Current; void Apply(WorldEvent ev); }
  interface IDashboardPublisher { Task PublishStatusAsync(AgentStatus status); }
  ```
- Create AgentRuntime record holding all 6 managers (Sprint 37: wire to AgentBackgroundService)

### P2-B: Observation-driven replanning foundation
**Files:** Agent.Planning/LlmChatInterpreter.cs, Agent.Core/Models/ActionOutcome.cs
- Add to ActionOutcome: an `IObservationSummary` interface concept for Sprint 36 LLM evaluation
- Add AgentBackgroundService concept-comment: "// Sprint 36: after each ActionOutcome, LLM evaluates if current plan is still valid"
- No runtime behavior change — just the scaffold and documentation

---

## Sprint 37 roadmap (do not implement in Sprint 36)

- Full AgentRuntime wiring: AgentBackgroundService → while(running) { runtime.Tick(); }
- IntentAssessment wrapping IntentDraft: { Draft, RiskLevel, RequiresConfirmation, ReasoningSummary }
- Observation-driven replanning: Plan → Execute → ActionOutcome → LLM Evaluate → Replan?
- ItemConsumedEvent full wiring (ingredients consumed during craft)
- ItemDroppedEvent (blocks placed during build)

---

## Required tests summary

| Task | Tests |
|---|---|
| P0-A | ClearAndEnqueueAsync with active dispatch → stop sent first |
| P0-B | ToolDispatcher.CallWithOutcomeAsync → ActionOutcome with correct Effects; AgentJournal records it |
| P0-C | SearchedRadius=32,Area=0 → retry action; SearchedRadius=48,Area=0 → no retry |
| P1-B | CraftCompleteEvent → ItemCraftedEvent updates inventory |
| P1-C | BuildSystemPrompt contains tool names |

All existing 276+ tests (+ Sprint 35's 21 tests = 297+) must pass.

---

## Version bump

- `/api/about`: v0.36.0, Phase = "Sprint 36 — AgentRuntime + Observation-Driven Replanning"

---

## Deferred from Sprint 35 (still open)

| ID | Item |
|---|---|
| D1 | Mineflayer entity?.metadata?.name version sensitivity |
| D6 | playerCollect 'unknown' inventory noise |

---

## Key file paths for Sprint 36

| File | Relevant to |
|---|---|
| WebUI.Blazor/AgentBackgroundService.cs | P0-A: ClearAndEnqueueAsync call sites; chat handling |
| Agent.Tools/ToolDispatcher.cs | P0-B: ActionOutcome wiring |
| Agent.Planning/Decomposition/BuildGoalDecomposer.cs | P0-C: SearchedRadius retry gate |
| Agent.Core/Events/WorldEvents.cs | P1-B: ItemCraftedEvent wiring |
| Agent.Planning/LlmChatInterpreter.cs | P1-C: tool names in prompt |
| Agent.Core/Models/ActionOutcome.cs | P2-B: observation-driven scaffold |
