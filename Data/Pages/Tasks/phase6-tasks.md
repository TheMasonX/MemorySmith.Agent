# Phase 6 Tasks

**Tracking file:** `Data/Pages/Tasks/phase6-tasks.md`  
**Phase 6 start:** 2026-06-16 (session 8, after TSK-0014 refactor)

---

## Sprints 1–8 (COMPLETE ✅)

See previous entries in commit history. Key sprint council reviews in `Data/Pages/council/`.

Summary:
- Sprint 1: Reliability (non-blocking LLM + reconnect)
- Sprint 2: End-to-end build (crafting chain, TTL cache)
- Sprint 3: Typed events + FindFlatAreaTool
- Sprint 4: SignalR dashboard + chat history
- Sprint 5: Tool safety (schema validation, memory lifecycle)
- Sprint 6: Journal, WorldModel, PlannerRouter + decomposers
- Sprint 7: Chat reliability, observability APIs, Serilog fix
- Sprint 8: Correctness polish (WorldModel lock, JournalEntryDto, error recovery)

---

## Sprint 9 — Flat-Area Scanner Depth (COMPLETE ✅)

**CI commit:** `2427c0b` (green)  
**Pre-sprint council:** `Data/Pages/council/sprint9-pre-council-20260617.md`  
**End council:** `Data/Pages/council/sprint9-10-council-20260617.md`

| ID | Task | Commit | Status |
|----|------|--------|--------|
| Vec3-fix | `bot.blockAt({x,y,z})` — eliminated undefined Vec3 runtime error | `d491c8c` | ✅ Done |
| A1 | Vertical scan widened: +10/-16 blocks, named constants, per-call override | `d491c8c` | ✅ Done |
| A2 | Composite scoring: `FLAT_SCORE_WEIGHTS` named object (area 50%, compact 30%, flat 20%) | `d491c8c` | ✅ Done |
| A5 | Slope penalty: components with yRange > maxSlope=3 rejected before scoring | `d491c8c` | ✅ Done |
| Liquid | `LIQUID_BLOCK_NAMES` set — water/lava/flowing excluded from heightMap | `d491c8c` | ✅ Done |
| Yield | `setImmediate` every 200 columns — keeps Mineflayer event loop responsive | `d491c8c` | ✅ Done |
| BuildFactKeys | `Agent.Core/BuildFactKeys.cs` — named constants for all shared fact key strings | `c874c7d` | ✅ Done |
| A3 | AgentBackgroundService: `FlatAreaFoundEvent` → `SetBuildOrigin("auto",...)` when area≥25 | `ff34ccb` | ✅ Done |
| A3-htn | `HtnTaskLibrary.DecomposeBuild`: reads auto-origin from `BuildFactKeys` facts | `e38c91a` | ✅ Done |
| A4 | `WorldStateProjectorTests`: 2 new FlatAreaFoundEvent tests + BuildFactKeys.LastFlatArea assert | `08c56c0` | ✅ Done |
| WSP | `WorldStateProjector`: stores `BuildFactKeys.LastFlatArea` in FlatAreaFoundEvent case | `b6d211` | ✅ Done |
| S7-D3 | `/api/agent/worldmodel?detail=false` — lightweight summary mode | `130701e` | ✅ Done |
| Guides | `Data/Pages/Guides/running-the-agent.md` — quickstart, config, API, issues | `de68751` | ✅ Done |
| Guides | `Data/Pages/Guides/features-reference.md` — architecture, tools, chat, build | `2427c0b` | ✅ Done |
| Pre-council | `Data/Pages/council/sprint9-pre-council-20260617.md` | `c097a6b` | ✅ Done |

---

## Sprint 10 — Build Robustness & Matt Pocock Handoff (COMPLETE ✅)

**CI commit:** `866d637` (green — Phase 0 early-return fix)  
**Council:** `Data/Pages/council/sprint9-10-council-20260617.md`

