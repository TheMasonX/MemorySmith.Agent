# Tool Registry (MCP Schema)

All tools available to the LLM are registered via Model Context Protocol (MCP). Each tool has a name, description (for prompt context), and a JSON Schema for arguments and return values.

The LLM calls tools as structured JSON; the `ToolEngine` validates and dispatches. No arbitrary code execution.

## Core Tools

### Memory Tools

| Tool | Arguments | Returns | Description |
|---|---|---|---|
| `SearchMemory` | `{query: string}` | `{results: [{pageId, score}]}` | Full-text / semantic search in MemorySmith |
| `GetPage` | `{pageId: string}` | `{content: string}` | Read a wiki page |
| `CreatePage` | `{title, content, type}` | `{pageId: string}` | Add memory or blueprint page |
| `UpdateTask` | `{taskId, status, details}` | `{status}` | Update or complete a task |

### World Tools

| Tool | Arguments | Returns | Description |
|---|---|---|---|
| `MoveTo` | `{x: int, y: int, z: int}` | `{success: bool}` | Navigate bot to coordinates |
| `MineBlock` | `{block: string, count: int}` | `{found: int, inventory: {…}}` | Mine specified blocks |
| `PlaceBlock` | `{x, y, z, material: string}` | `{success: bool}` | Place a block at coordinates |
| `AttackEntity` | `{entityId: string}` | `{success: bool}` | Attack a nearby entity |
| `FindNearestResource` | `{item: string}` | `{location: {x,y,z}}` | Query memory for resource location |

### Construction Tools

| Tool | Arguments | Returns | Description |
|---|---|---|---|
| `ConstructBlueprint` | `{blueprintId: string, origin: {x,y,z}}` | `{success: bool}` | Instantiate a saved blueprint in-world |
| `CreateGoal` | `{goalName, parameters}` | `{goalMeta}` | Start a new high-level goal |
| `TakeScreenshot` | `{}` | `{imageData: base64}` | Capture screenshot for aesthetic analysis |

## Example Tool Call (LLM → ToolEngine)

```json
{
  "tool": "SearchMemory",
  "arguments": { "query": "gothic architecture features" }
}
```

## Tool Schema Sample

```json
{
  "name": "SearchMemory",
  "description": "Full-text and semantic search across MemorySmith wiki pages and memories.",
  "inputSchema": {
    "type": "object",
    "properties": { "query": { "type": "string" } },
    "required": ["query"]
  }
}
```

## Safety

- All tool calls are validated against the registered `ITool.InputSchema`.
- Destructive actions (`PlaceBlock`, `AttackEntity`) can be sandboxed or approval-gated.
- A `ManualOverride` flag allows the user to pause autonomous execution via the UI.
- Dry-run mode logs planned actions without executing them.
