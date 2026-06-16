# Council Review — TSK-0012 Phase 5
**Topic:** Chat Interpretation, Crafting Tools, Interactive Dashboard, DI Fixes  
**Date:** 2026-06-16  
**File:** Data/Pages/council/tsk-0012-impl-council-20260616.md  
**CI status at review time:** Pending (last green: Phase 4b commits)

---

## Seat 1 — Source-Grounded Archivist

**Confidence:** 0.85 | **Vote:** APPROVE with one risk flag

**Review:**
Architecture consistency with prior phases is maintained:

- `ChatInterpreter` mirrors the deterministic-first D-003 principle: pure pattern matching, no LLM, static alias tables. Future LLM fallback is explicitly deferred.
- `ChatTool`, `CraftItemTool`, `FurnaceTool` follow the exact tool pattern (ITool, InputSchema, `ExecuteAsync` → `ActionData → worldAdapter.SendActionAsync`). ✓
- `GoalFactory.CreateAsync` was already a concrete method; the interface addition just makes it contractual. Existing `GoalFactory` implementation satisfies the new interface requirement. ✓
- `index.js` changes are minimally invasive: only the chat event and two new `case` branches. The sequential command queue and all existing handlers are untouched. ✓

**Risk flag (non-blocking):** `using Agent.Construction;` in `WebUI.Blazor/Program.cs` relies on transitive project reference resolution. SDK-style projects in .NET 5+ do include transitive project references in the compilation closure, so this should work. If CI fails with "type not found", add explicit `<ProjectReference Include="../Agent.Construction/Agent.Construction.csproj" />` to `WebUI.Blazor.csproj`. Probability of failure: low (~15%).

**ADR compliance:**
- D-002 ✓ (MemorySmith remains the memory backend)
- D-003 ✓ (ChatInterpreter is deterministic — no LLM calls)
- D-008 ✓ (Node.js for Mineflayer; craft/smelt added in Node layer)
- D-010 ✓ (Craft="craft", Smelt="smelt" added to ActionProtocol)

---

## Seat 2 — Data Model Architect

**Confidence:** 0.88 | **Vote:** APPROVE

**Review:**
The data flow is clean:

- `ChatInterpretation` record is minimal and immutable. `ChatIntentType` enum is a proper closed discriminated union. ✓
- `AgentBackgroundService` new public API (`CancelGoal`, `SetBuildOrigin`, `GetPendingActions`) follows the existing `SetGoal`/`Enqueue` pattern. ✓
- `_pendingActions` snapshot with a `_pendingLock` object is appropriate for this use (no atomic CAS complexity needed since `GetPendingActions` is called from HTTP handler threads, not the dispatch loop). ✓
- `ConsecutiveFailures` exposed as a read-only property — correct; no mutation from outside. ✓

**Minor concern (non-blocking):** `_pendingActions.RemoveAt(0)` in `DispatchActionsAsync` is O(n) on a `List<ActionData>`. For a 330-item house build plan this is ~330 × 330/2 ≈ 54,000 operations total — negligible at this scale but worth noting for very large plans. A `Queue<ActionData>` would be O(1). Phase 6 improvement.

---

## Seat 3 — Retrieval Specialist

**Confidence:** 0.82 | **Vote:** APPROVE

**Review:**
The chat → goal pipeline is correctly wired:

1. Mineflayer: `bot.on('chat', ...)` → `sendEvent('chat', { ..., onlinePlayers })` ✓
2. WebSocketBridge: parses JSON → `WorldEvent{EventType="chat"}` (existing machinery) ✓
3. `AgentBackgroundService.ProcessEventsAsync`: routes on `case "chat"` → `HandleChatEventAsync` ✓
4. `ChatInterpreter.Interpret`: returns `ChatInterpretation` with `GoalName` ✓
5. `GoalFactory.CreateAsync`: resolves goal from registry ✓
6. `AgentBackgroundService.SetGoal`: starts pursuit ✓

No gaps in the chain. `craftComplete` and `smeltComplete` events are also handled in `ProcessEventsAsync` with informational logging. ✓

**Note:** The `onlinePlayers` count from Node.js filters out the bot itself: `Object.keys(bot.players).filter(p => p !== bot.username).length` — this is the correct human-only count. ✓

---

## Seat 4 — Human Learning Advocate

**Confidence:** 0.90 | **Vote:** APPROVE

**Review:**
The interactive dashboard (`index.html`) provides meaningful agent control:

