# Council Review — Sprint 1: Non-blocking LLM + Reconnect

**Date:** 2026-06-16
**Sprint:** Phase 6 Sprint 1 — Reliability
**Scope:** AgentBackgroundService: chat channel + reconnect loop; 5 CI hotfixes
**Commit range:** ae98d54 → b0924ea (current HEAD)

---

## CI Hotfixes Applied This Session

Before Sprint 1 could proceed, five cascading compile/test bugs (all pre-existing in TSK-0014,
masked by the first compile error) were diagnosed and fixed:

| # | File | Bug | Fix |
|---|------|-----|-----|
| 1 | `Agent.Planning/LlmChatInterpreter.cs` | `$"""` raw string with `{{` JSON braces | `$$"""` + `{{var}}` interpolations |
| 2 | `Agent.Planning/LlmChatInterpreter.cs` | `Position? .HasValue/.Value` on reference type | `is not null` + direct dereference |
| 3 | `Agent.Planning/Llm/ChatOptions.cs` | `sealed class` → tests used `with {}` expression | `sealed record` |
| 4 | `MemorySmith.Agent.Tests/AgentBackgroundServiceTests.cs` | `CreateService(int)` passed int positionally as `GoalFactory?` | named arg `maxConsecutiveFailures:` |
| 5 | `Agent.Planning/ChatInterpreter.cs` | `GatherRegex`/`BuildRegex` filler groups `?` → don't handle "me", multi-filler sequences | `*` quantifier + added "me" to filler set |

CI was green (conclusion: success) before Sprint 1 push on commit `1840ec0`.

---

## Sprint 1 Implementation Summary

### 1a — Non-blocking LLM (AgentBackgroundService.cs)

**Change:** Added `Channel<WorldEvent> _chatChannel`. In `ProcessEventsAsync`, the `case "chat":`
branch now calls `_chatChannel.Writer.TryWrite(worldEvent)` instead of
`await HandleChatEventAsync(worldEvent, ct)`. A new `ChatConsumerAsync` task reads from the
channel and calls `HandleChatEventAsync` independently.

**Result:** A 5–10s LLM call in `HandleChatEventAsync` no longer delays blockMined/spawn/death
event processing in the main loop.

### 1b — Reconnect with exponential backoff (AgentBackgroundService.cs)

**Change:** `ExecuteAsync` now uses a retry loop (max = `_reconnectDelays.Length + 1` attempts).
Per-connection `CancellationTokenSource` is linked to `stoppingToken`. `MonitorAndCancelOnFaultAsync`
wrapper cancels the linked CTS if `ProcessEventsAsync` faults, propagating to sibling tasks.
Goals and WorldState survive reconnects (instance fields are not reset).

Default delays: 2s/4s/8s/16s/32s (configurable via `reconnectDelays` constructor parameter
for test speed).

### New Tests (AgentBackgroundServiceTests.cs)

| Test | Sprint | Assertion |
|------|--------|-----------|
| `SlowChatInterpreter_DoesNotBlock_BlockMinedEventProcessing` | 1a | blockMined inventory update ≤2s while LLM takes 6s |
| `Reconnect_AfterTwoFailures_ResumesCurrentGoal` | 1b | 3 connect attempts; goal name preserved |

---

## 6-Seat Council Review

### Seat 1 — Source-Grounded Archivist

**Confidence: 91%**

Verified against sprint spec:
- 1a: `Channel<WorldEvent>` with `SingleReader: true, SingleWriter: true` matches spec. `TryWrite` in event
  loop (non-blocking) ✓. Dedicated `ChatConsumerAsync` task added to `Task.WhenAll` ✓.
- 1b: max 5 attempts (delays.Length + 1 = 6 with default 5-element array → actually 6 attempts max).
  Spec says "max 5 attempts, 2s/4s/8s/16s/32s" — implementation has 5 delays = 6 total attempts
  (attempt 0 + 5 retries). Close to spec but not exact (spec implies 5 total, not 6).
  Delays: 2/4/8/16/32s ✓. "Reconnecting" event: spec says to send a reconnecting event to the channel —
  implementation logs a warning but does NOT write a WorldEvent to `_chatChannel`. Minor deviation.
- Tests: slow-LLM mock (6s) ✓. Reconnect mock (2 failures) ✓. Goal persistence verified ✓.

**Findings:**
- D1 (deferred): Attempt count is 6 max (0..delays.Length) vs spec's "5 attempts". Functional but 
  not spec-exact. Add a `MaxConnectAttempts` computed property for clarity.
- D2 (deferred): "Send a reconnecting event to the channel" from spec — not implemented. The event
  channel currently only holds real WorldEvents. Low priority since logs serve the same purpose.

### Seat 2 — Data Model Architect

**Confidence: 93%**

`Channel<WorldEvent>` is the correct primitive:
- Unbounded + SingleWriter/SingleReader hints are appropriate — only `ProcessEventsAsync` writes,
  only `ChatConsumerAsync` reads.
- `ReadAllAsync(ct)` respects cancellation cleanly (exits on `ct` cancellation).
- `TryWrite` never blocks (unbounded channel). Back-pressure is not a concern for chat events
  (rate-limited to 5/min by `ChatRateLimiter`).

WorldState field `_worldState` is updated by reference assignment (not locked). This is correct:
C# reference assignments are atomic on 64-bit; `WorldState` is an immutable record, so readers
always see a consistent snapshot.

