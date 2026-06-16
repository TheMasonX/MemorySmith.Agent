namespace Agent.Planning.Llm;

using System.Net.Http.Json;
using System.Text.Json.Serialization;

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
public sealed class GeminiProvider(HttpClient http, ChatOptions options) : ILlmProvider
{
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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.LlmTimeoutSeconds));

        try
        {
            var endpoint = $"/v1beta/models/{options.LlmModel}:generateContent?key={options.LlmApiKey}";
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
            };

            var response = await http.PostAsJsonAsync(endpoint, request, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content
                .ReadFromJsonAsync<GeminiResponse>(cancellationToken: cts.Token);

            return result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        }
        catch (OperationCanceledException) { return null; }
        catch (HttpRequestException)        { return null; }
        catch                               { return null; }
    }

    // ── Wire types ────────────────────────────────────────────────────────────

    private sealed class GeminiRequest
    {
        [JsonPropertyName("systemInstruction")] public GeminiContent? SystemInstruction { get; set; }
        [JsonPropertyName("contents")]          public GeminiContent[] Contents { get; set; } = [];
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
