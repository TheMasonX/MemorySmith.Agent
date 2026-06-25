# MemorySmith.Agent — Sprint 35 Branch Audit
**Repository:** TheMasonX/MemorySmith.Agent  
**Branch focus:** `sprint-35-llm-first`  
**Report date:** 2026-06-24  
**Scope:** current branch state versus stated project goals, with emphasis on in-progress areas and architecture health.

## What this report is based on
This review is grounded in the branch-visible source and sprint documentation I could read directly from the repository, including:

- `README.md`
- `Data/Pages/roadmap.md`
- `Data/Pages/architecture.md`
- `Data/Pages/council/sprint35-council-20260622.md`
- `Data/Pages/Tasks/agent-handoff-sprint35-llm-first.md`
- `Agent.Planning/LlmChatInterpreter.cs`
- `Agent.Planning/ChatInterpreter.cs`
- `WebUI.Blazor/AgentBackgroundService.cs`
- `Agent.Core/ActionQueue.cs`
- `Agent.Core/WorldStateProjector.cs`
- `Agent.World.Minecraft/WebSocketBridge.cs`
- `MineflayerAdapter/index.js`
- `Agent.Planning/Decomposition/BuildGoalDecomposer.cs`
- `Agent.Planning/Goals/BuildGoal.cs`
- `Agent.Tools/ToolDispatcher.cs`
- `Agent.Memory/RestMemoryGateway.cs`
- `WebUI.Blazor/Program.cs`
- `WebUI.Blazor/ApiKeyMiddleware.cs`

Where a sampled file resolved to the same SHA on `main` and `sprint-35-llm-first`, I treated that as evidence that the file content is currently unchanged on both refs for that sample.

---

# Executive summary

The branch is **not vibe-code slop**. The codebase has real structure, strong guardrails, and a lot of disciplined work already in place. The strongest parts are schema validation at the tool boundary, explicit action correlation, the world-state projector, inventory normalization, auth middleware, and the move toward typed events and bounded contexts.

The main problem is that the repository is in a **transition state with overlapping architectural contracts**. The project goals now point toward an LLM-first semantic layer, but the code still contains a deterministic parser/goal-creation path that is not fully retired. That creates two parallel “sources of truth” for intent and action completion.

The result is brittle behavior where the system can appear to be working while still drifting from reality.

### Bottom line
- **Architectural alignment with stated goals:** 68%
- **Operational robustness of in-progress areas:** 63%
- **Codebase health trajectory:** improving, but only if the architecture is consolidated
- **Confidence in the highest-severity findings:** 90%+

### Highest-risk issues
1. **Inventory truth is still inferred, not authoritative.** `blockMined` adds the mined block name to inventory instead of the drop, and craft/smelt do not force a reconciliation snapshot afterward. This is a direct source of silent drift.  
   Evidence: `WorldStateProjector.ApplyBlockMined` adds `e.Block`, not a drop name (`Agent.Core/WorldStateProjector.cs` L74-L82); `MineflayerAdapter` emits `blockMined` with `count: 1` and does not emit `sendBotStatus()` after craft/smelt completion (`MineflayerAdapter/index.js` L55-L73, L126-L165, L168-L223).

2. **The branch still has split-brain chat interpretation.** `LlmChatInterpreter` still short-circuits to deterministic fast paths for common commands, while `ChatInterpreter` still creates goals directly. That conflicts with the sprint-35 architecture doc that says parsers should produce `IntentDraft` only and planners should create goals.  
   Evidence: `LlmChatInterpreter` early-returns on `CreateGoal`, `CancelGoal`, `QueryHelp`, `QueryStatus`, `NavigateTo` (`Agent.Planning/LlmChatInterpreter.cs` L61-L69); `ChatInterpreter` still returns `CreateGoal` results directly (`Agent.Planning/ChatInterpreter.cs` L178-L239); the sprint-35 handoff explicitly says “Parsers never create goals” and “Chat → IntentDraft → Planner → Goal” (`Data/Pages/Tasks/agent-handoff-sprint35-llm-first.md` L16-L43, L131-L197).

