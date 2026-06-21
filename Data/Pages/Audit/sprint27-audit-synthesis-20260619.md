# Sprint 27 Audit Synthesis — External Audit Intake

**Date**: 2026-06-19  
**Branch**: `sprint-5-tool-safety` @ 08942cdb (Sprint 27 P0 implementation)  
**Audits reviewed**: 3 new external audits in Data/Pages/Audit/

---

## New Audit Files

### 1. memorysmith_agent_deep_code_audit_sprint5.md (13,265 bytes)

Deep code audit of Sprint 5–6 architecture seams. Independent review via PR #1 branch.

| Finding | Severity | Confidence | Sprint 27 Status |
|---|---|---|---|
| Tool safety TODO still present (ToolDispatcher schema validation) | CRITICAL 97% | was OPEN → **RESOLVED** (Sprint 25 P0-C) |
| WorldState mutable collection exposure | HIGH 92% | was OPEN → **PARTIALLY RESOLVED** (Sprint 25 P1-A fixed aliasing; get;init; still exposes mutable collections — DEF-NEW) |
| Replanning loses failure context (DecomposerPlanner.ReplanAsync drops failureReason) | HIGH 90% | **OPEN** — deferred |
| BuildGoalDecomposer.ReadOriginFact silent (0,0,0) fallback | HIGH 88% | **OPEN** — tracked as DEF-NEW-1 |
| Journal approximately bounded (best-effort) | MEDIUM 87% | **DELIBERATE** — formally closed in P1-B |
| Goal decomposer routing order-dependent | MEDIUM 84% | **OPEN** — deferred to Sprint 28 |
| Blueprint lookup broader than documented | MEDIUM 81% | **OPEN** — deferred |

### 2. memorysmith_agent_audit_addendum.md (6,030 bytes)

Supplemental findings on gather/explore/decomposer patterns.

| Finding | Severity | Confidence | Sprint 27 Status |
|---|---|---|---|
| GenericGatherGoal failure key collision (no targetCount in key) | MEDIUM-HIGH 93% | **OPEN** — DEF-NEW-2 |
| ExploreDecompose hardcoded two-pass loop | MEDIUM 89% | **OPEN** — deferred |
| GatherItemDecompose hardcoded wander radius=40 | MEDIUM 90% | **OPEN** — deferred |
| MineWoodDecompose duplicates gather pattern | MEDIUM 86% | **OPEN** — deferred |
| GoalFactory.GetInt silently truncates long→int | MEDIUM 84% | **OPEN** — DEF-NEW-3 |
| GoalFactory sync/async asymmetry | MEDIUM 76% | **OPEN** — deferred |

### 3. memorysmith_agent_code_audit_report.md (12,205 bytes)

Code audit focused on gather count, source block limits, and planner policy.

| Finding | Severity | Confidence | Sprint 27 Status |
|---|---|---|---|
| Gather count lost in planning (HtnPlanner passes empty params) | HIGH 97% | **RESOLVED** (Sprint 26 P0-B — IItemSpecGoal.TargetCount DIM) |
| Generic gather only considers Take(2) source blocks | HIGH 90% | **OPEN** — DEF-NEW-4 |
| Hardcoded planner numbers violate repo policy | MEDIUM-HIGH 95% | **OPEN** — deferred |
| Generic gather too vanilla-biased for mod support | MEDIUM-HIGH 88% | **ARCHITECTURAL** — long-term |
| Replan drops context, swallows exceptions | MEDIUM 84% | **PARTIALLY OPEN** — context preservation added in Sprint 5; ReplanAsync still swallows |
| GoalFactory sync/async asymmetry | MEDIUM 72% | **OPEN** — deferred |

---

## Cross-Audit Convergence

| Theme | Audits | Priority |
|---|---|---|
| Planner routing consolidation | All 3 | ✅ Sprint 27 P0-D (DELIVERED) |
| WorldState mutability | A1, A3 | DEF-NEW-5 (deferred) |
| Tool safety seam | A1, A3 | ✅ Sprint 25 (RESOLVED) |
| Gather count correctness | A2, A3 | ✅ Sprint 26 (RESOLVED) |
| Silent fallbacks (origin=0, long truncation) | A1, A2 | Tracked as DEF-NEW-1, DEF-NEW-3 |

---

## New Deferred Findings (DEF-NEW-*)

| ID | Finding | Priority | Target |
|---|---|---|---|
| DEF-NEW-1 | BuildGoalDecomposer.ReadOriginFact silent (0,0,0) — log a warning on fallback | P1 | Sprint 28 |
| DEF-NEW-2 | GenericGatherGoal failure key collision — include targetCount in key | P1 | Sprint 28 |
| DEF-NEW-3 | GoalFactory.GetInt long→int truncation — add range check | P2 | Sprint 28 |
| DEF-NEW-4 | GatherItemDecompose Take(2) source block limit | P2 | Sprint 28 |
| DEF-NEW-5 | WorldState get;init; still allows downstream mutation of collections | P2 | Sprint 28 |

---

## Sprint 27 Delivered (P0 scope)

- **P0-A**: AgentBackgroundServiceTestHelper.BuildMinimal — closes BLK-1 ✅
- **P0-B**: Version 0.27.0 unified (Program.cs + README) — closes BLK-2 ✅
- **P0-C**: ITimeProvider + SystemTimeProvider + FakeTimeProvider; 32 DateTimeOffset.UtcNow calls replaced ✅
- **P0-D**: CraftItemGoalDecomposer created; PlannerRouter now implements IPlanner; HtnPlanner type-switch branches removed; GatherGoalDecomposer redundant arm removed ✅
