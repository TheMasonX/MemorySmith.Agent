using Agent.Core;
using Agent.Tools;
using System.Text.Json;

namespace MemorySmith.Agent.Tests;

[TestFixture]
public class ToolEngineTests
{
    private ToolRegistry _registry = null!;
    private ToolEngine _engine = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = new ToolRegistry();
        _engine = new ToolEngine(_registry);
    }

    [Test]
    public async Task CallAsync_KnownTool_ReturnsSuccess()
    {
        _registry.Register(new EchoTool());
        var result = await _engine.CallAsync("Echo", JsonDocument.Parse("{\"message\":\"hello\"}").RootElement);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("hello"));
    }

    [Test]
    public async Task CallAsync_UnknownTool_ReturnsFailure()
    {
        var result = await _engine.CallAsync("NoSuchTool", JsonDocument.Parse("{}").RootElement);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("NoSuchTool"));
    }

    [Test]
    public async Task CallAsync_CaseInsensitiveToolName_ReturnsSuccess()
    {
        _registry.Register(new EchoTool());
        var result = await _engine.CallAsync("echo", JsonDocument.Parse("{\"message\":\"ci\"}").RootElement);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void All_RegisteredTools_AreVisible()
    {
        _registry.Register(new EchoTool());
        _registry.Register(new EchoTool()); // same name overwrites
        Assert.That(_registry.All, Has.Count.EqualTo(1));
    }
}

/// <summary>Simple echo tool for testing.</summary>
file sealed class EchoTool : ITool
{
    public string Name => "Echo";
    public string Description => "Returns the input message.";
    public JsonElement InputSchema => JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"message\":{\"type\":\"string\"}}}" ).RootElement;
    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var msg = arguments.TryGetProperty("message", out var v) ? v.GetString() ?? "" : "";
        return Task.FromResult(new ToolResult(true, $"Echo: {msg}"));
    }
}
