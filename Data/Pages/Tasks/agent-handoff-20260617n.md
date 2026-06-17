# Agent Handoff — MemorySmith.Agent Sprint 13
**Created:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` (PR #1 against `main` — extended dev branch, merge deferred)  
**CI status:** GREEN on `a0d24fb` (NUnit2058 fix)  
**Task tracker:** `Data/Pages/Tasks/phase6-tasks.md`  
**Council:** `Data/Pages/council/sprint12-fixes-council-20260617.md`

---

## Project summary

MemorySmith.Agent is a modular autonomous Minecraft bot (name: **Leo**) backed by an LLM
(Ollama/llama3.2:3b by default). Long-term memory via MemorySmith wiki REST API.
Two runtimes: .NET 10 C# host (10 projects) and a Node.js Mineflayer adapter over WebSocket.

**Read AGENTS.md first.** It explains every rule including the new warnings-as-errors policy.

**Solution:** `MemorySmith.Agent.slnx` (VS 2022 .slnx format)  
**Run:** see `Data/Pages/Guides/running-the-agent.md`  
**Test:** `dotnet test MemorySmith.Agent.Tests` (target: all green, zero warnings)

---

## Workflow (every sprint)

```
implement -> local build/test -> push (github__create_or_update_file, paramsFile, plain text) ->
wait for CI green -> LLM council review -> fix blockers -> next sprint
```

**Zero-warning policy (new):** `Directory.Build.props` sets `TreatWarningsAsErrors=true`.
Any warning you introduce is a CI failure. Fix before pushing. If unavoidable, suppress with
`#pragma warning disable <ID>  // reason`.

---

## What was completed (Sprints 1–12)

| Sprint | Theme | Key files |
|--------|-------|-----------|
| 1 | Reliability: non-blocking LLM + reconnect | AgentBackgroundService.cs |
| 2 | End-to-end build: crafting chain, TTL cache | HtnTaskLibrary.cs, index.js |
| 3 | Typed events, FindFlatAreaTool | WorldEvents.cs, WorldStateProjector.cs |
| 4 | SignalR dashboard + chat history | AgentHub.cs, LlmChatInterpreter.cs |
| 5 | Tool safety (schema validation), memory lifecycle | ToolDispatcher.cs |
| 6 | Journal, WorldModel, PlannerRouter + 3 decomposers | Agent.Core/Models/, Agent.Planning/Decomposition/ |
| 7 | Chat reliability, observability APIs, Serilog fix | ChatInterpreter.cs, Program.cs |
| 8 | Correctness polish (WorldModel lock, JournalEntryDto, error recovery) | WorldModel.cs, AgentBackgroundService.cs |
| 9 | Flat-area scanner: Vec3 fix, liquid check, async yield, BuildFactKeys, guides | index.js, BuildFactKeys.cs |
| 10 | Build robustness: D3 dedup, B4 chain, B2 checkpoint, Matt Pocock AGENTS.md | HtnTaskLibrary.cs, AGENTS.md |
| 11 | Observability: CraftRegex, LLM timeout (Layer 1), thinking indicator log, B1-v2 requireOrigin | ChatInterpreter.cs, LlmChatInterpreter.cs |
| 12 | Bug fixes: ActionQueue thread safety, response queue ordering, Task.WhenAny timeout, TreatWarningsAsErrors | ActionQueue.cs, AgentBackgroundService.cs, LlmChatInterpreter.cs, Directory.Build.props |

---

## Sprint 12 changes (what YOU must know)

### Three concurrent-access bugs fixed

**Bug 1: Infinite planning loop** (ActionQueue not thread-safe)
- `ActionQueue` used `Queue<ActionData>` — not thread-safe.
- `DispatchActionsAsync` (task A) and `ChatConsumerAsync` (task B) accessed it concurrently.
- Concurrent `Clear()` from `SetGoal` + `EnqueueAll()` from planner caused queue corruption.
- Queue appeared empty after EnqueueAll → planner ran again → 30+ plans per second.
- **Fix:** Switched to `ConcurrentQueue<ActionData>` in `Agent.Core/Models/ActionQueue.cs`.

**Bug 2: Bot never responded to goals** (response cleared by SetGoal)
- `HandleChatEventAsync` enqueued the chat response BEFORE calling `TryCreateGoalFromChatAsync`.
- `TryCreateGoalFromChatAsync` called `SetGoal` which calls `_queue.Clear()` — wiping the response.
- **Fix:** Saved response in `pendingResponse` local variable; enqueue AFTER the switch.
- NavigateTo case: response enqueued BEFORE MoveTo (inside the case) so "On my way!" appears before movement.

**Bug 3: Recovery loop** (TryRecoverFromGameErrorAsync set same goal)
- After a failed gather goal, blockNotFound events triggered `TryRecoverFromGameErrorAsync`.
- LLM suggested `GatherItem:oak_log` — same goal that just failed.
- `TryCreateGoalFromChatAsync` → `SetGoal` → same goal → same failure → loop.
- **Fix:** Added `_lastAbandonedGoalName` field; `SetGoal` resets it; recovery checks it.

