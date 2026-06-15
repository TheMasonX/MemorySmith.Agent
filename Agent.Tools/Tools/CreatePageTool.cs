namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Creates or updates a MemorySmith wiki page.
/// Used by the agent to record observations, blueprints, plans, or goals
/// as persistent wiki pages searchable by future sessions.
/// </summary>
public sealed class CreatePageTool(IMemoryGateway memory) : ITool
{
    public string Name => "CreatePage";
    public string Description => "Add a new wiki page to MemorySmith with a title, markdown body, and optional type tag.";
    public JsonElement InputSchema => JsonDocument.Parse(
        "{\"type\":\"object\",\"properties\":{\"title\":{\"type\":\"string\"},\"content\":{\"type\":\"string\"},\"type\":{\"type\":\"string\",\"default\":\"wiki\"}},\"required\":[\"title\",\"content\"]}"
    ).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("title", out var titleEl) ||
            !arguments.TryGetProperty("content", out var contentEl))
            return new ToolResult(false, "CreatePage requires 'title' and 'content' arguments.");

        var title = titleEl.GetString() ?? "";
        var content = contentEl.GetString() ?? "";
        var type = arguments.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "wiki" : "wiki";

        var pageId = await memory.CreatePageAsync(title, content, type, cancellationToken);
        return new ToolResult(true, $"Page '{title}' created with ID '{pageId}'.",
            new Dictionary<string, object?> { ["pageId"] = pageId });
    }
}