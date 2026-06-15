namespace Agent.Vision;

/// <summary>
/// Multimodal (aesthetic) vision model client.
/// Takes a screenshot of a Minecraft build and returns actionable feedback
/// (e.g. "windows are too small, towers uneven") that the agent uses to
/// refine its construction plan.
///
/// Backed by Ollama (local Qwen/Gemma vision) or OpenAI Vision API.
/// Abstracted so providers can be swapped via Microsoft.Extensions.AI.
/// </summary>
public interface IVisionModel
{
    Task<string> CritiqueAsync(byte[] screenshotBytes, string stylePrompt, CancellationToken cancellationToken = default);
}
