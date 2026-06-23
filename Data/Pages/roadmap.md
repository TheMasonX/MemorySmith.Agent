# Roadmap & Sprint History

MemorySmith.Agent uses a sprint-based delivery model. Each sprint is council-reviewed by a 6-seat panel before merge.

**Current version: v0.40.0** | **Latest: Sprint 41 (in progress)**

---

## Completed Phases

### Phase 0 — Skeleton ✅ COMPLETE (2026-06-15)

All interfaces defined, solution structure, wiki pages, CI workflow, NUnit test project.

### Phase 1 — Core Agent MVP ✅ COMPLETE

AgentHost, WebSocket bridge, basic movement tools, goal/action loop, Blazor status.

### Phase 2 — Memory Integration ✅ COMPLETE

`IMemoryGateway`, `RestMemoryGateway`, MemorySmith connection, memory tool implementations.

### Phase 3 — HTN/GOAP Planner ✅ COMPLETE

`HtnPlanner`, goal decomposition, task library, `GoalFactory`, `IPlanner.ReplanAsync`.

### Phase 4 — Advanced Adapter & Runtime ✅ COMPLETE (Sprint 40)

ActionData.Context bag, WebSocketBridge background receive loop, findBestBlock three-pass scorer, BLOCK_MINING_ALIASES, graduated stall retry [10,20,30,60]s, kick→reconnect verified E2E, emergency stop, stopComplete/MineAborted events, ReachableBlockFoundEvent parsing.

### Phase 5 — Intent Reliability & Adaptive Execution 🔄 IN PROGRESS (Sprint 41)

Intent parsing reliability (ollama 3B insufficient), goto() timeout safety, path_update wiring, stale-inventory guard at goal-creation, blueprint alias resolution in LLM path, MemorySmithBlueprintRepository logging.

---

## Sprint History (Sprints 5–41)

