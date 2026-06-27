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
/// Sprint 35 P1-B: removed fast-path for ChatIntentType.CreateGoal.
/// Sprint 43 (P0-1): re-added fast-path for NavigateTo — "come here" is zero-risk
/// and the LLM may misinterpret it as "cancel" without prompt guidance.
/// Current deterministic fast-paths: stop/cancel, status, inventory, help, navigate.
/// All other non-trivial chat reaches the LLM.
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
    IntentManager? intentManager = null,
    // Sprint 51: optional raw LLM context dump logger for full audit trail.
    LlmContextLogger? contextLogger = null) : IChatInterpreter
{
    private readonly IntentManager? _intentManager = intentManager;

    // -- IChatInterpreter ------------------------------------------------------------------

    // Sprint 39 P1-C: returns IntentDraft? — null when not addressed.
    public async Task<IntentDraft?> InterpretAsync(
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

        // 3. Distance gate: null quick means "not addressed" — if player is far, ignore.
        if (quick is null
            && playerPosition is not null
            && Distance(botPosition, playerPosition) > options.MaxResponseDistanceBlocks)
        {
            return null;
        }

        // Sprint 35 P1-B: fast-path ONLY for safe deterministic operations.
        // CreateGoal fast-path removed — all non-trivial chat reaches the LLM.
        // Sprint 43 (P0-1): re-added fast-path for "navigate" — "come here" is zero-risk
        // and the LLM may misinterpret it as "cancel" without prompt guidance.
        // Sprint 39 P1-C: check by Intent string instead of ChatIntentType enum.
        // Deterministic fast-paths: cancel, status, help, navigate.
        if (quick?.Intent is "cancel" or "status" or "help" or "navigate")
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
        //               Sprint 52: inject chat history so LLM remembers recent conversation
        var currentGoal = state.Facts.TryGetValue("currentGoal", out var cg) && cg is string s ? s : null;
        var chatHistory = history?.FormatForPrompt();
        var systemPrompt = BuildSystemPrompt(botName, botPosition, state, currentGoal,
            onlinePlayers, registeredToolNames, chatHistory);
        var userMessage = $"{username} says: \"{effective}\"";
        logger?.LogInformation("[llm] calling {Provider} ({Model}) for <{Username}> '{Message}'",
            provider.ProviderName, options.LlmModel, username,
            effective.Length > 60 ? effective[..60] : effective);

        // Sprint 51: dump full raw LLM context to dedicated audit log
        var llmStart = System.Diagnostics.Stopwatch.StartNew();
        var raw = await provider.CompleteAsync(systemPrompt, userMessage, ct);
        llmStart.Stop();
        contextLogger?.LogRequest(provider.ProviderName, options.LlmModel, username,
            systemPrompt, userMessage);
        contextLogger?.LogResponse(provider.ProviderName, options.LlmModel, raw, llmStart.Elapsed);

        // Sprint 41: log raw LLM response at Debug level for safety monitoring and debugging.
        if (raw is not null)
        {
            logger?.LogDebug("[llm] raw response from {Provider} ({Model}): {RawContent}",
                provider.ProviderName, options.LlmModel, raw);
        }

        if (raw is null)
        {
            logger?.LogWarning("[llm] {Provider} returned null -- falling back to pattern for <{Username}>",
                provider.ProviderName, username);
            return quick;
        }

        // 6. Parse LLM response into IntentDraft (Sprint 39 P1-C: ParseDecision now returns IntentDraft?)
        var llmResult = ParseDecision(raw, options.LlmConfidenceThreshold, logger);
        if (llmResult is null)
        {
            logger?.LogWarning("[llm] failed to parse JSON from {Provider} response: '{Content}'",
                provider.ProviderName, raw.Length > 200 ? raw[..200] : raw);
        }
        else
        {
            // Sprint 41: log parsed intent at Debug level for safety monitoring and debugging.
            logger?.LogDebug("[llm] parsed intent: {Intent}, item={Item}, blueprint={Blueprint}, " +
                "count={Count}, confidence={Confidence}, response={Response}",
                llmResult.Intent, llmResult.Item ?? "(null)", llmResult.Blueprint ?? "(null)",
                llmResult.Count?.ToString() ?? "(null)", llmResult.Confidence.ToString("F2"),
                llmResult.Response?.Length > 100 ? llmResult.Response[..100] : llmResult.Response ?? "(empty)");
        }

        // If low confidence with clarifying question — log it; bot.chat will be called by AgentBackgroundService
        if (llmResult?.Intent == "clarify" && !string.IsNullOrEmpty(llmResult.Response))
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
    ///
    /// Sprint 52: <paramref name="chatHistory"/> parameter injects formatted recent
    /// conversation turns so the LLM maintains context across chat messages.
    /// </summary>
    private static string BuildSystemPrompt(
        string botName, Position botPos, WorldState state,
        string? goal, int onlinePlayers,
        IReadOnlyList<string>? toolNames = null,
        string? chatHistory = null)
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

        // Sprint 52: inject recent conversation history so the LLM remembers prior turns
        var historyBlock = chatHistory is not null
            ? $"\nRecent conversation:\n{chatHistory}\n"
            : "";

        // Sprint 54 (TSK-0205/0208/0203): enriched prompt with compound commands,
        // tool auto-crafting, memory recall, and deterministic parser caveats.
        var toolList = toolNames is { Count: > 0 }
            ? $"\nAvailable bot tools: {string.Join(", ", toolNames)}."
            : "";

        var basePrompt = $$"""
        You are {{botName}}, an autonomous Minecraft bot at ({{botPos.X}},{{botPos.Y}},{{botPos.Z}}).
        Status: {{goalStatus}}. HP: {{health}}/20. Food: {{food}}/20. Players online: {{onlinePlayers}}.
        Inventory: {{invSummary}}.{{historyBlock}}{{toolList}}
        Decide if the next message is for you and what to do.
        Reply ONLY with valid JSON — no markdown, no prose:

        {
          "addressed": "yes" | "maybe" | "no",
          "intent": "gather" | "build" | "craft" | "smelt" | "navigate" | "cancel" | "status"
                  | "help" | "conversation" | "clarify" | "ignore",
          "item": "<minecraft_id or null>",
          "blueprint": "<blueprint_id or null>",
          "count": <integer or null>,
          "x": <integer or null>,
          "y": <integer or null>,
          "z": <integer or null>,
          "confidence": <0.0-1.0>,
          "clarificationQuestion": "<question to ask if confidence is low, or null>",
          "response": "<in-game reply, max 50 words, empty if intent is ignore>",
          "nextSteps": ["<optional subsequent commands the player wants, or empty array>"]
        }

        Rules:

        ADDRESSING RULES — choose EXACTLY ONE:
        - "yes" when your name is used or only 1 player is online.
        - "maybe" when it could be a command but your name is not mentioned.
        - "no" when players are talking to each other, not you.
        - "conversation" when the player is just chatting (greetings, questions, small-talk).
        - "clarify" when intent is ambiguous — set clarificationQuestion, confidence < 0.6.

        INTENT RULES — choose EXACTLY ONE:
        • "build"  — ONLY when the player says "build", "construct", "make a" + structure
        • "gather" — ONLY when the player wants to COLLECT items ("get wood", "mine stone")
        • "craft"  — ONLY when the player wants to craft an item ("make planks", "craft a pickaxe")
        • "smelt"  — ONLY when the player wants to smelt ore in a furnace ("smelt iron", "cook food").
          Set item to the ore/input (e.g. "iron_ore", "raw_iron", "copper_ore").
          NEVER use "craft" for smelting requests — that bypasses the furnace.
          CORRECT: "smelt 5 iron ore" → intent="smelt", item="iron_ore", count=5
          CORRECT: "smelt iron" → intent="smelt", item="raw_iron", count=1
        • "navigate" — when the player says "come here", "come to me", "follow me", "go to".
          Set coords to null — the system uses the player's current position.
          NEVER set intent="cancel" for "come here" — that will REJECT the command.
        WRONG: setting intent="gather" for "build a house" — this will be REJECTED.
        CORRECT: "build a house" → intent="build", blueprint="house"

        Use Minecraft item IDs without namespace prefix (oak_log, cobblestone, diamond).
        For inventory/what-do-you-have → intent "status", list inventory in response.

        BUILD COMMAND: When the user says "build <something>" (e.g. "build a house",
        "build a tower"), you MUST use intent "build" AND provide a valid "blueprint".
        Look at their exact words to determine the blueprint ID. For example:
          "build a house"      → "build", "blueprint": "house"
          "build a tower"      → "build", "blueprint": "tower"
          "build a bridge"     → "build", "blueprint": "bridge"
        The blueprint will be looked up in the blueprint repository. Do NOT simulate
        building in your response — the system handles actual construction. Set
        response to a short acknowledgement like "Starting to build a {blueprint}..."
        instead of pretending the build is complete. Never say "House built!" or
        similar completion messages — the system will report completion separately.

        COMPOUND COMMANDS: When the player chains multiple commands with "then", "and",
        or "after that" (e.g. "gather 5 oak_log then craft 20 planks then build a house"):
        - Parse the FIRST actionable step as the intent/item/blueprint/count.
        - List the REMAINING steps in "nextSteps" as an array of strings.
        - The system handles step sequencing automatically.
        - Example: "mine 10 stone then smelt it then craft a furnace"
          → intent="gather", item="stone", count=10,
            nextSteps=["smelt stone", "craft furnace"]

        TOOL AUTO-CRAFTING: The system automatically crafts required tools before
        gathering. If a player says "mine 10 stone" and no pickaxe exists, the bot
        will auto-craft a wooden pickaxe first. You do NOT need to issue a separate
        "craft pickaxe" intent — just return the gather intent and the system handles
        tool prerequisites.

        MEMORY: You can remember facts across sessions. If a player says "remember
        the password is taco" or "the base is at -200, 70, 300", the system stores
        it. If a player asks about previously stored facts, the system recalls them.
        Use intent="conversation" with a natural response for memory-related chat.

        CAVEAT: The deterministic command parser (non-LLM fallback) has known bugs.
        It may misinterpret messages containing "help" or "stop" in longer sentences.
        If you see unexpected behavior (e.g. emergency stop when none was requested),
        use intent="conversation" to explain what happened to the player.
        """;

        return basePrompt;
    }

    // -- Response parsing ------------------------------------------------------------------

    private static readonly Regex CodeFenceRegex =
        new(@"```(?:json)?\s*(?<body>[\s\S]*?)```", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BraceRegex =
        new(@"\{[\s\S]*\}", RegexOptions.Compiled);

    /// <summary>
    /// Sprint 35 P1-B: ParseDecision reads the IntentDraft JSON schema from the LLM.
    /// Handles confidence threshold — low confidence + clarificationQuestion → clarify intent.
    ///
    /// Sprint 39 P1-C: now returns <see cref="IntentDraft"/>? directly. IntentManager is
    /// no longer called here — the parser is purely a JSON → IntentDraft converter.
    /// Goal creation is the caller's responsibility (AgentBackgroundService via IntentManager).
    /// AGENTS.md CRITICAL rule: parsers never create goals.
    /// </summary>
    private static IntentDraft? ParseDecision(
        string content, double confidenceThreshold,
        ILogger<LlmChatInterpreter>? logger)
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
                return TryParseTruncatedJson(json, logger);
            }

            using var doc  = JsonDocument.Parse(m.Value);
            var root       = doc.RootElement;
            var addressed  = GetStr(root, "addressed") ?? "no";

            if (string.Equals(addressed, "no", StringComparison.OrdinalIgnoreCase))
                return null;   // not addressed → caller returns null

            var intent   = GetStr(root, "intent") ?? "ignore";
            var response = GetStr(root, "response") ?? string.Empty;
            var item      = GetStr(root, "item");
            var blueprint = GetStr(root, "blueprint");
            var count     = GetInt(root, "count");
            var x         = GetInt(root, "x");
            var y         = GetInt(root, "y");
            var z         = GetInt(root, "z");

            // Sprint 35 P1-A: read confidence and clarificationQuestion from IntentDraft schema
            var confidence = GetDouble(root, "confidence") ?? 1.0;
            var clarifyQ   = GetStr(root, "clarificationQuestion");

            // Sprint 54 (TSK-0205): parse nextSteps for compound command chaining
            IReadOnlyList<string>? nextSteps = null;
            if (root.TryGetProperty("nextSteps", out var ns) && ns.ValueKind == JsonValueKind.Array)
            {
                var steps = new List<string>();
                foreach (var step in ns.EnumerateArray())
                {
                    if (step.ValueKind == JsonValueKind.String)
                        steps.Add(step.GetString()!);
                }
                if (steps.Count > 0) nextSteps = steps;
            }

            // Low confidence → override intent to "clarify" (bot sends the question as response)
            if (confidence < confidenceThreshold && !string.IsNullOrWhiteSpace(clarifyQ))
            {
                logger?.LogDebug("[llm] confidence={Confidence:F2} < threshold={Threshold:F2} — requesting clarification",
                    confidence, confidenceThreshold);
                return new IntentDraft(addressed, "clarify",
                    item, blueprint, count, x, y, z,
                    confidence, clarifyQ, clarifyQ, nextSteps);
            }

            return new IntentDraft(addressed, intent,
                item, blueprint, count, x, y, z,
                confidence, clarifyQ, response, nextSteps);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "LlmChatInterpreter.ParseDecision: Failed to parse LLM response");
            return null;
        }
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
    ///
    /// Sprint 39 P1-C: return type changed to <see cref="IntentDraft"/>? (was ChatInterpretation?).
    /// Now a pure JSON-fragment → IntentDraft converter; IntentManager and goal-name strings
    /// are gone. Callers (Sprint21Tests) now assert on IntentDraft fields (Item, Count, Intent).
    /// </summary>
    private static IntentDraft? TryParseTruncatedJson(string json, ILogger? logger = null)
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
                return null;   // not addressed

            var intent = intentM.Success ? intentM.Groups["v"].Value : "ignore";

            var responseM = Regex.Match(json,
                @"""response""\s*:\s*""(?<v>[^""\\]*(?:\\.[^""\\]*)*)""");
            var response = responseM.Success ? responseM.Groups["v"].Value : string.Empty;

            var itemM  = Regex.Match(json, @"""item""\s*:\s*""(?<v>[^""]+)""",      RegexOptions.IgnoreCase);
            var bpM    = Regex.Match(json, @"""blueprint""\s*:\s*""(?<v>[^""]+)""", RegexOptions.IgnoreCase);
            var countM = Regex.Match(json, @"""count""\s*:\s*(?<v>\d+)",             RegexOptions.IgnoreCase);

            var item      = itemM.Success  ? itemM.Groups["v"].Value  : null;
            var blueprint = bpM.Success    ? bpM.Groups["v"].Value    : null;
            int? count    = countM.Success && int.TryParse(countM.Groups["v"].Value, out var c) ? c : null;

            return new IntentDraft(addressed, intent,
                item, blueprint, count,
                null, null, null,
                1.0, null, response);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "LlmChatInterpreter.TryParseTruncatedJson: Failed to salvage truncated JSON");
            return null;
        }
    }

    /// <summary>
    /// Horizontal distance (X/Z only). AUD-48-003: delegates to shared
    /// <see cref="ChatDistance.Horizontal"/> so deterministic and LLM chat
    /// paths use the same calculation — vertical separation does not affect
    /// whether a player is within chat-hearing range in Minecraft.
    /// </summary>
    private static double Distance(Position a, Position b) =>
        ChatDistance.Horizontal(a, b);
}
