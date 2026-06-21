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
