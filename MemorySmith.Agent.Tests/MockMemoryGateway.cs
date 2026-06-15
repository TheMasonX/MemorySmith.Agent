using Agent.Core;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// In-memory IMemoryGateway implementation for test isolation.
/// Pre-populate pages and search results before running assertions.
/// </summary>
public sealed class MockMemoryGateway : IMemoryGateway
{
    private readonly Dictionary<string, string> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Query, SearchResult[] Results)> _searchMap = [];
    public List<string> CreatedPageIds { get; } = [];

    public void AddPage(string pageId, string content) => _pages[pageId] = content;

    public void AddSearchResult(string query, params SearchResult[] results)
        => _searchMap.Add((query, results));

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var match = _searchMap.FirstOrDefault(m =>
            string.Equals(m.Query, query, StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<SearchResult> results = match.Results ?? [];
        return Task.FromResult(results);
    }

    public Task<string?> GetPageAsync(string pageId, CancellationToken cancellationToken = default)
        => Task.FromResult(_pages.TryGetValue(pageId, out var v) ? v : null);

    public Task<string> CreatePageAsync(string title, string content, string type, CancellationToken cancellationToken = default)
    {
        var id = $"page-{_pages.Count + 1}";
        _pages[id] = content;
        CreatedPageIds.Add(id);
        return Task.FromResult(id);
    }

    public Task UpdatePageAsync(string pageId, string content, CancellationToken cancellationToken = default)
    {
        _pages[pageId] = content;
        return Task.CompletedTask;
    }
}
