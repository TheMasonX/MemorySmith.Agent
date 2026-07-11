# Roadmap & Sprint History

MemorySmith.Agent uses a sprint-based delivery model. Each sprint is council-reviewed by a 6-seat panel before merge.

**Current version: v0.60.0** | **Latest: Sprint 59 — Entity awareness, inventory reliability, task governance**

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

### Phase 5 — Intent Reliability & Adaptive Execution ✅ COMPLETE (Sprint 45)

Intent parsing reliability (ollama 3B insufficient), goto() timeout safety, path_update wiring, stale-inventory guard at goal-creation, blueprint alias resolution in LLM path, MemorySmithBlueprintRepository logging.

### Phase 6 — Observability First ✅ COMPLETE (Sprint 49)

Silent-failure hardening: structured logging across all catch→null paths, WebSocketBridge receive loop resilience with auto-reconnect, BuildOrigin consolidation, ReplanResult typed outcomes, documentation drift repair. Theme: "make every failure observable."

### Phase 7 — Dashboard & Audit Hardening ✅ COMPLETE (Sprint 49)

Dashboard infrastructure (log sink, publisher, REST endpoints, static HTML UI), ActionQueue lock protection, WebSocket clean shutdown, structured tool outcomes (TSK-0110), emergency stop delivery resilience (TSK-0119).

### Phase 8 — Dashboard Usability ✅ COMPLETE (Sprint 50)

Dashboard Wave A (build placement fixes + overview UI), Wave B (BuildOrigin migration + creative cleanup + council review), Wave C (landing page, navigation, status panel enhancement, version bump to v0.50.1), Wave D (context wiring, chat cleanup, SQLite telemetry, version bump to v0.50.2).

### Phase 9 — Audit Synthesis + Runtime Hardening ✅ COMPLETE (Sprint 51)

**Wave A** (v0.51.0): Canonicalize & classify (12 tasks: bridge classification, doc alignment, SearchMemoryTool regex, SearchResult.Kind, deprecation policy, IHttpClientFactory, MakeAction freeze, breaking changes doc, UpdatePageAsync fix, NU1903 policy reform, LlmChatInterpreter doc fix, task sync). Harden robustness (5 tasks: Task.WhenAll unwrap, DeathEvent handler, fault logging, logging levels, terminal recovery).

**Wave B** (v0.51.1): Creative build infinite-replan fix (creative inventory fallback in adapter, non-progress tools removed from failure reset, direct gather recovery for missing materials). Verify-AboutDeps.ps1 script created.

**Wave B+**: PlaceBlock observability logging. Creative mode recovery guards: don't gather materials when adapter handles creative inventory. Build checkpoint advances past bot-position skips to fix origin infinite-loop. MoveTo early-exit when already at target. `IsProgressSignalTool` narrowed (removed MoveTo/Wander/FindFlatArea). ChatHistory MaxTurns increased 5→30, configurable.

**Incident:** SQLitePCLRaw CVE — package removed, File sink replaces SQLite sink. Package vetting policy (P-1 through P-5) created and enforced.

---

## Sprint History (Compact — Sprints 5–56)

### Phases 0–3: Foundation (Sprints 5–23, 2026-06-15 → 2026-06-19)

Tool safety, memory lifecycle, journal/worldmodel/decomposers, HTN planner, endpoint auth, adapter WebSocket bridge, basic action loop, `IMemoryGateway`/`RestMemoryGateway`, `HtnPlanner`/`GoalFactory`, inventory freshness, damage interrupt, World KB routing. Tests: ~142→200+.

### Phases 4–5: Adapter Stability & Intent Reliability (Sprints 24–45, 2026-06-19 → 2026-06-24)

Action correlation, `ITimeProvider`, planner routing consolidation, external audit synthesis, base64 decode sweep, DI wiring, build origin, LLM-first architecture pipeline lock, ActionOutcome, blueprint alias resolution, adapter P0/P1 fixes (3-pass scorer, mining aliases, graduated stall retry, kick→reconnect), smelt goal, SearchMemory dead call removal, 7 audit-fix tasks. Tests: 200→644.

### Phases 6–8: Observability & Dashboard (Sprints 46–50, 2026-06-24 → 2026-06-26)

