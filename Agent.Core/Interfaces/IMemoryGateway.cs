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
    Task UpdatePageAsync(string pageId, string content, CancellationToken cancellationToken = default);
}
