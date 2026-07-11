# Sprint 20 Audit — Runtime Failures, System Message Analysis & Architectural Review

**Date:** 2026-06-18
**Author:** Agent synthesis (Gemini + GPT architectural audit + first-party runtime logs)
**Sprint context:** Post-Sprint-19 hands-on session; pre-Sprint-20 planning
**Repo:** https://github.com/TheMasonX/MemorySmith.Agent

---

## Executive Summary

Two live runtime sessions exposed four critical failure modes that prevented productive bot operation:

1. **Perpetual replan stall loop** — the bot plans and executes 18 actions every 2 seconds indefinitely without game state change.
2. **Stale inventory false-completion** — goals complete instantly after an external inventory clear because the C# WorldState has no freshness gate.
3. **System message LLM leak** — server-generated messages (`Teleported Leo to…`, `Removed N item(s)…`) reach the LLM, wasting tokens and causing erroneous responses.
4. **LLM JSON truncation** — Ollama's `llama3.2:3b` cuts responses mid-JSON, causing parse failures that silently fall through to Unknown intent.

An independent architectural audit (Gemini + GPT) converges on the same root cause: **the planner reasons about actions more strongly than it reasons about progress**.

---

## Session 1 Runtime Observations (pre-restart)

### Leo drowned while health readout showed 20/20

The C# `WorldState.Health` remained at 20 while the bot was submerged and receiving drowning damage. Root cause: the `health` event from mineflayer is fire-and-forget; the C# readout reflects spawn health, not live health. Drowning damage fires health events but the status response (`QueryStatus`) reads `_worldState.Health` which may lag the event pipeline when the chat consumer is behind.

**Also**: bot became completely unresponsive in water. The `NavigateTo` intent from "leo come here" dispatched `MoveTo` then immediately hit `[stop] emergency stop dispatched` because the user said "leo stop" shortly after. Movement was never attempted.

### `Gather:sand` — replan loop every 2 seconds

```
[20:25:31] Goal set: Gather:sand
[20:25:31] New plan for 'Gather:sand': 4 actions.
[20:25:33] New plan for 'Gather:sand': 4 actions.
[20:25:35] New plan for 'Gather:sand': 4 actions.
...
[20:25:40] Inventory +1 sand -> total 1
[20:25:41] Inventory +2 sand -> total 3
[20:25:41] Goal 'Gather:sand' completed.
```

Sand was eventually gathered (real blockMined events arrived). The replan loop was churning because plan actions returned OK in ~0 ms (WebSocket ACK-only, not game-event-confirmed), and the governor's `RecordProgress()` was called on Wander and MineBlock tool-OK — which reset the stagnation counter every cycle, preventing STALL from ever triggering.

### System message `Teleported Leo to TheMasonX23]` reached LLM

```
[20:25:55] [chat] <TheMasonX23> Teleported Leo to TheMasonX23]
[20:25:55] [llm] calling ollama (llama3.2:3b) for <TheMasonX23> 'Teleported Leo to TheMasonX23]'
```

Session 1 was running pre-Sprint-19 code (no SYSTEM_MESSAGE_PATTERNS). Teleport confirmation messages from the Minecraft server are routed with the player's username. The LLM returned null → fell back to Unknown.

### `Build:wall` goal fails to create

```
[20:29:53] [chat] <TheMasonX23> leo build wall
[20:29:53] [chat] <TheMasonX23> -> CreateGoal (Build:wall)
[20:29:54] Chat goal 'Build:wall' could not be created.
```

No `wall` blueprint is registered. Only `small-house` is in `BlueprintRepository`. This is a known gap — only one blueprint exists.

### `Build:small-house` — findFlatArea fails 6 times then loops

```
[20:29:58] New plan for 'Build:small-house': 1 actions.
[20:29:58] [findFlatArea] scan area=0 below minimum 25 — auto-origin not updated
[20:30:00] (same)... × 6 times
[20:30:10] Connection attempt 1 failed. (OperationCanceledException)
```

The bot was standing on terrain that had insufficient flat area (scan returns 0). The plan loop correctly retried with findFlatArea (Sprint 19 Phase 5) but the node adapter crashed/disconnected before an area was found.

---

## Session 2 Runtime Observations (after restart)

### `Build:small-house` — 233-action plan executes in < 1 second

```
[00:31:00] Build origin set for 'auto': (-357,75,74)
[00:31:02] [plan] Build:small-house: 233 actions [SearchMemory → Wander → MineBlock → ... → PlaceBlock × 215 → GetStatus]
[00:31:02] [action] Wander OK (0ms)
[00:31:02] [action] MineBlock OK (0ms)
[00:31:02] [action] PlaceBlock OK (0ms)  ← × 215 times
```