| Sprint | Date | Theme | Key Deliverables |
|--------|------|-------|-----------------|
| **5** | 2026-06-16 | Tool Safety & Memory Lifecycle | ToolDispatcher validation, /api/agent/command lockdown, WorldState.Facts capped 1000, Fact record, context-preserving replan, 30s action timeout, FailureReason enum |
| **6** | 2026-06-17 | Journal, World Model, Decomposers | IAgentJournal (1000 entries, 11 types), IWorldModel (predict/reconcile/uncertainty), DecomposerRegistry, Build/Gather/SurviveNight decomposers, PlannerRouter |
| **11** | 2026-06-17 | Chat Observability + Correctness | CraftRegex fast-path (no LLM for craft/forge/smelt), LLM 10s timeout, thinking indicator log, intent log, requireOrigin flag |
| **17** | 2026-06-17 | Resolver Growth | ClassifySpec ore-drop fix, WorldFact third resolver source (0.70/0.50 confidence), /api/agent/resolve curl examples |
| **19** | 2026-06-18 | Logging + Planner Fixes | Serilog file sink, JS logStructured, 9 SYSTEM_MESSAGE_PATTERNS, gather plan rework (SearchMemory→MineBlock→GetStatus), replan governor (ACTIVE/STALLED, 3-fingerprint threshold), findFlatArea radius 32→48 retry |
| **20** | 2026-06-18 | Runtime Failure Recovery | Progress-hash governor (inventory-delta), 3 new system message patterns, TryParseTruncatedJson, OllamaProvider num_predict=300 |
| **21** | 2026-06-18 | Inventory Freshness + Governor Pre-Plan | IsInventoryStale flag, governor pre-plan IsStalled check (10s delay in STALL), D-2 BlockNotFoundEvent integration tests |
| **22** | 2026-06-18 | Planner Completeness + World KB | CraftItemGoal.IsComplete staleness gate, HtnPlanner IItemSpecGoal count fix, health-critical threshold, World KB separation (WorldKbUrl, named HttpClient, AddKeyedSingleton) |
| **23** | 2026-06-19 | Damage Interrupt + World KB Routing | DamageTakenEvent, per-goal DamageInterruptThresholdHp, ActionQueue.ClearAndEnqueue atomic, SearchMemory/CreatePage → World KB, GetPage → Agent KB, WorldKbUrl null default + startup warning |
| **24** | 2026-06-19 | *Planned — not implemented* | FindFlatAreaTool defaults sync, StatusTool dedup, Action Correlation IDs, TryInterruptOnDamage test. All items absorbed into Sprint 25. |
| **25** | 2026-06-19 | Tool Boundary Hardening + Action Lifecycle | FindFlatAreaTool defaults unified, StatusTool deleted (GetStatusTool alias), ToolDispatcher exception wrapping + TryGetInt32, Action correlation IDs (PendingAction/ActionLifecycle/ConcurrentDictionary), WorldModel defensive copy |
| **26** | 2026-06-19 | Damage Interrupt Tests + TargetCount Fix + Audit Intake | TryInterruptOnDamage integration tests (5), IItemSpecGoal.TargetCount DIM fix + GatherGoalDecomposer + HtnPlanner (3 tests), external audit intake with annotations |
| **27** | 2026-06-19 | ITimeProvider + Planner Routing Consolidation | AgentBackgroundServiceTestHelper, version unified to v0.27.0, ITimeProvider (SystemTimeProvider + FakeTimeProvider), PlannerRouter as IPlanner, CraftItemGoalDecomposer routing consolidated |
| **28** | 2026-06-20 | External Audit Synthesis + Replan Fix | BuildGoalDecomposer ReadOriginFact LogWarning, GenericGatherGoal HasFailed + targetCount, PlannerRouter.ReplanAsync originalGoal fix, architecture.md journal semantics |
| **29** | 2026-06-20 | Audit Sprint — No Production Code | 4 new audit files filed, base64 sweep incomplete (WorldStateProjector.cs + ToolDispatcher.cs still encoded), version bump deferred |
| **30** | 2026-06-20 | Base64 Decode + ITool Compliance + SEC-01 | WorldStateProjector.cs + ToolDispatcher.cs decoded, SearchMemoryTool/CreatePageTool ITool compliance fixed, version v0.28.0, ApiKeyMiddleware wired, ChatInterpreter regex fixes |
| **31** | 2026-06-20 | Council Review + Audit Synthesis | BLK-01 (BuildGoalDecomposer DI arity) re-confirmed, BLK-02 (possible base64 re-encoding) identified, no new code |
| **32** | 2026-06-20 | Build Restoration + SEC-02 + Quality Fixes | 5 C# files + index.js decoded, BLK-01 fixed (ILogger passed to BuildGoalDecomposer), SEC-02 adapter shared secret, ApiKeyMiddleware tests, SetFact [Obsolete], Rule E-2 documented |
| **33** | 2026-06-20 | DI Logger Wiring + Base64 Sweep + Rule E-2 | BLK-S33-01 Program.cs restored, GoalFactory + HtnPlanner ILogger wired, /api/about phase updated, TestHost added, SetFact migration (6 sites), README.md decoded |
| **34** | 2026-06-20 | Build Verification + Final Base64 Sweep | Build gate verification, comprehensive .cs base64 sweep, WebApplicationFactory entrypoint check — handoff only, no council review |
| **35** | 2026-06-20 | Build Origin + API Auth + Connect Announcement | API auth fix (MEMORYSMITH_API_KEY env var, WorldKbUrl→null fallback), chat announcement on connect, build origin coordinate system (three-tier resolution: explicit→facts→auto-detect FindFlatArea), LLM build intent coords passthrough |
| **36** | 2026-06-21 | Configurable Responses + Inventory Event Sourcing | Configurable agent responses via wiki pages, ActionOutcome record, ItemCollectedEvent (Sprint 35 P0-A), mineComplete event contract, playerCollect guard, IItemSpecGoal/SurviveNightGoal wiring, inventory report chat tool |
| **37** | 2026-06-21 | Service Lifetime + LLM Planning + Agent Greeting | Service lifetime fixes, LLM planning path, agent greeting on connect, plan display RLE, build task resource counts, task chat updates |
| **38** | 2026-06-22 | LLM-First Architecture, ActionOutcome, Correlation | Chat→IntentDraft→Planner→Goal pipeline locked (AGENTS.md Rule A-1), ParseDecision goal-name switch removed, IntentManager maps intents to GoalRequests, ActionOutcome universal result type, _cycleOutcomes, ILlmEvaluator interface stub, IItemSpecGoal/SurviveNightGoal cleanup, ItemConsumedEvent (P4-A) |
| **39** | 2026-06-22 | Build Pipeline Fixes + Blueprint Alias Resolution | BlueprintAliases added to IntentManager (Sprint 41 P1 fix), MemorySmithBlueprintRepository ILogger + per-stage logging, GoalFactory blueprint-not-found warnings, HandleChatEventAsync intent logging |
| **40** | 2026-06-23 | P0/P1 Fix Package — Adapter Stability | findBestBlock() three-pass scorer (same-Y→nearby→fallback), BLOCK_MINING_ALIASES (dirt←grass_block), MAX_DIG_FAILURES=3, graduated stall retry [10,20,30,60]s, kick→reconnect E2E verified, stopComplete/MineAborted/ReachableBlockFoundEvent wiring, configurable constants in index.js |
| **41** | 2026-06-23 (active) | Intent Reliability + Goto Safety | Intent parsing reliability (ollama 3B insufficient), goto() timeout safety, path_update wiring, stale-inventory guard at goal-creation, 28 core memories, 10 feature wiki pages |

