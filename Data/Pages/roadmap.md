# Roadmap & Sprint History

MemorySmith.Agent uses a sprint-based delivery model. Each sprint is council-reviewed by a 6-seat panel before merge.

**Current version: v0.23.0** | **Latest: Sprint 23 (2026-06-19)**

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

---

## CI Health

| Version | Sprint | Tests | Status |
|---------|--------|-------|--------|
| v0.23.0 | 23 | 200+ | ✅ green |
| v0.22.0 | 22 | 185+ | ✅ green |
| v0.21.0 | 21 | 171+ | ✅ green |
| v0.20.0 | 20 | 155+ | ✅ green |
| v0.19.0 | 19 | 142+ | ✅ green |

---

## Sprint 24 Priorities (Upcoming)

| Priority | Item |
|----------|------|
| P0 | Integration test for `TryInterruptOnDamage` (was deferred D-8 from Sprint 23) |
| P1 | `GatherGoalDecomposer` TargetCount pass-through fix |
| P1 | `TimeProvider` abstraction for testable time-dependent logic (D-8 Sprint 19) |
| P2 | `IWorldObservationGateway` interface note / design doc (D-5 Sprint 23) |

---

## Future Phases

### Phase 4 — Vision & Aesthetics (confidence 0.60)

ISpatialAnalyzer, IVisionModel, `TakeScreenshot` tool, aesthetic critique via Ollama/Gemma.

### Phase 5 — Advanced Features (confidence 0.50)

Multi-agent support, persona plugin, vector embeddings in Memory, multiple LLM providers, CI/CD pipelines.