| ID | Task | Commit | Status |
|----|------|--------|--------|
| D3 | `BuildCraftingChain`: `GroupBy+Sum` replaces `ToDictionary` — no more duplicate-key throw | `5119f75` | ✅ Done |
| B4 | `CraftingChainOrder` expanded: dark_oak/jungle/acacia/mangrove/cherry planks, stick, oak_stairs, fence, fence_gate, wooden/stone tools | `5119f75` | ✅ Done |
| B4 | `SmeltItem` step for `iron_ingot` in `DecomposeBuild` | `5119f75` | ✅ Done |
| B2 | `BuildFactKeys.BuildProgressIndex(string)` + context keys `PlaceBlockProgressBlueprintId/BlockIndex` | `6f553d2` | ✅ Done |
| B2 | `AgentBackgroundService.DispatchActionsAsync`: writes checkpoint fact on PlaceBlock success | `5709b23` | ✅ Done |
| B2 | `HtnTaskLibrary.DecomposeBuild`: reads `BuildProgressIndex` checkpoint, skips placed blocks | `866d637` | ✅ Done |
| B1-note | Phase 0 early-return removed (broke tests); preflight gating deferred to Sprint 11 after callsite audit | `866d637` | ✅ Done |
| AGENTS.md | Matt Pocock style rewrite: 7 rules, where-things-live, patterns, anti-patterns | `fd867f9` | ✅ Done |

---

## Sprint 11 — Build Completion & Foundation (NEXT)

From council review `sprint9-10-council-20260617.md`:

| ID | Task | File | Notes |
|----|------|------|-------|
| B1-v2 | `DecomposeBuild`: add `requireOrigin` flag — gate plan on origin presence | `HtnTaskLibrary.cs` | Proper fix after callsite audit |
| B3 | Orientation-aware PlaceBlock: facing direction in action args | `HtnTaskLibrary.cs`, `index.js` | |
| B5 | Clear-area action: mine grass/snow/plants before building | `index.js` + new C# tool | |
| Smoke | Node.js smoke test in CI: boot bot, call every tool once | `.github/workflows/` | Requires workflow scope |
| Tests | Unit tests: `TryGetIntFact` (4 types), `GroupBy.Sum` dups, B2 resume skip | `MemorySmith.Agent.Tests/` | Council AC 2,4,5 |
| D1 (S1) | Reconnect delay array: trim dead 32s entry (attempt 5 never used) | `AgentBackgroundService.cs` | Trivial |
| D2 (S1) | Emit ReconnectingEvent to world event stream during backoff | `AgentBackgroundService.cs` | |
| D2 (S2) | MemorySmithItemRegistry: parallel miss race (two HTTP calls on cache miss) | `Agent.Memory/` | |
| Stall | Detect stalled agent loop: warn if no action for >10s with active goal | `AgentBackgroundService.cs` | Council Q6 |
| Facts-persist | Document / implement `BuildProgressIndex` persistence boundary | `Data/Pages/` | Council Q1 |
| Bubble | Add `bubble_column` to LIQUID_BLOCK_NAMES | `MineflayerAdapter/index.js` | Minor |
| AGENTS.md | Add `last-reviewed` header and maintainer | `AGENTS.md` | |

---

## Deferred carry-forward

| ID | Finding | Target |
|----|---------|--------|
| D4 (S2) | `TorchesPerCraft = 4` hardcoded vanilla recipe | future IRecipeRegistry |
| D5 (S7) | Inventory × char in LLM prompt (benign, cosmetic) | Future |
| D6 (S1) | NUnit2058 warning in MockMemoryGatewayTests.cs | Sprint 11 cleanup |
| Atomic-origin | Three-fact auto-origin write is not atomic | Deferred (single-threaded safe for now) |
| Per-blueprint | `maxSlope`, `minArea` per-blueprint configuration | Sprint 12+ |
| Declarative-crafting | Fold `CraftingChainOrder` + `RequiresCraftingTable` into declarative table | Sprint 12+ |
| Typed-facts | `WorldState.Facts: Dictionary<string, object?>` — migrate to typed facts | Phase 7+ |
