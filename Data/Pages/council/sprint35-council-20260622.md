# Sprint 35 Council Review — 2026-06-22

**Topic:** Pre-implementation audit synthesis: internal line-level audit + external architecture review  
**Branch:** main @ 4de226b7  
**Council format:** 6-seat, per-seat confidence, blocking/deferred triage, testable acceptance criteria

---

## Context

Two independent audits were conducted before any Sprint 35 code was written:

1. **Internal audit** — line-level evidence from reading index.js, WorldStateProjector.cs, WebSocketBridge.cs, ChatInterpreter.cs, LlmChatInterpreter.cs, GoalFactory.cs, and directory listings of every module (commit 4de226b7).
2. **External audit** — architectural review uploaded as a wiki page, cross-referencing the same files with broader design-pattern analysis.

The two audits converge on four concrete bugs and one architectural inversion. This council reviews the synthesis and approves or blocks the Sprint 35 plan.

---

## Confirmed bug findings (evidence-grounded)

### BUG-1: Inventory only updates from spawn + explicit GetStatus

**Evidence:** `index.js` calls `sendBotStatus()` in exactly two locations: `bot.once('spawn', ...)` and `case 'status':`. After `craft`, `smelt`, or `place` actions complete, no inventory event is emitted. `WorldStateProjector.ApplyBlockMined` adds `e.Block` (the mined block name, not the drop) to inventory. For items where block ≠ drop (diamond_ore → diamond, iron_ore → raw_iron, stone → cobblestone), inventory counts drift from reality immediately.

**External audit reinforcement:** External audit independently identifies this as the "clearest concrete bug class" and recommends treating inventory events as authoritative drops via `playerCollect`, not by inferring from mined block names.

### BUG-2: Mining has no completion event; replan doesn't stop JS

**Evidence:** The JS `mine` case loops until `mined >= count`, then the dispatch function ends with no `sendEvent('mineComplete', ...)`. C# infers completion by checking `goal.IsComplete(state)` after each `blockMined` — this works when inventory is fresh but silently fails when stale. When `ActionQueue.ClearAndEnqueue` fires during a stall, no `stop` command is sent to JS; the JS cmdQueue drains the current mine action before the new plan actions run, creating a window where C# has moved on but JS is still executing the old action.

**External audit reinforcement:** Identifies the same "fire-and-forget tool dispatch" pattern and recommends richer execution observations.

### BUG-3: FlatAreaFoundEvent missing SearchedRadius; build origin is silently forgiving

**Evidence:** JS emits `searchedRadius: r` in the failure `flatAreaFound` event (area=0). `WebSocketBridge.ParseEvent` for `flatAreaFound` does not parse this field. `FlatAreaFoundEvent` record has no `SearchedRadius` property. `DecomposeBuild` retry-with-radius=48 logic is gated on `Area == 0` but cannot distinguish "searched radius 32 and found nothing" from "searched radius 48 and found nothing."

**External audit reinforcement:** Escalates the issue — build origin should be treated as explicitly required with a visible confirmation boundary, not a silent fallback. Auto-origin is a design smell because build origin is the spatial anchor for the entire goal.

### BUG-4: LLM is bypassed for all regex-recognized commands (architectural inversion)

**Evidence:** `LlmChatInterpreter.InterpretAsync` step 4 is an early return: `if (quick.IntentType is CreateGoal or CancelGoal or QueryHelp or QueryStatus or NavigateTo) return quick`. Since `ChatInterpreter` handles gather/craft/build/goTo deterministically, the LLM is only called for `Unknown` — i.e., things the regex can't parse at all. The regex covers the common cases, so the LLM rarely fires in normal operation.

**External audit reinforcement:** Names this "split-brain parser" and "the accumulation of many small helpful heuristics." Recommends a canonical `IntentDraft` schema produced by the LLM for all non-trivial inputs, with deterministic routes only for stop/cancel/status/help.

---

## Seat analysis

### Seat 1: Source-Grounded Archivist
*Confidence: 0.95*

All four bugs are confirmed by reading the actual source. BUG-1 is the most dangerous because it's silent — the bot appears to be tracking inventory while it's actually accumulating drift on every mine/craft/smelt cycle. BUG-3's `SearchedRadius` omission is a clean, targeted fix. BUG-4 is architectural and the most impactful to correct.

**No blocking objections to the sprint scope.**

### Seat 2: Data Model Architect
*Confidence: 0.88*

