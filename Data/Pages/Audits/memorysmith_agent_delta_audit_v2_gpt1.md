# MemorySmith.Agent Delta Audit — Newly Found Issues

**Target:** `TheMasonX/MemorySmith.Agent`  
**Branch / commit context:** `dev/round-3`, commit `57b8fdf79ff7ba6f877e0fcac2211934e0f3279c`  
**Scope:** New findings only. This report excludes issues already captured in the prior audit and the earlier delta reports.

## 1) Creative-mode smelt shortcut gives the wrong item
**Severity:** High  
**Confidence:** 97%

`HtnTaskLibrary.DecomposeSmeltItem()` short-circuits creative mode by returning `/give @p {inputItem} {count}`. `SmeltGoal.IsComplete()` checks for the smelted **output** item. For a normal smelt goal like `smelt iron_ore`, the creative shortcut hands out the input, not the output, so the shortcut does not satisfy the goal contract. fileciteturn138file0turn134file0

**Why this matters:** the “fast path” is not equivalent to the real path. It can stall the goal or create misleading completion behavior.

**Fix direction:** give the output item in creative mode, or keep the normal smelt pipeline and let the furnace path produce the output.

## 2) Count handling is inconsistent, and zero-count goals can complete immediately
**Severity:** High  
**Confidence:** 95%

`PlaceBlockGoal` clamps `count` to at least 1, but `IntentManager` and `GoalFactory` accept raw counts for `craft`, `gather`, and `smelt`. The count-based goal constructors (`CraftItemGoal`, `GenericGatherGoal`, `SmeltGoal`) also accept the raw value without validation. Because completion checks are `>= count`, a count of `0` produces a goal that can be considered complete before any action runs. fileciteturn50file0turn150file0turn149file0turn134file0turn156file0turn48file0

**Why this matters:** it creates silent vacuous goals and inconsistent semantics across goal types.

**Fix direction:** validate or clamp counts in one shared boundary helper, and reject zero or negative counts for all count-based goals.

## 3) `ParseCommandString()` can throw on oversized numeric inputs
**Severity:** Medium  
**Confidence:** 93%

`ParseCommandString()` accepts `\d+` for counts and coordinates, then immediately calls `int.Parse(...)` for those values. Very large digit strings can overflow `int` and throw, turning a malformed command into an exception instead of a clean “unrecognized command” response. This affects `craft`, `gather`, `place`, `move`, and `smelt` parsing paths. fileciteturn156file0

**Why this matters:** command parsing should be failure-tolerant. This is a brittle edge case that can crash intent parsing on input the regex already accepts.

**Fix direction:** use `int.TryParse` with bounds validation, or cap the accepted digit length before parsing.

## 4) `ExecutionManagerImpl` leaks `JsonDocument` objects on every dispatch
**Severity:** Medium  
**Confidence:** 94%

`ExecutionManagerImpl.DispatchAsync()` serializes arguments to JSON, parses them with `JsonDocument.Parse(...)`, and keeps only the `RootElement`. The `JsonDocument` itself is never disposed. Since this path runs for every dispatched action, it creates a steady resource leak in the hottest execution loop. fileciteturn139file0

**Why this matters:** the leak is small per call but constant, and this code sits on the agent’s critical path.

**Fix direction:** wrap the parse in `using var doc = JsonDocument.Parse(json);` and clone the root element if a long-lived `JsonElement` is needed.

## 5) Tool schema properties repeat the same `JsonDocument` leak pattern
**Severity:** Medium  
**Confidence:** 93%

Several tool implementations expose `InputSchema` via `JsonDocument.Parse(...).RootElement` in a getter. I confirmed the pattern in `CraftItemTool`, `FurnaceTool`, `SearchMemoryTool`, and `ChatTool`, and the repository search shows the same idiom in other tools as well. Because these getters are read repeatedly, they re-parse the same static schema text and leak the document each time. fileciteturn155file0turn153file0turn157file0turn158file0

**Why this matters:** this is duplicated code plus avoidable allocation/leak pressure in a shared boundary.

**Fix direction:** cache schemas once as static readonly values or centralize schema creation in a helper that owns disposal correctly.

## 6) `SearchMemoryTool` swallows all exceptions without logging
**Severity:** Medium  
**Confidence:** 87%

`SearchMemoryTool.ExecuteAsync()` catches every non-cancellation exception and returns `ToolResult(false, $"Search failed: {ex.Message}")`, but it does not log the exception. That means search failures disappear unless the caller happens to surface the message, and there is no stack trace or structured context for diagnosis. fileciteturn157file0

**Why this matters:** this is exactly the kind of silent failure that makes field debugging difficult, especially for transient gateway or parsing problems.

**Fix direction:** log the exception with structured context before returning the failure result.

## Recommended order

1. Fix the creative smelt shortcut mismatch.  
2. Normalize/reject zero counts across all count-based goals.  
3. Replace `int.Parse` in command parsing with bounded `TryParse`.  
4. Dispose `JsonDocument` in `ExecutionManagerImpl` and cache tool schemas.  
5. Add logging to `SearchMemoryTool` failure paths.
