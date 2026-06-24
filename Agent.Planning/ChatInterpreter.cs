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
/// Sprint 35 P1-D: GatherRegex, BuildRegex, and CraftRegex match blocks have been
/// removed from <see cref="ParseIntent"/>. All gather/build/craft intent is now handled
/// exclusively by <see cref="LlmChatInterpreter"/> → LLM → IntentDraft pipeline.
/// The regex field definitions and alias dictionaries are preserved for use by
/// LlmChatInterpreter's item normalization in Sprint 36.
///
/// Sprint 39 P1-C: <see cref="InterpretAsync"/> return type changed to
/// <see cref="IntentDraft"/>? — null means "not addressed" (replaces NotAddressed enum value).
/// </summary>
public sealed class ChatInterpreter : IChatInterpreter
{
    // ── Regex field definitions (preserved for test and Sprint 36 use) ────────

    /// <summary>
    /// Matches gather/mine/collect commands.
    /// Sprint 35 P1-D: no longer used in ParseIntent; match block removed.
    /// Preserved for test coverage and Sprint 36 item normalization.
    /// </summary>
    private static readonly Regex GatherRegex = new(
        @"\b(get|mine|gather|collect|fetch|bring|chop|cut|dig)\b\s+(?<count>\d+\s+)?(?<item>[a-z_]+(\s+[a-z_]+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches build commands with optional blueprint and coordinates.
    /// Sprint 35 P1-D: no longer used in ParseIntent; match block removed.
    /// Preserved for test coverage and Sprint 36 item normalization.
    /// </summary>
    private static readonly Regex BuildRegex = new(
        @"\b(build|construct|make|place|create)\b\s+(?<blueprint>[a-z_\-]+(\s+[a-z_\-]+)?)(\s+at\s+(?<x>-?\d+)\s+(?<y>-?\d+)\s+(?<z>-?\d+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches craft commands.
    /// Sprint 35 P1-D: no longer used in ParseIntent; match block removed.
    /// Preserved for test coverage and Sprint 36 item normalization.
    /// </summary>
    private static readonly Regex CraftRegex = new(
        @"\b(craft|smelt|cook|brew)\b\s+(?<count>\d+\s+)?(?<item>[a-z_]+(\s+[a-z_]+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Matches explicit coordinate navigation commands.</summary>
    private static readonly Regex GoToRegex = new(
        @"\b(go\s+to|goto|move\s+to|walk\s+to|navigate\s+to|teleport\s+to|tp\s+to)\b\s+(?<x>-?\d+)\s+(?<y>-?\d+)\s+(?<z>-?\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Alias dictionaries (TSK-0099: consolidated in AliasRegistry) ───────────

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly string _botName;
    private DateTimeOffset _lastBotSpoke = DateTimeOffset.MinValue;
    private readonly int _conversationWindowSeconds;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ChatInterpreter(string botName, int conversationWindowSeconds = 60)
    {
        _botName = botName;
        _conversationWindowSeconds = conversationWindowSeconds;
    }

    /// <summary>
    /// Sprint 39 P1-C: convenience constructor that takes <see cref="ChatOptions"/>.
    /// Bot name is not stored here — it arrives via the <c>botName</c> parameter of
    /// <see cref="InterpretAsync"/> at call time.
    /// </summary>
    public ChatInterpreter(ChatOptions opts)
        : this(string.Empty, opts.ConversationWindowSeconds) { }

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

        IntentDraft? result = ParseIntent(message, state);
        return Task.FromResult<IntentDraft?>(result);
    }

    public void RecordBotSpoke() => _lastBotSpoke = DateTimeOffset.UtcNow;

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Determines whether this message is directed at the bot.
    /// Criteria (any of):
    ///   - Bot name appears in the message
    ///   - Only 1 player is online (must be talking to the bot)
    ///   - Bot spoke recently (within conversation window) — continuation heuristic
    /// </summary>
    private bool IsDirectedAtBot(
        string message, string botName,
        int onlinePlayers, Position? playerPosition, Position botPosition)
    {
        // Explicit name mention
        if (message.Contains(botName, StringComparison.OrdinalIgnoreCase))
            return true;

        // Solo player — must be talking to the bot
        if (onlinePlayers <= 1)
            return true;

        // Conversation continuation window
        var elapsed = DateTimeOffset.UtcNow - _lastBotSpoke;
        if (elapsed.TotalSeconds <= _conversationWindowSeconds)
            return true;

        return false;
    }

    /// <summary>
    /// Core pattern-matching parser. Returns a <see cref="ChatInterpretation"/>
    /// for each recognized command category.
    ///
    /// Sprint 35 P1-D: GatherRegex, BuildRegex, and CraftRegex match blocks removed.
    /// All gather/build/craft intent is now handled by LlmChatInterpreter → LLM → IntentDraft.
    /// The alias dictionaries (ItemAliases, BlueprintAliases, CraftAliases) and resolver methods
    /// are preserved for use by LlmChatInterpreter's item normalization in Sprint 36.
    /// ChatInterpreter (pattern-only fallback) returns Unknown for these commands so the
    /// LlmChatInterpreter always routes them to the LLM.
    /// </summary>
    /// <summary>
    /// Core pattern-matching parser. Returns an <see cref="IntentDraft"/> for each
    /// recognised command category.
    ///
    /// Sprint 35 P1-D: GatherRegex, BuildRegex, and CraftRegex match blocks removed.
    /// All gather/build/craft intent is now handled by LlmChatInterpreter → LLM → IntentDraft.
    ///
    /// Sprint 39 P1-C: return type changed from <see cref="ChatInterpretation"/> to
    /// <see cref="IntentDraft"/>. navigate/come-here uses null X/Y/Z to signal "follow player".
    /// </summary>
    private static IntentDraft ParseIntent(string message, WorldState state)
    {
        if (Regex.IsMatch(message, @"\b(stop|cancel|enough|quit|abort|nevermind|never mind)\b",
            RegexOptions.IgnoreCase))
            return new IntentDraft("yes", "cancel",
                null, null, null, null, null, null,
                1.0, null, "Ok, stopping.");

        // TSK-0015: inventory report command
        if (Regex.IsMatch(message, @"\b(inventory|what do you have|what are you carrying|items)\b",
            RegexOptions.IgnoreCase))
        {
            var inv = state.Inventory.Count == 0
                ? "Inventory is empty."
                : string.Join(", ", state.Inventory
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Value}x {kv.Key}"));
            return new IntentDraft("yes", "status",
                null, null, null, null, null, null,
                1.0, null, $"Inventory: {inv}");
        }

        if (Regex.IsMatch(message, @"\b(status|what.?re you doing|what are you doing|report)\b",
            RegexOptions.IgnoreCase))
        {
            var goal = state.Facts.TryGetValue("currentGoal", out var cg) && cg is string s
                ? $"Working on: {s}." : "Idle.";
            return new IntentDraft("yes", "status",
                null, null, null, null, null, null,
                1.0, null, $"{goal} HP: {state.Health}/20, Food: {state.Food}/20.");
        }

        if (Regex.IsMatch(message, @"\b(help|commands|what can you do|usage)\b",
            RegexOptions.IgnoreCase))
            return new IntentDraft("yes", "help",
                null, null, null, null, null, null,
                1.0, null,
                "Commands: 'get/mine <item> [n]', 'craft <item>', 'build <blueprint> [at X Y Z]', " +
                "'go to X Y Z', 'come here', 'stop', 'status', 'inventory', 'help'");

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
