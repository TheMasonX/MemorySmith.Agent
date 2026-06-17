# Agent Handoff — MemorySmith.Agent Sprint 12
**Created:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` (PR #1 against `main` — extended dev branch, merge deferred)  
**CI status:** GREEN on `4d31c61` (council review push)  
**Task tracker:** `Data/Pages/Tasks/phase6-tasks.md`  
**Council:** `Data/Pages/council/sprint11-council-20260617.md`

---

## Project summary

MemorySmith.Agent is a modular autonomous Minecraft bot (name: **Leo**) backed by an LLM
(Ollama/llama3.2:3b by default). Long-term memory via MemorySmith wiki REST API.
Two runtimes: .NET 10 C# host (10 projects) and a Node.js Mineflayer adapter over WebSocket.

**Read AGENTS.md first.** It explains every rule you must not break in 5 minutes.

**Solution:** `MemorySmith.Agent.slnx` (VS 2022 .slnx format)  
**Run:** see `Data/Pages/Guides/running-the-agent.md`  
**Test:** `dotnet test MemorySmith.Agent.Tests` (target: all green)

---

## Workflow (every sprint)

```
implement → local build/test → push (github__create_or_update_file, paramsFile, plain text) →
wait for CI green → LLM council review → fix blockers → next sprint
```

**Push rule:** always `paramsFile` with **plain text** `content`. Never pre-encode base64.  
**CI check:** `curl -s "https://api.github.com/repos/TheMasonX/MemorySmith.Agent/commits/<sha>/check-runs"`  
**Failures:** `curl -s "https://api.github.com/repos/TheMasonX/MemorySmith.Agent/check-runs/<id>/annotations"`

---

## What was completed (Sprints 1–11)

| Sprint | Theme | Key files |
|--------|-------|-----------| 
| 1 | Reliability: non-blocking LLM + reconnect | AgentBackgroundService.cs |
| 2 | End-to-end build: crafting chain, TTL cache | HtnTaskLibrary.cs, index.js |
| 3 | Typed events, FindFlatAreaTool | WorldEvents.cs, WorldStateProjector.cs |
| 4 | SignalR dashboard + chat history | AgentHub.cs, LlmChatInterpreter.cs |
| 5 | Tool safety (schema validation), memory lifecycle | ToolDispatcher.cs |
| 6 | Journal, WorldModel, PlannerRouter + 3 decomposers | Agent.Core/Models/, Agent.Planning/Decomposition/ |
| 7 | Chat reliability, observability APIs, Serilog fix | ChatInterpreter.cs, Program.cs |
| 8 | Correctness polish (WorldModel lock, JournalEntryDto, error recovery) | WorldModel.cs, Dtos.cs, AgentBackgroundService.cs |
| 9 | Flat-area scanner: Vec3 fix, A1/A2/A5, liquid check, async yield, A3 BuildFactKeys, guides | index.js, BuildFactKeys.cs |
| 10 | Build robustness: D3 dedup, B4 chain expansion, B2 checkpoint, Matt Pocock AGENTS.md | HtnTaskLibrary.cs, AGENTS.md |
| 11 | Chat observability + correctness: CraftRegex, LLM timeout, thinking indicator log, B1-v2 requireOrigin, 9 new tests | ChatInterpreter.cs, LlmChatInterpreter.cs, AgentBackgroundService.cs, HtnTaskLibrary.cs |

---

## Sprint 11 changes (what YOU must know)

### Problem fixed: "craft an iron pickaxe" hung the LLM for 2+ minutes
- Root cause: `ChatInterpreter` had no "craft" verb → `Unknown` intent → `LlmChatInterpreter` forwarded to Ollama → no timeout.
- **Fix 1 (ChatInterpreter.cs):** `CraftRegex` matching `craft|forge|smelt` + `CraftAliases` dict. "craft an iron pickaxe" → `CreateGoal("CraftItem:iron_pickaxe")` deterministically. LLM never called for craft commands.
- **Fix 2 (LlmChatInterpreter.cs):** `llmCts.CancelAfter(options.LlmTimeoutSeconds)` wraps every `provider.CompleteAsync`. Default 10s (`ChatOptions.LlmTimeoutSeconds = 10`). Tune in appsettings: `"LlmTimeoutSeconds": 30` for slow hardware.
- **Fix 3 (AgentBackgroundService.cs):** Thinking indicator logs `[chat] thinking indicator sent ('Hmm...') — LLM response pending >1.5s`. Interpretation result logs `[chat] <{Username}> -> {Intent} ({GoalName})`.
- **Fix 4 (HtnTaskLibrary.cs):** `DecomposeBuild(requireOrigin: false)` default; pass `true` to get FindFlatArea-only when no origin.
- **9 new tests (HtnTaskLibraryExtraTests.cs):** TryGetIntFact coercion (int/long/double/string), GroupBy.Sum duplicates, requireOrigin variants.

### Known gap: GoalFactory may not handle CraftItem:* goals
"craft an iron pickaxe" now routes deterministically BUT if `GoalFactory.CreateAsync("CraftItem:iron_pickaxe", ...)` returns null, the bot responds "Sorry, I don't know how to do that yet." — **This is still correct behaviour** (no hang), but the craft won't execute. Fix in Sprint 12 (D1 below).

---

## What remains (Sprint 12 backlog — priority order)

### P0 — Unblock craft end-to-end (D1)

| ID | Task | File |
|----|------|------|
| D1 | Check/add `CraftItem:*` handling in `GoalFactory.CreateAsync` — map to an executable CraftItemGoal or existing goal type | `Agent.Planning/GoalFactory.cs` |

### P1 — Correctness carries from Sprint 11

| ID | Task | File |
|----|------|------|
| D2 | Add `JsonElement` branch to `HtnTaskLibrary.TryGetIntFact` — checkpoint from JSON deserialization silently fails without it | `Agent.Planning/HtnTaskLibrary.cs` |
| D3 | Wire `requireOrigin: true` in `HtnPlanner.PlanAsync` for BuildGoal — confirms B1-v2 is active in production flows; audit existing tests first | `Agent.Planning/HtnPlanner.cs` |

### P1 — Build features (from Sprint 11 handoff)

| ID | Task | File |
|----|------|------|
| B3 | Orientation-aware placement: pass facing direction in PlaceBlock args | `HtnTaskLibrary.cs`, `index.js` |
| B5 | Clear-area action: mine grass/snow/plants before building on a slight slope | `index.js` + new C# tool |
| Stall | Detect stalled agent loop: warn if no action dispatched for >10s with active goal | `AgentBackgroundService.cs` |

### P2 — Documentation

| ID | Task | File |
|----|------|------|
| D4 | Document `LlmTimeoutSeconds` in `appsettings.Development.json` example | config |
| D5 | Add craft-vs-make routing note to `running-the-agent.md` | `Data/Pages/Guides/running-the-agent.md` |

### P2 — Deferred carries (from earlier sprints)

| ID | Task | File |
|----|------|------|
| D1 (S1) | Reconnect delay array: trim dead 32s entry | `AgentBackgroundService.cs` |
| D2 (S2) | MemorySmithItemRegistry: parallel miss race | `Agent.Memory/` |
| D6 (S1) | NUnit2058 warning in MockMemoryGatewayTests.cs | `MemorySmith.Agent.Tests/` |
| Bubble | Add `bubble_column` to LIQUID_BLOCK_NAMES | `MineflayerAdapter/index.js` |
| Smoke | Node.js smoke test in CI (requires workflow scope workaround) | CI |

---

## Key architecture notes (Sprint 11 additions)

### Chat routing — deterministic-first (D-003)
```
Player message
  → ChatInterpreter.ParseIntent()
      cancel/status/help/navigate/build/gather/craft → CreateGoal/CancelGoal/etc. (fast-path)
      Unknown → LlmChatInterpreter sends to Ollama (with 10s timeout)
                → on timeout/null/parse-fail → pattern result ("Didn't catch that.")
