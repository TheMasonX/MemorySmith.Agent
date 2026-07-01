# Roadmap & Sprint History

MemorySmith.Agent uses a sprint-based delivery model. Each sprint is council-reviewed by a 6-seat panel before merge.

**Current version: v0.56.0** | **Latest: Sprint 58 — WorldModel Wiring + P1 Audit Fixes**

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

## Sprint History (Sprints 5–51)

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
| **41** | 2026-06-23 | Placement Hygiene | Block placement quality fixes (blockUpdate timeout, scaffolding prep, terrain clearance, hill detection). Sprint 41 P1-4 deferred. |
| **42** | 2026-06-23 | Checkpoint Verification | Build checkpoint only advances on confirmed BlockPlacedEvent, terrain occupancy skip, BlockPlaceSkipped event, Sprint 42 checkpoint/occupancy tests |
| **43** | 2026-06-23 | Smelt Goal + SearchMemory Removal | SmeltGoal, SmeltGoalDecomposer, SmeltGoalRequest, GoalFactory routing, LLM prompt, ABS handler — full end-to-end smelt route. 15 dead SearchMemory calls stripped from decompositions. |
| **44** | 2026-06-23 | Correctness Sprint | ChatInterpretation.GoalName removed (7-sprint-old zombie), _placeBlockContexts cleanup, 31 new tests (638 total), 5 new tasks (TSK-0082 through TSK-0086) |
| **45** | 2026-06-24 | Audit-Fix Sprint | TSK-0087 (origin typo), TSK-0090 (GetPageTool guard), TSK-0091 (Thread.Sleep→await), TSK-0088 (gateway try/catch), TSK-0094 (blueprint validation), TSK-0092 (null cache TTL), TSK-0089 (nav contract). 644 tests. |
| **46** | 2026-06-24 | Observability First 🔄 | TSK-0100 (WebSocketBridge resilience), TSK-0101 (7 catch→null fixes with logging), TSK-0102 (cross-repo request), TSK-0103 (BuildOrigin), TSK-0104 (ReplanResult), TSK-0105 (doc drift), TSK-0106 (error-path tests) |
| **41** | 2026-06-23 | Intent Reliability + Goto Safety | Intent parsing reliability (ollama 3B insufficient), goto() timeout safety, path_update wiring, stale-inventory guard at goal-creation, 28 core memories, 10 feature wiki pages |
| **49** | 2026-06-25 | Dashboard Wave 1 + Audit Hardening | Dashboard log sink, publisher, REST endpoints, static HTML UI; ActionQueue lock protection; WebSocket clean shutdown; structured tool outcomes (TSK-0110); emergency stop decoupling (TSK-0119); logger wiring (TSK-0120); version drift repair; TSK-0114/0115 test verification (731+ tests) |
| **50** | 2026-06-26 | Dashboard Usability — Waves A/B/C/D | **Wave A:** Rehome-to-origin removed, terrain clearance, self-position block skip, overview UI (live log strip, error/warning badges, position trail, current action, auto-scroll) — commit `153fbd6`<br>**Wave B:** BuildOrigin sentinel elimination (TSK-0107), creative dead code removal (TSK-0116), 7-seat council review — commit `3da01c1`<br>**Wave C:** Landing page redirect, header nav, version badge, SignalR status indicator, uptime counter, uncertainty display, enhanced metrics, about page update, docs update — v0.50.1<br>**Wave D:** MoveToTool context wiring (TSK-0004), dead regex field removal (TSK-0118), Serilog SQLite telemetry sink (TSK-0014), doc updates — v0.50.2 |
| **51** | 2026-06-26 | Audit Synthesis + Runtime Bug Fixes — Waves A/B/B+ | **Wave A (v0.51.0):** 12 canonicalize+classify tasks (bridge registry, doc alignment, regex hardening, deprecation policy, IHttpClientFactory), 5 robustness fixes (Task.WhenAll unwrap, DeathEvent, fault logging, log levels, terminal recovery). NU1903 visible-warning policy. **Wave B (v0.51.1):** Creative build infinite-replan fix, creative inventory fallback in adapter, direct gather recovery for missing materials, Verify-AboutDeps.ps1. **Wave B+:** PlaceBlock observability logging. **Incident:** SQLite CVE — package removed, File sink replaces SQLite, package vetting policy created. |
| **52** | 2026-06-26 | Situational Awareness: Entity Pipeline + ScenePack (planned) | Entity observation in adapter, EntityObservedEvent, WorldState entity projection, LLM scene context, ScenePackBuilder. |
| **53** | 2026-06-27 | Reachability, Motion & Environment Exposure (planned) | Pathfinder events wiring, goto() timeout protection, move event throttling, motion/equipment telemetry, durable memory writer, planner context integration. |
| **54** | 2026-06-28 | Inventory, Chat & Action Lifecycle (planned) | Local world shape, inventory updateSlot, structured message classification, action progress telemetry. |
| **55** | 2026-06-29 | Build Quality + Modularization (complete) | Environment queries (QueryBlocksTool, QueryEntitiesTool), WorldStateDiff, ILlmEvaluator with diff, Observe→Evaluate loop, PlaceBlock schema issue (P0), build dispatch flooding (P1), creative provisioning (P1), GetStatus timeout (P2). v0.55.0. |
| **56** | 2026-06-30 | Council-Driven Fixes (in progress) | Wave A: 6 adapter bug fixes from external audit (harvestTool, recipesFor, vec3 fix, reconnect, auth, ground check, pre-dig). Wave B: TaskSequenceGoal.IsComplete verification, /give command injection fix, chat command deny list, config injection fix, hub auth fix, test debt cleanup. |

