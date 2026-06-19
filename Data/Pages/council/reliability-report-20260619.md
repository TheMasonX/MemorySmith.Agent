# MemorySmith.Agent reliability report

Date: 2026-06-19  
Scope: `TheMasonX/MemorySmith.Agent`, branch `sprint-5-tool-safety` / PR #1

## Executive summary

The project is past the scaffold stage. It now has a real LLM chat interpreter, a schema-validating tool boundary, a Minecraft adapter that starts Node.js and speaks WebSocket, and a planner that can turn chat into `gather`, `build`, and `navigate` goals. The important shift is that the system is no longer "LLM talks to world directly"; it is "LLM proposes intent, deterministic code decomposes it, tools dispatch actions, and the world adapter executes them." fileciteturn12file0turn10file0turn11file0turn15file0turn27file0

The main reliability problem is not absence of features. It is inconsistent success semantics, default mismatches, and missing closed-loop verification. In several places the code treats "dispatch succeeded" as success even though the actual world outcome is only known later through events. In others, the planner, tool docs, and Node adapter disagree on defaults such as flat-area radii and minimum area thresholds. Those inconsistencies are the kind that create "works in chat, fails in world" behavior. fileciteturn18file0turn19file0turn20file0turn29file0turn15file0turn25file0

## What is solid already

The chat pipeline is genuinely useful now. `LlmChatInterpreter` truncates long messages, applies a distance gate, skips the LLM for known fast-path intents, rate-limits calls, and parses structured JSON with a fallback that can even salvage truncated model output. That is a strong pattern for keeping the LLM out of the hot path when the intent is already obvious. fileciteturn12file0turn13file0

The tool boundary is also much better than a plain function-calling layer. `ToolDispatcher` validates arguments against each tool's schema before execution, rejects unknown tools, rejects unknown properties, and type-checks object members in a minimal JSON-schema subset. The tests cover those failure modes directly. fileciteturn10file0turn27file0

On the Minecraft side, the adapter now does the hard practical work: starts Node.js, waits for the socket, forwards actions, filters chat noise, handles `mine`, `place`, `wander`, `findFlatArea`, `craft`, `smelt`, and emits structured events back to C#. The flat-area scanner is materially better than a naive search: it skips liquids, scores compactness and flatness, yields periodically, and returns a richer result for replanning. fileciteturn11file0turn22file0turn23file0turn25file0turn26file0

## More inconsistencies and fragility

### 1) Success often means "dispatched," not "done"

This is the biggest reliability smell in the current codebase. `MoveToTool`, `MineBlockTool`, `PlaceBlockTool`, `CraftItemTool`, `FurnaceTool`, and `FindFlatAreaTool` all return success after sending an action to the world adapter. That is useful as an acknowledgment, but it is not world completion. The actual state change arrives later via world events such as `moveComplete`, `blockMined`, `blockPlaced`, `craftComplete`, `smeltComplete`, and `flatAreaFound`. If those events are missed, delayed, or misprojected, the planner can believe progress happened when it did not. fileciteturn18file0turn19file0turn20file0turn29file0turn35file0turn25file0turn22file0turn33file0

That means the correct definition of success should be layered:

- **Dispatch success**: the command was accepted and sent.
- **World acknowledgment**: the adapter emitted the expected event.
- **World success**: the world state actually changed as intended.
- **Goal success**: the planner's condition is satisfied.

Right now, the system mostly exposes the first layer and relies on the rest implicitly. fileciteturn18file0turn22file0turn33file0

### 2) Defaults disagree across layers

There are at least three flat-area default regimes in play. `FindFlatAreaTool` still advertises default `radius = 20` and `minFlatArea = 9`. The planner's build preflight uses a radius of `30` and minimum area `25`. The Node adapter uses `FLAT_AREA_SCAN_RADIUS = 32` and `FLAT_AREA_MIN_SIZE = 25`, and it includes `searchedRadius` in the fallback event so C# can retry intelligently. That is a real source of drift. A user or an LLM looking only at one layer will get a different mental model than the actual runtime. fileciteturn29file0turn15file0turn25file0

There are smaller naming mismatches too. `StatusTool` is named `Status`, but the test suite and runtime paths also use `GetStatus` as an alias. `MineBlock` sometimes uses `minecraft:` prefixes and sometimes not. Those are survivable, but every alias widens the seam where bugs can hide. fileciteturn32file0turn27file0turn17file0turn31file0

### 3) The planner and adapter still depend on hidden conventions

Gathering is usable, but it is convention-heavy. `GatherItemDecompose` starts with `SearchMemory`, then may insert `Wander` only after a recent `BlockNotFound` fact for one of the source blocks, then emits `MineBlock` against each source block and a final status refresh. This is sensible, but it assumes that the right source blocks are encoded, that the memory search knows where to look, and that the inventory/world feedback loop is accurate. fileciteturn17file0turn33file0

Building is more robust than gathering, but it is also more fragile in practice because it layers many assumptions: the world must have or discover a valid origin, the flat-area scan must succeed, the build must have the right materials, the bot must be near a crafting table or furnace when needed, and the checkpoint logic must agree with the current world state. The system already retries some of this, but each extra precondition is another place where the action plan can look valid while still failing in the world. fileciteturn15file0turn16file0turn26file0turn35file0turn19file0

