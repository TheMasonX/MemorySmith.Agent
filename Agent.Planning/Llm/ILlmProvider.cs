namespace Agent.Planning.Llm;

/// <summary>
/// Abstraction over a language model completion endpoint.
///
/// Returns the raw text completion or null on failure (timeout, network error,
/// model unavailable). Callers are responsible for prompt construction and
/// response parsing — this interface deliberately stays thin.
///
/// Supported providers (see <see cref="LlmProviderFactory"/>):
///   ollama       — Local Ollama server (/api/chat)
///   openai       — OpenAI API (/v1/chat/completions)
///   openrouter   — OpenRouter (/v1/chat/completions, OpenAI-compatible)
///   deepseek     — DeepSeek (/v1/chat/completions, OpenAI-compatible)
///   github-copilot — GitHub Copilot Enterprise (/v1/chat/completions, OpenAI-compatible)
///   anthropic    — Anthropic Claude (/v1/messages)
///   gemini       — Google Gemini (/v1beta/models/{model}:generateContent)
/// </summary>
public interface ILlmProvider
{
    /// <summary>Provider identifier matching <see cref="ChatOptions.LlmProvider"/>.</summary>
    string ProviderName { get; }

    /// <summary>True if the provider is configured and can accept requests.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Sends a chat completion request.
    ///
    /// <paramref name="systemPrompt"/> sets the model's persona and output format.
    /// <paramref name="userMessage"/> is the content to reason about.
    ///
    /// Returns null on any failure — callers MUST handle null gracefully and fall
    /// back to deterministic logic (D-003).
    /// </summary>
    Task<string?> CompleteAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default);
}
