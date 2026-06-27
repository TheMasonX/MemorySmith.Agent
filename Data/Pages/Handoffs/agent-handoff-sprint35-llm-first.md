# Agent Handoff — Sprint 35: Inventory Truth + LLM-First (v2)

**Date:** 2026-06-22 (updated post-second-agent review)
**Branch:** main
**Council doc:** Data/Pages/council/sprint35-council-20260622.md
**Version target:** v0.35.0

> NOTE: A separate handoff file (agent-handoff-sprint35.md) exists for the creative-mode
> planner/logging regression. Resolve that first if those tests are still failing.
> This document covers the audit-derived runtime bugs and LLM-first architecture changes.

---

## Architecture decisions (locked in Sprint 35)

Three audits now converge on the same model. These are locked constraints:

**1. Three-layer ownership**
- LLM owns intent: chat → LLM → IntentDraft → planner → goal
- Deterministic tools own execution: ToolDispatcher schema validation is the safety wall
- WorldStateProjector owns state: sole canonical reducer, no other state mutation

**2. Parsers never create goals (NEW — locked in Sprint 35)**
Parsers produce `IntentDraft` only. Goals are created exclusively by the planner layer.
The path `Chat → Interpreter → Goal` is replaced by `Chat → IntentDraft → Planner → Goal`.
- No `ChatIntentType.CreateGoal` propagating GoalName strings out of the interpreter
- Interpreter expresses: what intent, what item/blueprint, what count, what coords, confidence
- Planner decides: what goal type to create, whether to chain goals, whether to replan
- This decouples the interpreter from GoalFactory conventions entirely

**3. ActionOutcome is the universal tool result artifact (NEW — Sprint 35 P1-E)**
Every ToolDispatcher execution produces an `ActionOutcome`. Recovery, replanning, memory,
journaling, and world-state updates all consume this — not raw adapter events.

**4. Inventory becomes event-sourced (Sprint 35 starts, Sprint 36 completes)**
Sprint 35 adds the first authoritative inventory events (playerCollect, post-craft sendBotStatus).
Sprint 36 completes the vocabulary: ItemCraftedEvent, ItemConsumedEvent, ItemDroppedEvent.
Final flow: events → WorldStateProjector → inventory projection → periodic GetStatus reconciliation.

**5. Deterministic fast-paths: stop/cancel, status, inventory, help only**
Everything else → LLM.

---

## Blocking pre-conditions (resolve BEFORE P0-D and P1)

### BLK-S35-01 — IntentDraft confidence + no GoalName
`IntentDraft` record must:
- Include `double Confidence` (0.0–1.0) and `string? ClarificationQuestion`
- NOT include a `GoalName` or `GoalParameters` field (those belong to the planner)
- Express intent semantically: Intent="gather", Item="oak_log" — not "GatherItem:oak_log"

### BLK-S35-02 — Read AgentBackgroundService.cs before P0-D
Read ClearAndEnqueue, DispatchActionsAsync, and TryRecoverFromGameErrorAsync sections.
Confirm stop-on-replan injection point. Document TryRecoverFromGameErrorAsync pattern for Sprint 36.

---

## Confirmed bugs from audit

| Bug | Root cause | File(s) |
|---|---|---|
| Inventory only updates on spawn/GetStatus | sendBotStatus() in 2 places only; block name ≠ drop (diamond_ore → diamond) | index.js, WorldStateProjector.cs |
| Mining reliability + replan gap | No mineComplete event; ClearAndEnqueue doesn't stop JS first | index.js, ActionQueue.cs |
| Build finds wrong place | FlatAreaFoundEvent missing SearchedRadius; build origin silently auto-set | WebSocketBridge.cs, FlatAreaFoundEvent.cs |
| Command parsing brittle | LLM bypassed for all regex-recognized commands (step 4 early return) | LlmChatInterpreter.cs |

---

## P0 — Runtime bug fixes

### P0-A: Inventory truth via playerCollect + post-action snapshot
**Files:** `MineflayerAdapter/index.js`, `Agent.World.Minecraft/WebSocketBridge.cs`,
`Agent.Core/WorldStateProjector.cs`, new `Agent.Core/Events/ItemCollectedEvent.cs`

1. index.js: hook `bot.on('playerCollect', (collector, entity) => { if (collector.username !== bot.username) return; sendEvent('itemCollected', { item: entity.metadata?.name ?? entity.displayName, count: entity.count ?? 1 }); })`
2. index.js: call `sendBotStatus()` at end of `craft` and `smelt` cases
3. WebSocketBridge: parse `itemCollected` → `ItemCollectedEvent(string Item, int Count, DateTimeOffset Timestamp)`
4. WorldStateProjector: `ApplyItemCollected` → `AddInventoryItem(e.Item, e.Count)` (uses actual drop name)

