### Council Review: Mineflayer Adapter Research Paper

**Assumptions flagged upfront:**

- `IWorldObservationGateway` referenced in memory is not visible in the provided source files; reviewed as an implicit seam only.
- `path_reset` event status determined from GitHub issue analysis (not raw source); confidence 85%.
- `bot.pathfinder.isBuilding()` corrected from initial assumption — confirmed present in official readme (confidence 95%).

---

#### What the Paper Gets Right

The core thesis is sound. The adapter currently dispatches all navigation via `await bot.pathfinder.goto(...)` — fire-and-wait — and never forwards any of the pathfinder plugin's own event surface back to C#. The paper correctly identifies this as the single biggest telemetry gap, and the paper's three-layer separation (adapter → projector → LLM) is already the architecture the codebase uses. Diagnosing this as "not missing data volume, missing disciplined projection" is accurate.

---

#### Defect Class 1 — Factual Errors About Existing Events

**DEF-PAPER-1: `path_reset` is a fabricated event name. (Confidence: 85%)**

Section 3 lists `path_reset` as a mineflayer-pathfinder event. It is not. GitHub issue #222 shows a user explicitly _requesting_ that the pathfinder emit a `path_reset` event — it does not. The actual signal for path failure is `path_update` with `status: 'noPath'`. The correct event list is: `goal_reached`, `path_update` (statuses: `success`, `noPath`, `timeout`, `partial`), `goal_updated`, `path_stop`. Using `path_reset` in a listener will silently do nothing.

**DEF-PAPER-2: `mineAborted` and `stopComplete` already exist and are already broken. (Confidence: 100%)**

Section 5 says the world picture is too shallow, citing inventory freshness and mining reliability — but misses a gap that's already in production. `index.js` emits `mineAborted` (Sprint 18 stop path) and `stopComplete` (from `handleStop()`). `WebSocketBridge.ParseEvent()` returns `null` for both. Neither has a C# `WorldEvent` subtype. The C# agent therefore cannot confirm that a stop command completed. This is a current production defect. The paper does not mention it.

---

#### Defect Class 2 — The `goto()` Hang Problem Is Underspecified

**DEF-PAPER-3: The paper does not address the known `pathfinder.goto()` infinite hang. (Confidence: 95%)**

From GitHub issue #222 and #273 on `mineflayer-pathfinder`: when the bot is obstructed by unbreakable blocks or encounters certain edge cases, `goto()` resolves neither its promise nor any event. It hangs silently. The paper proposes wiring `path_update` with `status: 'noPath'` as a recovery signal — which is correct and valuable — but the known hang case produces _no event at all_, so the listener alone is insufficient.

The current `index.js` has no timeout on any `await bot.pathfinder.goto(...)` call. Section 7 (Risks) doesn't list this. The implementation plan needs: (a) `path_update` listener for `noPath`/`timeout`/`partial`, AND (b) a per-`goto()` timeout with a `Promise.race()` wrapper as a hard backstop.

---

#### Defect Class 3 — Inventory Delta Mechanism Is Misidentified

**DEF-PAPER-4: Phase 2 says "prefer pickup events" but the actual hook is `bot.inventory.on('updateSlot')`. (Confidence: 90%)**

Section 4(D) proposes "inventory deltas and pickup events" and Phase 2 says to prefer pickup events over mining inference. This is correct directionally — the current `BlockMinedEvent → AddInventoryItem` path is an inference, not ground truth. But the paper never names the actual Mineflayer hook: `bot.inventory.on('updateSlot', (slot, oldItem, newItem) => {...})`. This is the real-time slot-level event confirmed in the Mineflayer API docs.

The paper also mentions `mineflayer-collectblock` as the mechanism for better inventory handling. It wouldn't help here; `collectblock` orchestrates the workflow but doesn't provide better inventory introspection than wiring `updateSlot` directly. Collectblock is a workflow wrapper, not an inventory accuracy tool.

---

#### Defect Class 4 — Phase 3 Has No Integration Point

**DEF-PAPER-5: "Observation summaries for the LLM" have no home in the existing architecture. (Confidence: 90%)**

Phase 3 proposes building compact observation summaries and feeding them into "the LLM evaluator." The existing pipeline is: `WorldEvent → WorldStateProjector → WorldState → IPlanner → ActionPlan`. There is no `IObservationSummarizer` interface, no `LlmEvaluator` step between the projector and the planner, and no documented hook in `AgentBackgroundService` for injecting this.

`IWorldModel` in `Agent.Core/Interfaces/IWorldModel.cs` _does_ define `Predict()`, `Reconcile()`, and `Uncertainty` — which is a natural home for observation-aware state management. The paper never references `IWorldModel`. The paper also does not reference `IWorldObservationGateway`, which is listed in active architectural concerns as the top refactoring candidate. If Phase 3 is meant to feed the LLM through the existing `ILlmProvider`/`ChatInterpreter` path, that needs to be stated explicitly with an integration point named.

---

#### Defect Class 5 — Dependency Gap in Phase 5

