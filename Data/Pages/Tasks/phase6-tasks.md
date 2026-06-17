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
**CI commit:** `cdc0d18` (B1 fix + tests)

| Task | Status | Notes |
|------|--------|-------|
| 2a: CraftItemTool pathfind to crafting table | ✅ Done | `index.js` |
| 2b: DecomposeBuild crafting chain | ✅ Done | `HtnTaskLibrary.cs` |
| 2b B1 fix: auto-emit crafting_table for slab/door/chest blueprints | ✅ Done | |
| 2c: IItemRegistry TTL cache | ✅ Done | `MemorySmithItemRegistry.cs` |
| AGENTS.md at repo root | ✅ Done | |

---

## Sprint 3 — Architecture: Typed Events + FindFlatAreaTool (COMPLETE ✅)

**Audit:** `Data/Pages/Tasks/sprint3b-audit.md`

| Task | Status | Notes |
|------|--------|-------|
| 3a: Typed world events (sealed records + pattern-match projector) | ✅ Done | `Agent.Core/Events/WorldEvents.cs`, `WorldStateProjector.cs` |
| 3b: FindFlatAreaTool (terrain scan) | ✅ Done | `Agent.Tools/Tools/FindFlatAreaTool.cs`, `MineflayerAdapter/index.js` |
| 3b HIGH fix: InputSchema use-after-dispose | ✅ Done | Static cached `JsonDocument` |

**Deferred from Sprint 3b audit:**

| ID | Finding | Priority | Sprint |
|----|---------|----------|--------|
| A1 | Flat-area scan: narrow vertical window (botY±5 only) | MEDIUM/HIGH | Sprint 9 |
| A2 | Flat-area scoring: area-only BFS ignores compactness/clearance | MEDIUM/HIGH | Sprint 9 |
| A3 | FlatAreaFoundEvent not consumed by planner (no build-origin auto-set) | MEDIUM | Sprint 9 |
| A4 | Missing tests: ParseEvent, FlatAreaFoundEvent, flood-fill edge cases | LOW/MEDIUM | Sprint 9 |

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
| P0: ToolDispatcher schema validation | ✅ Done | |
| P0: /api/agent/command locked to registered tools | ✅ Done | |
| P1: WorldState.Facts cap (1000) + Fact provenance | ✅ Done | |
| P1: ReplanAsync context preservation | ✅ Done | |
| P1: Per-action timeout (30s) | ✅ Done | |
| P2: ToolEngine/ToolRegistry deleted, ToolDispatcher consolidated | ✅ Done | |
| P2: FailureReason enum on IGoal | ✅ Done | |
| P2: MinecraftAdapter SIGTERM→wait→SIGKILL | ✅ Done | |

---

## Sprint 6 — Journal, World Model, Planner Extensibility (COMPLETE ✅)

**Council review:** `Data/Pages/council/sprint6-council-20260617.md`  
**CI commit:** `16c8eae8` (council blocker fixes, CI green)

| Task | Status | Notes |
|------|--------|-------|
| P0: AgentJournal (bounded, ConcurrentQueue, 15 call sites) | ✅ Done | |
| P1: WorldModel (ObservationState/BeliefState/PredictionState) | ✅ Done | |
| P2: IGoalDecomposer + DecomposerRegistry + PlannerRouter | ✅ Done | |
| B1/B2 fix: AgentJournal trim race + non-atomic Clear | ✅ Done | Council blocker |
| B3 fix: WorldModel.GetIntArg JsonElement branch | ✅ Done | Council blocker |

**Deferred from Sprint 6 council (D1–D7):**

| ID | Finding | Sprint |
|----|---------|--------|
| D1 | Uncertainty not in /api/agent/status | Sprint 8 ✅ |
| D4 | WorldModel.Reconcile lock inconsistency | Sprint 8 ✅ |
| D5 | No REST endpoints for journal / world model | Sprint 7 ✅ |
| D7 | DecomposerRegistry thread-safety audit | Sprint 8 ✅ (verified, already correct) |

---

## Sprint 7 — LLM Chat Fixes + Observability APIs (COMPLETE ✅)

**Council review:** `Data/Pages/council/sprint7-council-20260617.md`  
**CI commit:** `778c086` (green)