Observability-first hardening (WebSocketBridge resilience, 7 catch→null fixes, BuildOrigin, ReplanResult), Dashboard (log sink, publisher, REST, HTML UI, ActionQueue lock, emergency stop decoupling, overview UI, build origin elimination, landing page, nav, version badge, SignalR, MoveToTool context). Tests: 731+. Versions: v0.49.0→v0.50.2.

### Phase 9: Audit Synthesis + Runtime Hardening (Sprint 51, 2026-06-26 — v0.51.0/v0.51.1)

**Wave A:** 12 canonicalize+classify tasks, 5 robustness fixes. **Wave B:** Creative build infinite-replan fix, creative inventory fallback. **Incident:** SQLite CVE — package removed, File sink replaces SQLite, package vetting policy (P-1–P-5). Tests: 742.

### Sprint 52 — Situational Awareness: Entity Pipeline + ScenePack (planned — partially deferred)
Entity observation, EntityObservedEvent, WorldState entity projection, LLM scene context, ScenePackBuilder. Entity observation implemented; durable writer and planner integration deferred.

### Sprint 53 — Reachability, Motion & Environment (planned — deferred)
Pathfinder events wiring, goto() timeout protection, move event throttling, motion/equipment telemetry.

### Sprint 54 — Inventory, Chat & Action Lifecycle (planned — deferred)
Local world shape, inventory updateSlot, structured message classification, action progress telemetry.

### Sprint 55 — Build Quality + Modularization (2026-06-29 — v0.55.0)
Environment queries (QueryBlocksTool, QueryEntitiesTool), WorldStateDiff, ILlmEvaluator with diff, Observe→Evaluate loop, PlaceBlock schema fix (P0), build dispatch flooding fix (P1). Tests: 746.

### Sprint 56 — Council-Driven Fixes (2026-06-30 — v0.56.0)
| Wave | Theme | Status |
|:-----|:------|:------:|
| A | 6 adapter bug fixes from external audit (harvestTool, recipesFor, vec3, reconnect, auth, ground check, pre-dig) | ✅ Done |
| B | TaskSequenceGoal verification, /give injection, deny list, config injection, hub auth, test debt | ✅ Done |

---

## CI Health

| Version | Sprint | Tests | Status |
|---------|--------|-------|--------|
| v0.60.0 | 60 | 822 | ✅ green (Wave A+B+D/E partial) |
| v0.56.0 | 56–58 | 815+ | ✅ green |
| v0.55.0 | 55 | 746 | ✅ green |
| v0.51.1 | 51 | 742 | ✅ green |
| v0.50.2 | 50 | 731+ | ✅ green |
| v0.49.0 | 49 | 722 | ✅ green |
| v0.40.0 | 40 | 63+ | ✅ green |
| v0.35.0 | 35 | 501 | ✅ green (3 pre-existing fails) |
| v0.28.0 | 33 | 276+ | ✅ green |
| v0.23.0 | 23 | 200+ | ✅ green |
| v0.19.0 | 19 | 142+ | ✅ green |

---

## Sprint Roadmap — Active & Planned

### Sprint 57 — ExecutionContext + Audit Synthesis (2026-07-01)
**Status:** 🟢 Complete | Handoff: `Data/Pages/Handoffs/sprint-57-wavec-inventory-handoff.md`

| Wave | Theme | Status |
|:-----|:------|:------:|
| A | ExecutionContext + Policy Objects (TSK-0289/0290/0291/0294/0295) | ✅ Complete |
| B | Known Commands + Block Registry (TSK-0303/0304) | ✅ Complete |
| C | Inventory SSOT + PlaceBlock fix (TSK-0296/0301/0286, TSK-0302 partial) | ✅ Complete |
| D | Audit Synthesis + Bug Fixes (TSK-0305/0306/0307/0308) | ✅ Complete |

### Sprint 58 — WorldModel Wiring + P1 Audit Fixes (2026-07-01)
**Status:** 🟢 Complete (Wave C deferred) | Handoff: `Data/Pages/Handoffs/sprint-58-wavea-b-complete.md`

| Wave | Theme | Status |
|:-----|:------|:------:|
| A | Quick Wins + P1 Fixes (TSK-0312/0314/0317/0318/0319) | ✅ Complete |
| B | WorldModel + Precondition Wiring (TSK-0309/0310) | ✅ Complete |
| C | Tool Expansion (TSK-0311 — EquipItem, ActivateBlock, AttackEntity) | 🔙 Deferred |

