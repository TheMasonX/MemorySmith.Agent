namespace Agent.Planning.Llm;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// <see cref="ILlmProvider"/> for the Anthropic Claude API.
///
/// API: POST https://api.anthropic.com/v1/messages
/// Headers: x-api-key, anthropic-version: 2023-06-01
/// Request: { model, max_tokens, system, messages: [{role: "user", content}] }
/// Response: { content: [{ type: "text", text: "..." }] }
///
/// Config:
///   LlmProvider: "anthropic"
///   LlmModel: "claude-3-5-sonnet-20241022"  (or claude-3-haiku-20240307 for speed)
///   LlmApiKey: "sk-ant-..."  (from https://console.anthropic.com)
/// </summary>
public sealed class AnthropicProvider(HttpClient http, ChatOptions options,
    ILogger<AnthropicProvider>? logger = null) : ILlmProvider
{
    private readonly ILogger<AnthropicProvider> _logger = logger ?? NullLogger<AnthropicProvider>.Instance;

    public string ProviderName => "anthropic";
    public bool IsAvailable    => options.LlmEnabled
                               && string.Equals(options.LlmProvider, "anthropic",
                                                StringComparison.OrdinalIgnoreCase);

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
            var request = new AnthropicRequest
            {
                Model     = options.LlmModel,
                MaxTokens = 512,
                System    = systemPrompt,
                Messages  = [new AnthropicMessage { Role = "user", Content = userMessage }],
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
            {
                Content = JsonContent.Create(request),
            };
            req.Headers.Add("x-api-key", options.LlmApiKey ?? string.Empty);
            req.Headers.Add("anthropic-version", "2023-06-01");

            var response = await http.SendAsync(req, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content
                .ReadFromJsonAsync<AnthropicResponse>(cancellationToken: cts.Token);

            return result?.Content?.FirstOrDefault(c =>
                string.Equals(c.Type, "text", StringComparison.OrdinalIgnoreCase))?.Text;
        }
        catch (OperationCanceledException ex) { _logger.LogWarning(ex, "AnthropicProvider.CompleteAsync: Operation cancelled"); return null; }
        catch (HttpRequestException ex)        { _logger.LogWarning(ex, "AnthropicProvider.CompleteAsync: HTTP error"); return null; }
        catch (Exception ex)                   { _logger.LogWarning(ex, "AnthropicProvider.CompleteAsync: Unexpected error"); return null; }
    }

    // ── Wire types ────────────────────────────────────────────────────────────

    private sealed class AnthropicRequest
    {
        [JsonPropertyName("model")]      public string Model    { get; set; } = string.Empty;
        [JsonPropertyName("max_tokens")] public int MaxTokens  { get; set; } = 512;
        [JsonPropertyName("system")]     public string System   { get; set; } = string.Empty;
        [JsonPropertyName("messages")]   public AnthropicMessage[] Messages { get; set; } = [];
    }

    private sealed class AnthropicMessage
    {
        [JsonPropertyName("role")]    public string Role    { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    private sealed class AnthropicResponse
    {
        [JsonPropertyName("content")] public AnthropicContent[]? Content { get; set; }
    }

    private sealed class AnthropicContent
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
}