**DEF-PAPER-6: mineflayer-collectblock is not installed, and adding it changes the mine case semantics. (Confidence: 100%)**

`package.json` lists three dependencies: `mineflayer`, `mineflayer-pathfinder`, `ws`. Phase 5 proposes layering in `mineflayer-collectblock` and `mineflayer-tool` as supportive abstractions, but neither is in the dependency list and the install step is never mentioned.

More substantively: `collectblock` replaces the manual `findBlock → goto → dig` loop with a higher-level `collectBlock.collect(block)` call. This changes the semantics of the `mine` case in `dispatch()` non-trivially — the lower-level events (individual `blockMined` per dig, `blockNotFound`) would still need to be preserved or re-derived. The paper frames Phase 5 as purely additive, but it would require careful coordination with the existing mine loop.

---

#### Defect Class 6 — `bot.on('move')` Event Volume Is Unaddressed

**DEF-PAPER-7: Wiring movement changes naively creates a WebSocket flood. (Confidence: 95%)**

Section 4(B) proposes "position and movement changes." The current adapter already has `bot.on('move', () => sendEvent('move', botPos()))`. Mineflayer fires this on every physics tick during pathfinding — potentially 20 times per second. Each fires a JSON payload across the WebSocket to C#, where `WorldStateProjector.ApplyMove()` creates a new `WorldState` record and writes a fact. This is already the status quo and it's already happening, but the paper's Phase 1 proposal to add _more_ movement telemetry compounds the issue without recommending any debounce, throttle, or significance filter.

The `MoveEvent` overhead is functionally acceptable only because the C# projector is fast, but any new position-adjacent observation (entity positions, pathfinder position tracking) will need explicit rate control.

---

#### What's Genuinely Missing From Paper Scope

These are gaps the paper explicitly opts out of, which is reasonable, but worth flagging:

**Open Question OQ-1:** The paper is scoped to Mineflayer core + three plugins. It doesn't address the `IWorldObservationGateway` seam that's flagged as the top architectural concern. Should the new adapter events be routed through `IWorldObservationGateway`, through `IWorldAdapter.ReceiveEventsAsync`, or both? This should be resolved before Phase 1 work begins.

**Open Question OQ-2:** `path_update` fires on _every_ path recalculation during a `goto()`. At long distances this can be dozens of times. The paper doesn't specify whether every update should be forwarded to C# or only status transitions (`noPath`, `timeout`). Forwarding all of them would be noisier than the `move` event. Recommend: only forward on non-`success` statuses plus `goal_reached`.

**Open Question OQ-3:** The paper says "the LLM should decide whether to continue, recover, clarify, or replan." But `IReplanGovernor` already manages the stall-and-retry cycle. There's a design collision: if pathfinder `noPath` events trigger a new observation summary → LLM call → replan, that's a second replan trigger path running in parallel with the existing `ReplanGovernor`. Which has authority, and under what conditions does the LLM path override the deterministic governor?

**Open Question OQ-4:** The paper's Section 1 says "the adapter should not try to understand the game" — but Phase 4 says "the LLM should decide whether to switch tools, choose a new target..." This is a tension. The adapter is pure observation, but the paper's Phase 4 collapses the observation-evaluator boundary by saying the LLM evaluates observations directly. In the current architecture that evaluation happens in `AgentBackgroundService` + `IPlanner`. Is Phase 4 proposing a new LLM call path, or is it describing what should already happen with better observation input?

---

#### Summary Table

|#|Finding|Severity|Confidence|
|---|---|---|---|
|DEF-PAPER-1|`path_reset` is not a real pathfinder event; `path_update{noPath}` is correct|High|85%|
|DEF-PAPER-2|`mineAborted`/`stopComplete` are unhandled today — paper misses this existing gap|High|100%|
|DEF-PAPER-3|`goto()` hang bug not addressed; event listener alone insufficient|High|95%|
|DEF-PAPER-4|Inventory hook is `bot.inventory.on('updateSlot')`, not pickup events; collectblock won't help|Medium|90%|
|DEF-PAPER-5|Phase 3 observation summaries have no integration point in existing C# architecture|Medium|90%|
|DEF-PAPER-6|collectblock not in package.json; Phase 5 changes mine case semantics non-trivially|Medium|100%|
|DEF-PAPER-7|`move` event flood problem unaddressed; Phase 1 expansion worsens without throttle|Medium|95%|
|OQ-1|No mention of IWorldObservationGateway routing decision|Arch|—|
|OQ-2|`path_update` fire rate unspecified — recommend status-transition-only forwarding|Design|—|
|OQ-3|LLM replan path conflicts with existing ReplanGovernor authority|Design|—|
|OQ-4|Phase 4 collapses observation/evaluation boundary; clarification needed|Design|—|

The highest-value correct recommendations in the paper are: (1) wire `path_update` for failure status telemetry, (2) wire `bot.inventory.on('updateSlot')` for real inventory ground truth, and (3) introduce a typed adapter observation layer rather than expanding the current ad-hoc `sendEvent` approach. Those three things are real wins. The implementation plan just needs the defects above corrected before work starts.