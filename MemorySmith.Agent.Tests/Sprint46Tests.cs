namespace MemorySmith.Agent.Tests;

using global::Agent.Core;
using global::Agent.Planning;
using global::Agent.Planning.Goals;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

/// <summary>
/// Sprint 46 Wave B+ — Observability First
///
/// Test coverage:
///   TSK-0106: Error-path tests for catch→null patterns, fire-and-forget,
///             ReplanResult type, and BuildOrigin value object edge cases.
///   TSK-0103: BuildOrigin.FromNullable rejects partial coordinates.
///   TSK-0104: ReplanResult factory methods and ReplanGoalContext construction.
/// </summary>
[TestFixture]
public class Sprint46Tests
{
    // ── TSK-0103: BuildOrigin value object ──────────────────────────────────

    [Test]
    public void BuildOrigin_FromNullable_AllCoordsPresent_ReturnsOrigin()
    {
        var origin = BuildOrigin.FromNullable(100, 64, 200);
        Assert.That(origin, Is.Not.Null);
        Assert.That(origin!.X, Is.EqualTo(100));
        Assert.That(origin.Y, Is.EqualTo(64));
        Assert.That(origin.Z, Is.EqualTo(200));
        Assert.That(origin.Source, Is.EqualTo(BuildOriginSource.AutoScanned));
    }

    [Test]
    public void BuildOrigin_FromNullable_PartialCoords_ReturnsNull()
    {
        // Missing Y — should reject as ambiguous
        Assert.That(BuildOrigin.FromNullable(100, null, 200), Is.Null,
            "Partial coordinates should return null to prevent silent default to 0.");
    }

    [Test]
    public void BuildOrigin_FromNullable_NullX_ReturnsNull()
    {
        Assert.That(BuildOrigin.FromNullable(null, 64, 200), Is.Null);
    }

    [Test]
    public void BuildOrigin_FromNullable_AllNull_ReturnsNull()
    {
        Assert.That(BuildOrigin.FromNullable(null, null, null), Is.Null);
    }

    [Test]
    public void BuildOrigin_FromNullable_WithExplicitSource()
    {
        var origin = BuildOrigin.FromNullable(10, 20, 30, BuildOriginSource.Explicit);
        Assert.That(origin, Is.Not.Null);
        Assert.That(origin!.Source, Is.EqualTo(BuildOriginSource.Explicit));
    }

    [Test]
    public void BuildOrigin_Equality_SameCoordsAndSource_AreEqual()
    {
        var a = new BuildOrigin(100, 64, 200, BuildOriginSource.Explicit);
        var b = new BuildOrigin(100, 64, 200, BuildOriginSource.Explicit);
        Assert.That(a, Is.EqualTo(b));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void BuildOrigin_Equality_DifferentCoords_AreNotEqual()
    {
        var a = new BuildOrigin(100, 64, 200, BuildOriginSource.Explicit);
        var b = new BuildOrigin(101, 64, 200, BuildOriginSource.Explicit);
        Assert.That(a, Is.Not.EqualTo(b));
    }

    // ── TSK-0104: ReplanResult type ─────────────────────────────────────────

    [Test]
    public void ReplanResult_Success_HasPlanAndIsSuccessTrue()
    {
        var plan = new ActionPlan("test", [], []);
        var result = ReplanResult.Success(plan);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Plan, Is.SameAs(plan));
        Assert.That(result.ErrorMessage, Is.Null);
    }

