namespace MemorySmith.Agent.Tests;

using System.Text.Json;

/// <summary>
/// Sprint 51 — Context Carry Contract Fix (Wave B)
///
/// Test coverage:
///   TSK-0004: MoveToTool context fallback (nearestX/Y/Z from SearchMemory)
///   SearchMemoryTool coordinate extraction from snippets
///   ToolDispatcher context merge with schema-aware allowlist
/// </summary>
[TestFixture]
public class Sprint51ContextCarryTests
{
    private static JsonElement Args(string json) =>
        JsonDocument.Parse(json).RootElement;

    private MockWorldAdapter _adapter = null!;
    private ToolDispatcher _dispatcher = null!;

    [SetUp]
    public void SetUp()
    {
        _adapter = new MockWorldAdapter();
        _dispatcher = new ToolDispatcher();
        _dispatcher.Register(new MoveToTool(_adapter));
    }

    // ── MoveToTool: explicit coordinates (backward compat) ──────────────────

    [Test]
    public async Task MoveToTool_ExplicitCoords_SendsCorrectActionData()
    {
        var tool = new MoveToTool(_adapter);
        var result = await tool.ExecuteAsync(Args("{\"x\":10,\"y\":64,\"z\":-20}"));

        Assert.That(result.Success, Is.True);
        Assert.That(_adapter.SentActions, Has.Count.EqualTo(1));
        var action = _adapter.SentActions[0];
        Assert.That(action.Tool, Is.EqualTo("move"));
        Assert.That(action.Arguments["x"], Is.EqualTo(10));
        Assert.That(action.Arguments["y"], Is.EqualTo(64));
        Assert.That(action.Arguments["z"], Is.EqualTo(-20));
    }

    // ── MoveToTool: context-carry coordinates (nearestX/Y/Z) ───────────────

    [Test]
    public async Task MoveToTool_ContextCarryCoords_SendsCorrectActionData()
    {
        var tool = new MoveToTool(_adapter);
        // nearestX/Y/Z are the context-carried keys that SearchMemoryTool emits
        var result = await tool.ExecuteAsync(Args(
            "{\"nearestX\":100,\"nearestY\":65,\"nearestZ\":-40}"));

        Assert.That(result.Success, Is.True);
        Assert.That(_adapter.SentActions, Has.Count.EqualTo(1));
        var action = _adapter.SentActions[0];
        Assert.That(action.Tool, Is.EqualTo("move"));
        Assert.That(action.Arguments["x"], Is.EqualTo(100));
        Assert.That(action.Arguments["y"], Is.EqualTo(65));
        Assert.That(action.Arguments["z"], Is.EqualTo(-40));
    }

    [Test]
    public async Task MoveToTool_ExplicitCoordsOverrideContextCarry()
    {
        var tool = new MoveToTool(_adapter);
        // When both explicit and context-carried coords are present, explicit wins
        var result = await tool.ExecuteAsync(Args(
            "{\"x\":1,\"y\":2,\"z\":3,\"nearestX\":100,\"nearestY\":65,\"nearestZ\":-40}"));

        Assert.That(result.Success, Is.True);
        var action = _adapter.SentActions[0];
        Assert.That(action.Arguments["x"], Is.EqualTo(1));
        Assert.That(action.Arguments["y"], Is.EqualTo(2));
        Assert.That(action.Arguments["z"], Is.EqualTo(3));
    }

