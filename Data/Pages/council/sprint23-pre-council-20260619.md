# Sprint 23 Pre-Sprint 5-Chair Council Review
## MemorySmith.Agent Project

**Document ID:** MSAG-COUNCIL-S23-20260619  
**Date:** 2026-06-19  
**Branch Under Review:** `sprint-5-tool-safety`  
**Reference Commit:** `3bd22e0b` (Sprint 22 complete, CI queued)  
**Repository:** TheMasonX/MemorySmith.Agent  
**Review Type:** Pre-Sprint Council — Scope Validation and Risk Assessment  
**Status:** PENDING COUNCIL VOTE

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Evidence Synthesis](#evidence-synthesis)
3. [Proposed Sprint 23 Scope](#proposed-sprint-23-scope)
4. [Chair Reviews](#chair-reviews)
   - [Chair 1 — Source-Grounded Archivist](#chair-1--source-grounded-archivist)
   - [Chair 2 — Data Model Architect](#chair-2--data-model-architect)
   - [Chair 3 — Runtime Specialist](#chair-3--runtime-specialist)
   - [Chair 4 — Observability Advocate](#chair-4--observability-advocate)
   - [Chair 5 — Skeptical Reviewer](#chair-5--skeptical-reviewer)
5. [Findings Register](#findings-register)
6. [Chairman Synthesis and Recommendation](#chairman-synthesis-and-recommendation)

---

## Executive Summary

Sprint 23 proposes two parallel tracks of work: a real-time health interrupt system (P0-A) and world knowledge-base tool routing (P0-B), supported by three lower-priority housekeeping items (P1-A through P1-C). This council convened to assess whether the scope is internally consistent, architecturally sound, and free of blocking risks before any implementation work commences.

The council reviewed code-level evidence from Sprint 22's completed state, focusing on the WebSocket bridge, goal interface contracts, the background service event loop, and the memory gateway registration pattern. Five independent chairs contributed written assessments. The chairman's synthesis follows.

**Headline finding:** The interrupt design in P0-A is directionally correct but has two blocking concerns — a missing atomicity guarantee in queue-clear-plus-enqueue and an ambiguous definition of "combat goal" that could silently suppress health interrupts in non-combat scenarios. P0-B is considered low-risk once the injection site is confirmed. P1-A's semantic change (null default) requires a configuration migration notice. The remaining items are deferred housekeeping.

**Overall Recommendation:** CONDITIONAL — proceed with modifications detailed in this document.

---

## Evidence Synthesis

This section consolidates what is known with confidence from the Sprint 22 codebase, as distinct from what is being proposed for Sprint 23.

### What Sprint 22 Delivered (Confirmed State)

**Health event pipeline (Node.js → C# → WorldState):**  
The Node.js layer emits health data on a named event channel. The code `bot.on('health', () => sendEvent('health', { hp: bot.health, food: bot.food }))` is present and active. On the C# side, `WebSocketBridge.cs` deserializes the wire message matching the `"health"` discriminator into a typed `HealthEvent` record. `WorldStateProjector.cs` then applies the event via `ApplyHealth`, updating the `Health` and `Food` fields of the projected world state. This constitutes a complete, working pipeline.

**Critical health check positioning:**  
`ProcessEventsAsync` in `AgentBackgroundService` runs the health-critical check after every event dispatch — not exclusively after `StatusEvent`. This means a `HealthEvent` already triggers the threshold comparison and, if the bot's health is below threshold, enqueues a `GetStatus` call. This is architecturally significant: it means Sprint 22 already has a rudimentary health reaction, but without rate limiting or interrupt semantics.

**Known gaps inherited into Sprint 23:**
- No rate limiting: every `HealthEvent` below threshold causes a `GetStatus` enqueue. In a drowning scenario, Minecraft emits health updates at approximately 1 Hz, which would produce unbounded queue growth over a 20-second drowning event.
- No damage interrupt: the current behavior is additive — `GetStatus` is queued while the active action (mining, wandering) continues executing concurrently. There is no mechanism to stop or preempt the current plan.
- No `DamageTakenEvent` abstraction: C# does not yet compute health deltas; it only observes absolute `Health` values from the Node.js process.

**IGoal interface contract (Sprint 22 state):**  
The interface exposes `Name`, `Description`, `Phases`, `FailureReason {get;set;}`, `IsComplete`, and `HasFailed`. There is no policy property governing whether the goal permits external interruption. This means all goals are currently implicitly interruptible (or more precisely, nothing checks for interruptibility at all).

**World KB infrastructure (Sprint 22):**  
`RestMemoryGatewayOptions` holds a `WorldKbUrl` property defaulting to `"http://127.0.0.1:6869"`. The DI container in `Program.cs` registers a named `HttpClient` under the key `"memorysmith-world"` and registers a keyed singleton `IMemoryGateway` under the key `"world"`. This infrastructure exists but is not yet consumed by any tool.

**Tool injection status:**  
`SearchMemoryTool` and `CreatePageTool` both resolve `IMemoryGateway` via constructor injection using the default (unkeyed) registration, which points to the agent KB. Neither tool currently uses the `"world"`-keyed gateway. The `ToolDispatcher` registers tools by name and uses a factory pattern in `Program.cs`, which is the correct site for wiring the keyed injection.

### Architectural Tensions Identified in Evidence

**Tension 1 — Passive vs. Active health response.**  
The Sprint 22 approach (queue `GetStatus` when health is low) is fundamentally passive: the bot notices it is dying and asks for more information. Sprint 23 P0-A proposes converting this to an active interrupt: stop the current action, clear the queue, and re-plan. These are qualitatively different behaviors, and the transition raises questions about whether any partially completed actions (e.g., a block-place sequence mid-way through) need a rollback or cleanup step before the interrupt can safely take effect.

**Tension 2 — World KB routing and search intent.**  
The proposal routes `SearchMemory` to the world KB and `GetPage` to the agent KB. This is a reasonable split but is based on an inferred semantic: world observations are block/exploration data; code documentation lives in the agent KB. If the LLM issues a `SearchMemory` call intending to retrieve agent KB content (e.g., searching for a code pattern it remembers), the routing will silently deliver world observations instead. The proposal does not address how the LLM is informed of this routing change.

**Tension 3 — `AllowsDamageInterrupt` as a goal-level policy vs. a context-level policy.**  
The proposal adds `bool AllowsDamageInterrupt { get; }` to `IGoal`. This bakes the interrupt policy into the goal definition. However, whether a damage interrupt is appropriate might depend on context that the goal does not own — for instance, a `MineOreGoal` might want to allow damage interrupts when health drops to 4 HP but not at 16 HP. A binary interface property cannot capture threshold-relative behavior. This concern applies primarily to future extensibility, but the interface shape established in Sprint 23 will be difficult to change later.

---

## Proposed Sprint 23 Scope

### P0-A: Real-Time Health Response with Context Awareness

**Summary:** When the bot takes damage (health delta < 0), interrupt the current plan if the active goal's `AllowsDamageInterrupt` returns true. Interrupt means: emergency stop, clear action queue, enqueue `GetStatus`. Rate-limit the interrupt trigger to once per 3 seconds.

**New constructs required:**
- `DamageTakenEvent` — C#-side computed event, not received from Node.js wire
- `bool AllowsDamageInterrupt { get; }` added to `IGoal`
- `_lastDamageInterruptAt` field in `AgentBackgroundService`
- Health delta computation (compare `HealthEvent.Hp` to previously observed HP)

**Interaction with existing code:** `ProcessEventsAsync`, `WebSocketBridge.cs`, `WorldStateProjector.cs`, all `IGoal` implementations

### P0-B: World KB Tool Routing

**Summary:** Change `SearchMemoryTool` and `CreatePageTool` to resolve the `"world"`-keyed `IMemoryGateway` from the DI container instead of the default.

**New constructs required:** None beyond wiring changes in `Program.cs` or tool constructors

**Risk surface:** Narrow; primarily an injection change with no new abstractions

### P1-A: WorldKbUrl Null Default and Startup Warning

**Summary:** Change `WorldKbUrl` from `"http://127.0.0.1:6869"` to `null`. Log a warning at startup when null.

**Impact:** Existing deployments that rely on the default value silently connecting to a local world KB server will now get a warning and will route world KB writes to the agent KB instead.

### P1-B: Health Check Rate-Limit Guard

**Summary:** Add `_lastHealthCheckAt` to `AgentBackgroundService`. Enqueue `GetStatus` only when the elapsed time exceeds 2 seconds. Note: this is the existing passive health check guard, distinct from the 3-second interrupt rate limit in P0-A. Two separate rate-limit timestamps will coexist.

### P1-C: GatherGoalDecomposer TargetCount Comment

**Summary:** Add a code comment explaining the `TargetCount` propagation pattern. No behavioral change.

---

## Chair Reviews

---

### Chair 1 — Source-Grounded Archivist

**Role:** Consistency with prior sprint patterns, documentation gaps, wiki sufficiency, naming conventions, and change traceability.

**Confidence Score: 71%**

---

I approach this review by asking whether Sprint 23 continues patterns established in prior sprints or whether it introduces discontinuities that will burden future sprint retrospectives.

**Positive continuity observations:**

The `DamageTakenEvent` naming follows the established pattern for C#-side computed events — prior sprints have used the past-tense event name convention (`StatusEvent`, `HealthEvent`) consistently. The proposal correctly identifies that `DamageTakenEvent` is not a wire event from Node.js but a derived event computed in C#, which is consistent with how Sprint 22 treated `StatusEvent` aggregation. This distinction should be explicit in a docstring on the class; the archivist notes no such documentation requirement is called out in the scope.

**Concern B-1 — No migration notice for P1-A default change.**  
Changing `WorldKbUrl` from a concrete default to `null` is a breaking change for any deployment that implicitly depended on the localhost default. Sprint 22's pattern for configuration changes (see the `RestMemoryGatewayOptions` registration commit) included an inline XML doc comment explaining the default and its rationale. The Sprint 23 scope description does not specify whether the null change will be accompanied by updated XML docs, a CHANGELOG entry, or a migration note. Given that this project uses a handoff-driven sprint model, the absence of written migration guidance is a blocking concern: the next developer to pick up a deployment will have no in-code indication that the behavior changed between Sprint 22 and Sprint 23.

**Concern D-1 — IGoal interface change lacks a version note.**  
Adding `bool AllowsDamageInterrupt { get; }` to `IGoal` is an interface expansion. Sprint 21 introduced `HasFailed` to `IGoal` and at that time the team agreed to document interface evolutions in a comment block at the top of the interface file. There is no mention in the Sprint 23 scope of updating that comment block. This is deferred (not blocking) because it does not affect runtime behavior, but it will affect maintainability.

**Concern D-2 — Two rate-limit timestamps with different intervals.**  
P1-B introduces `_lastHealthCheckAt` (2-second gate) and P0-A's interrupt logic uses a separate 3-second gate. Both live in `AgentBackgroundService`. Sprint 22's pattern for similar multi-field state was to group related fields into a private nested record. Having two floating `DateTimeOffset` fields with similar names but different semantics is a naming-collision risk in future sprints. Recommended: name them `_lastHealthStatusEnqueuedAt` and `_lastDamageInterruptAt` respectively, and add a comment explaining why both exist. This is deferred but should be captured in the implementation ticket.

**Concern D-3 — Wiki coverage.**  
The Sprint 22 handoff document describes the health pipeline but does not describe the planned interrupt architecture. If Sprint 23 ships P0-A without a wiki update, the next sprint's council will have to re-derive the design from code. The scope should include a task: "Update architecture wiki section on health event flow to include interrupt semantics." This is deferred; nothing blocking here, but the pattern of skipping wiki updates is compounding across sprints.

**Summary from Chair 1:**  
One blocking concern (B-1: migration notice for null default), three deferred concerns. The scope is directionally consistent with prior sprint patterns. The archivist recommends conditional approval pending a written migration note for P1-A.

---

### Chair 2 — Data Model Architect

**Role:** IGoal interface evolution, DamageTakenEvent design, world KB type safety, extensibility for future combat scenarios.

**Confidence Score: 64%**

---

My review focuses on whether the data model choices made in Sprint 23 will support the system's stated future direction without requiring painful interface surgery in later sprints.

**On `DamageTakenEvent` design:**

The proposal to compute `DamageTakenEvent` C#-side by diffing consecutive `HealthEvent.Hp` values is sound for the current use case. However, the design as stated does not specify what data `DamageTakenEvent` carries. At minimum it needs: the previous HP, the new HP, and the computed delta. It should probably also carry a timestamp matching the originating `HealthEvent`. Without the timestamp, log correlation becomes ambiguous when two rapid damage events occur within the same event loop tick.

**Concern B-2 — `AllowsDamageInterrupt` is a binary policy on a gradient problem.**  
The proposed `bool AllowsDamageInterrupt { get; }` captures whether a goal permits interrupt at all. But the combat-goal use case described in the scope implies that the intent is "combat goals should never interrupt because interrupting a combat goal could be more dangerous than continuing." This is a valid use case. However, the design does not account for the inverse: a goal that should only interrupt at very low health (e.g., at or below 2 HP out of 20). A boolean cannot express this. The recommended design is `int? DamageInterruptThresholdHp { get; }` — returning null means "use system default," returning a value means "only interrupt if health drops below this threshold." This is more expressive and backward-compatible: the system default would be 6 HP (the current implied threshold), and combat goals would return null or a very low value. This change is blocking because the boolean form, once shipped as an interface contract, cannot be extended to carry a value without another breaking interface change.

**Concern D-4 — `DamageTakenEvent` should be a record, not a class.**  
The Sprint 22 pattern for events (cf. `HealthEvent`, `StatusEvent`) uses C# records for immutability. The scope does not specify the type shape of `DamageTakenEvent`. Implementors may default to a class. The archivist pattern for events in this codebase is records; this should be explicit in the implementation ticket. Deferred but important for consistency.

**On World KB type safety (P0-B):**

The current `IMemoryGateway` interface is shared between the agent KB and the world KB. This is appropriate for now, but the world KB is conceptually a different kind of store: it contains spatial and temporal observations rather than code-indexed documentation. Future sprints may need to distinguish between the two — for example, world KB writes might need a `BlockPosition` field that is meaningless in agent KB writes. Using a shared interface indefinitely will force future developers to work around the abstraction or pollute the interface with conditional fields.

**Concern D-5 — IMemoryGateway type divergence risk.**  
This sprint's scope does not need to resolve this, but Sprint 23 should include a note in the architecture docs acknowledging that `IMemoryGateway` is currently used for two semantically distinct stores, and that a future sprint should consider a `IWorldObservationGateway` subtype. If this note is not written now, the pattern will solidify without a record of the technical debt.

**On `WorldKbUrl` null default (P1-A):**

The semantic change is appropriate: the prior default value of `http://127.0.0.1:6869` was misleading because it implied the world KB server would be running at that address in all environments. Null is the correct sentinel for "not configured." The startup warning should be at `LogWarning` level (not `LogInformation`), because a missing world KB means world observations silently accumulate in the agent KB — a silent data contamination that could degrade future retrieval quality.

**Summary from Chair 2:**  
One blocking concern (B-2: boolean interrupt policy insufficient), two deferred concerns. Strongly recommend replacing `bool AllowsDamageInterrupt` with `int? DamageInterruptThresholdHp` before implementation begins. The world KB routing changes are architecturally sound.

---

### Chair 3 — Runtime Specialist

**Role:** Event routing correctness, concurrency, rate limiting edge cases, `ProcessEventsAsync` ordering, thread safety.

**Confidence Score: 68%**

---

I review this scope from the perspective of what actually happens at runtime when these features are exercised under load, specifically the drowning and rapid-damage scenarios cited in the sprint handoff.

**On the interrupt sequence (P0-A):**

The proposed interrupt is described as: emergency stop, clear queue, enqueue `GetStatus`. This is a three-step sequence. If `AgentBackgroundService` runs on a single background thread and the action executor runs on a separate thread (which is the standard pattern in this codebase), there is a window between "clear queue" and "enqueue GetStatus" during which the executor thread could observe an empty queue and enter an idle state. Depending on the idle-state implementation, this could cause the executor to emit a "nothing to do" log entry or trigger an idle callback. This is minor but could confuse log analysis.

**Concern B-3 — Clear-then-enqueue is not atomic.**  
More critically: if another event is processed between the queue clear and the `GetStatus` enqueue — for example, a block update event arriving in the same event batch — that event's handler could enqueue its own action before `GetStatus` is enqueued. The resulting queue state would be: `[block-update-action, GetStatus]`, which means the bot might attempt a block interaction before stopping. The fix is to make the interrupt sequence atomic: lock the queue during the clear-and-enqueue operation, or use a priority-queue pattern where the interrupt enqueue bypasses normal ordering. The current codebase does not appear to use a priority queue. This is blocking because the race is reproducible in the drowning-while-mining scenario (block updates arrive continuously while health degrades).

**On rate limiting (P0-A and P1-B):**

P0-A's 3-second rate limit on interrupt triggers is reasonable. P1-B's 2-second rate limit on `GetStatus` enqueue is also reasonable. However, the two limits must not interact in a way that suppresses a necessary interrupt. Scenario: health drops below threshold at T=0 (interrupt fires, `_lastDamageInterruptAt = T=0`). At T=1.5s, health drops further (still in interrupt window, suppressed). At T=3.5s, health drops below 2 HP (interrupt fires again). This is correct behavior. But consider the P1-B gate: if `_lastHealthCheckAt` is also updated by the interrupt path (because interrupt includes enqueuing `GetStatus`), then the 2-second health check gate might suppress a GetStatus that would have been queued by the passive health-check path between interrupt events. The two rate-limit fields need clearly specified update semantics: does the interrupt path update `_lastHealthCheckAt`? The scope does not say.

**Concern D-6 — Rate-limit gate update semantics are unspecified.**  
This is deferred because the worst case is a suppressed redundant `GetStatus`, not a missing interrupt. But it should be called out explicitly in the implementation ticket.

**On `ProcessEventsAsync` ordering:**

The health-critical check currently runs after every event. Adding damage delta computation means `ProcessEventsAsync` will need to retain the previous HP value across event loop iterations. This is straightforward state, but it must survive event batches: if the event loop processes 10 events in one batch, the "previous HP" for event 5 must be the HP after event 4 was applied, not the HP at the start of the batch. Verify that the projector applies events before the delta check reads the projected state.

**Concern D-7 — Previous-HP state initialization.**  
At startup, before the first `HealthEvent` arrives, `previousHp` is uninitialized (likely 0 or max HP depending on how the field is declared). If it is 0, the first `HealthEvent` carrying, say, 20 HP would compute a delta of +20 (health gain), which is correct behavior. If it is max HP (20), the first event at 20 HP computes a delta of 0, also correct. But if it is 0 and the first event carries 18 HP (bot spawned with 18 HP), delta would be +18, which is not a damage event — correct. The safe default is `float.MaxValue` or `null`, so that the first HealthEvent always computes no-damage on initialization. This should be specified in the implementation.

**Summary from Chair 3:**  
One blocking concern (B-3: non-atomic clear-then-enqueue), two deferred concerns. The rate-limit design is reasonable but needs specified update semantics. The event ordering concern is solvable with careful implementation guidance.

---

### Chair 4 — Observability Advocate

**Role:** Logging, testability, debuggability of the new health interrupt path, metrics, and operational visibility.

**Confidence Score: 79%**

---

Sprint 22 delivered a health pipeline that works but is largely invisible at runtime. A developer watching logs during a drowning event would currently see `GetStatus` appearing in the queue with no explanation of why. Sprint 23's interrupt system will be more aggressive and less reversible — the bot stops what it is doing. That irreversibility demands proportionate observability.

**What observability already exists:**

`AgentBackgroundService` logs at `LogInformation` when it enqueues `GetStatus` after a health check. This is the only health-related log entry in the pipeline. There are no structured log events for health delta, interrupt trigger, or queue-clear.

**What Sprint 23 must add (recommended, not yet in scope):**

For P0-A to be debuggable in production:
1. A structured log entry at `LogWarning` level when a damage interrupt is triggered: include `previousHp`, `currentHp`, `delta`, `goalName`, `AllowsDamageInterrupt` (or the threshold value if B-2 is resolved), and `wasRateLimited`.
2. A structured log entry at `LogDebug` when a `DamageTakenEvent` is computed but the interrupt is suppressed by rate-limiting.
3. A structured log entry at `LogInformation` when the interrupt clears the queue: include queue depth before clear.

**Concern B-4 — No logging requirements in scope.**  
The sprint scope does not mention logging requirements at all. Given that the interrupt path clears the entire action queue — an irreversible operation that discards potentially complex plan state — the absence of logging requirements is a blocking concern. If an interrupt fires incorrectly (false positive damage event due to a Node.js health normalization artifact), the only way to diagnose the issue post-hoc is through logs. Without structured log entries, the developer will have no evidence that an interrupt occurred, only that planned actions were not executed.

**On testability:**

The P0-A design requires a way to inject a fake clock for the rate-limit check (so unit tests can simulate the 3-second window without sleeping). The scope does not mention a clock abstraction. Sprint 22's `ProcessEventsAsync` already uses `DateTime.UtcNow` directly in at least one place, which is a pre-existing testability gap. Sprint 23 should not deepen this gap by adding two more `DateTime.UtcNow` call sites.

**Concern D-8 — No clock abstraction for rate-limit fields.**  
Recommend introducing an `ISystemClock` abstraction (or using `TimeProvider` from .NET 8, which this codebase should already support) and injecting it into `AgentBackgroundService`. This is deferred because tests can still be written with integration-style timing, but unit tests for rate-limit boundary conditions will be flaky without it.

**On world KB tool routing (P0-B) observability:**

`SearchMemoryTool` and `CreatePageTool` will silently route to different backends after this change. The LLM and the user will not know which backend was queried. Recommend adding a `LogDebug` entry in each tool indicating which gateway was used: `"SearchMemory routed to world KB"` or `"SearchMemory routed to agent KB"`. This enables debugging of retrieval failures where the wrong gateway is queried.

**Concern D-9 — No per-tool gateway routing log.**  
Deferred, but important for diagnosing future routing bugs.

**Summary from Chair 4:**  
One blocking concern (B-4: no logging requirements), two deferred concerns. The interrupt path is the highest-consequence new behavior in Sprint 23; it must emit structured logs at the point of trigger, suppression, and queue-clear. This is not optional for a production-grade agent.

---

### Chair 5 — Skeptical Reviewer

**Role:** Counterarguments, what could go wrong, edge cases, premature complexity, scope creep, and risk of over-engineering.

**Confidence Score: 58%**

---

My role is to argue the opposite case: why might this scope be the wrong set of work, or why might the proposed solutions create more problems than they solve?

**On P0-A (damage interrupt) — is this actually needed?**

The drowning scenario is real and cited as the motivating use case. However, let me examine the actual failure mode more carefully. When the bot is drowning, it is underwater. The current behavior is: health drops below threshold → `GetStatus` is queued → the bot's LLM is queried → the LLM presumably decides to surface. The question is: how long does this take? If the queue processes quickly and the LLM responds within 2-3 seconds, the bot may still survive a drowning event without an interrupt, because Minecraft's drowning damage occurs at 2 HP per second, starting when the air meter empties (roughly 15 seconds after submersion). A bot with 20 HP has approximately 9 seconds of drowning before death. If the LLM response latency is under 5 seconds, the passive approach may be sufficient.

The interrupt approach is more aggressive and introduces real risks: clearing the queue means discarding in-flight plan steps. If the bot was mid-way through a `PlaceBlock` action sequence, clearing the queue without rollback leaves the world state inconsistent with the bot's planned state. The scope does not describe how interrupted plans are cleaned up or whether they are re-queued after health recovery.

**Concern D-10 — No plan cleanup on interrupt.**  
When the queue is cleared on interrupt, any action currently executing (not yet in the queue, but dispatched to the executor) continues to completion. This means the interrupt stops future planned actions but does not stop the current action. If the current action is `Dig(block)` or `Walk(target)`, it will complete before the bot can react to the health emergency. Depending on how long action execution takes, this could still result in bot death. The scope should clarify whether the interrupt also sends a stop signal to the active executor, not just clears the queue.

**On P0-A — premature abstraction risk:**

Adding `AllowsDamageInterrupt` (or the threshold variant) to `IGoal` is motivated by a combat goal that does not yet exist. The codebase currently has no combat goals. Adding an interface property for a use case that is at least two sprints away is premature. The simpler approach for Sprint 23: always interrupt on damage (no goal-level policy), implement the combat exception when combat goals are introduced. This eliminates B-2 entirely and reduces the interface surface.

**Concern D-11 — Premature IGoal extension for non-existent combat goals.**  
This is a deferred concern but the skeptic argues it should be a blocking concern. The interface property, once shipped, becomes a contract that all future IGoal implementations must satisfy. Shipping a property now to satisfy a use case that does not exist creates implementation overhead and documentation debt across every future goal class. The council should vote on whether to defer this property to the sprint that introduces the first combat goal.

**On P0-B (world KB routing) — is the routing assumption correct?**

The proposal routes `SearchMemory` to the world KB and implies `GetPage` goes to the agent KB. But both tools are available to the LLM at the same time. The LLM's choice between `SearchMemory` and `GetPage` is based on its understanding of which tool retrieves which kind of content. After P0-B ships, `SearchMemory` will silently query a different backend than it did before. The LLM's existing understanding (if any, from system prompt or examples) may not match the new routing. This could cause the LLM to retrieve world observations when it expected code documentation, or vice versa.

**Concern B-5 — LLM prompt not updated to reflect new tool semantics.**  
If the system prompt or tool descriptions do not describe the new routing, the LLM will use the tools based on its prior understanding. This is a behavioral regression risk. Before P0-B ships, the tool descriptions (passed to the LLM as part of the tool manifest) should be updated: `SearchMemory` should say "searches world observations (block data, exploration notes)" and `GetPage` should say "retrieves agent knowledge base entries (code documentation, task history)." This is blocking because a routing change without a corresponding prompt update will produce incorrect retrieval behavior silently.

**On P1-A (null default) — operational risk:**

Any deployment running Sprint 22 with a local world KB server at `127.0.0.1:6869` will continue to work after Sprint 23 ships, because they should be explicitly setting `WorldKbUrl` in configuration. The null default only affects deployments that never set `WorldKbUrl` and relied on the default. The question is: does any deployment do this? In a sprint-based development project, the answer is likely "the developer's local machine." The change will affect local development without warning unless the `LogWarning` is prominent enough to be noticed. The skeptic recommends testing this change on the standard local development setup before the sprint ships.

**Summary from Chair 5:**  
Two blocking concerns (B-5: LLM prompt not updated; D-11 elevated to blocking question), one deferred concern. The skeptic recommends the council vote on whether `AllowsDamageInterrupt`/threshold should be deferred to the sprint introducing combat goals. Additionally, the LLM tool description update is not optional and must be explicitly in scope.

---

## Findings Register

This register consolidates all concerns raised by the five chairs. Each finding is classified as Blocking (B) or Deferred (D). Blocking findings must be resolved before implementation begins. Deferred findings are recorded for the sprint retrospective and the next sprint's council.

| Code | Classification | Raised By | Summary |
|------|---------------|-----------|---------|
| B-1  | BLOCKING | Chair 1 (Archivist) | P1-A null default change has no migration notice, XML doc update, or CHANGELOG entry specified |
| B-2  | BLOCKING | Chair 2 (Architect) | `bool AllowsDamageInterrupt` cannot express threshold-relative interrupt policy; recommend `int? DamageInterruptThresholdHp` |
| B-3  | BLOCKING | Chair 3 (Runtime) | Clear-then-enqueue interrupt sequence is not atomic; concurrent event handling can insert actions between clear and GetStatus enqueue |
| B-4  | BLOCKING | Chair 4 (Observability) | No logging requirements specified for interrupt trigger, suppression, or queue-clear; irreversible operation without audit trail |
| B-5  | BLOCKING | Chair 5 (Skeptic) | LLM tool descriptions not updated to reflect new SearchMemory routing to world KB; silent behavioral regression risk |
| D-1  | DEFERRED | Chair 1 (Archivist) | IGoal interface change lacks version note in the interface file's header comment block |
| D-2  | DEFERRED | Chair 1 (Archivist) | Two rate-limit timestamp fields need distinct, self-documenting names and an explanatory comment |
| D-3  | DEFERRED | Chair 1 (Archivist) | Architecture wiki not updated to reflect interrupt semantics added in P0-A |
| D-4  | DEFERRED | Chair 2 (Architect) | `DamageTakenEvent` should be specified as a C# record (not class) to match codebase event pattern |
| D-5  | DEFERRED | Chair 2 (Architect) | `IMemoryGateway` shared between agent KB and world KB without documented divergence risk; future sprint should introduce `IWorldObservationGateway` |
| D-6  | DEFERRED | Chair 3 (Runtime) | Rate-limit gate update semantics unspecified: does interrupt path update `_lastHealthCheckAt`? |
| D-7  | DEFERRED | Chair 3 (Runtime) | Previous-HP state initialization undefined; recommend `null` or `float.MaxValue` as safe startup default |
| D-8  | DEFERRED | Chair 4 (Observability) | No clock abstraction for rate-limit fields; direct `DateTime.UtcNow` usage makes unit tests for boundary conditions flaky |
| D-9  | DEFERRED | Chair 4 (Observability) | No per-tool gateway routing log in SearchMemoryTool or CreatePageTool |
| D-10 | DEFERRED | Chair 5 (Skeptic) | Interrupt clears queue but does not stop currently executing action; executor completes current action before responding to health emergency |
| D-11 | DEFERRED (contested) | Chair 5 (Skeptic) | `AllowsDamageInterrupt`/threshold property on IGoal is premature; combat goals do not yet exist; recommend deferring to sprint introducing first combat goal |

**Note on D-11:** Chair 5 argued this should be elevated to blocking. The chairman addresses this in the synthesis below.

---

## Chairman Synthesis and Recommendation

**Date of synthesis:** 2026-06-19  
**Synthesis author:** Council Chairman (rotating role)

### Reading the Evidence

The five chairs produced independent assessments with no significant factual contradictions. Where chairs disagreed, the disagreements were about severity classification (D-11 contention) and design preference (boolean vs. threshold in B-2), not about the underlying facts. This is a healthy signal: the codebase evidence is clear and consistently read.

The most consequential finding cluster is around P0-A, which received concerns from all five chairs. This is expected: P0-A is the most complex and highest-consequence item in the scope. P0-B received one blocking concern (B-5) that is easily resolved. P1-A received one blocking concern (B-1) that is documentation-level and straightforward. P1-B and P1-C received no blocking concerns.

### Resolution of Contested Findings

**D-11 elevation question (Skeptic vs. majority):**  
The skeptic argues that adding interrupt policy to `IGoal` before combat goals exist is premature. The chairman finds this argument partially convincing but does not elevate to blocking. Rationale: if B-2 is resolved by adopting `int? DamageInterruptThresholdHp`, the property carries its weight immediately — it allows the *current* environmental-damage interrupt to use a different threshold than the system default (e.g., a `GatherGoal` might interrupt at 8 HP while a `WanderGoal` might interrupt at 4 HP). The property is not purely for combat. However, the skeptic's concern about implementation overhead across all future goal classes is valid. Resolution: adopt B-2 (threshold form), add a default interface implementation of `int? DamageInterruptThresholdHp => null` (null = use system default), so existing and future goal implementations need not override unless they deviate from the default. This eliminates the overhead concern while retaining the extensibility benefit. D-11 remains deferred.

**B-2 resolution specification:**  
The council adopts the Chair 2 recommendation with the following refinement: `int? DamageInterruptThresholdHp { get; }` with a default interface implementation returning `null`. System default threshold is 6 HP (30% of max health). A return value of `null` means "use system default." A return value of `0` means "never interrupt (combat exception)." This covers the stated use cases and is extensible without another interface break.

### Per-Blocking-Finding Disposition

**B-1 (Migration notice):** Required before sprint close. Implementation ticket must include: update XML doc comment on `WorldKbUrl` property, add a CHANGELOG entry under Sprint 23, and add a startup log entry at `LogWarning` level with message `"World KB URL is not configured (WorldKbUrl is null). World observations will be stored in agent KB. Set WorldKbUrl in appsettings to enable world KB separation."` This message is more informative than the scope's proposed generic warning.

**B-2 (Boolean policy):** Required before implementation of P0-A begins. Interface change: `bool AllowsDamageInterrupt { get; }` is replaced by `int? DamageInterruptThresholdHp { get; }` with default interface implementation. All existing `IGoal` implementations inherit the default and require no changes. Implementation ticket updated.

**B-3 (Non-atomic sequence):** Required before implementation of P0-A begins. The implementation must use either a lock or a `CancellationToken`-based approach to make the clear-plus-enqueue operation atomic from the perspective of the event loop. Recommended pattern: acquire a write lock on the queue, perform clear and enqueue, release lock. If the codebase uses a `Channel<T>` for the action queue, consider using a `PriorityChannel` or a bounded interrupt slot that bypasses normal ordering.

**B-4 (Logging requirements):** Required before implementation of P0-A begins. The implementation ticket must specify the following log entries as acceptance criteria:
- `LogWarning` on interrupt trigger: include `previousHp`, `currentHp`, `delta`, `goalName`, `thresholdHp`, `queueDepthBeforeClear`.
- `LogDebug` on interrupt suppressed by rate limit: include `previousHp`, `currentHp`, `timeSinceLastInterrupt`, `rateLimitSeconds`.
- `LogInformation` on `GetStatus` enqueue by passive health check gate.

**B-5 (LLM tool descriptions):** Required before P0-B ships. The `SearchMemoryTool` and `CreatePageTool` descriptions passed to the LLM must be updated. Specific text (subject to implementation review): `SearchMemory` → "Searches the world knowledge base for spatial observations, block data, biome notes, and exploration history." `CreatePage` → "Creates or updates a page in the agent knowledge base for code documentation, task plans, and agent-specific notes." The tool manifest update is part of the P0-B scope.

### Deferred Finding Tracking

All deferred findings (D-1 through D-11) are logged for Sprint 23 retrospective review. The implementation team is asked to address D-2 (rate-limit field naming), D-4 (DamageTakenEvent as record), and D-7 (previous-HP initialization) during implementation as they are low-cost to resolve at the time of writing and have no dependency risk.

D-8 (clock abstraction) is formally logged as a Sprint 24 candidate. The chairman notes that `TimeProvider` from .NET 8 is the preferred approach and should replace all direct `DateTime.UtcNow` call sites in `AgentBackgroundService` in a single future PR.

### Per-Chair Confidence Assessment

| Chair | Role | Confidence | Trend |
|-------|------|-----------|-------|
| Chair 1 — Archivist | Documentation and consistency | 71% | Moderate concern; one blocking gap |
| Chair 2 — Architect | Data model and interface design | 64% | Below median confidence; B-2 is significant |
| Chair 3 — Runtime | Concurrency and event ordering | 68% | Moderate concern; B-3 is correctness-critical |
| Chair 4 — Observability | Logging and testability | 79% | Highest confidence overall; B-4 is completable |
| Chair 5 — Skeptic | Edge cases and risk | 58% | Lowest confidence; systemic concerns about P0-A complexity |

**Weighted mean confidence (unweighted average):** 68%

The 68% council confidence reflects genuine uncertainty about whether P0-A's interrupt design is correctly scoped. This is not a confidence crisis; it is an appropriate level of caution for a feature that modifies runtime action-queue state irreversibly. The confidence will rise to approximately 82% once the five blocking findings are resolved with their specified dispositions.

### Final Recommendation

**OVERALL RECOMMENDATION: CONDITIONAL APPROVAL**

Sprint 23 may proceed under the following conditions:

1. Blocking finding B-1 resolved: migration notice for P1-A documented in CHANGELOG and XML doc before sprint close.
2. Blocking finding B-2 resolved: `bool AllowsDamageInterrupt` replaced with `int? DamageInterruptThresholdHp` with default implementation before P0-A implementation begins.
3. Blocking finding B-3 resolved: atomic clear-plus-enqueue pattern specified and implemented; mechanism documented in implementation ticket before P0-A implementation begins.
4. Blocking finding B-4 resolved: logging requirements added as acceptance criteria to the P0-A implementation ticket before implementation begins.
5. Blocking finding B-5 resolved: LLM tool description updates added to the P0-B scope before P0-B implementation begins.

Conditions 2, 3, and 4 must be resolved before any P0-A code is written. Conditions 1 and 5 must be resolved before sprint close. All deferred findings are forwarded to Sprint 23 retrospective.

Sprint 23 scope (P0-A, P0-B, P1-A, P1-B, P1-C) is otherwise well-formed, consistent with Sprint 22 patterns, and achievable within a standard sprint cadence given the codebase's current state.

---

**Document closed:** 2026-06-19  
**Next review checkpoint:** Sprint 23 Mid-Sprint Health Check (scheduled by team lead)  
**Council record retained in:** MSAG-COUNCIL-S23-20260619
