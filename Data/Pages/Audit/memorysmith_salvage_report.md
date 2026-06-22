# MemorySmith.Agent salvage report

## Executive diagnosis

The project is not irredeemable. It already has useful structure: the repository is modular, the README describes three bounded contexts, an HTN planner, dual memory gateways, a world model, schema validation, an agent journal, and an inventory freshness gate, with the repo claiming 200+ tests and CI green. The problem is that the current design still lets several layers compete for authority over the same concepts, especially intent, build origin, inventory truth, and action completion. citeturn157612view0

What makes it feel brittle is not one single bad subsystem. It is the accumulation of many small “helpful” heuristics: regex parsing, LLM fallback, stale-state guards, event correlation, mining progress synthesis, auto-origin fallback, and fire-and-forget tool dispatch. Each individual piece is defensible, but together they create a system where the agent can be technically busy while still being semantically wrong. The best recovery path is to make one layer own intent, one layer own world state, and one layer own execution. Everything else should feed those three layers, not compete with them. fileciteturn2file0 fileciteturn3file0 fileciteturn4file1 fileciteturn12file0 fileciteturn14file0

## What the repo already has going for it

The codebase is already closer to salvageable than the symptoms suggest. `ToolDispatcher` is a strong boundary: it validates arguments against each tool’s schema before execution, and tool exceptions become `ToolResult(false, ...)` rather than crashing the dispatch loop. That is the right safety wall for untrusted arguments, including LLM output. fileciteturn14file0

The world model is also well factored in principle. `WorldState` is a snapshot object with an explicit inventory stale flag, structured facts, and a builder API. `WorldStateProjector` is pure and stateless on paper: it receives typed events and produces a new state. That is the correct place to centralize canonical state updates. fileciteturn12file0 fileciteturn5file0

The agent loop already understands the need for reactivity. `AgentBackgroundService` tracks pending actions with correlation IDs, handles damage interrupts, rate-limits replanning, and pushes status updates to the dashboard. That means you do not need a ground-up rewrite; you need to simplify ownership and remove duplicated truth. fileciteturn4file1

## Where the current design breaks down

### 1) Intent extraction is still too regex-first

`ChatInterpreter` uses deterministic regexes to classify gather, build, go-to, and craft commands, with the LLM interpreter mostly acting as fallback. The LLM path itself is constrained by a fast-path that bypasses it for many common intent types. That is safe, but it means the system still assumes the regex layer can faithfully understand natural language, which is exactly where the brittleness shows up. fileciteturn2file0 fileciteturn3file0

The result is a split-brain parser: some commands are interpreted by regex, some by the LLM, and some are “rescued” from truncated JSON. That is a lot of complexity for what should be one canonical decision step. The reportable consequence is not just parse failures; it is inconsistent intent fidelity. A build request with coordinates may be partially captured, a gather request may resolve to the wrong alias, and a conversational message may accidentally be classified as a command or vice versa. fileciteturn2file0 fileciteturn3file0

### 2) World-state ownership is split across multiple components

`WorldStateProjector` is the right canonical reducer, but `AgentBackgroundService` also mutates state directly in several places: it marks inventory stale on goal changes, resets health tracking, clears pending actions, adds progress facts, and interprets event completion. That is understandable for orchestration, but it means the service is effectively a second reducer. fileciteturn12file0 fileciteturn4file1

The adapter also contributes state by converting game events into custom result events, and those events are then interpreted again in the C# loop. That is fine when the event stream is complete and timely. It becomes fragile when you depend on one event type to repair another event type’s uncertainty, such as using `StatusEvent` to clear inventory stale or using mined-block events to approximate inventory gain. fileciteturn1file1 fileciteturn5file0

### 3) Inventory is not authoritative enough

This is the single clearest concrete bug class I found. `WorldStateProjector.ApplyBlockMined` adds the mined block name to inventory, keyed by the mined block’s type, while `GenericGatherGoal` evaluates completion against the item spec’s source blocks and drop semantics. That works only for items where mined block and collected item are effectively the same. For stone, ore, and several other workflows, the state can drift away from actual item counts. fileciteturn5file0 fileciteturn9file0

