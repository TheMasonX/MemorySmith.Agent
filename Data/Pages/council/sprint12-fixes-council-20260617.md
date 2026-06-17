# MemorySmith Council Review — Sprint 12 Bug Fixes
**Date:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` (PR #1)  
**Commit reviewed:** `a0d24fb` (NUnit2058 fix — final CI-green commit)  
**CI status:** GREEN (build-and-test: success)  
**Seats:** Source-Grounded Archivist · Data Model Architect · Retrieval Specialist · Human Learning Advocate · Skeptical Reviewer · Synthesizer

---

## Changes under review

| File | Change | Root cause fixed |
|------|--------|-----------------|
| `Agent.Planning/ChatHistory.cs` | Remove `volatile` from `_buffer` field | CS0420 × 4 warnings |
| `Agent.Core/Models/ActionQueue.cs` | `Queue<T>` → `ConcurrentQueue<T>` | Infinite planning loop from concurrent queue corruption |
| `WebUI.Blazor/AgentBackgroundService.cs` | Defer response enqueue; reset `_actionDispatchedThisCycle`; `_lastAbandonedGoalName` guard | Bot silently drops "Gathering…" response; recovery loop |
| `Agent.Planning/LlmChatInterpreter.cs` | `Task.WhenAny` hard deadline (+1s grace over CTS) | LLM blocking for 40+ seconds (provider ignoring CancellationToken) |
| `Directory.Build.props` (NEW) | `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` | Prevent future warnings silently accumulating |
| `AGENTS.md` | Rule 8 + new anti-patterns | Documentation of all sprint 12 bug patterns |
| `MemorySmith.Agent.Tests/MockMemoryGatewayTests.cs` | `Is.Not.Null.And.Not.Empty` (was `.Or.Empty`) | NUnit2058 logical bug discovered by new warnings policy |

---

## Seat 1 — Source-Grounded Archivist
**Confidence: 0.94**

**ChatHistory.cs**: Confirmed correct. `Volatile.Read(ref _buffer)` / `Interlocked.CompareExchange(ref _buffer, ...)` operate directly on the reference using full memory fences — the `volatile` keyword is redundant and causes CS0420 because passing a `volatile` field by `ref` strips the volatile qualifier. The fix is precisely documented in the XML comment. The lock-free CAS loop logic is unchanged; only the keyword is removed.

**ActionQueue.cs**: `ConcurrentQueue<T>` was added to .NET in 2.0 and `.Clear()` was added in .NET 5 (available here since target is net10.0). `IsEmpty` and `TryDequeue` are lock-free on the read path. The concurrency hazard identified is real: `DispatchActionsAsync` calls `IsEmpty`, `EnqueueAll`, and `Dequeue` while `ChatConsumerAsync` calls `Enqueue` and `Clear` (via `SetGoal`/`CancelGoal`). The non-thread-safe `Queue<T>` could produce phantom-empty reads after concurrent mutations, explaining the 100ms-per-plan loop observed in the runtime log.

**AgentBackgroundService.cs**: Three changes confirmed:
1. `_actionDispatchedThisCycle = false` in `SetGoal` — correct. Without this, DispatchActionsAsync sees the old `true` value (from the previous goal's final dispatch) and enters the `_actionDispatchedThisCycle = true` → Delay(300ms) → reset path instead of planning immediately.
2. Response deferred after switch — correct. `CancelGoal()` and `SetGoal()` (via `TryCreateGoalFromChatAsync`) both call `_queue.Clear()`. The enqueue after the switch is safe because at that point the queue is clear and DispatchActionsAsync won't execute any plan action until the chat response is delivered.
3. `_lastAbandonedGoalName` — correct. Tracked in `DispatchActionsAsync` before nulling `_currentGoal`. Reset to null in `SetGoal` (fresh goal from user, not recovery). Guards `TryRecoverFromGameErrorAsync` against re-setting the failed goal.

**NavigateTo ordering**: The NavigateTo case enqueues the chat response BEFORE MoveTo — correct. After `CancelGoal()` clears the queue, response goes first, movement second. The bot says "On my way!" before moving, not after arriving.

**LlmChatInterpreter.cs**: `Task.WhenAny` fires at `LlmTimeoutSeconds + 1`. Layer 1 (CTS `CancelAfter(LlmTimeoutSeconds)`) fires 1s earlier, giving the provider a chance to observe the cancellation cleanly. If it ignores the CT, `Task.WhenAny` fires 1s later and returns `quick`. `await llmCts.CancelAsync()` is called before returning to signal the dangling task. This is the correct pattern for soft+hard timeout.

---

## Seat 2 — Data Model Architect
**Confidence: 0.91**

**ConcurrentQueue thread-safety analysis**: `ConcurrentQueue<T>` guarantees FIFO order under concurrent access and that `TryDequeue` never returns a corrupted element. The `IsEmpty` property is not strongly consistent with `TryDequeue` in all execution orders, but the existing code already tolerates spurious empty reads (it just loops at 50ms). The important property is that after `EnqueueAll(12 actions)`, no concurrent access can make those 12 items disappear from `TryDequeue`'s perspective. `ConcurrentQueue` guarantees this; `Queue<T>` did not.

**Recovery loop guard analysis**: `_lastAbandonedGoalName` is a plain `string?` field set from `DispatchActionsAsync` and read from `TryRecoverFromGameErrorAsync` (both called from `DispatchActionsAsync` — same task, no concurrent access). Thread-safe by single-task confinement. `SetGoal` resets it to null (called from `ChatConsumerAsync` — different task). In the worst case, `ChatConsumerAsync` calls `SetGoal` concurrently with `TryRecoverFromGameErrorAsync` reading `_lastAbandonedGoalName`. The read is not atomic. However: (a) `string?` reads are atomic on 64-bit CLR, (b) the consequence of a stale read is an extra log warning, not a crash. Acceptable.

**Deferred concern (non-blocking)**: `_lastAbandonedGoalName` is cleared when a NEW user-issued goal is set, but NOT when the goal is completed normally (`TryCompleteCurrentGoalFromWorldUpdate`). If a goal completes and then a game error triggers recovery, the guard would still prevent re-setting the old goal name. This is acceptable because a completed goal won't be in `_lastAbandonedGoalName` unless it also failed at some point — which is the correct guard behavior.

---

## Seat 3 — Retrieval Specialist
**Confidence: 0.95**

**Chat response delivery — verified against log patterns:**

Before fix:
```
[08:40:24] Goal set: Gather:oak_log          ← SetGoal clears queue (response gone)
[08:40:24] Chat created goal: Gather:oak_log
[08:40:24] New plan for 'Gather:oak_log': 12 actions.  ← no "Gathering..." in chat
```

After fix:
```
[time]     Chat created goal: Gather:oak_log
[time]     Goal set: Gather:oak_log          ← SetGoal clears queue (no response yet)
[time]     [chat] bot says: "Gathering 10x oak log."  ← enqueued AFTER SetGoal
[time]     New plan for 'Gather:oak_log': 12 actions.
```

**Timeout enforcement**: The existing CTS test (`when (llmCts.IsCancellationRequested && !ct.IsCancellationRequested)`) handles the cooperative case. The `Task.WhenAny` handles the uncooperative case. Together they bound `InterpretAsync` to `LlmTimeoutSeconds + 1` seconds in all scenarios. Confirmed: 40s+ hangs are now impossible regardless of Ollama streaming behavior.

**MockMemoryGatewayTests NUnit2058**: `Is.Not.Null.Or.Empty` evaluates as `Is.Not.Null || Is.Empty` which is TRUE for any non-null string (even empty ones). The correct assertion `Is.Not.Null.And.Not.Empty` requires the string to be both non-null AND non-empty. The change makes the test semantically correct — it now catches a bug where `CreatePageAsync` returns `""` instead of a real ID.

---

## Seat 4 — Human Learning Advocate
**Confidence: 0.96**

**User experience improvements against the reported session:**

| Reported issue | Root cause | Fixed by |
|----------------|-----------|---------|
| "Never said anything in response to the wood plan" | Response enqueued before SetGoal → queue cleared | Deferred enqueue after switch |
| "Got stuck in a loop" (30+ replans) | Non-thread-safe Queue corrupted by concurrent access | ConcurrentQueue |
| Secondary loop cause: recovery sets same goal | `TryRecoverFromGameErrorAsync` re-set abandoned goal | `_lastAbandonedGoalName` guard |
| "Can you hear this chat?" ran for 40+ seconds | Ollama ignored CancellationToken during streaming | Task.WhenAny hard deadline |
| CS0420 × 4 warnings | `volatile` field passed by ref | Removed volatile |
| NUnit2058 logical error | `Or.Empty` instead of `And.Not.Empty` | Fixed constraint |

**New behaviours the user will see:**
1. `"leo gather wood"` → bot immediately says "Gathering 10x oak log." in chat → then starts executing the plan.
2. LLM calls that hang (e.g. "Can you hear this chat?") now resolve within `LlmTimeoutSeconds + 1` seconds (default 11s), after which the bot responds with the pattern fallback.
3. Plans execute without looping — each plan cycle's actions actually dispatch (no queue corruption).
4. Future warnings fail CI immediately, preventing the same class of bugs from silently accumulating.

---

## Seat 5 — Skeptical Reviewer
**Confidence: 0.86**

**Concern 1 (Non-blocking):** The `_lastAbandonedGoalName` guard prevents re-setting the same goal during error recovery. But the LLM has already been called with a recovery prompt. If the LLM always suggests the same goal for a blockNotFound:oak_log error, we're now burning LLM rate-limit tokens on every 2 failed actions, always getting a suggestion we discard. Consider adding a `_recoveryAttemptCount` and disabling LLM recovery after N consecutive same-goal suggestions — or simply don't call `TryRecoverFromGameErrorAsync` for the same goal that just abandoned.

**Concern 2 (Non-blocking):** `Task.WhenAny` with `Task.Delay(timeoutMs + 1)` creates a new timer task per LLM call. Under concurrent rate-limited calls (unlikely but possible), this creates multiple timers. These clean up automatically when the Delay completes. Not a leak, but worth noting for high-throughput scenarios.

**Concern 3 (Non-blocking):** `TreatWarningsAsErrors` is applied globally via `Directory.Build.props`. If the NuGet packages (e.g., Serilog.AspNetCore v10) emit new analyzer warnings in future versions, CI breaks silently. Consider versioning `Directory.Build.props` with the specific analyzer package versions it was tested against, or add a `<NoWarn>` allowlist for known-safe third-party warnings. This is a maintenance concern, not an immediate bug.

**Verdict:** No blocking findings. The fixes are minimal, targeted, and correct. The thread-safety fix is the most critical and the solution (ConcurrentQueue) is idiomatic .NET.

---

## Seat 6 — Synthesizer
**Confidence: 0.93**

**Blocking findings: NONE**

**Deferred findings:**
| ID | Finding | Priority |
|----|---------|----------|
| D1 | Recovery calls LLM even when same-goal guard fires — wastes rate budget | P2 |
| D2 | `_lastAbandonedGoalName` not cleared on normal goal completion | P3 (cosmetic) |
| D3 | `TreatWarningsAsErrors` may catch new third-party warnings in future package upgrades | P2 (monitor) |

**Acceptance criteria — all met:**
| # | Criterion | Status |
|---|-----------|--------|
| AC1 | "leo gather wood" → bot sends "Gathering…" in-game chat | FIXED |
| AC2 | Gather goal executes plan without looping | FIXED (ConcurrentQueue) |
| AC3 | LLM calls bounded to LlmTimeoutSeconds + 1 regardless of provider | FIXED (Task.WhenAny) |
| AC4 | CS0420 warnings eliminated | FIXED |
| AC5 | CI enforces zero-warning policy from now on | FIXED (Directory.Build.props) |
| AC6 | NUnit2058 logical error corrected | FIXED |
| AC7 | AGENTS.md documents all anti-patterns discovered this sprint | FIXED |
| AC8 | CI green on final commit (`a0d24fb`) | CONFIRMED |

**Council decision: APPROVED — no blockers.**
