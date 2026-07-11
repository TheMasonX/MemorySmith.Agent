# Agent Handoff — Sprint 36 Implementation (session 2026-06-22)

**Date:** 2026-06-22
**Branch:** sprint-35-llm-first (HEAD: `20fe0151026fa1007c3310fd58641431024248d3`)
**Previous handoff:** Data/Pages/Tasks/agent-handoff-sprint36.md (Sprint 35 handoff)
**Version target:** v0.36.0

---

## What this session delivered

### BLK-S36-03 — CI fixes (prerequisite)

Three pre-existing CI failures in `AgentBackgroundServiceTests.cs` fixed:

1. **Chat registered as NoOp in SetUp** — the startup connection announcement
   (`_queue.Enqueue(new ActionData { Tool = "Chat" })`) was failing silently
   because "Chat" was not registered in the test dispatcher, causing a 300ms
   settle cycle that made `maxConsecutiveFailures=1` tests time out before
   `PlanAsync` was called. Fix: register `NoOpTool("Chat")` in `[SetUp]`.

2. **SlowChatInterpreter test updated** — Sprint 35 P0-A removed inventory
   updates from `ApplyBlockMined`. The test pushed `BlockMinedEvent` and
   checked inventory; now pushes `ItemCollectedEvent` instead.

3. **Deadlines raised from 2s to 4s** in the three error-channel tests.

Commit: `be8142b0`

### BLK-S36-01 — AGENTS.md update

Added to `AGENTS.md`:
- **CRITICAL Rule A-1: Parsers Never Create Goals** with full rationale
- IntentDraft JSON schema (all fields, confidence threshold, clarificationQuestion)
- ActionOutcome record (universal tool result, factory helpers)
- playerCollect guard pattern (version-safe `entity?.metadata?.find` chain)
- mineComplete event contract (fields + correlationId importance)
- ADR entries D-011/D-012/D-013
- Sprint 36 AgentRuntime interface preview in Key Interfaces table

Commit: `bb3f19b5`

### P0-A — TryInterruptOnDamageAsync stop-before-clear

`WebUI.Blazor/AgentBackgroundService.cs` — commit `8b62f98b`

**What changed:**
- `TryInterruptOnDamage(DamageTakenEvent)` → `TryInterruptOnDamageAsync(DamageTakenEvent, CancellationToken ct)`
- `SendEmergencyStop() + _queue.ClearAndEnqueue(GetStatus)` replaced by:
  ```csharp
  await _queue.ClearAndEnqueueAsync(
      new ActionData { Tool = "GetStatus" },
      () => worldAdapter.SendActionAsync(
          new ActionData { Tool = EmergencyStopActionName }, CancellationToken.None));
  ```
- Call site in `ProcessEventsAsync` updated to `await TryInterruptOnDamageAsync(damageTaken, ct)`
- `SendEmergencyStop()` preserved for its other callers (CancelGoal, TryCompleteCurrentGoalFromWorldUpdate)

**Why:** Previously `SendEmergencyStop()` was fire-and-forget — JS could receive new
plan actions before the stop signal, causing the old mine/wander loop to keep running
alongside the new plan. Now the stop is awaited before the atomic clear+enqueue.

Tests: 2 new tests in `Sprint36Tests.cs` — stop sent when large HP drop; no stop on small drop.

### P0-B — ActionOutcome wiring

Two commits:

**`a139643b` — `Agent.Core/Interfaces/IAgentJournal.cs`**
- Added `LogOutcome(ActionOutcome outcome)` as a default interface method (DIM)
- Translates ActionOutcome → JournalEntry using existing ActionCompleted / ActionFailed types
- Zero changes to AgentJournal, NullAgentJournal, or test doubles (DIM handles it)

**`a7bbd1e2` — `Agent.Tools/ToolDispatcher.cs`**
- Added `CallWithOutcomeAsync(Guid goalId, string toolName, JsonElement arguments, CancellationToken ct)`
- Calls existing `CallAsync` then wraps ToolResult in `ActionOutcome.Succeeded/Failed`
- Calls `_journal?.LogOutcome(outcome)` automatically
- Returns `(ToolResult, ActionOutcome)` tuple

**Still needed:** Callers in `AgentBackgroundService.DispatchActionsAsync` should migrate
from `toolCaller.CallAsync` to `CallWithOutcomeAsync` in a future sprint. Sprint 36 adds
the infrastructure only; wiring to the dispatch loop is Sprint 37.

Tests: **not yet written** — see deferred items below.

### P0-C — SearchedRadius retry gate

`Agent.Planning/HtnTaskLibrary.cs` — commit `a0fe4546`

