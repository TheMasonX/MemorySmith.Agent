# Guide: Adding a New Tool

Tools are the agent's atomic capabilities, exposed to the LLM via Model Context Protocol (MCP). Each tool has a name, JSON schema, and an `ExecuteAsync` implementation.

## When to add a tool

- When the agent needs a new **world action** (place block, attack entity, craft item)
- When the agent needs a new **memory operation** (update page, delete task)
- When the agent needs to **query information** not available through existing tools

## Step 1 — Implement ITool

Add a file to `Agent.Tools/Tools/`:

```csharp
// Agent.Tools/Tools/CraftItemTool.cs
namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>Crafts an item using the bot's crafting table.</summary>
public sealed class CraftItemTool(IWorldAdapter worldAdapter) : ITool
{
    public string Name => "CraftItem";
    public string Description =>
        "Craft an item using materials in the bot's inventory and a nearby crafting table.";

    public JsonElement InputSchema => JsonDocument.Parse(
        "{\"type\":\"object\",\"properties\":{" +
        "\"item\":{\"type\":\"string\",\"description\":\"Item to craft (e.g. minecraft:crafting_table)\"},{" +
        "\"count\":{\"type\":\"integer\",\"default\":1}}," +
        "\"required\":[\"item\"]}"
    ).RootElement;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("item", out var itemEl))
            return new ToolResult(false, "CraftItem requires an 'item' argument.");

        var item  = itemEl.GetString() ?? "";
        var count = arguments.TryGetProperty("count", out var cEl) ? cEl.GetInt32() : 1;

        await worldAdapter.SendActionAsync(
            new ActionData { Tool = "craft", Arguments = { ["item"] = item, ["count"] = (object?)count } },
            cancellationToken);

        return new ToolResult(true, $"Craft({item} x{count}) dispatched.");
    }
}
```

**Key rules:**
- `Name` must be unique across all registered tools (case-insensitive).
- `InputSchema` uses JSON Schema — validated before dispatch in Phase 4.
- `ExecuteAsync` should be fast and non-blocking (fire-and-forget via adapter).

## Step 2 — Register in DI (Program.cs)

Inside the `if (agentEnabled)` block in `WebUI.Blazor/Program.cs`, add:

```csharp
registry.Register(new CraftItemTool(world));
```

The tool is now available in the tool catalog and callable via `/api/agent/command` or the planner.

## Step 3 — Add to HtnTaskLibrary (optional)

If the tool should be used in predefined HTN tasks, add it to a decomposer in `HtnTaskLibrary.cs`:

```csharp
private static IReadOnlyList<ActionData> CraftToolDecompose(
    string[] _, WorldState __) =>
[
    MakeAction("CraftItem", ("item", "minecraft:crafting_table"), ("count", (object?)1)),
    MakeAction("GetStatus"),
];
```

## Step 4 — Write tests

Add `CraftItemToolTests.cs` to `MemorySmith.Agent.Tests/`:

```csharp
[TestFixture]
public class CraftItemToolTests
{
    [Test]
    public async Task ExecuteAsync_ValidItem_DispatchesAction()
    {
        var adapter = new MockWorldAdapter();
        var tool = new CraftItemTool(adapter);
        var args = JsonDocument.Parse("{\"item\":\"minecraft:crafting_table\"}").RootElement;

        var result = await tool.ExecuteAsync(args);

        Assert.That(result.Success, Is.True);
        Assert.That(adapter.SentActions, Has.Count.EqualTo(1));
        Assert.That(adapter.SentActions[0].Tool, Is.EqualTo("craft"));
    }

    [Test]
    public async Task ExecuteAsync_MissingItem_ReturnsFailure()
    {
        var adapter = new MockWorldAdapter();
        var tool = new CraftItemTool(adapter);
        var result = await tool.ExecuteAsync(JsonDocument.Parse("{}").RootElement);
        Assert.That(result.Success, Is.False);
    }
}
```

## Tool naming conventions

| Prefix | Type | Example |
|---|---|---|
| `Get*` | Query (no side effects) | `GetStatus`, `GetPage` |
| `Search*` | Search | `SearchMemory` |
| `Create*` | Create resource | `CreatePage` |
| `Move*` | Navigation | `MoveTo` |
| `Mine*` | World action | `MineBlock` |
| `Craft*` | Crafting | `CraftItem` |
| `Place*` | Build | `PlaceBlock` |

## Memory-backed tools

If your tool reads/writes MemorySmith, inject `IMemoryGateway` instead of `IWorldAdapter`.
See `SearchMemoryTool.cs`, `GetPageTool.cs`, and `CreatePageTool.cs` for examples.
