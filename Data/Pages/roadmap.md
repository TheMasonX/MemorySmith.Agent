# Roadmap & Sprint History

MemorySmith.Agent uses a sprint-based delivery model. Each sprint is council-reviewed by a 6-seat panel before merge.

**Current version: v0.35.0** | **Latest: Sprint 35 (2026-06-20)**

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

---

## Sprint History (Sprints 5–23)

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

---

## CI Health

| Version | Sprint | Tests | Status |
|---------|--------|-------|--------|
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

| Priority | Item |
|----------|------|
| P0 | `IBuildGoal` marker interface — replace `goal is BuildGoal` type-check in HtnPlanner |
| P1 | Semantic build locations — LLM resolves "build a house in the nearest village" from memory |
| P1 | World KB setup guide and dedicated instance deployment verification |
| P2 | Configurable agent responses — wiki-page-driven response templates (see Sprint 36 feature) |

---

## Future Phases

### Sprint 36 — Configurable Agent Responses (priority: upcoming)

All hardcoded bot chat responses (thinking indicators, navigation replies, stop acknowledgements, task announcements, error messages) should be configurable via a wiki page with named options per response type. See `Data/Pages/Tasks/configurable-agent-responses.md`.

| Priority | Item |
|----------|------|
| P0 | Define response type schema and wiki page format |
| P1 | Wire chat interpreter to read response config |
| P2 | REST API endpoint to query/update response config |

### Phase 4 — Vision & Aesthetics (confidence 0.60)

ISpatialAnalyzer, IVisionModel, `TakeScreenshot` tool, aesthetic critique via Ollama/Gemma.

### Phase 5 — Advanced Features (confidence 0.50)

Multi-agent support, persona plugin, vector embeddings in Memory, multiple LLM providers, CI/CD pipelines.
