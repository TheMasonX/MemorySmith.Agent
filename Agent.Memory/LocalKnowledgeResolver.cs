using Agent.Core;

namespace Agent.Memory;

/// <summary>
/// Phase 7-B implementation of <see cref="IKnowledgeResolver"/>.
///
/// Three knowledge sources in priority order (lexical-first per audit-synthesis C7):
///   1. <see cref="IItemRegistry"/>  — direct item lookup by normalized ID (deterministic, 0.95 confidence on exact hit).
///   2. <see cref="IMemoryGateway"/> — wiki search (semantic; receives raw query, not normalized ID).
///   3. WorldState.StructuredFacts   — runtime observed facts from the live world state (0.70 recent / 0.50 stale).
///
/// Retrieval pipeline:
///   1. Normalize query to item-id form (lowercase, spaces/hyphens → underscores).
///   2. Exact ID lookup in IItemRegistry.
///   3. IMemoryGateway.SearchAsync to fill remaining TopN slots.
///   4. WorldFact scan (synchronous, in-memory; no I/O) for keys containing the normalized query.
///   5. Apply type filter, confidence threshold, sort, TopN cap.
///   6. Detect ambiguity: flag WasAmbiguous when top-2 scores are within 0.05.
///
/// Does not call the LLM (D-003). Does not traverse the memory graph.
/// Phase 7-C will normalize the observation pipeline before WorldFacts enter the resolver.
/// Alias-based lookup will be added when IItemRegistry gains alias support (Phase 7-B D5).
/// </summary>
public sealed class LocalKnowledgeResolver(
    IItemRegistry registry,
    IMemoryGateway memory,
    Func<WorldState?>? worldStateAccessor = null) : IKnowledgeResolver
{
    // Confidence scores assigned per source (conservatively tuned for Phase 7-B stub).
    private const float RegistryExactMatchConfidence = 0.95f;
    private const float GatewaySearchBaseConfidence  = 0.60f;
    private const float AmbiguityThreshold           = 0.05f;

    // WorldFact confidence — decays with age of the observation.
    private const float          WorldFactRecentConfidence = 0.70f;
    private const float          WorldFactOldConfidence    = 0.50f;
    private static readonly TimeSpan WorldFactRecencyThreshold = TimeSpan.FromSeconds(60);

    /// <inheritdoc/>
    public async Task<KnowledgeResult> ResolveAsync(KnowledgeQuery query, CancellationToken ct = default)
    {
        var candidates = new List<KnowledgeCandidate>();

        // 1. Normalize query to item-id form.
        var normalizedId = NormalizeToItemId(query.Query);

        // 2. Registry: exact ID lookup (lexical-first per C7).
        var spec = await registry.GetAsync(normalizedId, ct);
        if (spec is not null)
        {
            candidates.Add(new KnowledgeCandidate(
                Id:          spec.ItemId,
                DisplayName: spec.DisplayName,
                Type:        ClassifySpec(spec),
                Confidence:  RegistryExactMatchConfidence,
                Detail:      spec.SourceBlocks.Count > 0
                                 ? $"source_blocks: {string.Join(", ", spec.SourceBlocks)}"
                                 : null));
        }

        // 3. Memory gateway: wiki search to fill remaining TopN slots.
        //    NOTE: SearchAsync receives query.Query (not normalizedId). This is intentional —
        //    wiki search is semantic, not lexical, and benefits from the human-readable form
        //    (e.g., "iron ore" performs better than "iron_ore" in embedding-based search).
        if (candidates.Count < query.TopN)
        {
            var searchResults = await memory.SearchAsync(query.Query, ct);
            foreach (var result in searchResults)
            {
                if (candidates.Count >= query.TopN) break;

                // Skip if already covered by a registry hit (PageId matches ItemId).
                if (candidates.Any(c => string.Equals(c.Id, result.PageId, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var pageConfidence = GatewaySearchBaseConfidence * (float)result.Score;
                candidates.Add(new KnowledgeCandidate(
                    Id:          result.PageId,
                    DisplayName: result.PageId,   // PageId is always available; Title field varies by implementation
                    Type:        CandidateType.WikiPage,
                    Confidence:  pageConfidence));
            }
        }

        // 4. WorldFact scan (synchronous, in-memory; no I/O).
        //    Scans StructuredFacts for keys that contain the normalized query string.
        //    Confidence is time-gated: recent observations are more trusted than stale ones.
        //    Added after gateway so the final sort weighs WorldFacts against wiki results —
        //    a recent fact (0.70) outranks a mid-score wiki page (0.60 × 0.7 = 0.42).
        var state = worldStateAccessor?.Invoke();
        if (state is not null)
        {
            foreach (var fact in state.StructuredFacts)
            {
                if (!fact.Key.Contains(normalizedId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (candidates.Any(c => string.Equals(c.Id, fact.Key, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var age        = DateTimeOffset.UtcNow - fact.Timestamp;
                var confidence = age <= WorldFactRecencyThreshold
                    ? WorldFactRecentConfidence
                    : WorldFactOldConfidence;

                candidates.Add(new KnowledgeCandidate(
                    Id:          fact.Key,
                    DisplayName: fact.Key,
                    Type:        CandidateType.WorldFact,
                    Confidence:  confidence,
                    Detail:      fact.Value));
            }
        }

        // 5a. Apply type filter.
        if (query.Types is { Length: > 0 } typeFilter)
            candidates = [.. candidates.Where(c => typeFilter.Contains(c.Type))];

        // 5b. Apply confidence threshold + sort + TopN cap.
        candidates = [.. candidates
            .Where(c => c.Confidence >= query.ConfidenceThreshold)
            .OrderByDescending(c => c.Confidence)
            .Take(query.TopN)];

        // 6. Detect ambiguity: top-2 confidence within AmbiguityThreshold of each other.
        var wasAmbiguous = candidates.Count >= 2
            && (candidates[0].Confidence - candidates[1].Confidence) <= AmbiguityThreshold;

        return new KnowledgeResult(candidates, wasAmbiguous);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Converts a query string to item-id form: lowercase, spaces/hyphens → underscores.
    /// "Iron Ore" → "iron_ore", "oak-log" → "oak_log".
    /// </summary>
    private static string NormalizeToItemId(string query) =>
        query.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

    /// <summary>
    /// Maps an <see cref="ItemSpec"/> to its most specific <see cref="CandidateType"/>.
    ///
    /// Heuristic (Phase 7-B, Sprint 17 fix):
    ///   RequiresSmelting=true                                               → Smeltable
    ///   ItemId is in CommonMinecraftBlocks.DirectMineBlocks                 → DirectMineable
    ///     (covers both block names like "oak_log" and raw drops like "diamond" from "diamond_ore")
    ///   SourceBlocks contains ItemId (self-sourced; block name = item name) → DirectMineable
    ///   SourceBlocks non-empty (other blocks yield this item)               → Craftable
    ///   Otherwise                                                           → WikiItem (informational spec)
    ///
    /// Sprint 17 fix (D1): Added CommonMinecraftBlocks.DirectMineBlocks check as the primary
    /// signal for DirectMineable. Before this fix, items where the drop name differs from the
    /// block name (e.g. "diamond" dropped by "diamond_ore") were incorrectly classified as
    /// Craftable because SourceBlocks.Contains("diamond") is false when SourceBlocks=["diamond_ore"].
    /// </summary>
    private static CandidateType ClassifySpec(ItemSpec spec)
    {
        if (spec.RequiresSmelting) return CandidateType.Smeltable;
        if (CommonMinecraftBlocks.DirectMineBlocks.Contains(spec.ItemId, StringComparer.OrdinalIgnoreCase)
            || spec.SourceBlocks.Contains(spec.ItemId, StringComparer.OrdinalIgnoreCase))
            return CandidateType.DirectMineable;
        if (spec.SourceBlocks.Count > 0) return CandidateType.Craftable;
        return CandidateType.WikiItem;
    }
}