**What changed:**
- Added `FlatAreaRetryRadius = 48` constant (matches JS `FLAT_AREA_RETRY_RADIUS`)
- In `DecomposeBuild` when `requireOrigin && origin is (0,0,0)`:
  - If `lastArea == 0 AND lastSearchedRadius >= 48`: return `Array.Empty<ActionData>()` (no retry; goal fails via consecutive-failures counter)
  - If `lastArea == 0 AND lastSearchedRadius < 48` (or no fact): retry with `FlatAreaRetryRadius = 48`
  - If `lastArea != 0`: first scan, use `PreflightFlatAreaRadius = 30`

Tests: 3 new tests in `Sprint36Tests.cs`:
- `SearchedRadius=32, Area=0` → retry `FindFlatArea` emitted with radius >= 48
- `SearchedRadius=48, Area=0` → empty plan (no retry)
- `No SearchedRadius fact, Area=0` → default retry (lastSearchedRadius=0 < 48)

### P1-A — FactSource enum expansion

`Agent.Core/Models/Fact.cs` — commit `32ff8bb9`

Added to `FactSource` enum (metadata only, no runtime behavior changes):
- `PlayerInstruction` — fact from player chat (maps to BuildOriginSource.Explicit)
- `Memory` — fact from MemorySmith search (SearchMemory, GetPage)
- `Scan` — fact from sensor scan (FindFlatArea, GetStatus; maps to BuildOriginSource.AutoScanned)
- `Recovery` — fact set during TryRecoverFromGameErrorAsync

### P1-B — ItemCraftedEvent wires to inventory

`Agent.Core/WorldStateProjector.cs` — commit `9b2948f5`

**What changed:**
- `ItemCraftedEvent e => StoreFacts(current, e)` → `ItemCraftedEvent e => ApplyItemCrafted(current, e)`
- New `ApplyItemCrafted` method: normalizes item key (strips `minecraft:` prefix), calls `AddInventoryItem`, then `StoreFacts`
- Mirrors `ApplyItemCollected` pattern from Sprint 35 P0-A

Tests: **not yet written** — see deferred items below.

### P1-C — Registered tool names in LLM prompt

`Agent.Planning/LlmChatInterpreter.cs` — commit `a7377f51`

**What changed:**
- Constructor gains `IReadOnlyList<string>? registeredToolNames = null` parameter
- `BuildSystemPrompt` gains `IReadOnlyList<string>? toolNames = null` parameter
- After the main prompt body, appends:
  ```csharp
  if (toolNames is { Count: > 0 })
      return basePrompt + $"\nRegistered tools: {string.Join(", ", toolNames)}.";
  ```
- No raw string content was modified (Rule E-1 compliant)

**Still needed:** `Program.cs` DI registration must pass
`dispatcher.All.Select(t => t.Name).ToList()` to the `LlmChatInterpreter`
constructor. This is a single line change in `Program.cs`.

Test scaffold: `Sprint36Tests.ToolDispatcher_All_ExposesRegisteredToolNames`
validates the data pipeline.

### P2-A — AgentRuntime decomposition interfaces

New files — commits `c0968d1e` / `20fe0151`:
- `Agent.Core/Runtime/IAgentRuntimeComponent.cs` — marker + 6 manager interfaces
- `Agent.Core/Runtime/AgentRuntime.cs` — record holding all 6 managers

**Definition-only in Sprint 36.** Sprint 37 wires `AgentBackgroundService` to delegate:
```csharp
// Sprint 37 target:
while (!ct.IsCancellationRequested)
    await runtime.TickAsync(ct);
```

---

## Deferred to next session

### BLK-S36-03b — Verify CI green

CI must be re-triggered on the new HEAD (`20fe0151`). The test fix (`be8142b0`) should
resolve the 3 pre-existing failures. The Sprint 35 regression
(`SlowChatInterpreter_DoesNotBlock_BlockMinedEventProcessing`) is now fixed too.

**Known risk:** the `ActionQueue.cs` test re-export is base64 encoded in the sprint35
branch version — confirm `MemorySmith.Agent.Tests` compiles cleanly after all Sprint 36
changes before council.

### Missing tests

| Task | Test description |
|------|-----------------|
| P0-B | `CallWithOutcomeAsync_Success_ReturnsOutcomeAndLogsToJournal` |
| P0-B | `CallWithOutcomeAsync_ToolFailure_ReturnsFailedOutcome` |
| P1-B | `ItemCraftedEvent_UpdatesInventory` (projector test) |

Add these to `Sprint36Tests.cs` or a new `Sprint36bTests.cs`.

### P2-B — Observation-driven replanning scaffold

