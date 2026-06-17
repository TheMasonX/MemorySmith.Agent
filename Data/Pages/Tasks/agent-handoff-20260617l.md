# Agent Handoff — MemorySmith.Agent Sprint 11
**Created:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` (PR #1 against `main` — extended dev branch, merge deferred)  
**CI status:** ✅ Green on `6f6b92f` (phase6-tasks update)  
**Task tracker:** `Data/Pages/Tasks/phase6-tasks.md`  
**Council:** `Data/Pages/council/sprint9-10-council-20260617.md`

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

## What was completed (Sprints 1–10)

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
| 9 | Flat-area scanner: Vec3 fix, A1/A2/A5, liquid check, async yield, A3 BuildFactKeys, guides | index.js, BuildFactKeys.cs, AgentBackgroundService.cs, HtnTaskLibrary.cs |
| 10 | Build robustness: D3 dedup, B4 chain expansion, B2 checkpoint, Matt Pocock AGENTS.md | HtnTaskLibrary.cs, AgentBackgroundService.cs, AGENTS.md |

---

## What remains (Sprint 11 backlog — priority order)

### P0 — Correctness

| ID | Task | File |
|----|------|------|
| B1-v2 | `DecomposeBuild`: add `requireOrigin` flag — gate plan generation on origin presence | `Agent.Planning/HtnTaskLibrary.cs` |
| Tests | Unit tests for `TryGetIntFact` (4 types), `GroupBy.Sum` dups, B2 resume skip correctness | `MemorySmith.Agent.Tests/` |
| Smoke | Node.js smoke test in CI (boot bot, call every tool once) — requires workflow scope workaround | CI |

### P1 — Build features

| ID | Task | File |
|----|------|------|
| B3 | Orientation-aware placement: pass facing direction in PlaceBlock args | `HtnTaskLibrary.cs`, `index.js` |
| B5 | Clear-area action: mine grass/snow/plants before building on a slight slope | `index.js` + new C# tool |
| Facts-persist | Document / implement `BuildProgressIndex` persistence across restarts | `Data/Pages/` |
| Stall | Detect stalled agent loop: warn if no action dispatched for >10s with active goal | `AgentBackgroundService.cs` |

### P2 — Deferred carries

| ID | Task | File |
|----|------|------|
| D1 (S1) | Reconnect delay array: trim dead 32s entry | `AgentBackgroundService.cs` |
| D2 (S2) | MemorySmithItemRegistry: parallel miss race | `Agent.Memory/` |
| D6 (S1) | NUnit2058 warning in MockMemoryGatewayTests.cs | `MemorySmith.Agent.Tests/` |
| Bubble | Add `bubble_column` to LIQUID_BLOCK_NAMES | `MineflayerAdapter/index.js` |

---

## Key architecture notes (Sprint 9–10 changes)

**BuildFactKeys.cs** — single source of truth for all shared fact key strings.
- `AutoBlueprintId`, `AutoOriginX/Y/Z` — written by `AgentBackgroundService.ProcessEventsAsync` on `FlatAreaFoundEvent ≥ 25`, read by `HtnTaskLibrary.DecomposeBuild` via `TryGetIntFact`.
- `BuildProgressIndex(blueprintId)` — written by `AgentBackgroundService.DispatchActionsAsync` on PlaceBlock success, read by `HtnTaskLibrary.DecomposeBuild` to resume from checkpoint.
- `LastFlatArea` — written by `WorldStateProjector` for any planner that wants last scan area.

**findFlatArea (index.js)** — fully overhauled:
- No Vec3 import; `bot.blockAt({x,y,z})` works.
- Scan window: `yAbove=10` / `yBelow=16`, per-call override.
- Scoring: `score = area × (FLAT_SCORE_WEIGHTS.area + compact × .compactness + flat × .flatness)`.
- Slope filter: `yRange > maxSlope=3` → reject before scoring.
- Liquid check: ground columns with water/lava in `LIQUID_BLOCK_NAMES` → excluded.
- Async yield: `setImmediate` every 200 columns.

**HtnTaskLibrary.DecomposeBuild** — three new behaviors:
1. Auto-origin resolution from `BuildFactKeys.AutoOriginX/Y/Z` when origin=(0,0,0).
2. B2 checkpoint: reads `BuildProgressIndex(blueprint.Name)` and skips placed blocks.
3. D3 fix: `GroupBy().ToDictionary()` instead of `ToDictionary()` on material list.
4. B4 expansion: more planks, tools, `SmeltItem iron_ingot`.

**B1 note:** Phase 0 early-return (return FindFlatArea only when no origin) was implemented then removed because it broke all crafting/build tests that pass (0,0,0) as origin. Sprint 11 adds a `requireOrigin` flag as the correct gate.

---

## File reference (Sprint 9–10 new/changed files)

| Path | Change |
|------|--------|
| `MineflayerAdapter/index.js` | Vec3+A1+A2+A5+liquid+async overhaul |
| `Agent.Core/BuildFactKeys.cs` | NEW — named fact key constants |
| `Agent.Core/WorldStateProjector.cs` | LastFlatArea write in FlatAreaFoundEvent case |
| `Agent.Planning/HtnTaskLibrary.cs` | D3+B4+B2 checkpoint+auto-origin |
| `WebUI.Blazor/AgentBackgroundService.cs` | FlatAreaFoundEvent handler (A3) + B2 checkpoint write |
| `WebUI.Blazor/Program.cs` | worldmodel ?detail=false |
| `MemorySmith.Agent.Tests/WorldStateProjectorTests.cs` | FlatAreaFoundEvent tests |
| `AGENTS.md` | Matt Pocock style rewrite |
| `Data/Pages/Guides/running-the-agent.md` | NEW — quickstart guide |
| `Data/Pages/Guides/features-reference.md` | NEW — features reference |
| `Data/Pages/council/sprint9-pre-council-20260617.md` | Pre-sprint 5-seat council |
| `Data/Pages/council/sprint9-10-council-20260617.md` | End-sprint 5-seat council |

---

## How to continue

1. `git fetch origin sprint-5-tool-safety && git checkout sprint-5-tool-safety`
2. `dotnet build MemorySmith.Agent.slnx` — should be clean
3. `dotnet test MemorySmith.Agent.Tests` — all green, ~0 failures
4. Read `AGENTS.md` (5 min, rules, patterns, anti-patterns)
5. Pick next task from `phase6-tasks.md` Sprint 11 backlog (start with B1-v2 + Tests)
6. Implement → push → CI → council → fix → next

**Sprint 11 recommended starting point:** B1-v2 (`requireOrigin` flag in `HtnTaskLibrary.DecomposeBuild`) + the three missing unit tests (TryGetIntFact types, GroupBy.Sum duplicate, B2 resume skip). These unblock council acceptance criteria 2, 3, 4, 5 from the Sprint 9-10 council.
