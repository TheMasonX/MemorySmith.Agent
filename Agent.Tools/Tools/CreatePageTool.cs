namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Creates or updates a page in the world knowledge base to record in-world
/// observations, block discoveries, or exploration notes.
/// <para>
/// Sprint 23 P0-B: routed to the world-keyed <see cref="IMemoryGateway"/>.
/// World observations are stored separately from agent codebase documentation,
/// keeping the agent KB clean for sprint docs, design notes, and code-level
/// references while world data accumulates in its own MemorySmith instance.
/// </para>
/// Sprint 30 P0-B: rewritten ExecuteAsync to JsonElement signature for ITool compliance.
/// ActionData-based signature was restored when file was decoded in Sprint 28
/// but the interface had already changed in Sprint 5.
/// </summary>
public sealed class CreatePageTool : ITool
{
    private readonly IMemoryGateway _memory;

    public CreatePageTool(IMemoryGateway memory) => _memory = memory;

    public string Name => "CreatePage";

    public string Description => "Creates or updates a page in the world knowledge base to record in-world observations, block discoveries, or exploration notes. Routes to the world KB instance (see WorldKbUrl in appsettings). Use CreatePage for world data; use GetPage for agent knowledge base retrieval.";

    public JsonElement InputSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "The page title" },
            "body": { "type": "string", "description": "The page content in markdown" },
            "content": { "type": "string", "description": "Alias for body content in markdown" },
            "type": { "type": "string", "description": "Optional page type (e.g. 'observation', 'note')" }
          },
          "required": ["title"]
        }
        """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var title = arguments.TryGetProperty("title", out var t) ? t.GetString()
                    : throw new ArgumentException("CreatePage requires 'title' parameter.");
        var body = arguments.TryGetProperty("body", out var b) && b.ValueKind != JsonValueKind.Null
            ? b.GetString()
            : arguments.TryGetProperty("content", out var c) && c.ValueKind != JsonValueKind.Null
                ? c.GetString()
                : throw new ArgumentException("CreatePage requires 'body' or 'content' parameter.");
        var type  = arguments.TryGetProperty("type",  out var ty) ? ty.GetString() ?? string.Empty : string.Empty;

        var page = await _memory.CreatePageAsync(title!, body!, type, ct).ConfigureAwait(false);
        return new ToolResult(
            true,
            $"Page '{title}' created or updated.",
            new Dictionary<string, object?> { ["page"] = page });
    }
}
