namespace Agent.Personality;

/// <summary>
/// Personality plug-in. Injects agent profile and voice into LLM prompts,
/// shaping how the agent narrates actions, responds to the user, and
/// chooses between stylistically equivalent plans.
/// </summary>
public interface IPersonality
{
    AgentProfile Profile { get; }
    string BuildSystemPrompt();
    Task<string> RespondAsync(string context, CancellationToken cancellationToken = default);
}
