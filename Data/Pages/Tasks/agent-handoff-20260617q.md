# MemorySmith.Agent Handoff — Sprint 16
**Date:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` → PR #1 (merge still deferred)  
**Head commit:** Sprint 15 changes (fb569c0 + queued CI fixes)  
**CI status:** Queued (Sprint 14 CI fix + Sprint 15 changes)

---

## Current state in one paragraph

Leo is a Minecraft bot (C# + Node.js) with a deterministic HTN planner, LLM fallback chat, and a MemorySmith wiki as long-term memory. Sprints 1–15 are complete. Critical bugs fixed: inventory now counts correctly from batch block events, iron crafting pre-gathers coal, gather goals complete properly. Seven external audits have been persisted (`Data/Pages/Audit/`) and synthesized into the Phase 7 roadmap. The next architectural layer is Phase 7-A: planner routing cleanup + unified knowledge resolver design.

---

## What changed in Sprint 15 (this session)

**CI fix** (`CraftItemGoalTests.cs`)  
- Narrowed `SkipsPreGather_WhenIngotsSufficient` assertion to iron-specific MineBlock+SmeltItem only — was too broad and caught oak_log mining from the crafting-table step

**P0 — Mining count bug** (`Agent.Core/WorldStateProjector.cs`)  
- `ApplyBlockMined` now uses `e.Count` not hardcoded `1`  
- 2 new tests: `Apply_BlockMined_MultiCount_AccumulatesCorrectly` (count=5) + namespaced variant  
- Gather goals that relied on batch events were silently broken before this fix

**P0 — Coal pre-gather** (`Agent.Planning/HtnTaskLibrary.cs`)  
- `DecomposeCraftItem` mines `coal_ore` before `SmeltItem` when coal is insufficient  
- Formula: `coalNeeded = Math.Max(1, (needIngots+7)/8)` (vanilla: 1 coal smelts 8)  
- 2 new tests: `EmitsCoalMine_WhenNoCoal` + `SkipsCoalMine_WhenCoalPresent`

**P1 — Goal completion clears recovery** (`WebUI.Blazor/AgentBackgroundService.cs`)  
- `TryCompleteCurrentGoalFromWorldUpdate` now resets `_lastRecoveredGoalName = null`  
- Fixes Sprint 13 D3: second run of same goal now gets fresh recovery if it fails

**P1 — Stall detection** (`WebUI.Blazor/AgentBackgroundService.cs`)  
- `_lastActionDispatchedAt` and `_lastStallWarnedAt` fields added  
- `SetGoal` resets `_lastActionDispatchedAt`  
- `_lastActionDispatchedAt` updated on every action dispatch  
- Warning fires in the 300ms settle path if elapsed > 10s with active goal  
- Suppressed for 30s between consecutive warnings

**Audit intake**  
- 3 new audit docs: Codebase Audit, Implementation Plan, Design Doc → `Data/Pages/Audit/`  
- Combined 6-seat + anonymous peer synthesis: `Data/Pages/council/audit-synthesis-council-20260617.md`

**Council review:** `Data/Pages/council/sprint15-council-20260617.md` — no blockers

---

## Suggested skills

- **GitHub MCP** — all code at `TheMasonX/MemorySmith.Agent`, branch `sprint-5-tool-safety`. Always fetch blob SHA before updating existing files. Use `paramsFile`, never inline.
- **CI check** — `curl -s "https://api.github.com/repos/TheMasonX/MemorySmith.Agent/commits/<sha>/check-runs"` + annotations for failures.
- **Council review pattern** — 6-seat review to `Data/Pages/council/` after each sprint.

---

## Sprint 16 starting point (P0 first)

### P0 — Phase 7-A: architecture inventory + planner routing cleanup

**Why:** The synthesis council identified planner routing documentation as the highest-priority Phase 7-A task. `PlannerRouter` documents GOAP and LLM-assisted strategies that are not wired. This misleads future agents.

**Tasks:**
1. Audit `Agent.Planning/PlannerRouter.cs` — document which strategy paths are actually implemented vs. aspirational
2. Add XML comments to each routing branch with status: `[IMPLEMENTED]`, `[STUB]`, `[ASPIRATIONAL]`
3. Write `Data/Pages/Architecture/planner-routing-status-20260617.md` — architecture inventory for planning layer

**Files:** `Agent.Planning/PlannerRouter.cs` · `Data/Pages/Architecture/` (NEW folder)

### P1 — Unified knowledge resolver: design stub

**Why:** Three independent audits (confidence 0.94–0.96) identify retrieval fragmentation as the biggest architectural gap. Phase 7-B starts here.

**Tasks:**
1. Define `IKnowledgeResolver` interface in `Agent.Memory/` (or `Agent.Core/` if cross-cutting):
   - `ResolveAsync(KnowledgeQuery query, CancellationToken ct) -> KnowledgeResult`
   - `KnowledgeQuery`: string query, CandidateType[] types, float confidenceThreshold, int topN
   - `KnowledgeResult`: KnowledgeCandidate[] candidates, bool wasAmbiguous
2. Write stub implementation `LocalKnowledgeResolver` that wraps the existing `IItemRegistry` and `IMemoryGateway`
3. Don't wire it to the planner yet — expose it via the API only

**Files:** `Agent.Memory/IKnowledgeResolver.cs` (NEW) · `Agent.Memory/LocalKnowledgeResolver.cs` (NEW)

### P2 — Carries from earlier sprints

| ID | Task | File |
|----|------|------|
| D3 (S15) | Refactor crafting-table bootstrap out of material pre-gather in DecomposeCraftItem | `HtnTaskLibrary.cs` |
| B3 | Orientation-aware PlaceBlock (facing direction) | `HtnTaskLibrary.cs`, `index.js` |
| B5 | Clear-area action before building on slight slope | `index.js` + new tool |
| D2 (S2) | MemorySmithItemRegistry parallel miss race | `Agent.Memory/` |
| NUnit2058 | Fix NUnit2058 warning | `MemorySmith.Agent.Tests/` |

---

## Phase 7 roadmap (from synthesis council)

| Sub-phase | Focus | Sprint estimate |
|-----------|-------|----------------|
| **7-A (now)** | Architecture inventory; planner routing cleanup | Sprint 16 |
| 7-B | Unified resolver stub (IKnowledgeResolver, two sources) | Sprint 17 |
| 7-C | Observation pipeline normalization | Sprint 18 |
| 7-D | Belief layer + IBeliefState | Sprint 19 |
| 7-E | Episodic memory + IEpisode | Sprint 20 |

---

## Key rules (non-negotiable)

All in `AGENTS.md` at repo root. Critical ones:
1. **Warnings = errors** (`Directory.Build.props`). Fix before pushing.
2. **paramsFile, never inline content** when pushing to GitHub MCP.
3. **CI must be green before council review.**
4. **Enqueue chat response AFTER the switch** in `HandleChatEventAsync`.
5. **ActionQueue is ConcurrentQueue** — don't revert to `Queue<T>`.
6. **GoalNamesMatch compares by suffix** — "GatherItem:X" matches "Gather:X".

---

## Files to read on arrival

- `AGENTS.md` — all rules, patterns, anti-patterns (5 min read)
- `Data/Pages/Tasks/phase6-tasks.md` — sprint tracker
- `Data/Pages/council/audit-synthesis-council-20260617.md` — Phase 7 direction + priority matrix
- Latest sprint council: `Data/Pages/council/sprint15-council-20260617.md`
- `Agent.Planning/PlannerRouter.cs` — start here for Sprint 16 P0
