# MemorySmith.Agent Audit Report
**Scope:** sprint-35-llm-first / current merged main snapshot  
**Generated:** 2026-06-23 13:27 UTC

## Executive summary

The intent interpreter and build planner have clearly improved: deterministic routing now covers common gather/build/craft commands, the HTN layer resumes build checkpoints, inventory normalization is in place, and the Minecraft bridge already emits several useful world events. The code is moving in the right direction.

The main gap is still **observation and verification**. The build pipeline can create `PlaceBlock` actions, and the Node adapter can emit `blockPlaced`, but the C# side does not yet turn that into a strong “this blueprint block is confirmed present and still present” guarantee. That means a build can look successful in the action log while still being partially wrong in the world. This is the most important remaining brittleness.

A second issue is **correlation loss**. The adapter sends `correlationId` with placement events, but the typed C# event model does not preserve it, and the build orchestration path shown here does not visibly complete placement actions from `BlockPlacedEvent`. That weakens lifecycle tracking and makes recovery/replanning much less reliable.

A third issue is **checkpoint and replan fragility**. Build checkpoint facts exist, but the current replan preservation logic does not clearly retain those `build:...` progress keys, so a mid-build replan can forget where it was. That is a classic silent-duplication risk.

## What looks solid

The following pieces are genuinely improved and should be preserved:

- Deterministic fast paths for stop/status/help/inventory are in place, which reduces unnecessary LLM traffic and makes common commands more reliable.
- `HtnTaskLibrary.DecomposeBuild` now handles origin resolution, material pre-gather, crafting-table bootstrap, and checkpoint-based resume.
- `WorldStateProjector.ApplyStatus` normalizes namespaced inventory keys, which prevents silent inventory mismatches.
- The Mineflayer adapter emits useful event types such as `blockMined`, `blockNotFound`, `blockPlaced`, `flatAreaFound`, and `status`.
- The codebase already has the right ingredients for richer observation-driven recovery: typed world events, correlation IDs, and a task roadmap that anticipates observation normalization.

## Highest-priority findings

### 1) Build placement still lacks a true verification loop
**Confidence:** 96%

`BlueprintExecutor` emits `PlaceBlock` actions for every block in the blueprint, but the surrounding build pipeline only appends a final `GetStatus`. The projector stores `BlockPlacedEvent` as raw facts, and the adapter emits `blockPlaced`, but there is no visible code path that verifies the placed block actually matches the blueprint after placement or that it remains present later.

Why this matters:
- A placement can succeed locally but still be wrong in the world because of adjacency constraints, later interference, chunk-loading issues, or a mismatched block state.
- A silent miss is worse than a hard failure because it can advance checkpoint state and suppress retries.
- The current “success” signal is effectively “the adapter said it placed something,” not “the blueprint block is confirmed present.”

Evidence:
- `HtnTaskLibrary.DecomposeBuild` adds `PlaceBlock` actions and ends with `GetStatus`, but no explicit block verification stage.
- `WorldStateProjector` treats `BlockPlacedEvent` as fact storage only.
- The Node adapter emits `blockPlaced` after `bot.placeBlock(...)`, but there is no corresponding verification event or post-placement world check.

### 2) Placement correlation is being dropped
**Confidence:** 93%

The Node adapter includes `correlationId` in `blockPlaced`, but the C# `BlockPlacedEvent` record does not carry it, and the bridge/parser drops it. That means the action lifecycle tracker cannot reliably tie a placement confirmation back to the exact dispatched action.

Why this matters:
- Recovery logic cannot tell whether a specific `PlaceBlock` action completed.
- Build resumption becomes fuzzier because completion evidence is not tied to the intended block index.
- Duplicate placements become more likely when a replan happens near the completion boundary.

Evidence:
- The Node adapter sends `blockPlaced` with `correlationId`.
- `WorldEvents.cs` defines `BlockPlacedEvent(int X, int Y, int Z, string Block, ...)` with no correlation field.
- `WorldStateProjector` stores only coordinates/block facts for that event.
- The visible `AgentBackgroundService` event switch completes `MineBlock`, `CraftItem`, `SmeltItem`, `FindFlatArea`, `MoveTo`, and `Wander`, but not `PlaceBlock`.

### 3) Build checkpoint state is vulnerable during replans
**Confidence:** 87%

`HtnPlanner` preserves selected context keys across replans, but the preserved prefixes do not obviously include the build progress keys that `BuildFactKeys` defines for checkpoint resume.

Why this matters:
- A replan in the middle of a build can forget which block index was last placed.
- The next plan may replay already-completed placement actions.
- This is exactly the kind of “looks okay until one retry” bug that produces silent drift in long-running builds.

Evidence:
- `BuildFactKeys.BuildProgressIndex(...)` writes `build:{blueprintId}:progress:index`.
- `HtnPlanner.CreateCreativeBuildActions` attaches progress context keys to each `PlaceBlock`.
- `HtnPlanner.ReplanAsync` only preserves context keys starting with `SearchMemory:`, `CraftItem:`, `FindFlatArea:`, `Build:`, or `MoveTo:`.
- The checkpoint keys begin with `build:` rather than `Build:`.

### 4) Replan error handling is still too opaque
**Confidence:** 82%

`HtnPlanner.ReplanAsync` catches all exceptions and returns `null`. That keeps the system alive, but it also hides planner bugs and makes it hard to distinguish “planner could not recover” from “planner crashed internally.”

