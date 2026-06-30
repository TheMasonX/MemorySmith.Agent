# Council Review: Blueprint YAML Design + LLM Autonomy Design

**Date:** 2026-06-29
**Council mode:** 6-seat self-simulated subagent review (user-permitted)
**Evidence ground truth:** Actual codebase files, wiki pages, task records, and repo memories

---

## Decision

**Adopt Autonomy Phases 0–2 and Blueprint Phase 0 model alignment as the immediate work items for Sprint 56+. Defer all other phases pending proven prerequisites.**

---

## Evidence Reviewed

### Pages & Design Docs
- `Data/Pages/Tasks/memorysmith_llm_autonomy_design_2026-06-29.md` — Autonomy design
- `Data/Pages/Tasks/blueprint-yaml-design-doc.md` — Blueprint YAML design (high-level)
- `Data/Pages/Tasks/blueprint-yaml-implementation-plan` — Blueprint YAML low-level plan (wiki page)
- `features/blueprint-system` — current blueprint system description
- `handoffs/sprint-55-build-quality-reliability` — Sprint 55 handoff with deferred items

### Architecture & Code
- `Data/Pages/architecture.md` — canonical pipeline
- `AGENTS.md` — coding guidelines
- `WebUI.Blazor/AgentBackgroundService.cs` — main agent loop (~2500 lines)
- `Agent.Core/Models/ActionOutcome.cs` — outcome model
- `Agent.Core/Models/IntentDraft.cs` — intent model with NextSteps
- `Agent.Core/Models/TaskSequenceGoal.cs` — sequence goal (MaxSteps=5)
- `Agent.Core/Interfaces/ILlmEvaluator.cs` — evaluator interface
- `Agent.Core/Runtime/AgentRuntime.cs` — runtime composition (unused)
- `Agent.Core/Interfaces/IMemoryGateway.cs` — memory gateway
- `Agent.Construction/BlueprintSchema.cs` — current PlacementBlock/Blueprint records
- `Agent.Construction/BlueprintParser.cs` — markdown parser (~380 lines)
- `Agent.Construction/BlueprintExecutor.cs` — block placement action generator
- `Agent.Planning/Goals/BuildGoal.cs` — build goal with origin resolution
- `Agent.Planning/IntentManager.cs` — IntentDraft → GoalRequest mapping
- `Agent.Planning/LlmChatInterpreter.cs` — LLM system prompt + JSON parsing
- `Agent.Memory/MemorySmithBlueprintRepository.cs` — 3-stage lookup

### Tasks & Validation
- TSK-0238, TSK-0239, TSK-0240 — Autonomy P0–P2 (Backlog)
- 240 task files in `Data/Tasks/` — zero for blueprint YAML
- `pwsh Scripts/Test-TaskRecords.ps1` — task validation

---

## Findings

| Seat | Recommendation | Confidence | Blocking concern |
|---|---|---|---|
| **Source-Grounded Archivist** | Autonomy P0–P2 is well-grounded; Blueprint YAML needs task creation and migration plan | 0.90 | "All autonomy re-enters same pipeline" is aspirational — no signal wiring exists. Blueprint YAML has zero tasks. |
| **Data Model Architect** | Approve both designs with conditional changes to data model | 0.87 | `EvaluationResult` too narrow (bool+strings). `BlockState` type change breaks 5+ subsystems. `FollowUpPolicy` vs `TaskSequenceGoal` interaction undefined. |
| **Retrieval Specialist** | Approve with search-suppression prerequisites | 0.92 | Timer wakeups could flood MemorySmith with searches. No search dedup/caching exists. TSK-0132 (page score=0.0) must be fixed before completion-driven autonomy. |
| **Human Learning Advocate** | Approve autonomy P0–P1, conditional on dashboard visibility | 0.84 | "Pause autonomy" toggle and wake reason display must ship BEFORE autonomous behavior reaches operators. Phase 5 dashboard features are not optional. |
| **Skeptical Reviewer** | Approve with 5 blocking concerns resolved | 0.93 | **5 blocking issues** (see below). Both designs have solid foundations but contain issues that must be resolved before implementation. |
| **Synthesizer** | Autonomy P0–P2 first, then Blueprint P0 model alignment in parallel | 0.91 | Autonomy has more prerequisites and fewer blocking gaps. Blueprint YAML's `BlockState` change is systemic. Defer all other phases. |

