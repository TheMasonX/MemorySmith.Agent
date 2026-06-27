# Council Review: Accept Audit Findings and Adopt 3-Sprint Consolidation Roadmap

**Date:** 2026-06-26
**Branch:** `sprint-35-llm-first` (baseline: `f6ab1c0`)
**Council type:** Full 6-seat review (subagents, user-authorized)
**Review scope:** 6 audit documents consolidated into one decision

---

## Decision

**Accept both audits as directionally correct with severity adjustments; adopt a 3-sprint architectural consolidation roadmap (Sprints 51–53) that canonicalizes the pipeline first, extracts monolith boundaries second, and hardens typed contracts third — deferring dashboard infrastructure (TSK-0042–0050) until the architectural foundation is solid.**

---

## Evidence Reviewed

### Primary Audit Documents
1. `Data/Pages/Audit/memorysmith_agent_delta_audit_addendum_sprint35_llm_first_f6ab1c0_v3.md` (Delta Audit v3)
2. `Data/Pages/Audit/2026-06-26_memorysmith_agent_single_source_of_truth_audit_v4.md` (Single Source of Truth Audit v4)
3. `research-MemorySmith.Agent Sprint-35 Code Audit.docx` (Deep Code Audit)
4. `research-MemorySmith.Agent Codebase Audit Sprint-35.docx` (Codebase Audit)
5. `research-MemorySmith.Agent Delta Audit Sprint-35 Commit d2ef16ab.docx` (Delta Audit @ d2ef16ab)
6. `research-MemorySmith.Agent Commit Delta Audit.docx` (Commit Delta Audit)

### Source Files Verified
- `WebUI.Blazor/AgentBackgroundService.cs` — 16-param constructor, ~2300 lines, 56+ fields
- `Agent.Planning/HtnTaskLibrary.cs` — monolithic, 800+ lines, 5+ decomposers
- `Agent.Planning/Decomposition/GatherGoalDecomposer.cs` — delegates to HtnTaskLibrary
- `Agent.Planning/Goals/GatherWoodGoal.cs` — FindTree→MineWood→Collect phases
- `Agent.Tools/ToolDispatcher.cs` — bundles registration, validation, journaling
- `Agent.Core/Models/ActionData.cs` — `Dictionary<string, object?> Context`
- `Agent.Tools/Tools/SearchMemoryTool.cs` — regex extraction, top-hit-only, no TryParse
- `Agent.Tools/Tools/MoveToTool.cs` — context-carry fallback, dual coordinate sources
- `Data/Pages/architecture.md` — deterministic HTN pipeline description
- `AGENTS.md` — LLM-first pipeline rules, CRITICAL Rule A-1
- `Data/Pages/Handoffs/sprint-50-complete-next-steps.md` — current sprint state

### Task State Verified
- TSK-0042 through TSK-0050: all in Backlog; address MemorySmith.App, NOT MemorySmith.Agent
- Audit v4 incorrectly maps these to Agent concerns

---

## Findings

| Seat | Recommendation | Confidence | Blocking Concern |
|---|---:|---|
| **Source-Grounded Archivist** | Accept audits with addendum; re-rank roadmap; audits are correct but have a cross-repo task scoping error and miss ADR D-003 context (deterministic fast-paths are intentional, not drift) | **85%** | Audit v4 references wrong-codebase tasks (TSK-0042–0050); deterministic-per-ADR paths must not be removed indiscriminately |
| **Data Model Architect** | Introduce typed `PlanContext` wrapping the dictionary; use `ObservationStub` reusing `StructuredEffect` vocabulary; add `StructuredMemoryHint` companion type to `SearchResult`; incremental migration with explicit retirement criteria per bridge | **87%** | Cross-repo coordination needed for MemorySmith API structured metadata; dictionary escape hatch risk of permanence |
| **Retrieval Specialist** | Scan ALL results for coordinates (not just top hit); guard `int.Parse` with `TryParse`; align test regex with production; add SearchMemory→MoveTo→MineBlock routing in gather decomposition; clear spatial keys on goal transition | **87%** | Top-hit-only limitation is Critical — bot can miss known resource locations; SearchMemory not yet integrated into gather decomposition (TSK-0080 stripped the calls) |
| **Human Learning Advocate** | Onboarding is Hard; docs are 5/10 accurate (version skew v0.23→v0.50, contradictory pipeline descriptions); create single canonical pipeline diagram; update version strings; rewrite Adding-a-Goal guide; add audit index | **87%** | A new developer cannot determine the authoritative pipeline from docs alone; version skew undermines all documentation trust |
| **Skeptical Reviewer** | DA-001 overstated (Critical→High); DA-004 overstated (High→Medium); DA-005 overstated (Medium→Low); 5 missed priorities identified including lying comment in `GatherItemDecompose`, unused `_agentRuntime` field, and LLM failure mode gaps | **82%** | Fix risk from premature decomposition exceeds cost of inaction for DA-004/DA-005; audits focus on structural concerns at expense of behavioral ones |
| **Synthesizer** | Accept audits; 3-sprint roadmap: S51 canonicalize pipeline + small fixes, S52 extract AgentBackgroundService + HtnTaskLibrary, S53 harden contracts + integration tests + delete obsolete bridges; defer dashboard tasks to S54+ | **88%** | Sprint 52 extraction is highest-risk; mitigated by one-collaborator-at-a-time with build+test validation between each |

