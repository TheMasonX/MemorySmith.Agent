namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Llm;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// <see cref="IChatInterpreter"/> that combines LLM-powered evaluation with
/// deterministic fast-path fallback.
///
/// Evaluation pipeline for each incoming message:
///   1. Truncate message at <see cref="ChatOptions.MaxMessageLength"/> characters.
///   2. Distance gate: player far away and not naming bot = NotAddressed.
///   3. 4 deterministic fast-paths: stop/cancel, status, inventory, help — never touch LLM.
///   4. Rate-limit check: per-player and global window.
///   5. LLM call: ILlmProvider.CompleteAsync with a structured JSON prompt (IntentDraft schema).
///   6. JSON parse: extract ChatInterpretation from IntentDraft response.
///      Confidence &lt; threshold + ClarificationQuestion → return Unknown + ask clarifying question.
///   7. Fallback: use pattern-matcher result if LLM fails.
///   8. Truncation recovery: TryParseTruncatedJson extracts intent from cut-off JSON. (Sprint 20)
///
/// Sprint 35 P1-B: removed fast-path for ChatIntentType.CreateGoal and NavigateTo.
/// All non-trivial chat now reaches the LLM. Only stop/cancel, status, inventory, help
/// are fast-pathed deterministically (safe, zero-risk operations).
///
/// Sprint 35 P1-A: LLM prompt requests IntentDraft schema (confidence, clarificationQuestion).
/// Sprint 35 P1-C: BuildSystemPrompt enriched with inventory, HP, active goal, tool names.
/// Sprint 36 P1-C: BuildSystemPrompt accepts registeredToolNames — tool names now injected
///   from ToolDispatcher.All at construction time, making them available to the LLM.
///
/// AGENTS.md CRITICAL rule: parsers never create goals.
/// IntentDraft has no GoalName field. The mapping Intent→GoalName is done in
/// AgentBackgroundService.IntentDraftToGoal (Sprint 35 transition layer).
/// Sprint 36: IntentDraftToGoal moves to IntentManager.
/// </summary>
public sealed class LlmChatInterpreter(
    ILlmProvider provider,
    ChatInterpreter patternFallback,
    ChatRateLimiter rateLimiter,
    ChatOptions options,
    ChatHistory? history = null,
    ILogger<LlmChatInterpreter>? logger = null,
    // Sprint 36 P1-C: registered tool names from ToolDispatcher.All, injected at DI time.
    // Passed to BuildSystemPrompt so the LLM knows what tools are available.
    IReadOnlyList<string>? registeredToolNames = null) : IChatInterpreter
{
    // -- IChatInterpreter ------------------------------------------------------------------

    public async Task<ChatInterpretation> InterpretAsync(
        string username, string message, string botName,
        int onlinePlayers, Position botPosition, Position? playerPosition,
        WorldState state, CancellationToken ct = default)
    {
        // 1. Enforce max length before any processing
        var effective = message.Length > options.MaxMessageLength
            ? message[..options.MaxMessageLength]
            : message;

        // Sprint 4b: record the incoming player message in conversation history
        history?.Record(username, effective);

        // 2. Get the pattern-matcher's view -- used as fallback for fast-paths
        var quick = await patternFallback.InterpretAsync(
            username, effective, botName, onlinePlayers,
            botPosition, playerPosition, state, ct);

        // 3. Distance gate
        if (quick.IntentType == ChatIntentType.NotAddressed
            && playerPosition is not null
            && Distance(botPosition, playerPosition) > options.MaxResponseDistanceBlocks)
        {
            return quick;
        }

        // Sprint 35 P1-B: fast-path ONLY for the 4 safe deterministic operations.
        // CreateGoal and NavigateTo are removed — all non-trivial chat reaches the LLM.
        // This enforces the "LLM owns intent" architecture locked in Sprint 35.
        if (quick.IntentType is ChatIntentType.CancelGoal
                              or ChatIntentType.QueryHelp
                              or ChatIntentType.QueryStatus)
        {
            return quick;
        }

        // 4. Rate-limit check
        if (!provider.IsAvailable)
        {
            logger?.LogInformation("[llm] {Provider} not available (LlmEnabled={Enabled}, provider={Provider}) -- falling back to pattern for <{Username}>",
                provider.ProviderName, options.LlmEnabled, options.LlmProvider, username);
            return quick;
        }

        if (!rateLimiter.TryAcquire(username, out var wait))
        {
            logger?.LogInformation("[llm] rate-limited for <{Username}> (wait={Wait}s) -- falling back to pattern",
                username, wait.TotalSeconds.ToString("F1"));
            return quick;
        }

        // 5. LLM call — Sprint 35 P1-C: enrich system prompt with inventory, HP, tools
        //               Sprint 36 P1-C: pass registeredToolNames so LLM knows available tools
        var currentGoal = state.Facts.TryGetValue("currentGoal", out var cg) && cg is string s ? s : null;
        logger?.LogInformation("[llm] calling {Provider} ({Model}) for <{Username}> '{Message}'",
            provider.ProviderName, options.LlmModel, username,
            effective.Length > 60 ? effective[..60] : effective);
        var raw = await provider.CompleteAsync(
            BuildSystemPrompt(botName, botPosition, state, currentGoal, onlinePlayers, registeredToolNames),
            $"{username} says: \"{effective}\"",
            ct);

        if (raw is null)
        {
            logger?.LogWarning("[llm] {Provider} returned null -- falling back to pattern for <{Username}>",
                provider.ProviderName, username);
            return quick;
        }

        // 6. Parse LLM response (IntentDraft schema)
        var llmResult = ParseDecision(raw, options.LlmConfidenceThreshold, logger);
        if (llmResult is null)
            logger?.LogWarning("[llm] failed to parse JSON from {Provider} response: '{Content}'",
                provider.ProviderName, raw.Length > 100 ? raw[..100] : raw);

        // If low confidence with clarifying question — log it; bot.chat will be called by AgentBackgroundService
        if (llmResult?.IntentType == ChatIntentType.Unknown && llmResult.Response.Length > 0)
            logger?.LogInformation("[llm] low-confidence — clarification requested: {Question}", llmResult.Response);

        return llmResult ?? quick;
    }

    public void RecordBotSpoke() => patternFallback.RecordBotSpoke();

    // -- Prompt construction ---------------------------------------------------------------

    /// <summary>
    /// Sprint 35 P1-C: enriched system prompt with inventory, HP/food, active goal,
    /// last tool error, and available tool names. Requests IntentDraft JSON schema
    /// with confidence and clarificationQuestion fields.
    ///
    /// Sprint 36 P1-C: <paramref name="toolNames"/> parameter injects comma-separated
    /// registered tool names from ToolDispatcher.All so the LLM knows what tools are
    /// available when deciding intent.
    /// </summary>
    private static string BuildSystemPrompt(
        string botName, Position botPos, WorldState state,
        string? goal, int onlinePlayers,
        IReadOnlyList<string>? toolNames = null)
    {
        // Sprint 35 P1-C: build inventory summary (non-zero items, desc order, max 8)
        var invSummary = state.Inventory.Count == 0
            ? "empty"
            : string.Join(", ", state.Inventory
                .Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .Take(8)
                .Select(kv => $"{kv.Key}:{kv.Value}"));

        var goalStatus = goal is not null ? $"pursuing: {goal}" : "idle";
        var health = state.Health;
        var food = state.Food;

        var basePrompt = $$"""
        You are {{botName}}, an autonomous Minecraft bot at ({{botPos.X}},{{botPos.Y}},{{botPos.Z}}).
        Status: {{goalStatus}}. HP: {{health}}/20. Food: {{food}}/20. Players online: {{onlinePlayers}}.
        Inventory: {{invSummary}}.

        Decide if the next message is for you and what to do.
        Reply ONLY with valid JSON — no markdown, no prose:

        {
          "addressed": "yes" | "maybe" | "no",
          "intent": "gather" | "build" | "craft" | "navigate" | "cancel" | "status"
                  | "help" | "conversation" | "clarify" | "ignore",
          "item": "<minecraft_id or null>",
          "blueprint": "<blueprint_id or null>",
          "count": <integer or null>,
          "x": <integer or null>,
          "y": <integer or null>,
          "z": <integer or null>,
          "confidence": <0.0-1.0>,
          "clarificationQuestion": "<question to ask if confidence is low, or null>",
          "response": "<in-game reply, max 50 words, empty if intent is ignore>"
        }

        Rules:
        "yes" when your name is used or only 1 player is online.
        "maybe" when it could be a command but your name is not mentioned.
        "no" when players are talking to each other, not you.
        "conversation" when the player is just chatting (greetings, questions, small-talk).
        "clarify" when intent is ambiguous — set clarificationQuestion, confidence < 0.6.
        Use Minecraft item IDs without namespace prefix (oak_log, cobblestone, diamond).
        For inventory/what-do-you-have → intent "status", list inventory in response.
        """;

        // Sprint 36 P1-C: append registered tool names so the LLM knows what's available.
        if (toolNames is { Count: > 0 })
            return basePrompt + $"\nRegistered tools: {string.Join(", ", toolNames)}.";
        return basePrompt;
    }

    // -- Response parsing ------------------------------------------------------------------

    private static readonly Regex CodeFenceRegex =
        new(@"```(?:json)?\s*(?<body>[\s\S]*?)```", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BraceRegex =
        new(@"\{[\s\S]*\}", RegexOptions.Compiled);

    /// <summary>
    /// Sprint 35 P1-B: ParseDecision now reads the IntentDraft schema response.
    /// Handles confidence threshold — low confidence + clarificationQuestion → Unknown.
    /// The GoalName mapping (gather → "GatherItem:item") remains here as the Sprint 35
    /// transition layer. Sprint 36: moves to IntentManager.IntentDraftToGoal().
    /// </summary>
    private static ChatInterpretation? ParseDecision(
        string content, double confidenceThreshold,
        ILogger<LlmChatInterpreter>? logger)
    {
        try
        {
            var json = CodeFenceRegex.IsMatch(content)
                ? CodeFenceRegex.Match(content).Groups["body"].Value
                : content;

            var m = BraceRegex.Match(json);
            if (!m.Success)
            {
                // Sprint 20: try to salvage intent from truncated JSON (no closing brace).
                return TryParseTruncatedJson(json);
            }

            using var doc  = JsonDocument.Parse(m.Value);
            var root       = doc.RootElement;
            var addressed  = GetStr(root, "addressed") ?? "no";

            if (string.Equals(addressed, "no", StringComparison.OrdinalIgnoreCase))
                return new ChatInterpretation(ChatIntentType.NotAddressed);

            var isUncertain = string.Equals(addressed, "maybe", StringComparison.OrdinalIgnoreCase);
            var intent      = GetStr(root, "intent") ?? "ignore";
            var response    = GetStr(root, "response") ?? string.Empty;

            // Sprint 35 P1-A: read confidence and clarificationQuestion from IntentDraft schema
            var confidence = GetDouble(root, "confidence") ?? 1.0;
            var clarifyQ   = GetStr(root, "clarificationQuestion");

            // Low confidence → clarify (bot sends the question as its response)
            if (confidence < confidenceThreshold && !string.IsNullOrWhiteSpace(clarifyQ))
            {
                logger?.LogDebug("[llm] confidence={Confidence:F2} < threshold={Threshold:F2} — requesting clarification",
                    confidence, confidenceThreshold);
                return new ChatInterpretation(ChatIntentType.Unknown, Response: clarifyQ);
            }

            // Sprint 35 transition layer: map IntentDraft intent → GoalName.
            // AGENTS.md CRITICAL rule: parsers never create goals.
            // This mapping stays in the interpreter only for Sprint 35; moves to IntentManager in Sprint 36.
            string? goalName = null;
            IReadOnlyDictionary<string, object?>? parameters = null;

            switch (intent.ToLowerInvariant())
            {
                case "gather":
                    var item = GetStr(root, "item");
                    if (item is not null)
                    {
                        goalName   = $"GatherItem:{item}";
                        parameters = new Dictionary<string, object?> { ["count"] = GetInt(root, "count") ?? 10 };
                    }
                    break;
                case "craft":
                    var craftItem = GetStr(root, "item");
                    if (craftItem is not null)
                    {
                        goalName   = $"CraftItem:{craftItem}";
                        parameters = new Dictionary<string, object?> { ["count"] = GetInt(root, "count") ?? 1 };
                    }
                    break;
                case "build":
                    var bp = GetStr(root, "blueprint");
                    if (bp is not null)
                    {
                        goalName = $"Build:{bp}";
                        var bx = GetInt(root, "x");
                        var by = GetInt(root, "y");
                        var bz = GetInt(root, "z");
                        if (bx is not null && by is not null && bz is not null)
                        {
                            parameters = new Dictionary<string, object?>
                            {
                                ["originX"] = bx,
                                ["originY"] = by,
                                ["originZ"] = bz,
                            };
                        }
                    }
                    break;
                case "navigate":
                    var x = GetInt(root, "x");
                    var y = GetInt(root, "y");
                    var z = GetInt(root, "z");
                    if (x is not null && y is not null && z is not null)
                    {
                        goalName   = "MoveTo";
                        parameters = new Dictionary<string, object?> { ["x"] = x, ["y"] = y, ["z"] = z };
                    }
                    break;
            }

            var intentType = intent.ToLowerInvariant() switch
            {
                "gather" or "build" or "craft" => ChatIntentType.CreateGoal,
                "cancel"                        => ChatIntentType.CancelGoal,
                "status"                        => ChatIntentType.QueryStatus,
                "help"                          => ChatIntentType.QueryHelp,
                "navigate"                      => ChatIntentType.NavigateTo,
                "conversation"                  => ChatIntentType.Chat,
                "clarify"                       => ChatIntentType.Unknown,
                _                               => isUncertain ? ChatIntentType.Unknown : ChatIntentType.NotAddressed,
            };

            return new ChatInterpretation(intentType, goalName, parameters, response);
        }
        catch { return null; }
    }

    private static string? GetStr(JsonElement root, string key)
    {
        if (root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString();
        return null;
    }

    private static int? GetInt(JsonElement root, string key)
    {
        if (root.TryGetProperty(key, out var el))
        {
            if (el.ValueKind == JsonValueKind.Number) return el.GetInt32();
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var i)) return i;
        }
        return null;
    }

    private static double? GetDouble(JsonElement root, string key)
    {
        if (root.TryGetProperty(key, out var el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d)) return d;
            if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s)) return s;
        }
        return null;
    }

    /// <summary>
    /// Sprint 20: best-effort extraction from truncated JSON missing the closing brace.
    /// Sprint 21 P1-C: extended to extract item/count from truncated gather/build JSON.
    /// </summary>
    private static ChatInterpretation? TryParseTruncatedJson(string json)
    {
        try
        {
            var addressedM = Regex.Match(json,
                @"""addressed""\s*:\s*""(?<v>[^""]+)""", RegexOptions.IgnoreCase);
            var intentM = Regex.Match(json,
                @"""intent""\s*:\s*""(?<v>[^""]+)""", RegexOptions.IgnoreCase);
            if (!addressedM.Success) return null;

            var addressed = addressedM.Groups["v"].Value;
            if (string.Equals(addressed, "no", StringComparison.OrdinalIgnoreCase))
                return new ChatInterpretation(ChatIntentType.NotAddressed);

            var intent = intentM.Success ? intentM.Groups["v"].Value : "ignore";

            string? goalName = null;
            IReadOnlyDictionary<string, object?>? parameters = null;

            switch (intent.ToLowerInvariant())
            {
                case "gather":
                {
                    var itemM  = Regex.Match(json, @"""item""\s*:\s*""(?<v>[^""]+)""",  RegexOptions.IgnoreCase);
                    var countM = Regex.Match(json, @"""count""\s*:\s*(?<v>\d+)",         RegexOptions.IgnoreCase);
                    if (itemM.Success)
                    {
                        goalName   = $"GatherItem:{itemM.Groups["v"].Value}";
                        var cnt    = countM.Success && int.TryParse(countM.Groups["v"].Value, out var c) ? c : 10;
                        parameters = new Dictionary<string, object?> { ["count"] = cnt };
                    }
                    break;
                }
                case "build":
                {
                    var bpM = Regex.Match(json, @"""blueprint""\s*:\s*""(?<v>[^""]+)""", RegexOptions.IgnoreCase);
                    if (bpM.Success)
                        goalName = $"Build:{bpM.Groups["v"].Value}";
                    break;
                }
                case "craft":
                {
                    var itemM  = Regex.Match(json, @"""item""\s*:\s*""(?<v>[^""]+)""",  RegexOptions.IgnoreCase);
                    var countM = Regex.Match(json, @"""count""\s*:\s*(?<v>\d+)",         RegexOptions.IgnoreCase);
                    if (itemM.Success)
                    {
                        goalName   = $"CraftItem:{itemM.Groups["v"].Value}";
                        var cnt    = countM.Success && int.TryParse(countM.Groups["v"].Value, out var c) ? c : 1;
                        parameters = new Dictionary<string, object?> { ["count"] = cnt };
                    }
                    break;
                }
            }

            var intentType = intent.ToLowerInvariant() switch
            {
                "gather" or "build" or "craft" => goalName is not null ? ChatIntentType.CreateGoal : ChatIntentType.Unknown,
                "cancel"                        => ChatIntentType.CancelGoal,
                "status"                        => ChatIntentType.QueryStatus,
                "help"                          => ChatIntentType.QueryHelp,
                "conversation"                  => ChatIntentType.Chat,
                "clarify"                       => ChatIntentType.Unknown,
                "ignore"                        => ChatIntentType.NotAddressed,
                _                               => ChatIntentType.Unknown,
            };

            var responseM = Regex.Match(json,
                @"""response""\s*:\s*""(?<v>[^""\\]*(?:\\.[^""\\]*)*)""");
            var response = responseM.Success ? responseM.Groups["v"].Value : string.Empty;

            return new ChatInterpretation(intentType, goalName, parameters, response);
        }
        catch { return null; }
    }

    private static double Distance(Position a, Position b)
    {
        var dx = a.X - b.X; var dy = a.Y - b.Y; var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
