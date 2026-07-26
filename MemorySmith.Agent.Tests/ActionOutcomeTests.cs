namespace MemorySmith.Agent.Tests;

using global::Agent.Core;

[TestFixture]
public sealed class ActionOutcomeTests
{
    private static readonly Guid GoalId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Factories_PreserveOutcomeData()
    {
        var collected = ActionOutcome.Collected(GoalId, "MineBlock", "oak_log", 3, Timestamp);
        var succeeded = ActionOutcome.Succeeded(GoalId, "GetStatus", "status refreshed", Timestamp);
        var failed = ActionOutcome.Failed(GoalId, "CraftItem", "missing ingredients", Timestamp);

        Assert.Multiple(() =>
        {
            Assert.That(collected.Outcome, Is.EqualTo(OutcomeType.Completed));
            Assert.That(collected.Effects, Has.Count.EqualTo(1));
            Assert.That(collected.Timestamp, Is.EqualTo(Timestamp));
            Assert.That(succeeded.Success, Is.True);
            Assert.That(succeeded.ObservationSummary, Is.Not.Empty);
            Assert.That(failed.Outcome, Is.EqualTo(OutcomeType.Failed));
            Assert.That(failed.Success, Is.False);
            Assert.That(failed.Timestamp, Is.EqualTo(Timestamp));
        });
    }
}