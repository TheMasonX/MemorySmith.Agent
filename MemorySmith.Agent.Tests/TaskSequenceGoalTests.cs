namespace MemorySmith.Agent.Tests;

using global::Agent.Core;
using NUnit.Framework;

/// <summary>
/// Sprint 56 (TSK-0274): Tests for TaskSequenceGoal state machine.
/// Verifies IsComplete delegates to current step, TryAdvance advances correctly,
/// HasFailed propagates, and sequences can actually complete.
/// </summary>

[TestFixture]
[Description("Sprint 56: TaskSequenceGoal.IsComplete, TryAdvance, and HasFailed tests.")]
public sealed class TaskSequenceGoalTests
{
    /// <summary>A simple test goal that can be marked complete/failed externally.</summary>
    private sealed class TestGoal : IGoal
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; init; } = "TestGoal";
        public string Description { get; init; } = "";
        public string[] Phases { get; init; } = [];
        public string? FailureReason { get; set; }
        public int? DamageInterruptThresholdHp => null;

        public bool IsCompleteResult { get; set; }
        public bool HasFailedResult { get; set; }

        public bool IsComplete(WorldState state) => IsCompleteResult;
        public bool HasFailed(WorldState state) => HasFailedResult;
    }

    private static WorldState EmptyState => new();

    // ── Construction ─────────────────────────────────────────────────────────

    [Test]
    public void Constructor_EmptySteps_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TaskSequenceGoal([]));
    }

    [Test]
    public void Constructor_TooManySteps_Throws()
    {
        var steps = Enumerable.Range(0, TaskSequenceGoal.MaxSteps + 1)
            .Select(_ => new TestGoal())
            .Cast<IGoal>()
            .ToList();
        Assert.Throws<ArgumentException>(() => new TaskSequenceGoal(steps));
    }

    [Test]
    public void Constructor_MaxSteps_Succeeds()
    {
        var steps = Enumerable.Range(0, TaskSequenceGoal.MaxSteps)
            .Select(_ => new TestGoal())
            .Cast<IGoal>()
            .ToList();
        Assert.DoesNotThrow(() => new TaskSequenceGoal(steps));
    }

    // ── IsComplete ───────────────────────────────────────────────────────────

    [Test]
    public void IsComplete_CurrentStepNotComplete_ReturnsFalse()
    {
        var step = new TestGoal { IsCompleteResult = false };
        var seq = new TaskSequenceGoal([step]);

        Assert.That(seq.IsComplete(EmptyState), Is.False);
    }

    [Test]
    public void IsComplete_CurrentStepComplete_ReturnsTrue()
    {
        // Sprint 56 (TSK-0274): IsComplete must delegate to the current step.
        var step = new TestGoal { IsCompleteResult = true };
        var seq = new TaskSequenceGoal([step]);

        Assert.That(seq.IsComplete(EmptyState), Is.True);
    }

    [Test]
    public void IsComplete_OneStepSequence_CurrentStepComplete_ReturnsTrue()
    {
        var step = new TestGoal { IsCompleteResult = true };
        var seq = new TaskSequenceGoal([step]);

        Assert.That(seq.IsComplete(EmptyState), Is.True);
    }

    [Test]
    public void IsComplete_MultiStepSequence_FirstStepComplete_ReturnsTrue()
    {
        var step1 = new TestGoal { IsCompleteResult = true };
        var step2 = new TestGoal { IsCompleteResult = false };
        var seq = new TaskSequenceGoal([step1, step2]);

        // First step is complete — IsComplete should report true so the dispatch
        // loop can call TryAdvanceSequence → TryAdvance to move to step 2.
        Assert.That(seq.IsComplete(EmptyState), Is.True);
    }

    [Test]
    public void IsComplete_MultiStepSequence_NeitherComplete_ReturnsFalse()
    {
        var step1 = new TestGoal { IsCompleteResult = false };
        var step2 = new TestGoal { IsCompleteResult = false };
        var seq = new TaskSequenceGoal([step1, step2]);

        Assert.That(seq.IsComplete(EmptyState), Is.False);
    }

    // ── TryAdvance ───────────────────────────────────────────────────────────

    [Test]
    public void TryAdvance_SingleStep_ReturnsFalse()
    {
        var step = new TestGoal();
        var seq = new TaskSequenceGoal([step]);

        Assert.That(seq.TryAdvance(), Is.False);
        Assert.That(seq.CurrentStepIndex, Is.EqualTo(0));
    }

    [Test]
    public void TryAdvance_TwoSteps_AdvancesToStep1()
    {
        var step1 = new TestGoal();
        var step2 = new TestGoal();
        var seq = new TaskSequenceGoal([step1, step2]);

        var advanced = seq.TryAdvance();

        Assert.Multiple(() =>
        {
            Assert.That(advanced, Is.True);
            Assert.That(seq.CurrentStepIndex, Is.EqualTo(1));
            Assert.That(seq.TotalSteps, Is.EqualTo(2));
        });
    }

    [Test]
    public void TryAdvance_AtLastStep_ReturnsFalse()
    {
        var step1 = new TestGoal();
        var step2 = new TestGoal();
        var seq = new TaskSequenceGoal([step1, step2]);

        seq.TryAdvance(); // 0 → 1

        Assert.Multiple(() =>
        {
            Assert.That(seq.TryAdvance(), Is.False); // 1 → no more
            Assert.That(seq.CurrentStepIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void TryAdvance_ThreeSteps_AdvancesFully()
    {
        var steps = Enumerable.Range(0, 3).Select(_ => new TestGoal()).Cast<IGoal>().ToList();
        var seq = new TaskSequenceGoal(steps);

        Assert.That(seq.TryAdvance(), Is.True);
        Assert.That(seq.CurrentStepIndex, Is.EqualTo(1));

        Assert.That(seq.TryAdvance(), Is.True);
        Assert.That(seq.CurrentStepIndex, Is.EqualTo(2));

        Assert.That(seq.TryAdvance(), Is.False); // done
    }

    // ── HasFailed ────────────────────────────────────────────────────────────

    [Test]
    public void HasFailed_CurrentStepNotFailed_ReturnsFalse()
    {
        var step = new TestGoal { HasFailedResult = false };
        var seq = new TaskSequenceGoal([step]);

        Assert.That(seq.HasFailed(EmptyState), Is.False);
        Assert.That(seq.FailureReason, Is.Null);
    }

    [Test]
    public void HasFailed_CurrentStepFailed_ReturnsTrue_AndPropagatesReason()
    {
        var step = new TestGoal
        {
            HasFailedResult = true,
            FailureReason = "out of resources"
        };
        var seq = new TaskSequenceGoal([step]);

        Assert.Multiple(() =>
        {
            Assert.That(seq.HasFailed(EmptyState), Is.True);
            Assert.That(seq.FailureReason, Is.EqualTo("out of resources"));
        });
    }

    [Test]
    public void HasFailed_MultiStep_OnlyChecksCurrentStep()
    {
        var step1 = new TestGoal { HasFailedResult = false };
        var step2 = new TestGoal { HasFailedResult = true, FailureReason = "step2 failed" };
        var seq = new TaskSequenceGoal([step1, step2]);

        // Only step 1 is current — should not fail
        Assert.That(seq.HasFailed(EmptyState), Is.False);

        seq.TryAdvance();

        // Now step 2 is current — should fail
        Assert.That(seq.HasFailed(EmptyState), Is.True);
        Assert.That(seq.FailureReason, Is.EqualTo("step2 failed"));
    }

    // ── Name / Description delegation ────────────────────────────────────────

    [Test]
    public void Name_DelegatesToCurrentStep()
    {
        var step1 = new TestGoal { Name = "gather_oak" };
        var step2 = new TestGoal { Name = "build_house" };
        var seq = new TaskSequenceGoal([step1, step2]);

        Assert.That(seq.Name, Is.EqualTo("Sequence:gather_oak"));

        seq.TryAdvance();

        Assert.That(seq.Name, Is.EqualTo("Sequence:build_house"));
    }

    // ── RemainingSteps ───────────────────────────────────────────────────────

    [Test]
    public void RemainingSteps_IncludesAllFromCurrent()
    {
        var step1 = new TestGoal { Name = "a" };
        var step2 = new TestGoal { Name = "b" };
        var step3 = new TestGoal { Name = "c" };
        var seq = new TaskSequenceGoal([step1, step2, step3]);

        Assert.That(seq.RemainingSteps.Count, Is.EqualTo(3));

        seq.TryAdvance();

        Assert.That(seq.RemainingSteps.Count, Is.EqualTo(2));
        Assert.That(seq.RemainingSteps[0].Name, Is.EqualTo("b"));
    }
}