**Tests required:**
- diamond_ore mined → WorldState.Inventory["diamond"] increments (not "diamond_ore")
- iron_ore mined → WorldState.Inventory["raw_iron"]
- stone mined → WorldState.Inventory["cobblestone"]
- sendBotStatus fires after craft/smelt

**Acceptance:** mine 5 diamond_ore → WorldState.Inventory["diamond"] = 5

### P0-B: mineComplete event
**Files:** `MineflayerAdapter/index.js`, `Agent.World.Minecraft/WebSocketBridge.cs`,
new `Agent.Core/Events/MineCompleteEvent.cs`, `WebUI.Blazor/AgentBackgroundService.cs`

1. index.js mine case: after while loop exits, `sendEvent('mineComplete', { block: shortName, mined, targetCount: count, correlationId })`
2. WebSocketBridge: parse → `MineCompleteEvent(string Block, int Mined, int TargetCount, DateTimeOffset Timestamp)`
3. AgentBackgroundService: on MineCompleteEvent → transition correlatedAction to Completed

**Tests required:**
- MineCompleteEvent received; correlationId matches dispatch
- Correlation lifecycle: Dispatched → Completed

### P0-C: FlatAreaFoundEvent.SearchedRadius + BuildGoal.OriginSource
**Files:** `Agent.Core/Events/FlatAreaFoundEvent.cs`, `Agent.World.Minecraft/WebSocketBridge.cs`,
`Agent.Planning/Decomposition/BuildGoalDecomposer.cs`, `Agent.Planning/Goals/BuildGoal.cs`

1. Add `int SearchedRadius` to `FlatAreaFoundEvent` record
2. Parse `searchedRadius` in WebSocketBridge flatAreaFound handler
3. Add `BuildOriginSource` enum: `Explicit | PlayerPosition | AutoScanned`
4. DecomposeBuild retry: gate on `SearchedRadius < 48` (not just `Area == 0`)
5. LogWarning when OriginSource == AutoScanned before construction begins

Note: OriginSource is a preview of Sprint 36's full Fact.Source enum
(PlayerInstruction | Observation | Memory | Inference | Scan | Recovery).
Design OriginSource to be trivially mappable to that enum in Sprint 36.

**Tests required:**
- SearchedRadius parsed correctly from JSON
- OriginSource.Explicit when originX/Y/Z provided; AutoScanned when not
- No retry when SearchedRadius=48 and area=0

### P0-D: Stop-on-replan (AFTER BLK-S35-02 read)
**Files:** `WebUI.Blazor/AgentBackgroundService.cs`, `Agent.Core/Models/ActionQueue.cs`

1. Read ClearAndEnqueue + DispatchActionsAsync sections first
2. When ClearAndEnqueue called with active Dispatched entries → send stop via bridge first
3. Add test: replan during active dispatch → stop event sent before new actions

---

## P1 — LLM-first architecture

### P1-A: IntentDraft record (resolves BLK-S35-01, enforces "parsers never create goals")
**File:** new `Agent.Planning/IntentDraft.cs`

```csharp
/// <summary>
/// Semantic intent produced by the LLM interpreter. Contains NO goal construction
/// logic — parsers produce intent, the planner layer creates goals.
/// Sprint 36 evolution: wrapped in IntentAssessment{ Draft, RiskLevel, RequiresConfirmation, ReasoningSummary }.
/// </summary>
public record IntentDraft(
    string Addressed,               // "yes" | "maybe" | "no"
    string Intent,                  // "gather" | "build" | "craft" | "navigate" | "cancel"
                                    // "status" | "conversation" | "clarify" | "ignore"
    string? Item,                   // Minecraft item ID, no namespace prefix
    string? Blueprint,              // Blueprint ID
    int? Count,
    int? X, int? Y, int? Z,
    double Confidence,              // 0.0-1.0
    string? ClarificationQuestion,  // non-null when Confidence < threshold
    string Response                 // In-game reply text (max 50 words)
);
```

Note: no GoalName, no GoalParameters. The planner maps Intent+Item → GoalFactory call.
Add `LlmConfidenceThreshold` (default 0.6) to `ChatOptions`.

### P1-B: Flip LlmChatInterpreter pipeline + intent→goal bridge
**File:** `Agent.Planning/LlmChatInterpreter.cs`, `WebUI.Blazor/AgentBackgroundService.cs`