### Sprint 59 — Sprint 57 Audit Fixes (2026-07-01)
**Status:** 🟢 Mostly Complete (1 task remaining)

| Task | Priority | Summary | Status |
|:-----|:--------:|:--------|:------:|
| TSK-0320 | **P0** | Fix LlmEvaluator fast-path — check WorldStateDiff before "continue" | ✅ Done |
| TSK-0321 | **P0** | Fix inventory sync — remove guard, fix 60s stacked delay | ✅ Done |
| TSK-0322 | **P0** | Fix ExecutionManager JSON round-trip type fidelity loss | 🔙 Backlog |
| TSK-0323 | **P0** | Fix HtnPlanner sync-over-async deadlock risk | ✅ Done |
| TSK-0324 | **P1** | Fix safety config merge — XOR switch erodes default deny list (35+ protections) | ✅ Done |
| TSK-0325 | **P1** | Add LLM evaluator circuit breaker | ✅ Done |
| TSK-0326–0329 | P2 | Goal-identity guard, SafetyConfig runtime normalization, log levels, SignalR event name drift | ✅ Done |

### Sprint 60 — Architectural Stability & Legacy Cleanup 🎯 **CURRENT**
**Status:** 🟢 Wave D/E/F partial complete | **Theme:** Remove legacy fallback paths, reduce tech debt surface, harden core pipelines

This sprint focuses on architectural stability — removing deprecated/legacy fallbacks, refactoring fragile subsystems, and cleaning up tech debt accumulated across 50+ sprints.

| Wave | Theme | Tasks | Priority | Status |
|:-----|:------|:------|:--------:|:------:|
| A | **Legacy Fallback Removal** | TSK-0293 (remove legacy planning/runtime shims), TSK-0284 (migrate Facts writers to StructuredFacts), TSK-0082 (extract shared SmeltableMapping), TSK-0118 (remove dead ChatInterpreter regex fields) | **High** | ✅ Complete |
| B | **Core Pipeline Hardening** | TSK-0322 (ExecutionManager JSON round-trip — P0), TSK-0345 (replan flooding fix), TSK-0348 (WorldModel inventory/ActionOutcome wiring) | **Critical** | ✅ Complete |
| C | **Audit Synthesis Tasks** | TSK-0346 (adapter goto timeout gap), TSK-0347 (restore search scoring), TSK-0349 (rotate secrets + secret scanning), TSK-0350 (global antiforgery filter) | **High** | ✅ Complete |
| D | **Inventory SSOT + BlockState** | TSK-0302 (inventory SSOT refactor — event-sourced, remove IsInventoryStale), TSK-0245 (assess BlockState type change impact) | **Critical** | ✅ Complete |
| D | **High-ROI Safe Fixes (Wave 1)** | TSK-0406 (craft/smelt stop), TSK-0402 (ABS silent catches), TSK-0403 (SignalR log level), TSK-0134 (DI startup logging), TSK-0144 (CI package vetting), TSK-0145 (CI Verify-AboutDeps) | **High/Critical** | ✅ Complete |
| E | **Ready Backlog Cleanup** | TSK-0133 (replan param preservation), TSK-0132 (page search score fix), TSK-0271 (build dispatch sequencing), TSK-0293 (remove legacy shims) | High | 🔵 Planned |
| F | **Post-Wave-C Audit Fixes (Remaining)** | **Critical:** TSK-0383/0390 (LLM prompt injection). **High:** TSK-0392 (ToolResult Success/Outcome), TSK-0393 (ActionData mutable dicts), TSK-0396 (5 JS events ParseEvent), TSK-0397/0398 (over-mining fixes), TSK-0400 (MaxTokens config), TSK-0401 (memory gateway error handling), TSK-0404 (DashboardPublisherImpl), TSK-0405 (goto timeout 9+ calls), TSK-0407/0408 (rate limiting) | **Critical/High** | 🔵 Ready |
| Note | **Duplicates resolved** | TSK-0380/0387 (sendEvent crash), TSK-0381/0388 (unhandledRejection), TSK-0382/0389 (pipe buffer), TSK-0384/0407 (BuildGoal.Id), TSK-0386/0408 (rate limiting) — keep Done copies, archive Backlog duplicates | — | ✅ |

