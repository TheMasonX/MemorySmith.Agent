using Agent.Construction;
using Agent.Core;
using Agent.Planning;
using Agent.Planning.Goals;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Sprint 11 unit tests covering three council acceptance criteria:
///
/// 1. TryGetIntFact coercion — verifies that DecomposeBuild B2 checkpoint reads correctly
///    from int, long, double, and string-typed WorldState fact values.
/// 2. GroupBy.Sum with duplicate materials — verifies the D3 fix does not throw when
///    a blueprint has two MaterialEntry rows for the same block.
/// 3. B1-v2 requireOrigin flag — verifies that DecomposeBuild returns a single
///    FindFlatArea action when requireOrigin=true and no origin is resolvable, and
///    proceeds normally when an origin is available.
/// </summary>
[TestFixture]
[Description("Sprint 11: TryGetIntFact coercion, GroupBy.Sum duplicates, requireOrigin flag")]
public sealed class HtnTaskLibraryExtraTests
{
    // ── Shared fixtures ───────────────────────────────────────────────────────

    private HtnTaskLibrary _library = null!;

    [SetUp]
    public void SetUp() => _library = new HtnTaskLibrary();

    /// <summary>
    /// Three 1x1x1 blocks at distinct positions. Blueprint.Name = "test"
    /// so the checkpoint key = BuildFactKeys.BuildProgressIndex("test").
    /// </summary>
    private static readonly IReadOnlyList<PlacementBlock> ThreeBlocks =
    [
        new(0, 0, 0, "cobblestone"),
        new(1, 0, 0, "cobblestone"),
        new(2, 0, 0, "cobblestone"),
    ];

    private static Blueprint MakeBlueprint(
        string id = "test",
        MaterialEntry[]? materials = null) => new()
    {
        Id        = id,
        Name      = id,
        Materials = materials ?? [new MaterialEntry("cobblestone", 3)],
    };

    // ── Per-block status filtering (TSK-0125) ──────────────────────────────────
    //
    // TSK-0125 replaced linear checkpoint (BuildProgressIndex) with per-block
    // status facts (BlockStatus). Setting block 0 and 1 to "placed" causes
    // DecomposeBuild to skip them, leaving only block 2 as a PlaceBlock action.

    [Test]
    [Description("Per-block status: blocks 0 and 1 marked placed → only block 2 emitted")]
    public void DecomposeBuild_Checkpoint_IntFact_SkipsCorrectBlocks()
    {
        var state = new WorldState()
            .With(b => b.SetFact(BuildFactKeys.BlockStatus("test", 0), BuildFactKeys.BlockStatusPlaced, FactSource.Observed))
            .With(b => b.SetFact(BuildFactKeys.BlockStatus("test", 1), BuildFactKeys.BlockStatusPlaced, FactSource.Observed));
        var actions = _library.DecomposeBuild(MakeBlueprint(), ThreeBlocks, new BuildOrigin(0, 64, 0, BuildOriginSource.AutoScanned), state);
        Assert.That(CountTool(actions, "place"), Is.EqualTo(1),
            "Blocks 0 and 1 marked placed; only block 2 remains.");
    }

    [Test]
    [Description("Per-block status works regardless of fact value type")]
    public void DecomposeBuild_Checkpoint_LongFact_SkipsCorrectBlocks()
    {
        var state = new WorldState()
            .With(b => b.SetFact(BuildFactKeys.BlockStatus("test", 0), BuildFactKeys.BlockStatusPlaced, FactSource.Observed))
            .With(b => b.SetFact(BuildFactKeys.BlockStatus("test", 1), BuildFactKeys.BlockStatusPlaced, FactSource.Observed));
        var actions = _library.DecomposeBuild(MakeBlueprint(), ThreeBlocks, new BuildOrigin(0, 64, 0, BuildOriginSource.AutoScanned), state);
        Assert.That(CountTool(actions, "place"), Is.EqualTo(1));
    }

    [Test]
    [Description("Per-block status skips blocks with placed status")]
    public void DecomposeBuild_Checkpoint_DoubleFact_SkipsCorrectBlocks()
    {
        var state = new WorldState()
            .With(b => b.SetFact(BuildFactKeys.BlockStatus("test", 0), BuildFactKeys.BlockStatusPlaced, FactSource.Observed))
            .With(b => b.SetFact(BuildFactKeys.BlockStatus("test", 1), BuildFactKeys.BlockStatusPlaced, FactSource.Observed));
        var actions = _library.DecomposeBuild(MakeBlueprint(), ThreeBlocks, new BuildOrigin(0, 64, 0, BuildOriginSource.AutoScanned), state);
        Assert.That(CountTool(actions, "place"), Is.EqualTo(1));
    }

    [Test]
    [Description("Per-block status: string-typed facts from deserialization still work")]
    public void DecomposeBuild_Checkpoint_StringFact_SkipsCorrectBlocks()
    {
        var state = new WorldState()
            .With(b => b.SetFact(BuildFactKeys.BlockStatus("test", 0), BuildFactKeys.BlockStatusPlaced, FactSource.Observed))
            .With(b => b.SetFact(BuildFactKeys.BlockStatus("test", 1), BuildFactKeys.BlockStatusPlaced, FactSource.Observed));
        var actions = _library.DecomposeBuild(MakeBlueprint(), ThreeBlocks, new BuildOrigin(0, 64, 0, BuildOriginSource.AutoScanned), state);
        Assert.That(CountTool(actions, "place"), Is.EqualTo(1));
    }