3. **Build origin handling is still ambiguous and brittle.** Partial origins are accepted as “explicit” if any one axis is present, and missing axes default to zero. That can silently move the scan/build anchor to the wrong place.  
   Evidence: `BuildGoal.HasExplicitOrigin => OriginX.HasValue || OriginY.HasValue || OriginZ.HasValue` (`Agent.Planning/Goals/BuildGoal.cs` L43-L52); `BuildGoalDecomposer` then zero-fills missing origin values (`Agent.Planning/Decomposition/BuildGoalDecomposer.cs` L28-L64).

4. **A lot of protocol and parsing failures are silently swallowed.** The code often falls back to `null` or “best effort” without surfacing enough context to distinguish malformed input from legitimate absence. That makes debugging harder and can mask protocol drift.  
   Evidence: `LlmChatInterpreter.ParseDecision` catches all exceptions and returns `null` (`Agent.Planning/LlmChatInterpreter.cs` L152-L237); `WebSocketBridge.ParseEvent` catches all exceptions and returns `null` (`Agent.World.Minecraft/WebSocketBridge.cs` L197-L310); `MineflayerAdapter` suppresses log I/O errors and some action errors are handled best-effort (`MineflayerAdapter/index.js` L94-L106, L175-L180, L225-L240).

### What is already strong
- JSON schema validation before tool execution (`Agent.Tools/ToolDispatcher.cs` L59-L127).
- Fixed-time auth check for `/api/*` routes (`WebUI.Blazor/ApiKeyMiddleware.cs` L8-L47).
- Typed world-state projection with inventory normalization (`Agent.Core/WorldStateProjector.cs` L84-L131).
- Action correlation and timeout sweeping (`WebUI.Blazor/AgentBackgroundService.cs` L323-L520, L1120-L1659).
- Dedicated world/agent memory separation in DI (`WebUI.Blazor/Program.cs` L64-L123).
- Concern-aware logging and a bounded journal (`README.md` L13-L25, `Data/Pages/architecture.md` L87-L99).

---

# Alignment with the project’s stated goals

## Goal 1: Deterministic first, LLM is opt-in
The repository still claims deterministic-first behavior in the high-level docs (`README.md` L29-L45, L157-L169; `Data/Pages/architecture.md` L101-L111). But Sprint 35’s own council/handoff docs explicitly move the architecture toward LLM-first intent handling (`Data/Pages/Tasks/agent-handoff-sprint35-llm-first.md` L16-L43, L131-L197; `Data/Pages/council/sprint35-council-20260622.md` L42-L46, L96-L99).

**Assessment:** the codebase is in a policy transition, not a stable state.  
**Confidence:** 97%

## Goal 2: World adapter should expose reality, not guesses
The architecture docs say the world adapter and projector should keep the agent grounded in actual game state (`Data/Pages/architecture.md` L45-L63). The current code still has gaps in the reality loop: inventory reconciliation is incomplete, build origin is partially inferred, and completion is often inferred from state that may already be stale.

**Assessment:** partially implemented, but not yet reliable enough for “current world truth.”  
**Confidence:** 95%

## Goal 3: Clean bounded contexts with deep modules
The code mostly follows this goal. The bounded contexts are clear in the solution structure (`MemorySmith.Agent.slnx`), and the most important boundaries are real: tools, planner, world adapter, memory, and web host. The problem is that `AgentBackgroundService` still acts like an orchestration god-object.

**Assessment:** structurally present, but not yet evenly enforced.  
**Confidence:** 90%

## Goal 4: Robust safety boundaries around tool execution
This is one of the strongest parts of the repo. Tool input validation is explicit, and invalid tool arguments or exceptions are converted into `ToolResult` failures instead of crashing the agent loop (`Agent.Tools/ToolDispatcher.cs` L59-L127). That is a real strength.

**Assessment:** strong and aligned.  
**Confidence:** 96%

## Goal 5: Secure control plane
The API key middleware is a meaningful improvement over open REST control (`WebUI.Blazor/ApiKeyMiddleware.cs` L8-L47; `WebUI.Blazor/Program.cs` L274-L279). The remaining deployment risk is that if `Agent:ApiKey` is absent, the API is intentionally open for dev convenience.

**Assessment:** good mechanism, but operationally risky if misconfigured.  
**Confidence:** 94%

---

# Findings

## P0 — Inventory truth is still approximate, not authoritative

### Why this matters
Inventory is used everywhere as a completion signal, a replanning signal, and a diagnostic signal. If it drifts, the entire agent can start reasoning from a false state.