### Blocking Concerns (must be resolved before implementation)

| # | Design | Issue | Resolution |
|---|---|---|---|
| B1 | Autonomy | **Timer signals have no entry point** — AgentBackgroundService has 3 concurrent loops (ProcessEvents, DispatchActions, ChatConsumer) but no signal inbox for timer/completion signals | Add IRuntimeSignalSink and signal-injection path in Phase 1 |
| B2 | Autonomy | **EvaluationResult return type too narrow** — `bool ShouldReplan` + strings cannot express the 6 distinct outcomes the design requires (stop/continue/advance/branch/schedule/recover) | Expand to discriminated union type in Phase 0 |
| B3 | Autonomy | **Timer relevance check implies unbounded LLM cost** — every timer tick could cost an LLM call just to decide if the wake matters | Define deterministic short-circuit filters before LLM call |
| B4 | Blueprint | **BlockState type change breaks 5+ subsystems** — changing `string?` to structured `BlockState` class breaks PlacementBlock, BlueprintParser, BlueprintExecutor, adapter JSON, and tests | Full impact assessment before any model change |
| B5 | Blueprint | **No migration plan for 4 existing markdown blueprints** — Phase 4 says "replace markdown parser" but has no transition path for small-house, castle, farm, wizards-tower | Migration plan must be documented and accepted before Phase 4 |

### Moderate Concerns

| # | Design | Issue |
|---|---|---|
| M1 | Autonomy | `FollowUpPolicy` vs `TaskSequenceGoal` interaction undefined — two chaining mechanisms could conflict |
| M2 | Autonomy | No tasks exist for Phases 3–5 — scope unmanaged |
| M3 | Autonomy | Completion re-entry tests cannot use existing patterns — no `TickAsync` decomposition exists |
| M4 | Blueprint | Derivation engine scope ("the compiler") is too broad — 6 subsystems in one phase |
| M5 | Blueprint | `YamlDotNet` package vetting not addressed per AGENTS.md policy (P-1 through P-5) |
| M6 | Blueprint | No tasks exist for any phase — design is not actionable |
| M7 | Cross | Page search score=0.0 (TSK-0132) under-ranks blueprint pages — must fix before completion-driven search |
| M8 | Cross | No search dedup/caching exists — timer wakeups could flood MemorySmith |

---

## Synthesis

### What Changes NOW (Sprint 56)

**Autonomy Phase 0** (contracts — ~2 days):
- Define `WakeReason` enum (Chat, Timer, GoalCompleted, ActionCompleted, Recovery, Manual, ExternalCommand, StateChange)
- Define `RuntimeSignalEnvelope` with extensible `object? Payload`
- Define `FollowUpPolicy` as sealed class with boolean properties (NOT flags enum)
- Expand `EvaluationResult` to discriminated union supporting 6 outcome types
- Document `FollowUpPolicy` vs `TaskSequenceGoal` interaction rules
- Update `architecture.md` and `AGENTS.md`

**Autonomy Phase 1** (signal plumbing — ~4 days):
- Implement `IRuntimeSignalSink` in `AgentBackgroundService`
- Add timer emitter using `ITimeProvider`
- Wire wake logging (reason, goal ID, accepted/suppressed, suppression reason)
- Implement suppression cooldowns with named boolean gates
- Ship minimum-viable dashboard: wake reason display + suppressed wake counter
- Ship "pause autonomy" toggle (chat command + dashboard button)