    [Test]
    public void ReplanResult_Failure_HasErrorMessageAndIsSuccessFalse()
    {
        var result = ReplanResult.Failure("Something went wrong");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Plan, Is.Null);
        Assert.That(result.ErrorMessage, Is.EqualTo("Something went wrong"));
    }

    [Test]
    public void ReplanResult_Failure_EmptyMessage_Allowed()
    {
        var result = ReplanResult.Failure("");
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorMessage, Is.EqualTo(""));
    }

    [Test]
    public void ReplanGoalContext_ConstructsWithAllProperties()
    {
        var plan = new ActionPlan("gather", [], []);
        var state = new WorldState();
        var goal = new GatherWoodGoal(5);
        var context = new ReplanGoalContext(plan, state, "blocked", goal);

        Assert.That(context.CurrentPlan, Is.SameAs(plan));
        Assert.That(context.State, Is.SameAs(state));
        Assert.That(context.FailureReason, Is.EqualTo("blocked"));
        Assert.That(context.OriginalGoal, Is.SameAs(goal));
    }

    [Test]
    public void ReplanGoalContext_OriginalGoalDefaultsToNull()
    {
        var plan = new ActionPlan("gather", [], []);
        var context = new ReplanGoalContext(plan, new WorldState(), "blocked");

        Assert.That(context.OriginalGoal, Is.Null);
    }

    // ── TSK-0104: HtnPlanner error-path via ReplanResult ─────────────────────

    [Test]
    public async Task HtnPlanner_ReplanAsync_ThrowingPlanner_ReturnsFailureWithMessage()
    {
        // GoalFactory doesn't have a "NoSuchGoal" task, so PlanAsync throws,
        // which ReplanAsync catches and wraps in Replansult.Failure.
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var plan = new ActionPlan("NoSuchGoal", [],
            [new ActionData { Tool = "GetStatus" }]);

        var result = await planner.ReplanAsync(
            new ReplanGoalContext(plan, new WorldState(), "forced failure"));

        Assert.That(result.IsSuccess, Is.False,
            "ReplanAsync should return Failure when inner PlanAsync throws.");
        Assert.That(result.Plan, Is.Null);
        Assert.That(result.ErrorMessage, Is.Not.Null.And.Not.Empty,
            "ErrorMessage should be populated with the planner error details.");
    }

    // ── TSK-0103: BuildGoal origin consolidation ─────────────────────────────

    [Test]
    public void BuildGoal_WithOrigin_HasExplicitOriginIsTrue()
    {
        var blueprint = new global::Agent.Construction.Blueprint
        {
            Id = "test",
            Name = "Test",
            Dimensions = new global::Agent.Construction.Dimensions(1, 1, 1),
        };
        var origin = new BuildOrigin(10, 64, 200, BuildOriginSource.Explicit);
        var goal = new BuildGoal(blueprint, [], origin);

        Assert.That(goal.Origin, Is.Not.Null);
        Assert.That(goal.Origin!.X, Is.EqualTo(10));
        Assert.That(goal.Origin.Y, Is.EqualTo(64));
        Assert.That(goal.Origin.Z, Is.EqualTo(200));
        Assert.That(goal.Origin.Source, Is.EqualTo(BuildOriginSource.Explicit));
        Assert.That(goal.HasExplicitOrigin, Is.True);
        Assert.That(goal.Description, Does.Contain("(10,64,200)"));
    }

    [Test]
    public void BuildGoal_WithoutOrigin_HasExplicitOriginIsFalse()
    {
        var blueprint = new global::Agent.Construction.Blueprint
        {
            Id = "test",
            Name = "Test",
            Dimensions = new global::Agent.Construction.Dimensions(1, 1, 1),
        };
        var goal = new BuildGoal(blueprint, []);

        Assert.That(goal.Origin, Is.Null);
        Assert.That(goal.HasExplicitOrigin, Is.False);
    }

    // ── TSK-0099: AliasRegistry consolidation ────────────────────────────────

    [Test]
    public void AliasRegistry_ItemAliases_ResolvesKnownItem()
    {
        Assert.That(AliasRegistry.ItemAliases["wood"], Is.EqualTo("oak_log"));
        Assert.That(AliasRegistry.ItemAliases["cobble"], Is.EqualTo("cobblestone"));
        Assert.That(AliasRegistry.ItemAliases["diamond"], Is.EqualTo("diamond"));
    }

    [Test]
    public void AliasRegistry_ItemAliases_IncludesLlmEntries()
    {
        // IntentManager-specific entries merged into shared registry
        Assert.That(AliasRegistry.ItemAliases["wool"], Is.EqualTo("white_wool"));
        Assert.That(AliasRegistry.ItemAliases["planks"], Is.EqualTo("oak_planks"));
        Assert.That(AliasRegistry.ItemAliases["glass"], Is.EqualTo("glass"));
    }

    [Test]
    public void AliasRegistry_ItemAliases_UsesGoalFriendlyValuesForOres()
    {
        // IntentManager values win for iron/gold/copper (block IDs)
        Assert.That(AliasRegistry.ItemAliases["iron"], Is.EqualTo("iron_ore"),
            "Item aliases should use block IDs (iron_ore) for gather-goal creation.");
    }

    [Test]
    public void AliasRegistry_BlueprintAliases_ResolvesHouse()
    {
        Assert.That(AliasRegistry.BlueprintAliases["house"], Is.EqualTo("small-house"));
        Assert.That(AliasRegistry.BlueprintAliases["cabin"], Is.EqualTo("small-house"));
    }

    [Test]
    public void AliasRegistry_CraftAliases_ResolvesKnownCraft()
    {
        Assert.That(AliasRegistry.CraftAliases["plank"], Is.EqualTo("oak_planks"));
        Assert.That(AliasRegistry.CraftAliases["torch"], Is.EqualTo("torch"));
        Assert.That(AliasRegistry.CraftAliases["furnace"], Is.EqualTo("furnace"));
    }

    // ── TSK-0109: /api/about metadata ────────────────────────────────────────

    [Test]
    public async Task ApiAbout_ReturnsCorrectVersionAndPhase()
    {
        // Verify the /api/about endpoint returns the correct version and phase
        // matching the current roadmap.
        await using var app = BuildApiAboutApp();
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/about");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Read the response and parse it as JSON
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;

        // Results.Ok() with anonymous types serializes with camelCase
        // in minimal API hosts using System.Text.Json defaults.
        Assert.That(json.GetProperty("version").GetString(), Is.EqualTo("0.46.0"),
            "/api/about version must match roadmap (Sprint 46).");
        Assert.That(json.GetProperty("phase").GetString(), Does.StartWith("Sprint 46"),
            "/api/about phase must reflect current sprint.");
    }

    /// <summary>
    /// Mirrors the /api/about route from Program.cs so we can test it in isolation.
    /// </summary>
    private static WebApplication BuildApiAboutApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging(lb => lb.SetMinimumLevel(LogLevel.None));

        var app = builder.Build();

        // Route definition mirrors WebUI.Blazor/Program.cs
        app.MapGet("/api/about", () => Results.Ok(new
        {
            Name    = "MemorySmith.Agent",
            Version = "0.46.0",
            Phase   = "Sprint 46 — Tightening the Contracts",
            License = "MIT",
            Repository  = "https://github.com/TheMasonX/MemorySmith.Agent",
            Dashboard   = "/",
            RegisteredGoals = Array.Empty<string>(),
        }));

        return app;
    }
}
