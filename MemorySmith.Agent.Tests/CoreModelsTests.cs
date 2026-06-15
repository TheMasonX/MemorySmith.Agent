namespace MemorySmith.Agent.Tests;

using Agent.Core;
using Agent.Tools;
using System.Text.Json;

[TestFixture]
public class CoreModelsTests
{
    [Test]
    public void WorldState_DefaultPosition_IsOrigin()
    {
        var state = new WorldState();
        Assert.Multiple(() =>
        {
            Assert.That(state.Position.X, Is.EqualTo(0));
            Assert.That(state.Position.Y, Is.EqualTo(64));
            Assert.That(state.Position.Z, Is.EqualTo(0));
            Assert.That(state.Health, Is.EqualTo(20));
        });
    }

    [Test]
    public void WorldState_With_UpdatesFact()
    {
        var state = new WorldState();
        var updated = state.With(b => b.SetFact("biome", "forest"));
        Assert.That(updated.Facts["biome"]?.ToString(), Is.EqualTo("forest"));
    }

    [Test]
    public void ActionQueue_EnqueueDequeue_WorksCorrectly()
    {
        var queue = new ActionQueue();
        var action = new ActionData { Tool = "MoveTo", Arguments = { ["x"] = (object?)10 } };
        queue.Enqueue(action);

        Assert.That(queue.Count, Is.EqualTo(1));
        var dequeued = queue.Dequeue();
        Assert.That(dequeued?.Tool, Is.EqualTo("MoveTo"));
        Assert.That(queue.IsEmpty, Is.True);
    }

    [Test]
    public void ActionQueue_EnqueueAll_AddsMultiple()
    {
        var queue = new ActionQueue();
        queue.EnqueueAll([
            new ActionData { Tool = "MoveTo" },
            new ActionData { Tool = "MineBlock" },
        ]);
        Assert.That(queue.Count, Is.EqualTo(2));
    }

    [Test]
    public void ToolResult_Success_HasCorrectFlag()
    {
        var result = new ToolResult(true, "Done");
        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Is.EqualTo("Done"));
    }

    [Test]
    public void ToolRegistry_RegisterAndGet_FindsTool()
    {
        var registry = new ToolRegistry();
        var tool = new StubTool("TestTool");
        registry.Register(tool);

        Assert.That(registry.Get("TestTool"), Is.SameAs(tool));
        Assert.That(registry.Get("nonexistent"), Is.Null);
    }

    [Test]
    public void ToolRegistry_Get_IsCaseInsensitive()
    {
        var registry = new ToolRegistry();
        registry.Register(new StubTool("MoveTo"));
        Assert.That(registry.Get("moveto"), Is.Not.Null);
        Assert.That(registry.Get("MOVETO"), Is.Not.Null);
    }
}

/// <summary>Minimal ITool stub for registry tests.</summary>
file sealed class StubTool(string name) : ITool
{
    public string Name => name;
    public string Description => "stub";
    public JsonElement InputSchema => JsonDocument.Parse("{}").RootElement;
    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
        => Task.FromResult(new ToolResult(true));
}