### Evidence
- `WorldStateProjector.ApplyBlockMined` adds the mined block key directly into inventory (`Agent.Core/WorldStateProjector.cs` L74-L82).
- `MineflayerAdapter` emits `blockMined` with `count: 1`, but that still represents the mined block name rather than the drop item (`MineflayerAdapter/index.js` L55-L73).
- `MineflayerAdapter` emits `craftComplete` and `smeltComplete`, but `sendBotStatus()` is only clearly invoked on spawn and explicit `status` (`MineflayerAdapter/index.js` L267-L285, L289-L301, L113-L115, L162-L165, L168-L223).
- The council doc independently identifies the same bug class and recommends `playerCollect` plus post-action reconciliation (`Data/Pages/council/sprint35-council-20260622.md` L22-L35, L104-L116).

### Impact
- Mined blocks can be counted instead of drops.
- Crafted/smelted output can lag behind reality.
- Completion checks can be wrong when `IsInventoryStale` is not refreshed by a new status snapshot.

### Recommendation
Treat inventory as **event-sourced plus reconciled**, not inferred. The current code needs a real item-collection event path and a post-craft/post-smelt status refresh.

### Confidence
98%

---

## P0 — Chat interpretation still has split-brain architecture

### Why this matters
The sprint-35 docs say the interpreter should produce semantic intent, not goals. The code still has two different styles of interpretation:

- deterministic regex -> goal creation
- LLM fallback -> goal creation
- direct planner assumptions downstream

That creates overlap and makes debugging intent behavior hard.

### Evidence
- `LlmChatInterpreter` still returns early for the deterministic fast paths `CreateGoal`, `CancelGoal`, `QueryHelp`, `QueryStatus`, `NavigateTo` (`Agent.Planning/LlmChatInterpreter.cs` L61-L69).
- `ChatInterpreter` still creates `CreateGoal` interpretations directly for build/gather/craft/navigate routes (`Agent.Planning/ChatInterpreter.cs` L178-L239).
- The sprint-35 handoff explicitly says parsers should not create goals and that `IntentDraft` should sit between chat interpretation and planning (`Data/Pages/Tasks/agent-handoff-sprint35-llm-first.md` L16-L43, L131-L197).
- The council doc calls the current situation an architectural inversion and approves the transition only with blockers (`Data/Pages/council/sprint35-council-20260622.md` L42-L46, L64-L65, L96-L99).

### Impact
- Common commands bypass the LLM entirely.
- The LLM is used less than the branch goals imply.
- The system can’t yet reason about ambiguous requests in a unified way.

### Recommendation
Finalize the transition layer:
- keep only stop/status/inventory/help as deterministic shortcuts,
- route everything else through semantic intent,
- move goal creation into the planner/intent manager layer.

### Confidence
99%

---

## P0 — Build origin semantics are brittle and can silently point to the wrong place

### Why this matters
Build location is spatially central to the goal. A partially specified origin or a zero-filled fallback can put the agent in the wrong place without obvious failure.

### Evidence
- `BuildGoal.HasExplicitOrigin` is true if any one of X/Y/Z is present, not if all are present (`Agent.Planning/Goals/BuildGoal.cs` L43-L52).
- `BuildGoalDecomposer` then copies `OriginX`, `OriginY`, and `OriginZ` into local variables, defaulting missing values to 0 (`Agent.Planning/Decomposition/BuildGoalDecomposer.cs` L28-L64).
- The decomposer also logs that explicit origins are used as scan centers, which is a subtle semantic shift from “build here” to “search around here” (`Agent.Planning/Decomposition/BuildGoalDecomposer.cs` L28-L41).
- Sprint docs show the project is still deciding how explicit, player-position, and auto-scanned origins should interact (`Data/Pages/Tasks/agent-handoff-sprint35-llm-first.md` L103-L120; `Data/Pages/council/sprint35-council-20260622.md` L117-L123).

### Impact
- Partial chat parsing or partial state can yield `0,0,0` for missing axes.
- “Build at X Y Z” may behave more like “scan around X Y Z” depending on the phase.
- This is a silent contract, not a validated one.

### Recommendation
Require all-or-none origin specification and validate it before decomposition. Store explicit origin source as a typed enum and make fallback behavior visible in logs and status.

### Confidence
94%

---

## P1 — `AgentBackgroundService` is still too much of a second reducer

