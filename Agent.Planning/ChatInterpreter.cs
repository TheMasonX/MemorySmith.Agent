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
///   - Bot name appears as whole word in message (case-insensitive)
///   - Bot spoke within the conversation window (<see cref="ChatOptions.ConversationWindowSeconds"/>)
///   - Player is within <see cref="ProximityAddressBlocks"/> blocks of the bot
///
/// Sprint 11: added CraftRegex — "craft/forge/smelt &lt;item&gt;" is now routed
/// deterministically, never touching the LLM. Fixes the case where "craft an iron
/// pickaxe" was forwarded to Ollama which could hang indefinitely.
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

        if (!IsDirectedAtBot(effective, botName, onlinePlayers, botPosition, playerPosition))
            return Task.FromResult(new ChatInterpretation(ChatIntentType.NotAddressed));

        var stripped = StripBotName(effective.Trim(), botName);
        return Task.FromResult(ParseIntent(stripped, state));
    }

    public void RecordBotSpoke() => _lastBotSpoke = DateTimeOffset.UtcNow;

    // ── Item / blueprint / craft aliases ──────────────────────────────────────

    private static readonly Dictionary<string, string> ItemAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["wood"]       = "oak_log",  ["log"]          = "oak_log",   ["logs"]      = "oak_log",
        ["oak"]        = "oak_log",  ["oak log"]       = "oak_log",  ["oak logs"]  = "oak_log",
        ["birch"]      = "birch_log", ["spruce"]       = "spruce_log",
        ["cobble"]     = "cobblestone", ["cobblestone"] = "cobblestone",
        // Sprint 19: "stone" resolves to "stone" — GoalFactory maps yield (cobblestone)
        ["stone"]      = "stone",
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

    /// <summary>
    /// Multi-word craft item aliases. Single-word items are auto-converted to
    /// underscore form by <see cref="ResolveCraftId"/> without an explicit entry.
    /// </summary>
    private static readonly Dictionary<string, string> CraftAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Pickaxes
        ["wooden pickaxe"]  = "wooden_pickaxe",  ["wood pickaxe"]    = "wooden_pickaxe",
        ["stone pickaxe"]   = "stone_pickaxe",
        ["iron pickaxe"]    = "iron_pickaxe",
        ["golden pickaxe"]  = "golden_pickaxe",  ["gold pickaxe"]    = "golden_pickaxe",
        ["diamond pickaxe"] = "diamond_pickaxe",
        // Axes
        ["wooden axe"]      = "wooden_axe",      ["wood axe"]        = "wooden_axe",
        ["stone axe"]       = "stone_axe",
        ["iron axe"]        = "iron_axe",
        // Shovels
        ["wooden shovel"]   = "wooden_shovel",   ["wood shovel"]     = "wooden_shovel",
        ["stone shovel"]    = "stone_shovel",
        ["iron shovel"]     = "iron_shovel",
        // Swords
        ["wooden sword"]    = "wooden_sword",    ["wood sword"]      = "wooden_sword",
        ["stone sword"]     = "stone_sword",
        ["iron sword"]      = "iron_sword",
        // Armour
        ["iron helmet"]     = "iron_helmet",
        ["iron chestplate"] = "iron_chestplate",
        ["iron leggings"]   = "iron_leggings",
        ["iron boots"]      = "iron_boots",
        // Blocks
        ["crafting table"]  = "crafting_table",
        ["oak planks"]      = "oak_planks",
        ["oak slab"]        = "oak_slab",        ["oak slabs"]       = "oak_slab",
        ["oak stairs"]      = "oak_stairs",
        ["oak door"]        = "oak_door",        ["oak doors"]       = "oak_door",
        ["oak fence"]       = "oak_fence",       ["oak fences"]      = "oak_fence",
        ["oak fence gate"]  = "oak_fence_gate",
        ["stone bricks"]    = "stone_bricks",
        // Misc items
        ["iron ingot"]      = "iron_ingot",      ["iron ingots"]     = "iron_ingot",
        ["gold ingot"]      = "gold_ingot",      ["gold ingots"]     = "gold_ingot",
        ["glass pane"]      = "glass_pane",      ["glass panes"]     = "glass_pane",
        ["glass bottle"]    = "glass_bottle",
        ["bookshelf"]       = "bookshelf",
        ["chest"]           = "chest",
        ["stick"]           = "stick",           ["sticks"]          = "stick",
        ["torch"]           = "torch",           ["torches"]         = "torch",
        ["bowl"]            = "bowl",
        ["bucket"]          = "bucket",
        ["compass"]         = "compass",
        ["clock"]           = "clock",
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

    /// <summary>
    /// Sprint 11: deterministic routing for craft / forge / smelt commands.
    /// "craft" and "forge" are not in <see cref="GatherRegex"/> or <see cref="BuildRegex"/>
    /// so there is no ambiguity. "smelt" maps to the CraftItem goal; the HTN planner
    /// routes it to the furnace via the smelting chain.
    ///
    /// Examples matched: "craft an iron pickaxe", "forge 2 iron swords",
    /// "smelt iron ore", "craft me a crafting table".
    /// </summary>
    private static readonly Regex CraftRegex = new(
        @"\b(craft|forge|smelt)\b\s+" +
        @"(?:(?:me|us|a|an|the|some)\s+)*" +
        @"(?:(?<count>\d+)\s+)?" +
        @"(?:(?:me|us|a|an|the|some)\s+)*" +
        @"(?<item>[a-z_][a-z_ ]{1,35})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Intent parsing ─────────────────────────────────────────────────────────

    private static ChatInterpretation ParseIntent(string message, WorldState state)
    {
        if (Regex.IsMatch(message, @"\b(stop|cancel|enough|quit|abort|nevermind|never mind)\b",
            RegexOptions.IgnoreCase))
            return new ChatInterpretation(ChatIntentType.CancelGoal,
                Response: "Ok, stopping.");

        // Sprint 30 P1-E: removed bare \bdoing\b token — it matched any sentence
        // containing "doing" (e.g. "what are you doing with that wood") as a status query.
        // The compound patterns "what.?re you doing" and "what are you doing" already cover
        // all legitimate status queries that contain "doing".
        if (Regex.IsMatch(message, @"\b(status|what.?re you doing|what are you doing|report)\b",
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
                Response: "Commands: 'get/mine <item> [n]', 'craft <item>', 'build <blueprint>', " +
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

        // Sprint 11: deterministic craft routing — never forward to LLM.
        var craft = CraftRegex.Match(message);
        if (craft.Success)
        {
            var craftId = ResolveCraftId(craft.Groups["item"].Value.Trim());
            if (craftId is not null)
            {
                var count = int.TryParse(craft.Groups["count"].Value, out var c) ? c : 1;
                return new ChatInterpretation(ChatIntentType.CreateGoal,
                    GoalName: $"CraftItem:{craftId}",
                    GoalParameters: new Dictionary<string, object?> { ["count"] = count },
                    Response: $"Crafting {count}x {craftId.Replace('_', ' ')}.");
            }
        }

        return new ChatInterpretation(ChatIntentType.Unknown,
            Response: "Didn't catch that. Say 'help' for commands.");
    }

    // ── Heuristics ─────────────────────────────────────────────────────────────

    // Proximity in blocks within which all chat is treated as addressed at the bot.
    private const int ProximityAddressBlocks = 32;

    private bool IsDirectedAtBot(
        string message, string botName, int onlinePlayers,
        Position botPosition, Position? playerPosition)
    {
        if (onlinePlayers <= 1) return true;
        // Whole-word name match anywhere in message so that
        // "hello Leo" and "Leo, come here" both address the bot correctly.
        if (ContainsBotName(message, botName)) return true;
        // Bot spoke recently — continue the conversation thread.
        var window = TimeSpan.FromSeconds(options.ConversationWindowSeconds);
        if (DateTimeOffset.UtcNow - _lastBotSpoke < window) return true;
        // Player is within proximity range — treat as a local conversation.
        if (playerPosition is not null)
        {
            var dx = botPosition.X - playerPosition.X;
            var dy = botPosition.Y - playerPosition.Y;
            var dz = botPosition.Z - playerPosition.Z;
            var distSq = dx * dx + dy * dy + dz * dz;
            if (distSq <= ProximityAddressBlocks * ProximityAddressBlocks) return true;
        }
        return false;
    }

    // Cached per-instance — botName is fixed at startup; no need to recompile every call.
    private Regex? _botNameRegex;

    /// <summary>
    /// Returns true when <paramref name="message"/> contains <paramref name="botName"/>
    /// as a whole word (not part of a longer word or username). Case-insensitive.
    /// Matches "hello Leo", "Leo come here", "Leo," but NOT "Leopold" or "Leo_bot".
    /// Regex is compiled once per interpreter instance for performance.
    /// </summary>
    private bool ContainsBotName(string message, string botName)
    {
        _botNameRegex ??= new Regex(
            $@"(?<![a-zA-Z0-9_]){Regex.Escape(botName)}(?![a-zA-Z0-9_])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        return _botNameRegex.IsMatch(message);
    }

    private static string StripBotName(string message, string botName)
    {
        if (!message.StartsWith(botName, StringComparison.OrdinalIgnoreCase)) return message;
        var after = message[botName.Length..].TrimStart(',', ':', ' ');
        return after.Length > 0 ? after : message;
    }

    private static string? ResolveItemId(string raw)
    {
        // Sprint 30 P1-D: constrained plural map — explicit entries in ItemAliases
        // cover all known plurals (logs, diamonds, etc.). The former TrimEnd('s')
        // heuristic was removed; it could strip valid letters (e.g. "grass" -> "gra").
        if (ItemAliases.TryGetValue(raw, out var id)) return id;
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

    /// <summary>
    /// Resolves a raw craft item string to a Minecraft item ID.
    /// Checks <see cref="CraftAliases"/> first (handles multi-word items like "iron pickaxe"),
    /// then converts spaces to underscores and accepts any lowercase identifier.
    /// </summary>
    private static string? ResolveCraftId(string raw)
    {
        if (CraftAliases.TryGetValue(raw, out var id)) return id;
        var underscored = raw.Replace(' ', '_').ToLowerInvariant();
        if (CraftAliases.TryGetValue(underscored, out id)) return id;
        // Accept any string that looks like a valid Minecraft item ID.
        if (Regex.IsMatch(underscored, @"^[a-z][a-z0-9_]*$")) return underscored;
        return null;
    }
}
