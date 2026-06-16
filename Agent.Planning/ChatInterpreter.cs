namespace Agent.Planning;

using Agent.Core;
using System.Text.RegularExpressions;

/// <summary>
/// Deterministic, pattern-matching <see cref="IChatInterpreter"/>.
/// All parsing is done via regex + alias tables — no LLM, no network calls (D-003).
///
/// Also used as the fallback inside <see cref="LlmChatInterpreter"/> when the LLM is
/// unavailable or the rate limit is exceeded.
///
/// Directed-at-bot heuristics (any one sufficient):
///   1. Solo play: <c>onlinePlayers &lt;= 1</c>
///   2. Bot name at start of message (case-insensitive)
///   3. Bot spoke within the last 60 seconds (active conversation window)
///
/// <see cref="InterpretAsync"/> wraps the synchronous <see cref="Interpret"/> method
/// for interface compatibility; the <paramref name="botPosition"/> and
/// <paramref name="playerPosition"/> args are passed to the distance gate in
/// <see cref="LlmChatInterpreter"/> — this class ignores them (pure pattern matching).
/// </summary>
public sealed class ChatInterpreter : IChatInterpreter
{
    // ── Conversation tracking ───────────────────────────────────────────────

    private DateTimeOffset _lastBotSpoke = DateTimeOffset.MinValue;
    private static readonly TimeSpan ConversationWindow = TimeSpan.FromSeconds(60);

    public void RecordBotSpoke() => _lastBotSpoke = DateTimeOffset.UtcNow;

    // ── IChatInterpreter ────────────────────────────────────────────────

    public Task<ChatInterpretation> InterpretAsync(
        string username, string message, string botName,
        int onlinePlayers, Position botPosition, Position? playerPosition,
        WorldState state, CancellationToken ct = default)
        => Task.FromResult(Interpret(username, message, botName, onlinePlayers, state));

    // ── Item / blueprint alias tables ─────────────────────────────────────────