---

## CI Health

| Version | Sprint | Tests | Status |
|---------|--------|-------|--------|
| v0.56.0 | 58 | 815 | ✅ green |
| v0.55.0 | 55 | 746 | ✅ green |
| v0.51.1 | 51 | 742 | ✅ green |
| v0.51.0 | 51 | 742 | ✅ green |
| v0.50.2 | 50 | 731+ | ✅ green |
| v0.50.1 | 50 | 731+ | ✅ green |
| v0.50.0 | 49 | 731+ | ✅ green |
| v0.49.0 | 49 | 722 | ✅ green |
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
| S57WD | 57 | 816 | ✅ green |

---

## Sprint Roadmap (In Progress & Planned)

### Sprint 56 — Council-Driven Fixes + TaskSequenceGoal Verification
**Status:** 🟡 In Progress (Wave A complete, Wave B in progress)

| Wave | Theme | Tasks |
|:-----|:------|:------|
| A | Adapter bug fixes from external audit | TSK-0260 through TSK-0266 (harvestTool, recipesFor, vec3 fix, reconnect, auth, ground check, pre-dig) |
| B | Council-driven immediate fixes | TSK-0274 (TaskSequenceGoal.IsComplete verification), TSK-0275 (/give command injection), TSK-0277 (chat command deny list), TSK-0278 (config injection), TSK-0279 (hub auth), TSK-0280 (test debt) |

### Sprint 5✅ Complete (4 waves) | **Handoff:** `Data/Pages/Handoffs/sprint-57-wavec-inventory-handoff.md`

| Wave | Theme | Tasks |
|:-----|:------|:------|
| A | ExecutionContext + Policy Objects | TSK-0289, TSK-0290, TSK-0291, TSK-0294, TSK-0295 |
| B | Known Commands + Block Registry | TSK-0303, TSK-0304 |
| C | Inventory SSOT + PlaceBlock fix | TSK-0296, TSK-0301, TSK-0286, TSK-0302 (partial) |
| D | Audit Synthesis + High-ROI Bug Fixes | TSK-0305 (summon silent failure), TSK-0306 (creative gather/craft/smelt), TSK-0307 (dual sequence advance), TSK-0308 (eval result check) |

**Wave D completed bugs (from 3 external audits):**
| Task | Priority | Summary |
|:-----|:--------:|:--------|
| TSK-0305 | **P0** | ✅ Fix /summon denylist conflict — enqueue blocked response to player |
| TSK-0306 | **P0** | ✅ Fix creative gather/craft/smelt — IsCreativeMode guards in decomposers |
| TSK-0307 | **P1** | ✅ Fix dual TaskSequenceGoal advancement — shared ResetForNextSequenceStep |
| TSK-0308 | **P1** | ✅ Check EvaluationResult.IsSuccess — add _consecutiveLlmEvalFailures counter |

### Sprint 58 — WorldModel Wiring + P1 Audit Fixes + Precondition Implementation
**Status:** 🟢 Waves A+B complete (Wave C deferred) | **Handoff:** `Data/Pages/Handoffs/sprint-58-wavea-b-complete.md`

