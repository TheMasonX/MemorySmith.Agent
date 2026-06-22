# MemorySmith.Agent Audit & Improvement Guide

**Generated:** 2026-06-21 19:32 UTC  
**Scope:** `TheMasonX/MemorySmith.Agent` on `main`  
**Goal:** Reduce brittleness, improve changeability, and make failure handling more explicit without losing the deterministic-first design.

## Executive summary

`MemorySmith.Agent` already has the right architectural intent: it is organized around bounded contexts, uses a dedicated planner/router layer, validates tool inputs at a dispatch boundary, and treats LLM behavior as a fallback rather than the primary control plane. That intent is visible in `Data/Pages/architecture.md`, `Data/Pages/planner.md`, `Data/Pages/chat-system.md`, `Agent.Planning/Router/PlannerRouter.cs`, and `Agent.Tools/ToolDispatcher.cs`.

The brittleness comes from **accumulated policy in hot paths**.

The biggest sources are:

- `WebUI.Blazor/AgentBackgroundService.cs` has become a coordination god-class, handling reconnects, goal lifecycle, journaling, health interrupts, replanning, dashboard signaling, and pending-action correlation in one place.
- `Agent.Planning/ChatInterpreter.cs` and `Agent.Planning/LlmChatInterpreter.cs` contain layered parsing, aliasing, direct command handling, proximity heuristics, rate limiting, LLM prompting, LLM parsing, truncation recovery, and fallback policy.
- `Agent.Tools/ToolDispatcher.cs` is the correct place for validation and execution safety, but the current architecture still lets a lot of malformed intent and failure semantics leak into the rest of the system.

The most effective improvement is not “add more LLM.” It is to introduce a **single normalized intent contract** and a **formal repair path** so the system can recover from ambiguity without changing meaning at every layer.

## What is actually brittle today

### 1) Chat parsing mixes too many responsibilities

`Agent.Planning/ChatInterpreter.cs` is doing more than intent parsing. It now owns:

- directed-at-bot heuristics,
- item aliases,
- craft aliases,
- build parsing with optional coordinates,
- go-to parsing,
- status/help/cancel parsing,
- gather parsing,
- and a fallback “unknown” response.

That makes change risk high, because adding one pattern can shift behavior in another branch. The file is already carrying policy decisions that should probably be data or configuration, not code.

`Agent.Planning/LlmChatInterpreter.cs` adds another layer:

- distance gating,
- deterministic fast-paths,
- rate limiting,
- a structured LLM request,
- JSON parsing,
- truncated JSON recovery,
- and fallback to the pattern parser.

This is a reasonable evolution path, but it is also the definition of a fragile “multi-parser” system. The LLM path is acting like a second parser rather than a repair step.

### 2) The host loop is too central

`WebUI.Blazor/AgentBackgroundService.cs` has expanded into the control center for the agent. From the architecture docs, it is the place where event processing, goal evaluation, replanning, action dispatch, damage interrupts, and journal updates converge. That is useful for now, but long-term it makes the system hard to evolve because many unrelated fixes touch the same file.

### 3) The boundary semantics are partly implicit

`Agent.Tools/ToolDispatcher.cs` is doing the right thing by validating inputs against tool schemas before execution, but the rest of the system still relies on a chain of assumptions:

- an interpreted chat message became a valid goal,
- the goal produced a valid plan,
- the plan produced a valid action,
- the action matched the adapter expectations,
- and failures will be handled later.

That is how brittleness accumulates: every layer assumes the previous layer “basically got it right.”

### 4) Behavior is spread across code and docs

The repo is self-documenting through `Data/Pages/*`, which is a strength. But when policy is repeated in code comments, wiki pages, and handoff docs, the source of truth becomes fuzzy. A future change can satisfy one place and silently contradict another.

## The design principle that should govern the cleanup

Keep the current deterministic-first strategy, but make uncertainty explicit.

The system should distinguish these outcomes early and consistently:

- `NotAddressed`
- `Parsed`
- `NeedsClarification`
- `NeedsRepair`
- `Validated`
- `Rejected`
- `Executed`
- `FailedAtExecution`

Right now, several of those states are collapsed into `Unknown` or “fallback to the other parser.” That is convenient, but it obscures intent and makes debugging harder.

## Recommended target architecture

### A. Introduce a normalized intent model

Create a single canonical result type for chat interpretation and command parsing, for example:

- `NormalizedIntent`
- `IntentConfidence`
- `RepairHint`
- `ValidationState`

This type should flow from `ChatInterpreter` / `LlmChatInterpreter` into the goal layer and should represent:

- what the user wants,
- whether the message is addressed to the bot,
- whether the result is actionable,
- whether repair is needed,
- and what fields are missing or ambiguous.

The goal is to stop passing raw ad hoc shapes around.

#### Where this belongs

