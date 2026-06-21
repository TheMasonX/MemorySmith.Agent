# Build Origin System

> Sprint 35 — Coordinate resolution for blueprint placement.

## Overview

Blueprints define block positions **relative** to a build origin — they are "stamps" that can be placed anywhere in the world. The system resolves the absolute world coordinates using a priority chain:

```
worldCoord = origin + blueprintOffset
```

## Origin Resolution Priority

| Priority | Source | Example |
|----------|--------|---------|
| 1 (highest) | Explicit origin from chat | `"build a house at 100 64 200"` |
| 2 | World-state facts | `build:small-house:origin:x` via REST API |
| 3 (fallback) | Auto-detect `FindFlatArea` | Nearest flat spot near the agent |

### 1. Chat Coordinates

Users can supply optional coordinates in chat:

```
build a house at 100 64 200
build shelter at -50 64 300
make small-house at 0 64 0
```

The `BuildRegex` captures the `at X Y Z` suffix and passes them as `originX`, `originY`, `originZ` parameters through `GoalFactory` → `BuildGoal`.

### 2. World-State Facts

Facts are stored as `build:{blueprintId}:origin:{axis}` and can be set via:

- **REST API**: `POST /api/agent/origin` with body `{ blueprintId, x, y, z }`
- **Auto-detect** (`FlatAreaFoundEvent`): sets `build:auto:origin:x/y/z` facts

### 3. Auto-Detect Fallback

When no explicit origin or stored facts exist, `BuildGoalDecomposer` sets `requireOrigin=true` on the `DecomposeBuild` call, which emits a `FindFlatArea` action. This scans the terrain and resolves a suitable flat spot automatically.

## Key Files

| File | Purpose |
|------|---------|
| `Agent.Planning/Goals/BuildGoal.cs` | Goal model with optional `OriginX/Y/Z` |
| `Agent.Planning/GoalFactory.cs` | Reads origin params from chat, passes to BuildGoal |
| `Agent.Planning/ChatInterpreter.cs` | `BuildRegex` captures optional `at X Y Z` |
| `Agent.Planning/LlmChatInterpreter.cs` | LLM prompt passes `x/y/z` for build intent |
| `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` | Origin resolution + auto-detect fallback |
| `Agent.Planning/HtnTaskLibrary.cs` | `DecomposeBuild` with `requireOrigin` flag |

## Design Decisions

- **Blueprints are stamps**: They never store absolute positions. All `PlacementBlock` coordinates are relative offsets (starting at 0,0,0).
- **No silent (0,0,0) default**: When no origin is available, the system emits a `FindFlatArea` action instead of building at the world origin.
- **LLM can supply coords**: The LLM prompt's JSON schema includes `x`, `y`, `z` fields. When the LLM routes a build intent, those coordinates are passed as `originX/Y/Z` parameters.
