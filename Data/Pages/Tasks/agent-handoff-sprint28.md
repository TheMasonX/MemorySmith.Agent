# Agent Handoff — Sprint 28
**Date**: 2026-06-20  
**Branch**: `sprint-5-tool-safety` (PR #1, merge deferred)  
**Branch HEAD**: `6443b2db`  
**Version**: v0.27.0  
**Handoff type**: Post-external-audit council review, Sprint 28 planning  
**Council doc**: `Data/Pages/council/sprint28-council-20260620.md`

---

## I. What Just Happened

The user pushed at least 5 new external audit files to the branch. A full independent code verification was performed against branch HEAD, validating or refuting every claim. A 6-seat council reviewed the synthesis. Key outcomes:

1. **12 stale audit findings confirmed as already-fixed** (Sprints 5–27). These should never be re-opened.
2. **DEF-NEW-4 refuted** — `GatherItemDecompose` does NOT have `Take(2)`. The external audit was looking at an older snapshot. P2-B in Sprint 28 is CLOSED.
3. **P2-D upgraded to P1** — `ReplanAsync` creates a `SimpleGoal` shell that breaks all decomposer-handled goal replanning (gather/build/craft goals throw `InvalidOperationException` during replan, agent stalls on consecutive failures).
4. **5 new findings added** (DEF-NEW-6 through DEF-NEW-10).
5. CI status at `6443b2db`: **NOT YET CONFIRMED** — Sprint 28 P0-A must verify this first.

---

## II. Critical Invariants (Never Break)

1. **TreatWarningsAsErrors=true** — no new compiler warnings
2. **Rule E-1** — C# verbatim-string files patched via `paramsFile` only, never agent intermediary
3. **StatusTool.cs is deleted** — do not re-introduce; `GetStatusTool` registered as `"GetStatus"` and aliased as `"Status"`
4. **OperationCanceledException always propagates** through `ToolDispatcher` (not caught/wrapped)
5. **IItemSpecGoal.TargetCount DIM default = 1**, not 0
6. **DamageInterruptThresholdHp=0** means never interrupt; null means use system default (6 HP)
7. **PlannerStrategy enum** (not PlannerId) is the correct name for routing enum
8. **BlueprintRepository** uses HTTP gateway via MemorySmith REST API, NOT local filesystem
9. **DecomposerRegistry registration order matters** — keep registered decomposers' predicates disjoint

---

## III. Stale Audit Index (DO NOT REOPEN)

Many files in `Data/Pages/Audit/` describe the codebase at Sprint 5-era state. Use this index:

| Finding in audit | Resolved by |
|---|---|
| ToolDispatcher TODO / no schema validation | Sprint 25 P0-C |
| ToolEngine / ToolRegistry / IToolRegistry still present | Sprint 5 P2 |
| WorldState dictionary aliasing | Sprint 25 P1-A |
| `/api/agent/command` accepts arbitrary tool names | Sprint 5 P0 |
| ActionQueue not thread-safe | Sprint 23 P0-A |
| Version mismatch (`/api/about` = 0.7.0 vs README 0.23.0) | Sprint 27 P0-B (now 0.27.0) |
| LLM chat interpretation blocks world-state loop | Sprint 1a — `Channel<WorldEvent>` already in place (REFUTED) |
| Gather count lost — `IItemSpecGoal` receives empty params | Sprint 26 P0-B |
| HtnPlanner hardcoded type-switch (IItemSpecGoal/BuildGoal/CraftItemGoal) | Sprint 27 P0-D |
| **DEF-NEW-4: GatherItemDecompose Take(2)** | **REFUTED** — never existed at this HEAD; `foreach (var block in spec.SourceBlocks)` |

---

## IV. Current Codebase State (Sprint 27 End)

### Architecture (all delivered)
- **Agent**: `AgentBackgroundService` — event loop, goal lifecycle, damage interrupt, ITimeProvider
- **Planner**: `PlannerRouter` (implements `IPlanner`) → `DecomposerRegistry` → `HtnPlanner` fallback
- **Decomposers**: `BuildGoalDecomposer`, `GatherGoalDecomposer`, `SurviveNightGoalDecomposer`, `CraftItemGoalDecomposer`
- **Memory**: `RestMemoryGateway` × 2 (agent KB + world KB); `IMemoryGateway` keyed singleton "world"
- **Tools**: `ToolDispatcher` (sole dispatcher, validated, try/catch wrapped, alias support)
- **Journal**: `AgentJournal` (bounded 1000, best-effort, diagnostic store — NOT durable event store)
- **Safety**: `ReplanGovernor` (progress-hash stall detection), damage interrupt, health-critical gate
- **Testing**: `AgentBackgroundServiceTestHelper.BuildMinimal`, `FakeTimeProvider`

### Test count
~244+ tests (Sprint 27 added 12 new tests across BuildMinimal helper, FakeTimeProvider, CraftItemGoalDecomposer routing)

### Known open state
- WorldKbUrl defaults to null (warns at startup if unconfigured)
- `WorldStateProjector` handles `BlockNotFoundEvent`, health events, position, inventory
- `_correlatedActions` ConcurrentDictionary tracks `PendingAction` lifecycle with sweep

---

## V. Sprint 28 Task List

### P0 — Must ship first

**P0-A: Verify CI green on `sprint-5-tool-safety` @ `6443b2db`**
- Check CI run for all 5 jobs (build-and-test, browser-navigation-freeze, browser-route-smoke, build-docs, deploy-pages)
- Use `github__pull_request_read` method=`get_check_runs` + annotation endpoints for details
- Treat ANY failure as a new regression (baseline is all-green)
- If failures exist: diagnose with annotations endpoint, fix, and push before proceeding

**P0-B: DEF-NEW-1 — BuildGoalDecomposer.ReadOriginFact warn instead of silent zero**
- File: `Agent.Planning/Decomposition/BuildGoalDecomposer.cs`
- When origin fact is missing or unparseable → `LogWarning("Build origin fact missing or unparseable; defaulting to (0,0,0). Goal may build at wrong location.")` + emit a journal entry
- Still proceed (don't throw) — the 0,0,0 fallback is used by `ResolveAutoOrigin` which checks live WorldState facts
- Test: `ReadOriginFact` with missing fact → warning logged; with bad fact → warning logged; with valid fact → no warning

**P0-C: DEF-NEW-2 — GenericGatherGoal failure key includes targetCount**
- File: `Agent.Planning/Goals/GenericGatherGoal.cs`
- Change: `HasFailed` key from `$"goal:Gather:{itemId}:failed"` → `$"goal:Gather:{itemId}:{targetCount}:failed"`
- Also update `AgentBackgroundService` where it sets the failure fact to use the same key format
- Test: two `GenericGatherGoal` for same `itemId` but different `targetCount` → failure of one does NOT set `HasFailed` on the other

---

### P1 — Should ship this sprint

**P1-A: PlannerRouter.ReplanAsync type loss (UPGRADED FROM P2-D)**
- **Root cause**: `PlannerRouter.ReplanAsync` and `DecomposerPlanner.ReplanAsync` both reconstruct a `SimpleGoal` shell from `currentPlan.GoalName + Phases` and pass it to `Select()` / `decomposer.Decompose()`. All registered decomposers' `CanHandle` predicates require concrete goal types (`IItemSpecGoal`, `BuildGoal`, `CraftItemGoal`). `SimpleGoal` matches none → decomposer throws `InvalidOperationException` → replan silently returns null → agent stalls.
- **Fix**: `AgentBackgroundService` should pass the **original `IGoal` object** (the current goal, not reconstructed from plan) to `ReplanAsync`. Modify `IPlanner.ReplanAsync` signature to accept `IGoal? originalGoal = null`; pass it through; `DecomposerPlanner` uses it directly for `Decompose(originalGoal ?? reconstructed, state)`.
- Alternative simpler fix: In `AgentBackgroundService.ReplanAsync` (or wherever replan is triggered), call `planner.PlanAsync(currentGoal, state)` instead of `ReplanAsync` — effectively treat replan as a fresh plan from the same goal object.
- Test: Trigger replan while a `GenericGatherGoal` is active → result is non-null and has actions from `GatherGoalDecomposer`
- Test: Trigger replan while a `CraftItemGoal` is active → result routes through `CraftItemGoalDecomposer`
- **This must be done before P1-B (E2E test) because E2E tests consecutive failure paths**

**P1-B: E2E gather integration test (chat→goal→plan→dispatch→world event→IsComplete)**
- Requires P1-A to be complete (otherwise replan path is broken in the test)
- Use `AgentBackgroundServiceTestHelper.BuildMinimal` + `MockWorldAdapter` + `FakeTimeProvider`
- Test path: enqueue `ChatEvent` with "get 5 dirt" → ABS interprets → creates `GenericGatherGoal(dirt, 5)` → plans → dispatches `MineBlock` → mock emits `StatusEvent` with 5 dirt in inventory → `IsComplete` returns true → goal cleared
- Also test: failure path → replan triggered → plan reconstituted from original goal (validates P1-A)

**P1-C: Journal semantics decision in architecture.md**
- File: `Data/Pages/Architecture/architecture.md` (or `Data/Pages/architecture.md`)
- Add section: "Agent Journal is a bounded diagnostic buffer (max 1000 entries, best-effort trim under concurrency), not a durable event store. Use MemorySmith REST API for persistent memory."
- Closes Deep Code Audit Finding 4 from Sprint 26 external audit

---

### P2 — Nice to have this sprint

**P2-A: DEF-NEW-3 — GoalFactory.GetInt range check**
- File: `Agent.Planning/GoalFactory.cs`
- Change `long l => (int)l` to `long l when l >= int.MinValue && l <= int.MaxValue => (int)l` with a fallback to `defaultValue` on out-of-range
- Test: `GetInt` with `long.MaxValue` → returns `defaultValue`, not a wrapped negative

**P2-B: CLOSED — DEF-NEW-4 REFUTED**
- `GatherItemDecompose` does NOT have `Take(2)`. Confirmed by code inspection at branch HEAD.
- Do not implement this. Remove from backlog.

**P2-C: DEF-NEW-5 — WorldState collection mutability**
- File: `Agent.Core/Models/WorldState.cs`
- Change `Inventory` from `Dictionary<string, int>` to `IReadOnlyDictionary<string, int>` backed by frozen/immutable collection
- Same for `Facts` (currently `Dictionary<string, object?>`)
- Ensure `WorldStateProjector` creates new `WorldState` instances on each projection rather than mutating
- This is a broader change; may require a follow-up sprint if the projection pattern changes significantly

**P2-E: DEF-NEW-6 — TrimEnd('s') item normalization**
- File: `Agent.Planning/ChatInterpreter.cs`, `ResolveItemId` method
- Replace `raw.TrimEnd('s')` with a constrained plural map (e.g. `TryDepluralize` that only strips `s` when the result is a known alias key)
- OR: require the `TrimEnd` result to match the `ItemAliases` dictionary before accepting it (don't fall through to the raw ID path)
- Test: `ResolveItemId("glass")` → `"glass"` (not `"glas"`)
- Test: `ResolveItemId("moss")` → `null` or `"moss"` (not `"mos"`)
- Test: `ResolveItemId("diamonds")` → `"diamond"` (still works via alias, not via TrimEnd fallback)

**P2-F: DEF-NEW-7 — `\bdoing\b` status regex false positives**
- File: `Agent.Planning/ChatInterpreter.cs`, `ParseIntent` method
- Remove bare `doing` token from the status regex
- Keep: `status`, `what.?re you doing`, `what are you doing`, `report`
- Test: `"I am doing fine"` → `ChatIntentType.Unknown`
- Test: `"what are you doing"` → `ChatIntentType.QueryStatus`

**P2-G: DEF-NEW-9 — MineWoodDecompose namespace inconsistency**
- File: `Agent.Planning/HtnTaskLibrary.cs`, `MineWoodDecompose`
- Remove `minecraft:` namespace prefix → use `oak_log`, `birch_log` (consistent with rest of codebase)
- Consider expanding to all 7 log types (consistent with `OakLogSpec.SourceBlocks`)
- OR: remove `MineWoodDecompose` entirely (it's registered as `"MineWood"` but `GatherWoodDecompose` already delegates to the generic gather path)
- Test: `MineWoodDecompose` returns actions with block names matching `CommonMinecraftBlocks.DirectMineBlocks` format (no namespace prefix)

---

### P3 — Deferred (log only, do not block on these)

**P3-A: DEF-NEW-10 — MaxResponseDistanceBlocks unused**
- `ChatOptions.MaxResponseDistanceBlocks` (default 64.0) documented as "closest agent responds" heuristic
- `ChatInterpreter.IsDirectedAtBot` uses `private const int ProximityAddressBlocks = 32` instead
- Fix: wire `options.MaxResponseDistanceBlocks` in `IsDirectedAtBot` instead of the const
- OR: document const as the design choice and remove the ChatOptions property

**P3-B: DEF-NEW-8 — ExploreDecompose hardcoded two-pass**
- Extract retry count and budget as named constants or config parameters
- Medium priority, not user-visible in current gameplay

**P3-C: Lifecycle endpoint stubs**
- `/api/agent/connect` and `/api/agent/stop` return success without mutating state
- Either wire to real lifecycle (start/stop MineflayerAdapter subprocess) or document as informational

**P3-D: `/api/blueprints` hardcoded**
- Back with `IBlueprintRepository.SearchAsync` 

**P3-E: ChatRateLimiter.Prune verification**
- Verify `Prune()` is being called on a timer; if not, add timer in `ChatRateLimiter` or `LlmChatInterpreter`

---

## VI. Files Expected to Change in Sprint 28

| File | Change | Priority |
|---|---|---|
| `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` | LogWarning on missing origin | P0-B |
| `Agent.Planning/Goals/GenericGatherGoal.cs` | Include targetCount in failure key | P0-C |
| `Agent.Core/AgentBackgroundService.cs` | Update failure fact key format; fix replan to pass original goal | P0-C, P1-A |
| `Agent.Planning/Router/PlannerRouter.cs` | Fix ReplanAsync to preserve original goal type | P1-A |
| `Agent.Core/Interfaces/IPlanner.cs` | Optionally add `originalGoal` parameter to ReplanAsync | P1-A |
| `MemorySmith.Agent.Tests/Sprint28Tests.cs` | NEW — tests for P0-B, P0-C, P1-A, P1-B, P2 | All |
| `Data/Pages/Architecture/architecture.md` | Journal semantics section | P1-C |
| `Agent.Planning/GoalFactory.cs` | GetInt range guard | P2-A |
| `Agent.Core/Models/WorldState.cs` | IReadOnlyDictionary surface | P2-C |
| `Agent.Planning/ChatInterpreter.cs` | TrimEnd fix, `\bdoing\b` removal | P2-E, P2-F |
| `Agent.Planning/HtnTaskLibrary.cs` | MineWoodDecompose namespace fix | P2-G |
| `Data/Pages/council/sprint28-council-20260620.md` | NEW — this review | Doc |
| `Data/Pages/Tasks/agent-handoff-sprint28.md` | NEW — this doc | Doc |

---

## VII. GitHub / CI Tooling Reminders

- **Push files**: Use `github__create_or_update_file` per file (not `github__push_files` — the tree API is 403'd on this token)
- **Workflow files**: Cannot write to `.github/workflows/` — surface YAML to user for manual upload
- **CI check runs**: `github__pull_request_read method=get_check_runs` + `GET /repos/TheMasonX/MemorySmith.Agent/commits/{sha}/check-runs` + `/check-runs/{id}/annotations`
- **Branch**: Always push to `sprint-5-tool-safety`; main is ahead only in docs (03b5eb9c)
- **AGENTS.md Rule E-1**: Never patch C# verbatim-string files via agent string intermediary — use `paramsFile`

---

## VIII. Definition of Done for Sprint 28

Sprint 28 is DONE when:
1. CI is green on the pushed branch HEAD
2. P0-B and P0-C are implemented with passing tests
3. P1-A (ReplanAsync fix) is implemented with at least 2 tests
4. P1-B (E2E gather test) passes
5. P1-C (journal semantics doc) is committed
6. Council review of the implemented sprint is written and the verdict is APPROVED
7. This handoff doc superseded by an `agent-handoff-sprint29.md`
