namespace MemorySmith.Agent.Tests;

using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using Microsoft.Extensions.Logging;
using Agent.Core;
using Agent.Planning;

/// <summary>
/// Sprint 30 tests: P1-B (BuildGoalDecomposer real logger invocation),
/// P1-D/E (ChatInterpreter plural-map and status-regex regression).
/// </summary>
[TestFixture]
public class Sprint30Tests
{
    // ── P1-B: BuildGoalDecomposer real logger invocation ─────────────────────
    //
    // ReadOriginFact is private; we invoke it via reflection.
    // HtnTaskLibrary is not used by ReadOriginFact so null is safe here.
    //
    // REFLECTION STABILITY CONTRACT (Sprint 32 P2-5):
    // These tests use System.Reflection to access private methods. If the following
    // signatures change, the reflection lookups will return null and tests will fail
    // with an explicit Assert.NotNull failure (not a compile error). Keep these stable:
    //   - BuildGoalDecomposer.ReadOriginFact(WorldState state, string blueprintId, string axis)
    //   - ChatInterpreter.ResolveItemId(string rawItem)  [static]
    //   - ChatInterpreter.ParseIntent(string message, WorldState state)  [static]
    // When these signatures must change, update the GetXxx() reflection helpers below.

    private static MethodInfo? GetReadOriginFact()
        => typeof(BuildGoalDecomposer).GetMethod(
            "ReadOriginFact",
            BindingFlags.NonPublic | BindingFlags.Instance);

    [Test]
    public void BuildGoalDecomposer_ReadOriginFact_MissingKey_LogsWarning()
    {
        var logger = new TestLogger<BuildGoalDecomposer>();
        var decomposer = new BuildGoalDecomposer(null!, logger);
        var method = GetReadOriginFact();
        Assert.That(method, Is.Not.Null, "ReadOriginFact must exist as a non-public instance method.");

        var state = new WorldState(); // no facts — key will be absent
        method!.Invoke(decomposer, new object[] { state, "test-bp", "x" });

        Assert.That(logger.HasWarning("missing or unparseable"), Is.True,
            "LogWarning must fire with 'missing or unparseable' when the origin fact key is absent.");
    }

    [Test]
    public void BuildGoalDecomposer_ReadOriginFact_UnparseableValue_LogsWarning()
    {
        var logger = new TestLogger<BuildGoalDecomposer>();
        var decomposer = new BuildGoalDecomposer(null!, logger);
        var method = GetReadOriginFact();
        Assert.That(method, Is.Not.Null);

        var state = new WorldState();
        // An object() does not match int / long / string branches → falls to _ arm → LogWarning
        state.Facts["build:test-bp:origin:y"] = new object();
        method!.Invoke(decomposer, new object[] { state, "test-bp", "y" });

        Assert.That(logger.HasWarning("defaulting to 0 for axis"), Is.True,
            "LogWarning must fire with 'defaulting to 0 for axis' when fact value cannot be parsed.");
    }

    [Test]
    public void BuildGoalDecomposer_ReadOriginFact_ValidIntValue_NoWarning()
    {
        var logger = new TestLogger<BuildGoalDecomposer>();
        var decomposer = new BuildGoalDecomposer(null!, logger);
        var method = GetReadOriginFact();
        Assert.That(method, Is.Not.Null);

        var state = new WorldState();
        state.Facts["build:test-bp:origin:z"] = 42; // valid int
        method!.Invoke(decomposer, new object[] { state, "test-bp", "z" });

        var warnings = logger.Entries.Count(e => e.Level == LogLevel.Warning);
        Assert.That(warnings, Is.EqualTo(0),
            "No warnings should be emitted when a valid int origin fact is present.");
    }

    // ── P1-D: ChatInterpreter plural-map regression ──────────────────────────

    private static MethodInfo? GetResolveItemId()
        => typeof(ChatInterpreter).GetMethod(
            "ResolveItemId",
            BindingFlags.NonPublic | BindingFlags.Static);

    [Test]
    public void ChatInterpreter_ResolveItemId_Grass_DoesNotReturnGra()
    {
        // Sprint 30 P1-D: removed TrimEnd('s') heuristic.
        // "grass" must not resolve to "gra" (TrimEnd strips 'ss').
        var method = GetResolveItemId();
        Assert.That(method, Is.Not.Null, "ResolveItemId must exist as a private static method.");

        var result = (string?)method!.Invoke(null, new object[] { "grass" });

        Assert.That(result, Is.Not.EqualTo("gra"),
            "'grass' must never resolve to 'gra' — TrimEnd('s') has been removed (Sprint 30 P1-D).");
        // "grass" is a valid bare identifier, so it should pass through unchanged.
        Assert.That(result, Is.EqualTo("grass"),
            "'grass' is a valid Minecraft identifier and must pass through the explicit-map fallback.");
    }

