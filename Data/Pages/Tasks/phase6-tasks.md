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

## Sprint 3 — Architecture: Typed Events + FindFlatAreaTool (COMPLETE ✅)

**Audit:** `Data/Pages/Tasks/sprint3b-audit.md` (approve with revisions — all blocking items addressed)

| Task | Status | Notes |
|------|--------|-------|
| 3a: Typed world events (sealed records + pattern-match projector) | ✅ Done | `Agent.Core/Events/WorldEvents.cs`, `WorldStateProjector.cs` |
| 3b: FindFlatAreaTool (terrain scan) | ✅ Done | `Agent.Tools/Tools/FindFlatAreaTool.cs`, `MineflayerAdapter/index.js` |
| 3b HIGH fix: InputSchema use-after-dispose | ✅ Done | Static cached `JsonDocument` (`4384ee3` on sprint branch) |

**Deferred from Sprint 3b audit** (see `Data/Pages/Tasks/sprint3b-audit.md` for full details):

| ID | Finding | Priority | Sprint |
|----|---------|----------|--------|
| A1 | Flat-area scan: narrow vertical window (botY±5 only) | MEDIUM/HIGH | Sprint 4 |
| A2 | Flat-area scoring: area-only BFS ignores compactness/clearance | MEDIUM/HIGH | Sprint 4 |
| A3 | FlatAreaFoundEvent not consumed by planner (no build-origin auto-set) | MEDIUM | Sprint 4 |
| A4 | Missing tests: ParseEvent, FlatAreaFoundEvent, flood-fill edge cases | LOW/MEDIUM | Sprint 4 |

---

## Sprint 4 — UX: SignalR Dashboard + Chat History (COMPLETE ✅)

| Task | Status | Notes |
|------|--------|-------|
| 4a: SignalR push (AgentHub) | ✅ Done | `WebUI.Blazor/` |
| 4b: LLM chat history context window (last 5 turns) | ✅ Done | `Agent.Planning/LlmChatInterpreter.cs` |

---

## Sprint 5 — Tool Safety & Memory Lifecycle (COMPLETE ✅)

**Council review:** `Data/Pages/council/sprint4b-audit-council-20260616.md`

| Task | Status | Notes |
|------|--------|-------|
| P0: ToolDispatcher schema validation | ✅ Done | Sprint 5 |
| P0: /api/agent/command locked to registered tools | ✅ Done | Sprint 5 |
| P1: WorldState.Facts cap (1000) + Fact provenance | ✅ Done | Sprint 5 |
| P1: ReplanAsync context preservation | ✅ Done | Sprint 5 |
| P1: Per-action timeout (30s) | ✅ Done | Sprint 5 |
| P2: ToolEngine/ToolRegistry deleted, ToolDispatcher consolidated | ✅ Done | Sprint 5 |
| P2: FailureReason enum on IGoal | ✅ Done | Sprint 5 |
| P2: MinecraftAdapter SIGTERM→wait→SIGKILL | ✅ Done | Sprint 5 |

---

## Sprint 6 — Journal, World Model, Planner Extensibility (COMPLETE ✅)

**Council review:** `Data/Pages/council/sprint6-council-20260617.md`  
**CI commit:** `5617abc` (success)

| Task | Status | Notes |
|------|--------|-------|
| P0: AgentJournal (bounded, ConcurrentQueue, 15 call sites) | ✅ Done | Sprint 6 |
| P1: WorldModel (ObservationState/BeliefState/PredictionState) | ✅ Done | Sprint 6 |
| P2: IGoalDecomposer + DecomposerRegistry + PlannerRouter | ✅ Done | Sprint 6 |
| B1/B2 fix: AgentJournal trim race + non-atomic Clear | ✅ Done | Council blocker |
| B3 fix: WorldModel.GetIntArg JsonElement branch | ✅ Done | Council blocker |

**Deferred from Sprint 6 council** (D1–D7):

| ID | Finding | Priority | Sprint |
|----|---------|----------|--------|
| D1 | Uncertainty not in /api/agent/status | Low | Sprint 7 |
| D3 | IAgentJournal missing Count property | Low | Sprint 7 |
| D4 | WorldModel.Reconcile lock inconsistency | Low | Sprint 7 |
| D5 | No REST endpoints for journal / world model | Medium | Sprint 7 P0 |
| D6 | Nullable IAgentJournal — NullAgentJournal pattern | Low | Sprint 7 |
| D7 | DecomposerRegistry thread-safety audit | Low | Sprint 7 |

---

## Sprint 7 — LLM Chat Fixes + Observability APIs (IN PROGRESS 🔄)

| Task | Status | Notes |
|------|--------|-------|
| config: bot name → Leo, rate limit → 20/min | ✅ Done | `4003b88` |
| fix: FindFlatAreaTool.InputSchema use-after-dispose | ✅ Done | `0ec465e` |
| fix: NavigateTo LLM fast-path (named come-here was broken) | ✅ Done | `8efe6b7` |
| fix: inject playerPos into LLM system prompt | ✅ Done | `8efe6b7` |
| docs: sprint3b-audit.md captured in repo | ✅ Done | `4384ee3` |
| P0: GET /api/agent/journal + /api/agent/worldmodel endpoints | 🔲 Todo | D5 |
| P0: NullAgentJournal singleton | 🔲 Todo | D6 |
| P1: IAgentJournal.Count, Reconcile lock, DecomposerRegistry audit | 🔲 Todo | D3/D4/D7 |
| P2: Merge PR #1 → main | 🔲 Todo | |
| A1/A2: Widen flat-area scan window + compactness scoring | 🔲 Todo | Sprint 3b audit |
| A3: FlatAreaFoundEvent → auto-set build origin in planner | 🔲 Todo | Sprint 3b audit |
| A4: ParseEvent + FlatAreaFoundEvent unit tests | 🔲 Todo | Sprint 3b audit |

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
