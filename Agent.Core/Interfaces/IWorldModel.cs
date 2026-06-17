namespace Agent.Core;

/// <summary>
/// The agent's internal world model — separates observation from belief,
/// supports prediction, and reconciles predictions against observed reality.
/// This is the cognitive core that distinguishes "I observed," "I believe," and "I predict."
/// </summary>
public interface IWorldModel
{
    /// <summary>Current observed state (last known ground truth).</summary>
    ObservationState Observed { get; }

    /// <summary>Current belief state (may extend beyond direct observation).</summary>
    BeliefState Belief { get; }

    /// <summary>
    /// Update the model with a new observation from a world event.
    /// The implementation decides which beliefs to update, retain, or invalidate.
    /// </summary>
    void Observe(ObservationState observation);

    /// <summary>
    /// Predict the outcome of executing an action given current belief.
    /// Rule-based: understands move/health/food/inventory changes.
    /// Returns a prediction with confidence.
    /// </summary>
    PredictionState Predict(string toolName, IReadOnlyDictionary<string, object?> args);

    /// <summary>
    /// Compare a previous prediction against the current observation.
    /// Returns a deviation score (0.0 = perfect match, higher = more deviation).
    /// This feeds the uncertainty metric.
    /// </summary>
    double Reconcile(PredictionState prediction, ObservationState actual);

    /// <summary>
    /// Current aggregate uncertainty (0.0 – 1.0) across recent predictions.
    /// Derived from the running average of Reconcile results.
    /// </summary>
    double Uncertainty { get; }
}
