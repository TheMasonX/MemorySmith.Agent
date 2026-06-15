namespace Agent.Memory;

using Agent.Core;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// IMemoryGateway implementation that calls MemorySmith's REST API.
///
/// API contract (TheMasonX/MemorySmith):
///   GET  /api/search?query={q}&limit={n}  — unified search (memories + pages)
///   GET  /api/pages/{slug}                — get a page by slug
///   POST /api/pages                       — create page { slug, title, body, minimumRole }
///   PUT  /api/pages/{slug}                — update page { slug, title, body, minimumRole }
///
/// Auth: optional X-Api-Key header when MemorySmith is configured with ApiKey.
/// </summary>
public sealed class RestMemoryGateway(HttpClient http, RestMemoryGatewayOptions options) : IMemoryGateway
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Search ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, CancellationToken cancellationToken = default)
    {
        var url = $"api/search?query={Uri.EscapeDataString(query)}&limit=20";
        var hits = await http.GetFromJsonAsync<SearchHit[]>(url, JsonOpts, cancellationToken)
                   ?? [];
        return hits
            .Select(h => new SearchResult(h.Id, h.Score ?? 0.0, h.Snippet))
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

        // Fetch existing title so we can preserve it on update
        var existing = await http.GetFromJsonAsync<PageResponse>(url, JsonOpts, cancellationToken);
        var title = existing?.Title ?? pageId;

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