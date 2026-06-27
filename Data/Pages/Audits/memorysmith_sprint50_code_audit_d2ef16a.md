# MemorySmith.Agent code audit

**Head commit:** `d2ef16ab86d433cc62912939c213cde088dcaf05`  
**Branch label in sprint docs:** `sprint-35-llm-first`  
**Audit focus:** current in-progress wave, context carry, chat path cleanup, telemetry, and any legacy fallback/consolidation risk.

## Executive summary

The latest head commit is a relatively small cleanup wave on top of a much larger sprint 50 surface area, but it reveals a bigger architectural risk: the repository is trying to move toward explicit cross-step context carry while the execution boundary is still enforcing strict JSON-schema validation. That combination is currently inconsistent. The planned `TSK-0004` context-carry feature is only partially wired in the runtime dispatch loop, while the tools that are supposed to consume it still require explicit arguments and do not yet extract or accept the new values. In practice, the current shape will either fail validation or silently never use the intended values. fileciteturn18file0turn22file0turn24file0turn40file0turn41file0

The most important codebase-health issue is therefore not the dead regex cleanup itself; it is that the new context-carry model is being treated as if it were a simple argument merge, but the dispatcher is intentionally rejecting unexpected properties. That makes the current implementation brittle by construction. This is high severity because it sits on the hot path for every tool dispatch, and high confidence because the validator explicitly rejects undeclared properties. fileciteturn40file0turn41file0turn33file0

A second high-confidence gap is that the sprint-35 gather flow still does not match the design note. The `GatherWood` path still decomposes to direct mining actions and does not invoke `SearchMemory` or `MoveTo` in the way the task file describes. `SearchMemoryTool` also returns search results, page IDs, and counts, but no coordinates; `MoveToTool` still requires explicit `x/y/z` and returns an error if they are missing. That means the intended “find wood in memory, then navigate to it” flow is not actually present yet. fileciteturn18file0turn24file0turn22file0turn26file0turn31file0

There is also a smaller but important architectural mismatch in chat handling: the code comments claim that only stop/cancel, status, inventory, and help are deterministic fast-paths, but the current `LlmChatInterpreter` still fast-paths `navigate`. That may be intentional, but it is a legacy exception that contradicts the stated “LLM owns intent” direction and should be either justified in the design docs or removed. fileciteturn47file0turn44file0

On the positive side, the wave D changes are coherent in isolation: the repo now has a clear version bump, a telemetry sink, and explicit documentation of the intent to consolidate context wiring and dead regex cleanup. The cleanup itself is real, and the docs/handoff artifacts correctly record the work as completed for the wave. fileciteturn37file0turn55file0turn9file1

## Highest-priority findings

| Severity | Confidence | Finding | Why it matters |
|---|---:|---|---|
| High | 97% | Global context merging into `Arguments` conflicts with strict schema validation | Any non-whitelisted context key will make dispatch fail before the tool runs. |
| High | 96% | `TSK-0004` is not complete end-to-end | `MoveToTool` still needs explicit coords; `SearchMemoryTool` does not emit coords; gather flow still mines directly. |
| Medium | 90% | `LlmChatInterpreter` still fast-paths `navigate` | This is a legacy exception that conflicts with the current “LLM owns intent” story. |
| Medium | 88% | Search/move context contract is underspecified | The repo uses both `nearest*`-style intent in the task and generic argument-only tools in code. |
| Medium | 78% | SQLite telemetry is enabled with a project-wide NU1903 suppression | The warning suppression is coarse and should be revisited as a tracked dependency choice, not an ambient default. |

## Detailed findings

### 1) Context carry is architecturally inconsistent right now

`ActionData` now documents a shared mutable `Context` bag for cross-action carry, and the dispatch loop merges all non-internal context entries into `Arguments` before tool validation. However, `ToolDispatcher.ValidateAgainstSchema` rejects any argument not declared in the tool schema, so this merge makes the new context keys part of the schema contract whether the tool wants them or not. That is a risky implicit contract, not a safe abstraction. fileciteturn33file0turn12file0turn40file0turn41file0

The practical failure mode is straightforward: as soon as one action carries metadata like `nearestWoodX/Y/Z`, every downstream tool with a strict schema and no matching properties will fail validation. That means the new feature is not just incomplete; it is likely to break unrelated dispatches unless the schema model changes with it. This is a high-confidence design bug because the validator’s behavior is explicit in code. fileciteturn41file0turn12file0

### 2) `TSK-0004` is not implemented end-to-end

The task file says `SearchMemoryTool` should extract coordinates from result text or structured location data, and `MoveToTool` should fall back to `Context['nearestX']`, `Context['nearestY']`, and `Context['nearestZ']` when explicit arguments are absent. The current implementation does neither. `SearchMemoryTool` returns only `query`, `results`, `bestPageId`, and `count`. `MoveToTool` still requires `x`, `y`, and `z` and returns an error if they are missing. fileciteturn18file0turn24file0turn22file0

The gather plan is also not using the new flow. `GatherItemDecompose` still emits `MineBlock` actions for the source blocks and then returns; there is no `SearchMemory` step and no `MoveTo` step in the active gather path. That means the feature the task describes is still backlog work, not merely an incomplete polish item. fileciteturn18file0turn31file0

### 3) The chat fallback story still has a legacy exception

The `LlmChatInterpreter` comments describe only four deterministic fast-paths, but the actual code still returns the quick pattern result for `navigate` before it considers LLM routing. That is a direct mismatch between the architecture note and the implementation. It may be a deliberate zero-risk shortcut, but it is still a legacy fallback that the repo’s sprint narrative does not currently explain. fileciteturn47file0turn44file0

