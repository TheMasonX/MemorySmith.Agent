namespace Agent.Tools;

using Agent.Core;
using Microsoft.Extensions.Logging;
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
///
/// Sprint 36 P0-B: CallWithOutcomeAsync wraps CallAsync and produces an ActionOutcome.
/// Sprint 37 P0-B: CallAsync no longer emits its own success/failure journal entry;
/// callers using CallWithOutcomeAsync call _journal?.LogOutcome(outcome) explicitly.
///
/// Sprint 36 P1-C: RegisteredNames exposes all registration keys (including aliases) in
/// sorted order for deterministic LLM prompt injection.
///
/// Sprint 39 P3: ValidateAgainstSchema extended with deeper constraints:
///   minimum/maximum for numeric properties, enum for allowed-value sets,
///   minLength/maxLength for string properties.
/// </summary>
public sealed class ToolDispatcher : IToolCaller
{
    private readonly ConcurrentDictionary<string, ITool> _tools =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IAgentJournal? _journal;
    private readonly ILogger<ToolDispatcher>? _logger;

    public ToolDispatcher(IAgentJournal? journal = null, ILogger<ToolDispatcher>? logger = null)
    {
        _journal = journal;
        _logger  = logger;
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
    ///
    /// Sprint 38 P4-C: LogWarning when overwriting an existing registration to aid
    /// diagnostics in production (double-registration is almost always a bug).
    /// </summary>
    public void Register(string name, ITool tool)
    {
        if (_tools.ContainsKey(name))
            _logger?.LogWarning("ToolDispatcher] Register: overwriting existing tool '{Name}' " +
                "with {NewTool}. Check for duplicate registrations.", name, tool.Name);
        _tools[name] = tool;
    }

    public ITool? Get(string name) =>
        _tools.TryGetValue(name, out var t) ? t : null;

    public IReadOnlyList<ITool> All => [.. _tools.Values];

    /// <summary>
    /// Sprint 36 P1-C: Returns all registered names (including aliases) in
    /// case-insensitive alphabetical order. Use this instead of
    /// <see cref="All"/> when building an LLM prompt — <see cref="All"/>
    /// iterates dict values in nondeterministic order and does not surface
    /// alias keys (e.g. "Status" is registered as an alias for GetStatusTool
    /// but does not appear in <see cref="All"/> because the dict stores only
    /// one tool instance per value, not per registration key).
    /// </summary>
    public IReadOnlyList<string> RegisteredNames =>
        [.. _tools.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)];

