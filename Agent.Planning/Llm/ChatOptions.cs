namespace Agent.Planning.Llm;

/// <summary>
/// Unified configuration for the chat interpretation system.
/// Bind from the "Agent:Chat" configuration section.
///
/// Example appsettings.json:
/// <code>
/// "Agent": {
///   "Chat": {
///     "LlmEnabled": true,
///     "LlmProvider": "ollama",
///     "LlmModel": "llama3.2",
///     "LlmBaseUrl": "http://localhost:11434",
///     "LlmTimeoutSeconds": 10,
///     "PlayerCooldownSeconds": 3,
///     "GlobalPerMinuteMax": 5,
///     "MaxMessageLength": 1024,
///     "MaxResponseDistanceBlocks": 64.0,
///     "ConversationWindowSeconds": 60
///   }
/// }
/// </code>
///
/// Quick-start (Ollama):
///   1. Install Ollama: https://ollama.com
///   2. Pull a model: ollama pull llama3.2
///   3. Set LlmEnabled: true
/// </summary>
public sealed class ChatOptions
{
    // ── LLM provider ──────────────────────────────────────────────────────────

    /// <summary>Enable LLM-powered chat interpretation. Default: false (pattern-matching only).</summary>
    public bool LlmEnabled { get; init; } = false;

    /// <summary>
    /// LLM provider identifier. Case-insensitive.
    /// Supported: ollama | openai | openrouter | deepseek | github-copilot | anthropic | gemini
    /// </summary>
    public string LlmProvider { get; init; } = "ollama";

    /// <summary>Model name passed to the provider, e.g. "llama3.2", "gpt-4o", "claude-3-5-sonnet-20241022".</summary>
    public string LlmModel { get; init; } = "llama3.2";

    /// <summary>
    /// Provider base URL. Defaults to the provider's standard endpoint when empty.
    /// Ollama: http://localhost:11434
    /// OpenAI: https://api.openai.com
    /// OpenRouter: https://openrouter.ai/api
    /// DeepSeek: https://api.deepseek.com
    /// Anthropic: https://api.anthropic.com
    /// Gemini: https://generativelanguage.googleapis.com
    /// </summary>
    public string LlmBaseUrl { get; init; } = string.Empty;

    /// <summary>API key for cloud providers (OpenAI, Anthropic, Gemini, etc.). Not required for Ollama.</summary>
    public string? LlmApiKey { get; init; }

    /// <summary>Per-request LLM timeout in seconds. Default: 10.</summary>
    public int LlmTimeoutSeconds { get; init; } = 10;

    // ── Rate limiting ─────────────────────────────────────────────────────────

    /// <summary>Minimum seconds between LLM calls for the same player. Default: 3.</summary>
    public int PlayerCooldownSeconds { get; init; } = 3;

    /// <summary>Maximum LLM calls across all players per minute. Default: 5.</summary>
    public int GlobalPerMinuteMax { get; init; } = 5;

    // ── Chat behaviour ────────────────────────────────────────────────────────

    /// <summary>
    /// Maximum in-game message length accepted for interpretation.
    /// Messages longer than this are truncated before processing.
    /// Prevents excessively long prompts from being sent to the LLM. Default: 1024.
    /// </summary>
    public int MaxMessageLength { get; init; } = 1024;

    /// <summary>
    /// Distance (blocks) beyond which the bot ignores messages not directed at it by name.
    /// Implements the "closest agent responds" heuristic for multi-bot deployments. Default: 64.
    /// </summary>
    public double MaxResponseDistanceBlocks { get; init; } = 64.0;

    /// <summary>
    /// Seconds after the bot last spoke during which any message is treated as a continuation
    /// of the conversation (directed at the bot). Default: 60.
    /// </summary>
    public int ConversationWindowSeconds { get; init; } = 60;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Resolved base URL, applying provider-specific defaults when LlmBaseUrl is empty.</summary>
    public string ResolvedBaseUrl => string.IsNullOrEmpty(LlmBaseUrl)
        ? LlmProvider.ToLowerInvariant() switch
        {
            "openai"         => "https://api.openai.com",
            "openrouter"     => "https://openrouter.ai/api",
            "deepseek"       => "https://api.deepseek.com",
            "github-copilot" => "https://api.githubcopilot.com",
            "anthropic"      => "https://api.anthropic.com",
            "gemini"         => "https://generativelanguage.googleapis.com",
            _                => "http://localhost:11434", // ollama
        }
        : LlmBaseUrl;
}
