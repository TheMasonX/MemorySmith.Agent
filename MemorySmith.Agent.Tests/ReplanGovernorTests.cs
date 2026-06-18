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
        var gov = new ReplanGovernor(
            identicalPlanThreshold: 2,
            stalledRecoveryTimeout: TimeSpan.FromMilliseconds(100));

        gov.Evaluate(PlanA);
        gov.Evaluate(PlanA);
        Assert.That(gov.IsStalled, Is.True);

        // Still stalled immediately
        Assert.That(gov.Evaluate(PlanA), Is.EqualTo(ReplanVerdict.Stalled));

        // Wait for timeout
        await Task.Delay(150);

        // Should auto-recover
        Assert.That(gov.Evaluate(PlanA), Is.EqualTo(ReplanVerdict.Proceed));
        Assert.That(gov.IsStalled, Is.False);
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
