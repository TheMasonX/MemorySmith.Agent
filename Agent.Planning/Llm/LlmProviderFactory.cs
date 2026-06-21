namespace Agent.Planning.Llm;

using Microsoft.Extensions.Logging;

/// <summary>
/// Creates the appropriate <see cref="ILlmProvider"/> from <see cref="ChatOptions"/>.
///
/// Supported providers:
///   ollama         → <see cref="OllamaProvider"/>
///   openai         → <see cref="OpenAICompatibleProvider"/>
///   openrouter     → <see cref="OpenAICompatibleProvider"/>
///   deepseek       → <see cref="OpenAICompatibleProvider"/>
///   github-copilot → <see cref="OpenAICompatibleProvider"/>
///   anthropic      → <see cref="AnthropicProvider"/>
///   gemini         → <see cref="GeminiProvider"/>
///
/// All providers receive an <see cref="HttpClient"/> whose BaseAddress is set to
/// <see cref="ChatOptions.ResolvedBaseUrl"/>. The caller (DI container) should
/// register a named HttpClient "llm" with the correct base address and pass it
/// to <see cref="Create"/>.
/// </summary>
public static class LlmProviderFactory
{
    /// <summary>Creates and returns the configured provider. Never returns null.</summary>
    /// <exception cref="NotSupportedException">Unknown provider slug in <paramref name="options"/>.</exception>
    public static ILlmProvider Create(HttpClient http, ChatOptions options,
        ILogger<OllamaProvider>? ollamaLogger = null) =>
        options.LlmProvider.ToLowerInvariant() switch
        {
            "ollama"         => new OllamaProvider(http, options, ollamaLogger),
            "openai"
            or "openrouter"
            or "deepseek"
            or "github-copilot" => new OpenAICompatibleProvider(http, options),
            "anthropic"      => new AnthropicProvider(http, options),
            "gemini"         => new GeminiProvider(http, options),
            var p            => throw new NotSupportedException(
                $"Unknown LLM provider '{p}'. " +
                "Supported: ollama, openai, openrouter, deepseek, github-copilot, anthropic, gemini"),
        };
}
