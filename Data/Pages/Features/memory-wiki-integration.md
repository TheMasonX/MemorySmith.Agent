# Memory & Wiki Integration

**Feature ID:** F-MEMORY  
**Status:** Core (Stable)  
**Location:** `Agent.Memory/`

The agent uses the MemorySmith wiki as its long-term memory through a dual-gateway `IMemoryGateway` setup. Each gateway connects to a separate MemorySmith instance for different knowledge domains.

## Dual Gateway Architecture

```
Agent Memory Gateway (default, http://localhost:5001)
  → Agent KB: codebase knowledge, guides, architecture, blueprints
  → Used by: GetPageTool

World Memory Gateway ('world', configurable URL)
  → World KB: world facts, exploration log, observations
  → Used by: SearchMemoryTool, CreatePageTool
```

## IMemoryGateway Interface

| Method | HTTP Equivalent | Purpose |
|--------|----------------|---------|
| `SearchAsync(query)` | GET /api/search | Unified search (pages + memories) |
| `GetPageAsync(slug)` | GET /api/pages/{slug} | Read page content |
| `CreatePageAsync(slug, ...)` | POST /api/pages | Write new page |
| `UpdatePageAsync(slug, ...)` | PUT /api/pages/{slug} | Update existing page |

## Local Knowledge Resolver

When the MemorySmith instance is unavailable, `LocalKnowledgeResolver` provides local file fallback:
- Maps slugs to files under `Data/Pages/` and `Data/Memories/`
- Supports item registry pages from `Data/Pages/item-registry/`
- Used as fallback chain in `MemorySmithBlueprintRepository`

## Configuration

```json
{
  "Agent": {
    "Memory": {
      "BaseUrl": "http://localhost:5001",
      "ApiKey": null,
      "TimeoutSeconds": 30,
      "DefaultPageRole": "User",
      "WorldKbUrl": null
    }
  }
}
```

## Related

- [Wiki Integration Memory](../memories/Core/agent-wiki-integration.json)
- [Memory Gateway Integration](../memories/Core/agent-memorygateway-integration.json)
- [Memory Wiki Page](../memory.md)
- [MemorySmith Setup Guide](../guides/memorysmith-setup.md)
- [World KB Guide](../guides/world-kb.md)
