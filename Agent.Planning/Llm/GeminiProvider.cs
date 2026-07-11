namespace Agent.Planning.Llm;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// <see cref="ILlmProvider"/> for the Google Gemini API.
///
/// API: POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}
/// Request: { systemInstruction: { parts: [{text}] }, contents: [{role: "user", parts: [{text}]}] }
/// Response: { candidates: [{ content: { parts: [{ text: "..." }] } }] }
///
/// Config:
///   LlmProvider: "gemini"
///   LlmModel: "gemini-2.0-flash"  (or gemini-1.5-pro for stronger reasoning)
///   LlmApiKey: "AIza..."  (from https://aistudio.google.com/apikey)
/// </summary>
public sealed class GeminiProvider(HttpClient http, ChatOptions options,
    ILogger<GeminiProvider>? logger = null) : ILlmProvider
{
    private readonly ILogger<GeminiProvider> _logger = logger ?? NullLogger<GeminiProvider>.Instance;

    public string ProviderName => "gemini";
    public bool IsAvailable    => options.LlmEnabled
                               && string.Equals(options.LlmProvider, "gemini",
                                                StringComparison.OrdinalIgnoreCase);

    public async Task<string?> CompleteAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        if (!IsAvailable) return null;

        // Sprint 60 (TSK-0399): Guard against null/empty API key.
        if (string.IsNullOrWhiteSpace(options.LlmApiKey))
        {
            _logger.LogWarning("GeminiProvider: LlmApiKey is null or empty");
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.LlmTimeoutSeconds));

        try
        {
            // Sprint 60 (TSK-0399): API key moved from URL query string to
            // X-Goog-Api-Key header to prevent key leakage in server logs,
            // crash dumps, and HttpRequestException messages.
            var endpoint = $"/v1beta/models/{options.LlmModel}:generateContent";
            var request  = new GeminiRequest
            {
                SystemInstruction = new GeminiContent { Parts = [new GeminiPart { Text = systemPrompt }] },
                Contents          =
                [
                    new GeminiContent
                    {
                        Role  = "user",
                        Parts = [new GeminiPart { Text = userMessage }],
                    }
                ],
                // Sprint 60 (TSK-0400): Add generation config to cap response length.
                GenerationConfig = new GeminiGenerationConfig
                {
                    MaxOutputTokens = options.LlmMaxResponseTokens > 0
                        ? options.LlmMaxResponseTokens
                        : 512,
                },
            };

            var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Add("X-Goog-Api-Key", options.LlmApiKey);
            req.Content = JsonContent.Create(request);

            var response = await http.SendAsync(req, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cts.Token);
                _logger.LogWarning("GeminiProvider: HTTP {Status} from {Endpoint}: {Body}",
                    (int)response.StatusCode, endpoint, body);
                return null;
            }

            var result = await response.Content
                .ReadFromJsonAsync<GeminiResponse>(cancellationToken: cts.Token);

            return result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        }
        catch (OperationCanceledException ex) { _logger.LogWarning(ex, "GeminiProvider.CompleteAsync: Operation cancelled"); return null; }
        catch (HttpRequestException ex)        { _logger.LogWarning(ex, "GeminiProvider.CompleteAsync: HTTP error"); return null; }
        catch (Exception ex)                   { _logger.LogWarning(ex, "GeminiProvider.CompleteAsync: Unexpected error"); return null; }
    }

    // ── Wire types ────────────────────────────────────────────────────────────

    private sealed class GeminiRequest
    {
        [JsonPropertyName("systemInstruction")] public GeminiContent? SystemInstruction { get; set; }
        [JsonPropertyName("contents")]          public GeminiContent[] Contents { get; set; } = [];
        [JsonPropertyName("generationConfig")]  public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    /// <summary>
    /// Sprint 60 (TSK-0400): Generation config to control response length.
    /// Maps to https://ai.google.dev/api/generate-content#generationconfig
    /// </summary>
    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("maxOutputTokens")] public int MaxOutputTokens { get; set; } = 512;
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("role")]  public string? Role  { get; set; }
        [JsonPropertyName("parts")] public GeminiPart[] Parts { get; set; } = [];
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    }

    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")] public GeminiCandidate[]? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")] public GeminiContent? Content { get; set; }
    }
}
