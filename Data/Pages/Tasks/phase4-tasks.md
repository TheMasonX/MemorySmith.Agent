# Phase 4 Tasks — Vision & Adaptive Execution

Status: **PLANNED**

Phase 4 confidence: 0.60 (vision subsystem is speculative; adaptive execution is medium risk)

## Priority 1: Adaptive Execution (Phase 3 gate)

These are required BEFORE shipping Phase 3 results to a real Minecraft session.

- [ ] Add `AgentBackgroundServiceTests` integration test (council acceptance criteria)
- [ ] `ActionData.Context` — add mutable `Dictionary<string, object?>` field for inter-tool state carry
- [ ] Update `AgentBackgroundService.DispatchActionsAsync` to pass context between actions
- [ ] Update `SearchMemoryTool` to write first result's ID/location to `Context`
- [ ] Update `HtnTaskLibrary.GatherWoodDecompose` to accept coordinate injection from context
- [ ] `MinecraftAdapterConfig.MinecraftVersion` — add string field (default "1.21")
- [ ] Parameterize block IDs in HtnTaskLibrary based on version flag

## Priority 2: ISpatialAnalyzer (core Phase 4)

- [ ] Implement `Agent.Vision/SpatialAnalyzer.cs` — flatness, tree coverage, water proximity
- [ ] Feed `SpatialAnalysis` into `GatherWoodGoal.IsComplete` for location scoring
- [ ] Add `AnalyzeTerrainTool` to the tool catalog
- [ ] Add `SpatialAnalyzerTests` (flatness ratio, tree coverage computation)

## Priority 3: GOAP integration (advanced Phase 4)

- [ ] Define `GoapAction` record (Name, Preconditions, Effects, Cost)
- [ ] Implement `GoapPlanner` — backward-chaining from goal to achievable actions
- [ ] Wire GOAP into `HtnPlanner.ReplanAsync` for failed phases
- [ ] Test: path blocked → GOAP finds alternative mining route

## Priority 4: IVisionModel (aesthetic — confidence 0.60)

- [ ] Implement `OllamaVisionClient : IVisionModel` using Ollama HTTP API
- [ ] Add `TakeScreenshotTool` to tool catalog (calls Node.js adapter)
- [ ] Add `AnalyzeStructureTool` — takes screenshot, calls IVisionModel, returns feedback
- [ ] Test: mock vision client returns structured critique

## Priority 5: Microsoft.Extensions.AI (LLM fallback)

- [ ] Add `Microsoft.Extensions.AI` to `Agent.Planning.csproj`
- [ ] Add `OllamaSharp` for local LLM support
- [ ] Add `IChatClient` abstraction to `HtnPlanner.PlanAsync` LLM fallback path
- [ ] `MockChatClient` for test isolation
- [ ] Test: unknown goal → LLM returns JSON plan → phases decomposed by library

## Phase 4 acceptance criteria

All priority 1 items must be complete before any real Minecraft integration. Vision and GOAP can be developed in parallel with adaptive execution.
