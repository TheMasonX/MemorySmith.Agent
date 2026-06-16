using Agent.Core;
using Agent.Memory;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Integration tests for <see cref="MemorySmithItemRegistry"/> using the existing
/// <see cref="MockMemoryGateway"/> to isolate from HTTP.
///
/// Covers: direct page lookup, search fallback, null-return for unknowns,
/// slug normalisation (underscore → hyphen), smelting flag parsing.
/// Sprint 2c additions: TTL cache hit, cache miss/expiry, disabled cache.
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
        _registry = MakeRegistry(); // default opts: TTL = 60s
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private MemorySmithItemRegistry MakeRegistry(RestMemoryGatewayOptions? opts = null) =>
        new(_gateway, opts ?? new RestMemoryGatewayOptions());

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
        _gateway.AddPage("item-registry/bad-item",
            "# bad-item\nThis page has no front-matter.\n");

        var spec = await _registry.GetAsync("bad_item");

        Assert.That(spec, Is.Null);
    }

    [Test]
    public async Task GetAsync_NeverCallsLlm_OnMiss()
    {
        await _registry.GetAsync("nonexistent_item");
        Assert.That(_gateway.CreatedPageIds, Is.Empty,
            "Registry must not create pages or call LLM on a miss.");
    }

    // ── Sprint 2c: TTL cache ───────────────────────────────────────────────────

    /// <summary>
    /// Cache hit: calling GetAsync twice for the same item should only query
    /// the gateway once. The second call returns the cached result.
    /// </summary>
    [Test]
    public async Task GetAsync_CacheHit_DoesNotQueryGatewaySecondTime()
    {
        const string pageContent = "item_id: oak_log\ndisplay_name: Oak Log\nsource_blocks: oak_log\n";
        _gateway.AddPage("item-registry/oak-log", pageContent);

        var counting = new CountingGateway(_gateway);
        var registry = new MemorySmithItemRegistry(counting, new RestMemoryGatewayOptions());

        // First call — should hit gateway
        var spec1 = await registry.GetAsync("oak_log");
        // Second call — should hit cache
        var spec2 = await registry.GetAsync("oak_log");

        Assert.That(spec1, Is.Not.Null);
        Assert.That(spec2, Is.Not.Null);
        Assert.That(counting.GetPageCallCount, Is.EqualTo(1),
            "Second GetAsync call should return cached result without a gateway call.");
    }

    /// <summary>
    /// Null results (missing pages) are also cached to avoid hammering the API.
    /// </summary>
    [Test]
    public async Task GetAsync_NullResult_IsCached()
    {
        var counting = new CountingGateway(_gateway);
        var registry = new MemorySmithItemRegistry(counting, new RestMemoryGatewayOptions());

        // Item doesn't exist — returns null
        var spec1 = await registry.GetAsync("missing_item");
        // Second call — should still be cached null
        var spec2 = await registry.GetAsync("missing_item");

        Assert.That(spec1, Is.Null);
        Assert.That(spec2, Is.Null);
        Assert.That(counting.GetPageCallCount, Is.EqualTo(1),
            "Null (missing) results should be cached to avoid repeated gateway calls.");
    }

    /// <summary>
    /// When ItemCacheTtlSeconds = 0, caching is disabled and every call queries the gateway.
    /// </summary>
    [Test]
    public async Task GetAsync_CachingDisabled_AlwaysQueriesGateway()
    {
        const string pageContent = "item_id: oak_log\ndisplay_name: Oak Log\nsource_blocks: oak_log\n";
        _gateway.AddPage("item-registry/oak-log", pageContent);

        var counting = new CountingGateway(_gateway);
        var opts     = new RestMemoryGatewayOptions { ItemCacheTtlSeconds = 0 }; // disable
        var registry = new MemorySmithItemRegistry(counting, opts);

        await registry.GetAsync("oak_log");
        await registry.GetAsync("oak_log");

        Assert.That(counting.GetPageCallCount, Is.EqualTo(2),
            "With ItemCacheTtlSeconds=0, every GetAsync call must query the gateway.");
    }

    /// <summary>
    /// Two different items use separate cache entries.
    /// </summary>
    [Test]
    public async Task GetAsync_DifferentItems_IndependentCacheEntries()
    {
        _gateway.AddPage("item-registry/oak-log",
            "item_id: oak_log\ndisplay_name: Oak Log\nsource_blocks: oak_log\n");
        _gateway.AddPage("item-registry/iron-ore",
            "item_id: iron_ore\ndisplay_name: Iron Ore\nsource_blocks: iron_ore\n");

        var counting = new CountingGateway(_gateway);
        var registry = new MemorySmithItemRegistry(counting, new RestMemoryGatewayOptions());

        await registry.GetAsync("oak_log");   // fills oak_log cache entry
        await registry.GetAsync("iron_ore");  // fills iron_ore cache entry

        Assert.That(counting.GetPageCallCount, Is.EqualTo(2),
            "Each unique item should populate its own independent cache entry.");
    }
}

/// <summary>
/// Counting wrapper for IMemoryGateway — tracks GetPageAsync call count
/// to verify TTL cache behaviour in tests. File-local to avoid name collisions.
/// </summary>
file sealed class CountingGateway(MockMemoryGateway inner) : IMemoryGateway
{
    public int GetPageCallCount { get; private set; }

    public async Task<string?> GetPageAsync(string pageId, CancellationToken cancellationToken = default)
    {
        GetPageCallCount++;
        return await inner.GetPageAsync(pageId, cancellationToken);
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => inner.SearchAsync(query, cancellationToken);

    public Task<string> CreatePageAsync(string title, string content, string type, CancellationToken cancellationToken = default)
        => inner.CreatePageAsync(title, content, type, cancellationToken);

    public Task UpdatePageAsync(string pageId, string content, CancellationToken cancellationToken = default)
        => inner.UpdatePageAsync(pageId, content, cancellationToken);
}
