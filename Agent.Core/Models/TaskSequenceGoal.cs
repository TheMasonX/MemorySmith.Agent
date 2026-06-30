namespace Agent.Core;

/// <summary>
/// A compound goal that executes a sequence of sub-goals in order.
/// Created when the LLM detects a compound command like
/// "gather wood then build a house" (TSK-0205).
///
/// Each step is a standard <see cref="IGoal"/> created via the normal
/// GoalFactory pipeline. When step N completes, the agent advances to
/// step N+1 automatically.
///
/// Sprint 54 (TSK-0205): initial implementation with sequential execution.
/// </summary>
public sealed class TaskSequenceGoal : IGoal
{
    private readonly IReadOnlyList<IGoal> _steps;
    private int _currentStep;

    /// <summary>Maximum steps allowed in a sequence (guard against runaway chains).</summary>
    public const int MaxSteps = 5;

    public TaskSequenceGoal(IReadOnlyList<IGoal> steps)
    {
        if (steps.Count == 0)
            throw new ArgumentException("TaskSequenceGoal requires at least one step.", nameof(steps));
        if (steps.Count > MaxSteps)
            throw new ArgumentException(
                $"TaskSequenceGoal supports at most {MaxSteps} steps (got {steps.Count}).",
                nameof(steps));
        _steps = steps;
        _currentStep = 0;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string Name => $"Sequence:{_steps[_currentStep].Name}";

    public string Description =>
        $"Step {_currentStep + 1}/{_steps.Count}: {_steps[_currentStep].Description}";

    public string[] Phases => _steps[_currentStep].Phases;

    public string? FailureReason { get; set; }

    public int? DamageInterruptThresholdHp => _steps[_currentStep].DamageInterruptThresholdHp;

    /// <summary>Index of the current step (0-based).</summary>
    public int CurrentStepIndex => _currentStep;

    /// <summary>Total number of steps in the sequence.</summary>
    public int TotalSteps => _steps.Count;

    /// <summary>The currently active sub-goal.</summary>
    public IGoal CurrentStep => _steps[_currentStep];

    /// <summary>All remaining steps including the current one.</summary>
    public IReadOnlyList<IGoal> RemainingSteps =>
        _steps.Skip(_currentStep).ToList().AsReadOnly();

    /// <summary>
    /// Advances to the next step. Returns true if there is a next step,
    /// false if the sequence is complete.
    /// </summary>
    public bool TryAdvance()
    {
        if (_currentStep + 1 >= _steps.Count)
            return false;
        _currentStep++;
        return true;
    }

    public bool IsComplete(WorldState state)
    {
        // Sprint 56 (TSK-0274): The sequence is complete when all steps are done.
        // The original implementation only checked _currentStep >= _steps.Count,
        // but _currentStep is only incremented by TryAdvance(), which is only
        // called from TryAdvanceSequence() inside the IsComplete==true branch.
        // This created a circular dependency — sequences could never complete.
        //
        // Fix: delegate to the current step's IsComplete first. If the current
        // step is complete AND we're on the last step, the sequence is done.
        // Otherwise, the caller (TryAdvanceSequence) will advance to the next step.
        if (_currentStep >= _steps.Count)
            return true;

        // Check if the current step itself is complete.
        return _steps[_currentStep].IsComplete(state);
    }

    public bool HasFailed(WorldState state)
    {
        // The sequence fails if the current step fails.
        if (_currentStep < _steps.Count && _steps[_currentStep].HasFailed(state))
        {
            FailureReason = _steps[_currentStep].FailureReason;
            return true;
        }
        return false;
    }
}