### LLM hard-deadline timeout
- Previous timeout used only CTS `CancelAfter(LlmTimeoutSeconds)`.
- Ollama streaming can ignore CancellationToken during token generation, causing 40s+ blocks.
- **Fix:** Added `Task.WhenAny(providerTask, Task.Delay(LlmTimeoutSeconds + 1))` as hard deadline.
- Both layers still exist: CTS fires first (cooperative), WhenAny fires +1s later (hard).

### Warnings = errors
- `Directory.Build.props` adds `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- Also fixed: `MockMemoryGatewayTests.cs` line 46 `Is.Not.Null.Or.Empty` → `Is.Not.Null.And.Not.Empty` (NUnit2058 — logical error caught by new policy).
- Also fixed: `ChatHistory._buffer` `volatile` keyword removed (CS0420 × 4).

---

## What remains (Sprint 13 backlog)

### P0 — End-to-end craft (D1 from Sprint 11, not yet done)

| ID | Task | File |
|----|------|------|
| D1 | Add `CraftItemGoal` + wire in `GoalFactory.CreateAsync("CraftItem:...")` + `HtnPlanner` + `HtnTaskLibrary.DecomposeCraftItem` | `Goals/CraftItemGoal.cs`, `GoalFactory.cs`, `HtnPlanner.cs`, `HtnTaskLibrary.cs` |

### P1 — Gather goal reliability

| ID | Task | Notes |
|----|------|-------|
| Gather-dirt | `GatherItem:dirt` fails because IItemRegistry has no spec for `dirt`. Add `dirt`, `snow`, `gravel` to a fallback item spec or extend `MemorySmithItemRegistry` with hardcoded basic specs for common mineable blocks. | `Agent.Memory/MemorySmithItemRegistry.cs` or new `FallbackItemRegistry` |
| Recovery | Don't call `TryRecoverFromGameErrorAsync` when `_lastAbandonedGoalName` matches — currently burns LLM rate budget on discarded suggestions | `AgentBackgroundService.cs` |

### P2 — Deferred carries

| ID | Task | File |
|----|------|------|
| D2 (S11) | Add `JsonElement` branch to `HtnTaskLibrary.TryGetIntFact` | `HtnTaskLibrary.cs` |
| D3 (S11) | Wire `requireOrigin: true` in `HtnPlanner.PlanAsync` for BuildGoal | `HtnPlanner.cs` |
| B3 | Orientation-aware placement: pass facing direction in PlaceBlock args | `HtnTaskLibrary.cs`, `index.js` |
| Stall | Detect stalled agent loop: warn if no action dispatched for >10s with active goal | `AgentBackgroundService.cs` |
| D2 (S2) | MemorySmithItemRegistry: parallel cache miss race | `Agent.Memory/` |
| D1 (S1) | Reconnect delay array: trim dead 32s entry | `AgentBackgroundService.cs` |

---

## Key architecture notes (Sprint 12)

### ActionQueue is now ConcurrentQueue
`ActionQueue` (at `Agent.Core/Models/ActionQueue.cs`) wraps `ConcurrentQueue<ActionData>`. All
operations (`Enqueue`, `EnqueueAll`, `Dequeue`, `Clear`, `IsEmpty`, `Count`) are thread-safe. This
is critical because `DispatchActionsAsync` and `ChatConsumerAsync` run as concurrent Tasks.

**Do NOT revert this to `Queue<T>`** — the concurrency hazard is real and hard to reproduce in tests.

### Chat response ordering rule
In `HandleChatEventAsync`:
- Save `pendingResponse` before the switch.
- Enqueue `pendingResponse` AFTER the switch (after `SetGoal`/`CancelGoal` have cleared the queue).
- Exception: NavigateTo — enqueue response before `MoveTo` (inside the case) so the bot speaks before moving.

This pattern must be maintained for all future modifications to `HandleChatEventAsync`.

### LLM timeout is now two-layer
`LlmChatInterpreter.InterpretAsync` has:
- Layer 1: `llmCts.CancelAfter(LlmTimeoutSeconds)` — signals provider's CancellationToken.
- Layer 2: `Task.WhenAny(providerTask, Task.Delay(LlmTimeoutSeconds + 1))` — hard deadline.

The +1s gap means the CTS fires first (clean cancel), WhenAny fires 1s later only if the provider
ignored the token (uncooperative). Do NOT collapse these into a single mechanism — both serve distinct purposes.

### Zero-warning policy
`Directory.Build.props` at repo root applies `TreatWarningsAsErrors=true` to ALL projects.
Every warning is a CI failure. This policy also caught a pre-existing logical bug in `MockMemoryGatewayTests`.

---

## How to continue

1. `git fetch origin sprint-5-tool-safety && git checkout sprint-5-tool-safety`
2. `dotnet build MemorySmith.Agent.slnx` — zero warnings, zero errors
3. `dotnet test MemorySmith.Agent.Tests` — all green
4. Read `AGENTS.md` — new Rule 8 and anti-patterns section covers sprint 12 learnings
5. Start with D1 (CraftItemGoal) — this is the most impactful user-facing gap

**Sprint 13 recommended starting point:** CraftItemGoal (D1). The user can now say "craft an iron pickaxe" and Leo routes it deterministically, but the goal factory returns null. Adding CraftItemGoal gives Leo the ability to actually execute crafting requests end-to-end.