- `Agent.Planning/Interfaces/IChatInterpreter.cs`
- `Agent.Planning/ChatInterpreter.cs`
- `Agent.Planning/LlmChatInterpreter.cs`
- `WebUI.Blazor/AgentBackgroundService.cs`

### B. Split parsing from recovery

`Agent.Planning/ChatInterpreter.cs` should become the deterministic parser only.

It should not:
- call LLMs,
- perform rate limiting,
- know about truncation recovery,
- or own fallback policy.

`Agent.Planning/LlmChatInterpreter.cs` should become a **repair provider** that is only invoked when the deterministic parser returns `NeedsRepair` or `NeedsClarification`.

This is the single biggest change that would reduce brittleness without losing flexibility.

### C. Make failure states first-class

The system should not treat all failures as “Unknown.” Instead, use explicit failure categories:

- parse failure,
- validation failure,
- schema mismatch,
- missing required parameter,
- LLM timeout,
- LLM JSON parse error,
- tool execution exception,
- world-adapter failure,
- plan stall,
- and stale world state.

This makes logging, metrics, and retries much more meaningful.

### D. Keep validation at the dispatch boundary

`Agent.Tools/ToolDispatcher.cs` is the right place to enforce tool schema constraints. Keep that boundary strict.

All tool execution should continue to funnel through `ToolDispatcher.CallAsync(...)`, and any repair or fallback should still have to satisfy the schema before dispatch.

## Concrete implementation guidance

## 1. Refactor chat parsing into a pipeline

### Current pain point

`Agent.Planning/ChatInterpreter.cs` is doing token matching, alias resolution, intent classification, and object construction in one pass.

### Recommended shape

Split into pure stages:

1. **Preprocess**
   - trim, normalize whitespace, cap length
   - optionally remove bot name / handle quoting

2. **Deterministic classify**
   - detect direct command patterns
   - determine whether the message addresses the bot
   - produce an explicit `Parsed` / `NotAddressed` / `NeedsRepair` result

3. **Normalize**
   - map aliases to canonical Minecraft IDs / blueprint IDs
   - normalize counts and coordinates
   - validate field presence

4. **Repair**
   - if deterministic parsing is incomplete, send a compact prompt to the LLM
   - require structured output only
   - return a repair result, not a side effect

5. **Validate**
   - enforce schema and domain constraints
   - reject impossible or underspecified intents

6. **Execute**
   - hand the normalized result to the goal layer

### Why this helps

It reduces the “one new regex changed three commands” problem and makes tests smaller.

### File-by-file guidance

- `Agent.Planning/ChatInterpreter.cs`
  - keep regexes and alias maps here or move them into dedicated static helpers
  - remove repair logic
  - return a richer parse result instead of final action intent when possible

- `Agent.Planning/LlmChatInterpreter.cs`
  - rename conceptually to something like `ChatIntentRepairer` or `IntentRepairService`
  - invoke only on unresolved or ambiguous parses
  - produce structured repair output, not direct action

- `Agent.Planning/Interfaces/IChatInterpreter.cs`
  - consider splitting into `IChatParser` and `IChatRepairer`
  - this makes dependency injection clearer and tests simpler

## 2. Make fallback a repair path, not a second parser

### Direct answer to your question

Yes: allowing parse failures to fall back to LLM parsing can remove a lot of brittleness, **but only if it is constrained as a repair step**.

It should be used for:
- ambiguous user language,
- partial commands,
- missing item or blueprint names,
- natural language that is close to a known intent,
- or truncated/ill-formed responses from the model.

It should **not** be used for:
- server/system messages,
- direct tool execution,
- bypassing domain validation,
- or rescuing obvious parser bugs that should be fixed in code.

### Best-practice policy

Use the LLM only when one of these is true:

- deterministic parser returns `NeedsRepair`,
- deterministic parser returns `NeedsClarification`,
- the message contains a likely command but is missing required fields,
- or the parser cannot map an otherwise valid request to a canonical item/blueprint ID.

Do not use the LLM simply because a regex failed.

### Repair contract

The repair output should be validated against a strict schema before it can be converted into a goal. For example, the repairer may return:

- `intent`
- `item`
- `blueprint`
- `count`
- `coordinates`
- `response`
- `confidence`
- `needsClarification`

Then the same domain checks used for deterministic parsing should run again.

### Practical rule

If the LLM output cannot be validated, the result should become either:
- `NeedsClarification`, or
- `Rejected`

not “best effort but probably okay.”

## 3. Shrink `AgentBackgroundService`

### Current pain point

`WebUI.Blazor/AgentBackgroundService.cs` is a coordination knot.

### Recommended extraction targets

Extract separate services for:

- `ReconnectPolicy`
- `GoalLifecycleService`
- `DamageInterruptService`
- `ReplanGovernor`
- `DashboardPublisher`
- `PendingActionTracker`

`AgentBackgroundService` should become a thin orchestrator that wires these pieces together.

### Resulting benefit

You get:
- smaller tests,
- fewer cross-cutting regressions,
- easier onboarding,
- and clearer ownership when debugging behavior.

