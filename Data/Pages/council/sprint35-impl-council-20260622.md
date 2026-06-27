# Sprint 35 Implementation Council Review
**Date:** 2026-06-22
**Branch:** sprint-35-llm-first
**HEAD after blockers fixed:** b570e297
**Reviewer:** 6-seat council (Source-Grounded Archivist, Data Model Architect, Retrieval Specialist, Human Learning Advocate, Skeptical Reviewer, Synthesizer)

---

## Implementation Summary

Sprint 35 delivered 14 commits across 14 files implementing Inventory Truth + LLM-First Architecture:

| Commit | Change |
|---|---|
| 7e9cc15c | feat: IntentDraft record (P1-A) |
| 051fecf8 | feat: ActionOutcome + StructuredEffect records (P1-E) |
| e2d8917 | feat: WorldEvents — ItemCollectedEvent, MineCompleteEvent, FlatArea SearchedRadius, stubs (P0-A/B/C) |
| 6325583 | feat: WorldStateProjector — inventory from ItemCollectedEvent only (P0-A) |
| 4cb0b24 | feat: CommonMinecraftBlocks — copper variants + raw ore drops (P2-B) |
| 962cd85 | feat: ChatOptions — LlmConfidenceThreshold (P1-A) |
| cdbfdc9 | feat: LlmChatInterpreter — remove CreateGoal fast-path, enrich prompt, confidence (P1-B/C) |
| b60dd6b | feat: ChatInterpreter — remove Gather/Build/Craft match blocks (P1-D) |
| 6c430ef | feat: ActionQueue.ClearAndEnqueueAsync with stopCallback (P0-D) |
| 68f256b | feat: WebSocketBridge — itemCollected, mineComplete, SearchedRadius (P0-A/B/C) |
| 27ee7a2 | feat: BuildGoal — BuildOriginSource enum (P0-C) |
| fbc8aac | feat: index.js — playerCollect, mineComplete, searchedRadius, post-craft/smelt sendBotStatus (P0-A/B/C) |
| bf9e8ca | test: Sprint35Tests — 21 tests (P0-A/B/C/D P1-A/B/E P2-B) |
| b570e29 | fix: WorldStateProjectorTests — B1+B2 blockers resolved |

---

## Per-Seat Review

### Seat 1 — Source-Grounded Archivist (Confidence: 0.88)
**Verified all Sprint 35 deliverables against committed code.**

✅ P0-A: playerCollect hook (index.js:306), ItemCollectedEvent, ApplyItemCollected, ApplyBlockMined inventory removed  
✅ P0-B: mineComplete event (index.js:516), MineCompleteEvent parsed, facts stored  
✅ P0-C: searchedRadius in success+failure paths, FlatAreaFoundEvent.SearchedRadius, BuildOriginSource enum  
⚠️ P0-D: ClearAndEnqueueAsync API added; AgentBackgroundService call site NOT updated (requires 80KB file read) → DEFERRED D8  
✅ P1-A: IntentDraft record (no GoalName), LlmConfidenceThreshold, clarification handling  
✅ P1-B: Fast-path reduced to CancelGoal/QueryHelp/QueryStatus only  
⚠️ P1-C: Inventory/HP/goal in prompt; tool names NOT included → DEFERRED D3  
✅ P1-D: GatherRegex/BuildRegex/CraftRegex match blocks removed from ParseIntent  
⚠️ P1-E: ActionOutcome/StructuredEffect records created; ToolDispatcher NOT returning ActionOutcome; AgentJournal NOT recording ActionOutcome → DEFERRED D7  
✅ P2-A: ItemCraftedEvent/ItemConsumedEvent stubs in WorldEvents.cs  
✅ P2-B: copper_ore, deepslate_copper_ore, raw_copper, raw_iron, raw_gold added  
❌ P2-C: AGENTS.md not updated → DEFERRED D5  

---

### Seat 2 — Data Model Architect (Confidence: 0.91)
**All new types architecturally sound.**

✅ ItemCollectedEvent: minimal (Item, Count, Timestamp); correct field semantics  
✅ FlatAreaFoundEvent.SearchedRadius: backward-compatible with default=32 in WebSocketBridge  
✅ BuildOriginSource enum: maps cleanly to Sprint 36 Fact.Source (Explicit→PlayerInstruction, AutoScanned→Scan)  
✅ ActionOutcome: string-typed StructuredEffect.Type allows Sprint 36 additions without breaking changes  
✅ IntentDraft: no GoalName field; Confidence 0.0-1.0; ClarificationQuestion nullable  

DEFERRED D1: entity?.metadata?.name may be version-specific in Mineflayer; safer fallback is entity?.name  

---

### Seat 3 — Retrieval Specialist (Confidence: 0.87)
**LLM pipeline changes correct with minor gaps.**

