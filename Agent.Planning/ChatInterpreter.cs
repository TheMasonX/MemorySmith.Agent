namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Llm;
using System.Text.RegularExpressions;

/// <summary>
/// Deterministic, pattern-matching <see cref="IChatInterpreter"/>. No network calls.
///
/// Used standalone when LLM is disabled or unavailable, and as the fallback inside
/// <see cref="LlmChatInterpreter"/> when the rate limit is exceeded or Ollama is down.
///
/// Directed-at-bot heuristics (any one sufficient):
///   - Solo play: <c>onlinePlayers &lt;= 1</c>
///   - Bot named at start of message (case-insensitive)
///   - Bot spoke within the conversation window (<see cref="ChatOptions.ConversationWindowSeconds"/>)
/// </summary>
public sealed class ChatInterpreter(ChatOptions options) : IChatInterpreter
{
    private DateTimeOffset _lastBotSpoke = DateTimeOffset.MinValue;

    // ── IChatInterpreter ──────────────────────────────────────────────────────

    public Task<ChatInterpretation> InterpretAsync(
        string username, string message, string botName,
        int onlinePlayers, Position botPosition, Position? playerPosition,
        WorldState state, CancellationToken ct = default)
    {
        var effective = message.Length > options.MaxMessageLength
            ? message[..options.MaxMessageLength]
            : message;

        if (!IsDirectedAtBot(effective, botName, onlinePlayers))
            return Task.FromResult(new ChatInterpretation(ChatIntentType.NotAddressed));

        var stripped = StripBotName(effective.Trim(), botName);
        return Task.FromResult(ParseIntent(stripped, state));
    }

    public void RecordBotSpoke() => _lastBotSpoke = DateTimeOffset.UtcNow;

    // ── Item / blueprint aliases ──────────────────────────────────────────────

