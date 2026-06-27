# Wiki Memory Gap Analysis & Suggested Research Areas

**Date:** 2026-06-23  
**Current Core Memories:** 13 files  
**Target State:** ~30-40 core memories covering all critical codebase areas

## Existing Core Memories (13)

| # | Memory | Status | Notes |
|---|--------|--------|-------|
| 1 | agent-architecture-bounded-contexts | OK | Good overview of bounded contexts |
| 2 | agent-build-pipeline-state | **NEW** | Sprint 41 — build pipeline, alias resolution, logging fixes |
| 3 | agent-ci-baseline-status | OK | CI config, test count, scripts |
| 4 | agent-council-reviews | OK | Review history log |
| 5 | agent-game-testing-readiness | OK | Pre-game checklist |
| 6 | agent-intent-parsing-issues | Needs update | Add Failure 4 (build intent null blueprint) |
| 7 | agent-memorygateway-integration | OK | Memory gateway architecture |
| 8 | agent-mineflayer-adapter-state | **UPDATED** | Sprint 41 — vec3 module, dig fixes, placeBlock interop |
| 9 | agent-phase-status-current | Needs update | Add Sprint 41 status |
| 10 | agent-planner-task-library | OK | HTN task library docs |
| 11 | agent-sprint40-p0-implementation-status | Needs update | Add Sprint 41 additions |
| 12 | agent-technology-stack | OK | Tech stack overview |
| 13 | stevebot-mcp-verified-baseline | OK | MCP tools baseline |

---

## Suggested New Memories (High Priority)

These are critical codebase areas with ZERO dedicated memory coverage. Each should be a focused research task: read the source files, test files, and any related docs, then produce a concise memory.

### Category A: Agent Runtime & Lifecycle

#### A1. AgentBackgroundService Runtime Loop
**Files:** `WebUI.Blazor/AgentBackgroundService.cs` (~1850 lines)  
**Why:** The god class. Covers ExecuteAsync (reconnect loop), DispatchActionsAsync (plan/dispatch/settle), ProcessEventsAsync (event routing), ChatConsumerAsync. Understanding this is essential for ANY agent behavior change.  
**Research angle:** Trace the full lifecycle from starting → connecting → planning → dispatching → settling → replanning → completing → stopping. Document the 3 concurrent loops and how they synchronize.  
**Output:** `agent-runtime-lifecycle.json`

#### A2. Goal Types & Lifecycle
**Files:** `Agent.Core/Interfaces/IGoal.cs`, `Agent.Planning/Goals/*.cs`, `Agent.Core/Models/GoalRequest.cs`  
**Why:** 7+ goal types (BuildGoal, GatherGoal, GenericGatherGoal, CraftItemGoal, SurviveNightGoal, NavigateGoal, SimpleGoal). Each has different IsComplete/HasFailed logic, different parameters, and different decomposer routing.  
**Research angle:** Catalog each goal type, its IsComplete criteria, its HasFailed criteria, its FailureReason mapping, and which decomposer handles it.  
**Output:** `agent-goal-types-catalog.json`

#### A3. Action Dispatch & Correlation
**Files:** `Agent.Tools/ToolDispatcher.cs`, `WebUI.Blazor/AgentBackgroundService.cs` (correlation helpers ~1430-1550), `Agent.Core/Models/PendingAction.cs`  
**Why:** Fire-and-forget dispatch vs synchronous. Action correlation lifecycle (Dispatched → Completed/Failed/TimedOut). The _correlatedActions ConcurrentDictionary + CAS loop.  
**Research angle:** Trace a MineBlock action from enqueue → dispatch → in-flight → blockMined event → correlation complete. Document the race conditions the CAS loop prevents.  
**Output:** `agent-action-correlation.json`

#### A4. Recovery & Error Handling
**Files:** `WebUI.Blazor/AgentBackgroundService.cs` (TryRecoverFromGameErrorAsync ~1806, TryInterruptOnDamageAsync ~641, SweepTimedOutActions ~1507)  
**Why:** The agent has 4+ recovery paths (game error recovery, damage interrupt, stall auto-recovery, consecutive failure abort). Each has different triggers and behaviors.  
**Research angle:** Document each recovery trigger, the recovery action taken, the rate-limiting/guard logic (lastRecoveredGoalName, lastAbandonedGoalName), and how the conversation recovery prompt is constructed.  
**Output:** `agent-recovery-system.json`

### Category B: World & Events

#### B1. WorldState & WorldStateProjector
**Files:** `Agent.Core/WorldStateProjector.cs`, `Agent.Core/Interfaces/IWorldModel.cs`  
**Why:** WorldState is the central fact store. The projector applies events (blockMined, itemCollected, chat, etc.) to mutate state. Understanding this is essential for debugging inventory sync issues.  
**Research angle:** List all event types, what facts each projects, how inventory staleness works, how position is tracked, how game mode switches are handled.  
**Output:** `agent-worldstate-projector.json`

#### B2. World Event Types
**Files:** `Agent.Core/Events/*.cs` (25+ event record types)  
**Why:** The event wire protocol between JS adapter and C# host. Each event carries specific payload fields.  
**Research angle:** Catalog all event types with their fields, which adapter action produces them, and which C# handler consumes them. Include the WebSocketBridge.ParseEvent routing.  
**Output:** `agent-world-event-catalog.json`