The external audit correctly identifies `WorldStateProjector` as the right canonical reducer, but notes that `AgentBackgroundService` is also acting as a second reducer (mutates inventory stale, resets health tracking, adds progress facts). The 80KB file size is a real signal. The sprint plan addresses inventory truth but does not tackle the AgentBackgroundService split — this is appropriate for Sprint 35 scope; defer the split to Sprint 36.

**BLOCKING (BLK-S35-01):** `IntentDraft` schema must include a `confidence` field and a `clarificationQuestion` field. Without confidence, the system cannot distinguish a low-confidence intent from a high-confidence one, and cannot decide when to confirm rather than execute. The current LlmChatInterpreter JSON prompt has no confidence field. This must be in the P1 design before any P1 code is written.

### Seat 3: Retrieval Specialist
*Confidence: 0.82*

The external audit's recommendation to pass inventory delta history (not just current counts) to the LLM is correct and important. The LLM can reason about trends ("you had 8 oak_log five actions ago and still have 8 — something is wrong") in ways that a snapshot cannot. This is not P0 but should be P1-C context enrichment.

`TryRecoverFromGameErrorAsync` exists in `AgentBackgroundService.cs` (80KB file, not read in detail). Before generalizing the recovery pattern into a "recovery policy layer" (P2), that method should be read and its pattern either preserved or superseded explicitly.

**DEFERRED:** Read `TryRecoverFromGameErrorAsync` before Sprint 36 recovery policy work.

### Seat 4: Human Learning Advocate
*Confidence: 0.91*

The user's framing — "LLM should have a strong deterministic toolset, but ultimately plans at a high level, with tools exposing data and handling grunt work" — is exactly the right contract. The external audit's three-layer model (LLM as orchestrator, tools as executors, projector as reducer) directly matches it.

One concern: the `clarificationQuestion` the LLM can generate in the `IntentDraft` must actually be surfaced to the player via in-game chat, not silently discarded. The current bot.chat path exists; it must be wired to the clarification output.

**DEFERRED (D-S35-01):** Wire `IntentDraft.clarificationQuestion` → bot.chat response in AgentBackgroundService.

### Seat 5: Skeptical Reviewer
*Confidence: 0.79*

BUG-2's stop-on-replan hypothesis is plausible but unverified — `AgentBackgroundService.cs` was not read in full (80KB). The sprint plan should verify that `ClearAndEnqueue` is the right injection point before coding P0-D. Specifically: confirm whether the existing correlation sweep in `StopAsync` already handles this case.

**BLOCKING (BLK-S35-02):** P0-D (stop-on-replan) requires reading the relevant section of `AgentBackgroundService.cs` before implementation to confirm the injection point. Do not code P0-D based only on the hypothesis from smaller files.

The external audit's inventory `playerCollect` recommendation is correct in principle, but Mineflayer's `playerCollect` event fires for ALL nearby players collecting items, not just the bot. The bot filter must be `event.collector.username === bot.username`. This is a one-line guard but must not be omitted.

### Seat 6: Synthesizer
*Confidence: 0.90*

**Verdict: APPROVED with two blocking findings (BLK-S35-01, BLK-S35-02).**

The sprint scope is appropriate. P0 addresses the four confirmed runtime bugs. P1 addresses the architectural inversion. P2 lays groundwork without overcommitting. The two audits agree on all major findings and the external audit adds useful nuance (IntentDraft confidence, playerCollect filter, TryRecoverFromGameErrorAsync). Both blockers are resolvable within the session before coding begins.

---

## Approved Sprint 35 plan

### P0 — Stop semantic drift (implement first, verify before P1)

**P0-A: Inventory truth via playerCollect + post-action snapshot**
- In `index.js`: hook `bot.on('playerCollect', (collector, entity) => ...)` — guard `collector.username === bot.username` — emit `itemCollected` event with `{item: entity.metadata?.name ?? entity.displayName, count: entity.count ?? 1}`
- Also call `sendBotStatus()` after every `craft` and `smelt` completion to reconcile
- In `WebSocketBridge.cs`: parse new `itemCollected` event → `ItemCollectedEvent` record → `WorldStateProjector.ApplyItemCollected` adds to inventory by actual drop name
- Acceptance: "mine diamond_ore" → WorldState.Inventory["diamond"] increments, not "diamond_ore"

**P0-B: mineComplete event**
- In `index.js` `mine` case: after while loop exits, emit `sendEvent('mineComplete', { block: shortName, mined, targetCount: count, correlationId })`
- In `WebSocketBridge.cs`: parse `mineComplete` → `MineCompleteEvent`; in AgentBackgroundService correlation tracking, transition correlated action to Completed on this event
- Acceptance: C# correlation lifecycle shows Dispatched → Completed for mine actions

