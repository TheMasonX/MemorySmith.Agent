# MemorySmith.Agent — Single Source of Truth Audit v4
**Branch:** `sprint-35-llm-first`  
**Baseline commit:** `f6ab1c02990de81f078cf723cfdb4f9825f7ef9a`  
**Date:** 2026-06-26

## Summary

| Area | Status | Confidence | Decision |
|---|---:|---:|---|
| Canonical planning/execution pipeline | Open | 92% | Still split across legacy + target shapes |
| Runtime orchestration load | Open | 97% | `AgentBackgroundService` still too wide |
| Planner decomposition shape | Open | 94% | `HtnTaskLibrary` still monolithic |
| Context propagation | In Progress | 88% | Safer, but still dictionary-based |
| Memory contract shape | Open | 84% | Structured metadata still missing |
| Integration coverage at seams | Open | 91% | Unit tests outpace boundary tests |
| Transitional compatibility layers | Open | 89% | Missing retirement criteria |
| Dispatcher internal structure | Deferred | 89% | Healthy now, but close to needing split points |

## Findings

### DA-001 — Canonical runtime pipeline still diverges from target architecture
**Severity:** Critical  
**Status:** Open  
**Confidence:** 92%

**Rationale**
- Architecture docs describe a clean pipeline: UI → host → planner → dispatcher → adapter. `AgentBackgroundService` still hosts multiple cross-cutting loops and policies directly. citeturn78file0turn79file0
- Current runtime still contains deterministic fast-paths, context-carry bridges, and legacy fallback behavior that are not yet classified as permanent vs temporary. citeturn80file0turn68file0turn57file0

**Recommendation**
- Define one canonical pipeline.
- Classify every legacy path as: permanent fast-path / temporary bridge / obsolete.
- Add removal criteria and target sprint to every bridge.

**Sources**
- `Data/Pages/architecture.md`
- `WebUI.Blazor/AgentBackgroundService.cs`
- `Agent.Tools/ToolDispatcher.cs`

---

### DA-002 — `AgentBackgroundService` is still the runtime god object
**Severity:** High  
**Status:** Open  
**Confidence:** 97%

**Rationale**
- The host owns connection lifecycle, replanning cadence, stall handling, damage interrupts, action dispatch/correlation, chat intake, dashboard pushes, and creative provisioning in one class. citeturn79file0turn68file0turn57file0
- Constructor surface is already a signal: many collaborators, many policies, one file. citeturn79file0

**Recommendation**
- Extract:
  - `AgentConnectionService`
  - `AgentPlanLoop`
  - `AgentDispatchCoordinator`
  - `AgentEventProjector`
  - `AgentChatIngress`
  - `CreativeProvisioningService`
- Keep `AgentBackgroundService` as conductor only.

**Implementation plan**
1. Extract pure helpers first.
2. Move lifecycle-specific logic behind interfaces.
3. Replace direct field mutation with state snapshot objects.
4. Add one integration test per extracted collaborator.

**Sources**
- `WebUI.Blazor/AgentBackgroundService.cs`
- `Data/Pages/architecture.md`

---

### DA-003 — Planner decomposition is still monolithic, and gather still bypasses the new memory path
**Severity:** High  
**Status:** Open  
**Confidence:** 94%

**Rationale**
- `HtnTaskLibrary` contains gather, craft, smelt, build, explore, and terrain-search logic in one module. citeturn67file0
- `GatherGoalDecomposer` still delegates gather work to `HtnTaskLibrary`; `GatherWoodGoal` still describes the older `FindTree → MineWood → Collect` flow. citeturn75file0turn70file0
- The new `SearchMemoryTool → MoveToTool` path exists, but the current gather decomposition still emits direct mining actions instead of a memory-driven navigation step. citeturn54file0turn83file0

**Recommendation**
- Split planner logic by goal family.
- Add explicit `SearchMemory → MoveTo → MineBlock` routing where spatial lookup is intended.
- Keep `HtnTaskLibrary` as a registry/facade only.

**Implementation plan**
1. Extract `GatherTaskDecomposer`, `CraftTaskDecomposer`, `SmeltTaskDecomposer`, `BuildTaskDecomposer`, `ExploreTaskDecomposer`, `TerrainSearchTaskDecomposer`.
2. Move shared constants to support modules.
3. Add a gather integration test proving memory search precedes movement when coordinates are available.

**Sources**
- `Agent.Planning/HtnTaskLibrary.cs`
- `Agent.Planning/Decomposition/GatherGoalDecomposer.cs`
- `Agent.Planning/Goals/GatherWoodGoal.cs`
- `Agent.Tools/Tools/SearchMemoryTool.cs`
- `Agent.Tools/Tools/MoveToTool.cs`

---

### DA-004 — Context propagation is safer, but still a loosely typed ambient bus
**Severity:** High  
**Status:** In Progress  
**Confidence:** 88%

**Rationale**
- `ActionData.Context` is still a mutable `Dictionary<string, object?>` shared across actions in a plan. citeturn83file0
- `AgentBackgroundService` copies plan context into every action and merges only schema-declared keys into arguments. This fixed correctness, but the contract is still string-keyed. citeturn68file0turn57file0

**Recommendation**
- Introduce a typed `PlanContext` / execution-effect model.
- Move high-value keys first:
  - coordinates
  - build origin
  - correlation IDs
  - inventory observations
- Keep dictionary context only as an escape hatch.

**Implementation plan**
1. Wrap the existing dictionary in a typed facade.
2. Add typed properties for common coordination fields.
3. Delete ad hoc string keys after migration.