```

**Verb mapping:**
- `craft|forge|smelt` + item → `CraftItem:<item_id>` (deterministic)
- `get|gather|collect|mine|find|bring|fetch` + item → `GatherItem:<item_id>` (deterministic)
- `build|construct|make` + blueprint → `Build:<blueprint_id>` (deterministic)
- `make` + item (not a blueprint) → Unknown → LLM → may timeout to "Didn't catch that."
- greetings/questions/ambiguous → Unknown → LLM → max 10s

### LLM timeout configuration
Set in `appsettings.json` under `Agent:Chat:LlmTimeoutSeconds`. Default 10s. Use 30s for a 3B model on slow hardware, 60s for 7B+.

### requireOrigin flag
`HtnTaskLibrary.DecomposeBuild(..., requireOrigin: false)` — default false for backward compatibility.
When true: if auto-origin lookup fails, returns `[FindFlatArea]` only. HtnPlanner does NOT yet pass true (D3).

---

## How to continue

1. `git fetch origin sprint-5-tool-safety && git checkout sprint-5-tool-safety`
2. `dotnet build MemorySmith.Agent.slnx` — should be clean
3. `dotnet test MemorySmith.Agent.Tests` — all green including 9 new tests in HtnTaskLibraryExtraTests
4. Read `AGENTS.md` (5 min)
5. Start with **D1** — check `GoalFactory.CreateAsync` for `CraftItem:*` handling

**Sprint 12 recommended starting point:** D1 (GoalFactory CraftItem support) + D2 (JsonElement branch in TryGetIntFact). These unblock the full craft end-to-end flow that Sprint 11 deterministically routes.