    // ── GroupBy.Sum with duplicate materials — D3 fix ─────────────────────────

    [Test]
    [Description("DecomposeBuild does not throw when a blueprint has two MaterialEntry rows for the same block (D3 GroupBy.Sum fix)")]
    public void DecomposeBuild_DuplicateMaterials_DoesNotThrow()
    {
        // Two entries for cobblestone — before D3 fix this threw ArgumentException.
        var blueprint = MakeBlueprint(materials:
        [
            new MaterialEntry("cobblestone", 2),
            new MaterialEntry("cobblestone", 1),
        ]);

        Assert.DoesNotThrow(() =>
            _library.DecomposeBuild(blueprint, ThreeBlocks, new BuildOrigin(0, 64, 0, BuildOriginSource.AutoScanned), new WorldState()),
            "Duplicate blueprint materials should be merged via GroupBy.Sum, not throw.");
    }

    [Test]
    [Description("DecomposeBuild merges duplicate materials — MineBlock quantity reflects the sum")]
    public void DecomposeBuild_DuplicateMaterials_SumsQuantity()
    {
        // 2 + 1 = 3 cobblestone needed, inventory empty.
        var blueprint = MakeBlueprint(materials:
        [
            new MaterialEntry("cobblestone", 2),
            new MaterialEntry("cobblestone", 1),
        ]);
        var actions = _library.DecomposeBuild(blueprint, ThreeBlocks, new BuildOrigin(0, 64, 0, BuildOriginSource.AutoScanned), new WorldState());
        var mineAction = actions.FirstOrDefault(a =>
            a.Tool == "MineBlock" &&
            a.Arguments.TryGetValue("block", out var b) &&
            string.Equals(b?.ToString(), "cobblestone", StringComparison.OrdinalIgnoreCase));

        Assert.That(mineAction, Is.Not.Null, "MineBlock for cobblestone should be emitted.");
        Assert.That(mineAction!.Arguments.TryGetValue("count", out var count), Is.True);
        Assert.That(Convert.ToInt32(count), Is.EqualTo(3),
            "MineBlock count should reflect the GroupBy.Sum (2+1=3).");
    }

    // ── B1-v2 requireOrigin flag ──────────────────────────────────────────────

    [Test]
    [Description("B1-v2: requireOrigin=true with no origin in state returns a single FindFlatArea action")]
    public void DecomposeBuild_RequireOrigin_NoOriginFact_ReturnsFlatAreaOnly()
    {
        var actions = _library.DecomposeBuild(
            MakeBlueprint(), ThreeBlocks,
            new BuildOrigin(0, 0, 0, BuildOriginSource.AutoScanned),
            new WorldState(),
            requireOrigin: true);

        Assert.That(actions, Has.Count.EqualTo(1),
            "When requireOrigin is set and no origin is resolvable, exactly one action is returned.");
        Assert.That(actions[0].Tool, Is.EqualTo("FindFlatArea"),
            "The single action should be FindFlatArea so the scanner can locate a valid site.");
    }

    [Test]
    [Description("B1-v2: requireOrigin=true with auto-origin facts set proceeds to full build plan")]
    public void DecomposeBuild_RequireOrigin_AutoOriginSet_ProceedsToBuild()
    {
        var state = new WorldState().With(b =>
        {
            b.SetFact(BuildFactKeys.AutoOriginX, 10.ToString(), FactSource.Observed);
            b.SetFact(BuildFactKeys.AutoOriginY, 64.ToString(), FactSource.Observed);
            b.SetFact(BuildFactKeys.AutoOriginZ, 10.ToString(), FactSource.Observed);
        });

        var actions = _library.DecomposeBuild(
            MakeBlueprint(), ThreeBlocks,
            new BuildOrigin(0, 0, 0, BuildOriginSource.AutoScanned),
            state,
            requireOrigin: true);

        Assert.That(CountTool(actions, "place"), Is.EqualTo(3),
            "When auto-origin is available, requireOrigin=true proceeds to the full 3-block build.");
    }

    [Test]
    [Description("B1-v2: requireOrigin=false (default) with no origin proceeds using (0,0,0) — backward compatible")]
    public void DecomposeBuild_RequireOriginFalse_NoOrigin_ProceedsWithZeroOrigin()
    {
        // requireOrigin defaults to false — existing callers (HtnPlanner) are not affected.
        var actions = _library.DecomposeBuild(
            MakeBlueprint(), ThreeBlocks,
            new BuildOrigin(0, 0, 0, BuildOriginSource.AutoScanned),
            new WorldState());

        Assert.That(CountTool(actions, "place"), Is.EqualTo(3),
            "With requireOrigin=false (default), the plan proceeds even without a stored origin.");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static int CountTool(IReadOnlyList<ActionData> actions, string toolName) =>
        actions.Count(a => string.Equals(a.Tool, toolName, StringComparison.OrdinalIgnoreCase));
}