### Severity Adjustments (Council Consensus)

| Finding | Audit Severity | Council Severity | Rationale |
|---|---|---|---|
| DA-001 (Pipeline divergence) | Critical | **High** | Primary intents follow target pipeline; remaining fast-paths are ADR D-003 permitted. No concrete example of non-deterministic fast-path bypass cited. |
| DA-002 (BackgroundService god object) | High | **High** (unchanged) | 16-param constructor, ~2300 lines — confirmed. But "god object" label is hyperbolic for a coherent orchestration host. |
| DA-003 (Planner monolithic) | High | **High** (unchanged) | Confirmed. `GatherItemDecompose` has a lying comment claiming SearchMemory routing. |
| DA-004 (Context loosely typed) | High | **Medium** | Schema-gated merge fixed correctness. Remaining concern is developer experience, not runtime safety. |
| DA-005 (Memory contracts) | Medium | **Medium** (unchanged) | No evidence of user-visible bug from top-hit limitation. But `int.Parse` without `TryParse` is a latent defect. |
| DA-006 (Integration tests) | High | **High** (unchanged) | Valid concern; cross-component seams are highest-risk area. |
| DA-007 (Compatibility layers) | High | **Medium** | Working fallbacks are robustness, not debt. Documentation of exit criteria is good practice but not urgent. |
| DA-008 (ToolDispatcher split) | Medium/Deferred | **Deferred** (unchanged) | Healthy façade; split when policy surface actually expands. |

---

## Missed Priorities (Discovered During Council Review)

These findings were NOT in the original audits but emerged during source-code verification:

| ID | Finding | Severity | Source |
|---|---|---|---|
| **MC-1** | Lying comment in `GatherItemDecompose`: says "Default plan: SearchMemory → MineBlock → GetStatus" but code emits `GetStatus → [Wander] → MineBlock` — zero SearchMemory | **High** | Skeptical Reviewer |
| **MC-2** | `_agentRuntime` field injected but unused — dead code or incomplete migration | **Medium** | Skeptical Reviewer |
| **MC-3** | `AdvanceBuildCheckpoint` iterates `ConcurrentDictionary` for first Dispatched PlaceBlock — non-deterministic with multiple concurrent dispatches | **Medium** | Skeptical Reviewer |
| **MC-4** | No evaluation of LLM failure modes (malformed JSON, timeouts, hallucinations) in audit scope | **High** | Skeptical Reviewer |
| **MC-5** | `CoordLabelsPattern` regex uses single group name `"x"` for all three axes — works but fragile | **Low** | Retrieval Specialist |
| **MC-6** | Audit v4 references MemorySmith.App tasks (TSK-0042–0050) as "existing work" for Agent concerns — scoping error | **Medium** | Source-Grounded Archivist |
| **MC-7** | Stale coordinates carry forward across goal transitions (planContext persists across replans) | **Medium** | Retrieval Specialist |

---

## Synthesis

### Sprint 51 — "Canonicalize & Classify"
**Theme:** Define one pipeline, classify every legacy path, execute small high-ROI fixes. Zero runtime risk.

