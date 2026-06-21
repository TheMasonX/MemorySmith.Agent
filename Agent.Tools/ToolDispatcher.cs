namespace Agent.Tools;

using Agent.Core;
using System.Collections.Concurrent;
using System.Text.Json;

/// <summary>
/// Deep module: registers tools, resolves by name, and executes with schema validation.
///
/// Replaces the former ToolRegistry + ToolEngine pair (two shallow modules that the
/// deletion test confirmed earned no separate interface depth). The caller surface shrinks
/// from four types (IToolRegistry, IToolCaller, ToolRegistry, ToolEngine) to two
/// (IToolCaller stays for DI; ToolDispatcher is the single concrete class).
///
/// Sprint 5: schema validation is now enforced at the dispatch boundary. Every tool's
/// InputSchema is validated before execution — this is the safety boundary between
/// untrusted arguments (from LLMs or REST API) and the tool layer.
///
/// Sprint 25 P0-C: ExecuteAsync is now wrapped in try/catch — tool exceptions produce
/// ToolResult(false, ...) instead of propagating. Integer validation uses TryGetInt32
/// to correctly reject scientific notation (e.g. "1e5").
/// </summary>
public sealed class ToolDispatcher : IToolCaller
{
    private readonly ConcurrentDictionary<string, ITool> _tools =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IAgentJournal? _journal;

    public ToolDispatcher(IAgentJournal? journal = null)
    {
        _journal = journal;
    }

    // ── Registration ──────────────────────────────────────────────────────────

    public void Register(ITool tool) => _tools[tool.Name] = tool;

    /// <summary>
    /// Sprint 25 P0-B: Register a tool under an explicit alias name.
    /// Used to register GetStatusTool under both "GetStatus" and "Status" after
    /// deleting the duplicate StatusTool class.
    ///
    /// Collision semantics: if <paramref name="name"/> is already registered, the
    /// existing entry is silently overwritten (<c>_tools[name] = tool</c>). This is
    /// intentional for alias registration. Callers that need to detect collisions
    /// should check <see cref="Get"/> before calling this overload.
    /// </summary>
    public void Register(string name, ITool tool) => _tools[name] = tool;

    public ITool? Get(string name) =>
        _tools.TryGetValue(name, out var t) ? t : null;

    public IReadOnlyList<ITool> All => [.. _tools.Values];

    // ── Dispatch ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the named tool, validates arguments against its InputSchema,
    /// and executes. Returns a failure result (not throws) when the tool is
    /// unknown or the arguments are malformed — the caller's dispatch loop
    /// decides whether to retry or abandon.
    /// </summary>
    public async Task<ToolResult> CallAsync(
        string toolName, JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            var unknownEntry = new JournalEntry(
                DateTimeOffset.UtcNow,
                JournalEntryType.ActionFailed,
                $"Tool '{toolName}' not registered");
            _journal?.Log(unknownEntry);
            return new ToolResult(false, $"Tool '{toolName}' is not registered.");
        }

        // Sprint 5: validate arguments against the tool's declared InputSchema
        var schema = tool.InputSchema;
        if (schema.ValueKind != JsonValueKind.Undefined)
        {
            var validationError = ValidateAgainstSchema(arguments, schema);
            if (validationError is not null)
            {
                var failEntry = new JournalEntry(
                    DateTimeOffset.UtcNow,
                    JournalEntryType.ActionFailed,
                    $"Validation failed for '{toolName}': {validationError}");
                _journal?.Log(failEntry);
                return new ToolResult(false, $"Schema validation failed for '{toolName}': {validationError}");
            }
        }

