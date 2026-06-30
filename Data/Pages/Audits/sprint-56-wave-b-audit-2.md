# Sprint 56 Wave B Audit

## What this commit actually does

Seven targeted fixes from a 10-seat council review: `TaskSequenceGoal.IsComplete` deadlock, `/give` command injection, chat command deny list, `BlockNotFound` retry counter, LLM parse failure signaling, `ParseEvaluationResult`/`ExtractJson` testability, and `chatFilter.js` deletion. All seven ship with tests and task files. The commit is well-scoped, well-documented, and significantly healthier than most of what preceded it. Most of the council's P0/P1 findings are genuinely addressed.

That said, there are real bugs in this commit, several architectural gaps that will prevent autonomous multi-step operation, and the council's own synthesis understates the gap between "agent that reacts to one command at a time" and "agent that chains actions to achieve goals dynamically." That gap is the most important thing here.

---

## Confirmed bugs in this commit

### 1. Command deny list can be trivially bypassed via whitespace

```csharp
var cmdLower = intent.Item.Split(' ')[0].ToLowerInvariant();
var isDenied = DeniedCommands.Contains(cmdLower);
```

`Split(' ')` splits only on the ASCII space character. A tab-separated command like `/kill\t@e` produces `cmdLower = "/kill\t@e"` which doesn't match `"/kill"` in the HashSet. An LLM generating commands doesn't always use canonical spacing. The fix is one character: `Split()` (no argument, splits on all whitespace) or `Split(new[]{' ', '\t'}, ...)`.

More critically: `/tp` is not on the deny list but `/teleport` is — they're the same command. Same for `/msg` (in deny list) vs `/message` (not in). Minecraft has several alias pairs. The deny list approach is inherently whack-a-mole unless you normalize the command through a Minecraft-aware alias table or use a strict allowlist instead.

### 2. `SafetyOptions.DeniedCommands` default is silently wiped by partial config

```csharp
public HashSet<string> DeniedCommands { get; init; } = new(StringComparer.OrdinalIgnoreCase) { "/op", "/deop", ... };
```

ASP.NET Core's configuration binding replaces collection properties entirely when a config key is present — it does not merge. If an operator adds a single extra command to `appsettings.json` or an environment-specific override, the entire default set of 45+ commands disappears and only their entries remain. The docs in `SafetyOptions.cs` don't mention this. The `init` accessor means there's also no runtime protection against the set being empty. This should use a separate additive `AdditionalDeniedCommands` list that merges with a hardcoded minimum, or use `GetSection().Get<string[]>()` with explicit merge logic in the getter.

### 3. `TaskSequenceGoal.IsComplete` fix creates a semantic trap for all callers

The fix is correct for the specific bug it targets. But `IsComplete` now returns `true` when the current step is complete, even when more steps remain — meaning the value `true` now has two distinct meanings: "current step done, advance please" vs "whole sequence done, discard goal." Every call site that checks `IsComplete` must now either be sequence-aware or it will prematurely terminate the goal.

The diff shows `TryCompleteCurrentGoalFromWorldUpdate` was updated with the sequence-awareness guard, which is the event path. But the main dispatch loop presumably also checks `IsComplete` — and from the verification document, `TryAdvanceSequence()` in ABS gates on `IsComplete`. That creates a tight coupling: every consumer of `IsComplete` across the codebase now needs to know that `true` from a `TaskSequenceGoal` is not terminal. The cleaner fix would have been a separate `IsCurrentStepComplete(state)` or `TryAdvanceIfReady(state)` method that encapsulates the "check and advance" contract, so `IsComplete` only means "everything done."

### 4. The `BlockNotFound` retry counter fix is incomplete

The task description claimed the bug was that `HtnTaskLibrary` reads the counter as an integer but it's stored as a string. The actual fix only changes how C#-side `TryRouteAsError` reads the counter to be more robust (using `pc?.ToString()` instead of `pc is string pcs`). The underlying write is unchanged: still `(prevCount + 1).ToString()`. If `HtnTaskLibrary` is reading via direct integer cast (e.g., `(int)facts[key]`), this fix doesn't help HtnTaskLibrary — it helps the C# recovery path's own re-read. Worth verifying whether the progressive radius widening (40→80→120 blocks) actually activates in a live run after this fix, because the fix may have solved the wrong reader.

### 5. `AgentBackgroundService` still takes `IOptions<SafetyOptions>?` as nullable

```csharp
IOptions<SafetyOptions>? safetyOptions = null) : BackgroundService
```

Since `SafetyOptions` is properly registered in `Program.cs`, this will always resolve. The nullable parameter exists as a fallback but communicates to the constructor that safety configuration is optional, which is wrong. If the DI registration is accidentally removed, ABS silently runs with `DefaultDeniedCommands` — and because `DefaultDeniedCommands` and `SafetyOptions.DeniedCommands` are identical sets, both declared twice in the codebase, they can drift. This should be `IOptions<SafetyOptions>` (non-nullable), and `DefaultDeniedCommands` should be deleted in favor of having one canonical source in `SafetyOptions.cs`.