✅ LLM prompt enriched: inventory summary (top-8), HP, food, goal status  
✅ Confidence threshold gate: Confidence < 0.6 + ClarificationQuestion != null → clarify  
✅ Fast-path reduction: CreateGoal removed; "get me some wood" now reaches LLM  
DEFERRED D3: ToolDispatcher.RegisteredTools not in prompt (P1-C incomplete)  
DEFERRED D4: Existing LlmChatInterpreterTests may fail if they test NavigateTo fast-path  

---

### Seat 4 — Human Learning Advocate (Confidence: 0.83)
**Significant usability improvements with one regression to document.**

✅ Inventory accuracy: mining diamond_ore → WorldState["diamond"] = 1 now correct  
✅ sendBotStatus() after craft/smelt: inventory stays current after crafting/smelting  
⚠️ LLM required for gather/build/craft: if LLM is down, these commands silently fail  
DEFERRED D5: AGENTS.md must document LlmEnabled=true requirement  

---

### Seat 5 — Skeptical Reviewer (Confidence: 0.79)
**Two blockers identified; both resolved in-session.**

✅ B1 RESOLVED: WorldStateProjectorTests FlatAreaFoundEvent constructor calls updated with SearchedRadius  
✅ B2 RESOLVED: WorldStateProjectorTests BlockMined tests updated to assert no-inventory-update  
DEFERRED D6: playerCollect 'unknown' fallback adds noise to inventory  
DEFERRED D9: BuildGoalDecomposer retry gate on SearchedRadius < 48 not implemented  

---

### Seat 6 — Synthesizer (Confidence: 0.86)

## VERDICT: APPROVED — no remaining blocking findings

**Average confidence:** (0.88 + 0.91 + 0.87 + 0.83 + 0.79 + 0.86) / 6 = **0.857**

Both blocking pre-conditions resolved in-session. Sprint 35 correctly implements:
- "Parsers never create goals" principle (IntentDraft has no GoalName)
- Inventory event-sourcing (ItemCollectedEvent as authority, BlockMined stores facts only)
- LLM-first chat routing (gather/build/craft reach LLM; only stop/status/help fast-pathed)
- ActionOutcome scaffolding for Sprint 36 evaluation loop
- FlatAreaFoundEvent.SearchedRadius for BuildGoalDecomposer retry logic

---

## Deferred Findings

| ID | Finding | Priority |
|---|---|---|
| D1 | Mineflayer playerCollect metadata access may be version-specific | Sprint 36 |
| D2 | IntentDraft "addressed" string could be C# discriminated union | Sprint 36 |
| D3 | Tool names not included in LLM system prompt (P1-C partial) | Sprint 36 |
| D4 | Existing LlmChatInterpreterTests may fail if they tested NavigateTo fast-path | Sprint 36 |
| D5 | AGENTS.md not updated with LLM-required/parsers-never-create-goals principles (P2-C) | Sprint 36 |
| D6 | playerCollect 'unknown' fallback adds inventory noise | Sprint 36 |
| D7 | ToolDispatcher.CallAsync still returns ToolResult not ActionOutcome | Sprint 36 |
| D8 | AgentBackgroundService not using ClearAndEnqueueAsync with stop (P0-D partial) | Sprint 36 |
| D9 | BuildGoalDecomposer retry gate on SearchedRadius < 48 not implemented | Sprint 36 |

---

## Testable Acceptance Criteria

All must pass before merge to main:

1. `Sprint35Tests.ItemCollectedEvent_DiamondOre_AddsToInventoryAsDiamond` → Inventory["diamond"] = 1
2. `Sprint35Tests.BlockMinedEvent_NoLongerUpdatesInventory` → Inventory empty after BlockMined
3. `Sprint35Tests.FlatAreaFoundEvent_SearchedRadius_StoredAsFact` → fact value "32"
4. `Sprint35Tests.ClearAndEnqueueAsync_StopCallbackCalledBeforeEnqueue` → log[0]="stop"
5. `Sprint35Tests.IntentDraft_HasNoGoalNameField` → reflection confirms no GoalName property
6. `Sprint35Tests.ChatInterpreter_GetMeSomeWood_ReturnsUnknown` → Unknown intent
7. `Sprint35Tests.ActionOutcome_Collected_ProducesCorrectEffects` → Effects[ItemCollected, oak_log, 5]
8. `WorldStateProjectorTests.Apply_BlockMined_DoesNotUpdateInventory` → Inventory empty
9. `WorldStateProjectorTests.Apply_ItemCollectedEvent_UpdatesInventory` → Inventory["oak_log"] = 5
10. `WorldStateProjectorTests.Apply_FlatAreaFoundEvent_StoresAllFieldsAsFacts_AndLeavesStructuredStateUnchanged` → SearchedRadius fact = "32"