| Wave | Theme | Tasks |
|:-----|:------|:------|
| A | Quick Wins + P1 Audit Fixes | TSK-0312 (Debug.WriteLine), TSK-0314 (dead code), TSK-0315 (C# sanitization ✓ already done), TSK-0317 (PlaceBlockGoal premature completion), TSK-0318 (denylist normalization), TSK-0319 (CommandExecutionEnabled default) |
| B | WorldModel + Precondition Wiring | TSK-0309 (WorldModel.Predict pre-dispatch), TSK-0310 (IGoalPrecondition on gather/craft/smelt) |
| C | Tool Expansion (deferred) | TSK-0311 (EquipItem, ActivateBlock, AttackEntity, etc.) |

**Wave A+B completed:**
| Task | Priority | Summary |
|:-----|:--------:|:--------|
| TSK-0312 | P2 | ✅ Fix Debug.WriteLine — ActionQueue explicit swallow + HtnPlanner instance logger |
| TSK-0314 | P3 | ✅ Delete IntentAssessment.cs + CreateCreativeBuildActions + test |
| TSK-0315 | P3 | ✅ C# /give sanitization — already done in Sprint 56 (SanitizeBlockName) |
| TSK-0317 | **P1** | ✅ PlaceBlockGoal premature completion — Dispatched incremented per BlockPlacedEvent |
| TSK-0318 | **P1** | ✅ Denylist normalization — config entries normalized to slash-prefixed at startup |
| TSK-0319 | **P1** | ✅ CommandExecutionEnabled default true→false (BREAKING, safe-by-default) |
| TSK-0309 | P2 | ✅ WorldModel.Predict injected into ABS, enriches WorldStateDiff for fire-and-forget tools |
| TSK-0310 | P2 | ✅ IGoalPrecondition on GenericGatherGoal, CraftItemGoal, SmeltGoal |

### Sprint 59 — Sprint 57 Audit Synthesis + Runtime Hardening
**Status:** 🟡 Planned | **Source:** `internal-audit-57-20260701.md` (34 findings from 4-seat council peer review)

**Theme:** Fix P0/P1 findings from the Sprint 57 internal audit that have no existing task coverage. These are high-ROI fixes that close critical correctness gaps in the evaluator, inventory sync, planning async safety, dead-code cleanup, and safety configuration hardening.

| Task | Priority | Source | Summary |
|:-----|:--------:|:------:|:--------|
| TSK-0320 | **P0** | P0-2 | **Fix LlmEvaluator fast-path** — check WorldStateDiff before returning "continue" when all outcomes succeeded |
| TSK-0321 | **P0** | P0-3 | **Fix inventory sync** — remove `_currentGoal is not null` guard, fix stacked-delay bug (sync now fires at 60s instead of 30s) |
| TSK-0322 | **P0** | P0-6 | **Fix ExecutionManager JSON round-trip** — avoid serialize→parse on every dispatch (18-sprint-old deferral) |
| TSK-0323 | **P0** | P0-7 | **Fix HtnPlanner sync-over-async** — deadlock risk in LLM fallback (`.GetAwaiter().GetResult()` on async call) |
| TSK-0324 | **P1** | P1-8 | **Fix safety config merge** — XOR switch replaces default deny list instead of merging (35+ protections lost on partial config) |
| TSK-0325 | **P1** | P1-9 | **Add LLM evaluator circuit breaker** — counter reset-to-0 after 3 failures creates infinite failed-LLM-call loop |
| TSK-0326 | P2 | P2-7 | **Add goal-identity guard** in ProvisionGoalIfCreativeAsync (stale /give enqueue after goal switch) |
| TSK-0327 | P2 | P2-8 | **Fix SafetyConfig runtime normalization** — Program.cs normalizes for LLM prompt but not for runtime gate |
| TSK-0328 | P2 | P2-9 | **Downgrade plan-raw log** — Sprint 52 diagnostic still at Warning level, fires every 10-30s on replan |
| TSK-0329 | P2 | PR-1 | **Fix SignalR event name drift** — live path uses "StatusUpdated", canonical constant is "SnapshotUpdated" |
| TSK-0313 | P2 | — | Implement ThinkAndPlan tool (mid-execution recursive sub-planning) |
| — | — | — | ABS extraction program (TSK-0292/0293 wiring) — deferred to Sprint 60 |

### Sprint 52 — Situational Awareness: Entity Pipeline + ScenePack
**Status:** 🟡 Planned (all tasks Backlog) | **Design:** `Data/Pages/Audit/memorysmith_situational_awareness_design_doc_20260625T020914Z.md`

| Task | Priority | Summary |
|:-----|:--------:|:--------|
| TSK-0146 | High | Entity observation in MineflayerAdapter (periodic scan, 32-block radius) |
| TSK-0147 | High | EntityObservedEvent + EntityDepartedEvent WorldEvents records |
| TSK-0148 | High | Project entity events into WorldState (NearbyEntities, bounded max 50) |
| TSK-0149 | High | Entity summary in LLM system prompt (BuildSystemPrompt) |
| TSK-0150 | High | ScenePackBuilder projection class (compact scene context) |
| TSK-0151 | High | Wire ScenePack into chat pipeline |

**Does not include:** Durable MemorySmith writing (Phase 2 → S53), Planner integration with ScenePack (Phase 3 → S53), Embeddings/graph links (Phase 4 → S54+)

---

### Sprint 53 — Reachability, Motion & Environment Exposure
**Status:** 🟡 Planned (all tasks Backlog) | **Design Refs:** SA design doc Phases 2-3, Mineflayer adapter audit

#### Wave A — Pathfinder Telemetry + Timeout Protection (Critical)

| Task | Priority | Summary |
|:-----|:--------:|:--------|
| TSK-0158 | **Critical** | Wire pathfinder events (path_update, goal_reached, path_stop) |
| TSK-0159 | **Critical** | Promise.race() timeout on all 7 goto() calls (15s default) |
| TSK-0160 | High | Throttle move events (250ms), add yaw/pitch orientation |
| TSK-0161 | High | Motion/equipment/environment telemetry (onGround, heldItem, timeOfDay, etc.) |

#### Wave B — Durable Memory + Planner Integration (SA Phase 2-3)

| Task | Priority | Summary |
|:-----|:--------:|:--------|
| TSK-0152 | Medium | Policy-based MemorySmith writer (goal boundaries, landmarks, failures) |
| TSK-0153 | Medium | Write durable pages to World KB (snapshot, landmark, goal, failure) |
| TSK-0154 | High | Feed ScenePack into planner context |
| TSK-0155 | High | Observation-driven replan comparison loop |

---

### Sprint 54 — Inventory, Chat & Action Lifecycle
**Status:** 🟡 Planned (all tasks Backlog)

| Task | Priority | Summary |
|:-----|:--------:|:--------|
| TSK-0162 | High | Local world shape: block underfoot, block in front, light level, hazards |
| TSK-0163 | High | Inventory updateSlot real-time slot-level ground truth |
| TSK-0164 | Medium | Chat structured message classification (messageKind field) |
| TSK-0165 | Medium | Action progress telemetry (started/progress/failed with reason codes) |

---

### Sprint 55 — Modularization + Cleanup
**Status:** 🟡 Planned (all tasks Backlog)

| Task | Priority | Summary |
|:-----|:--------:|:--------|
| TSK-0166 | Medium | Modularize MineflayerAdapter (~1500 lines → 15+ focused modules) |
| TSK-0167 | Low | Fix documentation version/sprint drift across README, roadmap, handoffs |
| TSK-0168 | Low | Remove HtnPlanner legacy typed decomposition branches |

---

### Future Phases (No Sprint Assigned)

| Phase | Summary | Blockers | Confidence |
|:------|:--------|:---------|:----------:|
| **Vision & Aesthetics** (Phase 6) | ISpatialAnalyzer, IVisionModel, TakeScreenshot tool, aesthetic critique via Ollama. TSK-0005 (SpatialAnalyzer) exists in Backlog. | Needs stable entity pipeline first | 0.60 |
| **Advanced Features** (Phase 7) | Multi-agent support, persona plugin, vector embeddings (TSK-0156), graph links (TSK-0157), multiple LLM providers, CI/CD pipelines | Blocked on MemorySmith backend (TSK-0156/0157) | 0.50 |

---

### Deferred from Sprint 51 — Still Pending (Ready, Not Started)

These are high-value, well-scoped tasks deferred from S51 that should be picked up before or alongside new sprint work.

| Task | Priority | Summary | Est. |
|:-----|:--------:|:--------|:----:|
| **TSK-0144** | **Critical** | Enforce package vetting policy in CI (`dotnet list package --vulnerable` fails build) | 2 hrs |
| **TSK-0145** | High | Run Verify-AboutDeps.ps1 in CI | 1.5 hrs |
| **TSK-0134** | High | DI startup failure logging + health check endpoints | 30 min |
| **TSK-0133** | High | Fix parameter preservation on replan (remaining count lost) | 45 min |
| **TSK-0132** | High | Fix page search Score=0.0 under-ranking | 30 min |
| **TSK-0137** | Medium | Fix consecutive failure guard reset on partial progress | 20 min |
| **TSK-0004** | High *(InProgress)* | Context carry: per-tool allowlist for context→Arguments merge | 15+ min |
| **TSK-0121** | **Critical** *(Backlog, reopened)* | Rehome-to-origin after every block — walks back to origin between placements | — |

---

### Stale Backlog Items (No Current Sprint)

| Task | Priority | Summary | First Mentioned |
|:-----|:--------:|:--------|:--------------:|
| TSK-0082 | High | Extract shared SmeltableMapping class | Sprint 44 |
| TSK-0093 | Medium | Structured ParseItemSpec result (NotFound vs Malformed) | Sprint 44 |
| TSK-0118 | Medium | Chat split-brain cleanup — dead `ChatInterpreter` regex fields | Sprint 47+ |
| TSK-0005 | Medium | Implement SpatialAnalyzer.cs in Agent.Vision | Sprint 0 |
| TSK-0013 | Medium | Add ListBlocks/ListItems tool for in-game block discovery | Sprint 0 |
| TSK-0169 | Medium | Chat context dashboard (expandable request/response viewer) | Sprint 51 B+ |
| TSK-0170 | Medium | Dashboard UI improvements (auto-scaling, goal setting, live log scroll) | Sprint 51 B+ |

---

## Gap Analysis: Items Planned in Documents But Missing from Tasks

The following items are mentioned across handoffs, sprint plans, and roadmap docs but have NO corresponding task record. They are at risk of being dropped.

| Item | Source Document | Proposed Priority | Notes |
|:-----|:----------------|:-----------------:|:------|
| Upgrade LLM model from 3B to 7B+ | Roadmap Upcoming Priorities (P0), Sprint 41 | **P0** | No task exists. Intent parsing reliability is the #1 runtime issue with ollama 3B. |
| IBuildGoal marker interface | Roadmap Upcoming Priorities (P1) | P1 | Replace `goal is BuildGoal` type-check in HtnPlanner with marker interface |
| Semantic build locations (LLM resolves from memory) | Roadmap Upcoming Priorities (P2) | P2 | "Build a house in the nearest village" pattern |
| World KB setup guide + dedicated instance deployment | Roadmap Upcoming Priorities (P2) | P2 | Deploy and verify a dedicated MemorySmith wiki instance for item/blueprint lookup |
| Configurable agent responses (wiki-page-driven templates) | Roadmap Upcoming Priorities (P2) | P2 | Response templates stored in wiki pages |
| Dashboard event bus | Roadmap Upcoming Priorities (P1), Sprint 42 Future Phases | P1 | Decouple publishing from agent loop (old TSK-0042-0046 numbering in MemorySmith repo) |
| Remove MoveTo-to-origin from replans (TSK-0121 fix) | Sprint 51 B+ handoff (Critical) | **Critical** | TSK-0121 exists but was marked Done then reopened. The original fix was claimed in S50 Wave A but the user confirms it's still happening. |
| GOAP planner evaluation | Sprint 51 Wave A (explicitly out of scope for S51-52), Sprint 54+ | Medium | HTN is sufficient currently; GOAP evaluation slated for Sprint 54+ |
| E2E game test (GatherWood) | Sprint 50 handoff (TSK-0003, High) | High | Requires Minecraft server + Mineflayer. No task in Agent repo's task store. |
| **Unified build decomposition (Creative+Survival)** | Multiple audits, council S52 plan, user request | **High** | Today `HtnTaskLibrary.DecomposeBuild` has a ~300-line `if/else` branch separating creative and survival. Most logic (origin, placement loop, checkpoint, vegetation clearing) is shared. Should extract `BuildActionPipeline` with injectable material provider. See `Data/Pages/Audit/survival-creative-build-decomposition-analysis.md`. |
| **HtnTaskLibrary split** | Council S52 plan | High | Council planned 5 focused decomposers (Gather, Craft, Smelt, Build, Explore). No task exists. Current S52 tasks (TSK-0146-0151) are entity awareness only. |
| **Typed PlanContext** | Council S52 plan | Medium | Replace `Dictionary<string, object?>` context with typed properties. |
| **IBuildGoal marker interface** | Roadmap Upcoming Priorities (P1) | P1 | Replace `goal is BuildGoal` type-check in HtnPlanner with marker interface |

---

## Knowledge Base Status

| Artifact | Count | Status |
|----------|-------|--------|
| Core memories (JSON) | 28 | 15 new + 13 existing = 28 covering all critical areas |
| Feature wiki pages | 10 | Agent Runtime, Chat Interpretation, Planning, Blueprints, World Events, Adapter, Safety, Memory/Wiki, Dashboard, Emergency Stop |
| Task records | 70+ | tsk-0001 through tsk-0170 (Agent: 55 active; MemorySmith: legacy numbering) |
| Guides | 18+ | Getting started, adding goals/tools, API, troubleshooting, etc. |
| Council reviews | 20+ | Sprint 0 through Sprint 38 |
| Blueprint files | 4 | small-house, farm, castle, wizards-tower |
| Item registry entries | 80+ | All craftable Minecraft items
