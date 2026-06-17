using Agent.Core;

namespace Agent.Memory;

/// <summary>
/// Phase 7-B stub implementation of <see cref="IKnowledgeResolver"/>.
///
/// Two knowledge sources (lexical-first per audit-synthesis C7):
///   1. <see cref="IItemRegistry"/>  — direct item lookup by normalized ID (deterministic, no network round-trip on cache hit).
///   2. <see cref="IMemoryGateway"/> — wiki search fallback (used when registry misses or more candidates are needed).
///
/// Retrieval pipeline:
///   1. Normalize query to item-id form (lowercase, spaces/hyphens → underscores).
///   2. Exact ID lookup in IItemRegistry.
///   3. Display-name prefix lookup in IItemRegistry (if exact missed and query >= 3 chars).
///   4. IMemoryGateway.SearchAsync to fill remaining TopN slots.
///   5. Apply type filter, confidence threshold, TopN cap.
///   6. Detect ambiguity: flag WasAmbiguous when top-2 scores are within 0.05.
///
/// Does not call the LLM (D-003). Does not traverse the memory graph.
/// Phase 7-C will add the observation pipeline as a third source.
/// </summary>
public sealed class LocalKnowledgeResolver(IItemRegistry registry, IMemoryGateway memory) : IKnowledgeResolver
{
    // Confidence scores assigned per source (conservatively tuned for Phase 7-B stub).
    private const float RegistryExactMatchConfidence  = 0.95f;
    private const float RegistryDisplayNameConfidence = 0.80f;
    private const float GatewaySearchBaseConfidence   = 0.60f;
    private const float AmbiguityThreshold            = 0.05f;

    /// <inheritdoc/>
    public async Task<KnowledgeResult> ResolveAsync(KnowledgeQuery query, CancellationToken ct = default)
    {
        var candidates = new List<KnowledgeCandidate>();

        // 1. Normalize query for lexical lookup.
        var normalizedId = NormalizeToItemId(query.Query);

        // 2. Registry: exact ID lookup.
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

        // 3. Registry: display-name / prefix fallback when exact missed
        //    and the original query differs from normalizedId (e.g. user typed "Oak Log").
        var queryAsId = query.Query.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        if (spec is null && query.Query.Length >= 3 && queryAsId != normalizedId)
        {
            var altSpec = await registry.GetAsync(queryAsId, ct);
            if (altSpec is not null)
            {
                candidates.Add(new KnowledgeCandidate(
                    Id:          altSpec.ItemId,
                    DisplayName: altSpec.DisplayName,
                    Type:        ClassifySpec(altSpec),
                    Confidence:  RegistryDisplayNameConfidence));
            }
        }

        // 4. Memory gateway: wiki search to fill remaining TopN slots.
        if (candidates.Count < query.TopN)
        {
            var searchResults = await memory.SearchAsync(query.Query, ct);
            foreach (var result in searchResults)
            {
                if (candidates.Count >= query.TopN) break;

                // Skip if already covered by a registry hit.
                if (candidates.Any(c => string.Equals(c.Id, result.PageId, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var pageConfidence = GatewaySearchBaseConfidence * (float)result.Score;
                candidates.Add(new KnowledgeCandidate(
                    Id:          result.PageId,
                    DisplayName: result.PageId,   // PageId is always available; Title field existence varies by implementation
                    Type:        CandidateType.WikiPage,
                    Confidence:  pageConfidence));
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
    /// </summary>
    private static string NormalizeToItemId(string query) =>
        query.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

    /// <summary>
    /// Maps an <see cref="ItemSpec"/> to its most specific <see cref="CandidateType"/>.
    ///
    /// Heuristic (Phase 7-B):
    ///   RequiresSmelting=true   → Smeltable
    ///   SourceBlocks contains ItemId → DirectMineable (mining the block yields the item)
    ///   SourceBlocks non-empty  → Craftable (other blocks yield this item via crafting)
    ///   Otherwise               → WikiItem (informational spec only)
    /// </summary>
    private static CandidateType ClassifySpec(ItemSpec spec)
    {
        if (spec.RequiresSmelting) return CandidateType.Smeltable;
        if (spec.SourceBlocks.Contains(spec.ItemId, StringComparer.OrdinalIgnoreCase))
            return CandidateType.DirectMineable;
        if (spec.SourceBlocks.Count > 0) return CandidateType.Craftable;
        return CandidateType.WikiItem;
    }
}
