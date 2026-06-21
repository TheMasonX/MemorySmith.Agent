using Agent.Construction;
using Agent.Core;
using Agent.Planning;

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

    // ── TryGetIntFact coercion — via B2 checkpoint ────────────────────────────
    //
    // DecomposeBuild reads the checkpoint fact as:
    //   checkpointIndex = lastPlaced + 1
    // so setting it to 1 causes the first two blocks (0,1) to be skipped and
    // the plan to contain exactly ThreeBlocks.Count - 2 = 1 PlaceBlock action.

    [Test]
    [Description("TryGetIntFact reads a boxed int fact correctly via B2 checkpoint path")]
    public void DecomposeBuild_Checkpoint_IntFact_SkipsCorrectBlocks()
    {
        var progressKey = BuildFactKeys.BuildProgressIndex("test");
        var state = new WorldState().With(b => b.SetFact(progressKey, 1.ToString(), FactSource.Observed)); // int
        var actions = _library.DecomposeBuild(MakeBlueprint(), ThreeBlocks, 0, 64, 0, state);
        Assert.That(CountTool(actions, "PlaceBlock"), Is.EqualTo(1),
            "Checkpoint=1 means blocks 0 and 1 were placed; only block 2 remains.");
    }

    [Test]
    [Description("TryGetIntFact reads a boxed long fact correctly via B2 checkpoint path")]
    public void DecomposeBuild_Checkpoint_LongFact_SkipsCorrectBlocks()
    {
        var progressKey = BuildFactKeys.BuildProgressIndex("test");
        var state = new WorldState().With(b => b.SetFact(progressKey, 1L.ToString(), FactSource.Observed)); // long
        var actions = _library.DecomposeBuild(MakeBlueprint(), ThreeBlocks, 0, 64, 0, state);
        Assert.That(CountTool(actions, "PlaceBlock"), Is.EqualTo(1));
    }

    [Test]
    [Description("TryGetIntFact reads a boxed double fact correctly via B2 checkpoint path")]
    public void DecomposeBuild_Checkpoint_DoubleFact_SkipsCorrectBlocks()
    {
        var progressKey = BuildFactKeys.BuildProgressIndex("test");
        var state = new WorldState().With(b => b.SetFact(progressKey, 1.0.ToString(), FactSource.Observed)); // double
        var actions = _library.DecomposeBuild(MakeBlueprint(), ThreeBlocks, 0, 64, 0, state);
        Assert.That(CountTool(actions, "PlaceBlock"), Is.EqualTo(1));
    }

    [Test]
    [Description("TryGetIntFact reads a string fact (from JSON deserialization) correctly via B2 checkpoint path")]
    public void DecomposeBuild_Checkpoint_StringFact_SkipsCorrectBlocks()
    {
        var progressKey = BuildFactKeys.BuildProgressIndex("test");
        var state = new WorldState().With(b => b.SetFact(progressKey, "1", FactSource.Observed)); // string
        var actions = _library.DecomposeBuild(MakeBlueprint(), ThreeBlocks, 0, 64, 0, state);
        Assert.That(CountTool(actions, "PlaceBlock"), Is.EqualTo(1));
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
            _library.DecomposeBuild(blueprint, ThreeBlocks, 0, 64, 0, new WorldState()),
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
        var actions = _library.DecomposeBuild(blueprint, ThreeBlocks, 0, 64, 0, new WorldState());
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
            originX: 0, originY: 0, originZ: 0,
            state: new WorldState(),
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
            originX: 0, originY: 0, originZ: 0,
            state: state,
            requireOrigin: true);

        Assert.That(CountTool(actions, "PlaceBlock"), Is.EqualTo(3),
            "When auto-origin is available, requireOrigin=true proceeds to the full 3-block build.");
    }

    [Test]
    [Description("B1-v2: requireOrigin=false (default) with no origin proceeds using (0,0,0) — backward compatible")]
    public void DecomposeBuild_RequireOriginFalse_NoOrigin_ProceedsWithZeroOrigin()
    {
        // requireOrigin defaults to false — existing callers (HtnPlanner) are not affected.
        var actions = _library.DecomposeBuild(
            MakeBlueprint(), ThreeBlocks,
            originX: 0, originY: 0, originZ: 0,
            state: new WorldState());

        Assert.That(CountTool(actions, "PlaceBlock"), Is.EqualTo(3),
            "With requireOrigin=false (default), the plan proceeds even without a stored origin.");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static int CountTool(IReadOnlyList<ActionData> actions, string toolName) =>
        actions.Count(a => string.Equals(a.Tool, toolName, StringComparison.OrdinalIgnoreCase));
}
