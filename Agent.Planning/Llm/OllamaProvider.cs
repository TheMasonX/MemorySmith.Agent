namespace Agent.Planning.Llm;

using System.Net.Http.Json;
using System.Text.Json.Serialization;

/// <summary>
/// <see cref="ILlmProvider"/> for a locally-running Ollama server.
///
/// API: POST {baseUrl}/api/chat
/// Request: { model, messages: [{role, content}], stream: false }
/// Response: { message: { role: "assistant", content: "..." }, done: true }
///
/// Setup:
///   1. Install: https://ollama.com
///   2. Pull:    ollama pull llama3.2
///   3. Config:  Agent:Chat:LlmEnabled=true, LlmProvider=ollama
/// </summary>
public sealed class OllamaProvider(HttpClient http, ChatOptions options) : ILlmProvider
{
    public string ProviderName => "ollama";
    public bool IsAvailable    => options.LlmEnabled
                               && string.Equals(options.LlmProvider, "ollama",
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
            var request = new OllamaChatRequest
            {
                Model    = options.LlmModel,
                Messages =
                [
                    new OllamaMessage { Role = "system", Content = systemPrompt },
                    new OllamaMessage { Role = "user",   Content = userMessage  },
                ],
                Stream = false,
            };

            var response = await http.PostAsJsonAsync("/api/chat", request, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content
                .ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cts.Token);

            return result?.Message?.Content;
        }
        catch (OperationCanceledException) { return null; }
        catch (HttpRequestException)        { return null; }
        catch                               { return null; }
    }

    // ── Ollama wire types ─────────────────────────────────────────────────────

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]    public string Model    { get; set; } = string.Empty;
        [JsonPropertyName("messages")] public OllamaMessage[] Messages { get; set; } = [];
        [JsonPropertyName("stream")]   public bool Stream { get; set; } = false;
    }

    private sealed class OllamaMessage
    {
        [JsonPropertyName("role")]    public string Role    { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    private sealed class OllamaChatResponse
    {
        [JsonPropertyName("message")] public OllamaMessage? Message { get; set; }
    }
}
