namespace Agent.Core;

/// <summary>
/// Abstraction over MemorySmith's wiki and memory storage.
/// Three integration patterns are supported:
///   In-Process — direct method calls when hosted in the same process.
///   MCP Tool   — LLM calls SearchMemory/CreatePage tools; gateway handles dispatch.
///   REST API   — HTTP calls to MemorySmith's /api/wiki endpoints.
/// </summary>
public interface IMemoryGateway
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
    Task<string?> GetPageAsync(string pageId, CancellationToken cancellationToken = default);
    Task<string> CreatePageAsync(string title, string content, string type, CancellationToken cancellationToken = default);
    /// <summary>
    /// Update an existing page. When <paramref name="title"/> is provided it is used
    /// as the page title; otherwise the gateway fetches the existing page to preserve
    /// its title (or falls back to a slug-derived title for upsert).
    /// </summary>
    Task UpdatePageAsync(string pageId, string content, string? title = null, CancellationToken cancellationToken = default);
}
