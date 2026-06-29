# Sprint 55 Handoff: Build Quality & Reliability

**Date:** 2026-06-28
**Branch:** `dev/round-3`
**Predecessor:** Sprint 54 (LLM Replanning Core — [round 5](llm-replanning-core-round-5.md))
**Build baseline:** 746 tests, 0 warnings
**Handoff author:** SteveBot (MemorySmith.Agent)

---

## 🎯 Thesis

**Sprint 54 made the agent capable of finishing builds (replanning, auto-skip, facing-aware placement). Sprint 55 makes the agent *reliable* in two waves: Wave A delivers contextual inventory, parameter preservation, startup resilience, and inventory accuracy. Wave B closes the observe→predict→observe→evaluate feedback loop so LLM planning/replanning/tool usage is correct and flexible enough to handle the delta between theory and practice. Roof/scaffolding blueprint changes are deferred.**

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

### Wave A: Contextual Inventory & Reliability

**Goal:** Make the agent *reliable* — contextual inventory awareness in status reports, parameter preservation on replan, visible startup diagnostics, and accurate post-craft inventory.

| TSK | Priority | What | Key Files |
|---|---|---|---|
| **TSK-0210** (existing) | Medium | Contextual inventory summary and intelligent status reports — report what the bot has vs. what it needs for the current goal | `Agent.Personality/`, `AgentBackgroundService.cs` |
| **TSK-0133** (existing) | High | Fix parameter preservation on replan — remaining count lost when replanning gather goals | `Agent.Planning/ReplanGovernor.cs`, `IGoal` implementations |
| **TSK-0134** (existing) | High | Add DI startup failure logging and health check endpoints — silent failures become visible | `WebUI.Blazor/Program.cs`, `AgentBackgroundService.cs` |
| **TSK-0117** (existing) | Medium | Post-craft/post-smelt inventory reconciliation — ensure WorldState inventory matches reality after crafting | `Agent.Core/WorldStateProjector.cs`, `MineflayerAdapter/index.js` |

### Wave B: LLM Planning Robustness — Observe→Evaluate Feedback Loop

**Goal:** Close the gap between planned expectations and observed reality. Sprint 54 made the LLM evaluator *build-aware*; Wave B makes it *goal-type-agnostic* with a universal observe→predict→observe→evaluate cycle. When an action completes, compare expected vs. actual outcome across inventory, position, health, and entity dimensions — then let the LLM decide whether to continue, adjust, or replan.

| TSK | Priority | What | Key Files |
|---|---|---|---|
| **TSK-0155** (existing) | High | Add observation-driven replan comparison loop — Plan → Dispatch → Observe → Compare → Replan. Compare expected vs. actual across inventory delta, position delta, health delta, and entity presence. Add `OutcomeMismatch` and `ThreatDetected` replan reasons. | `AgentBackgroundService.cs`, `Agent.Planning/ReplanGovernor.cs`, `Agent.Core/IReplanGovernor.cs` |
| **TSK-0165** (existing) | Medium | Add action progress telemetry — `actionStarted`, `actionProgress`, `actionFailed` with machine-readable reason codes. Feeds granular progress into the observe→evaluate loop so the LLM has mid-action visibility, not just final outcomes. | `MineflayerAdapter/index.js`, `Agent.Core/Events/` |
| — (new) | High | Generalize `ILlmEvaluator` beyond builds — make `EvaluateAsync` goal-type-agnostic with structured `WorldStateDiff` (expected vs. actual) input. Currently `LlmEvaluatorImpl` is build-specific; Wave B extends it to gather, craft, navigate, and multi-step goals. | `Agent.Planning/LlmEvaluatorImpl.cs`, `Agent.Core/Interfaces/ILlmEvaluator.cs` |

### Deferred

| TSK | Priority | What | Reason |
|---|---|---|---|
| **TSK-0077** (existing) | High | Add scaffolding phase for roof/upper wall placement | Deferred — blueprint plan changes punted to later sprint |
| **TSK-0215** (existing) | High | Insert `MoveTo exterior` before roof phase | Deferred — depends on TSK-0077 scaffolding phase |

---

## 📁 Key Files Reference

| File | Sprint 54 State | Sprint 55 Changes |
|---|---|---|
| `WebUI.Blazor/AgentBackgroundService.cs` | LLM replanning, auto-skip, stall details wired | Health checks, contextual inventory, observe→evaluate loop integration |
| `Agent.Planning/ReplanGovernor.cs` | Stall detection for builds | Parameter preservation, outcome-mismatch replan triggers |
| `Agent.Planning/LlmEvaluatorImpl.cs` | Build-aware LLM context | Generalized goal-type-agnostic evaluation with WorldStateDiff |
| `Agent.Core/WorldStateProjector.cs` | Inventory event sourcing | Post-craft reconciliation, expected-vs-actual diffing |
| `MineflayerAdapter/index.js` | Facing-aware placement | Action progress telemetry (started/progress/failed events) |
| `Agent.Personality/ChatInterpreter.cs` | "enough" keyword removed (TSK-0200) | Contextual inventory in status reports |

---

## 🧪 Validation Gates

- `dotnet test` → ≥746 tests pass (regression gate)
- `dotnet build` → 0 warnings
- `pwsh Scripts/Test-TaskRecords.ps1` → pass
- Battle-test: ask "what do you have for this build?" → verify contextual inventory in status response
- Battle-test: gather 64 cobblestone with replan → verify remaining count preserved
- Battle-test: startup with missing DI config → verify health endpoint reports failure
- Battle-test: craft items → verify post-craft inventory matches reality
- Battle-test: mine blocks near a hostile mob → verify ThreatDetected replan triggers
- Battle-test: request uncraftable item → verify LLM evaluator recognizes outcome mismatch and replans

---

## 🔗 References

- Sprint 54 handoff: `Data/Pages/Handoffs/llm-replanning-core-round-5.md`
- Architecture: `Data/Pages/architecture.md`
- AGENTS.md: root of repo
- Task system: TSK-0210, TSK-0133, TSK-0134, TSK-0117 (Wave A); TSK-0155, TSK-0165, ILlmEvaluator generalization (Wave B); TSK-0077, TSK-0215 (deferred)
- Situational awareness design: `Data/Pages/Audit/memorysmith_situational_awareness_design_doc_20260625T020914Z.md`
