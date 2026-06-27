# MemorySmith.Agent

## Engineering Audit

### Branch: sprint-35-llm-first

### Baseline Commit: f6ab1c02990de81f078cf723cfdb4f9825f7ef9a

---

# Executive Summary

## Overall Assessment

The project continues to trend in a positive architectural direction. Compared to earlier audits, several high-risk implementation defects have been resolved, particularly around schema-aware context propagation and tool parameter isolation. The remaining concerns are increasingly architectural rather than algorithmic.

The primary risk is no longer incorrect behavior; it is architectural divergence during the migration from legacy deterministic systems toward the intended LLM-first planning model. Multiple transitional compatibility layers are now coexisting successfully, but they should remain temporary. If left in place indefinitely, they will become the next generation of technical debt.

## Overall Health

Architecture: Good (Improving)

Implementation Quality: Good

Testing: Moderate

Technical Debt: Low, but increasing if transitional code is retained

Migration Progress: Approximately halfway through the intended architecture

Overall Confidence: 90%

---

# Priority Findings

Each finding should exist only once. Future audits update the status rather than creating duplicates.

Status values:

* Open
* In Progress
* Mitigated
* Closed
* Deferred

---

## DA-001 — Runtime Architecture and Target Architecture Have Diverged

Severity: Critical

Status: Open

Confidence: 92%

### Summary

Current runtime behavior does not completely match the architecture described in the Sprint 35 planning documents.

Documentation describes an LLM-first architecture:

User Input
→ IntentDraft
→ Planner
→ Goals
→ Execution

Current runtime still includes direct regex-driven goal creation paths.

### Why This Matters

Maintaining two architectural models creates uncertainty about where future features belong. Contributors can reasonably implement either interpretation, leading to duplicated logic and inconsistent abstractions.

### Evidence

* Chat interpretation still creates executable goals directly in portions of the runtime.
* Sprint planning documents describe parsers producing intermediate intent rather than executable goals.
* Transitional components remain active without clearly defined retirement criteria.

### Recommendations

Define one canonical execution pipeline and explicitly classify every remaining legacy path as either:

* permanent deterministic fast-path,
* temporary migration layer, or
* obsolete.

Update implementation and documentation together whenever the canonical pipeline changes.

### Success Criteria

* Every user request follows one documented planning pipeline.
* Intent generation has a single owner.
* Regex parsing is limited to clearly defined deterministic commands.

---

## DA-002 — Transitional Compatibility Layers Require Retirement Plans

Severity: High

Status: Open

Confidence: 89%

### Summary

Several compatibility bridges introduced during migration are appropriate today but lack defined removal milestones.

Examples include:

* MoveTo coordinate fallback behavior
* Context-carry compatibility
* Legacy parsing paths

### Risks

Temporary compatibility code tends to become permanent unless explicitly scheduled for removal.

This increases maintenance cost and obscures the intended architecture.

### Recommendations

Every migration bridge should define:

* owner
* purpose
* replacement
* removal criteria
* target sprint

No compatibility layer should exist without an exit strategy.

### Success Criteria

Each bridge is traceable to a work item and can be deleted without architectural redesign.

---

## DA-003 — Context Propagation Remains Loosely Typed

Severity: High

Status: In Progress

Confidence: 88%

### Summary

Schema-aware filtering has significantly improved execution safety, but execution context is still fundamentally represented as a generic dictionary.

The current implementation prevents many classes of accidental parameter injection, yet it still relies on convention rather than strong typing.

### Risks

* Hidden contracts
* String key drift
* Difficult refactoring
* Runtime-only validation
* Reduced IDE support

### Recommendations

Gradually replace dictionary-based execution context with explicit effect models, for example:

* SpatialTarget
* InventoryObservation
* RecipeObservation
* BlockObservation
* EntityObservation

Typed effects improve discoverability, validation, and long-term maintainability.

### Success Criteria

Execution context becomes self-describing and compile-time validated wherever practical.

---

## DA-004 — Integration Testing Lags Behind Architectural Complexity

Severity: High

Status: Open

Confidence: 91%

### Summary

Current unit tests validate individual tool behavior effectively but provide limited assurance that the complete execution pipeline behaves correctly.

The highest-risk areas now exist at subsystem boundaries rather than within isolated components.

### Recommendations

Increase end-to-end testing around:

* planner → dispatcher
* dispatcher → schema validation
* context propagation
* repair loops
* execution retries
* observation feedback

Focus future testing on architectural seams rather than isolated methods.

### Success Criteria

Every execution pipeline has at least one comprehensive integration test exercising production code paths.

---

## DA-005 — Memory Contracts Should Become Structured

Severity: Medium

Status: Open

Confidence: 84%

### Summary

Several systems currently recover structured information by parsing presentation-oriented text.

This approach is acceptable during early development but becomes increasingly fragile as the project grows.

### Recommendations

Expose structured metadata directly from the memory layer wherever possible.

Examples include:

* coordinates
* inventory
* entity identifiers
* observations
* recipes

Treat rendered text as presentation rather than an API.

### Success Criteria

Consumers no longer depend on regex extraction when structured metadata exists.

---

## DA-006 — Background Service Owns Too Many Responsibilities

Severity: Medium

Status: Open

Confidence: 86%

### Summary

The background service increasingly coordinates planning, execution, observation, retry logic, dashboard updates, and orchestration.

Although manageable today, continued growth risks creating a high-coupling orchestration class.

### Recommendations

Continue extracting cohesive services, for example:

* ExecutionCoordinator
* ObservationPipeline
* RetryCoordinator
* RepairEngine
* DashboardPublisher

The background service should orchestrate rather than implement behavior.

### Success Criteria

Responsibilities are clearly separated into independently testable services.

---

# Findings Improved Since Previous Audits

## Context Carry

Status: Mitigated

Summary

Schema-aware filtering substantially reduced accidental parameter propagation.

Remaining work focuses on stronger typing rather than correctness.

---

## Dispatcher Validation

Status: Improved

Summary

Validation now better reflects tool contracts.

Future improvements should focus on interface boundaries rather than validation correctness.

---

## MoveTo Compatibility

Status: Acceptable

Summary

Current compatibility behavior is appropriate during migration.

Long-term goal remains removal once upstream planning guarantees coordinate availability.

---

# Recommended Implementation Roadmap

## Phase 1 — Architectural Consolidation

* Finalize canonical planning pipeline.
* Align implementation and documentation.
* Document all migration bridges.

## Phase 2 — Contract Hardening

* Introduce typed execution effects.
* Replace string-based context where practical.
* Expand structured memory APIs.

## Phase 3 — Testing Expansion

* Increase integration coverage.
* Add regression tests for planner, dispatcher, repair, and observation loops.

## Phase 4 — Technical Debt Elimination

* Remove obsolete compatibility layers.
* Delete legacy execution paths.
* Simplify orchestration.

---

# Open Questions

* Should deterministic regex parsing remain permanently for simple commands?
* Should execution context evolve into strongly typed effect records?
* Should observation become event-driven rather than request-driven?
* At what point should compatibility bridges be removed rather than extended?

---

# Confidence Summary

| Area                      | Confidence |
| ------------------------- | ---------: |
| Runtime Findings          |        94% |
| Architectural Findings    |        90% |
| Testing Assessment        |        88% |
| Migration Recommendations |        86% |
| Overall Assessment        |        90% |
