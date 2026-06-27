# Survival vs Creative Build Decomposition — Analysis

**Date:** 2026-06-26
**Author:** SteveBot
**Status:** 🟡 Analysis Complete — No Task Exists for Unification

---

## Current Architecture

The build decomposition lives in a single method `HtnTaskLibrary.DecomposeBuild()` (~300 lines) with an `if (isCreative) { ... } else { ... }` branch at line 497. Both modes share the same origin resolution, checkpoint logic, vegetation clearing, and self-position skip — but diverge on what actions they emit.

### Shared Path (both modes)

```
Origin resolution (BuildOrigin value object, TSK-0107)
  └─ Explicit from chat → use as-is
  └─ Auto-detect (facts → FindFlatArea)
  └─ requireOrigin gate: emit FindFlatArea if no origin resolved

BlueprintExecutor → block actions
Checkpoint resume (BuildProgressIndex fact)
Vegetation clearing (replaceable blocks mined before placement)
Self-position skip (TSK-0123)
PlaceBlock loop with progress context
GetStatus (final action)
```

### Creative Branch (`if (isCreative)`)

```
Skip: material gathering, tool ensures, crafting chain
PlaceBlock actions only (no MoveTo origin — TSK-0121 removed it)
GetStatus (final action)
Leverages adapter-side creative inventory fallback (Sprint 51 Wave B)
```

### Survival Branch (`else`)

```
EnsureToolsForBlocks (pickaxe, axe, etc.)
Material gathering loop:
  For each non-crafted, non-torch material:
    Wander → MineBlock (if insufficient in inventory)
Torch material: MineBlock for coal_ore if needed (torch burning)
Iron ingot: MineBlock iron_ore → SmeltItem (if needed)
Crafting chain (BuildCraftingChain) — planks, tools, etc.
[Then falls through to the shared PlaceBlock loop + GetStatus]
```

### Routing Paths

```
Goal created → PlannerRouter
  ├─ BuildGoalDecomposer (preferred) → HtnTaskLibrary.DecomposeBuild()
  └─ HtnPlanner fallback → HtnTaskLibrary.DecomposeBuild()
```

Both paths converge on the same `DecomposeBuild` method. The old `HtnPlanner.CreateCreativeBuildActions` was removed in Sprint 50 Wave B (TSK-0116) — it was dead code because `PlannerRouter` prefers the decomposer path.

---

## What Was Discussed in Audits vs What Was Actually Tracked

### Council Consensus (audit-findings-consolidation-council, Sprint 50→51)

The 6-seat council's Sprint 52 plan called for extracting **HtnTaskLibrary** into separate decomposers:

| Planned | Task? | Status |
|:--------|:------|:-------|
| Extract GatherTaskDecomposer | ❌ No task | Gap |
| Extract CraftTaskDecomposer | ❌ No task | Gap |
| Extract SmeltTaskDecomposer | ❌ No task | Gap |
| Extract BuildTaskDecomposer | ❌ No task | Gap |
| Extract ExploreTaskDecomposer | ❌ No task | Gap |
| Introduce typed PlanContext | ❌ No task | Gap |

The current Sprint 52 task set (TSK-0146 through TSK-0151) focuses entirely on **entity awareness + ScenePack** — nothing about build decomposition or creative/survival unification.

### What WAS Tracked and Delivered

| Item | Task | Status |
|:-----|:-----|:-------|
| Remove dead creative branch from HtnPlanner | TSK-0116 | ✅ Done (S50 Wave B) |
| BuildOrigin sentinel elimination | TSK-0107 | ✅ Done (S50 Wave B) |
| Creative inventory fallback in adapter | Sprint 51 Wave B | ✅ Done (no task) |
| Self-position block skip | TSK-0123 | ✅ Done (S50 Wave A) |
| Rehome-to-origin removal (creative) | TSK-0121 | ⚠️ Reopened — fix didn't hold |
| Remove MoveTo-to-origin from replans | TSK-0121 comment | 🔴 Still happening per user |

### What Was NOT Tracked (Gaps)

| Gap | Source | Why It Matters |
|:----|:-------|:--------------|
| **Unified build plan** — one path for both modes with mode-specific action injection points | Multiple audits, council S52 plan | Currently `if/else` in a 300-line method. Changes to one branch risk the other. |
| **IBuildGoal marker interface** | Roadmap P1 | Replace `goal is BuildGoal` type-check |
| **HtnTaskLibrary decomposition** | Council S52 plan | Split into focused decomposers |
| **Typed PlanContext** | Council S52 plan | Replace `Dictionary<string, object?>` context |

---

## Why Unification Matters Now

The user's intuition is correct: **creative and survival builds share most of the same structure** — origin resolution, block placement loop, checkpoint resume, vegetation clearing, self-position skip. The differences are:

1. **Survival needs pre-placement material gathering** (MineBlock, SmeltItem, CraftItem)
2. **Creative doesn't** — but the adapter now handles creative inventory internally (S51 Wave B)

Everything else — the placement loop, the origin resolution, the checkpoint tracking, the vegetation clearing — is identical.

A unified approach would:
- **Eliminate the `if/else` in `DecomposeBuild`** — replace with a shared build pipeline that accepts an `IBuildMaterialProvider` or similar strategy
- **Make the build path testable in either mode** — survival tests verify gather/craft, creative tests verify instant placement
- **Prevent regression** — today a change to the survival path can accidentally break creative (or vice versa) because they share the same method scope
- **Prepare for GOAP/LLM-driven planning** — a unified pipeline is easier to reason about

### Concrete Proposal

Extract a shared `BuildActionPipeline` that both modes use:

```
BuildActionPipeline.BuildPlan(blueprint, blocks, origin, state, materialProvider)
  ├─ Origin resolution (shared)
  ├─ Material provisioning (injected strategy)
  │   ├─ SurvivalMaterialProvider: Wander→MineBlock→SmeltItem→CraftItem
  │   └─ CreativeMaterialProvider: no-op (creative inventory handles it)
  ├─ Vegetation clearing (shared)
  ├─ PlaceBlock loop with checkpoint (shared)
  └─ GetStatus (shared)
```

This keeps the divergence injected at a single point (material provisioning) rather than splitting the entire build method in two.

---

## Next Steps

1. **Create a task for `BuildActionPipeline` extraction** — Sprint 52 or 53
2. **Create a task for `IBuildGoal` marker interface** — Sprint 53
3. **Revisit TSK-0121** — the rehome-to-origin fix was claimed in S50 but the user says it's still happening. Needs investigation
4. **Add a test that builds the same blueprint in both creative and survival mode** — verifies the placement loop is identical

---

## References

- `Agent.Planning/HtnTaskLibrary.cs` lines 422-650 (DecomposeBuild)
- `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` (delegates to DecomposeBuild)
- `Agent.Planning/HtnPlanner.cs` lines 36-53 (fallback build path)
- `Data/Pages/council/audit-findings-consolidation-council-2026-06-26.md` (Sprint 52 extraction plan)
- `Data/Pages/Handoffs/sprint-50-waveb-council-buildorigin-creative.md` (TSK-0116 dead code removal)
- `Data/Pages/Handoffs/sprint-51-wave-b-handoff-2026-06-26.md` (creative inventory fallback)
- `Data/Tasks/tsk-0116-move-creative-build-handling-into-decomposer-layer.json` (✅ Done)
- `Data/Tasks/tsk-0121-rehome-to-origin-after-every-block.json` (⚠️ Reopened)