LlmChatInterpreter changes:
- Remove step 4 early return block
- Keep ONLY 4 fast-paths: stop/cancel, status, inventory, help
- All other inputs → LLM → IntentDraft
- If Confidence < threshold and ClarificationQuestion != null → send via bot.chat, return Unknown

AgentBackgroundService: add `IntentDraftToGoal(IntentDraft)` transition method:
- Intent="gather", Item="oak_log" → GoalFactory.CreateAsync("GatherItem:oak_log", ...)
- Intent="build", Blueprint="small-house" → GoalFactory.CreateAsync("Build:small-house", ...)
- Intent="craft", Item="iron_pickaxe" → GoalFactory.CreateAsync("CraftItem:iron_pickaxe", ...)
- Intent="navigate", X/Y/Z → GoalFactory.CreateAsync("MoveTo", ...)

This is the Sprint 35 transition layer. Sprint 36 moves it to IntentManager.

**Test required:** "get me some wood" → reaches LLM, IntentDraft produced (not ChatInterpretation with GoalName)

### P1-C: Context enrichment for LLM
**File:** `Agent.Planning/LlmChatInterpreter.cs` (BuildSystemPrompt)

Add to system prompt:
- Inventory: non-zero items sorted desc, e.g. `"oak_log: 12, stone: 4"`
- Position + HP + food
- Active goal name or "idle"
- Last tool error (200 chars max)
- Tool names from ToolDispatcher.RegisteredTools

**Test required:** BuildSystemPrompt string contains inventory and tool names

### P1-D: Remove regex intent matching from ChatInterpreter mainline
**File:** `Agent.Planning/ChatInterpreter.cs`

- Delete GatherRegex, BuildRegex, CraftRegex match blocks from ParseIntent
- Move ResolveCraftId, ResolveItemId, ResolveBlueprintId to new static `KnownAliases` class
- ParseIntent reduced to 4 patterns: stop/cancel, status, inventory, help

### P1-E: ActionOutcome record — universal tool result artifact
**Files:** new `Agent.Core/Models/ActionOutcome.cs`, new `Agent.Core/Models/StructuredEffect.cs`,
`Agent.Tools/ToolDispatcher.cs`, `WebUI.Blazor/AgentBackgroundService.cs`

```csharp
/// <summary>
/// Typed effect produced by a single tool execution. Used by ActionOutcome.
/// Type vocabulary: ItemCollected, ItemConsumed, ItemCrafted, PositionChanged,
/// BlockPlaced, BlockMined, StatusRefreshed, MemorySearched, MemoryPageCreated.
/// </summary>
public record StructuredEffect(
    string Type,
    string? Item = null,
    int? Count = null,
    string? Detail = null
);

/// <summary>
/// Universal result artifact for every ToolDispatcher.CallAsync execution.
/// Recovery, replanning, memory, journaling, and world-state updates all consume
/// this rather than interpreting raw adapter events separately.
/// Sprint 36: LLM receives ActionOutcome[] in its evaluation context for
/// observation-driven replanning (Plan → Execute → Observe → Evaluate → Replan).
/// </summary>
public record ActionOutcome(
    Guid GoalId,
    string ToolName,
    bool Success,
    string ObservationSummary,
    IReadOnlyList<StructuredEffect> Effects,
    DateTimeOffset Timestamp
);
```

Example ActionOutcome for MineBlock(oak_log, 5) success:
```json
{
  "tool": "MineBlock", "success": true,
  "observationSummary": "Mined 5 oak_log at (100,64,200)",
  "effects": [
    { "type": "ItemCollected", "item": "oak_log", "count": 5 },
    { "type": "PositionChanged" }
  ]
}
```

Sprint 35 wiring:
- `ToolDispatcher.CallAsync` returns `ActionOutcome` (wrapping existing `ToolResult`)
- `AgentJournal` records `ActionOutcome` per execution
- `AgentBackgroundService` uses `ActionOutcome.Effects` for WorldState update hints

**Tests required:**
- MineBlock produces ActionOutcome with Effects=[{ItemCollected, oak_log, N}]
- Failed tool produces ActionOutcome{Success=false, ObservationSummary contains error}
- AgentJournal records ActionOutcome (1 per tool call)

---

## P2 — Cleanup

### P2-A: Read TryRecoverFromGameErrorAsync + inventory event stubs
- Read the method in AgentBackgroundService.cs; document in AGENTS.md
- Add stub records (no wiring yet): `ItemCraftedEvent`, `ItemConsumedEvent`
  with XML doc comment marking them as Sprint 36 stubs

