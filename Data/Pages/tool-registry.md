# Tool Registry

All tools available to the agent are registered in `ToolDispatcher` via `RegisterTool`. Each tool exposes a JSON Schema (`InputSchema`) that is validated before execution. The LLM calls tools as structured JSON; `ToolDispatcher` validates and dispatches — no arbitrary code execution.

> **Sprint 5 note:** `ToolEngine` and `ToolRegistry` were deleted. `ToolDispatcher` is the single consolidated dispatcher for both LLM-driven and imperative tool calls.

## Registered Tools

### Memory Tools

| Tool | Arguments | Returns | Routes to |
|---|---|---|---|
| `SearchMemory` | `{query: string}` | `{results: [{pageId, title, score}]}` | World KB |
| `GetPage` | `{pageId: string}` | `{content: string}` | Agent KB |
| `CreatePage` | `{title, content, type}` | `{pageId: string}` | World KB |

**World KB routing (Sprint 23):** `SearchMemory` and `CreatePage` route to the world-keyed `IMemoryGateway` (exploration log, block locations, world facts). `GetPage` routes to the default agent KB (architecture, guides, codebase knowledge). See [World KB Guide](world-kb.md).

### World Tools

| Tool | Arguments | Returns | Description |
|---|---|---|---|
| `GetStatus` | `{}` | `{health, food, position, inventory}` | Query current bot state from Mineflayer |
| `MoveTo` | `{x: int, y: int, z: int}` | `{success: bool}` | Navigate bot to coordinates via pathfinder |
| `MineBlock` | `{block: string, count: int}` | `{found: int, inventory: {…}}` | Mine specified blocks |
| `PlaceBlock` | `{x, y, z, material: string}` | `{success: bool}` | Place a block at coordinates |
| `CraftItem` | `{item: string, count: int}` | `{success: bool, inventory: {…}}` | Craft an item using crafting table |
| `SmeltItem` | `{item: string, count: int}` | `{success: bool}` | Smelt items in furnace |
| `Wander` | `{radius: int}` | `{newPosition: {x,y,z}}` | Wander randomly within radius |
| `FindFlatArea` | `{radius: int}` | `{origin: {x,y,z}, area: int}` | Find flat ground for construction (default radius 32) |

### Chat Tool

| Tool | Arguments | Returns | Description |
|---|---|---|---|
| `Chat` | `{message: string}` | `{sent: bool}` | Send a chat message in-game |

## Tool Dispatch & Validation (Sprint 5)

`ToolDispatcher.CallAsync` performs these steps before execution:

1. **Name lookup** — if the tool name is not registered, returns `ToolNotFound` error.
2. **Schema validation** — checks all args against `ITool.InputSchema`:
   - `"type"` constraints (string, integer, number, boolean, object, array)
   - `"required"` field presence
   - `"properties"` structure
   - Extra properties not in schema raise a validation error.
3. **Execution** — calls `tool.ExecuteAsync(args, ct)` with a 30-second action timeout.
4. **Journal** — records success or failure in `IAgentJournal`.

Validation errors are returned as structured `ToolValidationError` responses, not exceptions.

## FailureReason Enum

When a tool fails, `IGoal.FailureReason` is set to one of:

| Value | Meaning |
|---|---|
| `ToolTimeout` | Action exceeded 30-second timeout |
| `TargetUnreachable` | Pathfinder could not reach target |
| `InventoryFull` | No room for gathered items |
| `RecipeMissing` | Crafting recipe not known / ingredients not available |
| `ConsecutiveFailures` | Too many consecutive tool failures |
| `NoValidActions` | Planner returned empty plan |
| `Unknown` | Unclassified error |

## /api/agent/command Lockdown (Sprint 5)

`POST /api/agent/command` only accepts registered tool names. Unknown tool names return:

```json
{
  "error": "Unknown tool 'FlyToMoon'.",
  "available": ["GetStatus", "MoveTo", "MineBlock", "SearchMemory", "GetPage", "CreatePage", "CraftItem", "SmeltItem", "Chat", "Wander", "FindFlatArea"]
}
```

## Tool Schema Sample

```json
{
  "name": "SearchMemory",
  "description": "Semantic and keyword search in the World KB wiki (block locations, events, world facts). Use for discovering what the agent has observed in the world.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "query": { "type": "string" }
    },
    "required": ["query"]
  }
}
```

## Adding New Tools

See [Adding a Tool](guides/adding-a-tool.md) for the step-by-step guide. All tools must:
1. Implement `ITool` with a descriptive `Description` (visible to LLM)
2. Define a complete `InputSchema` (JSON Schema)
3. Be registered via `ToolDispatcher.RegisterTool(tool)` in `Program.cs`
4. Have at least one unit test covering the happy path and one error case
