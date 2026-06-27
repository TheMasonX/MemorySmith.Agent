namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Llm;
using System.Text.RegularExpressions;

/// <summary>
/// Deterministic pattern-matching chat interpreter. Parses in-game player messages
/// into <see cref="IntentDraft"/> values using regex and alias dictionaries.
///
/// This class is the fast-path fallback used by <see cref="LlmChatInterpreter"/> for
/// safe, zero-risk operations (cancel, status, inventory, help) and as the fallback
/// when the LLM provider is unavailable or rate-limited.
///
/// Sprint 35 P1-D: GatherRegex, BuildRegex, and CraftRegex match blocks removed.
/// TSK-0118: dead regex field definitions removed (TSK-0118). All gather/build/craft
/// intent is handled by LlmChatInterpreter → LLM → IntentDraft pipeline.
/// Item aliases consolidated in <see cref="AliasRegistry"/> (TSK-0099).
///
/// Sprint 39 P1-C: <see cref="InterpretAsync"/> return type changed to
/// <see cref="IntentDraft"/>? — null means "not addressed" (replaces NotAddressed enum value).
/// </summary>
public sealed class ChatInterpreter : IChatInterpreter
{
    // ── Regex field definitions ──────────────────────────────────────────────

    // TSK-0118: GatherRegex, BuildRegex, and CraftRegex removed — dead code since
    // Sprint 35 P1-D removed their match blocks from ParseIntent. All gather/build/craft
    // intent is handled by LlmChatInterpreter → LLM → IntentDraft pipeline.
    // Item alias dictionaries were consolidated into AliasRegistry (TSK-0099).
    // GoToRegex is preserved for deterministic coordinate navigation.

