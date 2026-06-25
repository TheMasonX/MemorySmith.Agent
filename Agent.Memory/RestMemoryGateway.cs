namespace Agent.Memory;

using Agent.Core;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// IMemoryGateway implementation that calls MemorySmith's REST API.
///
/// API contract (TheMasonX/MemorySmith):
///   GET  /api/search?query={q}&amp;limit={n}  — unified search (memories + pages)
///   GET  /api/pages/{slug}                — get a page by slug
///   POST /api/pages                       — create page { slug, title, body, minimumRole }
///   PUT  /api/pages/{slug}                — update page { slug, title, body, minimumRole }
///
/// Auth: optional X-Api-Key header when MemorySmith is configured with ApiKey.
/// </summary>
public sealed class RestMemoryGateway(HttpClient http, RestMemoryGatewayOptions options, ILogger<RestMemoryGateway>? logger = null) : IMemoryGateway
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger<RestMemoryGateway> _logger = logger ?? NullLogger<RestMemoryGateway>.Instance;

    // ── Search ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, CancellationToken cancellationToken = default)
    {
        var url = $"api/search?query={Uri.EscapeDataString(query)}&limit=20";
        var hits = await http.GetFromJsonAsync<SearchHit[]>(url, JsonOpts, cancellationToken)
                   ?? [];
        // Preserve MemorySmith's server-side ordering (score desc, then date desc).
        // Kind="page" → PageId is a slug usable in GetPageAsync.
        // Kind="memory" → PageId is a UUID, NOT a valid page slug.
        return hits
            .Select(h => new SearchResult(h.Id, h.Score ?? 0.0, h.Snippet, h.Kind))
            .ToArray();
    }

    // ── Pages ─────────────────────────────────────────────────────────────────

    public async Task<string?> GetPageAsync(
        string pageId, CancellationToken cancellationToken = default)
    {
        var url = $"api/pages/{Uri.EscapeDataString(pageId)}";
        var resp = await http.GetAsync(url, cancellationToken);
        if (!resp.IsSuccessStatusCode) return null;

        var page = await resp.Content.ReadFromJsonAsync<PageResponse>(JsonOpts, cancellationToken);
        return page?.Body;
    }

    public async Task<string> CreatePageAsync(
        string title, string content, string type, CancellationToken cancellationToken = default)
    {
        var slug = ToSlug(title);
        var req = new PageSaveRequest(slug, title, content, options.DefaultPageRole);
        var resp = await http.PostAsJsonAsync("api/pages", req, JsonOpts, cancellationToken);
        resp.EnsureSuccessStatusCode();

        var created = await resp.Content.ReadFromJsonAsync<PageResponse>(JsonOpts, cancellationToken);
        return created?.Slug ?? slug;
    }

    public async Task UpdatePageAsync(
        string pageId, string content, CancellationToken cancellationToken = default)
    {
        var url = $"api/pages/{Uri.EscapeDataString(pageId)}";

        // Fetch existing page to preserve its title on update.
        // If the page doesn't exist we fall back to pageId as a reasonable title
        // (the PUT will create it as an upsert on MemorySmith's side).
        // TSK-0111: only HttpRequestException with 404 triggers the upsert fallback.
        // Auth failures, timeouts, and 500s propagate to avoid silent data loss.
        PageResponse? existing = null;
        try
        {
            existing = await http.GetFromJsonAsync<PageResponse>(url, JsonOpts, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger?.LogInformation("RestMemoryGateway.UpdatePageAsync: page '{PageId}' not found — will upsert.", pageId);
        }

        var title = existing?.Title ?? pageId.Replace("-", " ");
        var req = new PageSaveRequest(pageId, title, content, options.DefaultPageRole);
        var resp = await http.PutAsJsonAsync(url, req, JsonOpts, cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ToSlug(string title) =>
        title.ToLowerInvariant()
             .Replace(' ', '-')
             .Replace(".", "")
             .Replace("/", "-")
             .Trim('-');

    // ── Internal DTOs ─────────────────────────────────────────────────────────

    private sealed record SearchHit(
        string Kind, string Id, string Title, string Snippet, string Url,
        double? Score);

    private sealed record PageResponse(
        string Slug, string Title, string Body);

    private sealed record PageSaveRequest(
        string Slug, string Title, string Body, string MinimumRole);
}