---

## Smaller things worth tightening

`ParseEvaluationResult` is now `internal static` and `ExtractJson` returns `null` on no-JSON input — both good. But `ExtractJson` uses first-`{`/last-`}` which is vulnerable to malformed JSON that has nested objects with a trailing `}` after the last property but before a closing prose sentence containing a `}`. Edge case, but worth documenting as a known limitation in the method's XML doc.

The test file `LlmEvaluatorImplTests.cs` uses `global::Agent.Planning.Llm` for the namespace — verify this matches the actual namespace in `LlmEvaluatorImpl.cs` after the `internal` visibility change, otherwise the tests won't compile.

`DefaultDeniedCommands` and `SafetyOptions.DeniedCommands` are literally copy-pasted from each other. Two sources of truth for the deny list that will drift. Delete `DefaultDeniedCommands`, use only `SafetyOptions`, and handle the DI fallback via `IOptions<SafetyOptions>` required injection.

---

## The real gap: autonomous multi-step goal-driven behavior

This is what your question is actually about, and the council audit touches it only obliquely. Here's a direct, honest assessment of where the architecture stands and what needs to change.

**What the agent currently is:** a reactive command processor. A human sends a goal, the planner generates a flat sequence of actions, actions are dispatched, outcomes are collected, and an LLM is occasionally asked "should we replan?" The agent can retry and recover, but it cannot autonomously decompose a novel goal into subgoals, adapt mid-execution based on world feedback, or pursue a long-horizon objective without human checkpointing.

**What's needed for the goal you described:**

### Layer 1 — Rich, accurate, real-time world state (prerequisite for everything)

The agent can't plan what it can't observe. Right now world state is reconstructed from six partially-authoritative sources with no single owner (inventory from events + status snapshots + projected actions), position from move events that may lag, block state only from explicit `queryBlocks` calls, entity state only from `entityObserved` events during scan windows. TSK-0281 (wire `updateSlot`) is in Backlog — it's the most important infrastructure change for autonomous operation. Without real-time inventory truth, any multi-step plan involving crafting, building, or gathering will create planner/world divergence that accumulates with every step.

The adapter already has `queryBlocks` and `queryEntities` — these are good. But the C# side doesn't automatically pull them before planning. The world state the LLM reasons about is largely stale by the time a plan executes. A "plan kickoff snapshot" — block scan of the immediate build area, entity scan for threats, inventory reconciliation — should happen as a mandatory precondition before any plan is generated.

### Layer 2 — Goals need preconditions, postconditions, and invariants

`IGoal.IsComplete(WorldState)` and `IGoal.HasFailed(WorldState)` exist, which is good. But the interface is missing two things that autonomous operation absolutely requires:

**Preconditions** — `CanAttempt(WorldState)`: returns false if the goal is not achievable from the current state (e.g., no wood nearby, no crafting table in range, HP too low). Right now the planner attempts everything and discovers failures mid-execution. Precondition checking allows the planner to either acquire prerequisites first or declare the goal infeasible early with a specific reason.

**Expected postconditions** — a description of what world state the goal produces when it succeeds. This is what lets a higher-level goal chain say "if I run GatherWood, my inventory will have oak_log >= 10 afterward" and then chain CraftPlanks after it. Without explicit postconditions, goal chaining is either hardcoded (brittle) or ad-hoc LLM inference (expensive and unreliable).

The `ExecutionCapabilities` model from the council audit (TSK Sprint 59+) is architecturally the right direction here — but it needs to arrive before autonomous multi-step operation is reliable, not after.

### Layer 3 — Plans need to be reactive structures, not flat lists

The current plan model is essentially `List<ActionData>`. For autonomous operation you need plans that can:

**Branch** — "if I reach the tree and it's oak, mine 10. If it's a different type, pivot to nearest oak." Right now branching requires a full replan request to the LLM. A plan graph or conditional step type would let the adapter handle simple branches locally.

**Loop** — "mine until inventory has 20 cobblestone, up to 5 attempts." The `mine` action takes a count, but at the plan level there's no "retry this subgoal until the world-state postcondition is satisfied" primitive. This matters enormously for survival play where block availability is variable.

**Monitor world state mid-execution** — the evaluation loop (`LlmEvaluatorImpl`) runs after batches of actions complete, not continuously. If the bot is halfway through building and gets attacked, the plan should interrupt, handle the threat, and resume — not finish the current batch and then discover at evaluation time that it died. The `entityObserved` event is wired (from the previous commit), but there's no plan-level interrupt handler that acts on it. Connecting `entityObserved` → immediate queue interruption → threat response → plan resume is the missing piece.

