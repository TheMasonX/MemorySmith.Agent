namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Llm;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// <see cref="IChatInterpreter"/> that combines LLM-powered evaluation with
/// deterministic pattern-matching fallback and a distance-based routing gate.
///
/// Evaluation pipeline for each incoming message:
///   1. Truncate message at <see cref="ChatOptions.MaxMessageLength"/> characters.
///   2. Distance gate: player far away and not naming bot = NotAddressed.
///   3. Pattern fast-path: CreateGoal/CancelGoal/QueryHelp/QueryStatus/NavigateTo skip LLM.
///   4. Rate-limit check: per-player and global window.
///   5. LLM call: ILlmProvider.CompleteAsync with a structured JSON prompt.
///   6. JSON parse: extract ChatInterpretation from raw text.
///   7. Fallback: use pattern-matcher result if LLM fails.
///   8. Truncation recovery: TryParseTruncatedJson extracts intent from cut-off JSON. (Sprint 20)
/// </summary>
public sealed class LlmChatInterpreter(
    ILlmProvider provider,
    ChatInterpreter patternFallback,
    ChatRateLimiter rateLimiter,
    ChatOptions options,
    ChatHistory? history = null,
    ILogger<LlmChatInterpreter>? logger = null) : IChatInterpreter
{
    // -- IChatInterpreter ---------------------------------------------------------

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

        // 2. Get the pattern-matcher's view -- used as fallback and fast-path
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

        // 4. Pattern fast-path
        if (quick.IntentType is ChatIntentType.CreateGoal
                              or ChatIntentType.CancelGoal
                              or ChatIntentType.QueryHelp
                              or ChatIntentType.QueryStatus
                              or ChatIntentType.NavigateTo)
        {
            return quick;
        }

        // 5. Rate-limit check
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

        // 6. LLM call
        var currentGoal = state.Facts.TryGetValue("currentGoal", out var cg) && cg is string s ? s : null;
        logger?.LogInformation("[llm] calling {Provider} ({Model}) for <{Username}> '{Message}'",
            provider.ProviderName, options.LlmModel, username,
            effective.Length > 60 ? effective[..60] : effective);
        var raw = await provider.CompleteAsync(
            BuildSystemPrompt(botName, botPosition, currentGoal, onlinePlayers),
            $"{username} says: \"{effective}\"",
            ct);

        if (raw is null)
        {
            logger?.LogWarning("[llm] {Provider} returned null -- falling back to pattern for <{Username}>",
                provider.ProviderName, username);
            return quick;
        }

        // 7. Parse LLM response
        var llmResult = ParseDecision(raw);
        if (llmResult is null)
            logger?.LogWarning("[llm] failed to parse JSON from {Provider} response: '{Content}'",
                provider.ProviderName, raw.Length > 100 ? raw[..100] : raw);
        return llmResult ?? quick;
    }

    public void RecordBotSpoke() => patternFallback.RecordBotSpoke();

    // -- Prompt construction ------------------------------------------------------

    private static string BuildSystemPrompt(
        string botName, Position botPos, string? goal, int onlinePlayers) => $$"""
        You are {{botName}}, an autonomous Minecraft agent at ({{botPos.X}},{{botPos.Y}},{{botPos.Z}}).
        Status: {{(goal is not null ? $"pursuing goal: {goal}" : "idle")}}. Players online: {{onlinePlayers}}.

        Decide if the next message is for you and what to do.
        Reply ONLY with valid JSON -- no markdown, no prose:

        {
          "addressed": "yes" | "maybe" | "no",
          "intent": "gather" | "build" | "cancel" | "status" | "help" | "navigate" | "conversation" | "ignore" | "clarify",
          "item": "<minecraft_id or null>",
          "blueprint": "<blueprint_id or null>",
          "count": <integer or null>,
          "x": <integer or null>,
          "y": <integer or null>,
          "z": <integer or null>,
          "response": "<in-game reply, max 50 words, empty if intent is ignore>"
        }

        Rules: "yes" when your name is used or only 1 player is online.
        "maybe" when it could be a command but your name is not mentioned.
        "no" when players are talking to each other, not you.
        "conversation" when the player is just chatting (greetings, questions about you, casual talk).
        "clarify" when uncertain -- ask politely.
        Use Minecraft item IDs without namespace prefix (oak_log, cobblestone, iron_ore).
        """;

    // -- Response parsing ---------------------------------------------------------

    private static readonly Regex CodeFenceRegex =
        new(@"```(?:json)?\s*(?<body>[\s\S]*?)```", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BraceRegex =
        new(@"\{[\s\S]*\}", RegexOptions.Compiled);

    private static ChatInterpretation? ParseDecision(string content)
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
                case "build":
                    var bp = GetStr(root, "blueprint");
                    if (bp is not null)
                    {
                        goalName = $"Build:{bp}";
                        // Sprint 35: pass optional coordinates from LLM
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
                "gather" or "build" => ChatIntentType.CreateGoal,
                "cancel"            => ChatIntentType.CancelGoal,
                "status"            => ChatIntentType.QueryStatus,
                "help"              => ChatIntentType.QueryHelp,
                "navigate"          => ChatIntentType.NavigateTo,
                "conversation"      => ChatIntentType.Chat,
                "clarify"           => ChatIntentType.Unknown,
                _                   => isUncertain ? ChatIntentType.Unknown : ChatIntentType.NotAddressed,
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

    /// <summary>
    /// Sprint 20: best-effort extraction from truncated JSON missing the closing brace.
    /// Common with small models (llama3.2:3b) hitting num_predict limits.
    /// Sprint 21 P1-C: extended to extract item/count from truncated gather JSON and
    /// blueprint from truncated build JSON. Previously these fell through to Unknown.
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

            // Sprint 21 P1-C: extract goal parameters from truncated gather/build JSON.
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
            }

            var intentType = intent.ToLowerInvariant() switch
            {
                "gather" or "build" => goalName is not null ? ChatIntentType.CreateGoal : ChatIntentType.Unknown,
                "cancel"            => ChatIntentType.CancelGoal,
                "status"            => ChatIntentType.QueryStatus,
                "help"              => ChatIntentType.QueryHelp,
                "conversation"      => ChatIntentType.Chat,
                "clarify"           => ChatIntentType.Unknown,
                "ignore"            => ChatIntentType.NotAddressed,
                _                   => ChatIntentType.Unknown,
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
