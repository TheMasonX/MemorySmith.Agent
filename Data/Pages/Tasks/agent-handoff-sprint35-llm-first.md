# Agent Handoff — Sprint 35: Inventory Truth + LLM-First

**Date:** 2026-06-22  
**Branch:** main  
**Council doc:** Data/Pages/council/sprint35-council-20260622.md  
**Version target:** v0.35.0

> NOTE: A separate handoff file (agent-handoff-sprint35.md) exists for the creative-mode
> planner/logging regression. Resolve that first if those tests are still failing.
> This document covers the audit-derived runtime bugs and LLM-first architecture changes.

---

## Architecture decision (locked in Sprint 35)

**Three-layer ownership model:**
1. **LLM owns intent** — all non-trivial chat → LLM → `IntentDraft` → validate → goal
2. **Deterministic tools own execution** — ToolDispatcher schema validation remains the safety wall
3. **WorldStateProjector owns state** — sole reducer; no direct inventory mutation elsewhere

**Deterministic shortcuts (regex still allowed):** stop/cancel/abort, status/report, inventory/items, help/commands. Everything else → LLM.

---

## Blocking pre-conditions (resolve BEFORE P0-D and P1)

### BLK-S35-01 — IntentDraft confidence field
`IntentDraft` record must include `double Confidence` and `string? ClarificationQuestion`.  
If Confidence < 0.6 (configurable) → send ClarificationQuestion via bot.chat, return Unknown — do NOT create a goal.

### BLK-S35-02 — Read AgentBackgroundService.cs before P0-D
Read `ClearAndEnqueue`, `DispatchActionsAsync`, `TryRecoverFromGameErrorAsync` sections before coding P0-D. Confirm injection point for stop-on-replan and document existing recovery pattern for Sprint 36.

---

## Confirmed bugs from audit (evidence)

| Bug | Root cause | File(s) |
|---|---|---|
| Inventory only updates on spawn/GetStatus | sendBotStatus() in 2 places only; craft/smelt/place emit no inventory; block name ≠ drop (diamond_ore → diamond) | index.js, WorldStateProjector.cs |
| Mining reliability + replan gap | No mineComplete event; ClearAndEnqueue doesn't stop JS first | index.js, ActionQueue.cs |
| Build finds wrong place | FlatAreaFoundEvent missing SearchedRadius; build origin silently auto-set | WebSocketBridge.cs, FlatAreaFoundEvent.cs |
| Command parsing brittle | LLM bypassed for all regex-recognized commands (step 4 early return) | LlmChatInterpreter.cs |

---

## P0 — Runtime bug fixes

### P0-A: Inventory truth via playerCollect + post-action snapshot
**Files:** `MineflayerAdapter/index.js`, `Agent.World.Minecraft/WebSocketBridge.cs`, `Agent.Core/WorldStateProjector.cs`, new `ItemCollectedEvent.cs` in Agent.Core/Events/

1. index.js: `bot.on('playerCollect', (collector, entity) => { if (collector.username !== bot.username) return; sendEvent('itemCollected', { item: entity.metadata?.name ?? entity.displayName, count: entity.count ?? 1 }); })`
2. index.js: call `sendBotStatus()` at end of `craft` and `smelt` cases (after `craftComplete`/`smeltComplete` events)
3. WebSocketBridge: parse `itemCollected` → `ItemCollectedEvent(string Item, int Count, DateTimeOffset Timestamp)`
4. WorldStateProjector: `ApplyItemCollected` → `AddInventoryItem(e.Item, e.Count)`

**Tests required:**
- ItemCollectedEvent from diamond_ore → WorldState.Inventory["diamond"] (not "diamond_ore")
- ItemCollectedEvent from iron_ore → WorldState.Inventory["raw_iron"]
- ItemCollectedEvent from stone → WorldState.Inventory["cobblestone"]
- sendBotStatus called after craft/smelt (verify via log or mock)

**Acceptance:** mine 5 diamond_ore → WorldState.Inventory["diamond"] = 5

### P0-B: mineComplete event
**Files:** `MineflayerAdapter/index.js`, `Agent.World.Minecraft/WebSocketBridge.cs`, new `MineCompleteEvent.cs`, `WebUI.Blazor/AgentBackgroundService.cs`

1. index.js mine case: after while loop exits, `sendEvent('mineComplete', { block: shortName, mined, targetCount: count, correlationId })`
2. WebSocketBridge: parse `mineComplete` → `MineCompleteEvent(string Block, int Mined, int TargetCount, DateTimeOffset Timestamp)`
3. AgentBackgroundService: on MineCompleteEvent, transition correlatedAction to Completed (matching correlationId)

**Tests required:**
- MineCompleteEvent received; correlationId matches dispatch
- Correlation lifecycle: Dispatched → Completed

### P0-C: FlatAreaFoundEvent.SearchedRadius + BuildGoal.OriginSource
**Files:** `Agent.Core/Events/FlatAreaFoundEvent.cs`, `Agent.World.Minecraft/WebSocketBridge.cs`, `Agent.Planning/Decomposition/BuildGoalDecomposer.cs`, `Agent.Planning/Goals/BuildGoal.cs`

