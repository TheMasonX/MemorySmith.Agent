namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Decomposes a <see cref="TaskSequenceGoal"/> by delegating to the
/// registered decomposer for the current step's goal type.
///
/// Sprint 55: initial implementation — fixes the crash where
/// <see cref="HtnPlanner"/> received a wrapped <see cref="TaskSequenceGoal"/>
/// and could not decompose it because no decomposer was registered for
/// the sequence wrapper itself.
/// </summary>
public sealed class TaskSequenceGoalDecomposer(DecomposerRegistry registry) : IGoalDecomposer
{
    public bool CanHandle(IGoal goal) => goal is TaskSequenceGoal;

    public ActionPlan Decompose(IGoal goal, WorldState state)
    {
        var seq = (TaskSequenceGoal)goal;
        var step = seq.CurrentStep;

        if (registry.Find(step) is { } stepDecomposer)
            return stepDecomposer.Decompose(step, state);

        throw new InvalidOperationException(
            $"TaskSequenceGoal decomposer could not find a decomposer for " +
            $"step '{step.Name}' (type: {step.GetType().Name}). " +
            $"Register a decomposer for this goal type in DecomposerRegistry.");
    }
}