### 4) The chat loop can block world event processing

The repo's own architecture review calls out a serious issue: the LLM call lives in the event-processing loop, so a long chat completion can delay application of other world events such as health changes or death. The same review also notes missing chat history, a never-pruned rate limiter, and fragile JSON parsing. Those are not cosmetic problems; they directly affect correctness and responsiveness. fileciteturn38file0turn38file0

### 5) Several tools have one-sided completion models

`FurnaceTool` dispatches `smelt` and then relies on the Node side to wait for output; the C# side has no direct completion acknowledgment besides the later world event stream. The architecture review specifically notes that this leaves the host without a clean completion signal unless it polls status. `CraftItemTool` similarly dispatches and trusts the adapter to do the right thing, while the Node handler itself depends on a nearby crafting table and recipe resolution. fileciteturn35file0turn38file0

### 6) The adapter still has operational risks

The adapter already had to compensate for Mineflayer API changes with a `toVec3()` helper because current Mineflayer expects `.floored()` on positions. That is a concrete reminder that the Node layer is version-sensitive. The architecture review also points out that `place` uses a position-derived offset that can be imprecise during movement, that there is no reconnection strategy if the bot disconnects, and that `move` events are very chatty. Those are all common sources of "it worked once, then got weird" failures. fileciteturn23file0turn38file0

## How to make it more reliable

### Priority 1: make success state explicit

The first move should be to stop treating dispatch success as the end of the story. The system needs an explicit action lifecycle: `Accepted`, `Started`, `Progress`, `Completed`, `Failed`, `TimedOut`, `Aborted`. The planner should react to completion and failure events, not to the fact that a tool object returned `true`. This is the cleanest way to eliminate the current ambiguity. fileciteturn18file0turn22file0turn33file0

A practical implementation would be to give each action a correlation id, have the adapter echo that id in its events, and have the background service track pending actions until completion or timeout. That would make `MineBlock`, `CraftItem`, `SmeltItem`, and `FindFlatArea` far easier to reason about.

### Priority 2: align defaults and constants across layers

Put the tool defaults, planner defaults, and adapter defaults in one canonical place, then generate or share them. Right now the same concept has three numbers attached to it depending on where you look. The same cleanup should be applied to naming aliases such as `Status`/`GetStatus` and the block-name prefix handling. The goal is not "remove flexibility"; the goal is "one source of truth, many call sites." fileciteturn29file0turn15file0turn25file0turn32file0turn27file0turn31file0

### Priority 3: decouple world events from LLM latency

Move chat interpretation off the world-event hot path. The architecture review already recommends a separate channel/consumer model so health, death, kicked, and block events can be applied promptly even while an LLM call is in flight. That change would improve both correctness and debuggability. fileciteturn38file0

### Priority 4: close the loop with integration fixtures

The current tests are good seam tests, but they do not yet prove a full gather/build loop. The next reliability gain comes from tests that exercise the actual chain:

- chat intent → goal creation,
- goal → action decomposition,
- action → adapter dispatch,
- adapter event → world-state projection,
- world-state projection → planner decision.

A small deterministic test world with a known ore pocket and a known flat build area would be enough to expose most of the current failure modes. The architecture review already flags missing end-to-end confidence in `CraftItem`, build resume, adapter reconnect, and dashboard push behavior. fileciteturn27file0turn38file0turn33file0

### Priority 5: make the adapter resilient, not just capable

Add reconnection strategy, per-action timeouts at the host level, and a clean ack path for long-running actions like smelting. Throttle noisy move events. Harden `place` with a deliberate navigation step and better reference-block selection. Those changes do not make the system "smarter"; they make it much more predictable under imperfect world conditions. fileciteturn38file0turn11file0turn26file0turn19file0

### Priority 6: keep the planner simple where it already works

The current deterministic-first approach is good. Resist the temptation to let the LLM take over more of the tool semantics. The planner should own decomposition and replanning; the LLM should only choose among known patterns or fill in genuinely novel gaps. The tighter the tool schema and the more explicit the success criteria, the less room there is for hallucination to turn into a world action. fileciteturn12file0turn15file0turn17file0turn38file0

## Most important remaining risks

1. **False progress**: the planner believes the world changed because a tool returned success, but the world event never arrived. fileciteturn18file0turn19file0turn20file0turn22file0  
2. **Default drift**: different layers disagree on radii, thresholds, or aliases. fileciteturn29file0turn15file0turn25file0turn32file0  
3. **Event starvation**: the LLM blocks prompt world-state updates. fileciteturn38file0  
4. **Adapter brittleness**: Mineflayer API changes, disconnects, or placement quirks break the loop. fileciteturn23file0turn38file0  
5. **Completion ambiguity**: long-running actions finish somewhere in Node, but the host has no single authoritative completion signal. fileciteturn35file0turn38file0  

## Bottom line

The project is now capable enough that the next gains should come from reliability engineering, not feature invention. The strongest themes are already visible in the code: deterministic-first planning, schema-gated tools, structured world events, and a world adapter that tries hard to keep the bot alive. The weakest themes are also visible: inconsistent defaults, success semantics that stop at dispatch, and a few places where the event loop can get ahead of the world. Fix those, and the system becomes much easier to trust. fileciteturn10file0turn12file0turn15file0turn22file0turn25file0turn38file0
