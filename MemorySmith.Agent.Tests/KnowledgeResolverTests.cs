using Agent.Core;
using Agent.Memory;

namespace MemorySmith.Agent.Tests;

[TestFixture]
public class KnowledgeResolverTests
{
    // ── Test doubles ──────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal IItemRegistry stub backed by an in-memory dictionary.
    /// Returns null for any unregistered ID.
    /// </summary>
    private sealed class StubItemRegistry : IItemRegistry
    {
        private readonly Dictionary<string, ItemSpec> _specs =
            new(StringComparer.OrdinalIgnoreCase);

        public void Add(ItemSpec spec) => _specs[spec.ItemId] = spec;

        public Task<ItemSpec?> GetAsync(string itemId, CancellationToken ct = default) =>
            Task.FromResult(_specs.TryGetValue(itemId, out var s) ? s : (ItemSpec?)null);
    }

    private static ItemSpec MakeSpec(
        string id,
        string display,
        bool smelting      = false,
        string[]? sources  = null) =>
        new()
        {
            ItemId           = id,
            DisplayName      = display,
            RequiresSmelting = smelting,
            SourceBlocks     = sources ?? [id],
        };

    private static LocalKnowledgeResolver MakeResolver(
        Action<StubItemRegistry>?  configRegistry = null,
        Action<MockMemoryGateway>? configGateway  = null)
    {
        var registry = new StubItemRegistry();
        configRegistry?.Invoke(registry);
        var gateway = new MockMemoryGateway();
        configGateway?.Invoke(gateway);
        return new LocalKnowledgeResolver(registry, gateway);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task Resolve_ItemRegistryHit_ReturnsCandidateWithHighConfidence()
    {
        var resolver = MakeResolver(r => r.Add(MakeSpec("oak_log", "Oak Log")));

        var result = await resolver.ResolveAsync(new KnowledgeQuery("oak_log"));

        Assert.That(result.Candidates, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(result.Best!.Id, Is.EqualTo("oak_log"));
        Assert.That(result.Best.Confidence, Is.GreaterThan(0.9f));
        Assert.That(result.Best.Type, Is.EqualTo(CandidateType.DirectMineable));
    }

    [Test]
    public async Task Resolve_SmeltableItem_ReturnsSmeltableType()
    {
        var resolver = MakeResolver(
            r => r.Add(MakeSpec("iron_ingot", "Iron Ingot", smelting: true, sources: ["iron_ore"])));

        var result = await resolver.ResolveAsync(new KnowledgeQuery("iron_ingot"));

        Assert.That(result.Best, Is.Not.Null);
        Assert.That(result.Best!.Type, Is.EqualTo(CandidateType.Smeltable));
    }

    [Test]
    public async Task Resolve_MemoryGatewayFallback_ReturnsWikiPageCandidate()
    {
        var resolver = MakeResolver(
            configGateway: g => g.AddSearchResult("iron ore",
                new SearchResult("ore-guide-1", 0.85, "Iron Ore Guide")));

        var result = await resolver.ResolveAsync(new KnowledgeQuery("iron ore"));

        var wikiCandidate = result.Candidates.FirstOrDefault(c => c.Type == CandidateType.WikiPage);
        Assert.That(wikiCandidate, Is.Not.Null);
        Assert.That(wikiCandidate!.Id, Is.EqualTo("ore-guide-1"));
    }

    [Test]
    public async Task Resolve_TopNCapRespected_NeverExceedsLimit()
    {
        var resolver = MakeResolver(
            configGateway: g => g.AddSearchResult("stone",
                new SearchResult("p1", 1.0),
                new SearchResult("p2", 0.9),
                new SearchResult("p3", 0.8),
                new SearchResult("p4", 0.7),
                new SearchResult("p5", 0.6),
                new SearchResult("p6", 0.5)));

        var result = await resolver.ResolveAsync(new KnowledgeQuery("stone", TopN: 3));

        Assert.That(result.Candidates.Count, Is.LessThanOrEqualTo(3));
    }

    [Test]
    public async Task Resolve_ConfidenceThreshold_FiltersLowScoreCandidates()
    {
        // Gateway score 0.3 → confidence 0.60 × 0.3 = 0.18  (below threshold)
        // Gateway score 0.9 → confidence 0.60 × 0.9 = 0.54  (above threshold)
        var resolver = MakeResolver(
            configGateway: g => g.AddSearchResult("dirt",
                new SearchResult("low-page", 0.3),
                new SearchResult("high-page", 0.9)));

        var result = await resolver.ResolveAsync(
            new KnowledgeQuery("dirt", ConfidenceThreshold: 0.5f));

        Assert.That(result.Candidates.All(c => c.Confidence >= 0.5f), Is.True);
        Assert.That(result.Candidates.Any(c => c.Id == "low-page"), Is.False,
            "low-page should be filtered out by confidence threshold");
        Assert.That(result.Candidates.Any(c => c.Id == "high-page"), Is.True,
            "high-page should survive the threshold");
    }

    [Test]
    public async Task Resolve_TypeFilter_ExcludesNonMatchingTypes()
    {
        // Register a DirectMineable and a Smeltable item; also a wiki page.
        var resolver = MakeResolver(
            r =>
            {
                r.Add(MakeSpec("oak_log",   "Oak Log"));                           // DirectMineable
                r.Add(MakeSpec("iron_ingot", "Iron Ingot", smelting: true,
                               sources: ["iron_ore"]));                            // Smeltable
            },
            g => g.AddSearchResult("iron",
                new SearchResult("iron-wiki", 0.8, "Iron Guide")));               // WikiPage

        var result = await resolver.ResolveAsync(
            new KnowledgeQuery("iron",
                Types: [CandidateType.WikiPage],
                TopN: 10));

        Assert.That(result.Candidates.All(c => c.Type == CandidateType.WikiPage), Is.True);
    }

    [Test]
    public async Task Resolve_TwoNearEqualGatewayResults_MarksWasAmbiguous()
    {
        // Scores 1.0 and 0.97 → confidences 0.60, 0.582 → difference 0.018 ≤ 0.05
        var resolver = MakeResolver(
            configGateway: g => g.AddSearchResult("diamond",
                new SearchResult("diamond-block", 1.0, "Diamond Block"),
                new SearchResult("diamond-ore",   0.97, "Diamond Ore")));

        var result = await resolver.ResolveAsync(new KnowledgeQuery("diamond", TopN: 5));

        Assert.That(result.WasAmbiguous, Is.True,
            "Top-2 wiki results with scores 1.0 and 0.97 should trigger ambiguity flag");
    }

    [Test]
    public async Task Resolve_UnknownQuery_ReturnsEmptyWithNoAmbiguity()
    {
        var resolver = MakeResolver(); // empty registry and gateway

        var result = await resolver.ResolveAsync(new KnowledgeQuery("totally_unknown_xyzzy"));

        Assert.That(result.Candidates, Is.Empty);
        Assert.That(result.Best, Is.Null);
        Assert.That(result.WasAmbiguous, Is.False);
    }

    [Test]
    public async Task Resolve_CraftableItem_ReturnsCraftableType()
    {
        // oak_planks: SourceBlocks = [oak_log] (not itself) → Craftable
        var resolver = MakeResolver(
            r => r.Add(MakeSpec("oak_planks", "Oak Planks", sources: ["oak_log"])));

        var result = await resolver.ResolveAsync(new KnowledgeQuery("oak_planks"));

        Assert.That(result.Best, Is.Not.Null);
        Assert.That(result.Best!.Type, Is.EqualTo(CandidateType.Craftable));
    }
}