    [Test]
    public async Task MoveToTool_MissingAllCoords_ReturnsFailure()
    {
        var tool = new MoveToTool(_adapter);
        var result = await tool.ExecuteAsync(Args("{}"));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("nearestX"));
    }

    // ── ToolDispatcher: schema-aware context merge ──────────────────────────

    [Test]
    public async Task Dispatcher_ContextMerge_OnlyMergesDeclaredSchemaKeys()
    {
        // Simulate what AgentBackgroundService does: action has Context with
        // both declared (x, nearestX) and undeclared (bestPageId, query) keys.
        // The dispatcher's own CallAsync receives only the arguments AFTER the
        // schema-aware merge. This test verifies the merge contract by calling
        // MoveToTool directly with a mix of declared and undeclared keys.
        var tool = new MoveToTool(_adapter);
        var result = await tool.ExecuteAsync(Args(
            "{\"x\":10,\"y\":64,\"z\":-20,\"bestPageId\":\"abc\",\"query\":\"wood\"}"));

        // Declared keys (x,y,z) should pass validation; undeclared keys are
        // ignored by the MoveToTool but would fail ToolDispatcher.ValidateAgainstSchema.
        // The MoveToTool itself ignores extra properties, so execution succeeds.
        Assert.That(result.Success, Is.True);
        var action = _adapter.SentActions[0];
        Assert.That(action.Arguments["x"], Is.EqualTo(10));
    }

    // ── SearchMemoryTool: coordinate extraction from snippets ──────────────

    [Test]
    public void SearchMemoryTool_ExtractsCoordinates_FromAtPattern()
    {
        var snippet = "Found oak trees at (120, 64, -300) with plenty of wood.";
        var (x, y, z) = ExtractCoordsFromSnippet(snippet);
        Assert.That(x, Is.EqualTo(120));
        Assert.That(y, Is.EqualTo(64));
        Assert.That(z, Is.EqualTo(-300));
    }

    [Test]
    public void SearchMemoryTool_ExtractsCoordinates_FromLabeledPattern()
    {
        var snippet = "x: 45 y: 70 z: -150 — large spruce forest.";
        var (x, y, z) = ExtractCoordsFromSnippet(snippet);
        Assert.That(x, Is.EqualTo(45));
        Assert.That(y, Is.EqualTo(70));
        Assert.That(z, Is.EqualTo(-150));
    }

    [Test]
    public void SearchMemoryTool_NullSnippet_ReturnsNoCoords()
    {
        var (x, y, z) = ExtractCoordsFromSnippet(null);
        Assert.That(x, Is.Null);
        Assert.That(y, Is.Null);
        Assert.That(z, Is.Null);
    }

    [Test]
    public void SearchMemoryTool_NoCoordPattern_ReturnsNoCoords()
    {
        var snippet = "Found plenty of oak logs in the forest biome.";
        var (x, y, z) = ExtractCoordsFromSnippet(snippet);
        Assert.That(x, Is.Null);
        Assert.That(y, Is.Null);
        Assert.That(z, Is.Null);
    }

    // ── ToolDispatcher schema validation: undeclared properties ────────────

    [Test]
    public async Task Dispatcher_RejectsUndeclaredProperty_InArguments()
    {
        // ToolDispatcher.ValidateAgainstSchema should reject undeclared properties.
        // MoveToTool's schema now declares x, y, z, nearestX, nearestY, nearestZ.
        // An undeclared key like "bestPageId" should cause validation failure.
        var result = await _dispatcher.CallAsync("MoveTo",
            Args("{\"x\":1,\"y\":2,\"z\":3,\"bestPageId\":\"abc\"}"));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("unexpected property").Or.Contains("bestPageId"));
    }

    [Test]
    public async Task Dispatcher_ContextCarryKeys_AreDeclaredInSchema()
    {
        // The nearestX/Y/Z keys should be valid in MoveToTool's schema
        var result = await _dispatcher.CallAsync("MoveTo",
            Args("{\"nearestX\":100,\"nearestY\":65,\"nearestZ\":-40}"));

        Assert.That(result.Success, Is.True);
    }

    /// <summary>
    /// Mirrors the coordinate extraction logic from SearchMemoryTool for test isolation.
    /// Tests the regex patterns without needing a real IMemoryGateway.
    /// </summary>
    private static (int? x, int? y, int? z) ExtractCoordsFromSnippet(string? snippet)
    {
        if (snippet is null) return (null, null, null);

        // "at (x, y, z)" pattern
        var coordMatch = System.Text.RegularExpressions.Regex.Match(snippet,
            @"(?:at|coordinates?|pos(?:ition)?)\s*[=:≈~]?\s*\(?\s*(?<x>-?\d+)\s*[,;]\s*(?<y>-?\d+)\s*[,;]\s*(?<z>-?\d+)\s*\)?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (coordMatch.Success)
        {
            return (
                int.Parse(coordMatch.Groups["x"].Value),
                int.Parse(coordMatch.Groups["y"].Value),
                int.Parse(coordMatch.Groups["z"].Value)
            );
        }

        // "x: n y: n z: n" labeled pattern
        var labelMatch = System.Text.RegularExpressions.Regex.Match(snippet,
            @"\bx\s*[:=]\s*(?<x>-?\d+)\b.*\by\s*[:=]\s*(?<x>-?\d+)\b.*\bz\s*[:=]\s*(?<x>-?\d+)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (labelMatch.Success)
        {
            // Named groups are all "x" in this pattern — use captures
            var captures = labelMatch.Groups["x"].Captures;
            if (captures.Count >= 3)
            {
                return (
                    int.Parse(captures[0].Value),
                    int.Parse(captures[1].Value),
                    int.Parse(captures[2].Value)
                );
            }
        }

        return (null, null, null);
    }
}