**Blueprint Phase 0** (model alignment — ~3 days, parallel-safe):
- Create tasks for all blueprint YAML phases
- Complete `BlockState` type change impact assessment across 5+ subsystems
- Document migration plan for 4 existing markdown blueprints
- Complete `YamlDotNet` package vetting per AGENTS.md policy
- Create package justification document

### What Changes NEXT (Sprint 57)

**Autonomy Phase 2** (completion follow-up — ~5 days):
- Requires Phase 0/1 merged and stable
- Requires `EvaluationResult` discriminated union type available
- Wire completion signal type
- Attach `FollowUpPolicy` to goal/intent
- Evaluator can request continuation
- Sequence continuation limits enforced

**Blueprint Phase 1** (YAML parser — ~5 days):
- Requires Blueprint Phase 0 model alignment merged
- Requires YamlDotNet dependency vetted and added
- Implement `BlueprintYamlDto` → canonical `Blueprint` mapping
- Implement YAML serializer with stable ordering
- Add format detection in `MemorySmithBlueprintRepository` (try YAML first, markdown fallback)

### What Changes LATER (Sprint 58+)

- **Autonomy Phase 3** (chaining/branching) — defer until P2 is proven
- **Autonomy Phase 4** (persistence) — in-memory sufficient for single-host app
- **Autonomy Phase 5** (dashboard) — wake logging in P1 covers initial needs
- **Blueprint Phase 2a** (materials + legend derivation) — after P1 stable
- **Blueprint Phase 2b/c** (layers, phases, validation, docs) — scope needs breakdown
- **Blueprint Phase 3** (Blazor editor) — no UI design exists; massive effort
- **Blueprint Phase 4** (agent integration) — keep markdown parser as permanent fallback
- **Blueprint Phase 5** (advanced features) — defer indefinitely

### Key Design Constraints

1. **Markdown parser is permanent fallback** — Phase 4 must NOT remove `BlueprintParser`. Legacy blueprints must always load.
2. **Freeze `PlacementBlock` shape during Autonomy P0–P2** — `BlockState` change happens after autonomy phases ship, or before both start. No concurrent changes.
3. **Search dedup before timer autonomy** — implement `SearchDeduplicator` with 30s cooldown before timer-driven wakeups ship.
4. **Pause before autonomy** — "pause autonomy" toggle ships in Phase 1, before any autonomous behavior reaches production.

---

## Dissent

**Unresolved:** The Human Learning Advocate recommends moving wake reason display and pause toggle to **Phase 0** (prerequisites, not post-delivery). The Synthesizer recommends Phase 1. This is a scoping question resolved by the recommendation in this report (Phase 1) with the understanding that neither ships without these features. If the team prefers tighter operator safety, the pause toggle can be promoted to Phase 0 at minimal cost.

**Unresolved:** The Skeptical Reviewer rates the Blueprint YAML design's "derivation engine" as a critical-path risk (too broad). The Data Model Architect rates it as medium concern. The recommendation to split Phase 2 into 2a/2b/2c is a compromise that defers the most speculative parts (layers, phases, validation, docs) while shipping the essential parts (materials, legend).

---

## Acceptance Criteria

### Autonomy Phase 0
- [ ] `WakeReason` enum defined and documented with team consensus
- [ ] `RuntimeSignalEnvelope` type defined
- [ ] `FollowUpPolicy` designed (sealed class, NOT flags enum)
- [ ] `EvaluationResult` expanded to discriminated union (not bool+strings)
- [ ] `FollowUpPolicy` vs `TaskSequenceGoal` interaction documented
- [ ] `architecture.md` updated with signal paths
- [ ] `AGENTS.md` updated with autonomy conventions

### Autonomy Phase 1
- [ ] `IRuntimeSignalSink` wired into `AgentBackgroundService`
- [ ] Timer emitter using `ITimeProvider` works (unit-tested)
- [ ] Suppression cooldowns implemented with named boolean gates
- [ ] Wake logging records reason, goal ID, accepted/suppressed, suppression reason
- [ ] Dashboard shows wake reason and suppressed wake count
- [ ] "Pause autonomy" toggle works (chat command + dashboard button)
- [ ] Search dedup/caching prevents MemorySmith flooding (max 10 searches/min)
- [ ] All tests pass (≥746)
- [ ] Build produces 0 warnings

