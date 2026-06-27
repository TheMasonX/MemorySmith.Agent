namespace Agent.Planning.Llm;

using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

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
public sealed class OllamaProvider(HttpClient http, ChatOptions options,
    ILogger<OllamaProvider>? logger = null) : ILlmProvider
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

        var sw = Stopwatch.StartNew();

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
                Stream  = false,
                // Sprint 20: cap response length to avoid JSON truncation mid-brace.
                Options = options.LlmMaxResponseTokens > 0
                    ? new OllamaOptions { NumPredict = options.LlmMaxResponseTokens }
                    : null,
            };

            // Sprint 41: log full request at Debug level for safety monitoring and debugging.
            // Production Info-level stays summary-only to avoid log flooding.
            logger?.LogDebug("[ollama] request to {Model}: system={SysLen} chars, user={UserLen} chars\n{sysPrompt}",
                options.LlmModel, systemPrompt.Length, userMessage.Length, systemPrompt);
            logger?.LogDebug("[ollama] user message for {Model}: {UserMsg}",
                options.LlmModel, userMessage);

            var response = await http.PostAsJsonAsync("/api/chat", request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cts.Token);
                logger?.LogWarning("[ollama] HTTP {Status} from /api/chat: {Body}",
                    (int)response.StatusCode,
                    body.Length > 200 ? body[..200] : body);
                return null;
            }

            var result = await response.Content
                .ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cts.Token);

            var responseContent = result?.Message?.Content;

            // Sprint 41: log full response at Debug level for safety monitoring and debugging.
            if (responseContent is not null)
            {
                logger?.LogDebug("[ollama] response from {Model} ({Elapsed}s): {Response}",
                    options.LlmModel, sw.Elapsed.TotalSeconds.ToString("F1"), responseContent);
                logger?.LogInformation("[ollama] {Model} responded ({RespLen} chars) in {Elapsed}s",
                    options.LlmModel, responseContent.Length,
                    sw.Elapsed.TotalSeconds.ToString("F1"));
            }
            else
            {
                logger?.LogWarning("[ollama] {Model} returned empty response after {Elapsed}s",
                    options.LlmModel, sw.Elapsed.TotalSeconds.ToString("F1"));
            }

            return responseContent;
        }
        catch (OperationCanceledException)
        {
            logger?.LogWarning("[ollama] request timed out after {Timeout}s for model {Model}",
                options.LlmTimeoutSeconds, options.LlmModel);
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger?.LogWarning(ex, "[ollama] HTTP request failed for {Url}/api/chat: {Message}",
                http.BaseAddress, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[ollama] unexpected error calling {Model}: {Message}",
                options.LlmModel, ex.Message);
            return null;
        }
    }

    // ── Ollama wire types ─────────────────────────────────────────────────────

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]    public string Model    { get; set; } = string.Empty;
        [JsonPropertyName("messages")] public OllamaMessage[] Messages { get; set; } = [];
        [JsonPropertyName("stream")]   public bool Stream { get; set; } = false;
        // Sprint 20: optional generation parameters
        [JsonPropertyName("options")]  public OllamaOptions? Options   { get; set; }
    }

    private sealed class OllamaOptions
    {
        [JsonPropertyName("num_predict")] public int NumPredict { get; set; }
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