        // Sprint 25 P0-C: wrap ExecuteAsync in try/catch so tool exceptions produce
        // ToolResult failures instead of propagating up to the dispatch loop.
        // This is the safety boundary: ToolResult is the ONLY failure channel.
        ToolResult result;
        try
        {
            result = await tool.ExecuteAsync(arguments, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Let cancellation propagate — the caller manages cancellation tokens.
            throw;
        }
        catch (Exception ex)
        {
            var exEntry = new JournalEntry(
                DateTimeOffset.UtcNow,
                JournalEntryType.ActionFailed,
                $"Tool '{toolName}' threw: {ex.Message}");
            _journal?.Log(exEntry);
            return new ToolResult(false, $"Tool '{toolName}' threw: {ex.Message}");
        }

        var entry = new JournalEntry(
            DateTimeOffset.UtcNow,
            result.Success ? JournalEntryType.ActionCompleted : JournalEntryType.ActionFailed,
            result.Success
                ? $"Tool '{toolName}' succeeded"
                : $"Tool '{toolName}' failed: {result.Message}");
        _journal?.Log(entry);

        return result;
    }

    // ── Schema validation ─────────────────────────────────────────────────────
    //
    // Lightweight JSON Schema validator covering the subset used by tool schemas:
    // type (object), properties (name → { type, description }), required array.
    // This is intentionally minimal — it validates the guard-rail subset, not full
    // JSON Schema Draft support. All 12 registered tools use this subset.

    /// <summary>
    /// Validates <paramref name="args"/> against a JSON Schema object.
    /// Returns null on success, or an error message string on failure.
    /// </summary>
    internal static string? ValidateAgainstSchema(JsonElement args, JsonElement schema)
    {
        // Schema root must declare type "object"
        if (schema.TryGetProperty("type", out var rootType) &&
            rootType.GetString() != "object")
            return $"Schema root type must be 'object', got '{rootType.GetString()}'.";

        // If args is not an object, it can never satisfy a "type": "object" schema
        if (args.ValueKind != JsonValueKind.Object)
            return "Arguments must be a JSON object.";

        // Gather the set of expected property names
        if (!schema.TryGetProperty("properties", out var schemaProps))
            return null; // no property constraints — accept anything

        var knownProps = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in schemaProps.EnumerateObject())
            knownProps.Add(prop.Name);

        // Check that all provided properties are declared in the schema
        foreach (var argProp in args.EnumerateObject())
        {
            if (!knownProps.Contains(argProp.Name))
                return $"Unexpected property '{argProp.Name}' is not declared in the tool schema.";

            if (schemaProps.TryGetProperty(argProp.Name, out var propSchema) &&
                propSchema.TryGetProperty("type", out var expectedType))
            {
                var error = CheckType(argProp.Name, argProp.Value, expectedType.GetString()!);
                if (error is not null) return error;
            }
        }

        // Check that all required properties are present
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var req in required.EnumerateArray())
            {
                var name = req.GetString()!;
                if (!args.TryGetProperty(name, out _))
                    return $"Missing required property '{name}'.";
            }
        }

        return null;
    }

    private static string? CheckType(string name, JsonElement value, string expected)
    {
        return expected switch
        {
            // Sprint 25 P0-C: use TryGetInt32 instead of Contains('.') for integer validation.
            // Contains('.') missed scientific notation like "1e5" which is a valid JSON number
            // but not a valid integer for tool arguments. TryGetInt32 correctly handles all
            // JSON numeric representations including scientific notation.
            "integer" when value.ValueKind != JsonValueKind.Number ||
                          !value.TryGetInt32(out _) => $"Property '{name}' must be an integer, got '{value.GetRawText()}'.",
            "number" when value.ValueKind != JsonValueKind.Number => $"Property '{name}' must be a number, got '{value.GetRawText()}'.",
            "string" when value.ValueKind != JsonValueKind.String => $"Property '{name}' must be a string, got '{value.GetRawText()}'.",
            "boolean" when value.ValueKind is not JsonValueKind.True and not JsonValueKind.False => $"Property '{name}' must be a boolean, got '{value.GetRawText()}'.",
            "object" when value.ValueKind != JsonValueKind.Object => $"Property '{name}' must be an object, got '{value.GetRawText()}'.",
            "array" when value.ValueKind != JsonValueKind.Array => $"Property '{name}' must be an array, got '{value.GetRawText()}'.",
            _ => null, // unknown type — accept (forward-compatible)
        };
    }
}
