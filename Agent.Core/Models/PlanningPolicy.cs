namespace Agent.Core;

/// <summary>
/// Represents a precondition that must be satisfied before a goal can be attempted.
///
/// Goals implement this to tell the planner whether they are feasible from
/// the current world state. When a precondition fails, the planner can either
/// acquire the prerequisites first or declare the goal infeasible early with
/// a specific reason — instead of discovering failure mid-execution.
///
/// Sprint 57: Introduced as part of the structured planning policy model.
/// </summary>
public interface IGoalPrecondition
{
    /// <summary>
    /// Returns true if the goal can be attempted from the given execution context.
    /// When false, <paramref name="blockingReason"/> explains why.
    /// </summary>
    bool CanAttempt(ExecutionContext context, out string? blockingReason);
}

/// <summary>
/// Describes the expected world state after a goal completes successfully.
///
/// Used by goal-chaining logic: "if I run GatherWood, my inventory will
/// have oak_log >= 10 afterward." This enables the planner to compose
/// goals without hardcoded or ad-hoc LLM inference.
///
/// Sprint 57: Introduced as part of the structured planning policy model.
/// </summary>
public interface IGoalPostcondition
{
    /// <summary>
    /// Returns a human-readable description of what the world should look
    /// like when this goal succeeds. Used by goal-chaining and LLM context.
    /// </summary>
    string ExpectedOutcome { get; }

    /// <summary>
    /// Returns a set of inventory item deltas expected on success.
    /// Keys are item IDs, values are expected count changes (positive = gained, negative = consumed).
    /// Null means "no inventory expectation" (e.g., navigate-only goals).
    /// </summary>
    IReadOnlyDictionary<string, int>? ExpectedInventoryDelta { get; }
}

/// <summary>
/// Structured recovery policy for a goal or plan failure.
///
/// Replaces the free-text recovery logic in AgentBackgroundService
/// with a typed contract the planner can act on deterministically.
///
/// Sprint 57: Introduced as part of the structured planning policy model.
/// </summary>
public interface IRemediationPolicy
{
    /// <summary>Human-readable description of the remediation strategy.</summary>
    string Description { get; }

    /// <summary>
    /// Priority order of remediation steps. The recovery manager tries
    /// each step in order until one succeeds or all are exhausted.
    /// </summary>
    IReadOnlyList<RemediationStep> Steps { get; }
}

/// <summary>
/// A single remediation step within an <see cref="IRemediationPolicy"/>.
/// </summary>
/// <param name="Action">The action to take (e.g., "retry", "wander", "getStatus", "abandon").</param>
/// <param name="MaxAttempts">Maximum consecutive attempts for this step before moving to the next.</param>
/// <param name="CooldownSeconds">Minimum seconds between attempts of this step.</param>
public sealed record RemediationStep(
    string Action,
    int MaxAttempts = 1,
    int CooldownSeconds = 0);

/// <summary>
/// Default remediation policies for common failure patterns.
/// </summary>
public static class RemediationPolicies
{
    /// <summary>Retry the current plan up to 3 times with 10s cooldown, then abandon.</summary>
    public static readonly IRemediationPolicy RetryThenAbandon = new SimpleRemediationPolicy(
        "Retry up to 3 times, then abandon",
        [
            new RemediationStep("retry", MaxAttempts: 3, CooldownSeconds: 10),
            new RemediationStep("abandon"),
        ]);

    /// <summary>Wander to find a new location, retry once, then abandon.</summary>
    public static readonly IRemediationPolicy WanderThenRetry = new SimpleRemediationPolicy(
        "Wander to find new resources, then retry",
        [
            new RemediationStep("wander", MaxAttempts: 2, CooldownSeconds: 15),
            new RemediationStep("retry", MaxAttempts: 1, CooldownSeconds: 10),
            new RemediationStep("abandon"),
        ]);

    /// <summary>Get fresh status, retry, then abandon.</summary>
    public static readonly IRemediationPolicy RefreshThenRetry = new SimpleRemediationPolicy(
        "Refresh world state, then retry",
        [
            new RemediationStep("getStatus", MaxAttempts: 2, CooldownSeconds: 5),
            new RemediationStep("retry", MaxAttempts: 2, CooldownSeconds: 10),
            new RemediationStep("abandon"),
        ]);

    private sealed class SimpleRemediationPolicy(string description, IReadOnlyList<RemediationStep> steps) : IRemediationPolicy
    {
        public string Description { get; } = description;
        public IReadOnlyList<RemediationStep> Steps { get; } = steps;
    }
}
