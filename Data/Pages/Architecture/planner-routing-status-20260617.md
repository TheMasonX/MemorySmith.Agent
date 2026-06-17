# Planner Routing — Architecture Inventory
**Date:** 2026-06-17  
**Sprint:** 16 (Phase 7-A)  
**Scope:** `Agent.Planning/Router/PlannerRouter.cs` and related planner infrastructure  
**Purpose:** Authoritative record of which planning paths are implemented vs. aspirational.  
Future agents: read this before modifying `PlannerRouter.cs` or adding strategy paths.

---

## Summary

The planning layer routes a goal to a planner through `PlannerRouter.Select`. As of Sprint 16, **two paths are implemented and two are aspirational placeholders**. This document records the status of all four.

---

## Path inventory

### [IMPLEMENTED] Path 1 — GoalDecomposer (preferred)

**File:** `Agent.Planning/Router/PlannerRouter.cs`  
**Code:** `if (registry.Find(goal) is { } decomposer) return new DecomposerPlanner(decomposer);`

`DecomposerRegistry.Find(goal)` iterates registered `IGoalDecomposer` instances and returns the first whose `CanHandle(goal)` returns true. If found, it is wrapped in a `DecomposerPlanner` adapter and returned.

**Registered decomposers** (wired in `Program.cs`):
| Decomposer | Handles |
|------------|---------|
| `BuildGoalDecomposer` | goals whose Name starts with "Build:" |
| `GatherGoalDecomposer` | goals whose Name starts with "Gather:" or "GatherItem:" |
| `SurviveNightGoalDecomposer` | goals whose Name starts with "SurviveNight" |

**Status:** Production. Used by the agent loop on every plan cycle.

---

### [IMPLEMENTED] Path 2 — HTN fallback

**File:** `Agent.Planning/Router/PlannerRouter.cs`  
**Code:** `return htnPlanner;`

When no registered decomposer matches, `HtnPlanner` is returned as the fallback. `HtnPlanner` delegates to `HtnTaskLibrary` which maps task names to `TaskDecomposer` delegates.

**Registered HTN task names** (in `HtnTaskLibrary` constructor):
`GatherWood`, `FindTree`, `MineWood`, `Collect`, `SurviveNight`, `FindShelter`, `LightArea`, `WaitForSunrise`, `Wander`, `Explore`, `FindFlatArea`

**Status:** Production. Final fallback for all goals not handled by registered decomposers.

---

### [ASPIRATIONAL] Path 3 — GOAP

**Declared:** `PlannerStrategy.Goap` enum value  
**Implemented:** NO. Not referenced anywhere in `PlannerRouter.Select`.  
**Reserved for:** Phase 7-E planner migration (Sprint 21 estimate per audit synthesis roadmap).  
**Do not add routing logic until:** Phase 7-E design is approved by council.

GOAP would replace the current goal→task→action decomposition with a backward-chaining search over action preconditions/effects. This requires a world-state schema that the current `WorldState.Facts: Dictionary<string, object?>` does not provide. Phase 7-D (belief layer) must land first.

---

### [ASPIRATIONAL] Path 4 — LLM-assisted planning

**Declared:** `PlannerStrategy.LlmAssisted` enum value  
**Implemented:** NO. Not referenced anywhere in `PlannerRouter.Select`.  
**Reserved for:** Phase 7-E (Sprint 21 estimate).  
**Constraint:** Per ADR D-003 (deterministic-first), LLM is a last resort only. If LLM-assisted planning is added, it must come AFTER the GOAP path, not before HTN.

LLM-assisted planning would construct action sequences by prompting an LLM with world state and goal context. It carries the latency and reliability issues documented in D-003. It should only fire for genuinely novel goals that neither GOAP nor HTN can decompose.

---

## What has NOT been built yet (Phase 7 context)

The synthesis council (`audit-synthesis-council-20260617.md`) identified these planning-adjacent gaps:

| Gap | Phase | Sprint estimate |
|-----|-------|----------------|
| Observation pipeline — typed, normalized adapter events | 7-C | Sprint 18 |
| Belief layer (`IBeliefState`) | 7-D | Sprint 19 |
| Planner input migration to world model + beliefs | 7-E | Sprint 21 |
| GOAP strategy | 7-E | Sprint 21 |
| LLM-assisted strategy | 7-E | Sprint 21 |

Until Phase 7-E, the planner consumes raw `WorldState` facts directly. Adding GOAP or LLM paths before the belief layer exists would produce planners that work against stale or under-typed world state.

---

## Key files

| File | Role |
|------|------|
| `Agent.Planning/Router/PlannerRouter.cs` | Main routing logic (`Select`) + `PlannerStrategy` enum |
| `Agent.Planning/Router/` (directory) | Router, registry, decomposer interface |
| `Agent.Planning/Decomposition/` | Registered decomposer implementations |
| `Agent.Planning/HtnPlanner.cs` | HTN fallback planner |
| `Agent.Planning/HtnTaskLibrary.cs` | Named task decomposition methods |
| `WebUI.Blazor/Program.cs` | DI wiring — registers decomposers + `PlannerRouter` |

---

## How to add a new decomposer (do NOT add a new .csproj)

1. Create `Agent.Planning/Decomposition/FooGoalDecomposer.cs` implementing `IGoalDecomposer`.
2. `CanHandle` should check `goal.Name.StartsWith("Foo:", StringComparison.OrdinalIgnoreCase)`.
3. Register in `Program.cs`: `reg.Register(new FooGoalDecomposer(lib));`.
4. Add tests in `MemorySmith.Agent.Tests/`.
5. Update this document.

Do NOT add GOAP or LLM routing to `PlannerRouter.Select` until Phase 7-E is scoped.
