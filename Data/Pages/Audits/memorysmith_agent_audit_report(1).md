# MemorySmith.Agent Deep Dive Audit
Branch: `dev/round-3`
Latest commit reviewed: `cd60bbe73b44d8b07e7cf824c478f4da34ddd8c6`

## Scope note
I reviewed the repo’s surfaced source paths and the active creative/build/recovery pipeline in depth, plus the sprint/task backlog to avoid duplicating already-tracked work. I cannot honestly claim literal every-line coverage of every file in the repository because the connector did not expose a full tree listing, but the critical paths tied to the regression were traced end-to-end.

## Executive summary

### What is most likely happening
The creative-mode regression is not a single bug. It is a policy split across three layers:

1. **Planner layer** already has a creative-mode branch for builds, but **gather decomposition still emits survival-style mining actions**. `DecomposeBuild()` skips pre-gather in creative mode, while `GatherItemDecompose()` continues to emit `Wander` + `MineBlock` with no creative special case. fileciteturn47file0 fileciteturn49file0
2. **Adapter layer** has a creative fallback for `place`, but it still verifies success against `bot.inventory.items()`, which is an unreliable proxy for creative inventory state on some Mineflayer versions. fileciteturn36file0 fileciteturn8file0
3. **Host layer** still has a creative `/give` provisioning fallback in `SetGoal()` for build goals, while recovery logic skips gather recovery in creative mode only after the error is already detected. That means creative behavior is not centralized; it is split between planner, host, and adapter. fileciteturn17file0 fileciteturn41file0

### Highest-confidence findings
- **High confidence (95%)**: the latest commit only touches `WebUI.Blazor/AgentBackgroundService.cs`; it is not a broad fix to the creative pipeline. The regression is therefore almost certainly an interaction/architecture issue, not a newly introduced large diff.  
- **High confidence (90%)**: creative-mode behavior is inconsistent across layers and still relies on legacy survival assumptions in at least one gather path.  
- **Medium confidence (75%)**: the specific symptom “creative mode no longer grants items” is caused by stale or mismatched creative provisioning contracts: the host expects `/give`-style provisioning for builds, the adapter expects creative inventory fallback for placement, and gather recovery still assumes mining is the right next step when a material is missing. fileciteturn30file0 fileciteturn41file0

### Bottom line
This codebase is already showing the classic failure mode of “greenfield with legacy bridges”: one path says creative means “provision items,” another says creative means “skip gathering,” and another still mines anyway. The fix should be a single creative-mode policy surface, not another local patch.

## Actionable findings

### 1) Creative gather still mines instead of provisioning or short-circuiting
**Severity:** Critical  
**Confidence:** 90%

`GenericGatherGoal.IsComplete()` intentionally no longer auto-completes in creative mode, but the decomposition path for gathering still emits survival mining actions. That is the exact shape of the regression: creative mode does not mean “gather goal is handled differently,” it only means one specific build path is handled differently. fileciteturn10file0 fileciteturn49file0

**Why it matters**
- A creative-mode gather goal will still plan `Wander` + `MineBlock`.
- If creative inventory is empty in `bot.inventory`, the adapter can throw “not in inventory,” which then pushes the system toward recovery logic.
- That recovery logic may skip or redirect in creative mode, but the primary plan is still wrong.

**Recommended fix**
- Add an explicit creative branch to gather decomposition, or make creative gather impossible by design with a clear failure reason.
- Do not depend on recovery to “fix” a plan that should never have been generated.

---

### 2) Host-side creative provisioning is still goal-type specific and stale
**Severity:** High  
**Confidence:** 85%

`AgentBackgroundService.SetGoal()` still triggers `ProvisionGoalIfCreativeAsync()` for creative mode, but that helper only handles `IBuildGoal`. It enqueues `/give` chat actions for build materials and then requests `GetStatus`. It does not generalize to gather goals. fileciteturn17file0

**Why it matters**
- The system currently treats “creative” as a build-material provisioning feature, not as a global inventory policy.
- The task record `tsk-0190` claims `/give` provisioning was removed from `SetGoal`, but the live code still contains that path. That is task/doc drift. fileciteturn28file0
- This inconsistency makes the behavior brittle and hard to reason about.

**Recommended fix**
- Choose one owner for creative provisioning: planner, host, or adapter.
- If adapter is authoritative, remove `/give` provisioning from the host and replace it with a single deterministic status/provision handshake.
- If host is authoritative, make it cover every goal type that depends on inventory, not only builds.

---

### 3) Creative inventory verification is brittle in the adapter
**Severity:** High  
**Confidence:** 80%

`MineflayerAdapter/index.js` uses `bot.inventory.items()` as the verification signal after creative fallback. That is a weak contract for creative inventory, especially when the adapter itself comments that creative inventory behavior varies by version. `creativeProvider.js` also relies on the same inventory verification loop. fileciteturn36file0 fileciteturn8file0

**Why it matters**
- If the creative item appears only in a creative slot or is not immediately mirrored into normal inventory, the code can incorrectly conclude the grant failed.
- That failure can then bubble into “not in inventory” recovery, which is exactly the path that leads to unwanted gathering.

**Recommended fix**
- Replace the success check with a version-appropriate authoritative state check, or return an explicit success signal from the creative selection API.
- Treat `bot.inventory.items()` as a convenience signal, not as the sole source of truth.

---

### 4) Recovery logic still depends on creative detection being perfectly fresh
**Severity:** High  
**Confidence:** 75%