    private static readonly Dictionary<string, string> ItemAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["wood"]       = "oak_log",  ["log"]          = "oak_log",   ["logs"]      = "oak_log",
        ["oak"]        = "oak_log",  ["oak log"]       = "oak_log",  ["oak logs"]  = "oak_log",
        ["birch"]      = "birch_log", ["spruce"]       = "spruce_log",
        ["cobble"]     = "cobblestone", ["cobblestone"] = "cobblestone", ["stone"]  = "cobblestone",
        ["iron"]       = "iron_ore", ["iron ore"]      = "iron_ore",
        ["gold"]       = "gold_ore", ["gold ore"]      = "gold_ore",
        ["coal"]       = "coal_ore", ["coal ore"]      = "coal_ore",
        ["diamond"]    = "diamond",  ["diamonds"]      = "diamond",
        ["sand"]       = "sand",     ["gravel"]        = "gravel",   ["dirt"]      = "dirt",
        ["planks"]     = "oak_planks", ["plank"]       = "oak_planks", ["oak planks"] = "oak_planks",
        ["glass"]      = "glass",    ["glass pane"]    = "glass_pane",
    };

    private static readonly Dictionary<string, string> BlueprintAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["house"] = "small-house", ["small house"] = "small-house",
        ["shelter"] = "small-house", ["home"] = "small-house",
        ["small-house"] = "small-house",
    };

    // ── Regexes ────────────────────────────────────────────────────────────────

    // Allow zero or more filler words (me, some, more, a, an, the, us) before count
    // and before the item name. Using * instead of ? handles "get me some wood" and
    // "get me 32 cobble" correctly — "me" is skipped as a filler.
    private static readonly Regex GatherRegex = new(
        @"\b(get|gather|collect|mine|find|bring|fetch)\b\s+" +
        @"(?:(?:me|us|some|more|a|an|the)\s+)*" +
        @"(?:(?<count>\d+)\s+)?" +
        @"(?:(?:me|us|some|more|a|an|the)\s+)*" +
        @"(?<item>[a-z_][a-z_ ]{1,24})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Allow zero or more filler words before the blueprint name. Using * instead of ?
    // handles "build me a shelter" → fillers consumed = ["me", "a"], blueprint = "shelter".
    private static readonly Regex BuildRegex = new(
        @"\b(build|construct|make)\b\s+" +
        @"(?:(?:me|us|a|the|an)\s+)*" +
        @"(?<blueprint>[a-z_][a-z_\- ]{1,30})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GoToRegex = new(
        @"\bgo\s+to\s+(?<x>-?\d+)\s+(?<y>-?\d+)\s+(?<z>-?\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Intent parsing ─────────────────────────────────────────────────────────

    private static ChatInterpretation ParseIntent(string message, WorldState state)
    {
        if (Regex.IsMatch(message, @"\b(stop|cancel|enough|quit|abort|nevermind|never mind)\b",
            RegexOptions.IgnoreCase))
            return new ChatInterpretation(ChatIntentType.CancelGoal,
                Response: "Ok, stopping.");

        if (Regex.IsMatch(message, @"\b(status|what.?re you doing|what are you doing|report|doing)\b",
            RegexOptions.IgnoreCase))
        {
            var goal = state.Facts.TryGetValue("currentGoal", out var cg) && cg is string s
                ? $"Working on: {s}." : "Idle.";
            return new ChatInterpretation(ChatIntentType.QueryStatus,
                Response: $"{goal} HP: {state.Health}/20, Food: {state.Food}/20.");
        }

        if (Regex.IsMatch(message, @"\b(help|commands|what can you do|usage)\b",
            RegexOptions.IgnoreCase))
            return new ChatInterpretation(ChatIntentType.QueryHelp,
                Response: "Commands: 'get/mine <item> [n]', 'build <blueprint>', " +
                          "'go to X Y Z', 'come here', 'stop', 'status', 'help'");

        if (Regex.IsMatch(message, @"\b(come here|come to me|follow me|follow)\b",
            RegexOptions.IgnoreCase))
            return new ChatInterpretation(ChatIntentType.NavigateTo,
                GoalParameters: new Dictionary<string, object?> { ["target"] = "player" },
                Response: "On my way!");

        var goTo = GoToRegex.Match(message);
        if (goTo.Success)
        {
            var x = int.Parse(goTo.Groups["x"].Value);
            var y = int.Parse(goTo.Groups["y"].Value);
            var z = int.Parse(goTo.Groups["z"].Value);
            return new ChatInterpretation(ChatIntentType.NavigateTo,
                GoalName: "MoveTo",
                GoalParameters: new Dictionary<string, object?> { ["x"] = x, ["y"] = y, ["z"] = z },
                Response: $"Heading to ({x},{y},{z}).");
        }

        var build = BuildRegex.Match(message);
        if (build.Success)
        {
            var bpId = ResolveBlueprintId(build.Groups["blueprint"].Value.Trim());
            if (bpId is not null)
                return new ChatInterpretation(ChatIntentType.CreateGoal,
                    GoalName: $"Build:{bpId}",
                    Response: $"Starting to build {bpId.Replace('-', ' ')}.");
        }

        var gather = GatherRegex.Match(message);
        if (gather.Success)
        {
            var itemId = ResolveItemId(gather.Groups["item"].Value.Trim());
            if (itemId is not null)
            {
                var count = int.TryParse(gather.Groups["count"].Value, out var c) ? c : 10;
                return new ChatInterpretation(ChatIntentType.CreateGoal,
                    GoalName: $"GatherItem:{itemId}",
                    GoalParameters: new Dictionary<string, object?> { ["count"] = count },
                    Response: $"Gathering {count}x {itemId.Replace('_', ' ')}.");
            }
        }

        return new ChatInterpretation(ChatIntentType.Unknown,
            Response: "Didn't catch that. Say 'help' for commands.");
    }

    // ── Heuristics ─────────────────────────────────────────────────────────────

    private bool IsDirectedAtBot(string message, string botName, int onlinePlayers)
    {
        if (onlinePlayers <= 1) return true;
        if (message.StartsWith(botName, StringComparison.OrdinalIgnoreCase)) return true;
        var window = TimeSpan.FromSeconds(options.ConversationWindowSeconds);
        if (DateTimeOffset.UtcNow - _lastBotSpoke < window) return true;
        return false;
    }

    private static string StripBotName(string message, string botName)
    {
        if (!message.StartsWith(botName, StringComparison.OrdinalIgnoreCase)) return message;
        var after = message[botName.Length..].TrimStart(',', ':', ' ');
        return after.Length > 0 ? after : message;
    }

    private static string? ResolveItemId(string raw)
    {
        if (ItemAliases.TryGetValue(raw, out var id)) return id;
        var singular = raw.TrimEnd('s');
        if (ItemAliases.TryGetValue(singular, out id)) return id;
        var underscored = raw.Replace(' ', '_').ToLowerInvariant();
        if (ItemAliases.TryGetValue(underscored, out id)) return id;
        if (Regex.IsMatch(raw, @"^[a-z][a-z0-9_]*$")) return raw;
        return null;
    }

    private static string? ResolveBlueprintId(string raw)
    {
        if (BlueprintAliases.TryGetValue(raw, out var id)) return id;
        var hyph = raw.Replace(' ', '-').ToLowerInvariant();
        if (BlueprintAliases.TryGetValue(hyph, out id)) return id;
        if (Regex.IsMatch(raw, @"^[a-z][a-z0-9\-]*$")) return raw;
        return null;
    }
}
