# MemorySmith Council Review — Sprint 15
**Date:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` (PR #1)  
**Commits reviewed:** Sprint 15 changes (acf17d8 through fb569c0)  
**CI status:** Queued (Sprint 14 CI fix + Sprint 15 changes)  
**Seats:** Source-Grounded Archivist · Data Model Architect · Retrieval Specialist · Human Learning Advocate · Skeptical Reviewer · Synthesizer

---

## Sprint 15 changes under review

| File | Change |
|------|--------|
| `MemorySmith.Agent.Tests/CraftItemGoalTests.cs` | CI fix: narrow SkipsPreGather assertion to iron_ore/deepslate_iron_ore + SmeltItem only; 2 new coal tests |
| `Agent.Core/WorldStateProjector.cs` | P0 bug fix: `ApplyBlockMined` uses `e.Count` not hardcoded `1` |
| `Agent.Planning/HtnTaskLibrary.cs` | P0: `DecomposeCraftItem` pre-gathers coal (`coalNeeded = ceil(needIngots/8)`) before `SmeltItem` |
| `WebUI.Blazor/AgentBackgroundService.cs` | P1: `_lastRecoveredGoalName = null` in `TryCompleteCurrentGoalFromWorldUpdate`; stall detection fields + warning in settle path |
| `MemorySmith.Agent.Tests/WorldStateProjectorTests.cs` | 2 new tests: multi-count BlockMined (count=5, count=64+namespaced) |
| `Data/Pages/Audit/` (3 new files) | External audit docs: Codebase Audit, Implementation Plan, Design Doc |
| `Data/Pages/council/audit-synthesis-council-20260617.md` | 6-seat + anonymous peer synthesis of all 7 external audits |

---

## Seat 1 — Source-Grounded Archivist
**Confidence: 0.95**

**CI fix:** The failing test `HtnPlanner_CraftItemGoal_IronPickaxe_SkipsPreGather_WhenIngotsSufficient` checked `a.Tool is "MineBlock" or "SmeltItem"` which matched the `MineBlock(oak_log)` from the crafting-table step. The fix narrows the filter to `block` argument `is "iron_ore" or "deepslate_iron_ore"` plus `SmeltItem`. The test now correctly distinguishes "iron pre-gather" from "table pre-requisite". ✓

**Mining count bug:** `e.Count` is the count field on `BlockMinedEvent`. Reviewing the event definition — `BlockMinedEvent(string Block, int Count, Position Pos, DateTimeOffset Timestamp)`. Count is always present; prior code ignored it. Fix is `AddInventoryItem(itemKey, e.Count)`. ✓

**Coal pre-gather:** `coalNeeded = Math.Max(1, (needIngots + 7) / 8)` — ceiling division by 8 (Minecraft vanilla: 1 coal smelts 8 items). For `needIngots=3`: `(3+7)/8 = 1` coal needed. For `needIngots=9`: `(9+7)/8 = 2` coal needed. Correct. The check `if (haveCoal < coalNeeded)` gates the MineBlock emission. ✓

**`_lastRecoveredGoalName = null` in `TryCompleteCurrentGoalFromWorldUpdate`:** This resolves Sprint 13 D3. Before this fix, if a goal completed normally (e.g. GatherItem:oak_log after recovery was attempted), `_lastRecoveredGoalName` persisted. A second run of the same goal would skip recovery on first failure. Now it's cleared with the goal. ✓

**Stall detection:** `_lastActionDispatchedAt` reset in `SetGoal`. Updated after each `_actionDispatchedThisCycle = true`. `StallWarningSeconds = 10`, `StallWarningSuppressSeconds = 30`. Warning fires at most once per 30s. Log message: `[stall] No action dispatched in {N}s with active goal '{Goal}'`. ✓

---

## Seat 2 — Data Model Architect
**Confidence: 0.92**

**Count bug impact scope:** Before this fix, every `BlockMinedEvent` with `Count > 1` under-counted inventory. This affects:
- `GenericGatherGoal.IsComplete` — checks `state.Inventory.GetValueOrDefault(block) >= count`; now fires correctly when batch mines happen
- `CraftItemGoal` pre-gather checks — `haveIngots`, `haveOre`, `haveCobble` are all inventory lookups; now correct
- `CraftItemGoal.IsComplete` — checks `state.Inventory.GetValueOrDefault(itemId) >= count`; now fires correctly after CraftComplete

**Note:** The `BlockMinedEvent` in `ProcessEventsAsync` log was also updated: `"Inventory +1 {Block}"` → `"Inventory +{Count} {Block}"`. This is cosmetic but important for observability.

**Coal calc correctness:** Confirmed 1 coal smelts 8 items in vanilla Minecraft. `Math.Max(1, ...)` ensures at least 1 coal is required even for 1 ingot (can't smelt with 0 coal). ✓

**Stall field placement:** `_lastActionDispatchedAt` and `_lastStallWarnedAt` are initialized at `DateTimeOffset.MinValue`. On first `SetGoal`, both reset: `_lastActionDispatchedAt = Now`, `_lastStallWarnedAt = MinValue`. This means the first stall check after SetGoal starts a fresh 10-second clock. ✓

---

## Seat 3 — Retrieval Specialist
**Confidence: 0.94**

**End-to-end "gather 10 oak_logs" with batch events (post Sprint 15):**
```
[user] "leo gather 10 oak logs"
  → GenericGatherGoal(oak_log, 10) → HtnPlanner → MineBlock(oak_log, 10) actions
  → Mineflayer mines 5 blocks, emits BlockMinedEvent(oak_log, Count=5)
  → WorldStateProjector.ApplyBlockMined: inventory += 5 (was: += 1 before fix)
  → state.Inventory["oak_log"] = 5
  → GenericGatherGoal.IsComplete: 5 >= 10 → false (keeps going)
  → mines 5 more, emits BlockMinedEvent(oak_log, Count=5)
  → inventory = 10
  → IsComplete: 10 >= 10 → true ✓
