# Sprint 25 Council Review
## TheMasonX/MemorySmith.Agent
### Sprint Theme: Tool Boundary Hardening + Action Lifecycle
**Review Date:** 2026-06-19
**Sprint:** 25

---

## Preamble

This council convenes to evaluate the implementation completeness, correctness, and quality of Sprint 25 deliverables. Six seats provide independent analysis before a binding verdict is issued. All seats are expected to be rigorous — rubber-stamp approvals are not acceptable.

Sprint 25 closed five P0/P1 items (P0-A through P0-D, P1-A) and explicitly deferred four items (P1-B, P1-C, P1-D, and all P2s). The implementation added approximately 17 new tests, bringing the suite to approximately 220+ total.

---

## Seat 1 — Source-Grounded Archivist

**Mandate:** Verify all stated claims against actual code. Flag divergences between sprint notes and implementation reality.

**Confidence: 72%**

*(Confidence is capped at 72% because the council does not have direct repository access in this review session; findings are based on the authoritative sprint summary and cross-referenced against known framework contracts.)*

### Findings

- **P0-A defaults are correctly stated.** The change from radius=20/minFlatArea=9 to radius=32/minFlatArea=25 aligns with the JS adapter constants FLAT_AREA_SCAN_RADIUS and FLAT_AREA_MIN_SIZE. The substitution of `TryGetInt32` for `r.GetInt32()` is a genuine safety improvement. Two tests are reported; the archivist notes these should cover at minimum the zero-radius edge case and the boundary condition where flatArea exactly equals minFlatArea. If either test only exercises the happy path, the safety gain is partially unverified.

- **P0-B deletion of StatusTool.cs is a clean architectural move,** but the archivist flags that the sprint notes say "ToolDispatchTests updated to use alias" — this implies the existing AllRegisteredTools test was modified, not merely that a new test was added. If the old test asserted on a fixed tool count, any future addition of tools will silently stay green unless the test was also updated to enumerate by name rather than by count. This is a latent maintenance hazard.

- **P0-C OperationCanceledException re-throw path is correctly documented** as tested, but the archivist cannot independently confirm that the catch block does not inadvertently catch `OperationCanceledException` before re-throwing in all compiler code paths (e.g., if it is wrapped in an `AggregateException` by the Task infrastructure before the catch boundary). The test must construct a genuinely cancelled `CancellationToken` and confirm the exception type propagates unchanged.

- **P0-D correlationId injection into ActionData.Context is stated but the contract for ActionData.Context is not specified in the sprint notes.** If `Context` is a `Dictionary<string, object>` or similar mutable map, concurrent reads during event dispatch could be a problem. The archivist flags this as requiring clarification from the Data Model Architect.

- **P1-A: Constructor copy claim is verifiable in principle.** "New Dictionary instances for _observed and _belief" and deep copy of inventory via `new Dictionary<string, int>(observation.Inventory)` are well-defined. The archivist notes the two tests should include a mutation test: modify the original after construction and assert the copy is unchanged.

### Blocking Items from This Seat
- None that rise to BLOCK level from documentation alone, but the archivist issues a **conditional flag**: if P0-A tests are happy-path only, this should be elevated to DEFERRED with a Sprint 26 requirement to add boundary tests.

### Deferred Items from This Seat
- Confirm AllRegisteredTools test enumerates by name, not count.
- Confirm P0-A tests include minFlatArea boundary and zero-radius edge case.

---

## Seat 2 — Data Model Architect

**Mandate:** Evaluate the PendingAction/ActionLifecycle design, ConcurrentDictionary usage, and record immutability.

**Confidence: 79%**

### Findings

- **PendingAction as a record is the correct choice.** Records provide value semantics and structural equality by default. However, the `State: ActionLifecycle` field creates a problem: records are immutable by default in C#, so transitioning state requires replacing the entire record in the dictionary. Using `ConcurrentDictionary<Guid, PendingAction>`, a state transition must use `TryUpdate(key, newValue, oldValue)` or `AddOrUpdate`. If the implementation uses direct assignment (`dict[id] = dict[id] with { State = newState }`), this is a non-atomic read-modify-write and introduces a race condition under concurrent event delivery. **This is the single most important correctness question from this seat.**

