namespace Agent.Planning;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agent.Core;

/// <summary>
/// <see cref="IChatLlmClient"/> implementation that calls a locally-running Ollama
/// instance (<c>http://localhost:11434</c> by default).
///
/// Uses the Ollama <c>/api/chat</c> endpoint (OpenAI-compatible chat messages format).
/// Requests stream=false, enforces a 5-second timeout per call.
///
/// System prompt instructs the model to respond ONLY with a small JSON object.
/// The JSON is parsed from the response content; markdown code fences are stripped.
/// Returns null on any failure (network error, timeout, unparseable JSON) so the
/// caller can fall back to pattern matching.
///
/// Model: configurable via <see cref="LlmOptions.Model"/> (default: llama3.2).
/// The model must be pulled in Ollama beforehand: <c>ollama pull llama3.2</c>.
/// </summary>
public sealed class OllamaLlmClient(HttpClient http, LlmOptions options) : IChatLlmClient
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    // ── System prompt ──────────────────────────────────────────────────

    private static string BuildSystemPrompt(
        string botName, Position botPos, string? goal, int onlinePlayers)
    {
        var goalDesc = goal is not null ? $"current goal: {goal}" : "currently idle";
        return $"""
            You are {botName}, an autonomous Minecraft agent at position ({botPos.X},{botPos.Y},{botPos.Z}).
            Status: {goalDesc}. Other players online: {onlinePlayers}.

            When a player speaks in chat, decide if the message is for you and what to do.
            Respond ONLY with valid JSON matching this exact schema — no prose, no markdown:

            {{
              "addressed": "yes" | "maybe" | "no",
              "intent": "gather" | "build" | "cancel" | "status" | "help" | "navigate" | "ignore" | "clarify",
              "item": "<minecraft_item_id or null>",
              "blueprint": "<blueprint_id or null>",
              "count": <integer or null>,
              "x": <integer or null>,
              "y": <integer or null>,
              "z": <integer or null>,
              "response": "<brief in-game chat reply, max 50 words, or empty string>"
            }}

            Rules:
            - "yes": your name is mentioned, OR only 1 player is online.
            - "maybe": command-like but your name is not mentioned and multiple players are online.
            - "no": clearly a conversation between players that does not involve you.
            - "clarify": you think the message might be for you but are unsure — ask politely.
            - Keep responses friendly, helpful, and brief. Stay in character as a helpful Minecraft bot.
            - Use Minecraft item IDs without namespace (e.g. oak_log, cobblestone, iron_ore).
            """;
    }

    // ── Public API ────────────────────────────────────────────────────

    public async Task<ChatInterpretation?> EvaluateAsync(
        string botName, Position botPosition,
        string username, string message,
        int onlinePlayers, Position? playerPosition,
        string? currentGoal,
        CancellationToken ct = default)
    {
        if (!options.Enabled) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallTimeout);

        try
        {
            var requestBody = new
            {
                model    = options.Model,
                messages = new object[]
                {
                    new { role = "system", content = BuildSystemPrompt(botName, botPosition, currentGoal, onlinePlayers) },
                    new { role = "user",   content = $"{username} says: \"{message}\"" },
                },
                stream = false,
            };

            var response = await http.PostAsJsonAsync("/api/chat", requestBody, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(
                cancellationToken: cts.Token);

            return result?.Message?.Content is null
                ? null
                : ParseDecision(result.Message.Content);
        }
        catch (OperationCanceledException) { return null; } // timed out
        catch (HttpRequestException)        { return null; } // Ollama not running
        catch                               { return null; } // any other error
    }

    // ── JSON parsing ──────────────────────────────────────────────────

    private static ChatInterpretation? ParseDecision(string content)
    {
        try
        {
            // Strip markdown code fences if present (e.g. ```json ... ```)
            var jsonContent = CodeFenceRegex.IsMatch(content)
                ? CodeFenceRegex.Match(content).Groups["body"].Value
                : content;

            // Extract the first {...} block (handles prose prefix/suffix)
            var braceMatch = BraceRegex.Match(jsonContent);
            if (!braceMatch.Success) return null;

            using var doc = JsonDocument.Parse(braceMatch.Value);
            var root = doc.RootElement;

            var addressed = GetString(root, "addressed", "no");
            if (string.Equals(addressed, "no", StringComparison.OrdinalIgnoreCase))
                return new ChatInterpretation(ChatIntentType.NotAddressed);

            var isUncertain = string.Equals(addressed, "maybe", StringComparison.OrdinalIgnoreCase);
            var intent      = GetString(root, "intent", "ignore");
            var response    = GetString(root, "response", string.Empty);

            string? goalName = null;
            IReadOnlyDictionary<string, object?>? parameters = null;

            switch (intent?.ToLowerInvariant())
            {
                case "gather":
                    var item = GetString(root, "item", null);
                    if (item is not null)
                    {
                        goalName = $"GatherItem:{item}";
                        var count = GetInt(root, "count", 10);
                        parameters = new Dictionary<string, object?> { ["count"] = count };
                    }
                    break;

                case "build":
                    var bp = GetString(root, "blueprint", null);
                    if (bp is not null) goalName = $"Build:{bp}";
                    break;

                case "navigate":
                    var x = GetInt(root, "x", null);
                    var y = GetInt(root, "y", null);
                    var z = GetInt(root, "z", null);
                    if (x is not null && y is not null && z is not null)
                    {
                        goalName = "MoveTo";
                        parameters = new Dictionary<string, object?>
                        {
                            ["x"] = x, ["y"] = y, ["z"] = z,
                        };
                    }
                    break;
            }

            var intentType = intent?.ToLowerInvariant() switch
            {
                "gather"   => ChatIntentType.CreateGoal,
                "build"    => ChatIntentType.CreateGoal,
                "cancel"   => ChatIntentType.CancelGoal,
                "status"   => ChatIntentType.QueryStatus,
                "help"     => ChatIntentType.QueryHelp,
                "navigate" => ChatIntentType.NavigateTo,
                "clarify"  => ChatIntentType.Unknown,  // bot will ask a clarifying question
                _          => isUncertain ? ChatIntentType.Unknown : ChatIntentType.NotAddressed,
            };

            return new ChatInterpretation(intentType, goalName, parameters, response ?? string.Empty);
        }
        catch { return null; }
    }

    private static string? GetString(JsonElement root, string key, string? fallback)
    {
        if (root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? fallback;
        return fallback;
    }

    private static int? GetInt(JsonElement root, string key, int? fallback)
    {
        if (root.TryGetProperty(key, out var el))
        {
            if (el.ValueKind == JsonValueKind.Number) return el.GetInt32();
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var i)) return i;
        }
        return fallback;
    }

    private static readonly Regex CodeFenceRegex = new(
        @"```(?:json)?\s*(?<body>[\s\S]*?)```",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BraceRegex = new(
        @"\{[\s\S]*\}",
        RegexOptions.Compiled);

    // ── Ollama response DTOs ──────────────────────────────────────────

    private sealed class OllamaResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public OllamaMessageContent? Message { get; set; }
    }

    private sealed class OllamaMessageContent
    {
        [System.Text.Json.Serialization.JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}

/// <summary>Configuration for the LLM client. Bind from "Agent:Llm" config section.</summary>
public sealed class LlmOptions
{
    /// <summary>Whether LLM chat interpretation is enabled. Default: false (opt-in).</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Base URL of the Ollama server. Default: http://localhost:11434</summary>
    public string OllamaUrl { get; init; } = "http://localhost:11434";

    /// <summary>Ollama model name. Must be pulled before use. Default: llama3.2</summary>
    public string Model { get; init; } = "llama3.2";
}
