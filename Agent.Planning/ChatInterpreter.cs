namespace Agent.Planning;

using Agent.Core;
using System.Text.RegularExpressions;

/// <summary>
/// Interprets in-game Minecraft chat messages and maps them to agent intents.
///
/// Design (D-003): deterministic-first — all parsing uses pattern matching, no LLM.
/// The LLM fallback for truly novel commands is deferred to Phase 6.
///
/// Directed-at-bot heuristics (any one condition is sufficient):
///   1. Only one non-bot player online (solo play — everything is addressed to the bot)
///   2. Message starts with the bot name, optionally followed by comma/colon/space
///   3. Bot spoke within the last 60 seconds (active conversation window)
///
/// Supported intents:
///   "get/gather/collect/mine/find X [count]" → GatherItem:{itemId}
///   "build [a/the] X"                        → Build:{blueprintId}
///   "stop/cancel/enough/quit"                → CancelGoal
///   "status/what/doing/report"               → QueryStatus
///   "help/commands/what can you do"          → QueryHelp
///   "come/follow"                            → NavigateTo (player position)
///   "go to X Y Z"                            → NavigateTo (coordinates)
///   (anything unrecognized)                  → Unknown (friendly error response)
/// </summary>
public sealed class ChatInterpreter
{
    // ── Conversation tracking ─────────────────────────────────────────────────

    private DateTimeOffset _lastBotSpoke = DateTimeOffset.MinValue;
    private static readonly TimeSpan ConversationWindow = TimeSpan.FromSeconds(60);

    /// <summary>Call this whenever the bot sends a chat message, so the interpreter
    /// knows a conversation is in progress.</summary>
    public void RecordBotSpoke() => _lastBotSpoke = DateTimeOffset.UtcNow;

    // ── Item / blueprint alias tables ─────────────────────────────────────────

