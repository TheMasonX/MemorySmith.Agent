namespace Agent.Memory;

using Agent.Core;
using System.Collections.Concurrent;

/// <summary>
/// <see cref="IItemRegistry"/> implementation backed by MemorySmith wiki pages.
///
/// Wiki pages must live at slug "item-registry/{itemId}" (underscores converted to hyphens)
/// and use the following front-matter format (YAML-like, one field per line):
///
/// <code>
/// # oak-log
///
/// item_id: oak_log
/// display_name: Oak Log
/// source_blocks: oak_log, birch_log, spruce_log
/// requires_smelting: false
/// min_harvest_level: 0
/// </code>
///
/// Fields are case-insensitive. Extra whitespace is tolerated. Lines that are not
/// valid key: value pairs (headings, comments, blank lines) are silently skipped.
///
/// Returns null for unknown or malformed pages. Never calls the LLM (D-003).
///
/// Sprint 2c: In-memory TTL cache keyed by normalised item slug.
/// TTL is controlled by <see cref="RestMemoryGatewayOptions.ItemCacheTtlSeconds"/>.
/// Set to 0 to disable caching (useful for tests or when data changes frequently).
/// </summary>
public sealed class MemorySmithItemRegistry(
    IMemoryGateway memory,
    RestMemoryGatewayOptions options) : IItemRegistry
{
    private const string PagePrefix = "item-registry/";

    // Cache entry: (nullable spec, expiry instant). Key is the normalised slug (lowercase, hyphens).
    private readonly ConcurrentDictionary<string, (ItemSpec? Spec, DateTimeOffset Expires)> _cache = new();

    private bool CachingEnabled => options.ItemCacheTtlSeconds > 0;
    private TimeSpan CacheTtl   => TimeSpan.FromSeconds(options.ItemCacheTtlSeconds);

    /// <inheritdoc/>
    public async Task<ItemSpec?> GetAsync(string itemId, CancellationToken ct = default)
    {
        // Normalize itemId to slug form: underscores → hyphens, lowercase.
        var slug = itemId.Replace('_', '-').ToLowerInvariant();

        // Sprint 2c: cache look-up (before any HTTP call).
        if (CachingEnabled && _cache.TryGetValue(slug, out var entry) && DateTimeOffset.UtcNow < entry.Expires)
            return entry.Spec;

        var spec = await FetchAsync(slug, itemId, ct);

        // Sprint 2c: store result (including null — avoids hammering the API for missing items).
        if (CachingEnabled)
            _cache[slug] = (spec, DateTimeOffset.UtcNow.Add(CacheTtl));

        return spec;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<ItemSpec?> FetchAsync(string slug, string itemId, CancellationToken ct)
    {
        var pageId = $"{PagePrefix}{slug}";

        // 1. Direct page lookup (deterministic, fast — preferred path per D-003).
        var content = await memory.GetPageAsync(pageId, ct);

        // 2. Fall back to search if direct lookup misses (e.g. modded items whose
        //    slug doesn't match the normalisation convention).
        if (content is null)
        {
            var results = await memory.SearchAsync($"{PagePrefix}{itemId}", ct);
            var hit = results.FirstOrDefault(r =>
                string.Equals(r.Kind, "page", StringComparison.OrdinalIgnoreCase) &&
                r.PageId.Contains("item-registry", StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                content = await memory.GetPageAsync(hit.PageId, ct);
        }

        return content is null ? null : ParseItemSpec(content);
    }

    /// <summary>
    /// Parses the front-matter fields from a wiki page body string.
    /// Returns null if required fields (<c>item_id</c>, <c>display_name</c>)
    /// are missing or the content is empty.
    ///
    /// Exposed as <c>public</c> for direct unit testing via ItemSpecParserTests.
    /// </summary>
    public static ItemSpec? ParseItemSpec(string pageContent)
    {
        if (string.IsNullOrWhiteSpace(pageContent)) return null;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? headingItemId = null;

        foreach (var rawLine in pageContent.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            // Extract item ID from the first Markdown heading (# oak-log → "oak_log").
            if (headingItemId is null && line.StartsWith('#'))
            {
                headingItemId = line.TrimStart('#', ' ').Trim()
                    .ToLowerInvariant()
                    .Replace('-', '_');
                continue;
            }

            // Parse "key: value" pairs.
            var colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;

            var key   = line[..colonIdx].Trim();
            var value = line[(colonIdx + 1)..].Trim();

            if (key.Length == 0 || !IsValidFieldKey(key) || value.Length == 0) continue;

            fields[key] = value;
        }

        // Required: item_id — prefer explicit field, fall back to heading.
        if (!fields.TryGetValue("item_id", out var itemId) || string.IsNullOrWhiteSpace(itemId))
            itemId = headingItemId;
        if (string.IsNullOrWhiteSpace(itemId)) return null;

        // Required: display_name.
        if (!fields.TryGetValue("display_name", out var displayName)
            || string.IsNullOrWhiteSpace(displayName))
            return null;

        // Optional: source_blocks — comma-separated list of block IDs.
        IReadOnlyList<string> sourceBlocks = [];
        if (fields.TryGetValue("source_blocks", out var sbRaw) && sbRaw.Length > 0)
        {
            sourceBlocks = sbRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .ToArray();
        }

        // Optional: requires_smelting (default: false).
        var requiresSmelting = false;
        if (fields.TryGetValue("requires_smelting", out var rsRaw))
            bool.TryParse(rsRaw, out requiresSmelting);

        // Optional: min_harvest_level (default: 0).
        var minHarvestLevel = 0;
        if (fields.TryGetValue("min_harvest_level", out var mhlRaw))
            int.TryParse(mhlRaw, out minHarvestLevel);

        return new ItemSpec
        {
            ItemId           = itemId,
            DisplayName      = displayName,
            SourceBlocks     = sourceBlocks,
            RequiresSmelting = requiresSmelting,
            MinHarvestLevel  = minHarvestLevel,
        };
    }

    // A valid front-matter key consists only of letters, digits, and underscores.
    private static bool IsValidFieldKey(string key)
    {
        foreach (var c in key)
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        return true;
    }
}