The system partly recognizes this by marking inventory stale on goal changes and forcing a `GetStatus` reconciliation before completion, but that is still a coarse workaround. It means the planner often has to wait for a fresh status snapshot to learn whether progress was real, and that makes the loop more reactive than it needs to be. fileciteturn12file0 fileciteturn4file1

### 4) Build origin resolution is still too forgiving

`BuildGoal` supports explicit origin coordinates, stored origin facts, or auto-detected flat area fallback. The planner can also auto-set build origin when `FindFlatArea` succeeds. That is useful, but it is also a major source of silent divergence from user intent. If the user says “build here” and the parser misses the coordinates or the scan picks a nearby but wrong patch, the build starts in the wrong place while still appearing valid to the system. fileciteturn8file0 fileciteturn4file1

This is a design smell because build origin is not just another parameter. It is the spatial anchor for the entire goal. It should be treated as required, explicit, and confidence-scored. Auto-origin should be a separate decision with a visible confirmation boundary, not a silent fallback. fileciteturn8file0

### 5) The Mineflayer adapter is doing low-level work, but not enough high-quality observation

The adapter’s mining action pathfinds to a target, tries to dig, and emits a `blockMined` event with a delta count of 1 per dig. It has retry loops and stop handling, which is good, but it still relies on the bot being in exactly the right local position and line of reach at the moment of dig. There is no richer pre-dig verification step visible in the adapter flow. fileciteturn1file1

Similarly, build placement depends on finding a nearby reference block and hoping the placement face is valid. That is a practical Mineflayer limitation, but it reinforces the overall theme: execution is deterministic, while the agent’s understanding of the world is under-instrumented. The remedy is not more regex. It is more observation data. fileciteturn1file1

## What the architecture should become

## Target operating model

The right pivot is:

**LLM owns high-level orchestration.**
**Deterministic tools own execution.**
**Canonical reducers own world state.**
**Structured observations own recovery.**

That means the LLM should decide:
- what the user most likely meant,
- whether the request is a command, a conversation, or a clarification request,
- what goal should be created or modified,
- when a replan is warranted,
- what extra observation should be requested next,
- whether recovery should switch goals entirely.

The deterministic layer should decide:
- whether a specific tool call is syntactically valid,
- whether the arguments satisfy the tool schema,
- how to execute the action,
- how to report the result,
- whether the result event is complete enough to update the world model.

The world-state layer should decide:
- what the current canonical inventory is,
- what the position/health/food/game mode are,
- what facts are considered observed versus inferred,
- what is stale and what is fresh,
- what counts as completion or failure for a goal.

That division matches the existing strengths of the repo: schema validation is already centralized in `ToolDispatcher`, and `WorldStateProjector` is already the natural reducer boundary. The work is to stop leaking authority around those boundaries. fileciteturn14file0 fileciteturn12file0 citeturn157612view0

## Recommended LLM-centric redesign

### 1) Replace regex parsing with an intent-draft pipeline

Do not let chat messages jump straight into `CreateGoal` or `NavigateTo` through ad hoc regexes unless they are hard-control commands. Instead, introduce one canonical `IntentDraft` schema, produced by the LLM, that covers:
- addressed / not addressed / uncertain,
- intent category,
- normalized item or blueprint IDs,
- count,
- coordinates,
- confidence,
- rationale,
- clarification question when needed.

`ChatInterpreter` can keep only the absolute minimum deterministic routes: `stop`, `cancel`, `status`, `help`, and exact coordinate formats if you really want them. Everything else should go through an LLM-backed intent draft and then schema validation. That makes failures easier to reason about because “parser failure” becomes “draft rejected” rather than “wrong regex matched.” fileciteturn2file0 fileciteturn3file0

### 2) Make the LLM the reactivity brain, not the executor