### Why this matters
`WorldStateProjector` is supposed to be the canonical reducer, but `AgentBackgroundService` still mutates the world state and owns several behavior loops that belong in dedicated components.

### Evidence
- `AgentBackgroundService` owns goal lifecycle, damage interrupt, stall detection, action correlation, replan timing, world-state mutation, and chat orchestration (`WebUI.Blazor/AgentBackgroundService.cs` L162-L239, L323-L520, L1120-L1659).
- The architecture docs claim the projector is the pure reducer and that the service is the host/orchestrator (`Data/Pages/architecture.md` L45-L63, L87-L99).
- Sprint-35 docs already flag the file as a likely split candidate for Sprint 36 (`Data/Pages/Tasks/agent-handoff-sprint35-llm-first.md` L255-L275; `Data/Pages/council/sprint35-council-20260622.md` L71-L73, L81-L82).

### Impact
- Cross-cutting responsibilities are hard to test.
- State transitions are distributed across multiple methods.
- Replan and recovery behavior becomes harder to reason about.

### Recommendation
Split the service into:
- an event consumer,
- a goal lifecycle coordinator,
- a recovery policy component,
- a chat intent coordinator,
- and a separate observation evaluator.

### Confidence
95%

---

## P1 — Silent fallback behavior is masking protocol and parsing drift

### Why this matters
“Best effort” is useful in edge handling, but overuse of broad catches turns real drift into invisible nulls.

### Evidence
- `LlmChatInterpreter.ParseDecision` catches all exceptions and returns null (`Agent.Planning/LlmChatInterpreter.cs` L152-L237).
- `WebSocketBridge.ParseEvent` catches all exceptions and returns null (`Agent.World.Minecraft/WebSocketBridge.cs` L197-L310).
- `RestMemoryGateway.UpdatePageAsync` catches all errors when fetching the existing page and silently falls back to the page slug title (`Agent.Memory/RestMemoryGateway.cs` L71-L90).
- `MineflayerAdapter` wraps several log and action behaviors in best-effort handlers (`MineflayerAdapter/index.js` L94-L106, L175-L180, L225-L240).

### Impact
- JSON schema mismatch can look like “no event arrived.”
- Adapter protocol changes can fail silently.
- Memory update edge cases can degrade without clear signals.

### Recommendation
Keep the fallback, but add explicit categorized diagnostics:
- malformed input,
- schema mismatch,
- missing field,
- unexpected event type,
- retryable transport error.

### Confidence
93%

---

## P1 — Build and flat-area scanning are close, but still contract-heavy

### Evidence
- `MineflayerAdapter` emits `searchedRadius` when no flat area is found (`MineflayerAdapter/index.js` L102-L108).
- `WebSocketBridge.ParseEvent` currently parses `flatAreaFound` without `searchedRadius` (`Agent.World.Minecraft/WebSocketBridge.cs` L293-L302).
- `BuildGoalDecomposer` uses the current world facts and a `requireOrigin` setting, which means the build scan behavior is still strongly policy-driven in code instead of explicitly surfaced to the user (`Agent.Planning/Decomposition/BuildGoalDecomposer.cs` L57-L64).

### Impact
- Build retry logic cannot distinguish “search too small” from “no valid area found.”
- The user can be surprised by the build anchor behavior.

### Recommendation
Persist `searchedRadius` in the event model and expose origin-source policy in user-facing status. Make the scan vs build contract explicit.

### Confidence
96%

---

## P2 — Memory gateway API is serviceable but has hardcoded assumptions

### Evidence
- `SearchAsync` hardcodes `limit=20` (`Agent.Memory/RestMemoryGateway.cs` L32-L44).
- `CreatePageAsync` ignores the `type` parameter and always uses `options.DefaultPageRole` (`Agent.Memory/RestMemoryGateway.cs` L59-L69).
- `ToSlug` is straightforward but not collision-safe or globally unique (`Agent.Memory/RestMemoryGateway.cs` L94-L99).

### Impact
- Search breadth is limited by implementation, not by caller need.
- Callers may assume `type` matters when it does not.
- Title-based slug generation can collide.

### Recommendation
Surface search limit as a parameter and either use or remove the unused `type` argument. Add slug collision handling if page creation can be user-generated.

### Confidence
81%

---

## P2 — Documentation now contains two competing operating models

