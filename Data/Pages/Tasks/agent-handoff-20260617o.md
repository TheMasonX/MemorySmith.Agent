# MemorySmith.Agent Handoff — Sprint 14
**Date:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` → PR #1 (merge still deferred)  
**Head commit:** `c6d913c` · CI: ✅ GREEN

---

## Current state in one paragraph

Leo is a Minecraft bot (C# + Node.js) with a deterministic HTN planner, LLM fallback chat, and a MemorySmith wiki as long-term memory. Sprints 1–13 are complete. The bot can gather common blocks without a wiki page, craft items deterministically, execute build goals with flat-area pre-scan, and recover from errors without looping. Warnings are now build errors. The branch is healthy.

---

## What changed in Sprints 12–13 (this session)

See full council reviews — no need to repeat details here:
- `Data/Pages/council/sprint12-fixes-council-20260617.md` — 6 bug fixes (ActionQueue thread-safety, response queue ordering, LLM hard timeout, CS0420, NUnit2058)
- `Data/Pages/council/sprint13-council-20260617.md` — CraftItemGoal, built-in gather fallback, JsonElement TryGetIntFact, requireOrigin in HtnPlanner, recovery rate-limiter + GoalNamesMatch fix

---

## Suggested skills

The next agent should invoke these before starting:

- **GitHub MCP** (`github__get_file_contents`, `github__create_or_update_file`) — all code lives at `TheMasonX/MemorySmith.Agent`, branch `sprint-5-tool-safety`. Always fetch blob SHA before updating existing files. Use `paramsFile` for content, never inline.
- **CI check** — `curl -s "https://api.github.com/repos/TheMasonX/MemorySmith.Agent/commits/<sha>/check-runs"` + `".../check-runs/<id>/annotations"` for failures. No admin rights needed.
- **Council review pattern** — after each sprint, write a 6-seat review to `Data/Pages/council/` and push before the handoff doc.

---

## Sprint 14 starting point (P0 first)

### P0 — Material pre-gather for craft goals (D4 from this council)

**Why:** "craft an iron pickaxe" now creates a goal and plans correctly, but `CraftItemTool` will fail if iron ingots aren't in inventory. Error recovery may suggest gathering, but only after a failure cycle. Better to pre-gather in the plan.

**Task:** Extend `HtnTaskLibrary.DecomposeCraftItem` to:
1. Detect iron-tool recipes → pre-gather `iron_ore` → smelt to `iron_ingot`
2. Detect stone-tool recipes → pre-gather `cobblestone`  
3. Keep sticks logic (already implemented for table-requiring items)

**Files:** `Agent.Planning/HtnTaskLibrary.cs` · `MemorySmith.Agent.Tests/CraftItemGoalTests.cs`  
**Test:** Add `HtnPlanner_CraftItemGoal_IronPickaxe_EmitsMineBlockAndSmelt`

### P1 — DirectMineBlocks / BuiltInDirectMineItems sync (D1)

**Why:** Two separate sets that must be kept in sync manually. Divergence will silently break gather goals.

**Task:** Extract to a shared constant in `Agent.Core/CommonMinecraftBlocks.cs` (static class). Both `HtnTaskLibrary.DirectMineBlocks` and `GoalFactory.BuiltInDirectMineItems` consume it.

**Files:** `Agent.Core/CommonMinecraftBlocks.cs` (NEW) · `Agent.Planning/HtnTaskLibrary.cs` · `Agent.Planning/GoalFactory.cs`

### P1 — Inventory key normalization (D2)

**Why:** `GenericGatherGoal.IsComplete` and `CraftItemGoal.IsComplete` check `state.Inventory.GetValueOrDefault(itemId)`. If the Mineflayer adapter sends `"minecraft:oak_log"` instead of `"oak_log"`, the inventory key won't match and goals never complete.

**Task:** In `WorldStateProjector` (or the `BlockMinedEvent` handler in `AgentBackgroundService`), strip the `"minecraft:"` namespace prefix when updating inventory. Add a test.

**Files:** `Agent.Core/WorldStateProjector.cs` · `MemorySmith.Agent.Tests/WorldStateProjectorTests.cs`

### P2 — Carries from earlier sprints

| ID | Task | File |
|----|------|------|
| B3 | Orientation-aware placement (facing direction in PlaceBlock) | `HtnTaskLibrary.cs`, `index.js` |
| B5 | Clear-area action before building on slight slope | `index.js` + new tool |
| Stall | Warn if no action dispatched >10s with active goal | `AgentBackgroundService.cs` |
| D2 (S2) | MemorySmithItemRegistry parallel cache miss race | `Agent.Memory/` |
| Bubble | Add `bubble_column` to `LIQUID_BLOCK_NAMES` | `index.js` |

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
- Latest council: `Data/Pages/council/sprint13-council-20260617.md`
- `Agent.Planning/Goals/CraftItemGoal.cs` — new, read it
- `Agent.Planning/HtnTaskLibrary.cs:DecomposeCraftItem` — start here for P0
