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
/// Sprint 37 P1-B: ParseDecision now delegates the intent→goal mapping to
///   <see cref="IntentManager.BuildGoalRequest"/> instead of implementing its own switch.
///   AGENTS.md CRITICAL rule enforced: parsers never create goals.
///   IntentDraft has no GoalName field. The mapping Intent→GoalName is performed by
///   IntentManager, not by the parser. GoalFactory creates the actual IGoal.
///
/// Sprint 38: InterpretAsync should be changed to return IntentDraft directly, removing
///   ChatInterpretation from the interpreter surface entirely.
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
    IReadOnlyList<string>? registeredToolNames = null,
    // Sprint 37 P1-B: intent→goal mapping delegate. Null falls back to local switch in
    // ParseDecision for backward compatibility (tests that don't inject IntentManager).
    IntentManager? intentManager = null) : IChatInterpreter
{
    private readonly IntentManager? _intentManager = intentManager;

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
        // Sprint 37 P1-B: pass _intentManager so ParseDecision delegates goal mapping to it.
        var llmResult = ParseDecision(raw, options.LlmConfidenceThreshold, logger, _intentManager);
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
    ///
    /// Sprint 37 P1-B: intent→goal mapping moved to IntentManager.BuildGoalRequest.
    /// When <paramref name="intentManager"/> is non-null, it resolves (GoalName, Parameters)
    /// from the parsed IntentDraft. When null (legacy / test mode), falls back to the
    /// local switch statement that was present before Sprint 37.
    ///
    /// AGENTS.md CRITICAL rule: parsers never create goals. ParseDecision now creates
    /// an IntentDraft value and asks IntentManager to map it — it does NOT derive goal
    /// names itself.
    /// </summary>
    private static ChatInterpretation? ParseDecision(
        string content, double confidenceThreshold,
        ILogger<LlmChatInterpreter>? logger,
        IntentManager? intentManager = null)
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
                // Sprint 38 P1-B: pass intentManager so TryParseTruncatedJson delegates goal mapping.
                return TryParseTruncatedJson(json, intentManager);
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

            // Sprint 37 P1-B: build IntentDraft and delegate goal mapping to IntentManager.
            // AGENTS.md CRITICAL: parsers never create goals — IntentManager owns this mapping.
            string? goalName = null;
            IReadOnlyDictionary<string, object?>? parameters = null;

            if (intentManager is not null)
            {
                // New path (Sprint 37+): IntentManager maps IntentDraft → GoalRequest.
                var draft = new IntentDraft(
                    addressed, intent,
                    GetStr(root, "item"),
                    GetStr(root, "blueprint"),
                    GetInt(root, "count"),
                    GetInt(root, "x"),
                    GetInt(root, "y"),
                    GetInt(root, "z"),
                    confidence, clarifyQ, response);
                var goalRequest = intentManager.BuildGoalRequest(draft);
                goalName   = goalRequest?.GoalName;
                parameters = goalRequest?.Parameters;
            }
            // Sprint 38 P1-A: Legacy switch removed. goalName and parameters remain null
            // when intentManager is not injected (test-only / backward-compat path).
            // In production, IntentManager is always injected via Program.cs DI.
            // Tests that do not inject IntentManager will get ChatInterpretation(CreateGoal)
            // with null GoalName — TryCreateGoalFromChatAsync is a no-op in that case.

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
    /// Sprint 37: TryParseTruncatedJson still uses local mapping (legacy path) — Sprint 38
    ///   will wire IntentManager here once the happy-path wiring is validated.
    /// Sprint 38 P1-B: accepts optional <paramref name="intentManager"/> — when supplied,
    ///   maps the partial IntentDraft to a GoalRequest via IntentManager (no goal-name
    ///   strings in the parser). The legacy local-switch path is kept for Sprint 21
    ///   reflection-based tests that call without IntentManager.
    /// </summary>
    private static ChatInterpretation? TryParseTruncatedJson(string json, IntentManager? intentManager = null)
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

            var responseM = Regex.Match(json,
                @"""response""\s*:\s*""(?<v>[^""\\]*(?:\\.[^""\\]*)*)""");
            var response = responseM.Success ? responseM.Groups["v"].Value : string.Empty;

            string? goalName = null;
            IReadOnlyDictionary<string, object?>? parameters = null;

            if (intentManager is not null)
            {
                // Sprint 38 P1-B: migrate to IntentManager — no goal-name strings in parser.
                var itemM2  = Regex.Match(json, @"""item""\s*:\s*""(?<v>[^""]+)""", RegexOptions.IgnoreCase);
                var bpM2    = Regex.Match(json, @"""blueprint""\s*:\s*""(?<v>[^""]+)""", RegexOptions.IgnoreCase);
                var countM2 = Regex.Match(json, @"""count""\s*:\s*(?<v>\d+)", RegexOptions.IgnoreCase);
                var partialDraft = new IntentDraft(
                    addressed,
                    intent,
                    itemM2.Success  ? itemM2.Groups["v"].Value  : null,
                    bpM2.Success    ? bpM2.Groups["v"].Value    : null,
                    countM2.Success && int.TryParse(countM2.Groups["v"].Value, out var pCount) ? pCount : null,
                    null, null, null,
                    1.0, null, response);
                var goalRequest = intentManager.BuildGoalRequest(partialDraft);
                goalName   = goalRequest?.GoalName;
                parameters = goalRequest?.Parameters;
            }
            else
            {
                // Legacy path — kept for Sprint 21 reflection-based tests that call without IntentManager.
                // Sprint 39 target: remove once all test callers inject IntentManager.
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
