using Agent.Core;
using Agent.Tools;
using System.Text.Json;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Tests that each tool correctly shapes the ActionData dispatched to MockWorldAdapter.
/// Previously untested (Finding 7 — zero per-tool tests).
/// This is the "interface is the test surface" principle: verify what crosses the seam.
/// </summary>
[TestFixture]
public class ToolDispatchTests
{
    private MockWorldAdapter _adapter = null!;

    [SetUp]
    public void SetUp() => _adapter = new MockWorldAdapter();

    // ── Helper ────────────────────────────────────────────────────────────────

    private static JsonElement Args(string json) =>
        JsonDocument.Parse(json).RootElement;

    // ── MoveToTool ────────────────────────────────────────────────────────────

    [Test]
    public async Task MoveToTool_SendsCorrectActionData()
    {
        var tool   = new MoveToTool(_adapter);
        var result = await tool.ExecuteAsync(Args("{\"x\":10,\"y\":64,\"z\":-20}"));

        Assert.That(result.Success, Is.True);
        Assert.That(_adapter.SentActions, Has.Count.EqualTo(1));
        var action = _adapter.SentActions[0];
        Assert.That(action.Tool, Is.EqualTo("move"));
        Assert.That(action.Arguments["x"], Is.EqualTo(10));
        Assert.That(action.Arguments["y"], Is.EqualTo(64));
        Assert.That(action.Arguments["z"], Is.EqualTo(-20));
    }

    [Test]
    public async Task MoveToTool_MissingCoords_ReturnsFailure()
    {
        var tool   = new MoveToTool(_adapter);
        var result = await tool.ExecuteAsync(Args("{\"x\":10}"));  // missing y, z

        Assert.That(result.Success, Is.False);
        Assert.That(_adapter.SentActions, Is.Empty);
    }

    // ── MineBlockTool ─────────────────────────────────────────────────────────

    [Test]
    public async Task MineBlockTool_SendsCorrectActionData()
    {
        var tool   = new MineBlockTool(_adapter);
        var result = await tool.ExecuteAsync(Args("{\"block\":\"minecraft:oak_log\",\"count\":5}"));

        Assert.That(result.Success, Is.True);
        var action = _adapter.SentActions[0];
        Assert.That(action.Tool,              Is.EqualTo("mine"));
        Assert.That(action.Arguments["block"], Is.EqualTo("minecraft:oak_log"));
        Assert.That(action.Arguments["count"], Is.EqualTo(5));
    }

    [Test]
    public async Task MineBlockTool_MissingBlock_ReturnsFailure()
    {
        var tool   = new MineBlockTool(_adapter);
        var result = await tool.ExecuteAsync(Args("{\"count\":3}"));

        Assert.That(result.Success, Is.False);
        Assert.That(_adapter.SentActions, Is.Empty);
    }

    // ── StatusTool ────────────────────────────────────────────────────────────

    [Test]
    public async Task StatusTool_SendsStatusAction()
    {
        var tool   = new StatusTool(_adapter);
        var result = await tool.ExecuteAsync(Args("{}"));

        Assert.That(result.Success, Is.True);
        Assert.That(_adapter.SentActions[0].Tool, Is.EqualTo("status"));
    }

    [Test]
    public async Task GetStatusTool_SendsStatusAction()
    {
        var tool   = new GetStatusTool(_adapter);
        var result = await tool.ExecuteAsync(Args("{}"));

        Assert.That(result.Success, Is.True);
        Assert.That(_adapter.SentActions[0].Tool, Is.EqualTo("status"));
    }

    // ── WanderTool ────────────────────────────────────────────────────────────

    [Test]
    public async Task WanderTool_DefaultParams_SendsWanderAction()
    {
        var tool   = new WanderTool(_adapter);
        var result = await tool.ExecuteAsync(Args("{}"));

        Assert.That(result.Success, Is.True);
        var action = _adapter.SentActions[0];
        Assert.That(action.Tool, Is.EqualTo("wander"));
        Assert.That(action.Arguments["radius"],             Is.EqualTo(20));
        Assert.That(action.Arguments["maxDistanceFromSpawn"], Is.EqualTo(100));
    }

    [Test]
    public async Task WanderTool_CustomRadius_PassesThrough()
    {
        var tool   = new WanderTool(_adapter);
        var result = await tool.ExecuteAsync(Args("{\"radius\":50,\"maxDistanceFromSpawn\":300}"));

        Assert.That(result.Success, Is.True);
        Assert.That(_adapter.SentActions[0].Arguments["radius"], Is.EqualTo(50));
        Assert.That(_adapter.SentActions[0].Arguments["maxDistanceFromSpawn"], Is.EqualTo(300));
    }

    // ── PlaceBlockTool ────────────────────────────────────────────────────────

    [Test]
    public async Task PlaceBlockTool_SendsPlaceAction()
    {
        var tool   = new PlaceBlockTool(_adapter);
        var result = await tool.ExecuteAsync(
            Args("{\"x\":5,\"y\":65,\"z\":10,\"material\":\"cobblestone\"}"));

        Assert.That(result.Success, Is.True);
        var action = _adapter.SentActions[0];
        Assert.That(action.Tool,                  Is.EqualTo("place"));
        Assert.That(action.Arguments["x"],         Is.EqualTo(5));
        Assert.That(action.Arguments["y"],         Is.EqualTo(65));
        Assert.That(action.Arguments["z"],         Is.EqualTo(10));
        Assert.That(action.Arguments["material"],  Is.EqualTo("cobblestone"));
    }

