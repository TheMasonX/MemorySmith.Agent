namespace MemorySmith.Agent.Tests;

using Agent.Construction;
using Agent.Core;
using Agent.Planning;
using Agent.Planning.Goals;

/// <summary>
/// Tests for <see cref="GoalFactory"/> Phase 4b additions:
/// - D1: RegisteredGoals exposes Build:{blueprintId} and GatherItem:{itemId} prefixes.
/// - D3: CreateAsync returns null (not an exception) when the repository is null.
/// - Build:{blueprintId} prefix creates a <see cref="BuildGoal"/> from a repository.
/// </summary>
[TestFixture]
[Description("GoalFactory Build: D1 registered goals, D3 null-repository warning, Build prefix")]
public sealed class GoalFactoryBuildTests
{
    // ── D1: RegisteredGoals ───────────────────────────────────────────────────

    [Test]
    public void RegisteredGoals_ContainsBuildPrefixPattern()
    {
        var factory = new GoalFactory();
        Assert.That(factory.RegisteredGoals,
            Has.Some.Matches<string>(s =>
                s.StartsWith("Build:", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public void RegisteredGoals_ContainsGatherItemPrefixPattern()
    {
        var factory = new GoalFactory();
        Assert.That(factory.RegisteredGoals,
            Has.Some.Matches<string>(s =>
                s.StartsWith("GatherItem:", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public void RegisteredGoals_ContainsGatherWood()
    {
        var factory = new GoalFactory();
        Assert.That(factory.RegisteredGoals,
            Has.Member("GatherWood"));
    }

    [Test]
    public void RegisteredGoals_ContainsSurviveNight()
    {
        var factory = new GoalFactory();
        Assert.That(factory.RegisteredGoals,
            Has.Member("SurviveNight"));
    }

    [Test]
    public void RegisteredGoals_HasAtLeastFourEntries()
    {
        // GatherWood, SurviveNight, GatherItem:{itemId}, Build:{blueprintId}
        var factory = new GoalFactory();
        Assert.That(factory.RegisteredGoals, Has.Count.GreaterThanOrEqualTo(4));
    }

    // ── D3: Null repository warning (no exception, just null return) ──────────

    [Test]
    public async Task CreateAsync_BuildPrefix_NoBlueprintRepository_ReturnsNull()
    {
        var factory = new GoalFactory(); // no repository
        var goal    = await factory.CreateAsync("Build:small-house");
        Assert.That(goal, Is.Null);
    }

    [Test]
    public async Task CreateAsync_GatherItemPrefix_NoItemRegistry_ReturnsNull()
    {
        var factory = new GoalFactory(); // no registry
        var goal    = await factory.CreateAsync("GatherItem:oak_log");
        Assert.That(goal, Is.Null);
    }

    // ── Build prefix with repository ──────────────────────────────────────────

    [Test]
    public async Task CreateAsync_BuildPrefix_KnownBlueprint_ReturnsBuildGoal()
    {
        var repo = new MockBlueprintRepository();
        repo.Add("small-house", MakeSimpleBlueprintWithMarkdown("small-house", "Small House"));

        var factory = new GoalFactory(blueprintRepository: repo);
        var goal    = await factory.CreateAsync("Build:small-house");

        Assert.That(goal, Is.InstanceOf<BuildGoal>());
    }

    [Test]
    public async Task CreateAsync_BuildPrefix_KnownBlueprint_NameIsCorrect()
    {
        var repo = new MockBlueprintRepository();
        repo.Add("small-house", MakeSimpleBlueprintWithMarkdown("small-house", "Small House"));

        var factory = new GoalFactory(blueprintRepository: repo);
        var goal    = await factory.CreateAsync("Build:small-house");

        Assert.That(goal!.Name, Is.EqualTo("Build:small-house"));
    }

    [Test]
    public async Task CreateAsync_BuildPrefix_UnknownBlueprint_ReturnsNull()
    {
        var repo    = new MockBlueprintRepository(); // empty
        var factory = new GoalFactory(blueprintRepository: repo);
        var goal    = await factory.CreateAsync("Build:nonexistent");
        Assert.That(goal, Is.Null);
    }

    [Test]
    public async Task CreateAsync_BuildPrefix_ParsesBlocksFromMarkdown()
    {
        const string markdown = """
            ---
            id: floor-test
            name: Floor Test
            ---

            ## Layers

            ### Y=0
            CCC
            CCC
            """;

        var repo = new MockBlueprintRepository();
        repo.Add("floor-test", new Blueprint
        {
            Id          = "floor-test",
            Name        = "Floor Test",
            RawMarkdown = markdown,
        });

        var factory = new GoalFactory(blueprintRepository: repo);
        var goal    = (BuildGoal?)await factory.CreateAsync("Build:floor-test");

        Assert.That(goal, Is.Not.Null);
        Assert.That(goal!.Blocks, Has.Count.EqualTo(6)); // 3×2 = 6 cobblestone
    }

    // ── Case-insensitivity ────────────────────────────────────────────────────

    [Test]
    public async Task CreateAsync_BuildPrefix_CaseInsensitive()
    {
        var repo = new MockBlueprintRepository();
        repo.Add("my-house", MakeSimpleBlueprintWithMarkdown("my-house", "My House"));

        var factory = new GoalFactory(blueprintRepository: repo);

        var g1 = await factory.CreateAsync("build:my-house");
        var g2 = await factory.CreateAsync("BUILD:my-house");
        var g3 = await factory.CreateAsync("Build:my-house");

        Assert.That(g1, Is.Not.Null, "lowercase 'build:' should work");
        Assert.That(g2, Is.Not.Null, "uppercase 'BUILD:' should work");
        Assert.That(g3, Is.Not.Null, "title case 'Build:' should work");
    }

    // ── Static goals still work ────────────────────────────────────────────────

    [Test]
    public void Create_GatherWood_ReturnsSyncGoal()
    {
        var factory = new GoalFactory();
        var goal    = factory.Create("GatherWood");
        Assert.That(goal, Is.Not.Null);
    }

    [Test]
    public async Task CreateAsync_UnknownGoal_ReturnsNull()
    {
        var factory = new GoalFactory();
        var goal    = await factory.CreateAsync("CompletelyUnknownGoal");
        Assert.That(goal, Is.Null);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Blueprint MakeSimpleBlueprintWithMarkdown(string id, string name) =>
        new()
        {
            Id          = id,
            Name        = name,
            RawMarkdown = $"---\nid: {id}\nname: {name}\n---\n\n## Layers\n\n### Y=0\nCCC\n",
        };
}

/// <summary>In-memory <see cref="IBlueprintRepository"/> for testing.</summary>
internal sealed class MockBlueprintRepository : IBlueprintRepository
{
    private readonly Dictionary<string, Blueprint> _store =
        new(StringComparer.OrdinalIgnoreCase);

    public void Add(string id, Blueprint bp) => _store[id] = bp;

    public Task<Blueprint?> GetAsync(string blueprintId, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(blueprintId, out var bp) ? bp : null);

    public Task<IReadOnlyList<Blueprint>> SearchAsync(string query, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Blueprint>>([]);

    public Task<string> SaveAsync(Blueprint blueprint, CancellationToken ct = default) =>
        Task.FromResult(blueprint.Id);
}