    // ── Dispatch ─────────────────────────────────────────────────────────────

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
            // TSK-0114: preserve structured exception metadata (type, stack, inner)
            _logger?.LogWarning(ex, "Tool '{ToolName}' threw {ExceptionType}: {Message}",
                toolName, ex.GetType().Name, ex.Message);
            var details = new Dictionary<string, object?>
            {
                ["exceptionType"] = ex.GetType().Name,
                ["message"] = ex.Message,
                ["stackTrace"] = ex.StackTrace,
                ["innerException"] = ex.InnerException?.Message,
            };
            var exEntry = new JournalEntry(
                DateTimeOffset.UtcNow,
                JournalEntryType.ActionFailed,
                $"Tool '{toolName}' threw {ex.GetType().Name}: {ex.Message}",
                details);
            _journal?.Log(exEntry);
            return new ToolResult(false,
                $"Tool '{toolName}' failed ({ex.GetType().Name}): {ex.Message}");
        }

        // Sprint 37 P0-B: success/failure journal entry removed here. Callers using
        // CallWithOutcomeAsync (e.g. DispatchActionsAsync) call _journal?.LogOutcome(outcome)
        // explicitly in the outer dispatch loop. This eliminates the double-journal issue
        // identified in Sprint 36 audit finding #4 while keeping validation-failure and
        // exception entries (which are separate semantics and must remain).
        return result;
    }

    /// <summary>
    /// Sprint 36 P0-B: Executes the named tool and wraps the result in an
    /// <see cref="ActionOutcome"/> that is also recorded in the journal via
    /// <see cref="IAgentJournal.LogOutcome"/>.
    ///
    /// Use this overload when recovery, replanning, or LLM evaluation needs
    /// structured outcome data (effects, observation summary). Callers that only
    /// need the raw <see cref="ToolResult"/> can continue using <see cref="CallAsync"/>.
    ///
    /// <para>
    /// The returned ActionOutcome uses factory helpers:
    /// <c>ActionOutcome.Succeeded</c> for success; <c>ActionOutcome.Failed</c> for failure.
    /// Callers that need richer outcomes (e.g. <c>ActionOutcome.Collected</c>) should
    /// build the outcome from the ToolResult and the world event that follows.
    /// </para>
    ///
    /// <para>
    /// Sprint 37 P0-B: CallAsync no longer emits its own ActionCompleted / ActionFailed
    /// entry (that entry was removed to prevent double-logging). The caller
    /// (<c>DispatchActionsAsync</c>) calls <c>_journal?.LogOutcome(outcome)</c> explicitly
    /// after this method returns. This method itself does NOT call LogOutcome.
    /// </para>
    /// </summary>
    public async Task<(ToolResult Result, ActionOutcome Outcome)> CallWithOutcomeAsync(
        Guid goalId,
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await CallAsync(toolName, arguments, cancellationToken);

        var outcome = result.Success
            ? ActionOutcome.Succeeded(goalId, toolName, result.Message ?? "Success")
            : ActionOutcome.Failed(goalId, toolName, result.Message ?? "Failed");

        // Sprint 37 P0-B: CallAsync no longer emits its own ActionCompleted / ActionFailed
        // journal entry (removed to prevent double-logging with DispatchActionsAsync's
        // explicit _journal?.LogOutcome(outcome) call). Do NOT add LogOutcome here.
        // The outer dispatch loop logs the structured outcome explicitly.
        return (result, outcome);
    }

    // ── Schema validation ─────────────────────────────────────────────────────
    //
    // Lightweight JSON Schema validator covering the subset used by tool schemas.
    // Sprint 5 baseline: type (object), properties (name → { type, description }), required.
    // Sprint 39 P3 additions: minimum/maximum (numbers), enum (any type), minLength/maxLength (strings).

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

            if (schemaProps.TryGetProperty(argProp.Name, out var propSchema))
            {
                // Type check (Sprint 5 baseline)
                if (propSchema.TryGetProperty("type", out var expectedType))
                {
                    var typeError = CheckType(argProp.Name, argProp.Value, expectedType.GetString()!);
                    if (typeError is not null) return typeError;
                }

                // Sprint 39 P3: deeper constraint checks (minimum, maximum, enum, minLength, maxLength)
                var constraintError = CheckConstraints(argProp.Name, argProp.Value, propSchema);
                if (constraintError is not null) return constraintError;
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

    /// <summary>
    /// Sprint 39 P3: Validates deeper JSON Schema constraints beyond basic type checking.
    /// Supports: minimum/maximum (numeric), enum (any type), minLength/maxLength (string).
    /// Returns null on success, or an error message on failure.
    /// </summary>
    private static string? CheckConstraints(string name, JsonElement value, JsonElement propSchema)
    {
        // minimum / maximum — is only applicable to numeric values
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numVal))
        {
            if (propSchema.TryGetProperty("minimum", out var minProp) &&
                numVal < minProp.GetDouble())
                return $"Property '{name}' must be >= {minProp.GetDouble()}, got {value.GetRawText()}.";

            if (propSchema.TryGetProperty("maximum", out var maxProp) &&
                numVal > maxProp.GetDouble())
                return $"Property '{name}' must be <= {maxProp.GetDouble()}, got {value.GetRawText()}.";
        }

        // enum — compares raw JSON text; handles strings, numbers, and booleans uniformly
        if (propSchema.TryGetProperty("enum", out var enumProp))
        {
            var argRaw = value.GetRawText();
            var matched = enumProp.EnumerateArray().Any(e => e.GetRawText() == argRaw);
            if (!matched)
            {
                var allowed = string.Join(", ", enumProp.EnumerateArray().Select(e => e.GetRawText()));
                return $"Property '{name}' must be one of [{allowed}], got {argRaw}.";
            }
        }

        // minLength / maxLength — only applicable to string values
        if (value.ValueKind == JsonValueKind.String)
        {
            var strVal = value.GetString()!;
            if (propSchema.TryGetProperty("minLength", out var minLenProp) &&
                strVal.Length < minLenProp.GetInt32())
                return $"Property '{name}' minLength is {minLenProp.GetInt32()}, got length {strVal.Length}.";

            if (propSchema.TryGetProperty("maxLength", out var maxLenProp) &&
                strVal.Length > maxLenProp.GetInt32())
                return $"Property '{name}' maxLength is {maxLenProp.GetInt32()}, got length {strVal.Length}.";
        }

        return null;
    }
}