This matters because the repo is explicitly trying to lift itself out of legacy systems and fallback sprawl. A hidden “one more fast-path” rule tends to spread: it makes future reasoning about intent routing harder, and it increases the chance that behavior differs depending on which interpreter path happened to match first. Confidence is medium-high because the code is unambiguous, even though the intent may be acceptable. fileciteturn47file0

### 4) The telemetry addition is reasonable, but the suppression is broad

The new Serilog SQLite sink is wired in cleanly at startup, and the version bump is reflected in the app and about page. The project file also suppresses NU1903 globally for the web host project. That is a maintenance tradeoff, not a bug, but it is a place where a greenfield codebase can accidentally inherit a permanent exception without a follow-up review. fileciteturn37file0turn55file0

The risk is not that the sink exists; the risk is that the warning suppression becomes “set and forget” while the dependency stack changes. For a greenfield project that wants to avoid legacy debt, this should be treated as a tracked dependency decision with a revisit date, not as a permanent quieting of build telemetry. Confidence is moderate because this is a policy risk rather than a direct runtime failure. fileciteturn55file0turn37file0

### 5) The codebase still has a few coupled orchestration hotspots

`AgentBackgroundService` remains the center of planning, dispatch, correlation, replan evaluation, failure tracking, and recovery. That is not inherently wrong, but it is now also doing context merging and lifecycle mutation in the same loop. The result is a class with a lot of cross-cutting responsibility and many hidden contracts between planner output, tool validation, and world-state projection. fileciteturn12file0turn13file0turn14file0turn15file0

This is a code-health concern because the more behaviors the service accumulates, the easier it becomes for a small change in one area to break another. The current wave is especially exposed because it mixes action context, correlation IDs, place-block checkpointing, rate limiting, replan evaluation, and recovery in one path. Confidence is high that this is a maintainability issue, even if the current behavior is still functioning. fileciteturn12file0turn13file0turn14file0turn15file0

## Implementation specifics worth preserving

The dead regex removal in `ChatInterpreter` is a good cleanup. The file now clearly shows that gather/build/craft regex blocks are gone and that the interpreter’s job is down to deterministic zero-risk commands plus navigation by coordinates. The code and the sprint documentation are aligned on the removal of the obsolete fields themselves. fileciteturn49file0turn9file1

The build planner already has a robust explicit-or-implicit origin model and a retry gate based on `FlatAreaRetryRadius`, so the build side is in better shape than the gather-memory side. In other words, the architecture is not uniformly weak; the biggest gap is specifically the memory-to-navigation handoff. fileciteturn29file0turn30file0turn44file0

## Assumptions

I assumed the commit SHA is the authoritative head for the audit, even though the sprint docs still label the branch as `sprint-35-llm-first` and some handoff pages use `main` as the branch field. I also assumed the task JSON files are still the authoritative source for open work items and that anything marked `Backlog` is intentionally unfinished rather than accidentally missing. fileciteturn9file1turn44file0turn18file0

I treated the code comments and sprint docs as intent, but not as proof of implementation. Where the code contradicted the docs, I treated the code as source of truth and flagged the mismatch. That is why the `navigate` fast-path and the missing `SearchMemory`/`MoveTo` wiring are both reported even though the documents describe a more complete LLM-first flow. fileciteturn47file0turn44file0turn18file0

## Open questions

1. Should `Context` be a true tool metadata channel that bypasses schema validation, or should only selected keys be mapped into tool arguments by tool-specific adapters?
2. Is `navigate` intentionally exempt from the “LLM owns intent” rule, or is it a legacy exception that should be removed now that the LLM path is stable?
3. Should `SearchMemoryTool` be extended to emit structured coordinate hints, or should the planner derive coordinates from memory pages before constructing a `MoveTo` action?
4. Is the global NU1903 suppression meant to stay until the sink package updates, or should it be limited to a narrower target and accompanied by a dependency-tracking issue?

## Recommended next steps

1. Finish `TSK-0004` as a single, test-backed flow rather than a generic argument merge.
2. Decide whether context carry is a schema-visible contract or an out-of-band metadata channel, then enforce that choice everywhere.
3. Either remove the `navigate` fast-path or codify it as an explicit exception in the sprint docs so the architecture remains honest.
4. Add an end-to-end gather/memory test that proves `SearchMemory → MoveTo` works with real data and does not depend on stale coordinates or hardcoded planner assumptions.
5. Revisit the SQLite telemetry dependency warning suppression after the next package bump.

## Evidence map

- `WebUI.Blazor/AgentBackgroundService.cs` — context merge, correlation, replan, recovery, and lifecycle handling. fileciteturn12file0turn13file0turn14file0turn15file0
- `Agent.Tools/ToolDispatcher.cs` — strict schema validation that rejects undeclared properties. fileciteturn40file0turn41file0
- `Agent.Core/Models/ActionData.cs` — `Context` bag contract. fileciteturn33file0
- `Data/Tasks/tsk-0004-wire-moveto-context-injection.json` — intended context-carry behavior. fileciteturn18file0
- `Agent.Tools/Tools/SearchMemoryTool.cs` and `Agent.Tools/Tools/MoveToTool.cs` — current gather/move capability gap. fileciteturn24file0turn22file0
- `Agent.Planning/HtnTaskLibrary.cs` — gather decomposition still mines directly. fileciteturn26file0turn27file0turn31file0
- `Agent.Planning/LlmChatInterpreter.cs` — navigate fast-path legacy exception. fileciteturn47file0
- `WebUI.Blazor/Program.cs` and `WebUI.Blazor/WebUI.Blazor.csproj` — SQLite telemetry and warning suppression. fileciteturn37file0turn55file0
- `Data/Pages/Tasks/agent-handoff-sprint35-llm-first.md` — sprint intent and open work alignment. fileciteturn44file0
