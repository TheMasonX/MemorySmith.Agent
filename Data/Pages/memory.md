# Memory Architecture

The agent's persistent memory is a MemorySmith wiki. All long-term state lives here — facts, plans, blueprints, observations, and agent profile. The agent reads and writes memory via `IMemoryGateway`.

## Memory Types

| Type | Examples | Storage |
|---|---|---|
| **Facts / World Knowledge** | "Village at X,Y", "Stone ore at (x,z)" | MemorySmith structured memories |
| **Observations** | Sensor logs, screenshots | Pages in `Data/Pages/observations/` |
| **Goals / Plans** | Current mission log | Pages in `Data/Pages/plans/` |
| **Blueprints / Designs** | GothicCathedral, FarmLayout | Pages in `Data/Pages/blueprints/` |
| **Agent Profile** | Name, backstory, preferences | `#AgentProfile` page |

## IMemoryGateway

Three integration patterns — all use the same interface:

```csharp
public interface IMemoryGateway
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, ...);
    Task<string?> GetPageAsync(string pageId, ...);
    Task<string> CreatePageAsync(string title, string content, string type, ...);
    Task UpdatePageAsync(string pageId, string content, ...);
}
```

### In-Process (fastest)

Agent host and MemorySmith run in the same process. Direct method calls — no serialization.

```csharp
var results = await memoryGateway.Search("gothic architecture");
```

### MCP Tool

The LLM calls `SearchMemory`, `GetPage`, `CreatePage` tools. The gateway dispatches under the hood.
Transparent to the LLM — it sees tool calls; the gateway talks to MemorySmith.

### REST API

```
GET  /api/wiki?page=GothicCathedral
POST /api/wiki  { title, content, type }
```

Used by the Blazor UI and remote agents.

## Search Pipeline

MemorySmith uses a hybrid search pipeline:

1. **BM25 (Lucene)** — fast keyword full-text index. Live from day one.
2. **Vector embeddings** — semantic similarity (OpenAI embeddings / Ollama local). Phase 5.
3. **Graph relations** — page link graph for related-page traversal. Future.

Result: `SearchMemory("gothic cathedral")` returns ranked pages combining keyword and semantic scores.

## Lifecycle

Memory pages can be tagged for lifecycle management:
- **Active** — currently relevant, injected into context.
- **Archived** — preserved but not injected.
- **Consolidated** — merged/summarized by the maintenance agent.

This prevents memory bloat as the wiki grows.
