namespace Agent.Memory;

using Agent.Construction;
using Agent.Core;

/// <summary>
/// <see cref="IBlueprintRepository"/> backed by MemorySmith wiki pages.
///
/// Blueprint pages must live at slug "blueprints/{blueprintId}" and use the
/// standard blueprint markdown format: frontmatter + Y-layer grids + optional legend.
///
/// Page lookup strategy mirrors <see cref="MemorySmithItemRegistry"/>:
///   1. Direct slug lookup: "blueprints/{id-with-hyphens}"
///   2. Search fallback: query "blueprints/{blueprintId}" and pick the first page hit
///      whose PageId contains "blueprints/".
///
/// Returns null for unknown blueprints. <see cref="SaveAsync"/> is not implemented in
/// Phase 4b — blueprints are authored as wiki pages manually.
/// Never calls the LLM (D-003).
/// </summary>
public sealed class MemorySmithBlueprintRepository(IMemoryGateway memory) : IBlueprintRepository
{
    private const string PagePrefix = "blueprints/";

    /// <inheritdoc/>
    public async Task<Blueprint?> GetAsync(
        string blueprintId, CancellationToken ct = default)
    {
        // Normalize to slug form: underscores → hyphens, lowercase.
        var slug   = blueprintId.Replace('_', '-').ToLowerInvariant();
        var pageId = $"{PagePrefix}{slug}";

        // 1. Direct page lookup (fast, deterministic — preferred per D-003).
        var content = await memory.GetPageAsync(pageId, ct);

        // 2. Search fallback for IDs that don't match the normalisation convention.
        if (content is null)
        {
            var results = await memory.SearchAsync($"{PagePrefix}{blueprintId}", ct);
            var hit = results.FirstOrDefault(r =>
                string.Equals(r.Kind, "page", StringComparison.OrdinalIgnoreCase) &&
                r.PageId.Contains("blueprints", StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                content = await memory.GetPageAsync(hit.PageId, ct);
        }

        if (content is null) return null;

        var (blueprint, _) = BlueprintParser.Parse(content);
        // Preserve the raw markdown so GoalFactory can re-parse blocks later.
        return blueprint with { RawMarkdown = content };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Blueprint>> SearchAsync(
        string query, CancellationToken ct = default)
    {
        var results    = await memory.SearchAsync(query, ct);
        var blueprints = new List<Blueprint>();

        foreach (var result in results.Where(r =>
            string.Equals(r.Kind, "page", StringComparison.OrdinalIgnoreCase) &&
            r.PageId.Contains("blueprints", StringComparison.OrdinalIgnoreCase)))
        {
            var content = await memory.GetPageAsync(result.PageId, ct);
            if (content is null) continue;

            var (blueprint, _) = BlueprintParser.Parse(content);
            if (!string.IsNullOrEmpty(blueprint.Id))
                blueprints.Add(blueprint with { RawMarkdown = content });
        }

        return blueprints;
    }

    /// <inheritdoc/>
    /// <exception cref="NotImplementedException">
    /// Phase 4b: blueprints are authored as wiki pages. Full CRUD is Phase 5.
    /// </exception>
    public Task<string> SaveAsync(Blueprint blueprint, CancellationToken ct = default) =>
        throw new NotImplementedException(
            "MemorySmithBlueprintRepository.SaveAsync is not implemented in Phase 4b. " +
            "Author blueprints as MemorySmith wiki pages under blueprints/.");
}