#### B3. WebSocketBridge & IWorldAdapter
**Files:** `Agent.World.Minecraft/WebSocketBridge.cs`, `Agent.World.Minecraft/IWorldAdapter.cs`, `Agent.World.Minecraft/MinecraftAdapter.cs`  
**Why:** The transport layer between C# and Node.js. Handles serialization, deserialization, reconnection, keep-alive.  
**Research angle:** Document the message format, correlationId flow, reconnection handshake, error handling, and backpressure.  
**Output:** `agent-websocket-bridge.json`

### Category C: Planning & Goals

#### C1. HtnPlanner & PlannerRouter
**Files:** `Agent.Planning/HtnPlanner.cs`, `Agent.Planning/Router/PlannerRouter.cs`  
**Why:** The planner routes goals to decomposers via a router pattern. Understanding the routing logic is essential for adding new goal types.  
**Research angle:** Document the IGoalDecomposer pattern, the routing table, how PlanAsync/ReplanAsync work, how creative vs survival mode changes planning.  
**Output:** `agent-planner-architecture.json`

#### C2. Blueprint System
**Files:** `Agent.Construction/BlueprintSchema.cs`, `Agent.Construction/BlueprintParser.cs`, `Agent.Construction/BlueprintExecutor.cs`, `Agent.Construction/Interfaces/IBlueprintRepository.cs`, `Agent.Memory/MemorySmithBlueprintRepository.cs`  
**Why:** The entire build pipeline depends on blueprints. Understanding the parse → resolve → decompose → execute chain is essential for build fixes.  
**Research angle:** Document the blueprint markdown format, the parser schema, the repository lookup fallback chain, the alias resolution points, how BuildGoalDecomposer uses blueprint data.  
**Output:** `agent-blueprint-system.json`

#### C3. Chat Interpretation Pipeline
**Files:** `Agent.Planning/LlmChatInterpreter.cs`, `Agent.Planning/ChatInterpreter.cs`, `Agent.Personality/Interfaces/IChatInterpreter.cs`, `Agent.Planning/IntentManager.cs`  
**Why:** Two parallel interpreters (fast-path deterministic + LLM). The IntentDraft → GoalRequest → IGoal chain. Confidence scoring and clarify routing.  
**Research angle:** Trace both paths for "gather 10 dirt" and "build a house". Compare how each resolves aliases. Document confidence threshold behavior.  
**Output:** `agent-chat-interpretation-pipeline.json`

### Category D: Infrastructure

#### D1. MineflayerAdapter Command Handlers (non-mine)
**Files:** `MineflayerAdapter/index.js` — place, wander, findFlatArea, craft, smelt, move cases  
**Why:** The mine handler is well-documented but the other handlers are not. Each has unique quirks (e.g. findFlatArea's height map algorithm, craft's crafting table pathfinding).  
**Research angle:** Document each handler's algorithm, tuning constants, error handling, and event emissions.  
**Output:** `agent-mineflayer-other-handlers.json`

#### D2. Testing Infrastructure
**Files:** `MemorySmith.Agent.Tests/`, `Scripts/`  
**Why:** Understanding test patterns (NUnit, mock setup, integration test setup) speeds up all future development.  
**Research angle:** Document the mocking strategy, the test categories (unit vs integration vs E2E), known flaky tests, test data fixtures.  
**Output:** `agent-testing-infrastructure.json`

#### D3. Emergency Stop System
**Files:** `WebUI.Blazor/AgentBackgroundService.cs` (SendEmergencyStop), `MineflayerAdapter/index.js` (handleStop)  
**Why:** StopNow bypasses the command queue and clears adapter state. Must be understood for any dispatch changes.  
**Research angle:** Trace StopNow from C# → WebSocket → adapter handleStop → cmdQueue clear → pathfinder cancel → stopComplete event → C# correlation cleanup.  
**Output:** `agent-emergency-stop.json`

### Category E: Integration Points

#### E1. MemorySmith Wiki Integration (REST Gateway)
**Files:** `Agent.Memory/RestMemoryGateway.cs`, `Agent.Memory/RestMemoryGatewayOptions.cs`, `Agent.Memory/LocalKnowledgeResolver.cs`  
**Why:** The agent's knowledge source. SearchMemory, GetPage, CreatePage all go through this.  
**Research angle:** Document the API contract, the local file fallback, the auth header, rate limiting, error handling.  
**Output:** `agent-wiki-integration.json`

#### E2. Dashboard Integration
**Files:** `WebUI.Blazor/AgentBackgroundService.cs` (PushStatusToDashboardAsync, PushChatToDashboardAsync, PushGoalToDashboardAsync), `WebUI.Blazor/Components/`  
**Why:** The Blazor dashboard provides real-time agent visibility.  
**Research angle:** Document the HTTP push endpoints, the data format, the update frequency, how the dashboard renders agent state.  
**Output:** `agent-dashboard-integration.json`

---

## Suggested Research Methodology

For each new memory:

1. **Read the source files** — focus on interfaces, key methods, and error handling
2. **Read the test files** — test cases reveal expected behavior and edge cases
3. **Read the related guides** — `Data/Pages/guides/` has docs for many subsystems
4. **Read existing Audit docs** — `Data/Pages/Audit/` has deep-dive analyses
5. **Produce a concise JSON memory** — 1-2 paragraphs of content, high-confidence facts only
6. **Link to source files** — use `SourceLinks` with `%RepoRoot%` prefix and `StartLine`/`EndLine` where possible
7. **Tag appropriately** — `kind:reference`, `audience:agent`, `scope:tech` are standard

## Total Suggested Additions: 15 new memories

This brings the total from 13 to ~28, covering all critical areas. The remaining ~10 can come from deeper dives into specific subsystems as bugs are encountered.