| Priority | Task | Description |
|:--------:|:-----|:------------|
| **Critical** | Classify all bridges | Label every compatibility bridge as permanent/temporary/obsolete with owner, purpose, replacement, removal criteria, target sprint |
| **Critical** | Align docs to single pipeline | `architecture.md` and `AGENTS.md` must describe the same pipeline. No contradictions. |
| **High** | Fix SearchMemoryTool regex | `TryParse` guards, scan all results (not just top hit), fix `CoordLabelsPattern` group names |
| **High** | Complete chat cleanup | Remove `ChatInterpretation.GoalName` field (deferred since Sprint 38) |
| **High** | Fix lying comment | `GatherItemDecompose` comment must match actual code or code must match comment |
| **Medium** | Extract SmeltableMapping | Shared ore→ingot mapping class (TSK-0082) |
| **Medium** | Resolve `_agentRuntime` | Either integrate or remove the dead code |

**Gate:** `dotnet build` 0w/0e, 731+ tests pass, architecture.md describes exactly ONE pipeline

### Sprint 52 — "Extract Boundaries"
**Theme:** Split the two monoliths. Highest-risk sprint. One collaborator at a time.

| Priority | Task | Description |
|:--------:|:-----|:------------|
| **Critical** | Extract AgentConnectionService | Connection lifecycle from AgentBackgroundService |
| **Critical** | Extract AgentPlanLoop | Plan dispatch cycle, stall detection, replanning cadence |
| **High** | Extract AgentEventProjector | Event handling, damage interrupt, mine-complete correlation |
| **High** | Extract AgentChatIngress | Chat intake pipeline (must preserve CRITICAL Rule A-1) |
| **High** | Split HtnTaskLibrary | Extract GatherTaskDecomposer, CraftTaskDecomposer, SmeltTaskDecomposer, BuildTaskDecomposer, ExploreTaskDecomposer |
| **High** | Add SearchMemory→MoveTo routing to gather | When coordinates available, emit MoveTo before MineBlock |
| **Medium** | Introduce typed PlanContext (phase 1) | Coordinates + CorrelationId typed properties wrapping dictionary |

**Gate:** `AgentBackgroundService` constructor ≤6 params, ≤500 lines; `HtnTaskLibrary` ≤150 lines (registry only); new integration test passes

### Sprint 53 — "Harden & Validate"
**Theme:** Replace fragile conventions with typed contracts, add integration coverage, delete retired paths.

| Priority | Task | Description |
|:--------:|:-----|:------------|
| **High** | Structured memory metadata | MemorySmith API returns optional `Coordinates` field; SearchMemoryTool reads structured field, regex fallback only |
| **High** | Host-level context-carry test | Planner→SearchMemory→context→MoveTo end-to-end |
| **High** | Planner-to-dispatcher test | Planner output → dispatcher input → schema validation |
| **High** | Event-feedback completion test | MineCompleteEvent → PendingAction transition → no orphan |
| **High** | Delete obsolete bridges | Only paths classified as "obsolete" in S51, gated by S53 integration tests |
| **Medium** | Expand PlanContext (phase 2) | BuildOrigin, InventoryObservation, BlockObservation typed properties |
| **Medium** | Runtime Configuration Model | TSK-0050 — typed config for extracted services |

**Gate:** Zero regex in hot path (fallback only), 3+ new integration tests, all obsolete bridges deleted, 750+ tests

---

## Dissent

### Skeptical Reviewer dissents on severity of DA-001, DA-004, DA-005

> "The pipeline DOES follow Chat→IntentDraft→Planner→Goal for primary intents. The remaining regex fast-paths are explicitly permitted. `Critical` label is architecture-purity anxiety without evidence of actual divergence. DA-004's dictionary context is *safe* (schema-gated) — the remaining concern is about developer experience, not correctness. DA-005 has zero evidence of user-visible impact — `Medium→Low`."

**Resolution:** Severities adjusted per consensus table above. The dissenter's concern that premature decomposition risks fragmentation is noted and mitigated by one-at-a-time extraction with build+test validation between each collaborator.

### Source-Grounded Archivist dissents on cross-repo task mapping

> "Audit v4 §'Existing work already in motion' references tasks TSK-0042 through TSK-0050 from the MemorySmith repository (ChatServices, PagesAndChatTests, TaskDomainService). These address the MemorySmith web app, NOT MemorySmith.Agent. This is a scoping error that could misdirect sprint capacity."