**Root cause:** The tool dispatcher sends commands to the Mineflayer WebSocket adapter and returns as soon as the protocol ACK is received, not when the game action completes. Wander, MineBlock, and PlaceBlock are fire-and-forget from the C# perspective. No `blockMined` or `blockPlaced` events are emitted for these phantom executions.

The plan "succeeds" (all tools return OK) but the `BuildGoal.IsComplete` check fails (blocks not actually placed) → triggers replan.

### Replan loop — governor never fires STALL

```
[00:31:04] [plan] Build:small-house: 18 actions [SearchMemory → Wander → MineBlock → × 3 → CraftItem × 7 → SearchMemory → MoveTo → GetStatus]
[00:31:06] (same 18-action plan)
[00:31:08] (same 18-action plan)
...continues every 2 seconds for 40 seconds...
[00:31:41] Goal cancelled by user
```

**Root cause:** The Sprint 19 ReplanGovernor tracks plan fingerprints and resets the stagnation counter via `RecordProgress()` whenever a progress-signal tool (including Wander) returns OK. Because Wander returns 0ms OK on every plan cycle, `RecordProgress()` is called every cycle, resetting the counter to 0. STALL threshold of 3 is never reached.

**Compound issue:** The governor calls `Evaluate(fingerprint)` AFTER `PlanAsync` returns, not before. During STALL, the loop calls `continue` (skips enqueueing) but immediately re-enters the planning block, calling `PlanAsync` again. This means even if STALL triggered, the planner would still be called repeatedly at 2-second intervals.

### Stale inventory false-completion

```
[00:44:55] [goal] set: Gather:dirt — Gather at least 5 Dirt. | inventory: [12x dirt]
[00:44:56] [goal] completed: Gather:dirt | inventory: [12x dirt]
NOTE: THE AGENT HAD NO ITEMS (admin had cleared inventory)
```

`GenericGatherGoal.IsComplete` checks `_worldState.Inventory[dirt] >= 5`. The WorldState still showed 12 dirt from the previous gather session, even though the items were removed via an admin command. No fresh `status` event had been received since the clear.

The bot also reported "I have 12 dirt blocks in my inventory" over chat, misleading the user. Health showed 20/20 while the user was observing different gameplay state.

### System message `Removed 13 item(s) from player Leo]` reached LLM

```
[00:44:45] [chat] <TheMasonX23> Removed 13 item(s) from player Leo]
[00:44:45] [llm] calling ollama (llama3.2:3b) for <TheMasonX23> 'Removed 13 item(s) from player Leo]'
```

Sprint 19 SYSTEM_MESSAGE_PATTERNS covers teleport/join/leave/server-prefix/time/gamemode/kill/give/own-gamemode. It does NOT cover `/clear` response messages (`Removed N item(s) from player X`). The pattern for `/give` (`/^Gave\s+\d+\s+/i`) also doesn't cover `/clear`.

### LLM JSON truncation causing repeated parse failures

```
[00:40:27] [llm] failed to parse JSON from ollama response: '{
  "addressed": "yes",
  "intent": "maybe",
  "response": "Still checking my surroundings..."'
```

The LLM output is truncated mid-string. The `response` field is cut off before the closing `"`. This is a `num_predict` (max_tokens) cap from Ollama. The JSON parser correctly rejects the malformed input but falls back to Unknown, which causes the bot to say "Didn't catch that" for valid conversational messages.

Observed in session 2 for: spatial statements ("you're on land"), open-ended questions ("what tools do you have?"), confirmations ("Yeah, you're on land"), and status questions about the bot's own items.

### `gather 5 grass` fails to create goal

```
[00:33:43] Chat goal 'GatherItem:grass' could not be created.
```

"grass" is not in the `ItemAliases` dictionary in `ChatInterpreter`. The `ResolveItemId` function falls through to the raw-id check (`/^[a-z][a-z0-9_]*$/`) which would pass `grass`, but `IItemRegistry.GetAsync("grass")` returns null because `grass` is not a harvestable item (grass_block gives seeds/grass_block, not "grass"). This is a game-semantics gap — "grass" as a collectible is only obtainable via silk touch and would need different handling.

---

## External Architectural Audit Summary (Gemini + GPT, June 2026)

The independent audit reviewed the full architecture and identified 10 core findings. Key points relevant to Sprint 20:

### Finding 1: Replanning governance is the largest architectural gap

> "The planner currently reasons about actions more strongly than it reasons about progress."

The replanning loop generates new plans whenever the queue empties and the goal is not complete. Without evidence that previous plan attempts produced real-world change, identical plans are generated indefinitely.

Recommended: a **GoalRun** object tracking `PlanHistory`, `ProgressHistory`, and `StagnationCounter`. Replanning should require evidence (inventory delta, position delta, terrain change, search space expansion).

### Finding 2: Quantity is parsed but not propagated into decomposition

