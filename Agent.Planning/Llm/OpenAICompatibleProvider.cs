namespace Agent.Planning.Llm;

using System.Net.Http.Json;
using System.Text.Json.Serialization;

/// <summary>
/// <see cref="ILlmProvider"/> for OpenAI chat completions API and compatible services.
///
/// This single implementation covers:
///   - OpenAI (https://api.openai.com/v1/chat/completions)
///   - OpenRouter (https://openrouter.ai/api/v1/chat/completions)
///   - DeepSeek (https://api.deepseek.com/v1/chat/completions)
///   - GitHub Copilot Enterprise (https://api.githubcopilot.com/v1/chat/completions)
///
/// All use the same JSON format with Bearer token authentication.
/// Set <see cref="ChatOptions.LlmProvider"/> to the service slug and provide
/// <see cref="ChatOptions.LlmApiKey"/> from the service's dashboard.
///
/// Config examples:
///   LlmProvider: "openai",    LlmModel: "gpt-4o",                    LlmApiKey: "sk-..."
///   LlmProvider: "openrouter", LlmModel: "mistralai/mistral-7b-instruct", LlmApiKey: "sk-or-..."
///   LlmProvider: "deepseek",  LlmModel: "deepseek-chat",             LlmApiKey: "..."
/// </summary>
public sealed class OpenAICompatibleProvider(HttpClient http, ChatOptions options) : ILlmProvider
{
    private static readonly HashSet<string> SupportedProviders =
        new(StringComparer.OrdinalIgnoreCase)
        { "openai", "openrouter", "deepseek", "github-copilot" };

    public string ProviderName => options.LlmProvider;
    public bool IsAvailable    => options.LlmEnabled
                               && SupportedProviders.Contains(options.LlmProvider);

    public async Task<string?> CompleteAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        if (!IsAvailable) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.LlmTimeoutSeconds));

        try
        {
            var request = new OpenAIChatRequest
            {
                Model      = options.LlmModel,
                Messages   =
                [
                    new OpenAIMessage { Role = "system", Content = systemPrompt },
                    new OpenAIMessage { Role = "user",   Content = userMessage  },
                ],
                MaxTokens  = 512,
                Stream     = false,
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
            {
                Content = JsonContent.Create(request),
            };
            if (!string.IsNullOrEmpty(options.LlmApiKey))
                req.Headers.Authorization = new("Bearer", options.LlmApiKey);

            var response = await http.SendAsync(req, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content
                .ReadFromJsonAsync<OpenAIChatResponse>(cancellationToken: cts.Token);

            return result?.Choices?.FirstOrDefault()?.Message?.Content;
        }
        catch (OperationCanceledException) { return null; }
        catch (HttpRequestException)        { return null; }
        catch                               { return null; }
    }

    // ── Wire types ────────────────────────────────────────────────────────────

    private sealed class OpenAIChatRequest
    {
        [JsonPropertyName("model")]      public string Model     { get; set; } = string.Empty;
        [JsonPropertyName("messages")]   public OpenAIMessage[] Messages { get; set; } = [];
        [JsonPropertyName("max_tokens")] public int MaxTokens   { get; set; } = 512;
        [JsonPropertyName("stream")]     public bool Stream      { get; set; } = false;
    }

    private sealed class OpenAIMessage
    {
        [JsonPropertyName("role")]    public string Role    { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    private sealed class OpenAIChatResponse
    {
        [JsonPropertyName("choices")] public OpenAIChoice[]? Choices { get; set; }
    }

    private sealed class OpenAIChoice
    {
        [JsonPropertyName("message")] public OpenAIMessage? Message { get; set; }
    }
}
