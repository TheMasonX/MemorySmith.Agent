using Agent.Core;
using Agent.Tools;
using System.Text.Json;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Tests for ToolDispatcher — the single dispatcher that replaces the former
/// ToolRegistry + ToolEngine pair (consolidated, P2).
/// </summary>
[TestFixture]
public class ToolDispatcherTests
{
    private ToolDispatcher _dispatcher = null!;

    [SetUp]
    public void SetUp() => _dispatcher = new ToolDispatcher();

    [Test]
    public async Task CallAsync_KnownTool_ReturnsSuccess()
    {
        _dispatcher.Register(new EchoTool());
        var result = await _dispatcher.CallAsync("Echo",
            JsonDocument.Parse("{\"message\":\"hello\"}").RootElement);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("hello"));
    }

    [Test]
    public async Task CallAsync_UnknownTool_ReturnsFailure()
    {
        var result = await _dispatcher.CallAsync("NoSuchTool",
            JsonDocument.Parse("{}").RootElement);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("NoSuchTool"));
    }

    [Test]
    public async Task CallAsync_CaseInsensitiveToolName_ReturnsSuccess()
    {
        _dispatcher.Register(new EchoTool());
        var result = await _dispatcher.CallAsync("echo",
            JsonDocument.Parse("{\"message\":\"ci\"}").RootElement);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void All_RegisteredTools_AreVisible()
    {
        _dispatcher.Register(new EchoTool());
        _dispatcher.Register(new EchoTool()); // same name — overwrites
        Assert.That(_dispatcher.All, Has.Count.EqualTo(1));
    }

    [Test]
    public void Get_KnownTool_ReturnsTool()
    {
        _dispatcher.Register(new EchoTool());
        Assert.That(_dispatcher.Get("Echo"), Is.Not.Null);
    }

    [Test]
    public void Get_UnknownTool_ReturnsNull()
    {
        Assert.That(_dispatcher.Get("Ghost"), Is.Null);
    }
}

/// <summary>Simple echo tool for testing.</summary>
file sealed class EchoTool : ITool
{
    public string Name => "Echo";
    public string Description => "Returns the input message.";
    public JsonElement InputSchema =>
        JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"message\":{\"type\":\"string\"}}}").RootElement;
    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var msg = arguments.TryGetProperty("message", out var v) ? v.GetString() ?? "" : "";
        return Task.FromResult(new ToolResult(true, $"Echo: {msg}"));
    }
}

// ─── TSK-0114: Structured exception metadata tests ─────────────────────────────

[TestFixture]
public class ToolDispatcherExceptionMetadataTests
{
    [Test]
    public async Task CallAsync_ThrowingTool_CapturesExceptionTypeInJournal()
    {
        var journal = new SpyJournal();
        var dispatcher = new ToolDispatcher(journal);
        dispatcher.Register(new ThrowingTool("IntentionalCrash"));

        var result = await dispatcher.CallAsync("ThrowingTool",
            JsonDocument.Parse("{}").RootElement);

        // User-facing result is still ToolResult(false, ...)
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("IntentionalCrash"));

        // Journal entry captures exception type and stack trace
        var entries = journal.All;
        Assert.That(entries, Has.One.Matches<JournalEntry>(e =>
            e.Type == JournalEntryType.ActionFailed &&
            e.Details is not null &&
            e.Details.TryGetValue("exceptionType", out var exType) &&
            exType?.ToString() == "InvalidOperationException" &&
            e.Details.TryGetValue("stackTrace", out _)));
    }

    [Test]
    public async Task CallAsync_ThrowingTool_ReturnsFailureWithExceptionType()
    {
        var dispatcher = new ToolDispatcher(NullAgentJournal.Instance);
        dispatcher.Register(new ThrowingTool("Kaboom"));

        var result = await dispatcher.CallAsync("ThrowingTool",
            JsonDocument.Parse("{}").RootElement);

        // User-facing message includes exception type name for diagnostics
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain(nameof(InvalidOperationException)));
    }

    [Test]
    public async Task CallAsync_ThrowingTool_JournalHasDetailsDictionary()
    {
        var journal = new SpyJournal();
        var dispatcher = new ToolDispatcher(journal);
        dispatcher.Register(new ThrowingTool("TestCrash"));

        await dispatcher.CallAsync("ThrowingTool",
            JsonDocument.Parse("{}").RootElement);

        var entry = journal.All.FirstOrDefault(e => e.Type == JournalEntryType.ActionFailed);
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Details, Is.Not.Null);
        Assert.That(entry.Details!.ContainsKey("exceptionType"), Is.True);
        Assert.That(entry.Details!.ContainsKey("stackTrace"), Is.True);
        Assert.That(entry.Details!.ContainsKey("innerException"), Is.True);
        Assert.That(entry.Details!.ContainsKey("message"), Is.True);
    }
}

/// <summary>A tool that always throws an exception for testing exception metadata capture.</summary>
file sealed class ThrowingTool(string reason) : ITool
{
    public string Name => "ThrowingTool";
    public string Description => $"Throws {reason}";
    public JsonElement InputSchema => JsonDocument.Parse("{}").RootElement;
    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
        => throw new InvalidOperationException($"Intentional crash: {reason}");
}

/// <summary>Simple spy journal that records entries for later inspection.</summary>
file sealed class SpyJournal : IAgentJournal
{
    private readonly List<JournalEntry> _entries = [];
    public List<JournalEntry> Entries => _entries;
    public int Count => _entries.Count;
    public IReadOnlyList<JournalEntry> All => _entries.AsReadOnly();
    public void Log(JournalEntry entry) => _entries.Add(entry);
    public IReadOnlyList<JournalEntry> Recent(int count) =>
        _entries.TakeLast(count).Reverse().ToList().AsReadOnly();
    public IReadOnlyList<JournalEntry> Query(
        JournalEntryType? type = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null) =>
        _entries.Where(e =>
            (type is null || e.Type == type) &&
            (from is null || e.Timestamp >= from) &&
            (to is null || e.Timestamp <= to))
        .ToList().AsReadOnly();
    public void Clear() => _entries.Clear();
}
