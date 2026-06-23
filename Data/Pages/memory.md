# Memory Architecture

The agent uses **two** MemorySmith instances as long-term memory, accessed via `IMemoryGateway`:

| Gateway | Key | Purpose |
|---------|-----|---------|
| **Agent KB** | (default) | Codebase knowledge — architecture, guides, blueprints, agent profile |
| **World KB** | `"world"` | World knowledge — block locations, exploration log, world facts |

## Memory Types

| Type | Examples | Gateway | Storage |
|---|---|---|---|
| **World facts** | "Village at X,Y", "Diamond at (x,z)" | World KB | MemorySmith structured memories |
| **Exploration log** | Block found events, path history | World KB | Pages in World KB |
| **Blueprints / Designs** | GothicCathedral, FarmLayout | Agent KB | Pages in `Data/Pages/blueprints/` |
| **Agent Profile** | Name, backstory, preferences | Agent KB | `#AgentProfile` page |
| **Codebase guides** | Architecture, API reference | Agent KB | Pages in `Data/Pages/` |

## IMemoryGateway

```csharp
public interface IMemoryGateway
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, ...);
    Task<string?> GetPageAsync(string pageId, ...);
    Task<string> CreatePageAsync(string title, string content, string type, ...);
    Task UpdatePageAsync(string pageId, string content, ...);
}
```

## Dual Gateway Setup (Sprint 22–23)

`RestMemoryGatewayOptions` now has two URLs:

```json
{
  "Agent": {
    "Memory": {
      "BaseUrl": "http://localhost:5001",
      "ApiKey": "",
      "TimeoutSeconds": 30
    },
    "WorldKb": {
      "WorldKbUrl": null,
      "WorldApiKey": "",
      "WorldTimeoutSeconds": 30
    }
  }
}
```

| Setting | Description |
|---------|-------------|
| `BaseUrl` | URL of the agent KB MemorySmith instance |
| `WorldKbUrl` | URL of the world KB MemorySmith instance. `null` = disabled (startup warning logged) |
| `WorldApiKey` | Optional API key for world KB |
| `WorldTimeoutSeconds` | HTTP timeout for world KB requests (default 30) |

When `WorldKbUrl` is null/empty, `Program.cs` logs a `LogWarning` at startup and world KB tools gracefully degrade.

### DI Registration

```csharp
// Program.cs
builder.Services.AddRestMemoryGateway(agentOptions);  // default key
builder.Services.AddKeyedSingleton<IMemoryGateway>("world", worldGateway);
```

See [World KB Guide](guides/world-kb.md) for full setup instructions.

## Tool Routing (Sprint 23)

| Tool | Routes to | Rationale |
|---|---|---|
| `SearchMemory` | World KB | Searches world facts and exploration data |
| `CreatePage` | World KB | Stores new world observations |
| `GetPage` | Agent KB | Retrieves codebase knowledge and guides |

The LLM sees updated tool descriptions that clarify this routing.

## WorldState & StructuredFacts (Sprint 5 + 17)

`WorldState.StructuredFacts` is a capped dictionary of typed `Fact` records:

```csharp
public record Fact(string Value, FactSource Source, DateTimeOffset Timestamp);

public enum FactSource { Observed, Inferred, Durable }
```

**Cap:** 1000 facts maximum (oldest removed when exceeded).

**Sources:**
- `Observed` — directly from Mineflayer events
- `Inferred` — derived by the agent from observations
- `Durable` — manually set or from world KB search results

### WorldFact Resolver (Sprint 17)

`LocalKnowledgeResolver` includes a fourth resolution step that scans `WorldState.StructuredFacts` for keys containing the normalized item ID:

| Age | Confidence |
|-----|-----------|
| < 60 seconds | 0.70 |
| ≥ 60 seconds | 0.50 |

This allows the agent to reuse recent world knowledge without a network call.

## IKnowledgeResolver Interface

`IKnowledgeResolver` (implemented by `LocalKnowledgeResolver`) provides a unified item resolution pipeline used by `GoalFactory` and other components.

```csharp
public interface IKnowledgeResolver
{
    Task<ResolutionResult?> ResolveAsync(string query, WorldState? state = null, CancellationToken ct = default);
}
```

## Knowledge Resolver Pipeline (Phase 7-B)

`LocalKnowledgeResolver.ResolveAsync` resolves items in order:

1. **Normalize query** — lowercase, trim, strip pluralization
2. **IItemRegistry.GetAsync** — exact match against item registry wiki pages (confidence **0.95**)
3. **IMemoryGateway.SearchAsync** — wiki search results from MemorySmith (confidence **0.60 × search score**)
4. **WorldState.StructuredFacts scan** — recent world facts matching the item (confidence **0.70** if < 60s old, **0.50** if older)
5. **Type filter + confidence cap + TopN sort** — results filtered by `CandidateType` (Smeltable, DirectMineable, Craftable, WikiItem, WorldFact, WikiPage) and capped
6. **Ambiguity detection** — if top-2 candidates are within 0.05 confidence → `WasAmbiguous` flag set

The old Phase 5 pipeline (CraftingRecipes → DirectMineBlocks → SourceBlocks → WorldFacts → Unknown) used hardcoded dictionaries. The Phase 7-B pipeline uses `IItemRegistry` for exact matches and `IMemoryGateway.SearchAsync` for wiki-driven resolution.

## Context Preservation Across Replans

`ReplanAsync` preserves `ActionData.Context` entries with these prefixes:

```
SearchMemory:   CraftItem:   FindFlatArea:   Build:   MoveTo:
```

This ensures block locations found by `SearchMemory` survive replanning.

## Search Pipeline

MemorySmith uses a hybrid search pipeline:

1. **BM25 (Lucene)** — fast keyword full-text index. Live from day one.
2. **Vector embeddings** — semantic similarity (Ollama local or OpenAI). Phase 5.
3. **Graph relations** — page link graph for related-page traversal. Future.

## SearchResult.Kind Disambiguation

`SearchResult.Kind` distinguishes between two result types — critical to check before passing a PageId to `GetPageAsync`:

| Kind | Meaning | PageId Usage |
|------|---------|-------------|
| `"page"` | Standard wiki page | ✅ Safe to pass to `GetPageAsync` as slug |
| `"memory"` | Structured memory record (UUID) | ❌ PageId is a UUID, NOT a valid page slug |

## API Reference

See `/api/agent/resolve` in [API Reference](guides/api-reference.md) for the knowledge resolver REST endpoint.

```bash
# Resolve an item via the REST endpoint
curl http://localhost:5000/api/agent/resolve?item=diamond
```
