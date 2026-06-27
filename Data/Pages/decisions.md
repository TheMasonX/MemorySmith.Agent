# Architectural Decisions

Key decisions recorded here so agents and developers understand the "why" behind the design.

## D-001: Minecraft as a World Adapter, Not the System

**Decision**: `IWorldAdapter` abstracts the game. Minecraft-specific code lives only in `Agent.World.Minecraft`.

**Rationale**: The agent framework should be game-agnostic. Future adapters (Factorio, a simulation environment, a mock) plug in without changing planning or memory logic.

**Confidence**: High (0.95).

## D-002: MemorySmith as Long-Term Memory

**Decision**: All persistent knowledge lives in the MemorySmith wiki, accessed via `IMemoryGateway`.

**Rationale**: MemorySmith already provides hybrid search (BM25 + embeddings), versioned pages, and a REST/MCP API. Building a custom knowledge store would duplicate this work.

**Confidence**: High (0.90).

## D-003: Deterministic-First Planning

**Decision**: The LLM is called sparingly — only for novel goals or after repeated failure. Deterministic HTN methods handle known task patterns.

**Rationale**: Minimizes token cost, latency, and hallucination risk. Deterministic methods for pathfinding, mining, and building patterns are faster and more reliable than LLM inference.

**Confidence**: High (0.85) for robustness.

## D-004: WebSocket over Named Pipes for Node Bridge

**Decision**: C# ↔ Node.js communication uses WebSocket.

**Rationale**: Named pipes are 12–15% faster but Windows-only. WebSocket works cross-platform and can later support remote agent workers. Cross-platform correctness > marginal perf gain.

**Confidence**: High (0.90).

## D-005: Microsoft.Extensions.AI for LLM Abstraction

**Decision**: Register `IChatClient` services via `Microsoft.Extensions.AI`. OllamaSharp implements `IChatClient` for local inference; OpenAI/Azure also available.

**Rationale**: Single API surface for all LLM providers. Swap Ollama → OpenAI → Azure without changing agent logic.

**Confidence**: High (0.90).

## D-006: Blueprints as MemorySmith Pages

**Decision**: Blueprints are wiki pages (markdown with structured header), not a separate database.

**Rationale**: Leverages MemorySmith search, versioning, and the Blazor editor for free. The agent can search blueprints with `SearchMemory("Blueprint Gothic")` alongside other memory types.

**Confidence**: High (0.90).

## D-007: slnx Solution Format

**Decision**: Use `MemorySmith.Agent.slnx` (VS 2022 XML solution format) to match `MemorySmith.slnx`.

**Rationale**: Consistent with the parent project's conventions. Modern format; supported by `dotnet` CLI.

## D-008: Node.js for Mineflayer (not .NET)

**Decision**: The Mineflayer bot runs in Node.js, not as a .NET library.

**Rationale**: Mineflayer is the most mature Minecraft bot library and is JavaScript-native. Running it in a subprocess over WebSocket keeps the C# host clean and allows independent restart/upgrade of the bot layer.

**Confidence**: High (0.95).

## D-009: HtnTask Record Deleted; TaskDecomposer Moved to HtnTaskLibrary

**Decision**: `HtnTask(Name, Description, SubTasks[])` record removed from `Agent.Planning/HtnTask.cs`. `TaskDecomposer` delegate moved into `HtnTaskLibrary.cs` (its only consumer). `Phases` property retained on goal classes.

**Rationale**: `HtnTask` was never instantiated — a placeholder from early Phase 2 design that was superseded by the `TaskDecomposer` + `HtnTaskLibrary` approach. Deleting it passes the deletion test: removing it changes nothing about runtime behaviour. `TaskDecomposer` is co-located with the only class that uses it. `Phases` is retained on `IGoal` implementations (including direct-decomposition goals like `GatherWoodGoal`) because: (a) `HtnPlanner` reads `goal.Phases` on the phase-by-phase decomposition path (non-direct goals); (b) the phases serve as readable documentation of a goal's logical stages and are a stable part of the `IGoal` contract.

**Reviewed**: phase3-refactor-candidates-council-20260616.md

