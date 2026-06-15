namespace Agent.Tools;

using Agent.Core;

/// <summary>
/// Registry of all tools available to the LLM via MCP.
/// Tools are registered at startup and exposed as a typed JSON Schema catalog.
/// The LLM calls tools by name; the ToolEngine validates and dispatches.
/// </summary>
public interface IToolRegistry
{
    void Register(ITool tool);
    ITool? Get(string name);
    IReadOnlyList<ITool> All { get; }
}
