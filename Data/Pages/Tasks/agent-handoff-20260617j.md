# Agent Handoff — MemorySmith.Agent Sprint 8
**Created:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` (PR #1 against `main`)  
**CI status:** ✅ Green on commit `778c086`  
**Task tracker:** `Data/Pages/Tasks/phase6-tasks.md`

---

## Project summary

MemorySmith.Agent is a modular autonomous Minecraft bot backed by an LLM (Ollama/llama3.2:3b by
default). The bot name is **Leo**. It reads/writes long-term memory via the MemorySmith wiki
REST API. The architecture has two runtimes: a .NET 10 C# host (10 projects) and a Node.js
Mineflayer adapter that bridges the Minecraft wire protocol over WebSocket.

**Solution:** `MemorySmith.Agent.slnx` (VS 2022 .slnx format)  
**Run:** `dotnet run --project WebUI.Blazor --launch-profile WebUI.Blazor`  
**Test:** `dotnet test MemorySmith.Agent.Tests` (target: all green, ~no skips)

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

## What was completed (Sprints 1–7)

| Sprint | Theme | Key files |
|--------|-------|-----------|
| 1 | Reliability: non-blocking LLM + reconnect | AgentBackgroundService.cs |
| 2 | End-to-end build: crafting chain, IItemRegistry TTL | HtnTaskLibrary.cs, index.js |
| 3a | Typed world events | Agent.Core/Events/WorldEvents.cs |
| 3b | FindFlatAreaTool: terrain scan | Agent.Tools/Tools/FindFlatAreaTool.cs |
| 4a/b | SignalR dashboard + chat history | AgentHub.cs, ChatHistory.cs |
| 5 | Tool safety (schema validation), memory lifecycle | ToolDispatcher.cs |
| 6 | Journal, WorldModel, PlannerRouter + 3 decomposers | Agent.Core/Models/*, Agent.Planning/Decomposition/ |
| 7 | Chat reliability, observability APIs, Serilog fix | See below |

**Sprint 7 highlights (all on `sprint-5-tool-safety` branch):**
- Bot renamed Leo; rate limit raised to 20/min
- `FindFlatAreaTool.InputSchema` use-after-dispose fixed (cached static JsonDocument)
- NavigateTo + QueryStatus fast-pathed (skip LLM; pattern matcher has better context)
- Thinking indicator: "Hmm..." enqueued after 1.5s of LLM delay
- `IAgentJournal.Count`, `NullAgentJournal`, `GET /api/agent/journal`, `GET /api/agent/worldmodel`
- Serilog: removed EventLog duplicate sink, `[HH:mm:ss] message` console template (no `INF`)
- `ContainsBotName`: whole-word name match — "hello Leo" and "Leo, come here" now addressed
- `RecordBotSpoke` always called on addressed messages (conversation window stays open)
- System prompt enriched with health/food/inventory/capabilities; "ignore" removed, "chat" added
- Council review: `Data/Pages/council/sprint7-council-20260617.md`

---

## What remains (Sprint 8 backlog)

Priority order from `Data/Pages/Tasks/phase6-tasks.md`:

### P0 — Merge + minor correctness

- **Merge PR #1 to main** (CI is green, code is stable)
- **D4**: `WorldModel.Reconcile` — lock the full reconcile operation, not just `_cachedUncertainty`  
  File: `Agent.Core/Models/WorldModel.cs`
- **D7**: `DecomposerRegistry` — verify its internal list is guarded by a lock (it's probably a plain List today)  
  File: `Agent.Planning/DecomposerRegistry.cs` (check if it exists or is inside PlannerRouter)

### P1 — Flat-area scanner (audit A1–A4)

- **A1/A2**: Widen vertical scan window (currently ±5 blocks hardcoded); add compactness scoring
  File: `MineflayerAdapter/index.js` — `findFlatArea` handler
- **A3**: Wire `FlatAreaFoundEvent` → auto-set build origin in `HtnTaskLibrary.cs`
- **A4**: Unit tests for `ParseEvent` (FlatAreaFoundEvent round-trip) and flood-fill edge cases

### P2 — Deferred from Sprint 7 council

- **D1 (S7)**: Add `uncertainty` to `GET /api/agent/status` response  
  File: `WebUI.Blazor/Program.cs`
- **D2 (S7)**: Typed DTO for journal API response (replace raw `JournalEntry` serialization)
- **D4 (S7)**: Thinking indicator fires even when LLM response will be empty — ensure `quick.Response` is always non-empty for `Unknown` in `ChatInterpreter.ParseIntent`
- **ChatIntentType.Chat** — add explicit `case ChatIntentType.Chat:` in `AgentBackgroundService.HandleChatEventAsync` (currently falls through to Unknown path)

### P3 — Error→LLM recovery improvements

`TryRecoverFromGameErrorAsync` already exists (landed in `cb64b33`). Improvements:
- Include current inventory + available tools in the recovery prompt
- Trigger immediately for `blockNotFound`/`recipeMissing` errors, not just after ≥2 failures
- Add `ErrorRecovery` as a `JournalEntryType`

---

## Key architecture notes

**Chat addressing pipeline (per message):**
1. `ProcessEventsAsync` receives `ChatEvent` → writes to `_chatChannel`
2. `ChatConsumerAsync` (single reader) calls `HandleChatEventAsync`
3. `LlmChatInterpreter.InterpretAsync`:
   - `ChatInterpreter.IsDirectedAtBot` checks: solo? name in message? recent spoke? proximity?
   - Fast-path (no LLM): CreateGoal, CancelGoal, QueryHelp, QueryStatus, NavigateTo
   - LLM path: all other addressed messages
4. Response queued as `Chat` tool action → dispatched by `DispatchActionsAsync`

**Bot name matching:** `ContainsBotName` in `ChatInterpreter` — whole-word regex, case-insensitive, instance-cached.

**Thinking indicator:** 1.5s `Task.Delay` CTS linked to main ct. Fires for slow LLM (>1.5s). Cancels automatically for fast-path.

**Conversation window:** `ConversationWindowSeconds = 60` in ChatOptions. `RecordBotSpoke()` is called on EVERY addressed message (not just ones with a response). This keeps the window active.

**onlinePlayers:** Mineflayer filters itself out — `onlinePlayers = count of human players only`. In solo play this is 1, so all messages are addressed regardless of name.

**MCP push constraint:** `.github/workflows/` paths require the `workflow` OAuth scope — surface YAML to user for manual upload instead.

---

## File reference

| Path | Purpose |
|------|---------|
| `Agent.Core/Events/WorldEvents.cs` | Typed event hierarchy (15 record types) |
| `Agent.Core/Interfaces/IAgentJournal.cs` | Journal interface |
| `Agent.Core/Models/AgentJournal.cs` | Bounded ConcurrentQueue journal |
| `Agent.Core/Models/NullAgentJournal.cs` | No-op singleton for disabled agent / tests |
| `Agent.Core/Models/WorldModel.cs` | Rule-based prediction engine |
| `Agent.Planning/ChatInterpreter.cs` | Pattern-matching interpreter (fast-path) |
| `Agent.Planning/LlmChatInterpreter.cs` | LLM interpreter + system prompt |
| `Agent.Planning/ChatModels.cs` | ChatIntentType enum + ChatInterpretation record |
| `Agent.Planning/ChatRateLimiter.cs` | Per-player + global sliding-window limiter |
| `Agent.Planning/HtnTaskLibrary.cs` | HTN task decompositions |
| `Agent.Planning/Decomposition/` | BuildGoalDecomposer, GatherGoalDecomposer, SurviveNightGoalDecomposer |
| `Agent.Tools/ToolDispatcher.cs` | Schema-validated tool dispatch |
| `Agent.Tools/Tools/FindFlatAreaTool.cs` | Terrain scan tool |
| `Agent.World.Minecraft/MinecraftAdapter.cs` | WebSocket bridge to Node.js |
| `MineflayerAdapter/index.js` | Node.js Mineflayer bot (645 lines) |
| `WebUI.Blazor/AgentBackgroundService.cs` | Main agent loop |
| `WebUI.Blazor/Program.cs` | DI wiring + Serilog + REST endpoints |
| `WebUI.Blazor/appsettings.json` | Config: bot name, LLM provider, rate limits |
| `Data/Pages/Tasks/phase6-tasks.md` | Sprint task tracker (authoritative) |
| `Data/Pages/council/` | All council review docs |

---

## How to continue

1. `git fetch origin sprint-5-tool-safety && git checkout sprint-5-tool-safety`
2. `dotnet build MemorySmith.Agent.slnx` — should be clean
3. `dotnet test MemorySmith.Agent.Tests` — all green, ~0 failures
4. Pick the next task from `phase6-tasks.md` Sprint 8 backlog
5. Implement → push → wait for CI → council review → fix blockers → next task
