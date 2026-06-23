# Blueprint System

**Feature ID:** F-BLUEPRINT  
**Status:** Core (Stable)  
**Location:** `Agent.Construction/`, `Agent.Memory/MemorySmithBlueprintRepository.cs`

The blueprint system allows the agent to build structures from markdown-based blueprint files. It covers the full pipeline from parsing to execution.

## Pipeline

```
Blueprint Markdown → BlueprintParser
                  → Blueprint record (schema-validated)
                  → MemorySmithBlueprintRepository (3-stage lookup)
                  → BuildGoal → BuildGoalDecomposer
                  → HtnTaskLibrary.DecomposeBuild → Tool Dispatcher
                  → BlueprintExecutor → PlaceBlock actions
```

## Blueprint Format

Blueprints are markdown files with YAML frontmatter and layer grids:

```markdown
---
id: small-house
name: Small Survival House
tags: house, starter, survival
dimensions: 9x5x7
materials: cobblestone x 63, oak_planks x 70
---

## Layers

### Y=0 (Floor)
CCCCCCCCC
...

## Legend
. = air, C = cobblestone, P = oak_planks, ...
```

## Repository Lookup (3-stage)

1. **Direct slug** on live MemorySmith gateway (`blueprints/{slug}`)
2. **Local file fallback** (`Data/Pages/blueprints/{slug}.md`)
3. **Search fallback** on gateway

## Blueprint Alias Resolution

Shorthand names are resolved at multiple points:
- `"house"` → `"small-house"` (IntentManager)
- `"house"` → `"small-house"` (ChatInterpreter, duplicated)
- Slug normalization: underscores → hyphens, lowercase

## Available Blueprints

- [small-house](../blueprints/small-house.md) — Compact survival starter (cobblestone + oak)
- [farm](../blueprints/farm.md) — Multi-layer farm structure
- [castle](../blueprints/castle.md) — Large-scale castle (complex build)
- [wizards-tower](../blueprints/wizards-tower.md) — Tall wizard tower

## Related

- [Blueprint System Memory](../memories/Core/agent-blueprint-system.json)
- [Build Pipeline State](../memories/Core/agent-build-pipeline-state.json)
- [Blueprint Wiki Page](../blueprints.md)
- [Adding a Goal Guide](../guides/adding-a-goal.md)