    private static readonly Dictionary<string, string> ItemAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Wood
        ["wood"]       = "oak_log",  ["log"]         = "oak_log",   ["logs"]     = "oak_log",
        ["oak"]        = "oak_log",  ["oak log"]      = "oak_log",  ["oak logs"] = "oak_log",
        ["birch"]      = "birch_log",["spruce"]       = "spruce_log",
        // Stone
        ["cobble"]     = "cobblestone", ["cobblestone"] = "cobblestone",
        ["stone"]      = "cobblestone",
        // Ores
        ["iron"]       = "iron_ore", ["iron ore"] = "iron_ore",
        ["gold"]       = "gold_ore", ["gold ore"] = "gold_ore",
        ["coal"]       = "coal_ore", ["coal ore"] = "coal_ore",
        ["diamond"]    = "diamond",  ["diamonds"] = "diamond",
        // Common blocks
        ["sand"]       = "sand",     ["gravel"]  = "gravel",  ["dirt"] = "dirt",
        // Crafted items (planks gathered via logs)
        ["planks"]     = "oak_planks", ["plank"] = "oak_planks", ["oak planks"] = "oak_planks",
        // Other
        ["glass"]      = "glass",    ["glass pane"] = "glass_pane",
    };

    private static readonly Dictionary<string, string> BlueprintAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["house"]       = "small-house", ["small house"] = "small-house",
        ["shelter"]     = "small-house", ["home"]        = "small-house",
        ["small-house"] = "small-house",
    };

    // ── Regexes ────────────────────────────────────────────────────────────────

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

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Interprets a chat message and returns an interpretation.
    /// Returns <see cref="ChatInterpretation"/> with IntentType = NotAddressed
    /// if the message is not directed at the bot.
    /// </summary>
    public ChatInterpretation Interpret(
        string username,
        string message,
        string botName,
        int onlinePlayers,
        WorldState state)
    {
        if (!IsDirectedAtBot(message, botName, onlinePlayers))
            return new ChatInterpretation(ChatIntentType.NotAddressed);

        var cleaned = StripBotName(message.Trim(), botName);
        return ParseIntent(cleaned, botName, state);
    }

    // ── Intent parsing ─────────────────────────────────────────────────────────

    private ChatInterpretation ParseIntent(string message, string botName, WorldState state)
    {
        // ── Cancel ────────────────────────────────────────────────────────────
        if (Regex.IsMatch(message, @"\b(stop|cancel|enough|quit|abort|nevermind|never mind)\b",
            RegexOptions.IgnoreCase))
            return new ChatInterpretation(ChatIntentType.CancelGoal,
                Response: "Ok, stopping what I'm doing.");

        // ── Status ────────────────────────────────────────────────────────────
        if (Regex.IsMatch(message, @"\b(status|what.?re you doing|what are you doing|report|doing)\b",
            RegexOptions.IgnoreCase))
        {
            var statusMsg = state.Facts.TryGetValue("currentGoal", out var cg) && cg is string cgStr
                ? $"I'm working on: {cgStr}. Health: {state.Health}/20, Food: {state.Food}/20."
                : $"I'm idle. Health: {state.Health}/20, Food: {state.Food}/20.";
            return new ChatInterpretation(ChatIntentType.QueryStatus, Response: statusMsg);
        }

        // ── Help ──────────────────────────────────────────────────────────────
        if (Regex.IsMatch(message, @"\b(help|commands|what can you do|usage)\b",
            RegexOptions.IgnoreCase))
            return new ChatInterpretation(ChatIntentType.QueryHelp,
                Response: "Commands: 'get/gather/mine <item> [count]', 'build <blueprint>', " +
                          "'go to X Y Z', 'come here', 'stop', 'status', 'help'");

        // ── Navigation: come / follow ─────────────────────────────────────────
        if (Regex.IsMatch(message, @"\b(come here|come to me|follow me|follow)\b",
            RegexOptions.IgnoreCase))
            return new ChatInterpretation(ChatIntentType.NavigateTo,
                GoalParameters: new Dictionary<string, object?> { ["target"] = "player" },
                Response: "Coming to you!");

        // ── Navigation: go to X Y Z ───────────────────────────────────────────
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

        // ── Build ─────────────────────────────────────────────────────────────
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

        // ── Gather / mine ─────────────────────────────────────────────────────
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

        // ── Unknown ────────────────────────────────────────────────────────────
        return new ChatInterpretation(ChatIntentType.Unknown,
            Response: "I didn't understand that. Say 'help' for a list of commands.");
    }

    // ── Heuristics ─────────────────────────────────────────────────────────────

    private bool IsDirectedAtBot(string message, string botName, int onlinePlayers)
    {
        // 1. Solo play — any message is for the bot
        if (onlinePlayers <= 1) return true;

        // 2. Message starts with bot name (case-insensitive)
        if (message.StartsWith(botName, StringComparison.OrdinalIgnoreCase))
            return true;

        // 3. Active conversation — bot spoke recently
        if (DateTimeOffset.UtcNow - _lastBotSpoke < ConversationWindow)
            return true;

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
        // Direct alias lookup
        if (ItemAliases.TryGetValue(rawName, out var id)) return id;

        // Try without trailing 's' (plurals)
        var singular = rawName.TrimEnd('s');
        if (ItemAliases.TryGetValue(singular, out id)) return id;

        // Try converting spaces to underscores (matches vanilla Minecraft block names)
        var underscored = rawName.Replace(' ', '_').ToLowerInvariant();
        if (ItemAliases.TryGetValue(underscored, out id)) return id;

        // Accept raw name if it looks like a valid Minecraft ID (lowercase + underscores)
        if (Regex.IsMatch(rawName, @"^[a-z][a-z0-9_]*$")) return rawName;

        return null;
    }

    private static string? ResolveBlueprintId(string rawName)
    {
        if (BlueprintAliases.TryGetValue(rawName, out var id)) return id;

        var hyphenated = rawName.Replace(' ', '-').ToLowerInvariant();
        if (BlueprintAliases.TryGetValue(hyphenated, out id)) return id;

        // Accept raw name if it looks like a valid blueprint slug
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
    /// <summary>Message is not directed at the bot — ignore.</summary>
    NotAddressed,
    /// <summary>Request to create and pursue a goal.</summary>
    CreateGoal,
    /// <summary>Request to cancel the current goal.</summary>
    CancelGoal,
    /// <summary>Request for status information.</summary>
    QueryStatus,
    /// <summary>Request for help / available commands.</summary>
    QueryHelp,
    /// <summary>Request to navigate to a location.</summary>
    NavigateTo,
    /// <summary>Could not parse intent — respond with friendly error.</summary>
    Unknown,
}
