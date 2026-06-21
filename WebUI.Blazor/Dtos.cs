namespace WebUI.Blazor;

/// <summary>
/// SignalR payload for real-time agent status updates pushed to the dashboard.
/// Sent via IHubContext&lt;AgentHub&gt;.Clients.Group("dashboard").SendAsync("StatusUpdated", ...).
/// </summary>
/// <param name="Status">Agent lifecycle state: "active", "idle", "reconnecting", or "disconnected".</param>
/// <param name="Goal">Current goal name, or null if the agent is idle.</param>
/// <param name="GoalDescription">Human-readable goal description, or null.</param>
/// <param name="Health">Agent health (0–20).</param>
/// <param name="Food">Agent food level (0–20).</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
/// <param name="Z">World Z coordinate.</param>
/// <param name="QueuedActions">Number of actions currently in the dispatch queue.</param>
/// <param name="ConsecutiveFailures">Running count of consecutive action failures.</param>
/// <param name="Inventory">Item→count snapshot of the agent's inventory.</param>
public record AgentStatusUpdate(
    string Status,
    string? Goal,
    string? GoalDescription,
    int Health,
    int Food,
    int X,
    int Y,
    int Z,
    int QueuedActions,
    int ConsecutiveFailures,
    IReadOnlyDictionary<string, int> Inventory);

/// <summary>
/// Typed REST API response shape for a single journal entry.
/// Uses <see cref="string"/>-typed fields throughout to ensure predictable JSON serialisation —
/// avoids the <c>object?</c> ambiguity in <see cref="Agent.Core.JournalEntry.Details"/> where
/// boxed ints, DateTimeOffset, or other value types may serialise differently across runtimes.
/// </summary>
/// <param name="Timestamp">ISO-8601 timestamp (round-trip format "O").</param>
/// <param name="Type">String name of the <see cref="Agent.Core.JournalEntryType"/> value.</param>
/// <param name="Summary">Short human-readable summary of what happened.</param>
/// <param name="Details">Key/value detail map — all values coerced to strings.</param>
public record JournalEntryDto(
    string Timestamp,
    string Type,
    string Summary,
    IReadOnlyDictionary<string, string?> Details);