The repo already has an error-recovery pattern in `TryRecoverFromGameErrorAsync`: it forms a structured prompt from the current goal, error message, and inventory summary, then asks the interpreter for an alternative goal. That is exactly the kind of loop the system should lean into. fileciteturn4file1

Generalize that idea into a “reactive planner” role:
- low-level failure occurs,
- observation event is captured,
- world model is refreshed,
- LLM is asked for a correction hypothesis,
- correction hypothesis is validated against tool schemas,
- a new goal or subgoal is enqueued only if it passes validation.

This is much better than making deterministic code guess at the next move after every error. The codebase already proves the pattern works in one place; it should become the standard recovery path. fileciteturn4file1

### 3) Expose richer context to the LLM

Give the LLM structured context, not prose only. At minimum, pass:
- current world snapshot,
- inventory delta history, not just current counts,
- active goal and phase,
- pending actions,
- last tool error,
- recent adapter result events,
- build origin facts,
- nearest relevant observations,
- tool schemas and allowed aliases,
- current constraints such as stale inventory, damage thresholds, and cooldowns.

The README already describes the system as having a world model, agent journal, dual memory gateways, and tool validation, which gives you the ingredients for this. The next step is to actually feed them into one intent/orchestration loop rather than scattering them across fallback prompts. citeturn157612view0 fileciteturn14file0 fileciteturn12file0

## Strong deterministic tools, weak deterministic parsing

A good rule for this project is:

**The more dangerous or spatially precise the action, the more deterministic the tool; the more ambiguous the user request, the more LLM-driven the interpretation.**

Examples:
- `stop`, `status`, `help` should stay deterministic.
- `mine`, `craft`, `smelt`, `place`, `move`, `findFlatArea` should stay deterministic at execution time.
- “gather wood,” “build a small house,” “make me a shelter near spawn,” and “can you get ready to mine more iron” should be LLM-orchestrated.
- “build here” should become “clarify or confirm origin” unless origin is explicit and validated.
- Error recovery should almost always go through the LLM, because the whole point is to reason over unusual state.

That gives you robustness without pretending the LLM should be directly touching Minecraft primitives. The LLM should ask for facts and choose among validated actions; it should not be hand-authoring low-level face selection, pathfinding, or inventory mutation. `ToolDispatcher` is already the correct place to enforce that discipline. fileciteturn14file0

## Centralized ownership model

Here is the ownership map I would enforce:

### Intent owner
`ChatInterpreter` / `LlmChatInterpreter` should own only one thing: converting human input and system recovery prompts into validated intent drafts or direct goal requests. It should not own inventory, pathfinding, or build origin resolution beyond producing a candidate draft. fileciteturn2file0 fileciteturn3file0

### State owner
`WorldStateProjector` should own all canonical world updates. If something changes the world model, it should happen there, not in the background service or in tool-specific ad hoc code. `WorldState` should remain the source of truth for current state plus provenance metadata. fileciteturn12file0 fileciteturn5file0

### Orchestration owner
`AgentBackgroundService` should own scheduling, replanning, prioritization, and recovery, but not state mutation beyond enqueueing or marking orchestration metadata. It should be the conductor, not the instrument. fileciteturn4file1

### Execution owner
`ToolDispatcher` and the Node adapter should own tool validation and tool execution. If a tool fails, that failure should return structured data rather than trigger speculative branch logic elsewhere. fileciteturn14file0 fileciteturn1file1

## Concrete recovery plan

### Phase 1: stop the semantic drift
1. Fix inventory reconciliation so mined drops are represented by actual obtained items, or by a post-action inventory diff, not by mined block names alone.  
2. Require explicit build origin when the user gives one; do not silently auto-origin unless the LLM or planner has explicitly chosen that fallback.  
3. Add a mandatory fresh status reconciliation after major material-changing action groups, especially gather and build phases.  
4. Keep `IsInventoryStale`, but treat it as a temporary guardrail, not a crutch. fileciteturn5file0 fileciteturn9file0 fileciteturn8file0 fileciteturn12file0

