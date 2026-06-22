namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Sprint 37 P1-C: Wraps an <see cref="IntentDraft"/> with risk assessment dimensions.
///
/// Confidence (how certain we are about what the user meant) and risk (how dangerous
/// the action is) are orthogonal:
///   "Build a house"  → high-confidence + high-risk
///   "Come here"      → lower-confidence + low-risk
///
/// Sprint 36 architecture note (locked):
///   IntentDraft captures semantic intent; IntentAssessment adds the safety layer.
///   Both live between the parser (LlmChatInterpreter) and the planner (GoalFactory).
///
/// Sprint 38: IntentAssessment will drive the confirmation gate:
///   IntentAssessment.RequiresConfirmation == true → bot asks in-game before acting.
/// </summary>
public sealed record IntentAssessment(
    /// <summary>The semantic intent parsed from the LLM response.</summary>
    IntentDraft Draft,

    /// <summary>
    /// How potentially dangerous or irreversible this action is.
    /// Determined by IntentManager heuristics (action type, item, count, etc.).
    /// </summary>
    RiskLevel RiskLevel,

    /// <summary>
    /// Whether the agent should ask the player for explicit confirmation before
    /// executing the goal. True for high-risk actions or when confidence is low.
    /// </summary>
    bool RequiresConfirmation,

    /// <summary>
    /// Brief natural-language summary of why this risk/confirmation assessment was
    /// reached. Used for logging and future LLM-readable explanations.
    /// </summary>
    string ReasoningSummary);

/// <summary>Sprint 37 P1-C: Risk dimension for <see cref="IntentAssessment"/>.</summary>
public enum RiskLevel
{
    /// <summary>Safe and easily reversible: status query, short movement, status check.</summary>
    Low,

    /// <summary>Moderate impact; reversible with some effort: gathering small quantities.</summary>
    Medium,

    /// <summary>
    /// High impact; difficult or impossible to undo: large builds, mass mining,
    /// craft chains with expensive ingredients.
    /// </summary>
    High,
}