- **ConcurrentDictionary is the right collection choice** for `_correlatedActions`, replacing `List<PendingAction>`. The old `List` required external locking for any concurrent access from the background service and event handlers. The new `ConcurrentDictionary` makes concurrent reads and writes safe at the collection level. The race concern above is specifically about the update pattern, not the collection type.

- **ActionLifecycle enum ordering (Dispatched → Acknowledged → Completed/Failed/TimedOut) is logically sound.** The presence of `TimedOut` as a terminal state alongside `Completed` and `Failed` is good design — it distinguishes "we never heard back" from "we heard back with an error." The archivist notes there is no `Cancelled` state. If a tool is cancelled via `OperationCanceledException`, what state does the action reach? If the exception propagates (per P0-C), the background service's event loop may never receive a corresponding event, leaving the action in `Dispatched` until sweep. This is an acceptable design choice but should be documented.

- **`SweepTimedOutActions()` called from idle branch** is a reasonable placement, but "idle branch" implies it is only triggered when no events are being processed. In a high-throughput scenario, the sweep may never run, and stale `Dispatched` actions accumulate. A time-based trigger (e.g., sweep every N seconds regardless of idle state) would be more robust. This is a **deferred design concern**, not a blocking defect, because Sprint 25's scope is the initial implementation.

- **`StopAsync` logging of abandoned `Dispatched` actions** is good operational practice. The concern is whether "abandoned" is defined as `State == Dispatched` at shutdown time, or whether it includes `Acknowledged` actions (dispatched and acknowledged by the JS side but not yet completed). Both are abandoned from the agent's perspective. The sprint notes only mention `Dispatched`; `Acknowledged` may be silently dropped.

### Blocking Items from This Seat
- **CONDITIONAL BLOCK:** If `_correlatedActions` state transitions use non-atomic read-modify-write, this must be corrected before approval. If `TryUpdate` or `AddOrUpdate` is used correctly, this block is lifted.

### Deferred Items from This Seat
- Define behavior for `Acknowledged` actions at `StopAsync`.
- Consider a time-based sweep trigger as a Sprint 26 improvement.
- Document the `Cancelled`-action state gap.

---

## Seat 3 — Retrieval Specialist

**Mandate:** Evaluate whether correlationId lifecycle can actually trace dispatch to completion. Assess event-to-tool mapping completeness.

**Confidence: 68%**

### Findings