### Blueprint Phase 0
- [ ] `BlockState` change impact assessment complete (all 5+ subsystems inventoried)
- [ ] Migration plan for 4 markdown blueprints documented and accepted
- [ ] `YamlDotNet` vetted (license ✅, vulnerability ✅, deprecation ✅)
- [ ] Package justification document created per AGENTS.md P-1
- [ ] Tasks created for Blueprint Phases 0–4
- [ ] `architecture.md` updated with YAML pipeline

---

## Open Questions

1. **Should `ExternalCommand` and `StateChange` be separate `WakeReason` values, or subsumed under `Manual` and `GoalCompleted`?** The Source-Grounded Archivist recommends separate values for semantic clarity.

2. **Should the markdown parser output be used to auto-generate YAML blueprints via a migration script?** This would simplify Phase 4 — the script runs once, converts all 4 blueprints, and YAML becomes the canonical format thereafter.

3. **What is the safe default for `FollowUpPolicy.MaxChainDepth`?** `TaskSequenceGoal.MaxSteps = 5` is a precedent. Recommend 3 for conditional chains (allows gather→decide→craft→decide→build with headroom).

4. **Should `FollowUpPolicy` be attached to `IntentDraft` or to `IGoal`?** The design doc says "intent/goal layer." Attaching to the intent allows the chat interpreter to set policy. Attaching to the goal allows runtime override. Recommend intent-level with goal-level override capability.

5. **How does the dashboard "pause autonomy" toggle interact with `ChatReceived` wakes?** Recommendation: pause suppresses TimerDue, ActionCompleted, GoalCompleted signals. ChatReceived always works (operator override). Confirm this in design.

---

## Task Actions

### New Tasks to Create

| Priority | Title | Phase |
|---|---|---|
| Critical | Expand `EvaluationResult` with discriminated outcome types for autonomy | Autonomy P0 |
| Critical | Assess `BlockState` type change impact across all subsystems | Blueprint P0 |
| High | Create migration plan for 4 markdown blueprints to YAML | Blueprint P0 |
| Blocking | Vet `YamlDotNet` package per AGENTS.md policy | Blueprint P0 |
| High | Define `FollowUpPolicy` vs `TaskSequenceGoal` interaction contract | Autonomy P0 |
| High | Add `IRuntimeSignalSink` and timer signal entry point to AgentBackgroundService | Autonomy P1 |
| High | Ship minimum-viable autonomy dashboard (wake reason + pause toggle) | Autonomy P1 |
| Medium | Implement `SearchDeduplicator` with configurable cooldown | Autonomy P1 prerequisite |
| Medium | Create Autonomy Phase 3–5 scoping tasks (type:design) | Autonomy backlog |
| Medium | Split Blueprint Phase 2 into 2a (materials+legend) and 2b/2c (deferred) | Blueprint P2 |

### Existing Tasks to Update

| Task | Update |
|---|---|
| TSK-0238 | Add deliverable: `EvaluationResult` expansion. Add exit criterion: FollowUpPolicy/TaskSequenceGoal interaction documented. |
| TSK-0239 | Add reference to new `IRuntimeSignalSink` task. Expand acceptance criteria to include suppression logging and pause toggle. |
| TSK-0240 | Add gate: requires `EvaluationResult` expansion. Add gate: requires TaskSequenceGoal completion detection. |
| TSK-0132 | Elevate priority: page score=0.0 fix is a prerequisite for completion-driven autonomy search. |

---

*Council report prepared by Agent Smith / SteveBot on 2026-06-29 using 6-seat self-simulated review with explicit user permission for subagent invocation. Council process documented in `.github/skills/council/SKILL.md`.*
