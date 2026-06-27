# MemorySmith.Agent Sprint-35 Branch Follow-up Audit

**Snapshot reviewed:** `sprint-35-llm-first`  
**Compared against:** current `main` in the connector view  
**Generated:** 2026-06-23 16:44 UTC

## Executive summary

The sprint branch is materially ahead of the current `main` snapshot in the connector, and it already contains several of the fixes that were still missing in the earlier review: `itemCollected`, `mineComplete`, `blockPlaceSkipped`, action correlation plumbing, and the checkpoint advancement fix that waits for `BlockPlacedEvent` rather than dispatch-time success. The branch also has the new LLM-first intent pipeline in place with `IntentDraft`, `IntentManager`, and `ActionOutcome`. fileciteturn44file0turn46file0turn57file0turn28file0turn29file0turn52file0turn53file0turn54file0

The important remaining gap is no longer “did the place command fire?” It is “did the world end up with the right block, and does it stay that way?” The branch still does not model block validity as durable state; `WorldStateProjector` continues to store `BlockPlaced`/`BlockPlaceSkipped` as raw facts only, so the system can acknowledge placement but still lacks a first-class integrity check for later breakage, terrain interference, or partial build drift. fileciteturn56file0

Two integration mismatches stand out as concrete follow-up bugs. First, `IntentManager.BuildGoalRequest` has a typo in its origin-null guard: it checks `OriginZ` twice and never checks `OriginY`, so malformed build requests can leak through with a missing Y coordinate. Second, the LLM prompt and the goal mapper disagree on navigation: the prompt tells the model to set navigate coordinates to null and let the system use the player’s current position, but `IntentManager` only constructs `NavigateGoalRequest` when X/Y/Z are all present. That means a prompt-following LLM response can silently fail to produce a goal. fileciteturn53file0turn26file0

## Current status versus earlier handoff

The earlier sprint handoff identified placement checkpointing, terrain occupancy, and inventory truth as problems to solve. In this branch, the occupancy and checkpoint items are already addressed in the implementation and task records: the adapter now emits `blockPlaceSkipped` on terrain collisions, `blockPlaced` on success, and the checkpoint task is marked done. The move-tolerance tweak before placement is also already completed. That means the next sprint should not rework those items unless it is specifically adding verification or recovery semantics on top of them. fileciteturn57file0turn28file0turn29file0turn52file0

The branch also already contains the LLM-first structural shift from the handoff: `LlmChatInterpreter` returns `IntentDraft?`, parses confidence and clarification fields, and delegates goal mapping to `IntentManager`. `ToolDispatcher` now produces `ActionOutcome` alongside `ToolResult`, which gives you a cleaner place to hang observation-driven replanning later. fileciteturn26file0turn27file0turn54file0

## New findings

### 1) Build integrity is still only partially verified
**Confidence: 94%**

The branch now knows when a placement was acknowledged and when a collision caused a skipped placement, but there is still no durable “blueprint footprint is correct” model. `BlueprintExecutor` still just emits ordered `PlaceBlock` actions, and `WorldStateProjector` still treats `BlockPlaced` as fact storage rather than as verified world truth. That is good enough for dispatch tracking, but not for “has the structure stayed valid after the build?” fileciteturn62file0turn56file0

What the next sprint should add is a verification layer that checks the world at the target coordinates after placement and again later if the build is relevant. In practice, that means a block placement should move through something like `Placed → Verified → Stable`, instead of stopping at `Placed`. A later `BlockMined`, `BlockPlaceSkipped`, or periodic scan should be able to invalidate that state. This is the piece that will catch “it was placed, then broken” cases. fileciteturn56file0turn57file0

### 2) The intent-to-goal bridge can silently reject valid navigation commands
**Confidence: 90%**

The navigation path is internally inconsistent. The prompt says to leave coordinates null for `navigate`, but `IntentManager` only creates a `NavigateGoalRequest` when X, Y, and Z are present. If the LLM follows the prompt literally, the request becomes `null` and the agent may do nothing. That is a real failure mode, not a style issue. fileciteturn26file0turn53file0

The next sprint should choose one contract and enforce it in both places. Either navigation intent should always carry explicit coordinates, or the runtime should resolve the current player position when the coordinates are missing. Right now the prompt and mapper are speaking different languages. fileciteturn26file0turn53file0

### 3) Build origin handling has a concrete bug
**Confidence: 97%**

`BuildGoalRequest.Parameters` has a copy-paste mistake: it checks `OriginZ` twice and never checks `OriginY`. That makes malformed build requests too easy to construct and can feed partial data forward into the goal pipeline. Fixing this is a low-risk, high-value cleanup item for the next sprint. fileciteturn53file0

### 4) The branch has a clear backlog for terrain and reach issues
**Confidence: 98%**

The branch already recognizes that some builds need scaffolding and terrain clearance. Those are not bugs to rediscover; they are already parked as backlog tasks `TSK-0077` and `TSK-0078`. `TSK-0077` calls for scaffolding or graceful skipping for unreachable roof/upper-wall blocks, and `TSK-0078` calls for a pre-build terrain survey and clearance pass. The new sprint should not duplicate those items; it should build on them. fileciteturn60file0turn61file0

## Guidance for the next sprint

The next sprint should focus on **integrity, not just acknowledgment**.

A practical implementation shape would be:

1. Keep the current placement acknowledgment flow (`blockPlaced` / `blockPlaceSkipped`) and the checkpoint fix.
2. Add a per-blueprint footprint tracker that records expected block material, coordinates, and verification status.
3. Verify each placed block against the world after placement, not just against the adapter’s success path.
4. Revalidate important structures on later world updates so broken blocks are detected.
5. Promote a failed verification into an explicit recovery signal that can trigger either a retry or a local replan.
6. Fix `BuildGoalRequest` origin validation and make navigation coordinate handling consistent between prompt and mapper.
7. Leave the existing occupancy/checkpoint tasks alone unless the sprint is extending them with validation semantics. fileciteturn54file0turn56file0turn57file0

## Suggested next-sprint task set

Keep the next sprint narrowly scoped around these items:

- Fix `BuildGoalRequest` origin validation typo.
- Resolve the navigation coordinate contract mismatch.
- Introduce placement verification state for blueprint footprints.
- Add a revalidation path for later block breakage.
- Add tests for “placed, then broken,” “skip occupied position,” and “navigate with missing coordinates.”

That gives the next agent a clean target without duplicating the already-completed placement checkpoint and occupancy fixes. fileciteturn53file0turn26file0turn57file0turn28file0turn29file0turn52file0

## Open questions

- Should verification happen immediately after every placed block, or batched per phase?
- Should a later invalidation trigger a single-block retry or a local replan of the surrounding footprint?
- Should navigation resolve to player position in the runtime, or should the prompt always emit explicit coordinates?
- Should the footprint tracker live in `WorldState`, in `AgentBackgroundService`, or in the planner’s world-model layer?

## Confidence summary

- Build integrity still lacks a durable verification loop: **94%**
- Navigation prompt/mapper mismatch is a real bug: **90%**
- `BuildGoalRequest` origin check typo is real: **97%**
- Scaffold/terrain work is already backlog, not a duplicate: **98%**
