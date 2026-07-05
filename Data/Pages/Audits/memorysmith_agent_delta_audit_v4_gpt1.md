# MemorySmith.Agent Delta Audit — Additional New Findings

**Target:** `TheMasonX/MemorySmith.Agent`  
**Branch / commit context:** `dev/round-3`, commit `57b8fdf79ff7ba6f877e0fcac2211934e0f3279c`  
**Scope:** New findings only. This report excludes issues already captured in the prior audit and earlier delta reports.

## 1) WorldModel reconciliation ignores inventory drift entirely
**Severity:** High  
**Confidence:** 92%

`WorldModel.Reconcile()` scores prediction quality using position, health, and food only. It never compares `PredictedInventory` against `ObservationState.Inventory`, even though the prediction state explicitly carries inventory. That means large item drift can exist without increasing uncertainty at all. fileciteturn222file0turn198file0

**Why this matters:** inventory is a first-class part of the planner’s decision-making. A model that ignores inventory mismatches can stay falsely confident while the agent’s actual capability diverges.

**Fix direction:** include inventory delta in the reconciliation score, or explicitly document that inventory is out of scope and stop carrying it through the prediction state.

## 2) Prediction updates are optimistic for item-consuming actions
**Severity:** High  
**Confidence:** 89%

`PredictCraft()` adds the crafted item to the predicted inventory but never subtracts ingredients. `PredictPlace()` leaves inventory unchanged even though placement consumes the held block. `PredictSmelt()` also leaves inventory unchanged. The result is a prediction model that only models gains, not costs, for item-consuming actions. fileciteturn197file0

**Why this matters:** the world model can systematically overestimate what the agent still has available after crafting, placing, or smelting.

**Fix direction:** model consumption and output together, or mark these actions as too uncertain to update inventory at all.

## 3) `WorldModel.GetIntArg()` silently narrows `long` to `int`
**Severity:** Medium  
**Confidence:** 91%

The world-model argument helper accepts `long l => (int)l` without any range check. That creates the same silent narrowing problem found elsewhere in the codebase, but here it affects prediction input parsing rather than goal creation. fileciteturn197file0

**Why this matters:** oversized numeric inputs can wrap or truncate into invalid coordinates or counts without a clear error.

**Fix direction:** use checked conversion or bounds validation before converting to `int`.

## 4) Movement predictions allow food to go negative
**Severity:** Medium  
**Confidence:** 87%

`PredictMove()` and `PredictWander()` both subtract 1 from food directly (`b.Food - 1`) and never clamp at zero. Repeated predictions can therefore produce impossible negative food values, which makes the model less trustworthy and can skew downstream heuristics. fileciteturn197file0

**Why this matters:** the model should stay inside the same domain as the observed state, which is naturally bounded at zero food.

**Fix direction:** clamp predicted food at zero, or model starvation explicitly instead of allowing negative values.

## 5) Count-bearing tool schemas and implementations still accept zero/negative values
**Severity:** Medium  
**Confidence:** 90%

`MineBlockTool` and `FurnaceTool` both expose a `count` schema field but do not declare a minimum and do not validate the parsed value before dispatch. Each tool uses `GetInt32()` and forwards the count directly into `ActionData`, so zero or negative values can reach the adapter unchecked. fileciteturn203file0turn223file0

**Why this matters:** it creates a brittle contract at the tool boundary and can produce nonsensical actions even when the schema says the field is optional.

**Fix direction:** set an explicit schema minimum and reject counts below 1 in the tool implementation. A shared helper would remove the repeated pattern.

## 6) The context-carry naming contract is inconsistent
**Severity:** Medium  
**Confidence:** 82%

`ActionData`’s comments say `SearchMemoryTool` writes coordinates to `Context["nearestWoodX/Y/Z"]`, while `MoveToTool` reads `nearestX/Y/Z` from merged arguments. Those are not the same key names. Even if one side has already moved on, this is a brittle cross-module contract and an easy place for silent “works in one path, fails in another” behavior. fileciteturn204file0turn183file0

**Why this matters:** context carry is supposed to reduce glue code, not create hidden key-name coupling between tools.

**Fix direction:** standardize the context key names in one place and remove the old alias comments once the path is confirmed.

## Practical priority order

1. Add inventory to world-model reconciliation or remove it from prediction state.  
2. Model inventory consumption for craft/place/smelt predictions.  
3. Clamp or validate numeric inputs in `WorldModel.GetIntArg()`.  
4. Clamp predicted food at zero.  
5. Put a minimum on all count-bearing tool schemas and validate at execution time.  
6. Standardize the context-carry key names and remove stale alias commentary.