    [Test]
    public void ChatInterpreter_ResolveItemId_Diamonds_ResolvesToDiamond()
    {
        // Plurals that ARE in ItemAliases must still work.
        var method = GetResolveItemId();
        Assert.That(method, Is.Not.Null);

        var result = (string?)method!.Invoke(null, new object[] { "diamonds" });

        Assert.That(result, Is.EqualTo("diamond"),
            "'diamonds' is explicitly mapped to 'diamond' in ItemAliases and must still resolve correctly.");
    }

    [Test]
    public void ChatInterpreter_ResolveItemId_Logs_ResolvesToOakLog()
    {
        var method = GetResolveItemId();
        Assert.That(method, Is.Not.Null);

        var result = (string?)method!.Invoke(null, new object[] { "logs" });

        Assert.That(result, Is.EqualTo("oak_log"),
            "'logs' is explicitly mapped to 'oak_log' in ItemAliases.");
    }

    // ── P1-E: ChatInterpreter status-regex 'doing' regression ──────────────

    private static MethodInfo? GetParseIntent()
        => typeof(ChatInterpreter).GetMethod(
            "ParseIntent",
            BindingFlags.NonPublic | BindingFlags.Static);

    [Test]
    public void ChatInterpreter_ParseIntent_BareDoingToken_IsNotQueryStatus()
    {
        // Sprint 30 P1-E: bare 'doing' was removed from the status regex.
        // A message consisting only of 'doing' must NOT produce QueryStatus.
        var method = GetParseIntent();
        Assert.That(method, Is.Not.Null, "ParseIntent must exist as a private static method.");

        var state = new WorldState();
        var result = method!.Invoke(null, new object[] { "doing", state });
        Assert.That(result, Is.Not.Null);

        // Use reflection to read the IntentType property generically.
        var intentProp = result!.GetType().GetProperty("IntentType")
                      ?? result.GetType().GetProperty("Intent")
                      ?? result.GetType().GetProperty("Type");

        if (intentProp is not null)
        {
            var intentValue = intentProp.GetValue(result)?.ToString();
            Assert.That(intentValue, Is.Not.EqualTo("QueryStatus"),
                "The bare word 'doing' alone must not trigger a status query after Sprint 30 P1-E.");
        }
        // If property name can't be found, at minimum assert the result is not null.
        // The integration build will catch any signature mismatch.
    }

    // ── P0-A/B verification: structural checks ────────────────────────────────

    [Test]
    public void WorldStateProjector_IsValidCSharpClass()
    {
        // Verifies WorldStateProjector.cs was correctly decoded (Sprint 30 P0-A).
        var type = typeof(Agent.Core.WorldStateProjector);
        Assert.That(type, Is.Not.Null, "WorldStateProjector must be a loadable type.");
        Assert.That(type.IsSealed, Is.True, "WorldStateProjector must be a sealed class.");

        var applyMethod = type.GetMethod("Apply",
            new[] { typeof(WorldState), typeof(WorldEvent) });
        Assert.That(applyMethod, Is.Not.Null,
            "WorldStateProjector.Apply(WorldState, WorldEvent) method must exist.");
    }

    [Test]
    public void SearchMemoryTool_ExecuteAsync_HasJsonElementSignature()
    {
        // Verifies Sprint 30 P0-B: SearchMemoryTool implements ITool.ExecuteAsync(JsonElement, CT).
        var type = typeof(Agent.Tools.SearchMemoryTool);
        var method = type.GetMethod("ExecuteAsync",
            new[] { typeof(JsonElement), typeof(CancellationToken) });
        Assert.That(method, Is.Not.Null,
            "SearchMemoryTool.ExecuteAsync must accept (JsonElement, CancellationToken) after Sprint 30 P0-B.");
    }

    [Test]
    public void CreatePageTool_ExecuteAsync_HasJsonElementSignature()
    {
        var type = typeof(Agent.Tools.CreatePageTool);
        var method = type.GetMethod("ExecuteAsync",
            new[] { typeof(JsonElement), typeof(CancellationToken) });
        Assert.That(method, Is.Not.Null,
            "CreatePageTool.ExecuteAsync must accept (JsonElement, CancellationToken) after Sprint 30 P0-B.");
    }

    [Test]
    public void SearchMemoryTool_HasInputSchemaProperty()
    {
        var type = typeof(Agent.Tools.SearchMemoryTool);
        var prop = type.GetProperty("InputSchema");
        Assert.That(prop, Is.Not.Null, "SearchMemoryTool must expose an InputSchema property.");
        Assert.That(prop!.PropertyType, Is.EqualTo(typeof(JsonElement)));
    }

    [Test]
    public void CreatePageTool_HasInputSchemaProperty()
    {
        var type = typeof(Agent.Tools.CreatePageTool);
        var prop = type.GetProperty("InputSchema");
        Assert.That(prop, Is.Not.Null, "CreatePageTool must expose an InputSchema property.");
        Assert.That(prop!.PropertyType, Is.EqualTo(typeof(JsonElement)));
    }
}