- **The correlation chain (C# dispatch → index.js echo → C# result event) is architecturally sound** but the end-to-end path has a gap the NUnit suite cannot close: the JS side. The sprint notes explicitly acknowledge "index.js correlationId echo is not verified by any test (JS tests are out of scope for NUnit)." This means the entire contract between C# and JS — that `correlationId` arrives correctly on the JS side and is echoed back on result events — is untested. If the JS serialization uses `camelCase` and the C# deserializer expects `PascalCase`, or if the `sendEvent` payload structure wraps the correlationId differently, the entire correlation system silently fails at runtime.

- **Event-to-tool mappings listed: moveComplete, wanderComplete, craftComplete, smeltComplete, flatAreaFound, statusEvent.** This is six event types. The retrieval specialist asks: are these the complete set of result events in the protocol? If `digComplete`, `buildComplete`, or any other result event exists in `index.js` but is not listed in `ProcessEventsAsync`, those actions will remain in `Dispatched` state indefinitely until swept. The event coverage appears to be the most significant operational correctness gap.

- **CorrelationId injected into ActionData.Context** — the retrieval specialist notes that if the JS side does not know to look in `Context` for the correlationId, it will not echo it. The contract must be explicit: the JS side reads `event.data.context.correlationId` (or however it is structured) and echoes it. Without a schema or integration test, this is faith-based.

- **State transitions on result events** are described as happening in `ProcessEventsAsync`. The retrieval specialist asks: does the lookup use `TryGetValue` on `_correlatedActions`? If an event arrives with an unknown or stale correlationId (e.g., a late result for a timed-out action), is it silently ignored or does it throw? Silent ignore is correct; throw would be a defect.

- **The 7 tests for P0-D** presumably cover the C# dispatch and state-transition logic. Without seeing the test names, the retrieval specialist cannot confirm they include: (a) a timed-out action being swept, (b) a result event arriving after timeout (late arrival), (c) a duplicate result for the same correlationId, and (d) an unknown correlationId in a result event.

### Blocking Items from This Seat
- **DEFERRED (cannot block without JS test infrastructure):** JS correlationId echo must be covered by an integration test or manual verification protocol in Sprint 26.
- **DEFERRED:** Enumerate all result event types in `index.js` and confirm `ProcessEventsAsync` handles every one.

### Deferred Items from This Seat
- Late-arrival result for timed-out action — confirm silent ignore.
- Duplicate result for same correlationId — confirm idempotent handling.
- Define a JS-side contract document (even informal) specifying the correlationId field location in event payloads.

---

## Seat 4 — Human Learning Advocate

**Mandate:** Evaluate runtime diagnostics, logging clarity, and operator experience improvements.

**Confidence: 84%**

### Findings

- **StopAsync abandoned-action logging is a meaningful operator win.** Before Sprint 25, a stopped agent would silently drop all in-flight actions with no record. Now, operators can inspect logs after an abnormal shutdown and know which actions were in-flight at stop time. This directly improves post-incident diagnosis.

- **CorrelationId in ActionData.Context means every log statement that includes the context will naturally carry the ID.** If the background service logs `correlationId` at dispatch time and at each state transition, an operator can grep a single GUID to reconstruct the full lifecycle of any action. Whether the implementation actually logs at each transition is not specified in the sprint notes. If logging is only at dispatch and final state, intermediate transitions (e.g., `Dispatched → Acknowledged`) are invisible in logs.

- **ToolDispatcher exception wrapping (P0-C) significantly improves operator experience.** Before this change, an unhandled exception in a tool would propagate up and likely crash the processing loop or produce an unstructured stack trace. Now, operators see a structured `ToolResult(false, ...)` with a message. The advocate recommends the error message include the exception type and message (not just a generic failure string) so operators can diagnose without enabling DEBUG logging.

- **SweepTimedOutActions surfacing timed-out actions** provides a new signal that was previously invisible. The advocate asks: does the sweep log the timed-out action's ToolName and DispatchedAt time? If so, operators can determine which tools are systematically slow or unresponsive. If the sweep only logs a count ("3 actions timed out"), the diagnostic value is much lower.

- **The AllRegisteredTools test update (P0-B)** is transparent to operators but matters for developer experience: a test that enumerates registered tools by name gives developers a living manifest of the tool registry. This is valuable onboarding documentation for new contributors.

### Blocking Items from This Seat
- None.

### Deferred Items from This Seat
- Confirm that ToolDispatcher exception messages include exception type and message string.
- Confirm SweepTimedOutActions logs per-action details (ToolName, DispatchedAt, correlationId), not just a count.
- Add logging at each ActionLifecycle state transition (Sprint 26).

---

## Seat 5 — Skeptical Reviewer

**Mandate:** Challenge assumptions, find gaps, ask hard questions about correctness.

**Confidence: 61%**

### Findings

- **The integer parsing inconsistency is the sharpest unresolved gap.** P0-C changes `ValidateAgainstSchema` to use `TryGetInt32` instead of `Contains('.')`. The sprint notes explicitly call out: "WorldModel `GetIntArg` for `JsonElement` uses `je.GetInt32()` — does this handle the same scientific notation case?" The answer is no: `JsonElement.GetInt32()` throws `InvalidOperationException` if the JSON token is a floating-point number (e.g., `1e3`). `TryGetInt32` in `JsonElement` is safer but still does not parse scientific notation. If the JS side ever sends `{"radius": 3.2e1}` (which is 32, a valid integer value), neither `GetInt32()` nor `TryGetInt32()` will handle it — `TryGetInt32` will return false and the argument will be silently dropped or defaulted. This is a latent correctness gap across `GetIntArg` in WorldModel and in the schema validator. The fix in P0-C only addressed the schema validator, not WorldModel. **This inconsistency must be tracked.**

- **The OperationCanceledException re-throw test depends on "the test creating a linked CTS correctly."** The skeptic asks: does the test actually cancel the token before or during tool execution? If the test creates a CancelledToken and the tool's `ExecuteAsync` checks it immediately, the test passes. But the real scenario is cancellation arriving mid-execution (e.g., during an async await inside the tool). A test that only pre-cancels the token does not verify mid-execution re-throw. This is a test quality concern, not a production correctness issue, but it reduces confidence in the coverage.

- **P0-B: "GetStatusTool registered under both 'GetStatus' and 'Status' names."** The skeptic asks: if someone calls `Register("GetStatus", tool)` and then `Register("GetStatus", differentTool)`, does the second registration overwrite, throw, or silently fail? The sprint notes introduce a new `Register(string name, ITool)` overload — its collision semantics are not specified. If it silently overwrites, a misconfigured Startup could shadow an existing tool with no warning.

- **P1-A WorldModel defensive copy uses `new Dictionary<string, int>(observation.Inventory)`.** This is a shallow copy, which is correct for `Dictionary<string, int>` since `int` is a value type. However, if `Inventory` is ever changed to `Dictionary<string, ItemRecord>` where `ItemRecord` is a reference type, this copy becomes a shallow reference copy and the defensive copy guarantee is broken. The skeptic recommends a code comment documenting the value-type assumption so future maintainers know to update the copy if the type changes.

- **P1-B (TryInterruptOnDamage) was a council B-2 carry from Sprint 24.** It is now deferred to Sprint 26. The skeptic notes: two consecutive deferrals of a B-level item is a pattern that should be explicitly acknowledged by the council. If this item is deferred again in Sprint 26, a formal escalation protocol should be triggered. The council should not allow indefinite deferral of blocking items.

- **The sprint reports "approximately 220+ tests (200 baseline + 17 new Sprint 25 tests)."** The use of "approximately" for a deterministic test count is a process concern. The exact count should be reportable from the CI system. Approximate counts suggest either the baseline was itself approximate or tests were added/removed during the sprint without tracking.

### Blocking Items from This Seat
- **DEFERRED (recommend Sprint 26 hard requirement):** `WorldModel.GetIntArg` scientific notation gap. P0-C fixed the schema validator but left `GetIntArg` unaddressed. These two code paths should use the same parsing strategy.
- **DEFERRED:** P1-B two-sprint deferral pattern — council must explicitly commit to Sprint 26 completion or escalate.

### Deferred Items from This Seat
- Document `Register` collision semantics (overwrite vs. throw vs. no-op).
- Add `// value-type assumption` comment to WorldModel defensive copy.
- Replace "approximately 220+" with exact CI-reported test count in sprint close.

---

## Seat 6 — Synthesizer

**Mandate:** Integrate all feedback into a final verdict with blocking/deferred triage and acceptance criteria.

**Confidence: 74%**

### Synthesis

Sprint 25 delivered a coherent set of hardening improvements. The P0 items (tool boundary safety, deduplication, exception wrapping, correlation IDs) form a well-scoped unit. P1-A (WorldModel defensive copy) is a clean, low-risk addition. The deferred P1-B/C/D and all P2s were correctly identified as out-of-scope for this sprint.

The council's aggregate analysis surfaces **one conditional block** (Data Model Architect Seat 2: state-transition atomicity), **no unconditional blocks**, and a cluster of deferred concerns that must be formally tracked.

The most significant concern is not a defect per se but a gap in test coverage boundary: the JS correlationId echo is a load-bearing contract with zero automated test coverage. The council accepts this as a practical limitation of the NUnit scope, but requires a manual verification protocol to be documented and executed before Sprint 25 is marked DONE in the backlog.

The integer parsing inconsistency (WorldModel `GetIntArg` vs. schema validator) is a real gap but does not affect any currently known production input — it is a latent risk. The council classifies it as deferred with a Sprint 26 hard requirement.

The two-sprint deferral of P1-B (TryInterruptOnDamage) is formally noted. If it is not completed in Sprint 26, the Synthesizer recommends it be elevated to a P0 in Sprint 27 planning.

---

## Explicit Dissent

**Seat 5 (Skeptical Reviewer) formally dissents from APPROVED verdict on one narrow point:**

The verdict is APPROVED conditional on the ConcurrentDictionary state-transition atomicity being confirmed. Seat 5 argues this confirmation should be required *before* the verdict is issued, not as a post-hoc condition. In the Skeptic's view, an unconfirmed race condition in the core action tracking data structure warrants a BLOCKED verdict pending a code review or test that specifically exercises concurrent state transitions. The Synthesizer notes this dissent and records it; the council majority accepts the conditional approval path because the risk is confined to a well-defined code pattern that is straightforwardly auditable.

---

## Blocking Findings (Must Fix Before Council Approval)

| ID | Finding | Owner | Resolution |
|----|---------|-------|------------|
| BLK-1 | Conditional: ConcurrentDictionary state transitions in `_correlatedActions` must use atomic `TryUpdate` or `AddOrUpdate`, not non-atomic read-modify-write (`dict[id] = dict[id] with { State = x }`). | Data Model Architect | Code review or targeted concurrency test confirming atomic update pattern. |

**If BLK-1 is confirmed resolved (by code review showing `TryUpdate`/`AddOrUpdate` usage), the verdict upgrades to unconditional APPROVED.**

---

## Deferred Findings (Track for Sprint 26)

| ID | Finding | Priority | Sprint 26 Action |
|----|---------|----------|------------------|
| DEF-1 | JS correlationId echo has no automated test coverage. | High | Manual verification protocol documenting expected payload structure; integration test if JS test infrastructure is added. |
| DEF-2 | Event-to-tool mapping completeness: enumerate all result event types in `index.js` and confirm `ProcessEventsAsync` handles every one. | High | Audit `index.js` sendEvent calls; add missing cases to ProcessEventsAsync. |
| DEF-3 | `WorldModel.GetIntArg` uses `je.GetInt32()` — does not handle scientific notation consistently with P0-C schema validator fix. | Medium | Align parsing strategy; consider a shared `ParseJsonInt` utility. |
| DEF-4 | P1-B (TryInterruptOnDamage) — second consecutive deferral of a Sprint 24 B-level item. | High | Hard commitment to Sprint 26 completion. If deferred again, escalate to P0 in Sprint 27. |
| DEF-5 | P1-C (GatherGoalDecomposer) — deferred. | Medium | Sprint 26 scope. |
| DEF-6 | P1-D (E2E gather test) — deferred. | Medium | Sprint 26 scope; depends on P1-C. |
| DEF-7 | `StopAsync` logs abandoned `Dispatched` actions but not `Acknowledged` actions. | Low | Extend to include Acknowledged state in Sprint 26. |
| DEF-8 | `SweepTimedOutActions` logging detail — confirm per-action details logged, not just count. | Low | Log audit in Sprint 26. |
| DEF-9 | `Register(string, ITool)` collision semantics undocumented. | Low | Add XML doc comment specifying overwrite/throw/no-op behavior. |
| DEF-10 | WorldModel defensive copy value-type assumption should be commented for future maintainers. | Low | One-line comment in Sprint 26. |
| DEF-11 | Replace "approximately 220+" with exact CI-reported test count in sprint close artifact. | Low | CI report pull in Sprint 26 retrospective. |
| DEF-12 | Time-based SweepTimedOutActions trigger (currently idle-branch only). | Low | Design consideration for Sprint 26 or Sprint 27. |

---

## Seat Confidence Summary

| Seat | Role | Confidence |
|------|------|------------|
| 1 | Source-Grounded Archivist | 72% |
| 2 | Data Model Architect | 79% |
| 3 | Retrieval Specialist | 68% |
| 4 | Human Learning Advocate | 84% |
| 5 | Skeptical Reviewer | 61% |
| 6 | Synthesizer | 74% |
| **Average** | | **73%** |

---

## Testable Acceptance Criteria

The following criteria must be satisfiable before Sprint 25 is marked DONE in the backlog:

### P0-A (FindFlatAreaTool)
- [ ] Test exists asserting default radius = 32 when no `radius` argument is provided.
- [ ] Test exists asserting default minFlatArea = 25 when no `minFlatArea` argument is provided.
- [ ] Test exists asserting `TryGetInt32` path handles a non-integer string without throwing (returns default).
- [ ] Tool description reflects updated defaults in its text.

### P0-B (StatusTool dedup)
- [ ] `ToolDispatcher` resolves `"GetStatus"` to the GetStatusTool instance.
- [ ] `ToolDispatcher` resolves `"Status"` to the same GetStatusTool instance.
- [ ] `StatusTool.cs` does not exist in the repository.
- [ ] AllRegisteredTools test enumerates by tool name (not count) or has been updated to reflect new alias count.

### P0-C (ToolDispatcher exception wrapping)
- [ ] A tool that throws `Exception` returns `ToolResult(false, ...)` from `CallAsync` — does not rethrow.
- [ ] A tool that throws `OperationCanceledException` causes `CallAsync` to propagate the exception (not return a ToolResult).
- [ ] `ValidateAgainstSchema` accepts JSON integer token via `TryGetInt32` path.
- [ ] `ValidateAgainstSchema` rejects JSON float token as not-integer.
- [ ] Error message in failed ToolResult includes the exception message string.

### P0-D (Action correlation IDs)
- [ ] `_correlatedActions` is populated at dispatch time with a new Guid key.
- [ ] State transitions from `Dispatched` to `Completed` (or `Failed`) on corresponding result events.
- [ ] `SweepTimedOutActions` transitions stale `Dispatched` actions to `TimedOut`.
- [ ] `StopAsync` logs the ToolName and correlationId of any action still in `Dispatched` state.
- [ ] State transitions use `TryUpdate` or `AddOrUpdate` (not direct index assignment) — **BLK-1 resolution gate.**
- [ ] (Manual / integration) `index.js` echoes `correlationId` in `moveComplete`, `wanderComplete`, `craftComplete`, `smeltComplete`, `flatAreaFound`, and `statusEvent` payloads.

### P1-A (WorldModel defensive copy)
- [ ] Mutating `_observed` after construction does not affect the source collection.
- [ ] Mutating source inventory after `Observe()` does not affect `_belief` inventory copy.
- [ ] Two separate `WorldModel` instances do not share the same `_observed` or `_belief` dictionary reference.

---

## Final Verdict

**APPROVED (Conditional)**

Condition: BLK-1 (ConcurrentDictionary state-transition atomicity) must be confirmed resolved by code review or targeted concurrency test before this verdict converts to unconditional approval. If BLK-1 is unresolvable without a code change, the verdict converts to **BLOCKED** pending that fix.

**Average Council Confidence: 73%**

The sprint delivered meaningful, well-scoped hardening work. The action correlation system is a valuable architectural foundation. The primary risks are in untested integration boundaries (JS echo) and a potential concurrency pattern in state transitions. Both are addressable without re-architecting the sprint's deliverables.

Sprint 26 must formally commit to P1-B (TryInterruptOnDamage), the JS correlation contract audit (DEF-1, DEF-2), and the WorldModel parsing alignment (DEF-3). These are not optional quality improvements — they are the unfinished load-bearing work from Sprint 25's design.

---

*Council review generated 2026-06-19. Next review: Sprint 26 close.*
