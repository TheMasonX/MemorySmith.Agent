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