### Phase 2: collapse the parsing surface
1. Create a single `IntentDraft` model.  
2. Make the LLM generate that draft for any nontrivial chat input.  
3. Validate the draft against a schema, just like tool arguments are validated today.  
4. Keep a tiny deterministic fast-path for control commands only.  
5. Remove broad regex intent matching from the mainline path. fileciteturn14file0 fileciteturn2file0 fileciteturn3file0

### Phase 3: make world-state updates event-sourced and observable
1. Treat adapter result events as the only way execution updates the world model.  
2. Expand adapter events to include richer observations: reachability status, face selection failures, inventory diffs, pathing reasons, and target selection metadata.  
3. Move any “guessing” logic out of the projector and into the planner or recovery LLM prompt.  
4. Keep the projector pure and boring. fileciteturn1file1 fileciteturn5file0

### Phase 4: make replanning intentional
1. Replan only on meaningful signals: fresh status, a failed action, an observation change, or a confidence drop.  
2. Use the LLM to choose whether the agent should persist, reorient, or abandon the current goal.  
3. Add a small “recovery policy” layer that classifies failures into categories like navigation failure, unreachable target, missing recipe, stale inventory, and malformed user intent.  
4. Let the LLM pick among known recovery policies instead of inventing recovery from scratch every time. fileciteturn4file1 fileciteturn14file0

## Recommended code-level refactor map

### `Agent.Planning/ChatInterpreter.cs`
Strip it down to a thin deterministic pre-filter and a draft translator. Move broad regex intent recognition out of the critical path. Keep only hard commands and trivial shortcuts. fileciteturn2file0

### `Agent.Planning/LlmChatInterpreter.cs`
Promote it to the main intent orchestrator. It should produce structured drafts, handle truncation, and be the default route for ambiguity. The current JSON prompt and parsing pipeline are a good base, but it should become the normal path, not an exception. fileciteturn3file0

### `WebUI.Blazor/AgentBackgroundService.cs`
Make it the reactive conductor. It should schedule, observe, and decide when to invoke the LLM for reorientation. Reduce direct state fiddling and keep orchestration metadata separate from canonical world state. fileciteturn4file1

### `Agent.Core/WorldStateProjector.cs`
Make it the sole state reducer. Add richer event types if needed, but do not let orchestration logic creep into it. If an event cannot be projected deterministically, the answer is a better event, not more hidden side effects. fileciteturn5file0

### `MineflayerAdapter/index.js`
Upgrade it from “fire action and hope” to “execute action and report rich observations.” The adapter should be the richest sensor in the system. That is how you get the LLM out of guesswork and into informed orchestration. fileciteturn1file1

### `Agent.Tools/ToolDispatcher.cs`
Keep the schema validation and make it even stricter. This is the place where LLM output becomes safe executable action. Preserve the boundary; do not weaken it while making the upstream orchestration smarter. fileciteturn14file0

## What success looks like

When the salvage is working, the agent should feel different:

- It should ask clarifying questions instead of hallucinating coordinates.
- It should choose the right build origin or explicitly confirm fallback use.
- It should know inventory freshness instead of trusting stale snapshots.
- It should reorient after failure instead of retrying the same bad plan forever.
- It should use the LLM for meaning, not for low-level block placement math.
- It should use deterministic tools for execution, not for brittle text interpretation. fileciteturn3file0 fileciteturn4file1 fileciteturn12file0 fileciteturn14file0

## Bottom line

The project should not be rebuilt around the LLM; it should be rebuilt around a clear contract between three layers:

1. **LLM as the high-level interpreter and reorientation engine**
2. **Deterministic tools as the execution boundary**
3. **A single canonical world-state reducer**

That pivot directly addresses the current brittleness without throwing away the useful work already in the repo. The most valuable salvage move is to stop asking deterministic parsing to solve meaning, and stop asking the LLM to solve execution. The repo already contains most of the parts needed for that separation; it just needs the boundaries tightened and the authority model made explicit. citeturn157612view0 fileciteturn14file0 fileciteturn12file0 fileciteturn4file1
