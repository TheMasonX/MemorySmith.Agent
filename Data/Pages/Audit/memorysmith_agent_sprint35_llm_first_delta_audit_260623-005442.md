# MemorySmith.Agent Sprint 35 LLM-First Delta Audit

**Scope:** `TheMasonX/MemorySmith.Agent` at head commit `073175b03387e05592780016db345f7ae48217c0`  
**Reference handoff:** Sprint 35 LLM-first handoff notes and sprint-35 planning docs  
**Audit timestamp:** 2026-06-23 00:54:42 America/Chicago

## Executive summary

This second pass confirms that the branch is materially ahead in code volume, but several of the sprint-35 architecture claims are still not fully realized in code. The most important delta findings are:

1. **The LLM-first transition is not complete.** The handoff says parsers should stop creating goals and instead emit an intent draft, but the code still uses `ChatInterpretation -> GoalName -> GoalParameters -> GoalFactory`. That is a real architectural drift, not just a naming issue.  
   **Confidence: 96%**

2. **The new `smelt` chat path is wrong.** `ChatInterpreter` routes `smelt` into `CraftItemGoal`, and the downstream decomposer ultimately dispatches `CraftItemTool`, which only crafts from inventory. That does not perform furnace smelting.  
   **Confidence: 98%**

3. **Explicit build origin support is only partially wired.** `BuildGoalDecomposer` honors `BuildGoal.OriginX/Y/Z`, but `HtnPlanner` still has its own build branch that ignores those properties and reads only world-state facts. Direct callers and tests that use `HtnPlanner` bypass the new behavior.  
   **Confidence: 93%**

4. **The flat-area retry contract from the handoff is still incomplete.** `FlatAreaFoundEvent` does not carry `SearchedRadius`, so the build fallback cannot distinguish “scanned too small a radius” from “scanned 48 blocks and still found nothing.” The current retry logic falls back to `lastArea == 0 ? 48 : 30`, which is a weaker heuristic than the sprint-35 plan.  
   **Confidence: 94%**

5. **A few lower-severity code health issues remain.** The `GetStatusTool` is registered twice as separate instances, and `HasExplicitOrigin` treats any one coordinate as a fully explicit origin, which can produce odd partial-origin behavior in future callers.  
   **Confidence: 68% / 57%**

## What changed since the first pass

The first pass established the broad shape of the branch: tool-safety hardening, journal/world-model additions, planner router wiring, and build-origin work. This pass sharpened that into confirmed defects and architecture mismatches:

- the **smelt/craft confusion** is confirmed and high risk;
- the **LLM-first intent-layer refactor** is still not present in code;
- the **build-origin path** is split between the new decomposer and the legacy HtnPlanner fallback;
- the **flat-area retry fix** is still not fully represented in event data.

That means the branch is directionally better, but the risky parts are now more clearly defined.

## Detailed findings

### 1) LLM-first intent layer is not implemented yet

**Why this matters:**  
The sprint-35 handoff states that parsers should no longer create goals and should instead emit an `IntentDraft` consumed by the planner layer. The handoff explicitly says: parsers produce intent only; goals are created exclusively by the planner. The current code still does the older thing.

**Evidence:**
- Handoff: `Data/Pages/Tasks/agent-handoff-sprint35-llm-first.md:L25-L31` says parsers never create goals and `Chat -> Interpreter -> Goal` is replaced by `Chat -> IntentDraft -> Planner -> Goal`.
- Code: `Agent.Planning/ChatModels.cs:L9-L44` still models `ChatInterpretation` with `GoalName` and `GoalParameters`.
- Code: `Agent.Planning/ChatInterpreter.cs:L221-L246` still emits `ChatIntentType.CreateGoal` plus `GoalName` and `GoalParameters`.
- Code: `Agent.Planning/LlmChatInterpreter.cs:L180-L226` still parses LLM output into goal-bearing chat interpretations.

**Impact:**  
This is not just a future refactor gap; it means the branch still routes through the legacy goal-construction model, so the sprint-35 “LLM-first” architecture remains partially aspirational.

**Recommendation:**  
Treat `IntentDraft` as a first-class transition artifact and keep goal naming in the planner boundary, not in the interpreter boundary.

### 2) `smelt` is routed to the wrong execution model

**Why this matters:**  
The code claims smelting support, but the actual path does not perform smelting. It crafts.

**Evidence:**
- `Agent.Planning/ChatInterpreter.cs:L158-L166` documents that `smelt` maps to the `CraftItem` goal.
- `Agent.Planning/ChatInterpreter.cs:L167-L173` includes `smelt` in the same regex as `craft` and `forge`.
- `Agent.Planning/Decomposition/CraftItemGoalDecomposer.cs:L24-L29` forwards every `CraftItemGoal` to `HtnTaskLibrary.DecomposeCraftItem`.
- `Agent.Planning/HtnTaskLibrary.cs:L54-L88` emits crafting-table bootstrap actions and a `CraftItem` action, not a furnace workflow.
- `Agent.Tools/Tools/CraftItemTool.cs:L8-L16` and `L35-L54` confirm that `CraftItemTool` crafts from inventory via the `craft` action; it is not a furnace smelting tool.

