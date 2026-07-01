# Sprint 57 Wave B Audit

This latest commit is a handoff/docs commit, not a code change: it adds the sprint-57 wave-C handoff page, updates the roadmap, and creates three new task files for TSK-0302/0303/0304.

My audit result is that the codebase is **moving in the right direction**, but it is **not yet at the point where the agent can reliably chain arbitrary actions, recover from unexpected world results, and replan toward a goal as a first-class behavior**. The strongest evidence is that the newer planning/execution manager layer exists, but the live agent loop is still anchored in `AgentBackgroundService`, which owns connection, event handling, dispatch, recovery, goal changes, and dashboard updates all in one place. `Program.cs` registers `PlanningManagerImpl`, `ExecutionManagerImpl`, `RecoveryManagerImpl`, `StateManagerImpl`, `DashboardPublisherImpl`, and `AgentRuntime`, but the hosted service path still runs directly through `AgentBackgroundService`.

The good news is that the modeling groundwork is already there. `IntentDraft` already has `NextSteps` for multi-step chaining, `LlmChatInterpreter` already prompts the LLM to fill `nextSteps` for compound commands, `TaskSequenceGoal` already executes a sequence of sub-goals, and `LlmEvaluatorImpl` already has a plan/execute/evaluate loop with world diffs.

The main blockers are these:

First, the replanning loop is still too coarse. `LlmEvaluatorImpl` short-circuits whenever all tool outcomes succeeded, even if the observed world state does not match the expected result. That means a tool can “succeed” but still leave the agent in the wrong state, and the evaluator will skip replanning before considering the mismatch diff. That is the biggest gap relative to your goal.

Second, inventory truth is still fragile. `WorldStateProjector.ApplyStatus` overwrites the whole inventory from a `StatusEvent` and clears the stale flag, while other event paths also mutate inventory incrementally. That is workable for simple play, but it is exactly the sort of model that breaks down when the agent needs robust long-horizon planning and reconciliation.

Third, the agent still has several fire-and-forget side effects that can outlive the goal that spawned them. In `SetGoal`, creative provisioning is launched with `CancellationToken.None`, so an old goal can keep enqueueing `/give` and `GetStatus` after a cancel or goal switch. That is a concrete bug risk and also a planning-model smell, because it means the “goal” boundary is not fully authoritative.

Fourth, the safety/command model is not yet as strong as the docs suggest. `SafetyOptions` says leading slashes are optional in the deny list, but `DeniedCommands` is checked directly against the lowercased command token without normalization, so a config entry like `op` may not match `/op` unless the config is already slash-prefixed. That is a correctness bug in the control surface that will matter once the LLM starts using richer command output.

My prioritization for making the stated goal achievable would be:

1. Make replanning sensitive to **world-state mismatch even when actions “succeed”**. The evaluator should not stop at “all outcomes succeeded” if the diff says the environment diverged.
2. Promote a real canonical runtime model around **ExecutionContext / Plan / ActionResult / WorldStateDiff / RecoveryReason**, and wire the live loop through the manager layer rather than keeping everything in `AgentBackgroundService`. The scaffolding is present, but it is not yet the main control path.
3. Refactor inventory into a reconciliation-friendly subsystem instead of overwriting snapshots from `StatusEvent` and mixing snapshot and incremental updates ad hoc. The existing TSK-0302 backlog is correctly pointing at this.
4. Remove uncancellable goal-scoped side effects, especially creative provisioning, so goal transitions are clean and deterministic.
5. Normalize and unify the command registry/denylist surface so the LLM sees one authoritative capability model, not a prompt list plus a separate partially normalized deny gate. The roadmap’s TSK-0303/0304 direction is aligned with this.

So the current state is: solid incremental progress, but still **reactive at the top level**. The repo already has the pieces for chained intent and stepwise execution; what is missing is a truly authoritative orchestration layer that can observe the world, compare it to expectation, decide whether the plan is still valid, and replan without human intervention.

---

The live codebase still has several deep correctness risks in the planning/execution path.

1. **Chat routing is over-permissive right now.** `IntentManagerImpl` hardcodes `onlinePlayers = 1` and passes `playerPosition: null` into the interpreter, while the prompt’s addressing rule says the bot should treat messages as addressed when only one player is online. That means the live path will systematically over-classify chat as being for the bot and it loses distance-based gating entirely.

2. **The observation/replan loop can miss real failures.** `AgentBackgroundService` computes a `WorldStateDiff` and passes it into `LlmEvaluatorImpl`, but the evaluator fast-path returns “continue” whenever all action outcomes succeeded. If a tool succeeds structurally but the observed world diverges from expectation, the evaluator can still suppress replanning. That is a direct mismatch with the intended “compare expected vs actual, then replan” behavior.

3. **`PlaceBlockGoal` can become “complete” before the actual placements finish.** `PlaceBlockGoalDecomposer` sets `pg.Dispatched = pg.Count` immediately during decomposition, and `PlaceBlockGoal.IsComplete` only checks `_dispatched >= _count`. That means the goal can look complete as soon as it is planned, not when the blocks are actually placed. This is a serious premature-completion bug.

4. **Creative provisioning is not goal-safe.** `SetGoal()` can launch `ProvisionGoalIfCreativeAsync(goal, CancellationToken.None)`, so the `/give`-based provisioning work is decoupled from the goal’s lifetime. If the goal changes or is canceled, that background work can still enqueue commands for the old goal.

5. **The deny-list contract is inconsistent with the runtime check.** `SafetyOptions` says commands can be configured with the leading slash optional, but the runtime checks `DeniedCommands.Contains(cmdLower)` against the slash-prefixed command token. If the config contains `kill` instead of `/kill`, the block can silently fail.

6. **Build origin provenance is being mislabeled.** `BuildGoalDecomposer` always stamps stored-world-state origins as `AutoScanned` unless the origin was explicit, even though the coordinates came from persisted facts. `BuildGoal` then renders that source in the description, so logs and downstream reasoning can misrepresent how the build origin was actually obtained.

The biggest architectural issue behind all of this is still the split-brain runtime: the new manager layer exists, but the live host still drives directly through `AgentBackgroundService`, so the “structured” planning/execution model and the actual control loop can drift apart.

The two fixes I would treat as most urgent are the replanning/diff bug and the premature completion bug in `PlaceBlockGoal`; both can make the agent look healthy while it is actually off-task.
