namespace Agent.Tools;

using Agent.Core;
using Agent.Memory;

/// <summary>
/// Creates or updates a page in the world knowledge base to record in-world
/// observations, block discoveries, or exploration notes.
/// <para>
/// Sprint 23 P0-B: routed to the world-keyed <see cref="IMemoryGateway"/>.
/// World observations are stored separately from agent codebase documentation,
/// keeping the agent KB clean for sprint docs, design notes, and code-level
/// references while world data accumulates in its own MemorySmith instance.
/// </para>
/// </summary>
public sealed class CreatePageTool : ITool
{
    private readonly IMemoryGateway _memory;

    public CreatePageTool(IMemoryGateway memory) => _memory = memory;

    public string Name => "CreatePage";

    public string Description => "Creates or updates a page in the world knowledge base to record in-world observations, block discoveries, or exploration notes. Routes to the world KB instance (see WorldKbUrl in appsettings). Use CreatePage for world data; use GetPage for agent knowledge base retrieval.";

    public async Task<ToolResult> ExecuteAsync(ActionData action, CancellationToken ct)
    {
        var title = action.GetString("title")
                    ?? throw new ArgumentException("CreatePage requires 'title' parameter.");
        var body  = action.GetString("body")
                    ?? throw new ArgumentException("CreatePage requires 'body' parameter.");
        var type  = action.GetString("type");

        var page = await _memory.CreatePageAsync(title, body, type, ct).ConfigureAwait(false);
        return ToolResult.Ok(new { page });
    }
}