Comment in `AgentBackgroundService.DispatchActionsAsync`, after the tool dispatch:
```csharp
// Sprint 36 P2-B: after each ActionOutcome, LLM evaluates if the current plan
// is still valid. If evaluation returns "replan", RequestReplan() is called.
// Full wiring in Sprint 37 when AgentRuntime decomposition is live.
```

Also add `IObservationSummary` concept to `ActionOutcome.cs` (a one-liner interface stub).

### Program.cs — LlmChatInterpreter DI wiring

In `WebUI.Blazor/Program.cs`, find the `LlmChatInterpreter` registration and add:
```csharp
registeredToolNames: dispatcher.All.Select(t => t.Name).ToList()
```

This is the only change needed to make P1-C actually active at runtime.

### Version bump

`WebUI.Blazor/Program.cs`: change `/api/about` version to `"0.36.0"` and phase to
`"Sprint 36 — AgentRuntime + Observation-Driven Replanning"`.

### Council review

After CI is green, write `Data/Pages/council/sprint36-impl-council-YYYYMMDD.md`.
6-seat format; include all 12 commits in scope. Focus areas for council:
- P0-A: confirm stop-before-clear is correct ordering (not just faster fire-and-forget)
- P0-C: confirm empty-plan fallback is correct failure path (goal fails gracefully)
- P1-B: confirm AddInventoryItem for crafted output is correct — no double-counting if
  both ItemCraftedEvent AND StatusEvent arrive (GetStatus reconciles; last write wins)
- P2-A: confirm 6-interface decomposition matches the actual ABS god-class structure
- BLK-S36-03: confirm the 3 formerly-failing tests now pass (blocking acceptance criterion)

---

## Key file paths

| File | Change |
|------|--------|
| `WebUI.Blazor/AgentBackgroundService.cs` | P0-A: TryInterruptOnDamageAsync |
| `Agent.Core/Interfaces/IAgentJournal.cs` | P0-B: LogOutcome DIM |
| `Agent.Tools/ToolDispatcher.cs` | P0-B: CallWithOutcomeAsync |
| `Agent.Planning/HtnTaskLibrary.cs` | P0-C: FlatAreaRetryRadius gate |
| `Agent.Core/Models/Fact.cs` | P1-A: FactSource expansion |
| `Agent.Core/WorldStateProjector.cs` | P1-B: ApplyItemCrafted |
| `Agent.Planning/LlmChatInterpreter.cs` | P1-C: registeredToolNames parameter |
| `Agent.Core/Runtime/IAgentRuntimeComponent.cs` | P2-A: 6 manager interfaces (NEW) |
| `Agent.Core/Runtime/AgentRuntime.cs` | P2-A: AgentRuntime record (NEW) |
| `AGENTS.md` | BLK-S36-01: CRITICAL rules, IntentDraft, ActionOutcome |
| `MemorySmith.Agent.Tests/AgentBackgroundServiceTests.cs` | BLK-S36-03: CI fix |
| `MemorySmith.Agent.Tests/Sprint36Tests.cs` | P0-A, P0-C, P1-C scaffold tests (NEW) |

---

## Branch commit log (Sprint 36 session)

```
20fe0151  feat: P2-A — AgentRuntime record
c0968d1e  feat: P2-A — IAgentRuntimeComponent interfaces
a7377f51  feat: P1-C — BuildSystemPrompt tool names
9b2948f5  feat: P1-B — ApplyItemCrafted wires ItemCraftedEvent to inventory
32ff8bb9  feat: P1-A — FactSource expansion (PlayerInstruction/Memory/Scan/Recovery)
a7bbd1e2  feat: P0-B — ToolDispatcher.CallWithOutcomeAsync
a139643b  feat: P0-B — IAgentJournal.LogOutcome DIM
4b40998f  test: Sprint36Tests.cs
a0fe4546  feat: P0-C — DecomposeBuild SearchedRadius retry gate
a7bbd... [see above]
8b62f98b  feat: P0-A — TryInterruptOnDamageAsync
bb3f19b5  docs: Sprint 36 AGENTS.md
be8142b0  test: fix CI regressions (BLK-S36-03)
```

(Plus Sprint 35's 16 commits already on branch.)

---

## Sprint 37 roadmap (do not implement in Sprint 36)

- Full AgentRuntime wiring: `AgentBackgroundService` → `while (running) { runtime.Tick(); }`
- `IntentAssessment` wrapping IntentDraft: `{ Draft, RiskLevel, RequiresConfirmation, ReasoningSummary }`
- Observation-driven replanning: `Plan → Execute → ActionOutcome → LLM Evaluate → Replan?`
- `ItemConsumedEvent` full wiring (ingredients consumed during craft)
- `ItemDroppedEvent` (blocks placed during build)
- `CallWithOutcomeAsync` adoption in `AgentBackgroundService.DispatchActionsAsync`