---

## CI Health

| Version | Sprint | Tests | Status |
|---------|--------|-------|--------|
| v0.40.0 | 40 | 63+ | ✅ green |
| v0.35.0 | 35 | 501 (498 passed, 3 pre-existing fails) | ✅ green |
| v0.28.0 | 33 | 276+ | ✅ green |
| v0.27.0 | 27 | 244+ | ✅ green |
| v0.26.0 | 26 | 230+ | ✅ green |
| v0.25.0 | 25 | 220+ | ✅ green |
| v0.23.0 | 23 | 200+ | ✅ green |
| v0.22.0 | 22 | 185+ | ✅ green |
| v0.21.0 | 21 | 171+ | ✅ green |
| v0.20.0 | 20 | 155+ | ✅ green |
| v0.19.0 | 19 | 142+ | ✅ green |

---

## Upcoming Priorities

| Priority | Item | Category |
|----------|------|----------|
| P0 | Upgrade LLM model from llama3.2:3b to 7B+ for reliable intent parsing | Intent Reliability |
| P0 | Goto() timeout safety — prevent infinite pathfinding | Adapter Stability |
| P1 | Structured chat message classification (TSK-0038) — replace fragile regex filters | Chat System |
| P1 | Dashboard event bus (TSK-0042-0046) — decouple publishing from agent loop | Dashboard |
| P1 | `IBuildGoal` marker interface — replace `goal is BuildGoal` type-check in HtnPlanner | Planning |
| P2 | Semantic build locations — LLM resolves "build a house in the nearest village" from memory | Planning |
| P2 | World KB setup guide and dedicated instance deployment verification | Memory |
| P2 | Configurable agent responses — wiki-page-driven response templates | Chat System |

---

## Future Phases

### Sprint 42 — Dashboard Decoupling + Chat Reliability

- Dashboard event bus implementation (TSK-0041 through TSK-0046)
- LLM model upgrade evaluation
- Structured chat classification (TSK-0038)
- path_update wiring for real-time movement tracking

### Phase 6 — Vision & Aesthetics (confidence 0.60)

ISpatialAnalyzer, IVisionModel, `TakeScreenshot` tool, aesthetic critique via Ollama/Gemma. Vision subsystem code already exists in `Agent.Vision` project.

### Phase 7 — Advanced Features (confidence 0.50)

Multi-agent support, persona plugin, vector embeddings in Memory, multiple LLM providers, CI/CD pipelines.

## Knowledge Base Status

| Artifact | Count | Status |
|----------|-------|--------|
| Core memories (JSON) | 28 | 15 new + 13 existing = 28 covering all critical areas |
| Feature wiki pages | 10 | Agent Runtime, Chat Interpretation, Planning, Blueprints, World Events, Adapter, Safety, Memory/Wiki, Dashboard, Emergency Stop |
| Task records | 46 | tsk-0001 through tsk-0046 |
| Guides | 18+ | Getting started, adding goals/tools, API, troubleshooting, etc. |
| Council reviews | 20+ | Sprint 0 through Sprint 38 |
| Blueprint files | 4 | small-house, farm, castle, wizards-tower |
| Item registry entries | 80+ | All craftable Minecraft items
