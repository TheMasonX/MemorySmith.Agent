namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Reads a single MemorySmith wiki page by its slug/ID.
/// Returns the raw markdown content for use in LLM context injection.
/// </summary>
public sealed class GetPageTool(IMemoryGateway memory) : ITool
{
    public string Name => "GetPage";
    public string Description => "Read a wiki page from MemorySmith by slug (e.g. 'architecture', 'blueprints/gothic-cathedral').";
    public JsonElement InputSchema => JsonDocument.Parse(
        "{\"type\":\"object\",\"properties\":{\"pageId\":{\"type\":\"string\",\"description\":\"Page slug or ID\"}},\"required\":[\"pageId\"]}"
    ).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("pageId", out var idEl))
            return new ToolResult(false, "GetPage requires a 'pageId' argument.");

        var pageId = idEl.GetString() ?? "";

        if (string.IsNullOrWhiteSpace(pageId))
            return new ToolResult(false, "pageId must be a non-empty string.");

        var content = await memory.GetPageAsync(pageId, cancellationToken);

        if (content is null)
            return new ToolResult(false, $"Page '{pageId}' not found.");

        return new ToolResult(true, $"Page '{pageId}' loaded ({content.Length} chars).",
            new Dictionary<string, object?> { ["content"] = content });
    }
}