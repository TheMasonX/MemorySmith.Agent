namespace Agent.Core;

/// <summary>
/// Semantic intent produced by the LLM interpreter. Contains NO goal construction
/// logic — parsers produce intent, the planner layer creates goals.
///
/// Enforces the "parsers never create goals" principle (AGENTS.md CRITICAL rule):
///   Old path: Chat → Interpreter → ChatInterpretation(GoalName="GatherItem:oak_log")
///   New path: Chat → IntentDraft → AgentBackgroundService.IntentDraftToGoal → GoalFactory
///
/// Sprint 36 evolution: this record will be wrapped in IntentAssessment which adds
/// RiskLevel and RequiresConfirmation as separate orthogonal dimensions:
///   IntentAssessment{ IntentDraft, RiskLevel, RequiresConfirmation, ReasoningSummary }
/// Design note: confidence (how sure we are what the user meant) ≠ risk (impact of doing it).
/// "Build a house" can be high-confidence + high-risk; "Come here" can be lower-confidence + low-risk.
///
/// Sprint 35 transition: AgentBackgroundService.IntentDraftToGoal performs the mapping
///   Intent="gather", Item="oak_log" → GoalFactory.CreateAsync("GatherItem:oak_log", ...)
///   Intent="build", Blueprint="small-house" → GoalFactory.CreateAsync("Build:small-house", ...)
/// Sprint 36: this transition logic moves to IntentManager.
///
/// Sprint 39 P1-C: moved from Agent.Planning to Agent.Core so that IAgentRuntimeComponent
/// (in Agent.Core.Runtime) can reference it without a circular project dependency.
/// Agent.Planning still uses IntentDraft via the Agent.Core project reference.
/// </summary>
/// <param name="Addressed">Whether the bot was addressed: "yes" | "maybe" | "no"</param>
/// <param name="Intent">Semantic intent: "gather" | "build" | "craft" | "navigate" | "cancel"
///   | "status" | "help" | "conversation" | "clarify" | "ignore"</param>
/// <param name="Item">Minecraft item ID without namespace prefix (e.g. "oak_log", "diamond")</param>
/// <param name="Blueprint">Blueprint ID (e.g. "small-house")</param>
/// <param name="Count">Quantity requested, or null</param>
/// <param name="X">Target X coordinate or null</param>
/// <param name="Y">Target Y coordinate or null</param>
/// <param name="Z">Target Z coordinate or null</param>
/// <param name="Confidence">LLM confidence in interpretation: 0.0 (uncertain) – 1.0 (certain)</param>
/// <param name="ClarificationQuestion">Non-null when Confidence &lt; threshold; the bot should
///   ask this question in-game before proceeding.</param>
/// <param name="Response">In-game reply text (max ~50 words). Empty for "ignore".</param>
public record IntentDraft(
    string Addressed,
    string Intent,
    string? Item,
    string? Blueprint,
    int? Count,
    int? X,
    int? Y,
    int? Z,
    double Confidence,
    string? ClarificationQuestion,
    string Response);