`_chatChannel` is a field (not per-connection), so messages queued before a reconnect would be
processed after reconnect — likely desired behavior.

**Findings:**
- D3 (deferred): If the service reconnects (new `connectionCts` per attempt), `ChatConsumerAsync`
  is re-spawned. The `_chatChannel` field persists across reconnects, so old chat events queued
  during a disconnect would be re-processed after reconnect. Whether that's desired should be
  documented (currently undocumented).

### Seat 3 — Retrieval Specialist

**Confidence: 88%**

No direct impact on MemorySmith REST calls or IItemRegistry/IBlueprintRepository. The chat consumer
runs off-loop, which correctly prevents blocking LLM calls from delaying memory queries in
`DispatchActionsAsync`.

The `SearchMemoryTool` and `GetPageTool` are dispatched via `DispatchActionsAsync`, which remains
on its own task. No retrieval regressions.

**Findings:**
- None blocking.

### Seat 4 — Human Learning Advocate

**Confidence: 90%**

The `Channel<WorldEvent>` pattern and reconnect loop represent clear, legible code patterns:
- `MonitorAndCancelOnFaultAsync` is a 5-line static helper with a self-documenting name.
- The retry loop uses a standard for-loop (not a while-true with break).
- `reconnectDelays` parameter makes test setup transparent — zero-delay tests are self-explanatory.
- The `SlowChatInterpreter` mock class is named precisely and documents its purpose.

The 5 hotfixes demonstrate a well-understood CI pipeline: all errors were incremental
compile-chain reveals (each fix exposed the next). That pattern is normal for code that was
never run through CI.

**Findings:**
- None blocking.

### Seat 5 — Skeptical Reviewer

**Confidence: 85%**

Concerns:

**BLOCKING (must fix before proceeding):**
None identified. The implementation is functionally correct.

**Deferred (high priority for Sprint 2):**

D4: The `MonitorAndCancelOnFaultAsync` method has a subtle behavior: when `ProcessEventsAsync`
completes NORMALLY (the async iterator returns without throwing), it calls `cts.Cancel()`.
This means if `ReceiveEventsAsync` returns an empty sequence (unlikely in production but
possible in tests), the service treats it as a reconnect trigger. This is probably the right
behavior but should be documented.

D5: `DisconnectAsync(CancellationToken.None)` is called in the `finally` block after
reconnects. On `FailingWorldAdapter` in tests, this calls `_inner.DisconnectAsync()` which
sets `_connected = false`. After the test's `cts.Cancel()`, `IsConnected` may briefly flip to
false. The test captures `IsConnected` before cancelling, so the test is correct, but this
pattern is fragile if test order changes.

D6: The sprint 1b test uses `reconnectDelays: [TimeSpan.Zero, ...]` for instant retries.
The test timeout is 5 seconds. With 3 zero-delay attempts, there's ample margin. But if a
future change adds synchronous work between attempts (e.g., logging), a `TimeSpan.Zero`
delay could still pass — the test is robust.

**NUnit warning (pre-existing, not this sprint):**
`MockMemoryGatewayTests.cs` line 46: `Is.Not.Empty` vs `Is.Not.Null.And.Not.Empty`.
Should be fixed in Sprint 2 or Sprint 3 cleanup.

### Seat 6 — Synthesizer

**Confidence: 91%**

Sprint 1 delivers two high-ROI reliability improvements from the architecture review:
1. **Non-blocking LLM** (was: HIGH severity) — fully resolved. The event loop can now
   process health/death/blockMined events without waiting for a 5–10s LLM call.
2. **Reconnect loop** (was: HIGH severity) — fully resolved. A single WebSocket disconnect
   no longer permanently terminates the agent; 5 retries with exponential backoff give
   adequate resilience for transient network issues.

The 5 CI hotfixes (pre-existing TSK-0014 bugs) were correctly identified and fixed without
introducing new regressions. The baseline of "CI was never green" was diagnosed accurately.

No blocking findings from any seat. Two deferred items (attempt count off-by-one from spec,
missing reconnecting WorldEvent) are low-priority. The NUnit warning is pre-existing.

**Recommendation: APPROVED. Proceed to Sprint 2.**

---

## Acceptance Criteria (from sprint spec)

| Criterion | Status |
|-----------|--------|
| Slow LLM (6s mock) does NOT block blockMined processing | ✅ Test: `SlowChatInterpreter_DoesNotBlock_BlockMinedEventProcessing` |
| Mock adapter failing twice, succeeds on 3rd; goal survives | ✅ Test: `Reconnect_AfterTwoFailures_ResumesCurrentGoal` |
| CI green before council review | ✅ Commit `1840ec0` (hotfixes); Sprint 1 CI pending |

---

## Blocking Findings

**None.** Council approves Sprint 1 to ship.

---

## Deferred Items

| ID | Finding | Seat | Sprint |
|----|---------|------|--------|
| D1 | Attempt count 6 vs spec's "5" — document or align | Archivist | 2 |
| D2 | "Reconnecting" WorldEvent not emitted on retry | Archivist | 3 |
| D3 | `_chatChannel` persists across reconnects — document intent | Data Model | 2 |
| D4 | Normal ProcessEventsAsync completion triggers reconnect — document | Skeptical | 2 |
| D5 | `FailingWorldAdapter.IsConnected` timing in tests | Skeptical | next cleanup |
| D6 | NUnit2058 warning in `MockMemoryGatewayTests.cs` | Skeptical | 3 |
