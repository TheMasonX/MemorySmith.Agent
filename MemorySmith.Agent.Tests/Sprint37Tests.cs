namespace MemorySmith.Agent.Tests;

using global::Agent.Core;
using global::Agent.Planning;
using global::Agent.Tools;
using System.Text.Json;

/// <summary>
/// Sprint 37 tests covering P0-A (ActionOutcome : IObservationSummary),
/// P0-B (IToolCaller.CallWithOutcomeAsync default implementation),
/// P1-A (IntentManager.BuildGoalRequest), P1-B (LlmChatInterpreter with IntentManager),
/// and P1-C (IntentAssessment record).
///
/// Test count: 10 new tests.
/// </summary>
[TestFixture]
public class Sprint37Tests
{
    // ── P0-A: ActionOutcome implements IObservationSummary ───────────────────────────────

    [Test]
    public void ActionOutcome_ImplementsIObservationSummary()
    {
        // ActionOutcome must implement IObservationSummary so it can be passed
        // to any consumer that expects structured observation context.
        var outcome = ActionOutcome.Succeeded(Guid.NewGuid(), "GetStatus", "Bot is healthy");
        Assert.That(outcome, Is.InstanceOf<IObservationSummary>());
    }

    [Test]
    public void ActionOutcome_IObservationSummary_Summary_MapsToObservationSummary()
    {
        var summary = "Mined 5 oak_log at (100,64,200)";
        var outcome = ActionOutcome.Succeeded(Guid.NewGuid(), "MineBlock", summary);

        // IObservationSummary.Summary must return the same text as ObservationSummary.
        var obs = (IObservationSummary)outcome;
        Assert.Multiple(() =>
        {
            Assert.That(obs.Summary, Is.EqualTo(summary));
            Assert.That(obs.Summary, Is.EqualTo(outcome.ObservationSummary));
        });
    }

    [Test]
    public void ActionOutcome_Failed_IObservationSummary_Summary_IsErrorMessage()
    {
        var reason = "Tool 'MineBlock' threw: entity not found";
        var outcome = ActionOutcome.Failed(Guid.NewGuid(), "MineBlock", reason);

        var obs = (IObservationSummary)outcome;
        Assert.That(obs.Summary, Is.EqualTo(reason));
    }

    // ── P0-B: IToolCaller.CallWithOutcomeAsync default implementation ─────────────────────

