namespace MemorySmith.Agent.Tests;

using WebUI.Blazor.Managers;
using AgentExecutionContext = global::Agent.Core.ExecutionContext;

/// <summary>
/// Sprint 57: Tests for ExecutionContext, RecoveryContext, ExecutionCapabilities,
/// ActionRegistry, PlanningPolicy types, and manager integration.
/// </summary>
[TestFixture]
public class Sprint57ExecutionContextTests
{
    // ── ExecutionContext ──────────────────────────────────────────────────────

    [Test]
    public void ExecutionContext_Idle_CreatesContextWithNoGoal()
    {
        var state = new WorldState();
        var caps = ExecutionCapabilities.Survival;
        var ctx = AgentExecutionContext.Idle(state, caps);

        Assert.That(ctx.IsIdle, Is.True);
        Assert.That(ctx.Goal, Is.Null);
        Assert.That(ctx.GoalName, Is.EqualTo("(idle)"));
        Assert.That(ctx.State, Is.SameAs(state));
        Assert.That(ctx.Capabilities, Is.SameAs(caps));
        Assert.That(ctx.ConsecutiveFailures, Is.Zero);
        Assert.That(ctx.QueueDepth, Is.Zero);
    }

    [Test]
    public void ExecutionContext_ForGoal_CreatesContextWithGoal()
    {
        var goal = new SimpleGoal("test", "desc", [], _ => false);
        var state = new WorldState();
        var caps = ExecutionCapabilities.Survival;
        var ctx = AgentExecutionContext.ForGoal(goal, state, caps);

        Assert.That(ctx.IsIdle, Is.False);
        Assert.That(ctx.Goal, Is.SameAs(goal));
        Assert.That(ctx.GoalName, Is.EqualTo("test"));
        Assert.That(ctx.ConsecutiveFailures, Is.Zero);
    }

    [Test]
    public void ExecutionContext_WithGoal_ResetsFailures()
    {
        var goal = new SimpleGoal("g", "d", [], _ => false);
        var ctx = AgentExecutionContext.ForGoal(goal, new WorldState(), ExecutionCapabilities.Survival)
            .WithFailure("error1")
            .WithFailure("error2");

        Assert.That(ctx.ConsecutiveFailures, Is.EqualTo(2));

        var newGoal = new SimpleGoal("g2", "d2", [], _ => false);
        ctx = ctx.WithGoal(newGoal);

        Assert.That(ctx.ConsecutiveFailures, Is.Zero);
        Assert.That(ctx.LastFailureReason, Is.Null);
        Assert.That(ctx.RecoveryContext, Is.EqualTo(RecoveryContext.None));
    }

    [Test]
    public void ExecutionContext_WithGoal_NullClearsGoal()
    {
        var goal = new SimpleGoal("g", "d", [], _ => false);
        var ctx = AgentExecutionContext.ForGoal(goal, new WorldState(), ExecutionCapabilities.Survival);

        ctx = ctx.WithGoal(null);

        Assert.That(ctx.IsIdle, Is.True);
        Assert.That(ctx.Goal, Is.Null);
    }

