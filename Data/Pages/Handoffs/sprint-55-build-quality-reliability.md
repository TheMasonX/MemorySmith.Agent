# Sprint 55 Handoff: Build Quality & Reliability

**Date:** 2026-06-28
**Branch:** `dev/round-3`
**Predecessor:** Sprint 54 (LLM Replanning Core — [round 5](llm-replanning-core-round-5.md))
**Build baseline:** 746 tests, 0 warnings
**Handoff author:** SteveBot (MemorySmith.Agent)

---

## 🎯 Thesis

**Sprint 54 made the agent capable of finishing builds (replanning, auto-skip, facing-aware placement). Sprint 55 makes those builds *correct* (roof scaffolding, exterior positioning) and the agent *reliable* (parameter preservation, startup resilience, inventory accuracy).**

---

## 📊 Sprint 54 Recap — What We Ship

| Capability | Status |
|---|---|
| LLM evaluator called during build stalls | ✅ `forceEvaluate` bypasses fire-and-forget gap |
| Build-aware LLM context (progress, skip reasons) | ✅ `BuildUserMessage` enriched |
| Auto-skip blocks after 3 consecutive timeouts | ✅ `_blockTimeoutCounts` tracking |
| Accurate stall diagnostics | ✅ `BuildStallDetail` excludes "placed" blocks |
| Facing-direction placement | ✅ `FACING_VECTORS`, `\| facing: north` in blueprints |
| GetStatus timeout fix | ✅ 30s → 10s, 2-failure gate |
| Cross-session memory | ✅ `LoadSessionFactsAsync` on startup |
| Multi-step chaining | ✅ `TaskSequenceGoal` with `TryAdvanceSequence` |
| Auto-tool crafting | ✅ `GatherGoalDecomposer` pre-crafts tools |

---

## 🚀 Sprint 55 — Proposed Waves

### Wave A: Roof & Build Quality

**Goal:** Fix the remaining build quality issues that Sprint 54 couldn't address — roof placement from inside, exterior positioning, and contextual inventory awareness.

| TSK | Priority | What | Key Files |
|---|---|---|---|
| **TSK-0077** (existing) | High | Add scaffolding phase for roof/upper wall placement — place temporary blocks to reach high positions | `AgentBackgroundService.cs`, `BlueprintExecutor.cs`, `MineflayerAdapter/index.js` |
| **TSK-0215** (existing) | High | Insert `MoveTo exterior` before roof phase to prevent bot from placing roof blocks while standing inside the structure | `Agent.Planning/`, `AgentBackgroundService.cs` |
| **TSK-0210** (existing) | Medium | Contextual inventory summary and intelligent status reports — report what the bot has vs. what it needs for the current goal | `Agent.Personality/`, `AgentBackgroundService.cs` |

### Wave B: Reliability & Bug Fixes

**Goal:** Fix known bugs that erode trust — parameter drift on replan, silent startup failures, inventory reconciliation gaps.

| TSK | Priority | What | Key Files |
|---|---|---|---|
| **TSK-0133** (existing) | High | Fix parameter preservation on replan — remaining count lost when replanning gather goals | `Agent.Planning/ReplanGovernor.cs`, `IGoal` implementations |
| **TSK-0134** (existing) | High | Add DI startup failure logging and health check endpoints — silent failures become visible | `WebUI.Blazor/Program.cs`, `AgentBackgroundService.cs` |
| **TSK-0117** (existing) | Medium | Post-craft/post-smelt inventory reconciliation — ensure WorldState inventory matches reality after crafting | `Agent.Core/WorldStateProjector.cs`, `MineflayerAdapter/index.js` |

---

## 📁 Key Files Reference

| File | Sprint 54 State | Sprint 55 Changes |
|---|---|---|
| `WebUI.Blazor/AgentBackgroundService.cs` | LLM replanning, auto-skip, stall details wired | Scaffolding phase, exterior MoveTo, health checks |
| `Agent.Planning/LlmEvaluatorImpl.cs` | Build-aware LLM context | Roof/scaffold context enrichment |
| `Agent.Planning/ReplanGovernor.cs` | Stall detection for builds | Parameter preservation on replan |
| `Agent.Core/WorldStateProjector.cs` | Inventory event sourcing | Post-craft reconciliation |
| `MineflayerAdapter/index.js` | Facing-aware placement | Scaffold block placement logic |
| `Agent.Construction/BlueprintExecutor.cs` | Facing/BlockState in ActionData | Scaffold phase injection |
| `Agent.Personality/ChatInterpreter.cs` | "enough" keyword removed (TSK-0200) | Contextual inventory in status reports |

---

## 🧪 Validation Gates

- `dotnet test` → ≥746 tests pass (regression gate)
- `dotnet build` → 0 warnings
- `pwsh Scripts/Test-TaskRecords.ps1` → pass
- Battle-test: build a house with roof → verify no interior confinement, correct roof placement
- Battle-test: gather 64 cobblestone with replan → verify remaining count preserved
- Battle-test: startup with missing DI config → verify health endpoint reports failure

---

## 🔗 References

- Sprint 54 handoff: `Data/Pages/Handoffs/llm-replanning-core-round-5.md`
- Architecture: `Data/Pages/architecture.md`
- AGENTS.md: root of repo
- Task system: TSK-0077, TSK-0215, TSK-0210, TSK-0133, TSK-0134, TSK-0117
