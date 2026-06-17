# Agent Handoff — MemorySmith.Agent Sprint 9
**Created:** 2026-06-17
**Branch:** `sprint-5-tool-safety` (PR #1 against `main` — extended dev branch, merge deferred)
**CI status:** ⏳ Queued (Sprint 8 commits pushed)
**Task tracker:** `Data/Pages/Tasks/phase6-tasks.md`

---

## Project summary

MemorySmith.Agent is a modular autonomous Minecraft bot (bot name: **Leo**) backed by an LLM
(Ollama/llama3.2:3b by default). Long-term memory via MemorySmith wiki REST API.
Two runtimes: .NET 10 C# host (10 projects) and a Node.js Mineflayer adapter over WebSocket.

**Solution:** `MemorySmith.Agent.slnx` (VS 2022 .slnx format)
**Run:** `dotnet run --project WebUI.Blazor --launch-profile WebUI.Blazor`
**Test:** `dotnet test MemorySmith.Agent.Tests` (target: all green, ~0 failures)

---

## Workflow (every sprint)

```
implement → local build/test → push (github__create_or_update_file) →
council review (6-seat, Data/Pages/council/) → fix blockers → confirm CI green
```

**Push rule:** Always use `paramsFile` with **plain text** `content` (not base64).
The MCP tool base64-encodes internally — pre-encoding causes double-encoding corruption.

**CI check:** `curl -s "https://api.github.com/repos/TheMasonX/MemorySmith.Agent/commits/<sha>/check-runs"`
**Failures:** `curl -s "https://api.github.com/repos/TheMasonX/MemorySmith.Agent/check-runs/<id>/annotations"`

---

## What was completed in Sprint 8

### D4 — WorldModel.Reconcile full-method lock
**File:** `Agent.Core/Models/WorldModel.cs`

The `Reconcile` method previously used a `ConcurrentQueue<double>` for the running deviation
scores, but only the final `_cachedUncertainty` write was inside `lock (_lock)`. This meant
the enqueue+trim+average sequence was not atomic: two concurrent calls could both see
`Count > MaxDeviationSamples` and over-trim, or one could read the average while the other
was mid-trim.

Fix: Switched `ConcurrentQueue<double>` → `Queue<double>` (no longer needed since we lock
everywhere), and moved the entire enqueue+trim+`_cachedUncertainty` update inside `lock (_lock)`.
The deviation score computation (pure arithmetic on passed-in arguments) stays outside the lock.

### D7 — DecomposerRegistry verification
**File:** `Agent.Planning/Decomposition/DecomposerRegistry.cs`

Verified: all three operations (`Register`, `Find`, `All`) already use `lock (_decomposers)`.
The plain `List<IGoalDecomposer>` is correctly protected. No code change needed.

### S7-D1 — Uncertainty in /api/agent/status
**File:** `WebUI.Blazor/Program.cs`

Added `IWorldModel? worldModel` parameter to the `/api/agent/status` endpoint.
Added `Uncertainty = worldModel?.Uncertainty ?? 0.0` to the response object.

### S7-D2 — Typed JournalEntryDto for journal API
**Files:** `WebUI.Blazor/Dtos.cs`, `WebUI.Blazor/Program.cs`

Added `JournalEntryDto(string Timestamp, string Type, string Summary,
IReadOnlyDictionary<string, string?> Details)` record to `Dtos.cs`.
Updated `/api/agent/journal` endpoint to project `JournalEntry` → `JournalEntryDto`,
coercing all `object?` Detail values to `string?` to ensure predictable JSON serialisation.

### S7-Chat — Explicit ChatIntentType.Chat case
**File:** `WebUI.Blazor/AgentBackgroundService.cs`

Added `case ChatIntentType.Chat: case ChatIntentType.Unknown: break;` to the
`HandleChatEventAsync` switch. The chat response was already enqueued above the switch;
the explicit case documents the intent clearly and prevents future accidental fall-through.

### S7-D4 — Empty-response guard in LlmChatInterpreter
**File:** `Agent.Planning/LlmChatInterpreter.cs`

Added guard step 8 in `InterpretAsync`: if `llmResult` is a non-`NotAddressed` intent but
has an empty `Response` string, substitute the pattern-matcher's `quick.Response` (which is
always non-empty for `Unknown` — "Didn't catch that. Say 'help' for commands."). This
prevents the "Hmm..." thinking indicator from firing and then going silent when the LLM omits
the response field.

### P3-a — ErrorRecovery journal entry type
**File:** `Agent.Core/Models/JournalEntry.cs`

Added `ErrorRecovery` to `JournalEntryType` enum with XML doc. Logged in
`TryRecoverFromGameErrorAsync` before the LLM call so recovery attempts are visible in the
journal even if the interpreter call throws.

### P3-b/c — Richer recovery prompt + immediate trigger
**File:** `WebUI.Blazor/AgentBackgroundService.cs`

`TryRecoverFromGameErrorAsync` now:
- Logs `ErrorRecovery` journal entry before calling the interpreter.
- Includes top-5 inventory items in the recovery prompt so the LLM can make
  inventory-aware suggestions ("you have planks, try crafting table instead").
- Lists available actions in the prompt.
- Triggers immediately (without waiting for `_consecutiveFailures >= 2`) when
  the error is `blockNotFound:*` or contains "recipe"+"missing" — these are
  unrecoverable without a goal change and should be short-circuited.

---

## Sprint 8 audit notes (from external code review)

The attached `memorysmith_agent_code_audit_report.md` (submitted at start of sprint) was
synthesised into `phase6-tasks.md` as Sprints 9 and 10. Key themes:

- **D7 / DecomposerRegistry** — already correctly locked, confirmed.
- **Flat-area scanner gaps (A1–A5)** → Sprint 9 (index.js + C# consumer).
- **Build robustness (B1–B5)** → Sprint 10 (preflight, checkpoint/resume, orientation, chains).
- **Chat prompt safety** — audit sec 5: role-separated prompt, smaller context → deferred
  (current prompt is safe enough for local/dev use; revisit before any network exposure).
- **Security** — audit sec 7: control endpoints unauthenticated → deferred behind a
  `TODO(auth)` comment; acceptable for localhost-only development.
- **SFT/distillation** — audit sec 6: dataset design and Unsloth approach noted in
  `phase6-tasks.md` future section; not scheduled until the decision pipeline is stable.

---

## What remains (Sprint 9 backlog)

### P0 — Flat-area scanner (A1–A5)

| ID | Task | File |
|----|------|------|
| A1 | Widen vertical scan window (currently ±5 → ±10 blocks) | `MineflayerAdapter/index.js` |
| A2 | Add compactness scoring — prefer contiguous squares, not thin strips | `MineflayerAdapter/index.js` |
| A3 | Wire `FlatAreaFoundEvent` result → auto-set build origin in planner | `Agent.Planning/HtnTaskLibrary.cs` |
| A4 | Unit tests: ParseEvent FlatAreaFoundEvent round-trip + flood-fill edge cases | `MemorySmith.Agent.Tests/` |
| A5 | Slope/roughness penalty (large height variance → reject) | `MineflayerAdapter/index.js` |

### P1 — Sprint 7 deferred carry-forward

| ID | Task | File |
|----|------|-------|
| D3 (S7) | WorldModel endpoint: add `?detail=false` for summary-only mode | `WebUI.Blazor/Program.cs` |
| D1 (S1) | Reconnect attempt count 6 vs spec "5" | `AgentBackgroundService.cs` |
| D2 (S1) | Emit "Reconnecting" WorldEvent during backoff | `AgentBackgroundService.cs` |
| D2 (S2) | Parallel cache miss race in `MemorySmithItemRegistry` | `Agent.Memory/` |
| D3 (S2) | `ToDictionary` throws on duplicate blueprint materials | `HtnTaskLibrary.cs` |

### P2 — Build robustness (Sprint 10)

Preflight validation, checkpoint/resume semantics, orientation-aware placement,
resource chain expansion (sticks, stone tools, ingots), clear-area action.
See `phase6-tasks.md` Sprint 10 table for details.

---

## Key architecture notes

**Chat addressing pipeline (per message):**
1. `ProcessEventsAsync` receives `ChatEvent` → writes to `_chatChannel`
2. `ChatConsumerAsync` (single reader) calls `HandleChatEventAsync`
3. `LlmChatInterpreter.InterpretAsync`:
   - Fast-path (no LLM): CreateGoal, CancelGoal, QueryHelp, QueryStatus, NavigateTo
   - LLM path: all other addressed messages
   - Empty-response guard (Sprint 8): substitutes pattern fallback if LLM omits response
4. Response queued as `Chat` tool action → dispatched by `DispatchActionsAsync`

**WorldModel uncertainty:** Running average of last 20 Reconcile deviation scores.
Now fully atomic (Sprint 8 D4). Exposed at `/api/agent/status` (Sprint 8 S7-D1).

**Recovery flow:** `TryRecoverFromGameErrorAsync` → `ErrorRecovery` journal entry →
LLM prompt with inventory context → if `CreateGoal` intent returned, `TryCreateGoalFromChatAsync`.
Triggers immediately for `blockNotFound`/`recipeMissing` (Sprint 8 P3-c).

**DecomposerRegistry:** Already correctly lock-guarded. No further changes needed.

---

## File reference (Sprint 8 changes)

| Path | Change |
|------|--------|
| `Agent.Core/Models/WorldModel.cs` | ConcurrentQueue→Queue; Reconcile fully atomic under _lock |
| `Agent.Core/Models/JournalEntry.cs` | Added ErrorRecovery enum value |
| `WebUI.Blazor/Dtos.cs` | Added JournalEntryDto record |
| `WebUI.Blazor/Program.cs` | /api/agent/status + uncertainty; /api/agent/journal uses JournalEntryDto |
| `Agent.Planning/LlmChatInterpreter.cs` | Step 8: empty-response guard |
| `WebUI.Blazor/AgentBackgroundService.cs` | Chat/Unknown switch cases; richer recovery prompt; immediate trigger |
| `Data/Pages/Tasks/phase6-tasks.md` | Sprint 8 complete; Sprint 9/10 backlogs; all deferred items |
| `Data/Pages/Tasks/agent-handoff-20260617k.md` | This file |

---

## How to continue

1. `git fetch origin sprint-5-tool-safety && git checkout sprint-5-tool-safety`
2. `dotnet build MemorySmith.Agent.slnx` — should be clean
3. `dotnet test MemorySmith.Agent.Tests` — all green, ~0 failures
4. Pick the next task from `phase6-tasks.md` Sprint 9 backlog (start with A1/A2 in index.js)
5. Implement → push → wait for CI → council review → fix blockers → next task

**Sprint 9 recommended starting point:** A1+A2 together (both in `MineflayerAdapter/index.js`,
the `findFlatArea` handler). The vertical window and compactness scoring are in the same function.
Do A1+A2 in one commit, then A5 (slope penalty) in the next, then A3 (C# consumer) + A4 (tests).
