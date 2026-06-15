namespace Agent.Personality;

/// <summary>
/// Persistent agent identity. Stored as a MemorySmith page (#AgentProfile).
/// Controls LLM prompt framing and stylistic preferences.
/// </summary>
public record AgentProfile
{
    public string Name { get; init; } = "Agent";
    public string Backstory { get; init; } = string.Empty;
    public string VoiceStyle { get; init; } = "helpful and concise";
    public string[] Preferences { get; init; } = [];
    public string[] DisallowedActions { get; init; } = [];
}