1. Add `int SearchedRadius` to `FlatAreaFoundEvent` record
2. Parse `searchedRadius` in WebSocketBridge flatAreaFound handler
3. Add `BuildOriginSource` enum: `Explicit | PlayerPosition | AutoScanned`; add `OriginSource` property to BuildGoal
4. DecomposeBuild retry: gate on `SearchedRadius < 48` (not just `Area == 0`)
5. LogWarning when OriginSource == AutoScanned before construction begins

**Tests required:**
- SearchedRadius parsed correctly from JSON
- OriginSource.Explicit when originX/Y/Z provided in chat
- OriginSource.AutoScanned when no coords given
- No retry when SearchedRadius=48 and area=0

### P0-D: Stop-on-replan (AFTER BLK-S35-02 read)
**Files:** `WebUI.Blazor/AgentBackgroundService.cs`, `Agent.Core/Models/ActionQueue.cs`

1. Read ClearAndEnqueue + DispatchActionsAsync sections first
2. When ClearAndEnqueue called with active Dispatched entries in _correlatedActions → send stop action via bridge before queue clear
3. Add test: replan during active dispatch → stop event sent

---

## P1 — LLM-first architecture

### P1-A: IntentDraft record (resolves BLK-S35-01)
**File:** `Agent.Planning/ChatModels.cs` or new `Agent.Planning/IntentDraft.cs`

```csharp
public record IntentDraft(
    string Addressed,               // "yes" | "maybe" | "no"
    string Intent,                  // "gather" | "build" | "craft" | "navigate" | "cancel" | "status" | "conversation" | "clarify" | "ignore"
    string? Item,
    string? Blueprint,
    int? Count,
    int? X, int? Y, int? Z,
    double Confidence,              // 0.0-1.0
    string? ClarificationQuestion,  // non-null when Confidence < threshold
    string Response
);
```

LLM prompt updated to return `confidence` (0.0-1.0) and `clarification_question` (null or string). Add `LlmConfidenceThreshold` to `ChatOptions` (default 0.6).

### P1-B: Flip LlmChatInterpreter pipeline
**File:** `Agent.Planning/LlmChatInterpreter.cs`

- Remove step 4 early return block: `if (quick.IntentType is CreateGoal or CancelGoal or ...)`
- Keep ONLY 4 hardcoded fast-paths: stop/cancel → CancelGoal, status → QueryStatus, inventory → QueryInventory, help → QueryHelp
- All other inputs reach LLM→IntentDraft→validate path
- If Confidence < threshold and ClarificationQuestion != null: send via bot.chat (D-S35-01), return Unknown

**Test required:** "get me some wood" → reaches LLM (not early-returned); verify via mock provider

### P1-C: Context enrichment for LLM
**File:** `Agent.Planning/LlmChatInterpreter.cs` (BuildSystemPrompt)

Add to system prompt:
- Inventory: non-zero items sorted desc, e.g. `"oak_log: 12, stone: 4"`
- Position: `"(100, 64, 200)"`
- HP + food
- Active goal or "idle"
- Last tool error (200 chars max)
- Tool names: from ToolDispatcher.RegisteredTools

**Test required:** BuildSystemPrompt output contains inventory string and tool list

### P1-D: Remove regex intent matching from ChatInterpreter mainline
**File:** `Agent.Planning/ChatInterpreter.cs`

- Delete GatherRegex, BuildRegex, CraftRegex match blocks from ParseIntent
- Move ResolveCraftId, ResolveItemId, ResolveBlueprintId to new static `KnownAliases` class (reference data for LLM context)
- ParseIntent reduced to 4 hardcoded patterns matching P1-B shortcuts

---

## P2 — Cleanup

### P2-A: Read and document TryRecoverFromGameErrorAsync
- Read the method; document pattern in AGENTS.md
- Note: generalize to full recovery policy layer in Sprint 36

### P2-B: CommonMinecraftBlocks additions
Add to DirectMineBlocks: `copper_ore`, `deepslate_copper_ore`, `deepslate_iron_ore`, `deepslate_gold_ore`, `deepslate_diamond_ore`, `deepslate_coal_ore`, `deepslate_lapis_ore`, `deepslate_redstone_ore`, `deepslate_emerald_ore`

### P2-C: AGENTS.md architecture update
- Three-layer ownership model
- IntentDraft schema + confidence threshold
- playerCollect guard: `collector.username !== bot.username` check required
- mineComplete event contract
- Sprint 36 deferred list

---

## Version bump

- `/api/about` or appsettings: v0.35.0, Phase = "Sprint 35 — Inventory Truth + LLM-First"

---

## Deferred to Sprint 36+

| ID | Sprint | Item |
|---|---|---|
| D-S35-01 | 35 P1 | Wire ClarificationQuestion → bot.chat in event loop |
| D-S35-02 | 36 | Generalize TryRecoverFromGameErrorAsync to recovery policy layer |
| D-S35-03 | 36 | AgentBackgroundService.cs split (80KB god file) |
| D-S35-04 | 36 | Inventory delta history in LLM context |
| D-S35-05 | 37 | Richer adapter observations (reachability, face selection, pathing) |