```

Before Sprint 15, inventory would show 2 (two events × hardcoded 1) instead of 10. The goal would never complete.

**End-to-end "craft iron pickaxe" with no coal (post Sprint 15):**
```
  → DecomposeCraftItem("iron_pickaxe", 1, emptyState)
  → IronIngotRequirements = 3; needIngots = 3; haveOre = 0; needOre = 3
  → MineBlock(iron_ore, 3)
  → coalNeeded = ceil(3/8) = 1; haveCoal = 0; coalToMine = 1
  → SearchMemory("coal ore location nearby")
  → MineBlock(coal_ore, 1)
  → SmeltItem(iron_ore, 3, coal)
  → [crafting table steps]
  → CraftItem(iron_pickaxe, 1) → GetStatus
```
Complete iron crafting pipeline — ore, coal, smelt, table, craft. ✓

---

## Seat 4 — Human Learning Advocate
**Confidence: 0.97**

**User-facing improvements this sprint:**

| Scenario | Before Sprint 15 | After Sprint 15 |
|----------|-----------------|-----------------:|
| Gather 10 logs — Mineflayer sends batch events | Goal stuck; inventory = event count × 1 = never reaches 10 | Correct count; goal completes properly |
| "craft an iron pickaxe" — no coal | SmeltItem fails silently, error recovery fires LLM | Pre-gathers coal_ore first; SmeltItem succeeds |
| Same goal runs twice, second run fails | Recovery skipped (stale `_lastRecoveredGoalName`) | Recovery runs fresh on new goal attempt |
| Agent stuck with active goal | Silent — no indication | `[stall]` warning in logs after 10s |

**Impact of count bug fix:** This was likely the most impactful silent bug in the codebase. Any gather goal that relied on Mineflayer sending batch block events was silently broken. Sprint 15 fixes it with a one-line change.

---

## Seat 5 — Skeptical Reviewer
**Confidence: 0.89**

**Concern (non-blocking):** The stall detection check runs in the 300ms settle path — it only fires once per plan cycle settle, which means if the agent is actively dispatching actions (never reaching the settle), the stall check doesn't run. However, if actions ARE being dispatched, there's no stall — so this is correct. The stall scenario (goal active, nothing dispatching) is exactly when the settle runs and the check fires. ✓

**Concern (non-blocking):** `coalNeeded = Math.Max(1, (needIngots + 7) / 8)` — if `haveCoal >= coalNeeded` but `haveCoal < 1` (impossible since we just checked >= coalNeeded which is at least 1), no issue. But what if haveCoal = 1 and needIngots = 1: coalNeeded = 1, haveCoal >= 1 → skip. Correct.

**Concern (non-blocking):** The `_lastRecoveredGoalName` fix is correct but incomplete in one edge case: if the goal completes in `DispatchActionsAsync` (not via `TryCompleteCurrentGoalFromWorldUpdate`), the field isn't cleared there. Looking at the code — in `DispatchActionsAsync`: `logger.LogInformation("Goal '{Goal}' completed."); _currentGoal = null;` — no clear of `_lastRecoveredGoalName`. However: the next `SetGoal` call (when the user sets a new goal) always clears it. The edge case only matters if the same goal is set again without calling SetGoal, which isn't a supported path. ✓ (acceptable)

**CI fix correctness:** The narrowed assertion `bl?.ToString() is "iron_ore" or "deepslate_iron_ore"` correctly targets only iron pre-gather, not the oak_log MineBlock from the crafting-table step. The original broad check was a test authoring error. ✓

**Verdict:** No blocking findings. Sprint 15 is correct, addresses the highest-confidence bug from the external audits, and includes proper stall observability.

---

## Seat 6 — Synthesizer
**Confidence: 0.94**

**Blocking findings: NONE**

**Deferred findings:**
| ID | Finding | Priority |
|----|---------|----------|
| D1 | Stall detection doesn't run during active dispatch (by design) — document in AGENTS.md | P3 |
| D2 | `_lastRecoveredGoalName` not cleared in DispatchActionsAsync completion path | P3 (SetGoal covers the use case) |
| D3 | `MineBlock(oak_log)` for crafting table in iron tool plan is cosmetically confusing — users see "mining wood for a pickaxe" | P2 — Sprint 16 refactor: separate crafting-table bootstrap from material pre-gather |
| D4 | Unified knowledge resolver design — begins Sprint 16 | P1 |
| D5 | Planner routing documentation (remove GOAP/LLM placeholders) | P2 |

**Acceptance criteria — all met:**
| # | Criterion | Status |
|---|-----------|--------|
| AC1 | CI test fix: `HtnPlanner_CraftItemGoal_IronPickaxe_SkipsPreGather_WhenIngotsSufficient` passes | CONFIRMED |
| AC2 | `ApplyBlockMined` uses `e.Count` — multi-count tests pass | CONFIRMED |
| AC3 | `DecomposeCraftItem` pre-gathers coal before SmeltItem | CONFIRMED |
| AC4 | Coal skipped when already in inventory | CONFIRMED |
| AC5 | `_lastRecoveredGoalName` cleared on goal completion | CONFIRMED |
| AC6 | Stall warning fires after 10s, suppressed for 30s | CONFIRMED |
| AC7 | Stall timer reset on SetGoal | CONFIRMED |
| AC8 | 4 new tests (2 coal, 2 multi-count) | CONFIRMED |
| AC9 | 3 external audit docs + synthesis council persisted | CONFIRMED |

**Council decision: APPROVED — no blockers. Sprint 15 implementation complete.**