    /// <summary>Matches explicit coordinate navigation commands.</summary>
    private static readonly Regex GoToRegex = new(
        @"\b(go\s+to|goto|move\s+to|walk\s+to|navigate\s+to|teleport\s+to|tp\s+to)\b\s+(?<x>-?\d+)\s+(?<y>-?\d+)\s+(?<z>-?\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly string _botName;
    private DateTimeOffset _lastBotSpoke = DateTimeOffset.MinValue;
    private readonly int _conversationWindowSeconds;
    private readonly double _maxResponseDistanceBlocks;

    // TSK-0105: compiled regex for whole-word bot name matching (avoids substring false positives)
    private Regex? _botNameRegex;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ChatInterpreter(string botName, int conversationWindowSeconds = 60,
        double maxResponseDistanceBlocks = 64.0)
    {
        _botName = botName;
        _conversationWindowSeconds = conversationWindowSeconds;
        _maxResponseDistanceBlocks = maxResponseDistanceBlocks;
        if (!string.IsNullOrEmpty(botName))
            _botNameRegex = BuildBotNameRegex(botName);
    }

    /// <summary>
    /// Sprint 39 P1-C: convenience constructor that takes <see cref="ChatOptions"/>.
    /// Bot name is not stored here — it arrives via the <c>botName</c> parameter of
    /// <see cref="InterpretAsync"/> at call time.
    /// TSK-0103: also extracts MaxResponseDistanceBlocks for distance-based gating.
    /// </summary>
    public ChatInterpreter(ChatOptions opts)
        : this(string.Empty, opts.ConversationWindowSeconds, opts.MaxResponseDistanceBlocks) { }

    /// <summary>
    /// TSK-0105: Builds a compiled word-boundary regex for bot name matching.
    /// Uses \b word boundaries to prevent substring false positives
    /// (e.g. "Leo" won't match "helios").
    /// RegexOptions.IgnoreCase for case-insensitive matching.
    /// </summary>
    private static Regex BuildBotNameRegex(string botName)
    {
        var escaped = Regex.Escape(botName);
        return new Regex($@"\b{escaped}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    // ── IChatInterpreter ──────────────────────────────────────────────────────

    // Sprint 39 P1-C: returns IntentDraft? — null means the message is not addressed at this bot.
    public Task<IntentDraft?> InterpretAsync(
        string username, string message, string botName,
        int onlinePlayers, Position botPosition, Position? playerPosition,
        WorldState state, CancellationToken ct = default)
    {
        var directed = IsDirectedAtBot(message, botName, onlinePlayers, playerPosition, botPosition);
        if (!directed)
            return Task.FromResult<IntentDraft?>(null);

        IntentDraft? result = ParseIntent(message, state, botName);
        return Task.FromResult<IntentDraft?>(result);
    }

    public void RecordBotSpoke() => _lastBotSpoke = DateTimeOffset.UtcNow;

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Determines whether this message is directed at the bot.
    /// Criteria (any of):
    ///   - Bot name appears in the message (whole-word match, TSK-0105)
    ///   - Only 1 player is online (must be talking to the bot)
    ///   - Bot spoke recently (within conversation window) — continuation heuristic
    ///
    /// TSK-0103: When none of the above apply, also checks distance gate:
    /// if the player is too far away, the message is not considered addressed.
    /// </summary>
    private bool IsDirectedAtBot(
        string message, string botName,
        int onlinePlayers, Position? playerPosition, Position botPosition)
    {
        // TSK-0105: whole-word match instead of substring Contains
        if (MatchesBotName(message, botName))
            return true;

        // Solo player — must be talking to the bot
        if (onlinePlayers <= 1)
            return true;

        // Conversation continuation window
        var elapsed = DateTimeOffset.UtcNow - _lastBotSpoke;
        if (elapsed.TotalSeconds <= _conversationWindowSeconds)
            return true;

        // TSK-0103: distance gate — if player is far away, not addressed
        if (playerPosition is not null
            && Distance(botPosition, playerPosition) > _maxResponseDistanceBlocks)
            return false;

        return false;
    }

    /// <summary>
    /// TSK-0105: Checks if the bot name appears as a whole word in the message.
    /// Uses a compiled word-boundary regex to avoid substring false positives.
    ///
    /// AUD-48-002: Uses the cached compiled regex (when available and matching)
    /// instead of creating a new regex on every call. When the bot name differs
    /// from the constructor parameter (e.g. via <see cref="InterpretAsync"/>'s
    /// <c>botName</c> override), falls back to inline <see cref="Regex.IsMatch"/>.
    /// </summary>
    private bool MatchesBotName(string message, string botName)
    {
        if (_botNameRegex is not null
            && string.Equals(_botName, botName, StringComparison.OrdinalIgnoreCase))
        {
            return _botNameRegex.IsMatch(message);
        }

        var escaped = Regex.Escape(botName);
        return Regex.IsMatch(message, $@"\b{escaped}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Horizontal distance (X/Z only) between two positions.
    /// AUD-48-003: delegates to shared <see cref="ChatDistance.Horizontal"/> so
    /// deterministic and LLM chat paths use the same calculation.
    /// Vertical separation (Y) is intentionally excluded — it does not affect
    /// whether a player is within chat-hearing range in Minecraft.
    /// </summary>
    private static double Distance(Position a, Position b) =>
        ChatDistance.Horizontal(a, b);

    /// <summary>
    /// Core pattern-matching parser. Returns a <see cref="ChatInterpretation"/>
    /// for each recognized command category.
    ///
    /// Sprint 35 P1-D: GatherRegex, BuildRegex, and CraftRegex match blocks removed.
    /// All gather/build/craft intent is now handled by LlmChatInterpreter → LLM → IntentDraft.
    /// The alias dictionaries (ItemAliases, BlueprintAliases, CraftAliases) and resolver methods
    /// ChatInterpreter (pattern-only fallback) returns "clarify" so LlmChatInterpreter
    /// always routes these to the LLM.
    /// </summary>
    /// <summary>
    /// Core pattern-matching parser. Returns an <see cref="IntentDraft"/> for each
    /// recognised command category.
    ///
    /// Sprint 35 P1-D: GatherRegex, BuildRegex, and CraftRegex match blocks removed.
    /// TSK-0118: dead regex field definitions removed. All gather/build/craft intent
    /// is handled by LlmChatInterpreter → LLM → IntentDraft.
    ///
    /// Sprint 39 P1-C: return type changed from <see cref="ChatInterpretation"/> to
    /// <see cref="IntentDraft"/>. navigate/come-here uses null X/Y/Z to signal "follow player".
    /// </summary>
    private static IntentDraft ParseIntent(string message, WorldState state, string? botName = null)
    {
        // Sprint 54 (TSK-0200): removed "enough" — too many false positives in everyday language.
        // Canonical stop commands only: stop, cancel, quit, abort, nevermind.
        if (Regex.IsMatch(message, @"\b(stop|cancel|quit|abort|nevermind|never mind)\b",
            RegexOptions.IgnoreCase))
            return new IntentDraft("yes", "cancel",
                null, null, null, null, null, null,
                1.0, null, "Ok, stopping.");

        // TSK-0015: inventory report command
        // Sprint 54 (TSK-0210): enriched with goal context and suggestions.
        if (Regex.IsMatch(message, @"\b(inventory|what do you have|what are you carrying|items)\b",
            RegexOptions.IgnoreCase))
        {
            var inv = state.Inventory.Count == 0
                ? "Inventory is empty."
                : string.Join(", ", state.Inventory
                    .OrderByDescending(kv => kv.Value)
                    .Take(10)
                    .Select(kv => $"{kv.Value}x {kv.Key}"));
            var goal = state.Facts.TryGetValue("currentGoal", out var cg) && cg is string s
                ? $" (Working on: {s})" : "";
            var suggestion = state.Inventory.Count == 0
                ? " Try 'get wood' to start gathering."
                : "";
            return new IntentDraft("yes", "status",
                null, null, null, null, null, null,
                1.0, null, $"Inventory{goal}: {inv}.{suggestion}");
        }

        if (Regex.IsMatch(message, @"\b(status|what.?re you doing|what are you doing|report)\b",
            RegexOptions.IgnoreCase))
        {
            // Sprint 54 (TSK-0210): enriched status with build progress, inventory summary,
            // and contextual suggestions instead of raw dumps.
            var goalStatus = state.Facts.TryGetValue("currentGoal", out var cg2) && cg2 is string gs
                ? gs : "Idle";

            // Top 5 inventory items for quick context
            var topItems = state.Inventory.Count > 0
                ? string.Join(", ", state.Inventory
                    .OrderByDescending(kv => kv.Value)
                    .Take(5)
                    .Select(kv => $"{kv.Value}x {kv.Key}"))
                : "nothing";

            var statusMsg = goalStatus == "Idle"
                ? $"Idle. HP: {state.Health}/20, Food: {state.Food}/20. Carrying: {topItems}."
                : $"Working on: {goalStatus}. HP: {state.Health}/20, Food: {state.Food}/20. Carrying: {topItems}.";

            return new IntentDraft("yes", "status",
                null, null, null, null, null, null,
                1.0, null, statusMsg);
        }

        // Sprint 54 (TSK-0200): only match "help"/"commands"/"usage" when the message is
        // essentially JUST the command, optionally preceded/followed by the bot name or
        // a casual prefix. Prevents false positives when these words appear incidentally
        // in longer sentences. Multi-word "what can you do" is unrestricted.
        var namePattern = string.IsNullOrEmpty(botName) ? @"\S+" : Regex.Escape(botName);
        if (Regex.IsMatch(message, $@"^(?:hey\s+|ok\s+)?(?:{namePattern}\s+)?(help|commands|usage)(?:\s+{namePattern})?\s*$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(message, @"\bwhat can you do\b", RegexOptions.IgnoreCase))
            return new IntentDraft("yes", "help",
                null, null, null, null, null, null,
                1.0, null,
                "Commands: 'get/mine <item> [n]', 'craft <item>', 'build <blueprint> [at X Y Z]', " +
                "'go to X Y Z', 'come here', 'stop', 'status', 'inventory', 'help'");

        // Sprint 54 (TSK-0203): "remember X is Y" / "recall X" — deterministic fast-path
        // for explicit memory commands. Conversation-path remembers are handled by the LLM.
        var rememberMatch = Regex.Match(message,
            @"\bremember\s+(?:the\s+)?(?<key>.+?)\s+is\s+(?<value>.+?)(?:[.!]?\s*$)",
            RegexOptions.IgnoreCase);
        if (rememberMatch.Success)
        {
            var key = rememberMatch.Groups["key"].Value.Trim();
            var value = rememberMatch.Groups["value"].Value.Trim();
            return new IntentDraft("yes", "remember",
                key, null, null, null, null, null,
                1.0, null, $"Got it — I'll remember that {key} is {value}.");
        }

        // "come here" / "follow me" — null X/Y/Z signals "follow player" to the caller
        if (Regex.IsMatch(message, @"\b(come here|come to me|follow me|follow)\b",
            RegexOptions.IgnoreCase))
            return new IntentDraft("yes", "navigate",
                null, null, null, null, null, null,
                1.0, null, "On my way!");

        var goTo = GoToRegex.Match(message);
        if (goTo.Success)
        {
            var x = int.Parse(goTo.Groups["x"].Value);
            var y = int.Parse(goTo.Groups["y"].Value);
            var z = int.Parse(goTo.Groups["z"].Value);
            return new IntentDraft("yes", "navigate",
                null, null, null, x, y, z,
                1.0, null, $"Heading to ({x},{y},{z}).");
        }

        // Sprint 35 P1-D: GatherRegex, BuildRegex, and CraftRegex match blocks removed.
        // All gather/build/craft intent is handled by LlmChatInterpreter → LLM → IntentDraft.
        // The alias dictionaries (ItemAliases, BlueprintAliases, CraftAliases) and resolver methods
        // are preserved for use by LlmChatInterpreter's item normalization in Sprint 36.
        // ChatInterpreter (pattern-only fallback) returns "clarify" so LlmChatInterpreter
        // always routes these to the LLM.

        return new IntentDraft("yes", "clarify",
            null, null, null, null, null, null,
            0.5, null, "Didn't catch that. Say 'help' for commands.");
    }

    // ── Item / blueprint resolution helpers ───────────────────────────────────

    /// <summary>
    /// Resolves a raw item token to a canonical Minecraft item ID.
    /// Checks <see cref="AliasRegistry.ItemAliases"/> first, then returns the token as-is
    /// (lowercased, spaces → underscores).
    /// </summary>
    public static string ResolveItem(string raw)
    {
        var normalized = raw.Trim().ToLowerInvariant();
        if (AliasRegistry.ItemAliases.TryGetValue(normalized, out var alias))
            return alias;
        return normalized.Replace(' ', '_');
    }

    /// <summary>
    /// Resolves a raw blueprint token to a canonical blueprint ID.
    /// Checks <see cref="AliasRegistry.BlueprintAliases"/> first, then returns the token
    /// as-is (lowercased, spaces → hyphens).
    /// </summary>
    public static string ResolveBlueprint(string raw)
    {
        var normalized = raw.Trim().ToLowerInvariant();
        if (AliasRegistry.BlueprintAliases.TryGetValue(normalized, out var alias))
            return alias;
        return normalized.Replace(' ', '-');
    }

    /// <summary>
    /// Resolves a raw craft item token to a canonical Minecraft item ID.
    /// Checks <see cref="AliasRegistry.CraftAliases"/> first, then falls back to
    /// <see cref="ResolveItem"/>.
    /// </summary>
    public static string ResolveCraftItem(string raw)
    {
        var normalized = raw.Trim().ToLowerInvariant();
        if (AliasRegistry.CraftAliases.TryGetValue(normalized, out var alias))
            return alias;
        return ResolveItem(raw);
    }
}