### P2-B: CommonMinecraftBlocks additions
Add: copper_ore, deepslate_copper_ore, deepslate_iron_ore, deepslate_gold_ore,
deepslate_diamond_ore, deepslate_coal_ore, deepslate_lapis_ore, deepslate_redstone_ore,
deepslate_emerald_ore

### P2-C: AGENTS.md architecture update
- Three-layer ownership model
- **"Parsers never create goals" principle** (CRITICAL — future agents must not violate this)
- IntentDraft schema + confidence threshold
- ActionOutcome as universal tool result artifact
- playerCollect guard: `collector.username !== bot.username`
- mineComplete event contract
- Sprint 36/37 deferred items

---

## Sprint 36 roadmap (do not implement in Sprint 35)

Architecturally agreed; reference in code comments as `// Sprint 36: ...`.

**36-A: IntentAssessment** — wrap IntentDraft with risk/confirmation:
```csharp
record IntentAssessment(IntentDraft Draft, RiskLevel Risk, bool RequiresConfirmation, string ReasoningSummary);
enum RiskLevel { Low, Medium, High }
// "Build a house" → High risk, RequiresConfirmation=true even at Confidence=0.95
// "Come here"    → Low risk,  RequiresConfirmation=false even at Confidence=0.7
```

**36-B: AgentRuntime decomposition** — split 80KB AgentBackgroundService:
```
AgentRuntime
 ├─ IntentManager      (IntentDraft → IntentAssessment → goal dispatch)
 ├─ PlanningManager    (HTN, decomposition, replan decisions)
 ├─ ExecutionManager   (ActionQueue, ToolDispatcher, correlation lifecycle)
 ├─ RecoveryManager    (generalized recovery policies from TryRecoverFromGameErrorAsync)
 ├─ StateManager       (WorldState, WorldStateProjector, stale flags)
 └─ DashboardPublisher (SignalR, status events)
AgentBackgroundService → while(running) { runtime.Tick(); }
```

**36-C: Observation-driven replanning** — LLM as evaluator:
```
Plan → Execute → ActionOutcome → LLM Evaluate → Replan? → Execute
```
LLM receives recent ActionOutcome[] and decides whether current plan still valid.
This is the path from command-driven bot to genuine agent runtime.

**36-D: Fact provenance expansion** — generalize OriginSource:
```csharp
enum FactSource {
    Observed, Inferred, Durable,             // existing
    PlayerInstruction, Memory, Scan, Recovery // new
}
```
Build origin becomes Fact{Source=PlayerInstruction|Scan} rather than BuildGoal.OriginSource.

**36-E: Full inventory event vocabulary:**
```
ItemCollectedEvent  ✓ Sprint 35
ItemCraftedEvent    Sprint 36 (stub added Sprint 35)
ItemConsumedEvent   Sprint 36 (stub added Sprint 35)
ItemDroppedEvent    Sprint 36
ContainerTransferredEvent Sprint 36
```

---

## Required tests summary

| Task | Test |
|---|---|
| P0-A | diamond_ore → Inventory["diamond"]; iron_ore → raw_iron; stone → cobblestone |
| P0-B | MineCompleteEvent received; correlationId Dispatched→Completed |
| P0-C | SearchedRadius parsed; OriginSource enum; no retry at max radius |
| P0-D | Stop sent on replan with active dispatch |
| P1-A | IntentDraft produced; no GoalName field; Confidence < threshold → clarification |
| P1-B | Non-trivial chat reaches LLM (not early-returned by regex) |
| P1-C | BuildSystemPrompt contains inventory + tool names |
| P1-E | ActionOutcome produced per tool call; Effects typed correctly; Journal records it |

All existing 276+ tests must still pass.

---

## Version bump

- `/api/about`: v0.35.0, Phase = "Sprint 35 — Inventory Truth + LLM-First"

---

## Deferred

| ID | Sprint | Item |
|---|---|---|
| D-S35-01 | 35 P1 | Wire ClarificationQuestion → bot.chat in event loop |
| D-S35-02 | 36-C | Observation-driven replanning (LLM as evaluator loop) |
| D-S35-03 | 36-B | AgentRuntime decomposition |
| D-S35-04 | 36-A | IntentAssessment with RiskLevel + RequiresConfirmation |
| D-S35-05 | 36-D | Full fact provenance expansion (Fact.Source enum) |
| D-S35-06 | 36-E | ItemCraftedEvent, ItemConsumedEvent full wiring |
| D-S35-07 | 37 | Richer adapter observations (reachability, face selection, pathing) |