---

### Sprint 61 — Situational Awareness, Tool Expansion & Audit Remediation
**Status:** 🔵 Planned | Theme: Entity pipeline, missing tools, durable memory, post-audit remediation

| Wave | Theme | Candidate Tasks |
|:-----|:------|:----------------|
| A | **Entity Pipeline Wiring** | TSK-0343 (EntityObservedEvent → StructuredFacts), TSK-0146/0147/0148 (entity observation, events, WorldState projection) |
| B | **Missing Tools** | TSK-0311 (EquipItem, ActivateBlock, AttackEntity), TSK-0299 (player coords in LLM context) |
| C | **Adapter Hardening** | TSK-0337 (WebSocketBridge reconnect retry), TSK-0339 (adapter reconnect backoff), TSK-0364 (over-mining), TSK-0365 (HasFailed) |
| D | **Runtime Decomposition Cleanup** | TSK-0369 (remove dead runtime decomposition — P0, S61 deferred), TSK-0292 (DashboardPublisherImpl cleanup), TSK-0374 (memory gateway error handling) |
| E | **Test Infrastructure** | TSK-0379 (e2e integration test), TSK-0376 (WebSocketBridge tests), TSK-0191 (LLM provider tests) |
| F | **Durable Memory Writer** | TSK-0152/0153 (policy-based world KB writer for goals/landmarks/failures) |

---

### Sprint 62 — Pathfinder, ScenePack & Autonomy
**Status:** 🔵 Planned | Theme: Reachability, motion telemetry, planner integration

| Wave | Theme | Candidate Tasks |
|:-----|:------|:----------------|
| A | Pathfinder Telemetry | TSK-0158/0159 (path events wiring, goto timeout), TSK-0160/0161 (move/equipment telemetry) |
| B | ScenePack Integration | TSK-0149/0150/0151 (LLM scene context, ScenePackBuilder, chat pipeline wiring) |
| C | Autonomy Dashboard | TSK-0248 (minimal autonomy dashboard), TSK-0249 (RuntimeSignalSink) |
| D | FollowUpPolicy | TSK-0247 (FollowUpPolicy vs TaskSequenceGoal contract) |

---

### Future Phases (No Sprint Assigned)

| Phase | Summary | Blockers | Confidence |
|:------|:--------|:---------|:----------:|
| **Vision & Aesthetics** | ISpatialAnalyzer, IVisionModel, TakeScreenshot tool, aesthetic critique via Ollama | Needs stable entity pipeline first | 0.60 |
| **Advanced Features** | Multi-agent support, persona plugin, vector embeddings, graph links, multiple LLM providers, CI/CD | Blocked on MemorySmith backend | 0.50 |

---

### Gap Analysis → New Task Candidates

The following documented items now have task records created during the 2026-07-10 roadmap triage:

| Item | Task | Priority |
|:-----|:----:|:--------:|
| **LLM model upgrade 3B→7B+** | TSK-0351 | **Critical** |
| **IBuildGoal marker interface** | TSK-0352 | High |
| **Unified build decomposition (Creative+Survival)** | TSK-0353 | High |
| **HtnTaskLibrary split** | TSK-0354 | High |
| **Typed PlanContext** | TSK-0355 | Medium |
| **E2E game test (GatherWood)** | TSK-0356 | High |
| **Dashboard event bus** | TSK-0357 | High |
| **World KB setup guide + deployment** | TSK-0358 | Medium |

---

## Knowledge Base Status

| Artifact | Count | Status |
|----------|-------|--------|
| Core memories (JSON) | 28 | 15 new + 13 existing = 28 covering all critical areas |
| Feature wiki pages | 10 | Agent Runtime, Chat Interpretation, Planning, Blueprints, World Events, Adapter, Safety, Memory/Wiki, Dashboard, Emergency Stop |
| Task records | 80+ | tsk-0001 through tsk-0358 (Agent: 80+ active; MemorySmith: legacy numbering) |
| Guides | 18+ | Getting started, adding goals/tools, API, troubleshooting, etc. |
| Council reviews | 20+ | Sprint 0 through Sprint 38 |
| Blueprint files | 4 | small-house, farm, castle, wizards-tower |
| Item registry entries | 80+ | All craftable Minecraft items
