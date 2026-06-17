# Sprint 6 Architecture Spec Review — Council Assessment

**Date:** 2026-06-16
**Scope:** Cross-reference of external architecture spec against current codebase
**Prior sprint:** Sprint 5 (tool safety & memory lifecycle)

---

## Council Verdict

The spec describes a target architecture that is **70% already implemented** across Sprints 1-5. The remaining 30% is genuinely greenfield. We reject the spec's proposal to create 6 new `.csproj` projects — the existing module boundaries (Agent.Core, Agent.Planning, Agent.Tools) are the right home for new types. Creating new projects for `Agent.Runtime.Journal`, `Agent.WorldModel`, etc. would violate the deletion-test principle: delete Agent.Core and the complexity reappears across every consumer.

---

## Spec Item Audit

| # | Spec Claim | Status | Actual Location | Assessment |
|---|---|---|---|---|
| 1 | `Agent.Security` / `IToolValidator` | **Implemented** (Sprint 5) | `ToolDispatcher.cs` L48-131 | Schema validation in `CallAsync`. No separate class needed — deletion test: extracting it to a separate interface would add indirection without removing complexity. |
| 2 | Minecraft process supervision | **Implemented** (Sprint 5) | `MinecraftAdapter.cs` L35-88 | SIGTERM → 5s wait → SIGKILL. Already handles stdout/stderr via WebSocketBridge. |
| 3 | Memory cache eviction | **Implemented** (Sprint 5) | `WorldState.cs` L10-11, L55-63 | MaxFacts=1000, FIFO eviction, FactSource provenance. |
| 4 | Replan context preservation | **Implemented** (Sprint 5) | `HtnPlanner.cs` L77-133 | 5 prefixes preserved across replans. |
| 5 | `IGoalDecomposer` interface | **MISSING** | n/a | Delegate pattern exists (`TaskDecomposer` in `HtnTaskLibrary.cs`) but no formal interface for registration/discovery. |
| 6 | WorldModel / BeliefState | **Partial** | `WorldState.cs`, `WorldStateProjector.cs` | Observation→Projection exists. Prediction→Reconcile loop does NOT exist. No uncertainty quantification. |
| 7 | `AgentJournal` | **MISSING** | n/a | No structured execution history. Only ILogger<string> calls. |
| 8 | `PlannerRouter` | **MISSING** | n/a | Single planner (HtnPlanner). No dynamic selection. |
| 9 | `ReflectionEngine` | **MISSING** | n/a | No post-execution reasoning or memory synthesis. |
| 10 | `Simulator` | **MISSING** | n/a | No forward-prediction of action outcomes. |

---

## Council Decision: Sprint 6 Scope

### P0 — Observability (greenfield)
- **AgentJournal** (new): event-sourced execution history in Agent.Core/Models
- Wire into AgentBackgroundService: goal set, goal cancel, plan created, action dispatched, action outcome, replan triggered
- Wire into ToolDispatcher: every CallAsync logs success/failure with schema validation result

### P1 — World Model Depth (existing module deepening)
- **IWorldModel** interface in Agent.Core/Interfaces
- **BeliefState**, **ObservationState**, **PredictionState** records in Agent.Core/Models
- Observation → Projection → Prediction → Reconcile loop (rule-based, not ML)
- Uncertainty quantification (simple: accumulate deviation between predicted and observed)

### P2 — Planner Extensibility (existing module deepening)
- **IGoalDecomposer** interface replacing the `TaskDecomposer` delegate
- **DecomposerRegistry** decoupling HtnPlanner from concrete decomposers
- **PlannerRouter** for dynamic planner selection (HTN default, GOAP future)

### P3 — Reflection & Simulation (Phase 6+)
- **ReflectionEngine**: journal compression, anomaly detection, MemorySmith writeback
- **Simulator**: rule-based plan rollout evaluation

### Non-goals
- No new `.csproj` files — all new types go into existing modules
- No multi-agent systems
- No ML training loops
- No GOAP engine (interface prep only)
- No replacement of deterministic paths with LLM paths

---

## Confidence

| Area | Confidence | Rationale |
|---|---|---|
| Journal design | 0.95 | Event-sourcing pattern is well-understood; simple implementation |
| World model depth | 0.75 | Observation→Reconcile is new territory; rule-based prediction for Minecraft is constrained |
| Decomposer registry | 0.85 | Delegates already exist; interface formalization is straightforward |
| PlannerRouter | 0.70 | Single-implementation router is trivial; value emerges when GOAP is added |
| Reflection engine | 0.50 | Depends on journal being in place first; design will evolve |

---

## Seat Votes

| Seat | Verdict |
|---|---|
| Architect (1) | Reject 6 new .csproj files. All new types go into existing modules. Journal belongs in Agent.Core. |
| Safety (2) | Tool validation already addressed in Sprint 5. Journal adds audit trail — strongly endorse. |
| Planner (3) | IGoalDecomposer is the right abstraction. Delegate pattern is fine but blocks extensibility. |
| World Modeler (4) | WorldModel deepening is the highest-value P1. Observation→Reconcile closes the cognitive loop. |
| Runtime (5) | Journal + per-action logging will improve debuggability. No concerns. |
| Integrator (6) | Tests for journal, world model, and decomposer registry must be added alongside. |

**Unanimous**: Sprint 6 starts with Journal (P0), then World Model (P1), then Planner (P2).