**Impact:**  
A player saying “smelt iron ore” is likely to get a craft-oriented plan that fails or behaves nonsensically. This is a high-confidence runtime bug.

**Recommendation:**  
Split smelting into a separate goal and decomposer path, or at minimum make `smelt` map to the furnace tool chain explicitly instead of `CraftItem`.

### 3) Build-origin support is split between new and legacy planner paths

**Why this matters:**  
The new `BuildGoalDecomposer` does the right thing for explicit origins. The legacy `HtnPlanner` path does not.

**Evidence:**
- `Agent.Planning/Decomposition/BuildGoalDecomposer.cs:L24-L60` reads `BuildGoal.OriginX/Y/Z` and passes them into `HtnTaskLibrary.DecomposeBuild`.
- `Agent.Planning/Goals/BuildGoal.cs:L43-L51` stores explicit origin properties.
- `Agent.Planning/HtnPlanner.cs:L40-L53` still special-cases `BuildGoal` and reads only world-state facts via `ReadOriginFact`, ignoring `BuildGoal.OriginX/Y/Z`.
- `Agent.Planning/Router/PlannerRouter.cs:L100-L119` routes through registered decomposers first, but the codebase itself still contains the legacy direct path.
- `MemorySmith.Agent.Tests/HtnPlannerBuildTests.cs:L184-L197` still exercises `HtnPlanner` directly, so the legacy path is not dead code in practice.

**Impact:**  
Production routing appears to prefer the decomposer, but direct callers, older tests, or future fallback paths can silently bypass explicit-origin support.

**Recommendation:**  
Either remove the direct `BuildGoal` branch from `HtnPlanner` or make it honor the same origin precedence as `BuildGoalDecomposer`.

### 4) Flat-area retry logic still cannot implement the handoff’s radius-aware contract

**Why this matters:**  
The sprint-35 handoff says `FlatAreaFoundEvent` should carry searched radius so build-origin retries can distinguish “too small radius” from “search at 48 still failed.”

**Evidence:**
- Handoff: `Data/Pages/Tasks/agent-handoff-sprint35-llm-first.md:L103-L120` calls for `FlatAreaFoundEvent.SearchedRadius` and a retry gate based on that radius.
- Event model: `Agent.Core/Events/WorldEvents.cs:L110-L119` defines `FlatAreaFoundEvent` with `Area`, bounding box coordinates, and timestamp, but no `SearchedRadius`.
- Build logic: `Agent.Planning/HtnTaskLibrary.cs:L45-L56` uses `lastArea == 0 ? 48 : PreflightFlatAreaRadius` when deciding whether to emit `FindFlatArea`.

**Impact:**  
The retry behavior is still heuristic. It can retry with 48 blocks after an area-0 scan, but it cannot know what radius was actually searched on the previous attempt.

**Recommendation:**  
Add searched-radius to the event and wire it through the bridge before relying on radius-sensitive retry logic.

### 5) Lower-severity code health issues

**Duplicate `GetStatusTool` instance registration**  
`WebUI.Blazor/Program.cs:L175-L180` registers `GetStatusTool(world)` twice, once under `GetStatus` and again under `Status`, but as separate instances. This is not catastrophic, but it is unnecessary and could become a divergence point if state is ever added to the tool.

**Partial explicit-origin semantics**  
`Agent.Planning/Goals/BuildGoal.cs:L50-L51` treats any one coordinate being set as “explicit origin.” That works for the current chat parser, which always supplies all three coordinates, but it is brittle for future callers.

## Assumptions

- I treated the head commit `073175b03387e05592780016db345f7ae48217c0` as the branch under review because the repository metadata and PR snapshot pointed there.
- I treated the sprint-35 LLM-first handoff as the planning baseline, even though some of its future-facing items are clearly not meant to be complete yet.
- I did not duplicate items that are already explicitly tracked in the sprint handoff unless the code still contradicts them in a way that matters for runtime behavior.

## Open questions

- Should the branch keep the legacy `HtnPlanner` typed-goal branches for backward compatibility, or is sprint 35 supposed to remove them entirely?
- Is `smelt` intended to become a separate goal type, or should it be normalized to a furnace-specific tool path inside the existing craft system?
- Do you want the flat-area event to carry search-radius now, or should the retry policy be simplified until Sprint 36?
- Should partial build origins be rejected explicitly, rather than treated as valid if only one coordinate is present?

## Confidence summary

- LLM-first architecture mismatch: **96%**
- `smelt` misrouting bug: **98%**
- Explicit-origin bypass in legacy planner path: **93%**
- Flat-area retry contract still incomplete: **94%**
- Minor tool-registration / partial-origin code health items: **68% / 57%**

## Bottom line

The branch has moved a lot of the right pieces into place, but the highest-risk gaps are now clearer: the intent layer is still goal-centric, the smelt path is wrong, and build-origin/flat-area handling is split across competing planner paths. The next useful pass is not more broad cleanup; it is tightening the contract boundaries so the sprint-35 architecture and the runtime behavior stop diverging.