### Method-level guidance

Focus first on the methods that are likely doing too much:

- `ExecuteAsync(...)`
- `SetGoal(...)`
- `CancelGoal(...)`
- `ProcessEventsAsync(...)`
- `DispatchActionsAsync(...)`
- `HandleChatEventAsync(...)`

Even if you do not split them immediately, isolate the rules they call into smaller services.

## 4. Keep the dispatcher strict, but make errors richer

`Agent.Tools/ToolDispatcher.cs` is the right boundary for schema validation and execution wrapping. Preserve that.

However, its failure model should be more specific than just `ToolResult(false, "...")`.

Recommended improvements:
- include an error code or category,
- keep a human-readable message,
- include the tool name,
- and include schema validation details separately from execution failure details.

That makes retries and telemetry far more useful.

### Suggested categories

- `UnknownTool`
- `SchemaValidationFailed`
- `ExecutionThrew`
- `ExecutionCancelled`
- `ExecutionReturnedFailure`

## 5. Move policy data out of code where possible

The following are good candidates for options or data files rather than hard-coded branches:

- alias mappings,
- maximum chat length,
- bot proximity threshold,
- LLM timeout,
- rate-limit windows,
- stall thresholds,
- and some build/item naming rules.

This does not mean “config everything.” It means anything that changes often or varies by deployment should not require code edits.

## 6. Add tests around the exact brittle boundaries

The repo already has a lot of tests, which is a strength. Add focused tests for the seams that are currently fragile.

### Chat parsing tests
Cover:
- direct commands,
- ambiguous commands,
- bot-name detection,
- proximity-based address detection,
- craft/build alias normalization,
- partial/truncated inputs,
- and fallback behavior.

### Repair tests
Cover:
- deterministic parse fails, repair succeeds,
- repair returns invalid schema, reject,
- repair times out, fallback to clarification,
- repair returns conflicting fields, reject.

### Host-loop tests
Cover:
- repeated failures do not deadlock the loop,
- reconnect behavior,
- damage interrupt cooldown,
- stall governor transitions,
- and cancellation behavior.

### Dispatcher tests
Cover:
- schema validation,
- missing required fields,
- unexpected fields,
- non-integer numeric values,
- tool exceptions,
- cancellation propagation.

## 7. Tighten the feedback loop between docs and code

The self-documenting wiki is good, but it should be traceable.

Every time behavior changes in:
- `Agent.Planning/ChatInterpreter.cs`
- `Agent.Planning/LlmChatInterpreter.cs`
- `Agent.Planning/Router/PlannerRouter.cs`
- `Agent.Tools/ToolDispatcher.cs`
- `WebUI.Blazor/AgentBackgroundService.cs`

update the corresponding wiki page in `Data/Pages/` in the same change. That keeps the docs from drifting into mythology.

## Open questions, resolved

### Should error states be allowed?
Yes. They should be **explicit** rather than swallowed.

### Should parse failures fall back to LLM parsing?
Yes, but only as a controlled repair path with schema validation, not as a blanket second parser.

### Will this resolve most brittleness?
It will resolve the brittleness caused by ambiguous or partial input, and it will make failures much easier to diagnose. It will not fix architectural brittleness caused by orchestration overload or duplicated policy. That requires the refactors above.

### Should the LLM be trusted to “figure it out”?
No. It should be used to propose a normalized interpretation that still must pass domain validation.

## Recommended rollout sequence

### Phase 1: carve the seams
- Introduce a normalized intent result.
- Separate parse, repair, and validation concerns.
- Keep behavior identical where possible.

### Phase 2: move fallbacks behind the seam
- Recast `LlmChatInterpreter` as repair-only.
- Require validated structured output.
- Keep deterministic fast-paths first.

### Phase 3: simplify the host
- Extract subservices from `AgentBackgroundService`.
- Make orchestration thin and explicit.

### Phase 4: move policy to configuration
- Externalize thresholds and aliases where they are stable enough to do so.
- Keep only truly static core logic in code.

### Phase 5: harden with tests
- Add seam-focused tests before and after each refactor.
- Prefer behavior tests over implementation-detail tests.

## Acceptance criteria

You will know the repo is getting healthier when:

- adding a new chat command only requires touching one parser path,
- LLM recovery cannot bypass validation,
- `AgentBackgroundService` shrinks instead of grows,
- a parse failure always has a named reason,
- and a tool failure can be traced to a specific category without guesswork.

## Bottom line

The best path forward is not to make the system more “dynamic” in the vague sense. It is to make uncertainty explicit, reduce duplicated parsing logic, and keep LLM assistance in a narrow repair lane.

That preserves the strengths already present in `Agent.Planning/Router/PlannerRouter.cs`, `Agent.Tools/ToolDispatcher.cs`, and `Data/Pages/architecture.md`, while removing the hidden complexity that currently makes the repo feel brittle.

