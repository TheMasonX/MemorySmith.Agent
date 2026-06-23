using Agent.Core;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Sprint 19: Tests for the 2-state replan governor (ACTIVE → STALLED).
/// Uses short recovery timeout (100ms) for testable auto-recovery.
/// </summary>
[TestFixture]
public sealed class ReplanGovernorTests
{
    private const string PlanA = "Gather:stone:SearchMemory,MineBlock,GetStatus";
    private const string PlanB = "Gather:stone:SearchMemory,Wander,MineBlock,GetStatus";

    // ── ACTIVE → STALLED after threshold identical plans ──────────────────────

    [Test]
    public void ThreeIdenticalPlans_TransitionsToStalled()
    {
        var gov = new ReplanGovernor(identicalPlanThreshold: 3);

        Assert.That(gov.Evaluate(PlanA), Is.EqualTo(ReplanVerdict.Proceed));
        Assert.That(gov.Evaluate(PlanA), Is.EqualTo(ReplanVerdict.Proceed));
        Assert.That(gov.Evaluate(PlanA), Is.EqualTo(ReplanVerdict.Stalled));
        Assert.That(gov.IsStalled, Is.True);
    }

    [Test]
    public void DifferentPlan_ResetsCounter()
    {
        var gov = new ReplanGovernor(identicalPlanThreshold: 3);

        Assert.That(gov.Evaluate(PlanA), Is.EqualTo(ReplanVerdict.Proceed));
        Assert.That(gov.Evaluate(PlanA), Is.EqualTo(ReplanVerdict.Proceed));
        // Different plan resets the counter
        Assert.That(gov.Evaluate(PlanB), Is.EqualTo(ReplanVerdict.Proceed));
        Assert.That(gov.Evaluate(PlanB), Is.EqualTo(ReplanVerdict.Proceed));
        // Still not stalled — only 2 identical plans (PlanB)
        Assert.That(gov.IsStalled, Is.False);
    }

    [Test]
    public void SinglePlan_DoesNotStall()
    {
        var gov = new ReplanGovernor(identicalPlanThreshold: 3);

        Assert.That(gov.Evaluate(PlanA), Is.EqualTo(ReplanVerdict.Proceed));
        Assert.That(gov.IsStalled, Is.False);
    }

    // ── RecordProgress clears STALLED ─────────────────────────────────────────

    [Test]
    public void RecordProgress_ClearsStalledState()
    {
        var gov = new ReplanGovernor(identicalPlanThreshold: 2);

        // Force stall
        gov.Evaluate(PlanA);
        gov.Evaluate(PlanA);
        Assert.That(gov.IsStalled, Is.True);

        // Progress clears it
        gov.RecordProgress();
        Assert.That(gov.IsStalled, Is.False);
        Assert.That(gov.Evaluate(PlanA), Is.EqualTo(ReplanVerdict.Proceed));
    }

    // ── Reset clears everything ───────────────────────────────────────────────

    [Test]
    public void Reset_ClearsAllState()
    {
        var gov = new ReplanGovernor(identicalPlanThreshold: 2);

        // Force stall
        gov.Evaluate(PlanA);
        gov.Evaluate(PlanA);
        Assert.That(gov.IsStalled, Is.True);

        // Reset
        gov.Reset();
        Assert.That(gov.IsStalled, Is.False);
        // Can start fresh
        Assert.That(gov.Evaluate(PlanA), Is.EqualTo(ReplanVerdict.Proceed));
        Assert.That(gov.Evaluate(PlanA), Is.EqualTo(ReplanVerdict.Stalled));
    }

    // ── Auto-recovery after timeout ──────────────────────────────────────────

    [Test]
    public async Task StalledAutoRecovery_AfterTimeout()
    {
        // Use a very short timeout for testability
        // Use 0s delay so recovery is immediate for test speed.
        // Evaluate auto-recovers on the next call after stall because
        // elapsed >= 0 is always true.
        var gov = new ReplanGovernor(
            identicalPlanThreshold: 2,
            stallGraduatedDelaysSec: [0]);

        gov.Evaluate(PlanA);
        gov.Evaluate(PlanA);
        Assert.That(gov.IsStalled, Is.True,
            "Should stall after 2 identical plans");

        // With 0s delay, Evaluate auto-recovers immediately.
        // The stall was registered (verified above), now it recovers.
        Assert.That(gov.Evaluate(PlanA), Is.EqualTo(ReplanVerdict.Proceed),
            "Should auto-recover with 0s delay");
        Assert.That(gov.IsStalled, Is.False,
            "Should no longer be stalled");
    }

    // ── While STALLED, Evaluate returns Stalled ──────────────────────────────

    [Test]
    public void WhileStalled_EvaluateReturnsStalled()
    {
        var gov = new ReplanGovernor(identicalPlanThreshold: 2);

        gov.Evaluate(PlanA);
        gov.Evaluate(PlanA);

        // Multiple evaluations while stalled all return Stalled
        Assert.That(gov.Evaluate(PlanA), Is.EqualTo(ReplanVerdict.Stalled));
        Assert.That(gov.Evaluate(PlanB), Is.EqualTo(ReplanVerdict.Stalled));
        Assert.That(gov.Evaluate("anything"), Is.EqualTo(ReplanVerdict.Stalled));
    }
}