Plans for `get 100 sand` and `get 1 sand` produce identical 4-action plans. The count parameter from `GatherItem:sand:100` reaches `GoalFactory.CreateAsync` and sets `GenericGatherGoal.TargetCount`, but the planner's `GatherItemDecompose` generates the same fixed action template regardless of remaining quantity. Cluster harvesting is absent.

### Finding 3: Item resolution needs a formal resource graph

The stone → cobblestone discrepancy is partially fixed (Sprint 17) but a comprehensive directed acquisition graph is absent. Aliases work for the happy path; they break for smelt-chain items, enchantment-dependent drops, and biome-specific resources.

### Finding 4: Inventory causality is incomplete

The system tracks inventory deltas (blockMined events → `AddInventoryItem`) but does not track WHY inventory changed. Future planners need `{ItemPickedUp, ItemCrafted, ItemMined, ItemDropped, ItemConsumed}` with location context.

### Finding 5: Cluster harvesting is missing

Observed sand behavior: mine a block, wander to a new location, mine a block, wander. No flood-fill cluster detection. The planner treats each resource acquisition as independent, leading to inefficient scattered harvesting.

### Finding 6: Recovery behaviors are not first-class

Water stalls, path failures, inventory-full states, and tool-missing states all fall through to generic "replan." A recovery state machine (WaterRecovery, PathRetry, InventoryDump, ToolCraft) is absent.

### Finding 7: System events and chat events need separation

Server-generated messages (teleport, join, leave, admin commands) should be classified as `SystemEvent` before reaching the LLM pipeline. This requires both JS-layer pattern filtering and C#-layer intent classification guards.

### Finding 8: Bot fences must be deterministic

