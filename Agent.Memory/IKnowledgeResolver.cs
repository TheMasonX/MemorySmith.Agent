using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Agent.Memory;

/// <summary>
/// Types of knowledge a <see cref="KnowledgeCandidate"/> can represent.
/// </summary>
public enum CandidateType
{
    /// <summary>A block the bot can mine directly (no crafting). Source: IItemRegistry (SourceBlocks contains ItemId or ItemId is in CommonMinecraftBlocks.DirectMineBlocks).</summary>
    DirectMineable,

    /// <summary>An item crafted from other materials (not smelted). Source: IItemRegistry (RequiresSmelting=false, SourceBlocks non-empty, not self-sourced).</summary>
    Craftable,

    /// <summary>An item produced via a furnace. Source: IItemRegistry (RequiresSmelting=true).</summary>
    Smeltable,

    /// <summary>A wiki item page describing an item's properties. Source: IItemRegistry (no source blocks).</summary>
    WikiItem,

    /// <summary>A general wiki page (blueprint, guide, or note). Source: IMemoryGateway.SearchAsync.</summary>
    WikiPage,

    /// <summary>A runtime observation fact from WorldState.StructuredFacts (in-memory, live). Confidence decays with age: 0.70 within 60 s, 0.50 after. Source: WorldState.</summary>
    WorldFact,
}

/// <summary>
/// A single candidate result from a <see cref="IKnowledgeResolver.ResolveAsync"/> call.
/// </summary>
/// <param name="Id">Canonical item or page ID (e.g. "oak_log", "item-registry/oak-log").</param>
/// <param name="DisplayName">Human-readable name (e.g. "Oak Log").</param>
/// <param name="Type">The knowledge source type for this candidate.</param>
/// <param name="Confidence">0.0–1.0 confidence score; lower means less certain.</param>
/// <param name="Detail">Optional extra context (e.g. "source_blocks: oak_log, birch_log").</param>
public sealed record KnowledgeCandidate(
    string Id,
    string DisplayName,
    CandidateType Type,
    float Confidence,
    string? Detail = null);

/// <summary>
/// A query submitted to <see cref="IKnowledgeResolver.ResolveAsync"/>.
/// </summary>
/// <param name="Query">Natural-language or identifier query (e.g. "oak_log", "iron ore").</param>
/// <param name="Types">Optional type filter — if provided, only candidates of these types are returned.</param>
/// <param name="ConfidenceThreshold">Minimum confidence required to include a candidate. Default: 0.0 (include all).</param>
/// <param name="TopN">Maximum candidates to return. Default: 5.</param>
public sealed record KnowledgeQuery(
    string Query,
    CandidateType[]? Types = null,
    float ConfidenceThreshold = 0.0f,
    int TopN = 5);

/// <summary>
/// The result of a <see cref="IKnowledgeResolver.ResolveAsync"/> call.
/// </summary>
/// <param name="Candidates">Ranked list of matching candidates (descending confidence).</param>
/// <param name="WasAmbiguous">True if the top-2 candidates share a near-equal confidence score (within 0.05).</param>
public sealed record KnowledgeResult(
    IReadOnlyList<KnowledgeCandidate> Candidates,
    bool WasAmbiguous)
{
    /// <summary>Convenience accessor: the single best candidate, or null if no results.</summary>
    public KnowledgeCandidate? Best => Candidates.Count > 0 ? Candidates[0] : null;
}

/// <summary>
/// Single-entry-point knowledge resolver for the Phase 7-B stub.
///
/// Answers "what is X?" by checking registered knowledge sources in priority order.
/// Retrieval strategy: lexical-first (normalize query → IItemRegistry → IMemoryGateway → WorldState).
///
/// <b>Phase 7-B scope constraint:</b> Three sources — IItemRegistry, IMemoryGateway, WorldState.StructuredFacts.
/// No graph traversal, no planner wiring. API-accessible via GET /api/agent/resolve.
/// The resolver grows in Phase 7-C when the observation pipeline is ready.
///
/// Per D-003: deterministic-first. No LLM calls.
/// Per audit-synthesis C8: low-confidence matches are not auto-picked — confidence threshold
/// is always applied and WasAmbiguous is surfaced to the caller.
/// </summary>
public interface IKnowledgeResolver
{
    /// <summary>
    /// Resolves <paramref name="query"/> against all registered knowledge sources
    /// and returns ranked <see cref="KnowledgeCandidate"/> results.
    /// </summary>
    Task<KnowledgeResult> ResolveAsync(KnowledgeQuery query, CancellationToken ct = default);
}