    private static readonly Dictionary<string, string> ItemAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["wood"]       = "oak_log",  ["log"]         = "oak_log",   ["logs"]     = "oak_log",
        ["oak"]        = "oak_log",  ["oak log"]      = "oak_log",  ["oak logs"] = "oak_log",
        ["birch"]      = "birch_log",["spruce"]       = "spruce_log",
        ["cobble"]     = "cobblestone", ["cobblestone"] = "cobblestone",
        ["stone"]      = "cobblestone",
        ["iron"]       = "iron_ore", ["iron ore"] = "iron_ore",
        ["gold"]       = "gold_ore", ["gold ore"] = "gold_ore",
        ["coal"]       = "coal_ore", ["coal ore"] = "coal_ore",
        ["diamond"]    = "diamond",  ["diamonds"] = "diamond",
        ["sand"]       = "sand",     ["gravel"]   = "gravel",  ["dirt"] = "dirt",
        ["planks"]     = "oak_planks", ["plank"] = "oak_planks", ["oak planks"] = "oak_planks",
        ["glass"]      = "glass",    ["glass pane"] = "glass_pane",
    };

    private static readonly Dictionary<string, string> BlueprintAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["house"]       = "small-house", ["small house"] = "small-house",
        ["shelter"]     = "small-house", ["home"]        = "small-house",
        ["small-house"] = "small-house",
    };

    // ── Regexes ─────────────────────────────────────────────────────────

    private static readonly Regex GatherRegex = new(
        @"\b(get|gather|collect|mine|find|bring|fetch)\b\s+" +
        @"(?:(?<count>\d+)\s+)?" +
        @"(?:(?:some|more|a|an|the)\s+)?" +
        @"(?<item>[a-z_][a-z_ ]{1,24})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BuildRegex = new(
        @"\b(build|construct|make)\b\s+(?:a |the |me )?(?<blueprint>[a-z_][a-z_\- ]{1,30})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GoToRegex = new(
        @"\bgo\s+to\s+(?<x>-?\d+)\s+(?<y>-?\d+)\s+(?<z>-?\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Public sync API ───────────────────────────────────────────────

    public ChatInterpretation Interpret(
        string username, string message, string botName,
        int onlinePlayers, WorldState state)
    {
        if (!IsDirectedAtBot(message, botName, onlinePlayers))
            return new ChatInterpretation(ChatIntentType.NotAddressed);

        var cleaned = StripBotName(message.Trim(), botName);
        return ParseIntent(cleaned, state);
    }

    // ── Intent parsing ─────────────────────────────────────────────────

    private static ChatInterpretation ParseIntent(string message, WorldState state)
    {
        if (Regex.IsMatch(message, @"\b(stop|cancel|enough|quit|abort|nevermind|never mind)\b", RegexOptions.IgnoreCase))
            return new ChatInterpretation(ChatIntentType.CancelGoal,
                Response: "Ok, stopping what I'm doing.");

        if (Regex.IsMatch(message, @"\b(status|what.?re you doing|what are you doing|report|doing)\b", RegexOptions.IgnoreCase))
        {
            var statusMsg = state.Facts.TryGetValue("currentGoal", out var cg) && cg is string cgStr
                ? $"I'm working on: {cgStr}. Health: {state.Health}/20, Food: {state.Food}/20."
                : $"I'm idle. Health: {state.Health}/20, Food: {state.Food}/20.";
            return new ChatInterpretation(ChatIntentType.QueryStatus, Response: statusMsg);
        }

        if (Regex.IsMatch(message, @"\b(help|commands|what can you do|usage)\b", RegexOptions.IgnoreCase))
            return new ChatInterpretation(ChatIntentType.QueryHelp,
                Response: "Commands: 'get/gather/mine <item> [count]', 'build <blueprint>', " +
                          "'go to X Y Z', 'come here', 'stop', 'status', 'help'");

        if (Regex.IsMatch(message, @"\b(come here|come to me|follow me|follow)\b", RegexOptions.IgnoreCase))
            return new ChatInterpretation(ChatIntentType.NavigateTo,
                GoalParameters: new Dictionary<string, object?> { ["target"] = "player" },
                Response: "Coming to you!");

        var goToMatch = GoToRegex.Match(message);
        if (goToMatch.Success)
        {
            var x = int.Parse(goToMatch.Groups["x"].Value);
            var y = int.Parse(goToMatch.Groups["y"].Value);
            var z = int.Parse(goToMatch.Groups["z"].Value);
            return new ChatInterpretation(ChatIntentType.NavigateTo,
                GoalName: "MoveTo",
                GoalParameters: new Dictionary<string, object?> { ["x"] = x, ["y"] = y, ["z"] = z },
                Response: $"Heading to ({x}, {y}, {z})!");
        }

        var buildMatch = BuildRegex.Match(message);
        if (buildMatch.Success)
        {
            var rawBp = buildMatch.Groups["blueprint"].Value.Trim();
            var bpId  = ResolveBlueprintId(rawBp);
            if (bpId is not null)
                return new ChatInterpretation(ChatIntentType.CreateGoal,
                    GoalName: $"Build:{bpId}",
                    Response: $"Starting to build {bpId.Replace('-', ' ')}!");
        }

        var gatherMatch = GatherRegex.Match(message);
        if (gatherMatch.Success)
        {
            var rawItem = gatherMatch.Groups["item"].Value.Trim();
            var itemId  = ResolveItemId(rawItem);
            if (itemId is not null)
            {
                var countStr = gatherMatch.Groups["count"].Value;
                var count    = int.TryParse(countStr, out var c) ? c : 10;
                return new ChatInterpretation(ChatIntentType.CreateGoal,
                    GoalName: $"GatherItem:{itemId}",
                    GoalParameters: new Dictionary<string, object?> { ["count"] = count },
                    Response: $"Ok, gathering {count}x {itemId.Replace('_', ' ')}!");
            }
        }

        return new ChatInterpretation(ChatIntentType.Unknown,
            Response: "I didn't understand that. Say 'help' for a list of commands.");
    }

    // ── Heuristics ────────────────────────────────────────────────────

    private bool IsDirectedAtBot(string message, string botName, int onlinePlayers)
    {
        if (onlinePlayers <= 1) return true;
        if (message.StartsWith(botName, StringComparison.OrdinalIgnoreCase)) return true;
        if (DateTimeOffset.UtcNow - _lastBotSpoke < ConversationWindow) return true;
        return false;
    }

    private static string StripBotName(string message, string botName)
    {
        if (message.StartsWith(botName, StringComparison.OrdinalIgnoreCase))
        {
            var after = message[botName.Length..].TrimStart(',', ':', ' ');
            return after.Length > 0 ? after : message;
        }
        return message;
    }

    private static string? ResolveItemId(string rawName)
    {
        if (ItemAliases.TryGetValue(rawName, out var id)) return id;
        var singular = rawName.TrimEnd('s');
        if (ItemAliases.TryGetValue(singular, out id)) return id;
        var underscored = rawName.Replace(' ', '_').ToLowerInvariant();
        if (ItemAliases.TryGetValue(underscored, out id)) return id;
        if (Regex.IsMatch(rawName, @"^[a-z][a-z0-9_]*$")) return rawName;
        return null;
    }

    private static string? ResolveBlueprintId(string rawName)
    {
        if (BlueprintAliases.TryGetValue(rawName, out var id)) return id;
        var hyphenated = rawName.Replace(' ', '-').ToLowerInvariant();
        if (BlueprintAliases.TryGetValue(hyphenated, out id)) return id;
        if (Regex.IsMatch(rawName, @"^[a-z][a-z0-9\-]*$")) return rawName;
        return null;
    }
}

/// <summary>The result of interpreting a chat message.</summary>
public record ChatInterpretation(
    ChatIntentType IntentType,
    string? GoalName = null,
    IReadOnlyDictionary<string, object?>? GoalParameters = null,
    string Response = "");

/// <summary>Categories of chat intent the agent can act on.</summary>
public enum ChatIntentType
{
    NotAddressed, CreateGoal, CancelGoal, QueryStatus, QueryHelp, NavigateTo, Unknown,
}
