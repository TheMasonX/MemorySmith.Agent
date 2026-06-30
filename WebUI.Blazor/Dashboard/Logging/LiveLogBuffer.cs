using System.Collections.Concurrent;

namespace WebUI.Blazor.Dashboard.Logging;

/// <summary>
/// Bounded in-memory ring buffer of recent <see cref="DashboardLogEntry"/> records.
/// Used by the dashboard timeline endpoint to surface recent Warning/Information/Error
/// log events without querying rolling files.
///
/// Thread-safe. Cap of 1000 entries; oldest are dropped when full.
/// </summary>
public sealed class LiveLogBuffer
{
    private const int DefaultCapacity = 1000;
    private readonly int _capacity;
    private readonly ConcurrentQueue<DashboardLogEntry> _entries = new();

    public LiveLogBuffer(int capacity = DefaultCapacity)
    {
        _capacity = capacity > 0 ? capacity : DefaultCapacity;
    }

    /// <summary>Adds an entry. Drops the oldest if at capacity.</summary>
    public void Add(DashboardLogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > _capacity && _entries.TryDequeue(out _)) { }
    }

    /// <summary>Returns the latest <paramref name="count"/> entries, oldest first
    /// (so the dashboard can auto-scroll to bottom for newest).</summary>
    public IReadOnlyList<DashboardLogEntry> GetLatest(int count = 100)
    {
        var all = _entries.ToArray();
        var take = Math.Min(count, all.Length);
        var result = new DashboardLogEntry[take];
        Array.Copy(all, all.Length - take, result, 0, take);
        // Keep oldest-first — frontend renders oldest→newest and auto-scrolls to bottom
        return result;
    }

    /// <summary>Current number of entries in the buffer.</summary>
    public int Count => _entries.Count;

    /// <summary>Clears all entries.</summary>
    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
    }
}
