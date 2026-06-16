namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Llm;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// <see cref="IChatInterpreter"/> that combines LLM-powered evaluation with
/// deterministic pattern-matching fallback and a distance-based routing gate.
///
/// Evaluation pipeline for each incoming message:
///   1. Truncate message at <see cref="ChatOptions.MaxMessageLength"/> characters.
///   2. Distance gate: if the player is &gt; <see cref="ChatOptions.MaxResponseDistanceBlocks"/>
///      blocks away AND didn't name this bot, return NotAddressed without calling the LLM.
///   3. Pattern fast-path: if <see cref="ChatInterpreter"/> returns a confident result
///      (CreateGoal / CancelGoal / QueryHelp), skip the LLM and use it.
///   4. Rate-limit check: per-player and global window via <see cref="ChatRateLimiter"/>.
///   5. LLM call: <see cref="ILlmProvider.CompleteAsync"/> with a structured JSON prompt.
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
    ChatOptions options) : IChatInterpreter
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

        // 2. Get the pattern-matcher's view — used as fallback and fast-path
        var quick = await patternFallback.InterpretAsync(
            username, effective, botName, onlinePlayers,
            botPosition, playerPosition, state, ct);

        // 3. Distance gate — skip LLM if player is far and not addressing this bot
        if (quick.IntentType == ChatIntentType.NotAddressed
            && playerPosition.HasValue
            && Distance(botPosition, playerPosition.Value) > options.MaxResponseDistanceBlocks)
        {
            return quick;
        }

        // 4. Pattern fast-path — skip LLM for unambiguous commands
        if (quick.IntentType is ChatIntentType.CreateGoal
                              or ChatIntentType.CancelGoal
                              or ChatIntentType.QueryHelp)
        {
            return quick;
        }

        // 5. Rate-limit check
        if (!provider.IsAvailable || !rateLimiter.TryAcquire(username, out _))
            return quick;

        // 6. LLM call
        var currentGoal = state.Facts.TryGetValue("currentGoal", out var cg) && cg is string s ? s : null;
        var raw = await provider.CompleteAsync(
            BuildSystemPrompt(botName, botPosition, currentGoal, onlinePlayers),
            $"{username} says: \"{effective}\"",
            ct);

        if (raw is null) return quick;

        // 7. Parse LLM response
        var llmResult = ParseDecision(raw);
        return llmResult ?? quick;
    }

    public void RecordBotSpoke() => patternFallback.RecordBotSpoke();

    // ── Prompt construction ───────────────────────────────────────────────────

    private static string BuildSystemPrompt(
        string botName, Position botPos, string? goal, int onlinePlayers) => $"""
        You are {botName}, an autonomous Minecraft agent at ({botPos.X},{botPos.Y},{botPos.Z}).
        Status: {(goal is not null ? $"pursuing goal: {goal}" : "idle")}. Players online: {onlinePlayers}.

        Decide if the next message is for you and what to do.
        Reply ONLY with valid JSON — no markdown, no prose:

        {{
          "addressed": "yes" | "maybe" | "no",
          "intent": "gather" | "build" | "cancel" | "status" | "help" | "navigate" | "ignore" | "clarify",
          "item": "<minecraft_id or null>",
          "blueprint": "<blueprint_id or null>",
          "count": <integer or null>,
          "x": <integer or null>,
          "y": <integer or null>,
          "z": <integer or null>,
          "response": "<in-game reply, max 50 words, empty if intent is ignore>"
        }}

        Rules: "yes" when your name is used or only 1 player is online.
        "maybe" when it could be a command but your name isn't mentioned.
        "no" when players are talking to each other, not you.
        "clarify" when uncertain — ask politely.
        Use Minecraft item IDs without namespace prefix (oak_log, cobblestone, iron_ore…).
        """;

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
