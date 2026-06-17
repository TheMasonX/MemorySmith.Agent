namespace Agent.Memory;

using Agent.Construction;
using Agent.Core;

/// <summary>
/// <see cref="IBlueprintRepository"/> backed by MemorySmith wiki pages.
///
/// Blueprint pages must live at slug "blueprints/{blueprintId}" and use the
/// standard blueprint markdown format: frontmatter + Y-layer grids + optional legend.
///
/// Page lookup strategy (in order):
///   1. Direct slug lookup on the live MemorySmith gateway.
///   2. Local file fallback: {localPagesRoot}/Data/Pages/blueprints/{id}.md (for offline/dev).
///   3. Search fallback via the gateway.
///
/// Returns null for unknown blueprints. <see cref="SaveAsync"/> is not implemented in
/// Phase 4b — blueprints are authored as wiki pages manually.
/// Never calls the LLM (D-003).
/// </summary>
public sealed class MemorySmithBlueprintRepository(
    IMemoryGateway memory,
    string? localPagesRoot = null) : IBlueprintRepository
{
    private const string PagePrefix = "blueprints/";
    private readonly string? _localPagesRoot = localPagesRoot ?? FindLocalPagesRoot();

    /// <inheritdoc/>
    public async Task<Blueprint?> GetAsync(
        string blueprintId, CancellationToken ct = default)
    {
        // Normalize to slug form: underscores → hyphens, lowercase.
        var slug   = blueprintId.Replace('_', '-').ToLowerInvariant();
        var pageId = $"{PagePrefix}{slug}";

        // 1. Direct page lookup (fast, deterministic — preferred per D-003).
        var content = await memory.GetPageAsync(pageId, ct);

        // 2. Local file fallback (offline / dev runs using checked-in pages).
        if (string.IsNullOrWhiteSpace(content))
            content = LoadLocalPage(slug);

        // 3. Search fallback for IDs that don't match the normalisation convention.
        if (string.IsNullOrWhiteSpace(content))
        {
            var results = await memory.SearchAsync($"{PagePrefix}{blueprintId}", ct);
            var hit = results.FirstOrDefault(r =>
                string.Equals(r.Kind, "page", StringComparison.OrdinalIgnoreCase) &&
                r.PageId.Contains("blueprints", StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                content = await memory.GetPageAsync(hit.PageId, ct);
        }

        if (string.IsNullOrWhiteSpace(content)) return null;

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

        // Supplement with any locally available blueprints not returned by the gateway.
        foreach (var local in LoadAllLocalBlueprints())
            if (blueprints.All(b => !string.Equals(b.Id, local.Id, StringComparison.OrdinalIgnoreCase)))
                blueprints.Add(local);

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

    // ── Local file helpers ────────────────────────────────────────────────────

    private string? LoadLocalPage(string slug)
    {
        if (string.IsNullOrWhiteSpace(_localPagesRoot)) return null;

        var candidate = Path.Combine(_localPagesRoot, "Data", "Pages", "blueprints", $"{slug}.md");
        return File.Exists(candidate) ? File.ReadAllText(candidate) : null;
    }

    private IEnumerable<Blueprint> LoadAllLocalBlueprints()
    {
        if (string.IsNullOrWhiteSpace(_localPagesRoot)) yield break;

        var dir = Path.Combine(_localPagesRoot, "Data", "Pages", "blueprints");
        if (!Directory.Exists(dir)) yield break;

        foreach (var file in Directory.EnumerateFiles(dir, "*.md"))
        {
            var content = File.ReadAllText(file);
            var (blueprint, _) = BlueprintParser.Parse(content);
            if (!string.IsNullOrEmpty(blueprint.Id))
                yield return blueprint with { RawMarkdown = content };
        }
    }

    private static string? FindLocalPagesRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(current, "Data", "Pages", "blueprints");
            if (Directory.Exists(candidate))
                return current;

            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;
        }

        return null;
    }
}
