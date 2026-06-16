# Phase 6 Tasks

**Tracking file:** `Data/Pages/Tasks/phase6-tasks.md`
**Phase 6 start:** 2026-06-16 (session 8, after TSK-0014 refactor)

---

## Sprint 1 — Reliability (COMPLETE ✅)

**Council review:** `Data/Pages/council/sprint1-impl-council-20260616.md`
**CI commit:** `b0924ea` (conclusion: success)

| Task | Status | Notes |
|------|--------|-------|
| 1a: Non-blocking LLM (Channel<WorldEvent>) | ✅ Done | `AgentBackgroundService.cs` — chat events offloaded to `ChatConsumerAsync` |
| 1b: Reconnect with exponential backoff | ✅ Done | `AgentBackgroundService.cs` — 5-delay retry loop, per-connection CTS |
| Tests: SlowChatInterpreter (1a) + FailingWorldAdapter (1b) | ✅ Done | Both pass |
| CI hotfixes: 5 pre-existing TSK-0014 bugs | ✅ Done | Raw string, Position?, ChatOptions record, named arg, filler regex |

---

## Sprint 2 — End-to-End Build (TODO)

**Backlog priority order:**

| # | Task | File | Notes |
|---|------|------|-------|
| 2a | CraftItemTool: pathfind to crafting table | `MineflayerAdapter/index.js` | Add pathfinder.goto() before bot.craft(); expand search radius 4→8 |
| 2b | HtnTaskLibrary.DecomposeBuild: crafting chain | `Agent.Planning/HtnTaskLibrary.cs` | oak_log → planks → table, slabs, door, chest, torches |
| 2c | IItemRegistry TTL cache (60s) | `Agent.Memory/MemorySmithItemRegistry.cs` | ConcurrentDictionary with DateTimeOffset expiry |

---

## Sprint 3 — Architecture: Typed Events + FindFlatAreaTool (TODO)

| # | Task | File |
|---|------|------|
| 3a | Typed world events (replace Dictionary payload) | `Agent.Core/`, `Agent.World.Minecraft/WebSocketBridge.cs` |
| 3b | FindFlatAreaTool (terrain scan, auto-set build origin) | `MineflayerAdapter/index.js` + new C# tool |

---

## Sprint 4 — UX: SignalR Dashboard + Chat History (TODO)

| # | Task | File |
|---|------|-------|
| 4a | SignalR push (AgentHub) | `WebUI.Blazor/` |
| 4b | LLM chat history context window (last 5 turns) | `Agent.Planning/LlmChatInterpreter.cs` |

---

## Deferred (from Sprint 1 council review)

| ID | Finding | Phase |
|----|---------|-------|
| D1 | Attempt count 6 vs spec's "5" — document or align | Sprint 2 |
| D2 | "Reconnecting" WorldEvent not emitted on retry | Sprint 3 |
| D3 | `_chatChannel` persistence across reconnects — document | Sprint 2 |
| D4 | Normal `ProcessEventsAsync` completion → reconnect trigger — document | Sprint 2 |
| D6 | NUnit2058 warning in MockMemoryGatewayTests.cs | Sprint 3 cleanup |
