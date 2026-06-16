using Agent.Construction;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Unit tests for <see cref="BlueprintParser"/>: frontmatter extraction,
/// legend parsing, grid layer parsing, and edge cases.
/// </summary>
[TestFixture]
[Description("BlueprintParser: frontmatter, legend, grid layers, edge cases")]
public sealed class BlueprintParserTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private const string SimpleHouse = """
        ---
        id: mini-house
        name: Mini House
        tags: house, test
        dimensions: 3x2x3
        materials: cobblestone x 9, oak_planks x 8
        description: Tiny test house.
        ---

        ## Layers

        ### Y=0 (Floor)
        CCC
        CCC
        CCC

        ### Y=1 (Walls)
        PPP
        P.P
        PPP
        """;

    // ── Frontmatter ───────────────────────────────────────────────────────────

    [Test]
    public void Parse_Extracts_Id()
    {
        var (meta, _) = BlueprintParser.Parse(SimpleHouse);
        Assert.That(meta.Id, Is.EqualTo("mini-house"));
    }

    [Test]
    public void Parse_Extracts_Name()
    {
        var (meta, _) = BlueprintParser.Parse(SimpleHouse);
        Assert.That(meta.Name, Is.EqualTo("Mini House"));
    }

    [Test]
    public void Parse_Extracts_Tags()
    {
        var (meta, _) = BlueprintParser.Parse(SimpleHouse);
        Assert.That(meta.Tags, Has.Member("house").And.Member("test"));
    }

    [Test]
    public void Parse_Extracts_Dimensions()
    {
        var (meta, _) = BlueprintParser.Parse(SimpleHouse);
        Assert.That(meta.Dimensions.X, Is.EqualTo(3));
        Assert.That(meta.Dimensions.Y, Is.EqualTo(2));
        Assert.That(meta.Dimensions.Z, Is.EqualTo(3));
    }

    [Test]
    public void Parse_Extracts_Materials_Count()
    {
        var (meta, _) = BlueprintParser.Parse(SimpleHouse);
        Assert.That(meta.Materials, Has.Length.EqualTo(2));
    }

    [Test]
    public void Parse_Extracts_CobblestoneQuantity()
    {
        var (meta, _) = BlueprintParser.Parse(SimpleHouse);
        var cob = meta.Materials.FirstOrDefault(m => m.Block == "cobblestone");
        Assert.That(cob,           Is.Not.Null);
        Assert.That(cob!.Quantity, Is.EqualTo(9));
    }

    [Test]
    public void Parse_Extracts_OakPlanksQuantity()
    {
        var (meta, _) = BlueprintParser.Parse(SimpleHouse);
        var planks = meta.Materials.FirstOrDefault(m => m.Block == "oak_planks");
        Assert.That(planks,           Is.Not.Null);
        Assert.That(planks!.Quantity, Is.EqualTo(8));
    }

    // ── Grid layers ──────────────────────────────────────────────────────────

    [Test]
    public void Parse_Floor_HasNineBlocks()
    {
        var (_, blocks) = BlueprintParser.Parse(SimpleHouse);
        Assert.That(blocks.Count(b => b.Y == 0), Is.EqualTo(9));
    }

    [Test]
    public void Parse_Floor_AllCobblestone()
    {
        var (_, blocks) = BlueprintParser.Parse(SimpleHouse);
        Assert.That(blocks.Where(b => b.Y == 0),
            Has.All.Matches<PlacementBlock>(b => b.BlockId == "cobblestone"));
    }

    [Test]
    public void Parse_Walls_OmitsAirCenter()
    {
        var (_, blocks) = BlueprintParser.Parse(SimpleHouse);
        // 3×3 wall minus 1 centre air = 8 planks
        Assert.That(blocks.Count(b => b.Y == 1), Is.EqualTo(8));
    }

    [Test]
    public void Parse_Walls_AllOakPlanks()
    {
        var (_, blocks) = BlueprintParser.Parse(SimpleHouse);
        Assert.That(blocks.Where(b => b.Y == 1),
            Has.All.Matches<PlacementBlock>(b => b.BlockId == "oak_planks"));
    }

    [Test]
    public void Parse_TotalBlockCount_Is17()
    {
        var (_, blocks) = BlueprintParser.Parse(SimpleHouse);
        Assert.That(blocks.Count, Is.EqualTo(17)); // 9 floor + 8 walls
    }

    [Test]
    public void Parse_CoordinateOrigin_IsZero()
    {
        var (_, blocks) = BlueprintParser.Parse(SimpleHouse);
        var origin = blocks.First(b => b.Y == 0 && b.X == 0 && b.Z == 0);
        Assert.That(origin.BlockId, Is.EqualTo("cobblestone"));
    }

    [Test]
    public void Parse_CoordinatesRangeMatchDimensions()
    {
        var (_, blocks) = BlueprintParser.Parse(SimpleHouse);
        Assert.That(blocks.Max(b => b.X), Is.EqualTo(2)); // 3 wide → 0,1,2
        Assert.That(blocks.Max(b => b.Z), Is.EqualTo(2)); // 3 deep → 0,1,2
        Assert.That(blocks.Max(b => b.Y), Is.EqualTo(1)); // 2 tall → 0,1
    }

    // ── Legend override ───────────────────────────────────────────────────────

    [Test]
    public void Parse_CustomLegend_OverridesDefault()
    {
        const string markdown = """
            ---
            id: custom
            name: Custom
            ---

            ## Layers

            ### Y=0
            AAA

            ## Legend
            - `A` = stone
            - `.` = air (skip)
            """;

        var (_, blocks) = BlueprintParser.Parse(markdown);
        Assert.That(blocks, Has.Count.EqualTo(3));
        Assert.That(blocks, Has.All.Matches<PlacementBlock>(b => b.BlockId == "stone"));
    }

    [Test]
    public void Parse_LegendNullEntry_SkipsBlock()
    {
        // F is in the default legend mapping to null — should be skipped
        const string markdown = """
            ---
            id: x
            name: X
            ---

            ## Layers

            ### Y=0
            HF.
            """;

        var (_, blocks) = BlueprintParser.Parse(markdown);
        // H → red_bed (placed), F → null (skip), . → null (skip)
        Assert.That(blocks, Has.Count.EqualTo(1));
        Assert.That(blocks[0].BlockId, Is.EqualTo("red_bed"));
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Test]
    public void Parse_NullString_ReturnsEmpty()
    {
        var (meta, blocks) = BlueprintParser.Parse(null);
        Assert.That(blocks, Is.Empty);
        Assert.That(meta.Id, Is.Empty);
    }

    [Test]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        var (meta, blocks) = BlueprintParser.Parse(string.Empty);
        Assert.That(blocks, Is.Empty);
        Assert.That(meta.Id, Is.Empty);
    }

    [Test]
    public void Parse_NoFrontmatter_ReturnsEmptyMetadata()
    {
        var (meta, _) = BlueprintParser.Parse("## Layers\n### Y=0\nCCC\n");
        Assert.That(meta.Id, Is.Empty);
    }

    [Test]
    public void Parse_ProseLineInLayer_IsRejectedAsNonGrid()
    {
        // "Standard cobblestone floor" contains spaces and letters not in legend
        const string markdown = """
            ---
            id: x
            name: X
            ---

            ## Layers

            ### Y=0 (Floor)
            Standard cobblestone floor
            CCC
            """;

        var (_, blocks) = BlueprintParser.Parse(markdown);
        // Only "CCC" is a valid grid row
        Assert.That(blocks, Has.Count.EqualTo(3));
    }

    [Test]
    public void Parse_MultipleLayerHeaders_ParseEachCorrectly()
    {
        const string markdown = """
            ---
            id: two-layer
            name: Two Layer
            ---

            ## Layers

            ### Y=0
            CC

            ### Y=1
            PP
            """;

        var (_, blocks) = BlueprintParser.Parse(markdown);
        Assert.That(blocks.Count(b => b.Y == 0), Is.EqualTo(2));
        Assert.That(blocks.Count(b => b.Y == 1), Is.EqualTo(2));
    }

    [Test]
    public void Parse_MaterialsColonFormat_Parses()
    {
        const string markdown = """
            ---
            id: colon-test
            name: Colon Test
            materials: cobblestone: 10, oak_planks: 5
            ---
            """;

        var (meta, _) = BlueprintParser.Parse(markdown);
        Assert.That(meta.Materials, Has.Length.EqualTo(2));
        var cob = meta.Materials.FirstOrDefault(m => m.Block == "cobblestone");
        Assert.That(cob?.Quantity, Is.EqualTo(10));
    }
}
