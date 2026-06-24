namespace Agent.Core;

/// <summary>
/// Encapsulates all context needed for a replan operation (TSK-0104).
///
/// Consolidates the parameter explosion on <see cref="IPlanner.ReplanAsync"/>
/// into a single record, making it easier to pass around and extend without
/// changing the interface signature.
/// </summary>
/// <param name="CurrentPlan">The current plan being replanned.</param>
/// <param name="State">Current world state.</param>
/// <param name="FailureReason">Human-readable reason the plan failed.</param>
/// <param name="OriginalGoal">Optional original goal object. When provided, preserves
/// the concrete goal type for correct decomposer routing (Sprint 28 P1-A).</param>
public sealed record ReplanGoalContext(
    IPlan CurrentPlan,
    WorldState State,
    string FailureReason,
    IGoal? OriginalGoal = null);