**P0-C: FlatAreaFoundEvent.SearchedRadius + build origin policy**
- Add `SearchedRadius` field to `FlatAreaFoundEvent` record; parse `searchedRadius` in WebSocketBridge
- In `DecomposeBuild`: gate retry on `SearchedRadius < 48` (not just `Area == 0`)
- Mark `BuildGoal.OriginSource` as enum: `Explicit` (from chat coordinates), `PlayerPosition` (from player location at command time), `AutoScanned` (from FindFlatArea result)
- When `OriginSource == AutoScanned`, log a visible warning before beginning construction
- Acceptance: "build a house" → OriginSource=AutoScanned + warning; "build at 100 64 200" → OriginSource=Explicit

**P0-D: Stop-on-replan (AFTER reading relevant AgentBackgroundService section — BLK-S35-02)**
- Confirm injection point: when `ClearAndEnqueue` is called while `_correlatedActions` has Dispatched entries, send `stop` action via bridge
- Acceptance: replan during active mine → bot stops movement; new plan starts clean

### P1 — LLM-first architecture

**P1-A: IntentDraft record (implements BLK-S35-01)**
```csharp
record IntentDraft(
    string Addressed,
    string Intent,
    string? Item,
    string? Blueprint,
    int? Count,
    int? X, int? Y, int? Z,
    double Confidence,            // NEW — required per BLK-S35-01
    string? ClarificationQuestion, // NEW — non-null when Confidence < threshold
    string Response
);
```
Confidence threshold: 0.6 (configurable). Below threshold → send ClarificationQuestion via bot.chat, return Unknown.

**P1-B: Flip LlmChatInterpreter pipeline**
- Remove `CreateGoal | CancelGoal | QueryHelp | QueryStatus | NavigateTo` early-return fast-path
- Keep ONLY: stop/cancel/abort, status/report, inventory/items, help/commands
- Everything else → LLM → IntentDraft

**P1-C: Context enrichment**
- System prompt receives: inventory snapshot, position + HP + food, active goal, last tool error, available tool names

**P1-D: Delete GatherRegex, BuildRegex, CraftRegex as primary path**
- Move alias dictionaries to static `KnownAliases` class (reference data, not parsing logic)

### P2 — Cleanup

- **P2-A:** Read and document `TryRecoverFromGameErrorAsync` in AGENTS.md
- **P2-B:** Add copper, deepslate variants, raw_iron/gold to `CommonMinecraftBlocks`
- **P2-C:** AGENTS.md update: three-layer ownership model, IntentDraft schema, playerCollect guard

---

## Blocking findings summary

| ID | Severity | Description | Resolution |
|---|---|---|---|
| BLK-S35-01 | Blocking | IntentDraft must include `confidence` + `clarificationQuestion` | Add to P1-A schema before coding |
| BLK-S35-02 | Blocking | P0-D requires reading AgentBackgroundService.cs first | Read before coding P0-D |

---

## Deferred findings

| ID | Sprint | Description |
|---|---|---|
| D-S35-01 | 35 (P1) | Wire clarificationQuestion → bot.chat in event loop |
| D-S35-02 | 36 | Read and document TryRecoverFromGameErrorAsync |
| D-S35-03 | 36 | Recovery policy layer (failure classification) |
| D-S35-04 | 36 | AgentBackgroundService split (80KB god file) |
| D-S35-05 | 36 | Inventory delta history in LLM context |
| D-S35-06 | 37 | Richer adapter observations (reachability, face selection) |

---

## Testable acceptance criteria

1. Mine 5 diamond_ore → `WorldState.Inventory["diamond"] == 5`, not `"diamond_ore"`
2. Craft iron_pickaxe → `sendBotStatus()` fires → inventory updates within same action cycle
3. Mine 10 oak_log → C# receives `mineComplete` event with `mined=10`
4. "build a house" → BuildGoal.OriginSource = AutoScanned, LogWarning visible
5. "build a house at 100 64 200" → BuildGoal.OriginSource = Explicit, no warning
6. FindFlatArea with searchedRadius=48, area=0 → no retry attempted
7. "leo I need some wood" → IntentDraft{Intent="gather", Item="oak_log", Confidence > 0.8}
8. Ambiguous request with Confidence < 0.6 → bot asks clarification in chat
9. Replan during active mine → `stop` sent to JS before new actions
10. All existing tests still pass (276+ passing, 0 failed)