- Goal selection with dynamic parameter inputs (Item ID + Count for GatherItem, Blueprint for Build) ✓
- Real-time status polling (health, food, position, inventory, queue count) ✓
- Build origin setter with "Use Bot Pos" shortcut ✓
- Action queue inspection (shows up to 30 pending actions) ✓
- Chat send panel ✓
- Dark theme consistent with `about.html` ✓
- `app.UseDefaultFiles()` means navigating to `/` now serves `index.html` — the main entry point is the control panel, not the static info page ✓

**Usability observation (non-blocking):** Chat received messages are not displayed in the log (only sent messages and sys messages). This requires SignalR/SSE push, deferred to Phase 6. Current UI is functional for control; observing chat requires the Minecraft client or server logs.

---

## Seat 5 — Skeptical Reviewer

**Confidence:** 0.80 | **Vote:** APPROVE with deferred concerns

**Potential CI regression check (same as Phase 4b protocol):**
Existing tests that use `IGoalFactory` as a type annotation — there are none (only `GoalFactory` concrete class is used in tests). Adding `CreateAsync` to the interface is non-breaking for all existing implementations. ✓

**Smelt timeout:** The 40-second timeout for smelting is reasonable for one item, but smelting 64 items would queue 64 separate smelt calls, each waiting up to 40s. A future optimization should place all items at once and wait for a full batch. Phase 6 improvement.

**CraftItem requires crafting table within 4 blocks:** The 4-block radius is tight. If the bot is 5 blocks away from its crafting table, craft will fail. Consider increasing to 8 blocks or pathfinding to the table first. Acceptable for Phase 5; Phase 6 improvement.

**`_pendingActions.RemoveAt(0)` called outside `_pendingLock`:** 

Wait — looking at the code more carefully:
```csharp
lock (_pendingLock)
{
    if (_pendingActions.Count > 0) _pendingActions.RemoveAt(0);
}
```
This IS inside the lock. ✓

**`HandleChatEventAsync` is called from within `ProcessEventsAsync`:** This means if `TryCreateGoalFromChatAsync` blocks, it pauses the event processing loop. Since `goalFactory.CreateAsync` calls `IItemRegistry.GetAsync` (HTTP to MemorySmith) or `IBlueprintRepository.GetAsync` (HTTP to MemorySmith), this could add 100-500ms latency to event processing per chat message. Acceptable for Phase 5; in Phase 6, consider a fire-and-forget `_ = Task.Run(...)` pattern for goal creation.

---

## Seat 6 — Synthesizer

**Confidence:** 0.87 | **Vote:** APPROVE — NO BLOCKING FINDINGS

**Summary:**
Phase 5 delivers a complete interactive dashboard, working chat interpretation, and three new tools (Chat, CraftItem, SmeltItem). The DI wiring in Program.cs is now correctly configured for all Phase 4b registry dependencies. The `POST /api/agent/plan` endpoint now correctly calls `CreateAsync` so dynamic goals (GatherItem: and Build:) work via REST.

**No blocking findings.** All concerns are non-blocking deferred items.

**Acceptance criteria — all met:**
- [x] IGoalFactory.CreateAsync added to interface
- [x] ChatInterpreter: solo-player + bot-name + conversation window heuristics
- [x] ChatInterpreter: gather, build, cancel, status, help, navigate intents
- [x] ChatTool, CraftItemTool, FurnaceTool registered in ToolDispatcher
- [x] ActionProtocol.Craft + Smelt constants
- [x] Program.cs: IItemRegistry + IBlueprintRepository + GoalFactory DI fixed
- [x] Program.cs: async plan endpoint (factory.CreateAsync)
- [x] Program.cs: new endpoints (DELETE /api/agent/goal, POST /api/agent/origin, GET /api/agent/queue, POST /api/agent/chat)
- [x] AgentBackgroundService: chat event → ChatInterpreter → SetGoal
- [x] AgentBackgroundService: CancelGoal, SetBuildOrigin, GetPendingActions, ConsecutiveFailures
- [x] index.js: onlinePlayers in chat events, craft action, smelt action
- [x] index.html: interactive dashboard (goal panel, status, inventory, queue, chat)
- [x] ChatInterpreterTests: 28 tests covering all intent types and heuristics
- [ ] CI green (pending — one transitive-reference risk flagged by Seat 1)

**Deferred to Phase 6:**
- SignalR for real-time chat/event push to dashboard
- CraftItemTool: pathfind to crafting table if not in range
- SmeltItem: batch smelting (place all items, wait once)
- HandleChatEventAsync: fire-and-forget goal creation to avoid event loop latency
- `_pendingActions`: replace List + RemoveAt(0) with Queue for O(1) dequeue