    [Test]
    public void ExecutionContext_WithFailure_IncrementsAndRecords()
    {
        var ctx = AgentExecutionContext.Idle(new WorldState(), ExecutionCapabilities.Survival);
        ctx = ctx.WithFailure("test failure");

        Assert.That(ctx.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(ctx.LastFailureReason, Is.EqualTo("test failure"));

        ctx = ctx.WithFailure("another");
        Assert.That(ctx.ConsecutiveFailures, Is.EqualTo(2));
        Assert.That(ctx.LastFailureReason, Is.EqualTo("another"));
    }

    [Test]
    public void ExecutionContext_WithState_UpdatesSnapshot()
    {
        var s1 = new WorldState();
        var s2 = new WorldState { GameMode = "creative" };
        var ctx = AgentExecutionContext.Idle(s1, ExecutionCapabilities.Survival);

        ctx = ctx.WithState(s2);

        Assert.That(ctx.State, Is.SameAs(s2));
        Assert.That(ctx.State.GameMode, Is.EqualTo("creative"));
    }

    [Test]
    public void ExecutionContext_WithRecovery_UpdatesContext()
    {
        var ctx = AgentExecutionContext.Idle(new WorldState(), ExecutionCapabilities.Survival);
        var rc = new RecoveryContext("error", 2, DateTimeOffset.UtcNow, "testGoal");

        ctx = ctx.WithRecovery(rc);

        Assert.That(ctx.RecoveryContext, Is.SameAs(rc));
    }

    [Test]
    public void ExecutionContext_HasFreshInventory_ReflectsStaleFlag()
    {
        var stale = new WorldState().With(b => b.SetInventoryStale(true));
        var fresh = new WorldState().With(b => b.SetInventoryStale(false));

        var ctxStale = AgentExecutionContext.Idle(stale, ExecutionCapabilities.Survival);
        var ctxFresh = AgentExecutionContext.Idle(fresh, ExecutionCapabilities.Survival);

        Assert.That(ctxStale.HasFreshInventory, Is.False);
        Assert.That(ctxFresh.HasFreshInventory, Is.True);
    }

    // ── RecoveryContext ────────────────────────────────────────────────────────

    [Test]
    public void RecoveryContext_None_IsEmpty()
    {
        var rc = RecoveryContext.None;

        Assert.That(rc.LastError, Is.Null);
        Assert.That(rc.AttemptCount, Is.Zero);
        Assert.That(rc.IsExhausted, Is.False);
    }

    [Test]
    public void RecoveryContext_RecordAttempt_Increments()
    {
        var now = DateTimeOffset.UtcNow;
        var rc = RecoveryContext.None
            .RecordAttempt("error1", now)
            .RecordAttempt("error2", now);

        Assert.That(rc.AttemptCount, Is.EqualTo(2));
        Assert.That(rc.LastError, Is.EqualTo("error2"));
        Assert.That(rc.LastAttemptAt, Is.EqualTo(now));
    }

    [Test]
    public void RecoveryContext_IsExhausted_WhenMaxAttemptsReached()
    {
        var now = DateTimeOffset.UtcNow;
        var rc = RecoveryContext.None;
        for (int i = 0; i < RecoveryContext.MaxAttempts; i++)
            rc = rc.RecordAttempt($"error{i}", now);

        Assert.That(rc.IsExhausted, Is.True);
        Assert.That(rc.AttemptCount, Is.EqualTo(RecoveryContext.MaxAttempts));

        // One more should still be exhausted.
        rc = rc.RecordAttempt("extra", now);
        Assert.That(rc.IsExhausted, Is.True);
    }

    [Test]
    public void RecoveryContext_WithGoal_SetsGoalName()
    {
        var rc = RecoveryContext.None.WithGoal("testGoal");

        Assert.That(rc.GoalName, Is.EqualTo("testGoal"));
    }

    // ── ExecutionCapabilities ─────────────────────────────────────────────────

    [Test]
    public void ExecutionCapabilities_Survival_HasExpectedValues()
    {
        var caps = ExecutionCapabilities.Survival;

        Assert.That(caps.GameMode, Is.EqualTo("survival"));
        Assert.That(caps.CanSpawnItems, Is.False);
        Assert.That(caps.CanFly, Is.False);
        Assert.That(caps.IsInvulnerable, Is.False);
    }

    [Test]
    public void ExecutionCapabilities_Creative_HasExpectedValues()
    {
        var caps = ExecutionCapabilities.Creative;

        Assert.That(caps.GameMode, Is.EqualTo("creative"));
        Assert.That(caps.CanSpawnItems, Is.True);
        Assert.That(caps.CanFly, Is.True);
        Assert.That(caps.IsInvulnerable, Is.True);
    }

    [Test]
    public void ExecutionCapabilities_Unknown_HasNoCapabilities()
    {
        var caps = ExecutionCapabilities.Unknown;

        Assert.That(caps.GameMode, Is.Null);
        Assert.That(caps.CanSpawnItems, Is.False);
        Assert.That(caps.CanFly, Is.False);
    }

    [Test]
    public void ExecutionCapabilities_FromWorldState_SurvivalMode()
    {
        var state = new WorldState { GameMode = "survival" };

        var caps = ExecutionCapabilities.FromWorldState(state);

        Assert.That(caps.GameMode, Is.EqualTo("survival"));
        Assert.That(caps.CanSpawnItems, Is.False);
        Assert.That(caps.CanFly, Is.False);
    }

    [Test]
    public void ExecutionCapabilities_FromWorldState_CreativeMode()
    {
        var state = new WorldState { GameMode = "creative" };

        var caps = ExecutionCapabilities.FromWorldState(state);

        Assert.That(caps.GameMode, Is.EqualTo("creative"));
        Assert.That(caps.CanSpawnItems, Is.True);
        Assert.That(caps.CanFly, Is.True);
    }

    [Test]
    public void ExecutionCapabilities_FromWorldState_CreativeViaFact()
    {
        var state = new WorldState().With(b =>
            b.SetFact("world:gamemode", "creative", FactSource.Observed));

        var caps = ExecutionCapabilities.FromWorldState(state);

        Assert.That(caps.CanSpawnItems, Is.True);
    }

    // ── ActionRegistry ────────────────────────────────────────────────────────

    [Test]
    public void ActionRegistry_RegisterAndLookup()
    {
        var reg = new ActionRegistry();
        reg.Register(new ActionDescriptor("MineBlock", "Mine a block"));

        Assert.That(reg.CanExecute("MineBlock"), Is.True);
        Assert.That(reg.CanExecute("mineblock"), Is.True); // case-insensitive
        Assert.That(reg.CanExecute("NonExistent"), Is.False);

        var d = reg.Get("MineBlock");
        Assert.That(d, Is.Not.Null);
        Assert.That(d!.Name, Is.EqualTo("MineBlock"));
        Assert.That(d.Description, Is.EqualTo("Mine a block"));
    }

    [Test]
    public void ActionRegistry_RegisterByNameOnly()
    {
        var reg = new ActionRegistry();
        reg.Register("PlaceBlock");

        Assert.That(reg.CanExecute("PlaceBlock"), Is.True);

        var d = reg.Get("PlaceBlock");
        Assert.That(d, Is.Not.Null);
        Assert.That(d!.Name, Is.EqualTo("PlaceBlock"));
        Assert.That(d.Description, Is.Null);
    }

    [Test]
    public void ActionRegistry_GetMissingReturnsNull()
    {
        var reg = new ActionRegistry();

        Assert.That(reg.Get("FakeTool"), Is.Null);
    }

    // ── RemediationPolicies ───────────────────────────────────────────────────

    [Test]
    public void RemediationPolicies_RetryThenAbandon_HasTwoSteps()
    {
        var policy = RemediationPolicies.RetryThenAbandon;

        Assert.That(policy.Steps, Has.Count.EqualTo(2));
        Assert.That(policy.Steps[0].Action, Is.EqualTo("retry"));
        Assert.That(policy.Steps[0].MaxAttempts, Is.EqualTo(3));
        Assert.That(policy.Steps[1].Action, Is.EqualTo("abandon"));
    }

    [Test]
    public void RemediationPolicies_WanderThenRetry_HasThreeSteps()
    {
        var policy = RemediationPolicies.WanderThenRetry;

        Assert.That(policy.Steps, Has.Count.EqualTo(3));
        Assert.That(policy.Steps[0].Action, Is.EqualTo("wander"));
        Assert.That(policy.Steps[1].Action, Is.EqualTo("retry"));
        Assert.That(policy.Steps[2].Action, Is.EqualTo("abandon"));
    }

    [Test]
    public void RemediationPolicies_RefreshThenRetry_HasStatusStep()
    {
        var policy = RemediationPolicies.RefreshThenRetry;

        Assert.That(policy.Steps[0].Action, Is.EqualTo("getStatus"));
        Assert.That(policy.Steps[0].CooldownSeconds, Is.EqualTo(5));
        Assert.That(policy.Steps[1].Action, Is.EqualTo("retry"));
        Assert.That(policy.Steps[2].Action, Is.EqualTo("abandon"));
    }

    // ── IGoalPrecondition integration ─────────────────────────────────────────

    [Test]
    public void GoalPrecondition_CanAttemptTrue_AllowsPlanning()
    {
        var goal = new PreconditionGoal(allowed: true, reason: null);
        var ctx = AgentExecutionContext.ForGoal(goal, new WorldState(), ExecutionCapabilities.Survival);

        var result = ((IGoalPrecondition)goal).CanAttempt(ctx, out var reason);

        Assert.That(result, Is.True);
        Assert.That(reason, Is.Null);
    }

    [Test]
    public void GoalPrecondition_CanAttemptFalse_BlocksWithReason()
    {
        var goal = new PreconditionGoal(allowed: false, reason: "no wood nearby");
        var ctx = AgentExecutionContext.ForGoal(goal, new WorldState(), ExecutionCapabilities.Survival);

        var result = ((IGoalPrecondition)goal).CanAttempt(ctx, out var reason);

        Assert.That(result, Is.False);
        Assert.That(reason, Is.EqualTo("no wood nearby"));
    }

    // ── StateManagerImpl.BuildContext ─────────────────────────────────────────

    [Test]
    public void StateManagerImpl_BuildContext_CreatesCorrectContext()
    {
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<StateManagerImpl>();
        var mgr = new StateManagerImpl(logger);
        mgr.Reset(new WorldState { GameMode = "survival" });

        var goal = new SimpleGoal("test", "desc", [], _ => false);
        var rc = new RecoveryContext("err", 1, DateTimeOffset.UtcNow, "prevGoal");

        var ctx = mgr.BuildContext(goal, queueDepth: 5, consecutiveFailures: 2,
            lastFailureReason: "blocked", recoveryContext: rc);

        Assert.That(ctx.Goal, Is.SameAs(goal));
        Assert.That(ctx.State.GameMode, Is.EqualTo("survival"));
        Assert.That(ctx.QueueDepth, Is.EqualTo(5));
        Assert.That(ctx.ConsecutiveFailures, Is.EqualTo(2));
        Assert.That(ctx.LastFailureReason, Is.EqualTo("blocked"));
        Assert.That(ctx.RecoveryContext, Is.SameAs(rc));
        Assert.That(ctx.Capabilities.GameMode, Is.EqualTo("survival"));
    }

    [Test]
    public void StateManagerImpl_BuildContext_NullGoalWorks()
    {
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<StateManagerImpl>();
        var mgr = new StateManagerImpl(logger);

        var ctx = mgr.BuildContext(null, 0, 0, null);

        Assert.That(ctx.IsIdle, Is.True);
        Assert.That(ctx.Goal, Is.Null);
    }

    // ── PlanningManagerImpl precondition gating ───────────────────────────────

    [Test]
    public async Task PlanningManagerImpl_PreconditionFailed_ReturnsEmptyPlan()
    {
        var mockPlanner = new MockPlanner();
        var mgr = new PlanningManagerImpl(mockPlanner, null,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<PlanningManagerImpl>());

        var goal = new PreconditionGoal(allowed: false, reason: "no resources");
        var ctx = AgentExecutionContext.ForGoal(goal, new WorldState(), ExecutionCapabilities.Survival);

        var plan = await mgr.PlanAsync(ctx);

        Assert.That(plan.Actions, Is.Empty);
        Assert.That(plan.IsEmpty, Is.True);
        // The planner should NOT have been called.
        Assert.That(mockPlanner.PlanCallCount, Is.Zero);
    }

    [Test]
    public async Task PlanningManagerImpl_PreconditionPassed_DelegatesToPlanner()
    {
        var mockPlanner = new MockPlanner();
        var mgr = new PlanningManagerImpl(mockPlanner, null,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<PlanningManagerImpl>());

        var goal = new PreconditionGoal(allowed: true, reason: null);
        var ctx = AgentExecutionContext.ForGoal(goal, new WorldState(), ExecutionCapabilities.Survival);

        var plan = await mgr.PlanAsync(ctx);

        Assert.That(mockPlanner.PlanCallCount, Is.EqualTo(1));
        Assert.That(plan.GoalName, Is.EqualTo("precond"));
    }

    // ── Helper types ──────────────────────────────────────────────────────────

    /// <summary>Custom goal implementing IGoal and IGoalPrecondition for testing.</summary>
    private sealed class PreconditionGoal : IGoal, IGoalPrecondition
    {
        private readonly bool _allowed;
        private readonly string? _reason;

        public PreconditionGoal(bool allowed, string? reason)
        {
            _allowed = allowed;
            _reason = reason;
        }

        public string Name => "precond";
        public string Description => "test precondition goal";
        public string[] Phases => [];
        public Guid Id => Guid.NewGuid();
        public string? FailureReason { get; set; }

        public bool IsComplete(WorldState state) => false;
        public bool HasFailed(WorldState state) => false;

        public bool CanAttempt(AgentExecutionContext context, out string? blockingReason)
        {
            blockingReason = _reason;
            return _allowed;
        }
    }

    /// <summary>Mock planner that tracks call count.</summary>
    private sealed class MockPlanner : global::Agent.Planning.IPlanner
    {
        public int PlanCallCount { get; private set; }

        public Task<IPlan> PlanAsync(IGoal goal, WorldState state, CancellationToken ct = default)
        {
            PlanCallCount++;
            return Task.FromResult<IPlan>(new ActionPlan(goal.Name, goal.Phases, [
                new ActionData { Tool = "Chat", Arguments = { ["message"] = "ok" } }
            ]));
        }

        public Task<ReplanResult> ReplanAsync(ReplanGoalContext context, CancellationToken ct = default) =>
            Task.FromResult(ReplanResult.Failure("mock"));
    }
}