Safety boundaries (stay within N blocks of spawn, don't mine protected areas) cannot be prompt-driven. A `SafetyGuard` layer between planner and executor is needed.

### Finding 9: Build site selection needs governance

When `findFlatArea` returns area=0, the current retry (expanded radius) works once but has no multi-attempt scoring, candidate persistence, or relocation fallback.

### Finding 10: MemorySmith should become the world model authority

Currently MemorySmith is a lookup source. Long-term it should store resource deposits, known structures, danger zones, and exploration history so planning is informed rather than reactive.

### Audit strategic roadmap

| Phase | Priority |
|-------|----------|
| 1 — Correctness | Quantity propagation, stone resolution, inventory causality, event classification |
| 2 — Reliability | Recovery controller, stagnation detection, replan governance, cluster harvesting |
| 3 — Safety | Bot fences, protected structures, action guardrails |
| 4 — World Intelligence | Persistent resource memory, terrain knowledge, world-model planning |
| 5 — Autonomous Ops | Long-horizon goals, multi-step acquisition chains, resource logistics |

---

## Root Cause Analysis

### RC-1: Governor RecordProgress called on tool-OK, not on game-event-confirmed

The Sprint 19 ReplanGovernor tracks fingerprints and resets stagnation when `RecordProgress()` is called. `RecordProgress()` is called after any progress-signal tool (including Wander) returns OK. But "OK" means "WebSocket ACK received" — not "game action completed." Wander returns OK in 0ms without the bot moving. This resets the stagnation counter every cycle.

**Fix:** Track progress via world-state snapshots (inventory sum, position) before and after a plan cycle completes, not via per-tool success callbacks.

### RC-2: Governor check is post-PlanAsync, loop spins during STALL

`Evaluate(fingerprint)` is called after `PlanAsync` completes. During STALL, `continue` skips enqueueing but immediately re-enters the while loop, which calls `PlanAsync` again (2-second cycle, expensive). Even if STALL triggered, the planner would be called hundreds of times.

**Fix:** Check `governor.CanPlan(worldState)` BEFORE calling `PlanAsync`. During STALL, delay (e.g., 10s intervals) instead of tight-looping.

### RC-3: No freshness gate on WorldState inventory

GoalFactory injects a `GenericGatherGoal` whose `IsComplete` checks `_worldState.Inventory[item] >= count`. The WorldState is updated by blockMined/craftComplete events. External inventory modifications (admin `/clear`, `/give`) produce no event, leaving the C# model stale indefinitely.

**Fix:** Inject a `GetStatus` action as the first queued action when `SetGoal` is called. This ensures `_worldState.Inventory` is fresh before the first `IsComplete` check.

### RC-4: SYSTEM_MESSAGE_PATTERNS gap — `/clear` responses not covered

The Sprint 19 filter covers teleport/join/leave/server-prefix/time-set/gamemode/kill/give/own-gamemode. Missing: `/clear` response (`Removed N item(s) from player X`), `/tp` response variant with `]` suffix, and general server-action confirmations.

**Fix:** Add 4 more patterns covering clear, give-to variants, and bracket-terminated server messages.

### RC-5: LLM max_tokens too low for JSON response

`llama3.2:3b` via Ollama is hitting token limits mid-response. The `response` field in the JSON is being truncated. The JSON parser rejects partial JSON → falls to Unknown → bot ignores legitimate messages.

**Fix:** Add `"num_predict": 256` to Ollama request options. Alternatively, implement a partial-JSON salvager that extracts `addressed`, `intent`, and partial `response` even from truncated output.

---

## Sprint 20 Priorities

### P0-A: Progress-hash governor (replaces fingerprint-only governor)

Replace `RecordProgress()` (per-tool callback) with `RecordCycleComplete(beforeState, afterState, fingerprint)` called after each plan cycle's 300ms settle. Progress = `sum(inventory.values)` changed or position changed. Identical progress hash + identical fingerprint for N cycles → STALL.

Add `CanPlan(WorldState)` called BEFORE `PlanAsync`. During STALL, delay 10s before next planning attempt (do NOT tight-loop through PlanAsync).

Acceptance criteria:
- Build:small-house stall detected after 3 cycles with no inventory change
- Gather:dirt stall NOT triggered when inventory is actively increasing
- STALL log line emitted with cycle count and fingerprint

### P0-B: GetStatus injection on SetGoal

When `SetGoal` is called, enqueue a `GetStatus` action BEFORE any planned actions. This ensures `_worldState.Inventory` is fresh before the first `IsComplete` check.

Acceptance criteria:
- After admin `/clear`, new gather goal does NOT complete immediately
- After admin `/clear`, bot gathers actual items and completes with correct count

### P0-C: SYSTEM_MESSAGE_PATTERNS expansion

Add patterns:
- `/^Removed\s+\d+\s+item/i` — /clear response
- `/^Cleared\s+\d+/i` — /clear alternative format
- `/^Gave\s+\S+\s+\d+\s+/i` — /give variant
- `/]\s*$/` guard for bracket-terminated server messages (secondary heuristic)

Acceptance criteria:
- `Removed 13 item(s) from player Leo]` → filtered (no LLM call)
- `Teleported Leo to TheMasonX23]` → filtered (already handled; regression test)
- `Leo, gather 5 dirt` → NOT filtered (real player command reaches LLM)

### P0-D: Ollama num_predict / partial JSON

Add `options.num_predict = 256` to the Ollama API request body in `OllamaLlmProvider` (or equivalent). Add a partial-JSON recovery path in `ParseDecision`: if the string ends with an incomplete string literal, try stripping the incomplete field and parsing what's there.

Acceptance criteria:
- JSON parse failures reduced for messages under 200 chars
- `"response"` field correctly captured for 50-word responses

### P1: Governor 4-state FSM (deferred from Sprint 19)

Expand to: ACTIVE → STAGNATING → STALLED → RECOVERING. STAGNATING emits a warning log but continues planning. STALLED blocks planning. RECOVERING allows one replan, promotes to STAGNATING on failure.

### P1: Cluster quantity awareness

`GatherItemDecompose` should emit multiple MineBlock actions based on remaining need (`targetCount - currentInventory[item]`), not a fixed template. For counts > 5, emit block-count targeted MineBlock args.

### P2: Water/swim recovery

Detect `_worldState.Position.Y < sea_level_threshold` and `bot.isInWater` fact. Emit a swim-escape action (move to shore) before resuming goal. Add to HtnTaskLibrary as `RecoverFromWater` decomposer.

### P2: NavigateTo uses playerPos from chat event

"Come here" intent currently creates `MoveTo` with `target: player` (no coordinates). Wire `playerPos` from the chat event (available in `HandleChatEventAsync` via `ExtractPlayerPosition`) into the `MoveTo` arguments.

---

## Testable Acceptance Criteria (Sprint 20 Summary)

| # | Test | Pass condition |
|---|------|---------------|
| T1 | Governor stall — no inventory change | STALL fires after 3 identical cycles |
| T2 | Governor no stall — active gathering | Counter resets when inventory increases |
| T3 | Inventory freshness on SetGoal | GetStatus enqueued as first action |
| T4 | Stale inventory false-complete blocked | Goal gathers after admin /clear |
| T5 | System message filter — /clear | No LLM call on "Removed N item(s)..." |
| T6 | System message filter — no false drop | "leo gather 5 dirt" reaches pattern matcher |
| T7 | LLM JSON truncation recovery | Partial JSON extracts addressed+intent |
| T8 | Governor CanPlan — blocks PlanAsync | No PlanAsync called during STALL period |

---

*This document was synthesized from: (1) runtime session logs 2026-06-18, (2) Gemini+GPT architectural audit June 2026, (3) Sprint 19 council review findings D-2/D-3/D-7, (4) codebase analysis at commit d3c7e4c3.*