### Evidence
- README and architecture docs still describe “deterministic first, LLM opt-in” (`README.md` L29-L45; `Data/Pages/architecture.md` L101-L111).
- Sprint-35 handoff and council docs now define “LLM owns intent” and “parsers never create goals” (`Data/Pages/Tasks/agent-handoff-sprint35-llm-first.md` L16-L43, L131-L197; `Data/Pages/council/sprint35-council-20260622.md` L102-L157).

### Impact
- Contributors will implement to different mental models.
- Code reviews will disagree on “correct” behavior.
- Future regressions will be caused by docs, not just code.

### Recommendation
Pick one canonical architecture doc and explicitly mark the old one as historical. Put the transition contract in one place.

### Confidence
99%

---

# Refactoring and codebase-health opportunities

## 1. Split `AgentBackgroundService`
This is the biggest health win available. It currently owns too many distinct responsibilities.

Suggested seam:
- `AgentEventLoop`
- `ActionCorrelationTracker`
- `GoalLifecycleCoordinator`
- `RecoveryPolicy`
- `ChatIntentCoordinator`
- `ObservationEvaluator`

**Value:** very high  
**Effort:** high  
**Confidence:** 95%

## 2. Introduce a single semantic intent model
`IntentDraft` is already implied by the sprint docs. Finish it and make it the only chat-facing semantic artifact.

**Value:** very high  
**Effort:** medium-high  
**Confidence:** 98%

## 3. Make world observations explicit and typed
The event stream is already typed. The next step is to make “expected effect vs observed effect” a first-class concept for tools, especially mine/craft/smelt/place.

**Value:** very high  
**Effort:** medium  
**Confidence:** 92%

## 4. Move brittle fallback parsing into explicit recovery policies
Do not remove resilience; make it visible.

**Value:** high  
**Effort:** medium  
**Confidence:** 91%

## 5. Replace implicit origin defaults with validated contracts
Partial origin values should be rejected or explicitly completed, never silently zero-filled.

**Value:** high  
**Effort:** low-medium  
**Confidence:** 94%

---

# Positive findings worth keeping

- Tool boundary validation is strong and well-contained (`Agent.Tools/ToolDispatcher.cs` L59-L127).
- `TryGetInt32` prevents a subtle scientific-notation bug (`Agent.Tools/ToolDispatcher.cs` L187-L203).
- Inventory normalization is handled centrally in the projector (`Agent.Core/WorldStateProjector.cs` L84-L131).
- Action correlation and timeout sweeping are real improvements over the older fire-and-forget model (`WebUI.Blazor/AgentBackgroundService.cs` L323-L520, L1120-L1659).
- API auth uses constant-time comparison and is applied at the `/api` boundary (`WebUI.Blazor/ApiKeyMiddleware.cs` L25-L47).
- The codebase is clearly being evolved through explicit sprint docs and council review, which is a major maintenance advantage (`Data/Pages/council/sprint35-council-20260622.md` L3-L18, L50-L99).

---

# Assumptions

1. I treated the branch ref `sprint-35-llm-first` as the source of truth for current-state review.
2. For sampled files, where the connector returned the same SHA on `main` and the branch ref, I assumed those files are unchanged on both refs.
3. I focused on the in-progress architecture and the code paths most directly tied to the sprint-35 goals rather than attempting a line-by-line audit of every file in the repo.
4. I treated the sprint council and handoff documents as intent/state documents, not as proof that all planned changes were already implemented.

---

# Open questions
None material remain for this branch-state audit. The main architectural transition is clear enough from the code and docs to evaluate without more branch metadata.

---

# Confidence profile
- Inventory truth / drop-vs-block mismatch: **98%**
- LLM-first split-brain interpretation: **99%**
- Build origin brittleness: **94%**
- `AgentBackgroundService` over-consolidation: **95%**
- Silent fallback / swallowed-error risk: **93%**
- Memory gateway hardcoded assumptions: **81%**
- Overall report confidence: **92%**

---

# Prioritized next moves
1. Finish the semantic intent transition and remove goal creation from parsers.
2. Make inventory authoritative through explicit item/collection reconciliation.
3. Validate build origin as an explicit contract.
4. Split the overgrown service into dedicated orchestration components.
5. Replace silent catch-and-null patterns with typed failure categories and traceable diagnostics.

