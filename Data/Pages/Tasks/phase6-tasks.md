# Phase 6 Tasks

**Tracking file:** `Data/Pages/Tasks/phase6-tasks.md`
**Phase 6 start:** 2026-06-16 (session 8, after TSK-0014 refactor)

---

## Sprint 1 — Reliability (COMPLETE ✅)

**Council review:** `Data/Pages/council/sprint1-impl-council-20260616.md`
**CI commit:** `b0924ea` (conclusion: success)

| Task | Status | Notes |
|------|--------|-------|
| 1a: Non-blocking LLM (Channel<WorldEvent>) | ✅ Done | `AgentBackgroundService.cs` |
| 1b: Reconnect with exponential backoff | ✅ Done | `AgentBackgroundService.cs` |
| Tests: SlowChatInterpreter (1a) + FailingWorldAdapter (1b) | ✅ Done | Both pass |
| CI hotfixes: 5 pre-existing TSK-0014 bugs | ✅ Done | |

---

## Sprint 2 — End-to-End Build (COMPLETE ✅)

**Council review:** `Data/Pages/council/sprint2-impl-council-20260616.md`
**CI commit:** `cdc0d18` (B1 fix + tests, conclusion: pending)

| Task | Status | Notes |
|------|--------|-------|
| 2a: CraftItemTool pathfind to crafting table | ✅ Done | `index.js` — CRAFT_TABLE_SEARCH_RADIUS=8, pathfinder.goto() |
| 2b: DecomposeBuild crafting chain | ✅ Done | `HtnTaskLibrary.cs` — planks→table→slabs/door/chest/torches |
| 2b B1 fix: auto-emit crafting_table for slab/door/chest blueprints | ✅ Done | `HtnTaskLibrary.cs` — B1 fix |
| 2c: IItemRegistry TTL cache | ✅ Done | `MemorySmithItemRegistry.cs` — configurable via ItemCacheTtlSeconds |
| AGENTS.md at repo root | ✅ Done | No magic numbers, C# + JS conventions, sprint workflow |

---

## Sprint 3 — Architecture: Typed Events + FindFlatAreaTool (TODO)

| # | Task | File |
|---|------|------|
| 3a | Typed world events (replace Dictionary payload) | `Agent.Core/`, `WebSocketBridge.cs` |
| 3b | FindFlatAreaTool (terrain scan, auto-set build origin) | `index.js` + new C# tool |

---

## Sprint 4 — UX: SignalR Dashboard + Chat History (TODO)

| # | Task | File |
|---|------|-------|
| 4a | SignalR push (AgentHub) | `WebUI.Blazor/` |
| 4b | LLM chat history context window (last 5 turns) | `Agent.Planning/LlmChatInterpreter.cs` |

---

## Deferred (from Sprint 1 council)

| ID | Finding | Phase |
|----|---------|-------|
| D1 (S1) | Reconnect attempt count 6 vs spec's "5" | Sprint 3 |
| D2 (S1) | "Reconnecting" WorldEvent not emitted | Sprint 3 |
| D3 (S1) | `_chatChannel` persistence across reconnects — document | Sprint 2 (done: AGENTS.md) |
| D6 (S1) | NUnit2058 warning in MockMemoryGatewayTests.cs | Sprint 3 cleanup |

## Deferred (from Sprint 2 council)

| ID | Finding | Phase |
|----|---------|-------|
| D2 (S2) | Parallel miss race — two HTTP calls on concurrent cache miss | Sprint 3 |
| D3 (S2) | `ToDictionary` throws on duplicate blueprint materials | Sprint 3 |
| D4 (S2) | `TorchesPerCraft = 4` hardcoded vanilla recipe | future IRecipeRegistry |
