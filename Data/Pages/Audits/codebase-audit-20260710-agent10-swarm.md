# Tracker: 10-Agent Swarm Codebase Audit — MemorySmith.Agent

**Date:** 2026-07-10
**Sprint:** 60 — Architectural Stability & Legacy Cleanup
**Version:** v0.60.0
**Type:** Full codebase audit with 10-subagent swarm + 5-seat council peer review

## Partition Strategy

10 subagents covering independent codebase layers:

| # | Layer | Scope | Projects/Folders |
|---|-------|-------|------------------|
| 1 | **Core Models & Events** | All model/event files | `Agent.Core/Models/*`, `Agent.Core/Events/*` |
| 2 | **Core Interfaces & Runtime** | Interfaces, Runtime, infrastructure classes | `Agent.Core/Interfaces/*`, `Agent.Core/Runtime/*`, `BuildFactKeys.cs`, `CommonMinecraftBlocks.cs`, `ItemSpec.cs`, `ToolRequirements.cs`, `WorldStateProjector.cs`, `ReplanGovernor.cs`, `SystemTimeProvider.cs` |
| 3 | **HTN Planner & Goal System** | Planner, goals, decomposers, router | `Agent.Planning/` (except Llm/, ChatInterpreter, IntentManager) |
| 4 | **LLM Integration & Chat** | LLM providers, evaluator, chat interpretation | `Agent.Planning/Llm/*`, `Agent.Planning/ChatInterpreter.cs`, `Agent.Planning/ChatHistory.cs`, `Agent.Planning/ChatRateLimiter.cs`, `Agent.Planning/ChatDistance.cs`, `Agent.Planning/ChatModels.cs`, `Agent.Planning/IntentManager.cs` |
| 5 | **Tool System** | Tool dispatcher + all tools | `Agent.Tools/ToolDispatcher.cs`, `Agent.Tools/Tools/*`, `Agent.Tools/ActionProtocol.cs` |
| 6 | **Memory Gateway & Knowledge** | Memory access, blueprint, item registry | `Agent.Memory/*` |
| 7 | **WebUI Blazor — API & Hosting** | Program.cs, services, middleware, config | `WebUI.Blazor/Program.cs`, `AgentBackgroundService.cs`, `AgentHub.cs`, `ApiKeyMiddleware.cs`, `appsettings.json`, `Options/*`, `Dtos.cs` |
| 8 | **WebUI Blazor — Managers & Dashboard** | All manager implementations, dashboard, logging | `WebUI.Blazor/Managers/*`, `WebUI.Blazor/Dashboard/*`, `WebUI.Blazor/Logging/*` |
| 9 | **World Adapter (C#) & Mineflayer (JS)** | World adapter + Node.js bridge | `Agent.World.Minecraft/*`, `MineflayerAdapter/*` (JS files) |
| 10 | **Tests & Supporting Projects** | All tests, Construction, Vision, Personality | `MemorySmith.Agent.Tests/*`, `Agent.Construction/*`, `Agent.Vision/*`, `Agent.Personality/*` |

## Execution Plan

### Phase 1: Swarm (10 parallel subagents)
Each agent receives:
- The sprint context (Sprint 60, v0.60.0)
- The full file list for their layer
- A checklist of what to examine (bugs, inconsistencies, gaps, weak guards, error handling, observability, overcoupling, architecture)
- Instructions to return structured findings with severity (P0–P3), file paths, and evidence

### Phase 2: Council Review (5 parallel subagents)
Each reviewer receives the consolidated report and verifies against source code:

| Seat | Focus |
|------|-------|
| **Architecture & Design** | Accuracy, severity calibration, missing findings, design patterns |
| **Runtime & Debugging** | Verify P0/P1 claims against actual source, confirm code snippets, catch inaccuracies |
| **Safety & Security** | Safety/security findings, API key handling, auth, deny-list integrity |
| **Completeness & QA** | Test coverage gaps, JS-side issues, SignalR path, .bak/hygiene, task mapping |
| **Synthesizer** | Cross-cutting patterns, priority ordering, task creation recommendations |

### Phase 3: Synthesis & Task Creation
- Consolidate all corrections
- Create MCP tasks for actionable findings
- Update roadmap

---

## Swarm Phase Results

*To be populated by subagent results*

### Layer 1: Core Models & Events
**Agent:** [Pending]

### Layer 2: Core Interfaces & Runtime
**Agent:** [Pending]

### Layer 3: HTN Planner & Goal System
**Agent:** [Pending]

### Layer 4: LLM Integration & Chat
**Agent:** [Pending]

### Layer 5: Tool System
**Agent:** [Pending]

### Layer 6: Memory Gateway & Knowledge
**Agent:** [Pending]

### Layer 7: WebUI Blazor — API & Hosting
**Agent:** [Pending]

### Layer 8: WebUI Blazor — Managers & Dashboard
**Agent:** [Pending]

### Layer 9: World Adapter & Mineflayer
**Agent:** [Pending]

### Layer 10: Tests & Supporting Projects
**Agent:** [Pending]

---

## Council Phase Results

*To be populated after swarm consolidation*

---

## Task Creation

*To be populated after council review*