Why this matters:
- Silent fallback paths can make a partially broken planner appear to be a legitimate “no plan available” case.
- The operator sees weaker diagnostics than the failure deserves.
- This makes the observation layer harder to trust because failure semantics are blurred.

### 5) The interpreter is still intentionally permissive, which remains brittle
**Confidence:** 70%

`ChatInterpreter` is deterministic and useful, but the regex-based extraction still accepts fairly broad item and blueprint strings. That is workable for a narrow command language, but it remains a source of false positives and malformed goals when chat is noisy or commands are ambiguous.

Why this matters:
- It can create goals from phrases that merely resemble commands.
- It couples the accepted vocabulary to regex shape rather than intent semantics.
- This is exactly the sort of thing the sprint-35 LLM-first handoff is trying to move away from.

## Architectural recommendation

The cleanest next step is to separate **execution** from **verification**.

A practical shape would be:

1. Execute `PlaceBlock`.
2. Capture a placement acknowledgment that includes `correlationId`, blueprint id, block index, expected material, and coordinates.
3. Trigger a follow-up observation/verify step that samples the world state at the target coordinate.
4. Mark the block as verified only when the observed block matches expectation.
5. Only then advance build checkpoint progress.

That can be implemented either as:
- a dedicated `VerifyPlacedBlock` tool, or
- an `ObservePlacement` / `GetBlockAt` observation step consumed by the agent loop.

This fits the existing architecture better than trying to infer success from raw `blockPlaced` alone.

## Recommended code changes

### Minimal fix set
- Add `CorrelationId` to `BlockPlacedEvent` and preserve it in `WebSocketBridge`.
- Add a structured placement-verification event or tool result that compares expected versus observed block state.
- Mark build progress only after verification, not merely after adapter acknowledgment.
- Preserve `build:...` checkpoint facts during replans.

### Better long-term shape
- Introduce a small placement state machine such as `PendingPlaced → Confirmed → Failed → Recovered`.
- Move placement verification into the world-model / observation layer so it becomes reusable for other actions.
- Emit a structured `ActionOutcome` for placement, consistent with the sprint-35 handoff direction.
- Treat placement failures as first-class observation data rather than relying on generic error paths.

## Existing sprint work to avoid duplicating

There is already active planning around related topics, so the new work should not duplicate those items:

- Sprint 35 handoff already tracks runtime bug fixes like `mineComplete`, stop-on-replan, flat-area origin source, and `ActionOutcome` wiring.
- The Phase 6 roadmap already lists observation pipeline normalization as the next step after Sprint 18.
- That means the safest framing for the new work is **build placement verification and recovered progress**, not a rehash of the sprint-35 inventory/auth/story items.

## Assumptions

- I assumed the current repo snapshot on `main` reflects the sprint-35-llm-first merged state, because the handoff docs and code references are on `main` in the repo snapshot I reviewed.
- I assumed the visible `AgentBackgroundService` switch is representative of the placement lifecycle path, because I did not find a visible `BlockPlacedEvent` completion branch in the reviewed section.
- I assumed the desired behavior is to verify placed blocks against blueprint expectations, not merely to confirm the adapter attempted placement.

## Open questions

- Should placement verification happen immediately after each block, or in small batches to reduce overhead?
- Should the authoritative source of truth be `bot.blockAt(...)` from the adapter, or a higher-level world model snapshot?
- Should a failed verification requeue just that one block index, or trigger a broader local replan?
- Should build checkpoints advance on adapter acknowledgment, or only after verified observation?

## Supplemental notes

### Strong evidence points
- `ChatInterpreter` now routes gather/build/craft commands deterministically.
- `HtnTaskLibrary` handles build origin resolution, crafting-table bootstrap, and material pre-gather.
- `BlueprintExecutor` emits ordered placement actions.
- `WebSocketBridge` already understands the world-event vocabulary needed for richer recovery.
- The remaining issue is not lack of events; it is lack of a **verified observation contract**.

### Confidence summary
- Placement verification gap: **96%**
- Correlation loss for placement: **93%**
- Replan checkpoint fragility: **87%**
- Opaque replan failure handling: **82%**
- Regex brittleness in intent parsing: **70%**

## Evidence index

- `Agent.Planning/ChatInterpreter.cs`
- `Agent.Planning/HtnPlanner.cs`
- `Agent.Planning/HtnTaskLibrary.cs`
- `Agent.Construction/BlueprintExecutor.cs`
- `Agent.World.Minecraft/WebSocketBridge.cs`
- `Agent.World.Minecraft/MinecraftAdapter.cs`
- `MineflayerAdapter/index.js`
- `Agent.Core/BuildFactKeys.cs`
- `Agent.Core/WorldStateProjector.cs`
- `Agent.Core/Events/WorldEvents.cs`
- `WebUI.Blazor/AgentBackgroundService.cs`
- `MemorySmith.Agent.Tests/HtnPlannerBuildTests.cs`
- `MemorySmith.Agent.Tests/HtnTaskLibraryExtraTests.cs`
- `MemorySmith.Agent.Tests/WorldStateProjectorTests.cs`
- `Data/Pages/Tasks/agent-handoff-sprint35-llm-first.md`
- `Data/Pages/Tasks/phase6-tasks.md`
