using Agent.Core;
using Agent.Memory;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Integration tests for <see cref="MemorySmithItemRegistry"/> using the existing
/// <see cref="MockMemoryGateway"/> to isolate from HTTP.
///
/// Covers: direct page lookup, search fallback, null-return for unknowns,
/// slug normalisation (underscore → hyphen), smelting flag parsing.
/// </summary>
[TestFixture]
public class ItemRegistryTests
{
    private MockMemoryGateway _gateway = null!;
    private MemorySmithItemRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _gateway  = new MockMemoryGateway();
        _registry = new MemorySmithItemRegistry(_gateway);
    }

    // ── Direct page lookup ────────────────────────────────────────────────────

    [Test]
    public async Task GetAsync_PageExists_ReturnsItemSpec()
    {
        _gateway.AddPage("item-registry/oak-log",
            "item_id: oak_log\ndisplay_name: Oak Log\nsource_blocks: oak_log, birch_log\nrequires_smelting: false\nmin_harvest_level: 0\n");

        var spec = await _registry.GetAsync("oak_log");

        Assert.That(spec,              Is.Not.Null);
        Assert.That(spec!.ItemId,      Is.EqualTo("oak_log"));
        Assert.That(spec.DisplayName,  Is.EqualTo("Oak Log"));
    }

    [Test]
    public async Task GetAsync_NormalizesItemIdToSlug_UnderscoreToHyphen()
    {
        // "iron_ore" should look up "item-registry/iron-ore"
        _gateway.AddPage("item-registry/iron-ore",
            "item_id: iron_ore\ndisplay_name: Iron Ore\nsource_blocks: iron_ore, deepslate_iron_ore\nrequires_smelting: false\nmin_harvest_level: 2\n");

        var spec = await _registry.GetAsync("iron_ore");

        Assert.That(spec,              Is.Not.Null);
        Assert.That(spec!.ItemId,      Is.EqualTo("iron_ore"));
        Assert.That(spec.DisplayName,  Is.EqualTo("Iron Ore"));
    }

    [Test]
    public async Task GetAsync_SourceBlocks_ParsedAsReadOnlyList()
    {
        _gateway.AddPage("item-registry/oak-log",
            "item_id: oak_log\ndisplay_name: Oak Log\nsource_blocks: oak_log, birch_log, spruce_log\n");

        var spec = await _registry.GetAsync("oak_log");

        Assert.That(spec,                Is.Not.Null);
        Assert.That(spec!.SourceBlocks,  Is.InstanceOf<IReadOnlyList<string>>());
        Assert.That(spec.SourceBlocks,   Has.Count.EqualTo(3));
        Assert.That(spec.SourceBlocks,   Contains.Item("birch_log"));
    }

    [Test]
    public async Task GetAsync_RequiresSmelting_ParsedTrue()
    {
        _gateway.AddPage("item-registry/iron-ingot",
            "item_id: iron_ingot\ndisplay_name: Iron Ingot\nsource_blocks: iron_ore, deepslate_iron_ore\nrequires_smelting: true\nmin_harvest_level: 2\n");

        var spec = await _registry.GetAsync("iron_ingot");

        Assert.That(spec,                   Is.Not.Null);
        Assert.That(spec!.RequiresSmelting, Is.True);
        Assert.That(spec.MinHarvestLevel,   Is.EqualTo(2));
    }

    // ── Search fallback ───────────────────────────────────────────────────────

    [Test]
    public async Task GetAsync_DirectLookupMisses_FallsBackToSearch_ReturnsItemSpec()
    {
        // Direct page lookup will miss ("item-registry/cobalt" not in mock)
        // but search will return a matching page result
        _gateway.AddSearchResult(
            "item-registry/cobalt",
            new SearchResult("item-registry/cobalt-ore", 0.88, "cobalt ore registry page", "page"));
        _gateway.AddPage("item-registry/cobalt-ore",
            "item_id: cobalt_ore\ndisplay_name: Cobalt Ore\nsource_blocks: cobalt_ore\nrequires_smelting: false\nmin_harvest_level: 3\n");

        var spec = await _registry.GetAsync("cobalt");

        Assert.That(spec,             Is.Not.Null);
        Assert.That(spec!.ItemId,     Is.EqualTo("cobalt_ore"));
        Assert.That(spec.DisplayName, Is.EqualTo("Cobalt Ore"));
    }

    [Test]
    public async Task GetAsync_SearchResultKindIsNotPage_ReturnsNull()
    {
        // A "memory" kind result should not be used as a page
        _gateway.AddSearchResult(
            "item-registry/unknown",
            new SearchResult("memory-id-abc123", 0.5, "some memory snippet", "memory"));

        var spec = await _registry.GetAsync("unknown");

        Assert.That(spec, Is.Null);
    }

    // ── Not-found / null returns ───────────────────────────────────────────────

    [Test]
    public async Task GetAsync_PageNotFound_ReturnsNull()
    {
        var spec = await _registry.GetAsync("nonexistent_item");
        Assert.That(spec, Is.Null);
    }

    [Test]
    public async Task GetAsync_PageExists_ButMalformedContent_ReturnsNull()
    {
        // Page exists but has no required fields
        _gateway.AddPage("item-registry/bad-item",
            "# bad-item\nThis page has no front-matter.\n");

        var spec = await _registry.GetAsync("bad_item");

        // Malformed page (missing display_name) should return null
        Assert.That(spec, Is.Null);
    }

    [Test]
    public async Task GetAsync_NeverCallsLlm_OnMiss()
    {
        // Ensure the registry doesn't attempt to call CreatePage or anything LLM-like.
        // MockMemoryGateway tracks created pages — verify none were created.
        await _registry.GetAsync("nonexistent_item");
        Assert.That(_gateway.CreatedPageIds, Is.Empty,
            "Registry must not create pages or call LLM on a miss.");
    }
}