**Resolution:** Confirmed. TSK-0042–0050 are in `d:\@Repos\MemorySmith\Data\Tasks\` and address MemorySmith.App concerns. They are excluded from the Agent sprint roadmap. The audit addendum should correct this.

---

## Acceptance Criteria (Cross-Sprint)

1. **Build gate:** `dotnet build` 0w/0e at every sprint boundary
2. **Test gate:** All existing tests pass + new integration tests at each sprint
3. **Pipeline singularity:** By end of S51, one canonical pipeline documented; no code path violates it
4. **Constructor shrink:** By end of S52, `AgentBackgroundService` ≤6 params, ≤500 lines
5. **Monolith split:** By end of S52, `HtnTaskLibrary.cs` ≤150 lines (registry only)
6. **Regex elimination:** By end of S53, zero production paths depend on regex for coordinate extraction
7. **Integration coverage:** By end of S53, ≥3 host-level integration tests at cross-component seams
8. **No silent regressions:** Every deleted bridge gated by integration test proving replacement works

---

## What Changes Now vs Later vs Never

| When | What |
|:-----|:-----|
| **Now** (S51) | Classify bridges, canonicalize pipeline docs, fix SearchMemoryTool regex, fix lying comment, small cleanup |
| **Now** (S52) | Extract AgentBackgroundService collaborators, split HtnTaskLibrary, add gather memory-path routing |
| **Now** (S53) | Structured memory metadata, integration tests, delete obsolete bridges, typed PlanContext expansion |
| **Later** (S54+) | Dashboard event bus (TSK-0042–0049), SignalR real-time push, LLM evaluator concrete impl, ToolDispatcher internal split |
| **Never** | Remove deterministic fast-paths for CancelGoal/QueryStatus/QueryInventory/QueryHelp (permanent per ADR D-003) |
| **Never** | Fully eliminate `Dictionary<string, object?>` context (typed facade is sufficient; escape hatch preserved) |

---

## Open Questions

1. **Should `AgentBackgroundService` split be driven by runtime loops or dataflow boundaries?** Council recommends loop-based extraction (connection → plan → execute → observe) for S52 — maps to existing code structure.
2. **Should gather workflows always use SearchMemory for spatial targeting?** Council recommends: make it the preferred path when coordinates exist; fall back to `Wander`+`FindBlock` when memory unavailable. Design in S52, implement S53.
3. **Is `_agentRuntime` dead code or migration in progress?** If migration, which sprint targets integration? Needs resolution in S51.
4. **Does the MemorySmith server team have bandwidth for structured metadata in search API?** Required for S53 TSK-0144. Cross-repo coordination needed.
5. **What does LLM failure-path coverage look like?** Neither audit evaluated. Should be a separate assessment in S52.
6. **Should `ToolResult.Data` and `JournalEntry.Details` also convert from `Dictionary<string, object?>?` to typed records?** Defer to S54+ — lower priority than PlanContext typing.
7. **Should `PlanContext` be mutable (mirroring current dictionary) or immutable (like `ReplanGoalContext`)?** Council recommends mutable for Phase 1 (S52), evaluate immutability in Phase 3 (S53).

---

## Confidence Summary

| Area | Confidence |
|---|---:|
| Audit factual accuracy | 90% |
| Severity adjustments | 85% |
| Sprint 51 feasibility | 95% |
| Sprint 52 feasibility | 82% |
| Sprint 53 feasibility | 85% |
| Missed priorities completeness | 80% |
| **Overall council confidence** | **86%** |

---

## Council Seat Signatures

- **Source-Grounded Archivist** — 85% confidence. Audits accepted with addendum for cross-repo task scoping and ADR D-003 context.
- **Data Model Architect** — 87% confidence. Typed PlanContext + ObservationStub + StructuredMemoryHint recommended; incremental migration path validated.
- **Retrieval Specialist** — 87% confidence. Top-hit-only limitation is Critical; SearchMemory not integrated into gather; regex guards needed.
- **Human Learning Advocate** — 87% confidence. Onboarding Hard, docs 5/10; single canonical pipeline diagram is highest-impact doc fix.
- **Skeptical Reviewer** — 82% confidence. Three severity downgrades; five missed priorities identified; alternative interpretation offered.
- **Synthesizer** — 88% confidence. 3-sprint roadmap; defer dashboard; highest-impact single change is extracting AgentBackgroundService collaborators.