`TryRecoverFromGameErrorAsync()` skips gather recovery in creative mode only after it has already parsed a “not in inventory” error. That means it is a downstream guard, not a policy layer. Also, the guard only works if `_worldState.IsCreativeMode` is already correct. fileciteturn41file0 fileciteturn9file0

**Why it matters**
- `WorldState.IsCreativeMode` depends on the latest status/gamemode data.
- If the status event has not arrived yet, or if the gamemode mapping is stale, the system can route down the wrong branch.
- This is a classic “implicit contract” bug.

**Recommended fix**
- Make creative mode a first-class runtime capability flag sourced from the adapter, not only from the projected world state.
- Avoid letting recovery infer policy from state that may lag behind the actual bot connection.

---

### 5) The regression is surrounded by task/doc drift and duplicate work signals
**Severity:** Medium  
**Confidence:** 95%

The backlog already contains creative-related items, but they are split across “Done,” “Backlog,” and “InProgress” in a way that makes it easy to reintroduce old behavior:
- `Creative Mode Gather Bypass Guard` is done. fileciteturn29file0
- `Creative mode recovery guards — don't gather materials in creative mode` is done. fileciteturn28file0
- `Creative Mode Gather` is still backlog. fileciteturn42file0
- `Creative Mode Give Items Overload` is still backlog. fileciteturn42file0
- Current in-progress work is actually `MoveToTool` context routing and Mineflayer adapter modularization. fileciteturn42file0

**Why it matters**
- The repo already has multiple “done” items whose code paths and comments disagree with the live code.
- That increases the chance of “fixing” the wrong layer and creating another drift cycle.

**Recommended fix**
- Before changing behavior, normalize the task records to match the actual runtime architecture.
- Mark the creative policy owner explicitly in one task, and close or merge the stale duplicates.

---

## Supplemental implementation specifics

### A. Current creative flow by layer
- **Planner / build path**: creative builds skip survival pre-gather; block placements are generated directly. fileciteturn47file0
- **Planner / gather path**: still mines. No creative override was visible in the gather decomposition. fileciteturn49file0
- **Host / goal set**: enqueues `/give` only for `IBuildGoal` when `IsCreativeMode` is true. fileciteturn17file0
- **Adapter / place action**: falls back to `creativeProvider.ensureCreativeItem()` if `bot.inventory` does not already contain the material. fileciteturn36file0
- **Recovery**: skips gather recovery in creative, but only after the error has already happened. fileciteturn41file0

### B. Existing architectural debt that amplifies this bug
- `AgentBackgroundService` remains a god-class with planner orchestration, recovery, dashboard, memory, and transport policy all mixed together. That makes policy drift likely. fileciteturn41file0
- Creative behavior is encoded in comments and local branches across multiple files instead of in one policy interface.
- Multiple “done” task files describe behavior that no longer matches live code. fileciteturn28file0 fileciteturn30file0

### C. Claims checked against task backlog to avoid duplication
Already in flight:
- `Wire MoveToTool to read coordinates from ActionData.Context (Phase 4 adaptive)` — unrelated. fileciteturn42file0
- `Modularize MineflayerAdapter: split index.js into focused modules` — related infrastructure, but not the creative policy fix itself. fileciteturn42file0

Already done / should not be re-opened blindly:
- `Creative Mode Gather Bypass Guard`  
- `Creative mode recovery guards — don't gather materials in creative mode`  
- `Sprint 37 Issue A: Remove GetStatus from GatherItemDecompose`  
These indicate previous partial repairs, but the behavior is not yet unified. fileciteturn29file0 fileciteturn28file0 fileciteturn42file0

---

## Assumptions
1. The target server is using Mineflayer behavior compatible with the current adapter fallback logic.
2. “Creative mode no longer grants items” means either `bot.creative.setInventorySlot()` is not reflected in normal inventory quickly enough, or the host recovery/path planning no longer leads to a valid item acquisition path.
3. The intended product behavior is that creative mode should **not** mine for materials when a direct creative provisioning path exists.

## Open questions
1. Should creative-mode gather goals be allowed at all, or should they be rejected/converted into a no-op with a clear explanation?
2. Is `bot.inventory.items()` the correct authoritative post-condition for creative provisioning on the target Mineflayer version?
3. Should creative status be sourced directly from the adapter and cached as a capability flag rather than inferred from projected world state?
4. Should `/give` remain in the host layer at all, or be removed in favor of adapter-only creative selection?
5. Are there tests that cover the transition from creative `place` fallback to recovery after a “not in inventory” error?

## Confidence by finding
- Creative build/gather policy split: **90%**
- Adapter inventory verification brittleness: **80%**
- Host `/give` provisioning drift: **85%**
- Exact root cause of this regression on live server: **75%**
- Task/documentation drift: **95%**

## Recommended implementation order
1. Make gather decomposition explicit for creative mode, or explicitly forbid it.
2. Choose one creative inventory owner and remove the competing fallback.
3. Switch creative success checks away from `bot.inventory.items()` as the sole verification signal.
4. Add tests for creative gather, creative place provisioning, and stale `IsCreativeMode` timing.
5. Update task records so “done” matches live code and no one reimplements the same guard again.

## Final assessment
The codebase is close to a clean separation, but creative mode still crosses three policy boundaries and one of them is still survival-first. The reported regression is therefore best treated as an architectural contract bug, not a one-line fix.