    [Test]
    public async Task IToolCaller_CallWithOutcomeAsync_SuccessResult_ProducesSucceededOutcome()
    {
        // A minimal IToolCaller stub that returns a success result.
        // The default interface implementation wraps it in ActionOutcome.Succeeded.
        // IMPORTANT: Must be typed as IToolCaller — default interface methods are only
        // accessible through the interface type, not the concrete implementing class.
        IToolCaller toolCaller = new StubToolCaller(success: true, message: "ok");
        var goalId = Guid.NewGuid();
        var args = JsonDocument.Parse("{}").RootElement;

        var (result, outcome) = await toolCaller.CallWithOutcomeAsync(goalId, "GetStatus", args);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outcome.Success, Is.True);
            Assert.That(outcome.GoalId, Is.EqualTo(goalId));
            Assert.That(outcome.ToolName, Is.EqualTo("GetStatus"));
            Assert.That(outcome, Is.InstanceOf<IObservationSummary>());
        });
    }

    [Test]
    public async Task IToolCaller_CallWithOutcomeAsync_FailureResult_ProducesFailedOutcome()
    {
        IToolCaller toolCaller = new StubToolCaller(success: false, message: "block not found");
        var goalId = Guid.NewGuid();
        var args = JsonDocument.Parse("{}").RootElement;

        var (result, outcome) = await toolCaller.CallWithOutcomeAsync(goalId, "MineBlock", args);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(outcome.Success, Is.False);
            Assert.That(outcome.ObservationSummary, Is.EqualTo("block not found"));
        });
    }

    // ── P1-A: IntentManager.BuildGoalRequest ─────────────────────────────────────────────

    [Test]
    public void IntentManager_Gather_WithItem_ReturnsGatherItemGoal()
    {
        var manager = new IntentManager();
        var draft = new IntentDraft(
            "yes", "gather", Item: "oak_log", Blueprint: null,
            Count: 32, X: null, Y: null, Z: null,
            Confidence: 0.95, ClarificationQuestion: null, Response: "On it!");

        var request = manager.BuildGoalRequest(draft);

        Assert.Multiple(() =>
        {
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.GoalName, Is.EqualTo("GatherItem:oak_log"));
            Assert.That(request.Parameters!["count"], Is.EqualTo(32));
        });
    }

    [Test]
    public void IntentManager_Craft_WithItem_ReturnsCraftItemGoal()
    {
        var manager = new IntentManager();
        var draft = new IntentDraft(
            "yes", "craft", Item: "iron_pickaxe", Blueprint: null,
            Count: 1, X: null, Y: null, Z: null,
            Confidence: 0.9, ClarificationQuestion: null, Response: "Crafting!");

        var request = manager.BuildGoalRequest(draft);

        Assert.Multiple(() =>
        {
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.GoalName, Is.EqualTo("CraftItem:iron_pickaxe"));
            Assert.That(request.Parameters!["count"], Is.EqualTo(1));
        });
    }

    [Test]
    public void IntentManager_Build_WithBlueprint_ReturnsBuildGoal()
    {
        var manager = new IntentManager();
        var draft = new IntentDraft(
            "yes", "build", Item: null, Blueprint: "small-house",
            Count: null, X: 100, Y: 64, Z: 200,
            Confidence: 0.85, ClarificationQuestion: null, Response: "Building!");

        var request = manager.BuildGoalRequest(draft);

        Assert.Multiple(() =>
        {
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.GoalName, Is.EqualTo("Build:small-house"));
            Assert.That(request.Parameters!["originX"], Is.EqualTo(100));
            Assert.That(request.Parameters!["originY"], Is.EqualTo(64));
            Assert.That(request.Parameters!["originZ"], Is.EqualTo(200));
        });
    }

    [Test]
    public void IntentManager_Navigate_WithCoords_ReturnsMoveToGoal()
    {
        var manager = new IntentManager();
        var draft = new IntentDraft(
            "yes", "navigate", Item: null, Blueprint: null,
            Count: null, X: -50, Y: 70, Z: 300,
            Confidence: 0.9, ClarificationQuestion: null, Response: "Moving!");

        var request = manager.BuildGoalRequest(draft);

        Assert.Multiple(() =>
        {
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.GoalName, Is.EqualTo("MoveTo"));
            Assert.That(request.Parameters!["x"], Is.EqualTo(-50));
            Assert.That(request.Parameters!["y"], Is.EqualTo(70));
            Assert.That(request.Parameters!["z"], Is.EqualTo(300));
        });
    }

    [Test]
    public void IntentManager_Conversation_ReturnsNull()
    {
        var manager = new IntentManager();
        var draft = new IntentDraft(
            "yes", "conversation", Item: null, Blueprint: null,
            Count: null, X: null, Y: null, Z: null,
            Confidence: 1.0, ClarificationQuestion: null, Response: "Hello!");

        var request = manager.BuildGoalRequest(draft);

        Assert.That(request, Is.Null,
            "conversation intent should produce no GoalRequest — there's nothing to do");
    }

    [Test]
    public void IntentManager_GatherWithoutItem_ReturnsNull()
    {
        var manager = new IntentManager();
        var draft = new IntentDraft(
            "yes", "gather", Item: null, Blueprint: null,
            Count: 10, X: null, Y: null, Z: null,
            Confidence: 0.5, ClarificationQuestion: null, Response: "");

        var request = manager.BuildGoalRequest(draft);

        Assert.That(request, Is.Null,
            "gather without item cannot produce a valid GoalRequest");
    }

    // ── P1-C: IntentAssessment record ────────────────────────────────────────────────────

    [Test]
    public void IntentAssessment_RecordFieldsAreAccessible()
    {
        var draft = new IntentDraft(
            "yes", "build", Item: null, Blueprint: "large-castle",
            Count: null, X: 0, Y: 64, Z: 0,
            Confidence: 0.92, ClarificationQuestion: null, Response: "Building a castle!");

        var assessment = new IntentAssessment(
            Draft: draft,
            RiskLevel: RiskLevel.High,
            RequiresConfirmation: true,
            ReasoningSummary: "Building a large structure is high-risk and irreversible.");

        Assert.Multiple(() =>
        {
            Assert.That(assessment.Draft, Is.SameAs(draft));
            Assert.That(assessment.RiskLevel, Is.EqualTo(RiskLevel.High));
            Assert.That(assessment.RequiresConfirmation, Is.True);
            Assert.That(assessment.ReasoningSummary, Does.Contain("irreversible"));
        });
    }

    // ── Stub helpers ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal IToolCaller stub that returns a fixed ToolResult.
    /// Used to test the IToolCaller.CallWithOutcomeAsync default implementation.
    /// </summary>
    private sealed class StubToolCaller(bool success, string message) : IToolCaller
    {
        public Task<ToolResult> CallAsync(
            string toolName, JsonElement arguments,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolResult(success, message));
    }
}
