using Agent.Core;
using Agent.Memory;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Unit tests for <see cref="MemorySmithItemRegistry.ParseItemSpec"/>.
/// Tests run against raw markdown strings — no HTTP, no mock gateway.
/// Covers: happy path, missing required fields, malformed values,
/// extra whitespace, case-insensitive keys, HTML comment lines.
/// </summary>
[TestFixture]
public class ItemSpecParserTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ItemSpec? Parse(string content) =>
        MemorySmithItemRegistry.ParseItemSpec(content);

    // ── Happy path ────────────────────────────────────────────────────────────

    [Test]
    public void ParseItemSpec_ValidPage_ReturnsItemSpec()
    {
        const string content = """
            # oak-log

            item_id: oak_log
            display_name: Oak Log
            source_blocks: oak_log, birch_log, spruce_log
            requires_smelting: false
            min_harvest_level: 0
            """;

        var spec = Parse(content);
        Assert.That(spec, Is.Not.Null);
        Assert.That(spec!.ItemId,          Is.EqualTo("oak_log"));
        Assert.That(spec.DisplayName,       Is.EqualTo("Oak Log"));
        Assert.That(spec.SourceBlocks,      Has.Count.EqualTo(3));
        Assert.That(spec.RequiresSmelting,  Is.False);
        Assert.That(spec.MinHarvestLevel,   Is.EqualTo(0));
    }

    [Test]
    public void ParseItemSpec_RequiresSmelting_ParsedTrue()
    {
        const string content = """
            # iron-ingot

            item_id: iron_ingot
            display_name: Iron Ingot
            source_blocks: iron_ore, deepslate_iron_ore
            requires_smelting: true
            min_harvest_level: 2
            """;

        var spec = Parse(content);
        Assert.That(spec, Is.Not.Null);
        Assert.That(spec!.RequiresSmelting, Is.True);
        Assert.That(spec.MinHarvestLevel,   Is.EqualTo(2));
    }

    [Test]
    public void ParseItemSpec_MultipleSourceBlocks_AllParsed()
    {
        const string content = """
            item_id: all_logs
            display_name: Any Log
            source_blocks: oak_log, birch_log, spruce_log, dark_oak_log, jungle_log, acacia_log, cherry_log
            """;

        var spec = Parse(content);
        Assert.That(spec, Is.Not.Null);
        Assert.That(spec!.SourceBlocks, Has.Count.EqualTo(7));
        Assert.That(spec.SourceBlocks, Contains.Item("oak_log"));
        Assert.That(spec.SourceBlocks, Contains.Item("cherry_log"));
    }

    [Test]
    public void ParseItemSpec_MinHarvestLevel_ParsedCorrectly()
    {
        const string content = """
            item_id: diamond
            display_name: Diamond
            source_blocks: diamond_ore, deepslate_diamond_ore
            requires_smelting: false
            min_harvest_level: 3
            """;

        var spec = Parse(content);
        Assert.That(spec, Is.Not.Null);
        Assert.That(spec!.MinHarvestLevel, Is.EqualTo(3));
    }

    // ── Tolerance ─────────────────────────────────────────────────────────────

    [Test]
    public void ParseItemSpec_ExtraWhitespace_Tolerated()
    {
        const string content = "  item_id  :   oak_log   \n  display_name  :  Oak Log  \n";
        var spec = Parse(content);
        Assert.That(spec, Is.Not.Null);
        Assert.That(spec!.ItemId,      Is.EqualTo("oak_log"));
        Assert.That(spec.DisplayName,  Is.EqualTo("Oak Log"));
    }

    [Test]
    public void ParseItemSpec_CaseInsensitiveFieldNames_Parsed()
    {
        const string content = """
            ITEM_ID: oak_log
            DISPLAY_NAME: Oak Log
            SOURCE_BLOCKS: oak_log
            REQUIRES_SMELTING: false
            MIN_HARVEST_LEVEL: 0
            """;

        var spec = Parse(content);
        Assert.That(spec, Is.Not.Null);
        Assert.That(spec!.ItemId,     Is.EqualTo("oak_log"));
        Assert.That(spec.DisplayName, Is.EqualTo("Oak Log"));
    }

    [Test]
    public void ParseItemSpec_CommentLines_AreIgnored()
    {
        const string content = """
            # oak-log

            <!-- This is a comment with: colons in it -->
            item_id: oak_log
            display_name: Oak Log
            source_blocks: oak_log
            """;

        var spec = Parse(content);
        Assert.That(spec, Is.Not.Null);
        Assert.That(spec!.ItemId, Is.EqualTo("oak_log"));
    }

    [Test]
    public void ParseItemSpec_BlankLines_Ignored()
    {
        const string content = "\n\nitem_id: oak_log\n\n\ndisplay_name: Oak Log\n\n";
        var spec = Parse(content);
        Assert.That(spec, Is.Not.Null);
        Assert.That(spec!.ItemId, Is.EqualTo("oak_log"));
    }

    [Test]
    public void ParseItemSpec_SourceBlocks_ExtraWhitespaceAroundCommas_Trimmed()
    {
        const string content = "item_id: oak_log\ndisplay_name: Oak Log\nsource_blocks:  oak_log ,  birch_log  ,  spruce_log  \n";
        var spec = Parse(content);
        Assert.That(spec, Is.Not.Null);
        Assert.That(spec!.SourceBlocks[0], Is.EqualTo("oak_log"));
        Assert.That(spec.SourceBlocks[1],  Is.EqualTo("birch_log"));
        Assert.That(spec.SourceBlocks[2],  Is.EqualTo("spruce_log"));
    }

    // ── Fallback item_id from heading ─────────────────────────────────────────

    [Test]
    public void ParseItemSpec_NoItemIdField_UsesHeading()
    {
        const string content = """
            # oak-log

            display_name: Oak Log
            source_blocks: oak_log
            """;

        // No explicit item_id field — should fall back to the heading "oak-log" → "oak_log"
        var spec = Parse(content);
        Assert.That(spec, Is.Not.Null);
        Assert.That(spec!.ItemId, Is.EqualTo("oak_log"));
    }

    // ── Missing required fields ───────────────────────────────────────────────

    [Test]
    public void ParseItemSpec_MissingDisplayName_ReturnsNull()
    {
        const string content = "item_id: oak_log\nsource_blocks: oak_log\n";
        var spec = Parse(content);
        Assert.That(spec, Is.Null);
    }

    [Test]
    public void ParseItemSpec_MissingItemIdAndHeading_ReturnsNull()
    {
        const string content = "display_name: Oak Log\nsource_blocks: oak_log\n";
        var spec = Parse(content);
        Assert.That(spec, Is.Null);
    }

    [Test]
    public void ParseItemSpec_EmptyContent_ReturnsNull()
    {
        Assert.That(Parse(""),          Is.Null);
        Assert.That(Parse("  "),        Is.Null);
        Assert.That(Parse("\n\n\n"),    Is.Null);
    }

    // ── Optional fields default correctly ────────────────────────────────────

    [Test]
    public void ParseItemSpec_NoOptionalFields_DefaultsApplied()
    {
        const string content = "item_id: oak_log\ndisplay_name: Oak Log\n";
        var spec = Parse(content);
        Assert.That(spec, Is.Not.Null);
        Assert.That(spec!.SourceBlocks,     Is.Empty);
        Assert.That(spec.RequiresSmelting,  Is.False);
        Assert.That(spec.MinHarvestLevel,   Is.EqualTo(0));
    }
}
