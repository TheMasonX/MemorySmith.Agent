# MemorySmith.Agent Handoff — Sprint 15
**Date:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` → PR #1 (merge still deferred)  
**Head commit:** Sprint 14 changes · CI: pending

---

## Current state in one paragraph

Leo is a Minecraft bot (C# + Node.js) with a deterministic HTN planner, LLM fallback chat, and a MemorySmith wiki as long-term memory. Sprints 1–14 are complete. The bot can gather blocks (with or without wiki pages for 22+ block types), craft items deterministically including pre-gathering required materials, execute build goals with flat-area pre-scan, and recover from errors without looping. Inventory keys from Mineflayer are now namespace-normalized. DirectMineBlocks and BuiltInDirectMineItems share one constant. Four external architectural audits have been persisted and their Phase 7 direction is documented in phase6-tasks.md.

---

## What changed in Sprint 14 (this session)

**P0 — DecomposeCraftItem material pre-gather** (`Agent.Planning/HtnTaskLibrary.cs`)  
- Iron tools: checks `IronIngotRequirements[itemId]`, mines iron_ore + smelts to ingots if inventory short  
- Stone tools: checks `CobblestoneRequirements[itemId]`, mines stone if cobblestone insufficient  
- 5 new tests in `CraftItemGoalTests.cs`

**P1a — CommonMinecraftBlocks** (`Agent.Core/CommonMinecraftBlocks.cs`, NEW)  
- Single shared `DirectMineBlocks` HashSet — union of the former separate sets  
- `HtnTaskLibrary.DirectMineBlocks` and `GoalFactory.TryMakeBuiltInSpec` both consume it  
- Clay, snow, snow_block now available as built-in gather specs (were only in GoalFactory before)

**P1b — Inventory key normalization** (`Agent.Core/WorldStateProjector.cs`)  
- `ApplyStatus.NormalizeInventory` strips `"minecraft:"` prefix from all inventory keys  
- Fast path for bare-key inventories (no allocation)  
- Merges duplicate namespaced+bare entries  
- 3 new tests in `WorldStateProjectorTests.cs`

**Wiki / audit intake** (`Data/Pages/Audit/`, NEW folder)  
- 4 external audit docs persisted  
- `phase6-tasks.md` updated with Sprint 14 rows + Phase 7 direction section

**Council review:** `Data/Pages/council/sprint14-council-20260617.md` — no blockers

---

## Suggested skills

The next agent should invoke these before starting:

- **GitHub MCP** (`github__get_file_contents`, `github__create_or_update_file`) — all code lives at `TheMasonX/MemorySmith.Agent`, branch `sprint-5-tool-safety`. Always fetch blob SHA before updating existing files. Use `paramsFile` for content, never inline.
- **CI check** — `curl -s "https://api.github.com/repos/TheMasonX/MemorySmith.Agent/commits/<sha>/check-runs"` + `".../check-runs/<id>/annotations"` for failures. No admin rights needed.
- **Council review pattern** — after each sprint, write a 6-seat review to `Data/Pages/council/` and push before the handoff doc.

---

## Sprint 15 starting point (P0 first)

### P0 — Furnace/coal pre-gather for SmeltItem (D2 from this council)

**Why:** `DecomposeCraftItem` now emits `SmeltItem(iron_ore, count, fuel=coal)` but doesn't verify coal or a furnace are available. If coal is absent, SmeltItem will fail silently.

**Task:** Extend `DecomposeCraftItem` to:
1. Check `state.Inventory.GetValueOrDefault("coal")` — if < `needIngots`, mine `coal_ore`
2. Optionally check for nearby furnace availability (can be deferred — tool already handles pathfinding to furnace)

**Files:** `Agent.Planning/HtnTaskLibrary.cs` · `MemorySmith.Agent.Tests/CraftItemGoalTests.cs`  
**Test:** `HtnPlanner_CraftItemGoal_IronPickaxe_EmitsCoalMine_WhenNoCoal`

### P1 — _lastRecoveredGoalName not cleared on goal completion (D3 from Sprint 13 council)

**Why:** If goal completes normally, the name persists. On the next run of the same goal, the recovery guard mistakenly skips LLM for the first failure.

**Task:** Clear `_lastRecoveredGoalName` in `TryCompleteCurrentGoalFromWorldUpdate`.

**Files:** `WebUI.Blazor/AgentBackgroundService.cs`

### P1 — Stall detection (carry-forward from Sprint 11)

**Why:** If no action is dispatched for >10s with an active goal, the loop is stalled silently.

**Task:** In `DispatchActionsAsync`, track `_lastActionDispatchedAt`. After the loop, if elapsed > 10s and goal is active, log `[stall] No action dispatched in {elapsed:N0}s — goal {currentGoal.Name} may be stuck`.

**Files:** `WebUI.Blazor/AgentBackgroundService.cs`

### P2 — Carries from earlier sprints

| ID | Task | File |
|----|------|------|
| B3 | Orientation-aware placement (facing direction in PlaceBlock) | `HtnTaskLibrary.cs`, `index.js` |
| B5 | Clear-area action before building on slight slope | `index.js` + new tool |
| D2 (S2) | MemorySmithItemRegistry parallel cache miss race | `Agent.Memory/` |
| Bubble | Add `bubble_column` to `LIQUID_BLOCK_NAMES` | `index.js` |
| NUnit2058 | Fix NUnit2058 warning in MockMemoryGatewayTests | `MemorySmith.Agent.Tests/` |

---

## Phase 7 direction (when Sprints 14–15 are clean)

Read `Data/Pages/Audit/` for the full architectural rationale. The first Phase 7 task is the architecture inventory described in `external-audit-concrete-refactor-plan-20260617.md` (Phase 0 section) — enumerate existing files and classify them as core cognition, adapter-only, or synthesis/ops before writing any new code.

---

## Key rules (non-negotiable)

All in `AGENTS.md` at repo root. Critical ones:
1. **Warnings = errors** (`Directory.Build.props`). Fix before pushing.
2. **paramsFile, never inline content** when pushing to GitHub MCP.
3. **CI must be green before council review.**
4. **Enqueue chat response AFTER the switch** in `HandleChatEventAsync` — SetGoal/CancelGoal clear the queue.
5. **ActionQueue is ConcurrentQueue** — don't revert to `Queue<T>`.
6. **GoalNamesMatch compares by suffix** — "GatherItem:X" matches "Gather:X".

---

## Files to read on arrival

- `AGENTS.md` — all rules, patterns, anti-patterns (5 min read)
- `Data/Pages/Tasks/phase6-tasks.md` — sprint tracker (update it)
- `Data/Pages/Guides/running-the-agent.md` — quickstart
- Latest council: `Data/Pages/council/sprint14-council-20260617.md`
- `Agent.Core/CommonMinecraftBlocks.cs` — new shared constant (read before touching block sets)
- `Agent.Planning/HtnTaskLibrary.cs:DecomposeCraftItem` — start here for P0