**Confidence**: High (0.93).

## D-010: ActionProtocol Constants; WebSocketBridge No Longer Lowercases Tool Name

**Decision**: `Agent.Tools/ActionProtocol.cs` defines string constants for all Node.js wire-action names (`move`, `mine`, `place`, `status`, `wander`, `chat`). Each tool sets `ActionData.Tool` to the appropriate constant in `ExecuteAsync`. `WebSocketBridge.SendAsync` forwards `ActionData.Tool` as-is — the `ToLowerInvariant()` call is removed.

**Rationale**: The old design had a hidden coupling: `MineBlockTool` set `Tool = "mine"` to compensate for the bridge's implicit lowercasing; `MoveTo` set `Tool = "move"` for the same reason. The mapping from logical tool name (`MineBlock`) to wire name (`mine`) was buried inside each tool's body, not visible to any reader of `WebSocketBridge`. The new design makes the tool the single source of truth for its wire name. `ActionProtocol` is the canonical registry of valid wire names, discoverable from one file.

**Reviewed**: phase3-refactor-candidates-council-20260616.md

**Confidence**: High (0.95).

## D-011: Parsers Never Create Goals (CRITICAL)

**Decision**: `IChatInterpreter.InterpretAsync` returns `ChatInterpretation`/`IntentDraft`. It expresses semantic intent (what, item, blueprint, count, coords, confidence). It does **not** call `GoalFactory` and does **not** return a `GoalName` string. The mapping `intent → GoalName` is done exclusively in `AgentBackgroundService.IntentDraftToGoal`/`IntentManager`.

**Rationale**: Fast-path goal creation bypasses the LLM and skips confidence scoring, clarification questions, and context enrichment. Sprint 35 P1-B explicitly removed these fast-paths after they caused BUG-4 (two-minute stall on "craft an iron pickaxe" when Ollama timed out). All non-trivial intents (gather, build, craft, navigate) must route through the LLM for confidence scoring.

**Enforcement**: `ChatInterpreter` returns null/Unknown for any non-fast-path intent. `LlmChatInterpreter.ParseDecision` has no goal-name switch. No regex in `ChatInterpreter` may produce `ChatIntentType.CreateGoal` for gather/build/craft intents.

**Reviewed**: AGENTS.md Rule A-1, Sprint 35 council

**Confidence**: High (0.98).

## D-012: ActionOutcome Is the Universal Tool Result

**Decision**: Every `ToolDispatcher.CallAsync` produces an `ActionOutcome` record. This is the single result type flowing into recovery, replanning, journaling, and world-state updates.

```csharp
public sealed record ActionOutcome(
    Guid GoalId, string ToolName, bool Success,
    string ObservationSummary,
    IReadOnlyList<StructuredEffect> Effects,
    DateTimeOffset Timestamp);
```

Factory helpers: `ActionOutcome.Collected(goalId, tool, item, count)`, `ActionOutcome.Succeeded(goalId, tool, summary)`, `ActionOutcome.Failed(goalId, tool, reason)`.

**Rationale**: Previously, each tool had its own result format, making recovery/replanning logic fragile. A single result type allows the `ILlmEvaluator` (Sprint 39 stub) to evaluate all outcomes uniformly.

**Reviewed**: Sprint 35 P1-E, AGENTS.md

**Confidence**: High (0.95).

## D-013: Inventory Is Event-Sourced via ItemCollectedEvent

**Decision**: Inventory is tracked via authoritative events (`ItemCollectedEvent` from `playerCollect`, `ItemCraftedEvent` from `craftComplete`), not by querying Mineflayer's inventory on every action.

**Rationale**: The `playerCollect` event is the authoritative source for item pickups. Querying inventory via `bot.inventory` is slow and can race with game ticks. Event sourcing gives accurate per-item deltas without polling.

**Guard** (Sprint 35 P0-A): The `bot.on('playerCollect', ...)` listener must guard against items collected by other players: `if (collector.username !== bot.username) return;`.

**Reviewed**: AGENTS.md Sprint 35 P0-A, Sprint 35 council

**Confidence**: High (0.95).