    [Test]
    public async Task PlaceBlockTool_MissingMaterial_ReturnsFailure()
    {
        var tool   = new PlaceBlockTool(_adapter);
        var result = await tool.ExecuteAsync(Args("{\"x\":0,\"y\":64,\"z\":0}"));

        Assert.That(result.Success, Is.False);
        Assert.That(_adapter.SentActions, Is.Empty);
    }

    // ── SearchMemoryTool ──────────────────────────────────────────────────────

    [Test]
    public async Task SearchMemoryTool_ReturnsResultsFromGateway()
    {
        var gateway = new MockMemoryGateway();
        gateway.AddSearchResult("oak log", new SearchResult("tree-page", 0.9, "oak trees at spawn"));

        var tool   = new SearchMemoryTool(gateway);
        var result = await tool.ExecuteAsync(Args("{\"query\":\"oak log\"}"));

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data, Is.Not.Null);
        Assert.That(result.Data!.ContainsKey("results"), Is.True);
    }

    [Test]
    public async Task SearchMemoryTool_WritesBestPageIdToData()
    {
        var gateway = new MockMemoryGateway();
        gateway.AddSearchResult("trees", new SearchResult("forest-page", 0.85, "forest biome"));

        var tool   = new SearchMemoryTool(gateway);
        var result = await tool.ExecuteAsync(Args("{\"query\":\"trees\"}"));

        Assert.That(result.Data!.ContainsKey("bestPageId"), Is.True);
        Assert.That(result.Data["bestPageId"]?.ToString(), Is.EqualTo("forest-page"));
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Sprint 5 — Schema Validation via ToolDispatcher
    // ═══════════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Dispatcher_ValidMoveTo_Dispatches()
    {
        var dispatcher = new ToolDispatcher();
        dispatcher.Register(new MoveToTool(_adapter));

        var result = await dispatcher.CallAsync("MoveTo", Args("{\"x\":10,\"y\":64,\"z\":-20}"));
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task Dispatcher_MissingRequired_ReturnsFailureWithMessage()
    {
        var dispatcher = new ToolDispatcher();
        dispatcher.Register(new MoveToTool(_adapter));

        var result = await dispatcher.CallAsync("MoveTo", Args("{\"x\":10}"));
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Schema validation failed"));
        Assert.That(result.Message, Does.Contain("'y'"));
    }

    [Test]
    public async Task Dispatcher_UnknownProperty_ReturnsFailure()
    {
        var dispatcher = new ToolDispatcher();
        dispatcher.Register(new MoveToTool(_adapter));

        // "colour" is not declared in MoveTo's schema
        var result = await dispatcher.CallAsync("MoveTo",
            Args("{\"x\":10,\"y\":64,\"z\":-20,\"colour\":\"blue\"}"));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("'colour'"));
    }

    [Test]
    public async Task Dispatcher_WrongType_ReturnsFailure()
    {
        var dispatcher = new ToolDispatcher();
        dispatcher.Register(new MoveToTool(_adapter));

        // x must be integer, but "north" is a string
        var result = await dispatcher.CallAsync("MoveTo",
            Args("{\"x\":\"north\",\"y\":64,\"z\":-20}"));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("'x'"));
        Assert.That(result.Message, Does.Contain("integer"));
    }

    [Test]
    public async Task Dispatcher_NonObject_ReturnsFailure()
    {
        var dispatcher = new ToolDispatcher();
        dispatcher.Register(new MoveToTool(_adapter));

        // "north" — a string, not an object
        var result = await dispatcher.CallAsync("MoveTo", Args("\"north\""));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("JSON object"));
    }

    [Test]
    public async Task Dispatcher_UnknownTool_ReturnsFailure()
    {
        var dispatcher = new ToolDispatcher();

        var result = await dispatcher.CallAsync("DoSomethingDangerous", Args("{}"));
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("not registered"));
    }

    [Test]
    public async Task Dispatcher_AllRegisteredTools_ValidateWithoutError()
    {
        // Sanity test: every registered tool's schema matches its expected arguments
        var dispatcher = new ToolDispatcher();
        dispatcher.Register(new MoveToTool(_adapter));
        dispatcher.Register(new MineBlockTool(_adapter));
        dispatcher.Register(new PlaceBlockTool(_adapter));
        dispatcher.Register(new StatusTool(_adapter));
        dispatcher.Register(new GetStatusTool(_adapter));
        dispatcher.Register(new WanderTool(_adapter));
        dispatcher.Register(new CraftItemTool(_adapter));
        dispatcher.Register(new FurnaceTool(_adapter));
        dispatcher.Register(new FindFlatAreaTool(_adapter));
        dispatcher.Register(new ChatTool(_adapter));

        // Valid args for each tool
        var cases = new (string Name, string Args)[]
        {
            ("MoveTo",    "{\"x\":10,\"y\":64,\"z\":-20}"),
            ("MineBlock", "{\"block\":\"oak_log\"}"),
            ("PlaceBlock","{\"x\":0,\"y\":64,\"z\":0,\"material\":\"cobblestone\"}"),
            ("Status",    "{}"),
            ("GetStatus", "{}"),
            ("Wander",    "{\"radius\":30}"),
            ("CraftItem", "{\"item\":\"stick\"}"),
            ("SmeltItem", "{\"item\":\"iron_ore\"}"),
            ("FindFlatArea","{}"),
            ("Chat",      "{\"message\":\"hello\"}"),
        };

        foreach (var (name, argsJson) in cases)
        {
            var result = await dispatcher.CallAsync(name, Args(argsJson));
            Assert.That(result.Success, Is.True,
                $"Tool '{name}' with args {argsJson} should succeed but got: {result.Message}");
        }
    }
}
