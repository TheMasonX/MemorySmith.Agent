namespace MemorySmith.Agent.Tests;

using Agent.Construction;
using Agent.Core;
using Agent.Planning.Goals;

/// <summary>
/// Unit tests for <see cref="BuildGoal"/>: name format, phases, IsComplete, HasFailed,
/// and property retention.
/// </summary>
[TestFixture]
[Description("BuildGoal: name format, phases, IsComplete, HasFailed, property retention")]
public sealed class BuildGoalTests
{
    private static Blueprint MakeBlueprint(string id = "test-house", string name = "Test House") =>
        new() { Id = id, Name = name };

    private static IReadOnlyList<PlacementBlock> NoBlocks => [];

    // ── Name ─────────────────────────────────────────────────────────────────

    [Test]
    public void Name_Format_IsBuildColonId()
    {
        var goal = new BuildGoal(MakeBlueprint("small-house"), NoBlocks);
        Assert.That(goal.Name, Is.EqualTo("Build:small-house"));
    }

    [Test]
    public void Name_UsesRawIdWithoutNormalization()
    {
        var goal = new BuildGoal(MakeBlueprint("My_House"), NoBlocks);
        Assert.That(goal.Name, Is.EqualTo("Build:My_House"));
    }

    // ── Description ──────────────────────────────────────────────────────────

    [Test]
    public void Description_ContainsBlueprintName()
    {
        var goal = new BuildGoal(MakeBlueprint("x", "Fancy Castle"), NoBlocks);
        Assert.That(goal.Description, Does.Contain("Fancy Castle"));
    }

    [Test]
    public void Description_ContainsBlockCount()
    {
        var blocks = new[]
        {
            new PlacementBlock(0, 0, 0, "cobblestone"),
            new PlacementBlock(1, 0, 0, "cobblestone"),
        };
        var goal = new BuildGoal(MakeBlueprint(), blocks);
        Assert.That(goal.Description, Does.Contain("2"));
    }

    // ── Phases ────────────────────────────────────────────────────────────────

    [Test]
    public void Phases_ContainsExpectedThreePhases()
    {
        var goal = new BuildGoal(MakeBlueprint(), NoBlocks);
        Assert.That(goal.Phases, Is.EquivalentTo(
            new[] { "GatherMaterials", "Build", "Verify" }));
    }

    // ── IsComplete ────────────────────────────────────────────────────────────

    [Test]
    public void IsComplete_ReturnsFalse_OnEmptyState()
    {
        var goal  = new BuildGoal(MakeBlueprint(), NoBlocks);
        var state = new WorldState();
        Assert.That(goal.IsComplete(state), Is.False);
    }

    [Test]
    public void IsComplete_ReturnsTrue_WhenCompleteFact_IsTrue()
    {
        var goal  = new BuildGoal(MakeBlueprint("test-house"), NoBlocks);
        var state = new WorldState().With(b =>
            b.SetFact("goal:Build:test-house:complete", true));
        Assert.That(goal.IsComplete(state), Is.True);
    }

    [Test]
    public void IsComplete_ReturnsFalse_WhenCompleteFact_IsFalse()
    {
        var goal  = new BuildGoal(MakeBlueprint("test-house"), NoBlocks);
        var state = new WorldState().With(b =>
            b.SetFact("goal:Build:test-house:complete", false));
        Assert.That(goal.IsComplete(state), Is.False);
    }

    [Test]
    public void IsComplete_UsesCorrectFactKey_ForDifferentIds()
    {
        var goalA = new BuildGoal(MakeBlueprint("house-a"), NoBlocks);
        var goalB = new BuildGoal(MakeBlueprint("house-b"), NoBlocks);

        var state = new WorldState().With(b =>
            b.SetFact("goal:Build:house-a:complete", true));

        Assert.That(goalA.IsComplete(state), Is.True,  "house-a should be complete");
        Assert.That(goalB.IsComplete(state), Is.False, "house-b should not be complete");
    }

    // ── HasFailed ─────────────────────────────────────────────────────────────

    [Test]
    public void HasFailed_ReturnsFalse_OnEmptyState()
    {
        var goal  = new BuildGoal(MakeBlueprint(), NoBlocks);
        var state = new WorldState();
        Assert.That(goal.HasFailed(state), Is.False);
    }

    [Test]
    public void HasFailed_ReturnsTrue_WhenFailedFact_IsTrue()
    {
        var goal  = new BuildGoal(MakeBlueprint("test-house"), NoBlocks);
        var state = new WorldState().With(b =>
            b.SetFact("goal:Build:test-house:failed", true));
        Assert.That(goal.HasFailed(state), Is.True);
    }

    // ── Property retention ────────────────────────────────────────────────────

    [Test]
    public void Blueprint_IsRetained()
    {
        var bp   = MakeBlueprint("retain-test");
        var goal = new BuildGoal(bp, NoBlocks);
        Assert.That(goal.Blueprint.Id, Is.EqualTo("retain-test"));
    }

    [Test]
    public void Blocks_AreRetained()
    {
        var blocks = new[] { new PlacementBlock(1, 2, 3, "stone") };
        var goal   = new BuildGoal(MakeBlueprint(), blocks);
        Assert.That(goal.Blocks, Has.Count.EqualTo(1));
        Assert.That(goal.Blocks[0].BlockId, Is.EqualTo("stone"));
        Assert.That(goal.Blocks[0].X, Is.EqualTo(1));
        Assert.That(goal.Blocks[0].Y, Is.EqualTo(2));
        Assert.That(goal.Blocks[0].Z, Is.EqualTo(3));
    }

    [Test]
    public void EmptyBlocks_ProducesEmptyBlockList()
    {
        var goal = new BuildGoal(MakeBlueprint(), []);
        Assert.That(goal.Blocks, Is.Empty);
    }
}
