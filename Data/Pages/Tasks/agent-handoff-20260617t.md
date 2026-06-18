# MemorySmith.Agent Handoff — Sprint 19
**Date:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` → PR #1 (merge still deferred)  
**Head commit:** `84cab34` (CI fix: remove SendEmergencyStop from SetGoal)  
**CI status:** ✅ Green (build-and-test: success on run 27727986513)

---

## Current state in one paragraph

Leo is a Minecraft bot (C# + Node.js) with a deterministic HTN planner, LLM fallback chat, and a MemorySmith wiki as long-term memory. Sprints 1–18 are complete. Sprint 18 fixed the three critical MVP blockers identified from live runtime testing: (1) `pos.floored is not a function` crash in findFlatArea — fixed with `toVec3()` helper; (2) emergency stop — `case 'stop':` bypasses the Node.js cmdQueue and immediately aborts mine/wander via `_stopRequested` flag and `pathfinder.setGoal(null)`; (3) gather count ignored — `GenericGatherGoal.TargetCount` now exposed and passed to `GatherItemDecompose`. Additional fixes: 2-second minimum replan interval ends the replanning storm (3x/sec → max 0.5x/sec), and a startup config summary log shows LLM timeout, rate limits, and memory URL at launch. Sprint 19 begins the MemorySmith wiki deployment (needed for blueprints and SearchMemory) and Phase 7-C (ObservationNormalizer).

---

## What changed in Sprint 18

**P0 — Emergency stop (Node.js + C#)**
- `MineflayerAdapter/index.js`: `_stopRequested` flag; `handleStop()` (clears queue + stops pathfinder + sets flag); `case 'stop':` in ws message handler bypasses cmdQueue; mine/wander/findFlatArea check `_stopRequested`; `toVec3()` helper fixes `pos.floored` crash
- `WebUI.Blazor/AgentBackgroundService.cs`: `SendEmergencyStop()` called in `CancelGoal()` and `TryCompleteCurrentGoalFromWorldUpdate()` (NOT in `SetGoal()` — test regression fix); `MinReplanIntervalSeconds = 2` + `_lastReplanAt` field; replan guard in `DispatchActionsAsync`

**P1 — Gather count fix**
- `Agent.Planning/Goals/GenericGatherGoal.cs`: `public int TargetCount => targetCount` added
- `Agent.Planning/Decomposition/GatherGoalDecomposer.cs`: `GenericGatherGoal` case passes `new[] { gg.TargetCount.ToString() }` — fixes "get 1 dirt mines 10"

**P2 — Startup config log**
- `WebUI.Blazor/Program.cs`: `=== Agent config: bot=... mc=... llmTimeout=...s ... ===` log at startup

**Council review:** `Data/Pages/council/sprint18-council-20260617.md` — no blockers, approved  
**Test plan:** `Data/Pages/Guides/test-plan-mvp.md` — MVP house-building test plan for user

---

## Suggested skills

- **GitHub MCP** — all code at `TheMasonX/MemorySmith.Agent`, branch `sprint-5-tool-safety`. Always fetch blob SHA before updating existing files. Use `paramsFile`, never inline.
- **CI check** — `curl -s "https://api.github.com/repos/TheMasonX/MemorySmith.Agent/commits/<sha>/check-runs"` + annotations for failures.
- **Council review pattern** — 6-seat + anonymous peer review to `Data/Pages/council/` after each sprint.
- **Testing note** — After any `AgentBackgroundService.cs` change, run the test: `AgentBackgroundServiceTests.ActionQueue_IsDrained_AfterPlanIsCreated` — it uses `_adapter.SentActions.Count` as a wait condition, so calls to `SendActionAsync` from outside the dispatch loop will break it.

---

## Sprint 19 starting point (P0-first)

### P0 — MemorySmith Wiki deployment scripts

**Why:** The bot needs a running MemorySmith wiki to:
1. Read blueprints via `IBlueprintRepository` (house building requires `small-house.md` page)
2. Run `SearchMemory` actions (currently returns empty results silently)
3. Store wiki pages Leo generates

**Tasks:**
1. Create `tools/Start-MemorySmithWiki.ps1` (Windows PowerShell):
   - Clones or references the MemorySmith repo
   - Sets `MemorySmith:DataPath` to `D:\Minecraft\MemoryWikis\Alpha\Memories`
   - Sets `MemorySmith:PagesPath` to `D:\Minecraft\MemoryWikis\Alpha\Pages`
   - Starts `dotnet run --project MemorySmith.App` with these settings
   - Default port: 5089 (matches `Agent:Memory:BaseUrl` default)

2. Create `tools/Seed-Wiki.ps1`:
   - Creates `D:\Minecraft\MemoryWikis\Alpha\` directory structure
   - Writes `Pages\blueprints\small-house.md` — the house blueprint in the format `IBlueprintRepository` expects (check existing MemorySmith page format: markdown with YAML front matter)
   - Writes `Memories\Working\item-registry-oak_log.json` and similar item specs
   - Uses the MemorySmith REST API (`POST /api/pages`, `POST /api/memories`) to seed

3. Document in `Data/Pages/Guides/wiki-setup.md`

**Reference:** MemorySmith README shows format. Pages are markdown with YAML front matter. The blueprint format that `MemorySmithBlueprintRepository` reads needs to be checked in `Agent.Memory/MemorySmithBlueprintRepository.cs`.

**Files:** `tools/Start-MemorySmithWiki.ps1` (NEW) · `tools/Seed-Wiki.ps1` (NEW) · `Data/Pages/Guides/wiki-setup.md` (NEW)

### P1 — Phase 7-C: ObservationNormalizer

**From Sprint 18 handoff (formerly Phase 7-C goal):**
Create `Agent.Core/ObservationNormalizer.cs` with `NormalizeId`, `NormalizeInventory`, `NormalizeBlockId`. Refactor `WorldStateProjector.ApplyStatus` to use `NormalizeInventory`. Patch `ApplyBlockMined` to normalize `e.BlockId`. Add 4 tests. No project reference changes needed.

### P2 — Sprint 18 deferred

| ID | Task | File |
|----|------|------|
| D2 | Goal change via TryCreateGoalFromChatAsync doesn't stop old mining — call `CancelGoal()` before `SetGoal()` in that path | `AgentBackgroundService.cs` |
| D3 | Add test for gather count fix (GatherGoalDecomposer passes TargetCount) | `MemorySmith.Agent.Tests/` |
| D6 | Add StopCompleteEvent to WorldEvents.cs so `TryRouteAsError` doesn't silently ignore stop confirmations | `Agent.Core/Events/WorldEvents.cs` |
| anonymous D | `toVec3` is a compat shim — revisit when Mineflayer API compatibility is stable; consider `import { Vec3 } from 'vec3'` | `MineflayerAdapter/index.js` |

---

## Phase 7 roadmap (current state)

| Sub-phase | Focus | Sprint estimate |
|-----------|-------|----------------|
| **7-A (done)** | Architecture inventory; planner routing cleanup | Sprint 16 ✅ |
| **7-B (done)** | Resolver growth: ClassifySpec fix + WorldFact source | Sprint 17 ✅ |
| **7-C (next)** | Observation pipeline normalization (ObservationNormalizer) | Sprint 19 |
| 7-D | Belief layer + IBeliefState | Sprint 20 |
| 7-E | Episodic memory + IEpisode | Sprint 21 |
| 7-F | Planner input migration to world model + beliefs | Sprint 22 |

---

## Key rules (non-negotiable)

1. **Warnings = errors** (`Directory.Build.props`). Fix before pushing.
2. **paramsFile, never inline content** when pushing to GitHub MCP.
3. **CI must be green before council review.**
4. **Enqueue chat response AFTER the switch** in `HandleChatEventAsync`.
5. **ActionQueue is ConcurrentQueue** — don't revert to `Queue<T>`.
6. **NEVER put `SendActionAsync` calls in `SetGoal()`** — breaks `ActionQueue_IsDrained_AfterPlanIsCreated` test. Emergency stop belongs in `CancelGoal()` and `TryCompleteCurrentGoalFromWorldUpdate()` only.
7. **Council workflow per phase:** implement → local build/test → push → CI green → council review → fix blockers → confirm CI → next sprint.

---

## Files to read on arrival

- `AGENTS.md` — all rules, patterns, anti-patterns (5 min read); includes /api/agent/resolve curl examples and known architectural patterns
- `Data/Pages/council/sprint18-council-20260617.md` — latest council; see deferred D1-D6
- `Data/Pages/Guides/test-plan-mvp.md` — MVP test plan with expected log lines
- `MineflayerAdapter/index.js` — Sprint 18 changes: toVec3, stop handling, abort flags
- `WebUI.Blazor/AgentBackgroundService.cs` — SendEmergencyStop placement, MinReplanInterval
- `Agent.Memory/MemorySmithBlueprintRepository.cs` — needed for P0 wiki seed (understand blueprint page format)

---

## Guidance for future agent sessions

Per user instructions, every sprint must follow this workflow:
1. Review the handoff and understand the next sprint's tasks
2. Implement the sprint (P0 first, then P1, then P2)
3. Run 6-seat council review + anonymous peer review to `Data/Pages/council/sprint<N>-council-<date>.md`
4. Fix any blocking council findings; confirm CI green
5. Update `Data/Pages/Tasks/phase6-tasks.md` with sprint tracking row
6. Write the next sprint handoff (`Data/Pages/Tasks/agent-handoff-20260617<letter>.md`) — next letter after `t` is `u`
7. Push all docs to the branch

**This guidance must be included in every future handoff.**
