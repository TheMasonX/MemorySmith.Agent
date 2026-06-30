# Additional audit findings — bugs, legacy reduction, and technical debt

## Summary

Several more issues stand out beyond the creative-mode regression. The biggest recurring pattern is split ownership: logic is spread across planner, recovery, adapter, and helper layers, with transitional compatibility paths still active. That creates race conditions, silent fallbacks, and extra maintenance cost.

The clearest new risks are:

* creative provisioning is fire-and-forget and can race against planning,
* task sequence advancement is stateful in a brittle way,
* LLM parsing fails closed to “continue” too easily,
* item normalization is not fully consistent,
* the adapter still has standalone legacy chat-filter code that may be dead or duplicated.

## High-priority findings

### 1) Creative provisioning can race the planner

**Severity:** High
**Confidence:** 93%

Creative provisioning is started as an async side effect during goal setup instead of being a hard precondition. That means planning can begin before the inventory grant finishes, which can reintroduce stale inventory and the same bad mining fallback you are trying to eliminate.

**Why it matters:** creative mode needs deterministic provisioning, not best-effort background work.

**Recommendation:** make creative provisioning part of goal activation, not a fire-and-forget task.

---

### 2) Sequence goal completion is brittle

**Severity:** Medium-High
**Confidence:** 88%

`TaskSequenceGoal` depends on a special dispatch-loop path to advance steps. Its `IsComplete()` method only returns true once the internal step index moves past the last item, but `TryAdvance()` never itself marks the whole sequence as complete. That creates a fragile state machine where completion logic is split across multiple layers.

**Why it matters:** sequences can become hard to reason about, and future changes may accidentally suppress or delay advancement.

**Recommendation:** simplify sequence state so one component owns advancement and completion consistently.

---

### 3) LLM parsing fails too quietly

**Severity:** High
**Confidence:** 90%

The LLM evaluator extracts a JSON object using first-`{` / last-`}` slicing, then treats any parse failure as “no replan.” That means malformed or chatty responses can silently suppress replanning even when the current plan is actually failing.

**Why it matters:** a malformed model response should not default to continuing a broken plan.

**Recommendation:** require a stricter structured output contract and treat parse failures as a first-class evaluation signal.

---

### 4) Goal creation normalization is inconsistent

**Severity:** Medium
**Confidence:** 86%

Built-in direct-mine item fallback logic lowercases and normalizes hyphens, but does not consistently strip namespacing in all paths. That means requests like namespaced Minecraft item IDs can miss the built-in fallback even when they should be recognized.

**Why it matters:** the same item can be handled differently depending on how it was named.

**Recommendation:** centralize item normalization before goal creation and completion checks.

---

### 5) The adapter still has likely-legacy chat-filter code

**Severity:** Medium
**Confidence:** 82%

The adapter contains a standalone chat-filter module with heuristic regex filtering and a broad catch-all error path. It may be active, but it also looks like a parallel compatibility layer that should be explicitly owned or removed.

**Why it matters:** duplicate message filtering paths create hidden behavior drift and make future refactors risky.

**Recommendation:** confirm the active call path, then either wire it as the canonical filter or delete it.

## Broader technical debt pattern

The same problem keeps recurring in different forms: transitional code paths remain in place after the “real” implementation already exists. That shows up as:

* planner logic duplicated across decomposer, HTN fallback, and recovery,
* creative behavior split across C#, Node, and task docs,
* string parsing used where structured events already exist,
* old compatibility layers left around after newer abstractions are introduced,
* documentation and task records drifting from the code path they describe.

This is the kind of debt that doesn’t just add clutter. It changes behavior in subtle ways and makes regressions harder to prevent.

## Cleanup priorities

### Immediate

1. Make creative provisioning synchronous or preconditioned.
2. Tighten LLM response parsing and failure handling.
3. Normalize item IDs and namespaced item forms in one place.

### Next

4. Simplify task-sequence advancement and completion ownership.
5. Verify whether the adapter chat-filter module is still part of the active path.
6. Remove or collapse duplicate compatibility layers where the new system already owns the behavior.

### Strategic

7. Keep moving toward one owner per concern:

   * planner owns planning,
   * adapter owns execution,
   * recovery owns recovery,
   * docs describe behavior, not imply it.

## Confidence summary

* Creative provisioning race: **93%**
* Sequence-goal brittleness: **88%**
* LLM parse failure risk: **90%**
* Goal normalization inconsistency: **86%**
* Standalone adapter chat filter may be legacy: **82%**

## Bottom line

The codebase is increasingly healthy in terms of explicit contracts, but it still carries a lot of legacy-shaped overlap. The most valuable next step is not adding more fallback logic. It is deleting or consolidating the duplicate paths that already exist.