**Sources**
- `Agent.Core/Models/ActionData.cs`
- `WebUI.Blazor/AgentBackgroundService.cs`

---

### DA-005 — Memory contract should become structured, not snippet-parsed
**Severity:** Medium  
**Status:** Open  
**Confidence:** 84%

**Rationale**
- `SearchMemoryTool` currently extracts coordinates from snippets by regex and emits `nearestX/Y/Z`. citeturn54file0
- The tool only checks the top hit/snippet path, so valid coordinates in lower-ranked results can be missed. citeturn54file0
- Malformed coordinates can still fail the tool path rather than degrading gracefully. citeturn54file0turn80file0

**Recommendation**
- Expose structured metadata from memory where possible:
  - coordinates
  - inventory
  - entity IDs
  - observation type
  - source confidence
- Treat rendered snippets as presentation, not API.

**Implementation plan**
1. Scan multiple results for the first valid coordinate-bearing hit.
2. Switch parsing to guarded `TryParse`/fallback behavior.
3. Emit provenance/confidence for parsed hints.

**Sources**
- `Agent.Tools/Tools/SearchMemoryTool.cs`
- `MemorySmith.Agent.Tests/Sprint51Tests.cs`
- `Agent.Tools/ToolDispatcher.cs`

---

### DA-006 — Integration tests cover seams, but not enough full-path behavior
**Severity:** High  
**Status:** Open  
**Confidence:** 91%

**Rationale**
- Tool tests prove individual tool behavior well, but the most failure-prone paths are cross-component: planner → dispatcher → context merge → event feedback. citeturn52file0turn54file0
- The current “merge” test verifies tool-level tolerance, not the real host dispatch merge path. citeturn54file0

**Recommendation**
Add production-path integration coverage for:
- planner → dispatcher
- dispatcher → schema validation
- context propagation
- repair/retry loops
- event-driven completion and replanning

**Implementation plan**
1. One host-level test for the full context-carry path.
2. One host-level test for planner output feeding dispatcher input.
3. One host-level test for event feedback completing an action.

**Sources**
- `MemorySmith.Agent.Tests/ToolDispatchTests.cs`
- `MemorySmith.Agent.Tests/Sprint51Tests.cs`
- `WebUI.Blazor/AgentBackgroundService.cs`

---

### DA-007 — Transitional compatibility layers need explicit exit criteria
**Severity:** High  
**Status:** Open  
**Confidence:** 89%

**Rationale**
- `MoveToTool` now supports both explicit coordinates and carried coordinates. citeturn52file0turn54file0
- `AgentBackgroundService` now selectively merges context keys instead of copying all context. citeturn68file0turn57file0
- These are good bridges, but they should not remain indefinite “helpful fallbacks.” citeturn78file0turn79file0

**Recommendation**
For every bridge, add:
- owner
- purpose
- replacement
- removal criteria
- target sprint

**Sources**
- `Agent.Tools/Tools/MoveToTool.cs`
- `WebUI.Blazor/AgentBackgroundService.cs`
- `Data/Memories/Core/agent-architecture-bounded-contexts.json`

---

### DA-008 — `ToolDispatcher` is acceptable now, but its policy surface is near the split threshold
**Severity:** Medium  
**Status:** Deferred  
**Confidence:** 89%

**Rationale**
- `ToolDispatcher` currently bundles registration, aliases, schema validation, exception translation, outcome mapping, and journaling policy. citeturn80file0
- It is still a good single façade, but it is getting close to being a mini framework.

**Recommendation**
- Keep the public façade.
- Extract internal helpers when policy grows further:
  - registry
  - schema validator
  - outcome mapper
  - execution journal policy

**Sources**
- `Agent.Tools/ToolDispatcher.cs`

## Existing work already in motion (do not duplicate)
- `tsk-0042-dashboard-event-bus`
- `tsk-0044-dashboard-broadcast-service`
- `tsk-0045-refactor-agentbackgroundservice-to-event-bus`
- `tsk-0047-program-cs-di-wiring-endpoints`
- `tsk-0050-runtime-configuration-model`  
**Source:** repository task index search results. citeturn100file0turn101file0turn105file0turn103file0turn104file0

## Prioritized implementation roadmap

### Phase 1 — Classify and contain
1. Label every compatibility bridge as permanent / temporary / obsolete.
2. Align docs and code on a single canonical pipeline.
3. Add retirement criteria to all temporary paths.

### Phase 2 — Extract boundaries
1. Split `AgentBackgroundService` into orchestration + collaborators.
2. Split `HtnTaskLibrary` by goal family.
3. Introduce typed `PlanContext` / execution effects.

### Phase 3 — Harden contracts
1. Replace snippet parsing with structured memory metadata where possible.
2. Add provenance/confidence for parsed hints.
3. Extract dispatcher policy helpers only if the surface expands further.

### Phase 4 — Lock in with tests
1. Full host-level context-carry test.
2. Planner-to-dispatch integration test.
3. Event-feedback completion test.
4. Delete retired fallback paths after tests pass.

## Open questions
- Which compatibility layers are intended to survive long term?
- Should gather workflows always use memory search for spatial targeting?
- Which high-value context fields should become typed first?
- Should `AgentBackgroundService` split be driven by runtime loops or by dataflow boundaries first?

## Confidence summary
- Runtime architecture drift: **92%**
- Host orchestration overload: **97%**
- Planner monolith risk: **94%**
- Context typing need: **88%**
- Memory contract structure need: **84%**
- Integration gap: **91%**
- Compatibility-layer retirement need: **89%**
- Dispatcher split threshold: **89%**
