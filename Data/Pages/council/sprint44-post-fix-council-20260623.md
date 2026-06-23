# Council Review: Sprint 44 Post-Fix Status

## Decision
Sprint 44's correctness fixes (TSK-0079, TSK-0080, TSK-0081, P1-1, P1-2) are structurally sound with 638 passing tests. Five critical issues found by council have been fixed inline; five medium-priority items are tracked as new tasks.

## Evidence Reviewed
- Sprint 44 changed files (9 files, +172/-36 lines)
- `Agent.Planning/Goals/SmeltGoal.cs` — new SmeltGoal implementation
- `Agent.Planning/Decomposition/SmeltGoalDecomposer.cs` — new decomposer
- `Agent.Planning/IntentManager.cs` — SmeltGoalRequest + smelt routing + aliases
- `Agent.Planning/GoalFactory.cs` — SmeltItem: prefix handling
- `Agent.Planning/HtnTaskLibrary.cs` — DecomposeSmeltItem + SearchMemory removal
- `Agent.Planning/HtnPlanner.cs` — creative build path + preserved prefixes
- `Agent.Planning/ChatModels.cs` — ChatInterpretation removal
- `WebUI.Blazor/AgentBackgroundService.cs` — SweepTimedOutActions cleanup + smelt handling
- `MemorySmith.Agent.Tests/Sprint44Tests.cs` — 31 new tests
- All 638 passing tests
- `Data/Pages/Tasks/handoff-sprint44-next-steps.md`

## Findings

| Seat | Recommendation | Confidence | Blocking concern |
|---|---|---|---|
| Source-Grounded Archivist | Code matches handoff for P0-1, P0-2, P1-1, P1-2. P0-3 (test gap) ~50% filled. 5 stale doc comments found. | 90% | Test class doc comment lies about checkpoint tests |
| Data Model Architect | SmeltGoal structurally consistent with CraftItemGoal. OutputItem logic duplicates mapping with DecomposeSmeltItem. | 88% | Drift risk: two copies of ore→ingot mapping |
| Retrieval Specialist | Smelt route fully wired end-to-end. One SearchMemory survivor in HtnPlanner (now fixed). Raw_iron handling gap. | 90% | HtnPlanner creative path emitted dead SearchMemory |
| Human Learning Advocate | Documentation quality is good overall. Stale test-class doc is misleading. OutputItem wildcard creates phantom items. | 92% | `_ore → _ingot` fallback produces nonsense IDs |
| Skeptical Reviewer | 14 findings from critical to trivial. OutputItem wildcard produces `redstone_ingot`. HasFailed is dead code. Race condition in orphaned cleanup (mitigated). No inventory projection for SmeltCompleteEvent. | 85% | OutputItem wildcard, missing inventory projection for smelt output |

## Critical Issues Fixed Inline

| Issue | Severity | Fix |
|---|---|---|
| SmeltGoal.OutputItem wild-card `_ore → _ingot` produces nonsense IDs | Critical | Removed wildcard fallback; only explicit mappings are valid |
| IntentManager ItemAliases missing `"iron"→"iron_ore"`, `"gold"→"gold_ore"`, etc. | High | Added 4 missing smeltable ore aliases |
| DecomposeSmeltItem can't mine `raw_iron` for 1.17+ servers | High | Added `"raw_iron"→"iron_ore"` mapping + `IsMineableBlock` entry |
| HtnPlanner.CreateCreativeBuildActions emits dead SearchMemory | High | Removed SearchMemory call; only MoveTo remains |
| HtnPlanner.PreservedContextPrefixes includes `"SearchMemory:"` | Medium | Removed from preserved prefixes array |
| SweepTimedOutActions orphaned check races with new dispatches | High | Added 1-second age threshold before treating context as orphaned |
| SmeltItem timeout (30s) shorter than JS adapter (40s) causing premature timeout | Medium | Added SmeltItem=45s timeout override |

## New Tasks Created

| Key | Title | Priority | Status |
|---|---|---|---|
| TSK-0082 | Extract shared SmeltableMapping class to eliminate OutputItem drift | P1 | Backlog |
| TSK-0083 | Add WorldStateProjector.ApplySmeltComplete for real-time inventory updates | P1 | Backlog |
| TSK-0084 | Add tests for AdvanceBuildCheckpoint, BlockPlacedEvent, BlockPlaceSkippedEvent, _placeBlockContexts | P1 | Backlog |
| TSK-0085 | Fix stale doc comments in HtnTaskLibrary, LlmChatInterpreter, ChatInterpreter, Sprint44Tests | P2 | Backlog |
| TSK-0086 | Fix SmeltGoal.HasFailed dead code — never returns true | P2 | Backlog |

## Deferred Items (Sprint 45+)

- **Build verification loop** — deferred per handoff (Sprint 45 architecture sprint)
- **Blueprint reconciliation layer** — deferred per handoff
- **AgentRuntime decomposition** — deferred per handoff
- **GoalFactory rename → GoalResolver** — cosmetic, deferred
- **WorldState.Facts vs StructuredFacts unification** — deferred

## Acceptance Criteria
1. ✅ All 638 tests pass
2. ✅ No SearchMemory action emissions in any code path (verified by diff + grep)
3. ✅ Smelt route traced end-to-end: IntentDraft → SmeltGoalRequest → SmeltGoal → SmeltGoalDecomposer → SmeltItem action
4. ✅ ChatInterpretation type confirmed absent via reflection test
5. ✅ SweepTimedOutActions cleanup verified by code inspection
6. 🔲 TSK-0082 through TSK-0086 tracked and accepted

## Open Questions
1. Is the Minecraft server pre-1.17 (iron_ore drops itself) or 1.17+ (drops raw_iron)? Affects whether `raw_iron` mapping is needed at runtime.
2. Should the OutputItem mapping live in a shared static class (TSK-0082) or be driven by wiki data?
3. Is the SmeltCompleteEvent inventory projection (TSK-0083) needed for correctness or just latency optimization?
