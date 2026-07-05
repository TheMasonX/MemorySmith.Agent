# MemorySmith.Agent — Delta Audit Addendum

Scope: newly identified findings beyond the previous audit pass. This addendum focuses on currently in-progress code paths, brittle assumptions, and normalization gaps.

## Executive summary

The highest-severity regression is the new **PlaceBlock** path: the goal, decomposer, and tool schema do not agree on the contract, so the path cannot succeed as written. The second most important issue is the chat bridge's hardcoded `onlinePlayers=1` / `playerPosition=null` default, which makes the bot far too eager to treat messages as addressed in multiplayer. There are also several observability regressions where catch blocks still return safe defaults but do not log failures, which weakens the repo’s own “never swallow silently” policy. fileciteturn53file0turn49file0turn51file0turn40file0turn70file0

## New findings

### 1) PlaceBlock is broken end-to-end
**Confidence:** 99%

**What changed:** `PlaceBlockGoal` accepts `x/y/z` but never stores them, still describes the goal as “in front,” and only tracks the item/count. `PlaceBlockGoalDecomposer` then emits `place` actions with `{ block, count }`, while the actual `PlaceBlockTool` requires `{ x, y, z, material }` and its schema rejects extra/missing fields. The dispatcher validates against that schema before execution, so this plan fails before it reaches Mineflayer. fileciteturn53file0turn49file0turn51file0turn40file0

**Why it matters:** the new place-block capability cannot work in its current form, and the code path will look “implemented” even though it is guaranteed to fail validation.

**Correction:** collapse this into one explicit contract. Either:
1. make the goal carry and preserve coordinates/material all the way through to `PlaceBlockTool`, or
2. rename the behavior to a separate “place in front” flow that computes x/y/z before dispatch.

Also add tests for:
- explicit coordinate placement,
- in-front placement,
- schema validation failure when coords/material are missing.

---

### 2) Chat intent routing still hardcodes a single-player assumption
**Confidence:** 94%

**What changed:** `IntentManagerImpl.ProcessChatAsync` still passes `DefaultOnlinePlayers = 1` and `playerPosition: null` into `IChatInterpreter.InterpretAsync`. That means the deterministic interpreter’s `onlinePlayers <= 1` branch is always true in this bridge, so messages are treated as bot-directed even when the server is multiplayer. fileciteturn70file0turn19file0

**Why it matters:** the bot becomes chatty in the wrong contexts and can trigger actions on ambient conversation.

**Correction:** thread the real live player count and player position into the bridge, or fail closed (`maybe` / not addressed) when the data is missing instead of forcing `1`.

---

### 3) Registered goals omit a supported capability
**Confidence:** 86%

**What changed:** `GoalFactory.CreateAsync` supports `PlaceBlock:{item}`, but `RegisteredGoals` only advertises `GatherItem`, `Build`, `CraftItem`, and `SmeltItem`. That makes the place-block path invisible to any caller that builds help text, prompts, or validation from the registry. fileciteturn25file0

**Why it matters:** the code already knows how to build the goal, but the advertised capability set is stale and incomplete.

**Correction:** include `PlaceBlock:{item}` in `RegisteredGoals`. If the intent is to keep placement temporary or special-cased, make that explicit in the registry rather than hiding it.

---

### 4) Command-string parsing still throws on malformed numeric steps
**Confidence:** 89%

**What changed:** `IntentManager.ParseCommandString` uses `int.Parse` in multiple branches (`craft`, `gather`, `place`, `move`, `smelt`) without guarded fallback. Multi-step commands coming from `IntentDraft.NextSteps` are not guaranteed to be perfectly numeric, so a malformed next step can throw and break chaining. fileciteturn36file0

**Why it matters:** a single bad step can abort the rest of the chain and make a planner or LLM error look like a runtime crash.

**Correction:** switch to `int.TryParse` in every branch and return `null` (or a structured parse failure) for bad input. Keep command parsing non-throwing.

---

### 5) SearchMemoryTool still swallows failures without logging
**Confidence:** 95%

**What changed:** `SearchMemoryTool.ExecuteAsync` catches `Exception` and returns `ToolResult(false, ...)`, but does not log the failure. This is a silent observability regression relative to the repo’s own rule that discarded events/exceptions must be visible. fileciteturn63file0turn2file0

**Why it matters:** missing search context becomes difficult to debug, especially because this tool is part of the context-carry path used by navigation.

**Correction:** log a warning with query, exception type, and a short message before returning failure.

---

### 6) The evaluator’s parse failure path is silent
**Confidence:** 81%

**What changed:** `LlmEvaluatorImpl.ParseReplanDecision` catches all parse exceptions and returns `false` with no logging. That is conservative, but it makes malformed model output indistinguishable from a deliberate “no replan,” which hides provider regressions. fileciteturn34file0

**Why it matters:** the replanning loop will quietly degrade if the evaluator prompt or provider output drifts.

**Correction:** keep the conservative fallback, but emit a warning or at least a debug log containing a response excerpt/hash and the parse failure reason.

---

### 7) CreatePage exposes a `type` argument that the REST gateway ignores
**Confidence:** 78%

**What changed:** `CreatePageTool` accepts `type` and passes it through to `IMemoryGateway.CreatePageAsync`, but `RestMemoryGateway.CreatePageAsync` ignores the argument and always sends `options.DefaultPageRole`. That makes the input look meaningful when it currently has no effect. fileciteturn83file0turn81file0turn85file0

**Why it matters:** this is a contract gap and a source of false confidence. Callers believe they are classifying pages, but the REST path drops the information.

**Correction:** either wire `type` into the request payload if MemorySmith supports it, or remove the parameter from the tool surface so the contract matches reality.

## Consolidation opportunities

- Unify the placement contract. Right now there are three partially overlapping ideas: “place a block,” “place in front,” and “build blueprint placement.” They should be represented by distinct goal/tool contracts instead of a single goal class with unused coordinates. fileciteturn53file0turn49file0turn51file0
- Replace the `IntentManagerImpl` hardcoded chat defaults with a small shared `ChatContext` / `PlayerContext` value object. That removes the brittle “1 player + null position” assumption and makes the interpreter inputs explicit. fileciteturn70file0turn19file0
- Standardize “parse or fail” helpers across the planner path. `TryParseInt`-style code should be reusable in `IntentManager`, `GoalFactory`, and any step-chain parser so malformed input never becomes an exception boundary. fileciteturn36file0turn25file0
- Use the same observability rule everywhere: return safe defaults only after logging. `SearchMemoryTool` and `LlmEvaluatorImpl` should match the repository’s own failure-visibility standard. fileciteturn63file0turn34file0turn2file0

## Task/sprint duplication check

I checked the place-block related task surface before adding these findings. The existing task `TSK-0123` is already marked done and is scoped to skipping `PlaceBlock` at the bot’s current position; it does **not** cover the current schema mismatch or the fact that coordinates are discarded in the new place-block goal path. fileciteturn79file0turn78file1

## Assumptions

- This addendum assumes the default branch HEAD is the review target.
- This is a static review only; no runtime build or live server execution was performed.
- Several in-progress comments mention Sprint 54-era work items; I treated those as current in-scope context even where the roadmap text still says Sprint 51/52. fileciteturn4file0turn31file0

## Open questions

- Should “place” mean explicit world coordinates, “in front of me,” or both?
- Is the `CreatePageTool.type` field meant to be a real page-classification signal, or is it only a placeholder for future API support?
- Is the live player count available in the chat bridge yet, or should the interpreter explicitly degrade to “not addressed” when it is absent?
