namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Llm;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// <see cref="IChatInterpreter"/> that combines LLM-powered evaluation with
/// deterministic pattern-matching fallback, a distance-based routing gate, and
/// a rolling chat history window (Sprint 4b).
///
/// Evaluation pipeline for each incoming message:
///   1. Truncate message at <see cref="ChatOptions.MaxMessageLength"/> characters.
///   2. Distance gate: if the player is > <see cref="ChatOptions.MaxResponseDistanceBlocks"/>
///      blocks away AND didn't name this bot, return NotAddressed without calling the LLM.
///   3. Pattern fast-path: if <see cref="ChatInterpreter"/> returns a confident result
///      (CreateGoal / CancelGoal / QueryHelp / QueryStatus / NavigateTo), skip the LLM.
///      NavigateTo is fast-pathed because the LLM cannot improve on it — player position
///      is already resolved by the pattern matcher, not the LLM.
///      QueryStatus/QueryHelp are fast-pathed because the pattern response is complete and
///      correct; calling the LLM just adds latency.
///   4. Rate-limit check: per-player and global window via <see cref="ChatRateLimiter"/>.
///   5. LLM call: <see cref="ILlmProvider.CompleteAsync"/> with a structured JSON prompt
///      that includes the last <see cref="ChatHistory.MaxTurnsDefault"/> conversation turns.
///   6. JSON parse: extract <see cref="ChatInterpretation"/> from the raw text.
///   7. Fallback: if any of steps 4-6 fail, use the pattern-matcher result.
///
/// "Split-brain" terminology: step 3 is the deterministic brain; steps 5-6 are the
/// reasoning brain. The deterministic brain always wins for clear commands.
/// </summary>
public sealed class LlmChatInterpreter(
    ILlmProvider provider,
    ChatInterpreter patternFallback,
    ChatRateLimiter rateLimiter,
    ChatOptions options,
    ChatHistory? history = null,
    Microsoft.Extensions.Logging.ILogger<LlmChatInterpreter>? logger = null) : IChatInterpreter
{
    // ── IChatInterpreter ──────────────────────────────────────────────────────

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

        // 2. Get the pattern-matcher's view — used as fallback and fast-path
        var quick = await patternFallback.InterpretAsync(
            username, effective, botName, onlinePlayers,
            botPosition, playerPosition, state, ct);

        // 3. Distance gate — skip LLM if player is far and not addressing this bot
        if (quick.IntentType == ChatIntentType.NotAddressed
            && playerPosition is not null
            && Distance(botPosition, playerPosition) > options.MaxResponseDistanceBlocks)
        {
            return quick;
        }

        // 4. Pattern fast-path — skip LLM for commands where the pattern response is
        //    already complete and correct. The LLM cannot improve these.
        //    NavigateTo: pattern knows player position; LLM does not.
        //    QueryStatus/QueryHelp: deterministic responses; LLM adds only latency.
        if (quick.IntentType is ChatIntentType.CreateGoal
                              or ChatIntentType.CancelGoal
                              or ChatIntentType.QueryHelp
                              or ChatIntentType.QueryStatus
                              or ChatIntentType.NavigateTo)
        {
            logger?.LogDebug("[llm] fast-path {Intent} for {Username}", quick.IntentType, username);
            return quick;
        }

        // Log when a message is about to go through the full LLM path vs. be skipped
        logger?.LogDebug("[llm] will call LLM for <{Username}> (pattern={Intent}): '{Snippet}'",
            username, quick.IntentType, effective[..Math.Min(40, effective.Length)]);

        // 5. Rate-limit check
        if (!provider.IsAvailable)
        {
            logger?.LogDebug("[llm] provider unavailable ({Provider}) — using pattern result", provider.ProviderName);
            return quick;
        }
        if (!rateLimiter.TryAcquire(username, out _))
        {
            logger?.LogDebug("[llm] rate-limited for {Username} — using pattern result", username);
            return quick;
        }

        // 6. LLM call
        var snippet = effective[..Math.Min(60, effective.Length)];
        logger?.LogInformation("[llm] calling {Provider} ({Model}) for <{Username}> '{Snippet}'",
            provider.ProviderName, options.LlmModel, username, snippet);
        var currentGoal = state.Facts.TryGetValue("currentGoal", out var cg) && cg is string s ? s : null;
        var historyContext = history?.FormatForPrompt();
        var raw = await provider.CompleteAsync(
            BuildSystemPrompt(botName, botPosition, currentGoal, onlinePlayers, historyContext, playerPosition, state),
            $"{username} says: \"{effective}\"",
            ct);

        if (raw is null)
        {
            logger?.LogWarning("[llm] {Provider} returned null — falling back to pattern for <{Username}>",
                provider.ProviderName, username);
            return quick;
        }

        // 7. Parse LLM response
        logger?.LogDebug("[llm] raw response ({Len} chars): {Raw}",
            raw.Length, raw[..Math.Min(200, raw.Length)]);
        var llmResult = ParseDecision(raw);
        if (llmResult is null)
            logger?.LogWarning("[llm] failed to parse JSON from {Provider} response", provider.ProviderName);
        return llmResult ?? quick;
    }

    public void RecordBotSpoke() => patternFallback.RecordBotSpoke();

    // ── Prompt construction ───────────────────────────────────────────────────

    private static string BuildSystemPrompt(
        string botName, Position botPos, string? goal, int onlinePlayers,
        string? chatHistory, Position? playerPos = null,
        WorldState? state = null) => $$"""
        You are {{botName}}, an autonomous Minecraft survival bot.
        Game: Minecraft Java Edition. Your position: ({{botPos.X}},{{botPos.Y}},{{botPos.Z}}).
        Health: {{state?.Health ?? 20}}/20. Food: {{state?.Food ?? 20}}/20.
        Inventory: {{FormatInventory(state)}}.
        Status: {{(goal is not null ? $"pursuing goal: {goal}" : "idle")}}. Players nearby: {{onlinePlayers}}.
        {{(playerPos is not null ? $"Player at: ({playerPos.X},{playerPos.Y},{playerPos.Z}) — use for navigation." : "")}}

        What you can do: gather/mine items, build structures, navigate (come here / go to X Y Z),
        craft items, smelt ores, report status and inventory, answer questions.

        {{(chatHistory is not null ? $"Recent conversation:\n{chatHistory}\n" : "")}}
        Decide if the next message is for you and respond.
        Reply ONLY with valid JSON — no markdown, no prose:

        {
          "addressed": "yes" | "maybe" | "no",
          "intent": "gather" | "build" | "cancel" | "status" | "help" | "navigate" | "chat" | "clarify",
          "item": "<minecraft_id or null>",
          "blueprint": "<blueprint_id or null>",
          "count": <integer or null>,
          "x": <integer or null>,
          "y": <integer or null>,
          "z": <integer or null>,
          "response": "<in-game reply, max 50 words — never empty for addressed messages>"
        }

        Rules:
        "yes" when your name is used or only 1 player is online.
        "maybe" when it could be for you but your name isn't mentioned.
        "no" when players are clearly talking to each other, not you.
        Use "chat" for greetings, questions, or conversation — always include a "response".
        Use "clarify" when you need more info — ask a short question.
        Never leave "response" empty for "yes" or "maybe" addressed messages.
        Use Minecraft item IDs without namespace prefix (oak_log, cobblestone, iron_ore…).
        """;

    private static string FormatInventory(WorldState? state)
    {
        if (state is null || state.Inventory.Count == 0) return "empty";
        var top = state.Inventory
            .OrderByDescending(kv => kv.Value)
            .Take(6)
            .Select(kv => $"{kv.Value}\u00d7{kv.Key}");
        return string.Join(", ", top)
            + (state.Inventory.Count > 6 ? $" (+{state.Inventory.Count - 6} more)" : "");
    }

    // ── Response parsing ──────────────────────────────────────────────────────

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
            if (!m.Success) return null;

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
                    if (bp is not null) goalName = $"Build:{bp}";
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
                "chat"              => ChatIntentType.Chat,
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

    private static double Distance(Position a, Position b)
    {
        var dx = a.X - b.X; var dy = a.Y - b.Y; var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