| Commit | Change |
|--------|--------|
| `4003b88` | Bot renamed Leo; rate limit 5→20/min |
| `0ec465e` | FindFlatAreaTool.InputSchema use-after-dispose (HIGH) |
| `8efe6b7` | NavigateTo LLM fast-path + playerPos in prompt |
| `a9e91d6` | QueryStatus added to fast-path |
| `bf03b50` | Thinking indicator ("Hmm...") after 1.5s LLM delay |
| `86d2499` | IAgentJournal.Count property |
| `541e2c0` | AgentJournal implements Count |
| `c847e94` | NullAgentJournal singleton |
| `26a6186` | GET /api/agent/journal + /api/agent/worldmodel |
| `4668366` | Serilog: remove EventLog dup sink, clean output template |
| `a48c12f` | ChatIntentType.Chat for conversational LLM responses |
| `3f391e5` | ContainsBotName — whole-word match |
| `5d84785` | RecordBotSpoke always called on addressed messages |
| `f9c553f` | Richer system prompt: health/food/inventory; remove "ignore" |
| `0cbb9c1` | B1: FormatInventory null guard |
| `c2cc74e` | B3: ContainsBotName cached compiled Regex |

---

## Sprint 8 — Correctness Polish & Observability (COMPLETE ✅)

**Handoff:** `Data/Pages/Tasks/agent-handoff-20260617k.md`  
**Council review:** `Data/Pages/council/sprint8-council-20260617.md` _(to be written)_

| ID | Task | Commit | Status |
|----|------|--------|--------|
| D4 | WorldModel.Reconcile: full-method lock + Queue<double> (atomic) | `ac6f29e` | ✅ Done |
| D7 | DecomposerRegistry: List already lock-guarded — verified, no change | — | ✅ Verified |
| S7-D1 | Add `Uncertainty` field to GET /api/agent/status | `01f6b18` | ✅ Done |
| S7-D2 | Typed `JournalEntryDto` for journal API + `Dtos.cs` | `19a4211`, `01f6b18` | ✅ Done |
| S7-Chat | Explicit `case ChatIntentType.Chat: break;` in switch | `21e489e` | ✅ Done |
| S7-D4 | LlmChatInterpreter: guard empty LLM response → substitute pattern fallback | `148b632` | ✅ Done |
| P3-a | `ErrorRecovery` JournalEntryType; log before recovery call | `3085ced` | ✅ Done |
| P3-b | Richer recovery prompt: inventory + available actions | `21e489e` | ✅ Done |
| P3-c | Immediate recovery trigger for `blockNotFound`/`recipeMissing` | `21e489e` | ✅ Done |

---

## Sprint 9 — Flat-Area Scanner Depth (NEXT)

| ID | Task | File |
|----|------|------|
| A1 | Widen vertical scan window (±5 → ±10 blocks) | `MineflayerAdapter/index.js` |
| A2 | Compactness scoring — prefer contiguous squares, not thin strips | `MineflayerAdapter/index.js` |
| A3 | Wire `FlatAreaFoundEvent` → auto-set build origin in planner | `Agent.Planning/HtnTaskLibrary.cs` |
| A4 | Unit tests: ParseEvent FlatAreaFoundEvent round-trip + flood-fill | `MemorySmith.Agent.Tests/` |
| A5 | Slope/roughness penalty (large height variance → reject site) | `MineflayerAdapter/index.js` |
| D3 (S7) | WorldModel endpoint: add `?detail=false` summary mode | `WebUI.Blazor/Program.cs` |
| D1 (S1) | Reconnect attempt count 6 vs spec "5" | `AgentBackgroundService.cs` |
| D2 (S1) | Emit "Reconnecting" WorldEvent during backoff | `AgentBackgroundService.cs` |
| D2 (S2) | Parallel cache miss race in `MemorySmithItemRegistry` | `Agent.Memory/` |
| D3 (S2) | `ToDictionary` throws on duplicate blueprint materials | `HtnTaskLibrary.cs` |

---

## Sprint 10 — Build Robustness (FUTURE)

| ID | Task | Notes |
|----|------|-------|
| B1 | Build-site preflight: inventory, fuel, table access, terrain clearance | Audit sec 4.3 item 6 |
| B2 | Checkpoint/resume: persist last successful placement index | Audit sec 4.2.D |
| B3 | Orientation-aware placement metadata in PlannerRouter | Audit sec 4.3 item 3 |
| B4 | Resource chain expansion (sticks, stone tools, ore→ingots, fuel selection) | Audit sec 4.2.C |
| B5 | Clear-area / flatten-area action for minor terrain irregularity | Audit sec 4.3 item 2 |

---

## Deferred carry-forward

| ID | Finding | Phase |
|----|---------|-------|
| D4 (S2) | `TorchesPerCraft = 4` hardcoded vanilla recipe | future IRecipeRegistry |
| D5 (S7) | Inventory × char in LLM prompt (benign, cosmetic) | Future |
| D6 (S1) | NUnit2058 warning in MockMemoryGatewayTests.cs | Sprint 9 cleanup |