### Layer 4 — The LLM needs a richer, more structured context for planning

Looking at what the evaluator and planner prompts have access to today: goal description, recent action outcomes, inventory snapshot, and build blueprint for build goals. For autonomous multi-step planning, the LLM needs:

**Available action primitives with signatures and semantics** — the LLM should know what `craft`, `smelt`, `findReachableBlock`, `queryEntities` etc. do, what arguments they take, and what events they emit on success/failure. Right now this is buried in the prompt or implicit. An `ActionRegistry` — a structured list of available actions, their argument schemas, their typical outcomes, and their failure modes — passed to the LLM at plan time would let it generate plans that are syntactically and semantically valid the first time.

**The current plan state** — not just "here are the last N outcomes" but "here is the full current plan, here are the steps that succeeded, here are the steps that failed with structured reason codes, here are the steps still pending." This gives the LLM enough context to do surgical replanning (insert a crafting step before block #7) rather than full plan regeneration.

**World query results** — the `queryBlocks` and `queryEntities` adapter actions should be used proactively at plan evaluation time. Before deciding whether to replan, the evaluator should query the area around the failure point and include that block data in the LLM context. "Block at (12, 64, 8) is stone_brick_slab, not oak_log — the block changed since planning" is actionable. "no_block_found" alone is not.

### Layer 5 — Recovery needs to be a first-class replanning path, not a string-matching fallback

The council identified this (Audits B, C) and it's tagged for Sprint 59+. But it's directly blocking autonomous operation. Right now `TryRecoverFromGameErrorAsync` parses error strings and has hardcoded recovery logic. For truly autonomous operation:

The recovery output should be a `RecoveryPolicy` — structured data: `{action: "replan", reason: "missing_item", item: "crafting_table", suggestion: "craft or find crafting table first"}`. The planner takes this policy and generates an explicit recovery plan: navigate to crafting table location, craft it from inventory if materials exist, else gather materials, then resume original plan.

This closes the "replan from failure with a new subgoal" loop that autonomous operation requires. Right now the loop is broken: failures trigger string-parsed recovery with fixed strategies, not LLM-driven subgoal insertion.

---

## Concrete next actions, prioritized by impact on your goal

**Sprint 57 (prerequisite infrastructure):**

1. Wire `bot.inventory.on('updateSlot')` (TSK-0281). Single biggest enabler. Can't build reliable multi-step plans without inventory truth.
2. Mandatory "world snapshot before plan" — before any plan is generated, run `queryBlocks` in a radius around the bot + entity scan + `GetStatus`. Inject the results into the LLM planning context. Currently the planner works from stale cached state.
3. `ProvisionGoalIfCreativeAsync` await (TSK-0280). Simple fix, already spec'd.

**Sprint 58 (goal/plan model expansion):**

4. Add `CanAttempt(WorldState) → (bool ok, string? reason)` to `IGoal`. Implement for all goal types. Wire into the dispatch loop to fail-fast with a specific reason rather than discovering infeasibility mid-execution.
5. Add a conditional step type to the plan model — `ConditionalActionData` with `Condition: WorldStatePredicate`, `Then: ActionData`, `Else: ActionData?`. Even supporting only the "check inventory before crafting" case would eliminate a large class of wasted action cycles.
6. Wire `entityObserved` → plan interrupt. When a hostile entity is detected within N blocks while executing a plan, push a `ThreatResponse` goal to the front of the queue (flee, or eventually fight). Resume the original plan after the threat clears. This connects the only active threat sensor to something that actually responds.

**Sprint 59 (autonomous loop closure):**

7. `RecoveryPolicy` structured output — recovery produces a structured decision (`retry`, `replan`, `abandon`, `insert_subgoal: {goal_type, params}`) that the planner acts on. Not string matching, not hardcoded handlers.
8. `ActionRegistry` — a structured catalog of adapter actions with argument schemas and typical outcomes, passed to the LLM as a tool manifest. This enables the LLM to generate valid plans rather than hallucinating action names or argument structures.
9. `ExecutionCapabilities` model (already spec'd) — replaces scattered `IsCreativeMode` checks. As a side effect, it makes the action availability surface explicit to the planner: "current capabilities: CanMineBlocks=true, CanInstantBreak=false, NeedsFood=true." The LLM reasons about capabilities, not mode strings.

These seven changes — inventory truth, world snapshot, precondition checking, conditional steps, threat interrupt wiring, structured recovery, and action registry — are the minimal path from "reactive command processor" to "autonomous goal-pursuing agent." The council's architecture is correct; what it underspecifies is the ordering dependency: items 1-3 are blockers for 4-6, which are blockers for 7-9. The `TaskSequenceGoal` fix in this commit was essential groundwork. The rest of Sprint 56 Wave B cleaned up the most dangerous technical debt. The architecture is now clean enough to build the autonomous loop on top of it — but that loop hasn't been built yet.