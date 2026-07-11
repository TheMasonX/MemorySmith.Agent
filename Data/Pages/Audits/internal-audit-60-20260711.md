# Internal Codebase Audit — Sprint 60

**Date:** 2026-07-11
**Version:** v0.60.0
**Scope:** MemorySmith.Agent (all projects) — Post-Sprint-60-Wave-C delta audit
**Type:** 10-agent parallel swarm audit + 5-seat council review
**Path:** Swarm (10 subagents)

## Executive Summary

Completed a 10-agent parallel swarm codebase audit of MemorySmith.Agent (Sprint 60, v0.60.0) following Sprint 60 Waves A–C. The audit focused on **remaining unfixed findings from Round 3** (2026-07-10), **regressions introduced by recent fixes**, and **new issues** in recently changed code (antiforgery, secret scanning, WorldModel updates, adapter goto timeout).

The 10 subagents explored independent partitions covering all layers of the codebase. Findings were consolidated, deduplicated, and severity-calibrated. A 5-seat heterogeneous council review then verified accuracy, recalibrated severities, and identified additional findings.

### Key Findings

- **Round 3 fix rate:** Of ~45 actionable findings from Round 3, approximately **12 were fixed** in Sprint 60 Waves A–C (~27% fix rate). The remaining ~33 findings are still open.
- **New findings:** 28 new issues discovered in this audit, including Sprint 60 regressions (WorldState.IsInventoryFresh UtcNow, WorldModel UtcNow usage, BlockState record equality broken).
- **Regressions:** 3 new Sprint 60 methods use `DateTimeOffset.UtcNow` directly despite `ITimeProvider` being available since Sprint 27.
- **Worst area:** WebUI.Blazor AgentBackgroundService — 0 of 6 Round 3 findings fixed; silent catches, SignalR logging, blocked command path, DashboardPublisherImpl dead code all persist.

### Severity Distribution

| Severity | Count | Description |
|:--------:|:-----:|-------------|
| **P0** | 7 | Critical — data loss, crash, or silent correctness failure |
| **P1** | 22 | High — significant functional or observability defect |
| **P2** | 27 | Medium — structural, test coverage, or consistency issue |
| **P3** | 20 | Low — documentation, observability, or tech debt |
| **Total** | **76** | |

### Severity Counts by Category

| Category | P0 | P1 | P2 | P3 |
|----------|:--:|:--:|:--:|:--:|
| Round 3 — NOT FIXED | 3 | 8 | 9 | 4 |
| Round 3 — PARTIALLY FIXED | 0 | 0 | 3 | 1 |
| NEW — This audit | 4 | 14 | 15 | 15 |

---

## P0 — Critical

### MSA-INFRA-001: ReplanGovernor hardcodes DateTimeOffset.UtcNow (Round 3, NOT FIXED)
**File:** `Agent.Core/ReplanGovernor.cs` (lines ~99, 115, 165)
**The issue:** Three instances of `DateTimeOffset.UtcNow` used directly instead of injected `ITimeProvider`. The `Evaluate` method uses it for auto-recovery timeout comparison, `_stalledAt` stamping uses it, and `TryAutoRecover` repeats the pattern. Constructor does not accept `ITimeProvider`.
**Impact:** Graduated backoff (5→10→20→30s) and auto-recovery are untestable with `FakeTimeProvider`. Tests must use zero-second timeout hacks instead of advancing a virtual clock.
**Recommendation:** Inject `ITimeProvider timeProvider` with default `SystemTimeProvider.Instance`, replace all `DateTimeOffset.UtcNow` with `_timeProvider.UtcNow`, and update tests to use `FakeTimeProvider`.
**Tracked:** TSK-0371 (Backlog)

### MSA-WEB-006: AgentRuntime registered but never consumed (Round 3, NOT FIXED)
**File:** `Agent.Core/Runtime/AgentRuntime.cs`, `WebUI.Blazor/Program.cs` (line ~403), `AgentBackgroundService.cs`
**The issue:** `AgentRuntime` is a sealed record grouping the 6 manager interfaces. It's registered in DI and all 6 managers are individually registered, but `AgentBackgroundService` (~3660 lines, 18+ DI params) receives none of them. It directly owns `_worldState`, runs its own `ProcessEventsAsync`, does its own dispatch and recovery. `IDashboardPublisher.PublishStatusAsync` is never called from anywhere.
**Impact:** The entire Sprint 36/37 runtime decomposition architecture exists as dead code — 5 concrete implementations never consumed, an `AgentRuntime` record never used, and a ~3660-line monolith that was supposed to have been decomposed 24 sprints ago.
**Recommendation:** Either execute the decomposition (make `ExecuteAsync` delegate to `AgentRuntime.TickAsync`) or remove the dead code. The midpoint — keeping dead code forever — is the worst option.
**Tracked:** TSK-0369 (Backlog, deferred)

### MSA-ADAPT-005: MinecraftAdapter uses Linux-only `kill` on Windows (Round 3, NOT FIXED)
**File:** `Agent.World.Minecraft/MinecraftAdapter.cs` (line ~60)
**The issue:** `DisconnectAsync` spawns `kill -TERM {pid}` — a Unix-only command. On Windows this throws. The `catch { }` swallows the error silently, so SIGTERM is skipped. The Node.js adapter is always force-killed.
**Impact:** On Windows dev machines, the Node.js adapter process receives no graceful shutdown signal. State may be lost.
**Recommendation:** Use `Process.Kill(sendCtrlC: true)` as a Windows-friendly graceful signal, or use cross-platform `taskkill /PID`. Log the OS platform at startup.

### MSA-ADAPT-003/004: Five JS-emitted events have no C# ParseEvent handler (Round 3, NOT FIXED)
**File:** `Agent.World.Minecraft/WebSocketBridge.cs` (ParseEvent switch)
**The issue:** These events are emitted by `index.js` but have no corresponding `case` in `WebSocketBridge.ParseEvent`:

| Event | C# Type Exists? | ParseEntry? | Status |
|---|---|---|---|
| `disconnected` | ❌ | ❌ | **Dropped** |
| `reconnected` | ❌ | ❌ | **Dropped** |
| `blockBelowChanged` | ✅ `BlockBelowChangedEvent` | ❌ | **Dropped** |
| `blockMineSkipped` | ❌ | ❌ | **Dropped** |
| `creativeProvisionComplete` | ❌ | ❌ | **Dropped** |

`blockBelowChanged` is especially notable: the event type IS defined in `WorldEvents.cs` and IS handled in `AgentBackgroundService.cs` line 1094, but the JSON parser switch never deserializes it — the event is silently dropped.
**Impact:** Safety-critical collision-avoidance events and connectivity signals are completely invisible to the C# agent.
**Recommendation:** Add ParseEvent entries for all five events. Create typed event records for those missing.

### MSA-CORE-001: ToolResult allows contradictory Success/Outcome (Round 3, NOT FIXED)
**File:** `Agent.Core/Models/ActionData.cs` (line ~34)
**The issue:** `ToolResult` is a positional record with both `bool Success` and `OutcomeType Outcome`. Both are independently settable, so `new ToolResult(Success: false, Outcome: OutcomeType.Completed)` is possible.
**Impact:** Downstream consumers checking `result.Success` get different answers than those checking `result.Outcome`.
**Recommendation:** Remove the standalone `Success` property from `ToolResult` and compute it from `Outcome`.

### MSA-CORE-002: ActionData mutable dictionaries allow post-init corruption (Round 3, NOT FIXED)
**File:** `Agent.Core/Models/ActionData.cs` (lines 14-21)
**The issue:** `Arguments` and `Context` are `Dictionary<string, object?>` with `{ get; init; }`. The dictionary reference is frozen but the **contents** are mutable. Any code holding a reference can mutate dictionaries after construction.
**Impact:** Shared-mutation bugs can still occur if a caller retains a reference to dispatched `ActionData`.
**Recommendation:** Change both to `IReadOnlyDictionary<string, object?>` and use `new Dictionary(...)` in the constructor.

---

## P1 — High

### MSA-CORE-005: Silent catch in DisconnectAsync (Round 3, NOT FIXED)
**File:** `WebUI.Blazor/AgentBackgroundService.cs` (line ~684)
**The issue:** `catch { /* best-effort cleanup */ }` in the disconnect path swallows exceptions with zero logging. Violates AGENTS.md Rule E-3.
**Note:** The ClearAndEnqueueAsync onStopError was FIXED in Wave A. This is a DIFFERENT silent catch (the disconnected-exhaustion path).
**Recommendation:** Log at `LogWarning` with context.

### MSA-CORE-012: Fact.Value is string while WorldState.Facts stores object? (Round 3, NOT FIXED)
**File:** `Agent.Core/Models/Fact.cs` (line ~54), `WorldState.cs` (line ~19)
**The issue:** `Fact.Value` is typed `string`, but `WorldState.Facts` is `Dictionary<string, object?>`. When code reads `Facts["key"]` and compares to `Fact.Value`, types don't match — callers must `Convert.ToString()`.
**Recommendation:** Either widen `Fact.Value` to `object?` or narrow `WorldState.Facts` to `Dictionary<string, string>`.

### MSA-CORE-018: WorldState.Facts exposed as mutable dictionary (Round 3, NOT FIXED)
**File:** `Agent.Core/Models/WorldState.cs` (line ~19)
**The issue:** `Facts` is `Dictionary<string, object?>` with `{ get; init; }`. Callers can mutate after construction, bypassing provenance tracking of `StructuredFacts`.
**Recommendation:** Change to `IReadOnlyDictionary<string, object?>` and provide an explicit `SetFact` path via `Builder`.

### MSA-CORE-020: BlockPlacedEvent.CorrelationId is Guid? while others use string? (Round 3, NOT FIXED)
**File:** `Agent.Core/Events/WorldEvents.cs` (line 184)
**The issue:** `BlockPlacedEvent` uses `Guid?`, while `ActionStartedEvent`, `ActionProgressEvent`, `ActionFailedEvent`, etc. all use `string?`. `PendingAction.CorrelationId` is `Guid`.
**Recommendation:** Standardize all to `string?` (the JS adapter sends strings).

### MSA-CORE-021: MineAbortedEvent nullable vs MineCompleteEvent non-nullable BlockPosition (Round 3, NOT FIXED)
**File:** `Agent.Core/Events/WorldEvents.cs` (lines ~155, 230)
**The issue:** `MineAbortedEvent.BlockPosition` is `Position?` (nullable), while `MineCompleteEvent.BlockPosition` is `Position` (non-nullable).
**Recommendation:** Make both nullable and update the WebSocketBridge fallback to use `null` instead of `new Position(0,0,0)`.

### MSA-PLAN-001: GatherItemDecompose mines ALL source blocks at full count (Round 3, NOT FIXED)
**File:** `Agent.Planning/HtnTaskLibrary.cs` (line ~820)
**The issue:** Iterates ALL source blocks in `spec.SourceBlocks` and emits a `MineBlock` action for each, all with the full requested count. For `OakLogSpec` (7 source blocks) with count=10, up to 70 blocks mined for 10 requested.
**Impact:** Wasteful — bot mines far more than needed.
**Recommendation:** Mine only the first source block found, fall through to variants only after `BlockNotFound`.

### MSA-PLAN-002: SurviveNight decomposers are all stubs (Round 3, NOT FIXED)
**File:** `Agent.Planning/HtnTaskLibrary.cs` (lines ~840–878)
**The issue:** All four SurviveNight-related decomposers emit nothing but `[GetStatus]`.
**Recommendation:** Implement at minimum `FindShelterDecompose` and `LightAreaDecompose`.

### MSA-PLAN-006: IGoalPrecondition not enforced in planning pipeline (Round 3, NOT FIXED)
**File:** `Agent.Planning/Goals/*.cs`, `Router/PlannerRouter.cs`
**The issue:** Three goals implement `IGoalPrecondition`, but `CanAttempt` is only called in `PlanningManagerImpl` — not in `HtnPlanner.PlanAsync` or any decomposer.
**Recommendation:** Add precondition check in `PlannerRouter.Select()` or `DecomposerRegistry.Find()`.

### MSA-PLAN-008: MineWoodDecompose over-mining (2×) — NEW
**File:** `Agent.Planning/HtnTaskLibrary.cs` (line ~830)
**The issue:** `MineWoodDecompose` mines BOTH `oak_log` AND `birch_log` at the full requested count.
**Recommendation:** Mine only one variant unless the first fails.

### MSA-LLM-005: Gemini API key in URL query string (Round 3, NOT FIXED, escalated to P1)
**File:** `Agent.Planning/Llm/GeminiProvider.cs` (line ~30)
**The issue:** API key embedded in URL query string: `?key={options.LlmApiKey}`. Exposed in server logs, referrer headers, crash dumps.
**Recommendation:** Use `X-Goog-Api-Key` header instead.

### MSA-LLM-003: Cloud providers hardcode MaxTokens=512 (Round 3, NOT FIXED)
**File:** `Agent.Planning/Llm/AnthropicProvider.cs` (line ~47), `OpenAICompatibleProvider.cs` (line ~65)
**The issue:** Both ignore `options.LlmMaxResponseTokens`. Gemini has no max-token parameter at all.
**Recommendation:** Apply Ollama's pattern to all providers.
**Tracked:** TSK-0370 (Backlog)

### MSA-MEM-001: LocalKnowledgeResolver.SearchAsync no error handling (Round 3, NOT FIXED)
**File:** `Agent.Memory/LocalKnowledgeResolver.cs` (line ~88)
**The issue:** Calls `SearchAsync` with zero try/catch. Transient HTTP failure crashes the pipeline.
**Tracked:** TSK-0374 (Backlog)

### MSA-MEM-002: CreatePageAsync throws on HTTP failure (Round 3, NOT FIXED)
**File:** `Agent.Memory/RestMemoryGateway.cs` (line ~95)
**The issue:** `EnsureSuccessStatusCode()` throws on 4xx/5xx with no error handling.
**Tracked:** TSK-0374 (Backlog)

### MSA-MEM-003: GetPageAsync collapses all HTTP errors to null (Round 3, NOT FIXED)
**File:** `Agent.Memory/RestMemoryGateway.cs` (line ~73)
**The issue:** `if (!resp.IsSuccessStatusCode) return null;` — callers can't distinguish 404 from 401/403/500.
**Recommendation:** Log warnings for non-404; throw on 401/403.

### NEW — RestMemoryGateway.GetPageAsync has no transport-level error handling
**File:** `Agent.Memory/RestMemoryGateway.cs` (line ~70)
**The issue:** Calls `http.GetAsync` with zero try/catch. Transport failures (DNS, TLS, connection refused) throw unhandled `HttpRequestException`.

### MSA-WEB-001/002: Silent catch blocks in ABS and Program.cs (Round 3, NOT FIXED)
**File:** `WebUI.Blazor/AgentBackgroundService.cs` (lines 684, 1630, 3659), `Program.cs` (line 601)
**The issue:** Three `catch { /* best-effort ... */ }` blocks with zero logging in ABS, plus one in Program.cs status endpoint.
**Recommendation:** Replace each with `logger.LogWarning(ex, ...)` per Rule E-3.

### MSA-WEB-003: SignalR push failures logged at Debug instead of Warning (Round 3, NOT FIXED)
**File:** `WebUI.Blazor/AgentBackgroundService.cs` (lines 3694, 3710, 3726)
**The issue:** All three dashboard push methods log SignalR failures at `LogDebug` — invisible in production.
**Recommendation:** Change to `LogWarning`.

### MSA-WEB-004: Hardcoded version in `/api/about` (Round 3, PARTIALLY FIXED)
**File:** `WebUI.Blazor/Program.cs` (line 578)
**The issue:** Startup log was fixed to use `ProductVersion` dynamically, but `/api/about` endpoint still returns `"0.55.0"` hardcoded. Dashboard `index.html` shows `v0.55.1`. Three different version strings across the app.
**Recommendation:** Derive `/api/about` version from assembly metadata.

### MSA-WEB-005: Blocked command path double-enqueues chat message (Round 3, NOT FIXED)
**File:** `WebUI.Blazor/AgentBackgroundService.cs` (lines 1392–1407)
**The issue:** When `pendingResponse` is non-null, the code records it in history but enqueues the terse `blockedMsg` fallback — the player sees "Command was blocked" while the LLM's nuanced response is discarded.
**Recommendation:** Use the LLM's actual response when available.

### MSA-MGR-001: DashboardPublisherImpl is dead code (Round 3, NOT FIXED)
**File:** `WebUI.Blazor/Managers/DashboardPublisherImpl.cs`
**The issue:** Registered in DI but none of its methods are ever called. ABS has its own independent push methods. `QueuedActions` hardcoded to 0. Dual SignalR write surface.
**Recommendation:** Either delete or wire up to replace ABS inline pushes.

### MSA-MGR-004: IntentManagerImpl hardcodes DefaultOnlinePlayers=1 (Round 3, NOT FIXED)
**File:** `WebUI.Blazor/Managers/IntentManagerImpl.cs` (line 27)
**The issue:** Doc comment says "Sprint 40: replace with IWorldAdapter.OnlinePlayerCount" but still hardcoded.
**Recommendation:** Inject `IWorldAdapter` and derive onlinePlayers from it.

### NEW — WorldState.IsInventoryFresh() uses DateTimeOffset.UtcNow (Sprint 60 regression)
**File:** `Agent.Core/Models/WorldState.cs` (line 54)
**The issue:** New method added in Sprint 60 Wave D (TSK-0302) uses `DateTimeOffset.UtcNow` directly despite `ITimeProvider` available since Sprint 27.
**Recommendation:** Accept optional `DateTimeOffset? now = null` param defaulting to `UtcNow`.

### NEW — WorldModel uses DateTimeOffset.UtcNow everywhere
**File:** `Agent.Core/Models/WorldModel.cs` (lines 46, 48, 66, 183)
**The issue:** Constructor, `Observe`, and `ApplyOutcome` all use `DateTimeOffset.UtcNow`. No `ITimeProvider` injection.
**Recommendation:** Add optional `ITimeProvider? timeProvider = null` parameter.

### NEW — ActionOutcome factory methods hardcode DateTimeOffset.UtcNow
**File:** `Agent.Core/Models/ActionOutcome.cs` (lines ~61-100)
**The issue:** All 7 factory helpers pass `DateTimeOffset.UtcNow` directly. Makes them untestable for time-sensitive scenarios.
**Recommendation:** Add optional `DateTimeOffset? timestamp = null` parameter to each factory method.

### NEW — WorldState mutable dictionaries break record value equality
**File:** `Agent.Core/Models/WorldState.cs` (lines 15-18)
**The issue:** `Inventory` is `Dictionary<string, int>` (mutable) and `Facts` is `Dictionary<string, object?>` (mutable). C# records compare `Dictionary` by reference equality, not structural content. Two `WorldState` instances with identical inventory content but different `Dictionary` references are NOT `==` equal.
**Impact:** Any code relying on `WorldState` value equality gets incorrect results.
**Recommendation:** Override `Equals`/`GetHashCode` to compare dictionary contents, or use `IReadOnlyDictionary` with a structural comparer.

### MSA-ADAPT-007: Unknown events silently dropped in ParseEvent (Round 3, NOT FIXED)
**File:** `Agent.World.Minecraft/WebSocketBridge.cs` (line ~550)
**The issue:** `_ => null` catch-all returns null without any logging. Any new event type added to JS is silently invisible on C# side.
**Recommendation:** Add `LogDebug` with the event type name.

### MSA-ADAPT-008: scanBlockBelow silent catch (Round 3, NOT FIXED)
**File:** `MineflayerAdapter/index.js` (line ~443)
**The issue:** Empty `catch (err) {}` with no logging.
**Recommendation:** Add `logStructured('warn', 'entity', 'scanBlockBelow failed', { error: err.message })`.

### AG-005: `_stopRequested` not checked in craft/smelt (Round 3, NOT FIXED)
**File:** `MineflayerAdapter/index.js`
**The issue:** The `craft` and `smelt` action cases do not check `_stopRequested`.
**Recommendation:** Check before starting new operations.

### MSA-ADAPT-009: WebSocket close event lacks close code/reason (Round 3, NOT FIXED)
**File:** `MineflayerAdapter/index.js` (line ~200)
**The issue:** Close handler logs only `[ws] C# agent disconnected` — no `code` or `reason`.
**Recommendation:** Log the WebSocket close event code and reason.

### NEW — BlockBelowChangedEvent silently dropped by WebSocketBridge
**File:** `Agent.World.Minecraft/WebSocketBridge.cs` (line ~498)
**The issue:** JS adapter emits `blockBelowChanged`, `WorldEvents.cs` defines `BlockBelowChangedEvent`, and `AgentBackgroundService.cs` has a handler — but `WebSocketBridge.ParseEvent` has NO `"blockBelowChanged"` case. The event falls through to `_ => null` and is silently discarded.
**Impact:** Block-below tracking is completely broken.
**Recommendation:** Add `"blockBelowChanged" =>` case in ParseEvent.

### NEW — SignalR hub has no authentication (Council finding, NOT FIXED)
**File:** `WebUI.Blazor/AgentHub.cs`
**The issue:** No `[Authorize]`, no auth middleware. Any peer who can reach the endpoint receives real-time agent state.
**Recommendation:** Add SignalR authentication.

### NEW — No HTTPS redirection (Council finding, NOT FIXED)
**File:** `WebUI.Blazor/Program.cs`
**The issue:** No `UseHttpsRedirection()` or `UseHsts()`. API keys in cleartext.
**Recommendation:** Add conditional HTTPS redirection.

---

## P2 — Medium

### MSA-CORE-003: ActionOutcome factories hardcode UtcNow (Round 3, NOT FIXED)
**File:** `Agent.Core/Models/ActionOutcome.cs` (lines ~61-100)

### MSA-CORE-009: BuildProgressReport.PercentComplete ignores skipped/in-progress (Round 3, NOT FIXED)
**File:** `Agent.Core/Models/BuildProgressReport.cs` (line ~19)

### MSA-CORE-014: PlanningPolicy interfaces unwired (Round 3, PARTIALLY FIXED)
**File:** `Agent.Core/Models/PlanningPolicy.cs`
**Note:** `IGoalPrecondition` is wired. `IGoalPostcondition` and `IRemediationPolicy` remain dead.

### MSA-CORE-019: WorldStateDiff.DescribeMismatches duplicates expectedKeys logic (Round 3, NOT FIXED)
**File:** `Agent.Core/Models/WorldStateDiff.cs` (lines ~160-210)

### MSA-PLAN-004: PlaceBlockGoal.Dispatched setter not thread-safe (Round 3, PARTIALLY FIXED)
**File:** `Agent.Planning/Goals/PlaceBlockGoal.cs` (line ~55)
**Note:** Production path (`IncrementDispatched`) is safe. The setter remains unsafe for test use.

### MSA-PLAN-009: PlaceBlockGoal missing IGoalPrecondition — NEW
**File:** `Agent.Planning/Goals/PlaceBlockGoal.cs`
**The issue:** Unlike `GenericGatherGoal`, `CraftItemGoal`, `SmeltGoal`, `PlaceBlockGoal` does not implement `IGoalPrecondition`.
**Recommendation:** Add `IGoalPrecondition` with `HasFreshInventory` guard.

### MSA-PLAN-010: PlaceBlockGoal.HasFailed no creative mode guard — NEW
**File:** `Agent.Planning/Goals/PlaceBlockGoal.cs` (line ~95)
**The issue:** Checks `have <= 0` but has no creative mode guard. In creative, blocks aren't deducted from inventory.
**Recommendation:** Add `if (state.IsCreativeMode) return false;`.

### MSA-LLM-008: ChatRateLimiter.Prune() never called (Round 3, NOT FIXED)
**File:** `Agent.Planning/ChatRateLimiter.cs`

### MSA-TOOL-001: CreatePageTool throws instead of ToolResult (Round 3, NOT FIXED)
**File:** `Agent.Tools/Tools/CreatePageTool.cs` (lines 40-43)

### MSA-TOOL-003: InputSchema caching bug — 11 of 14 tools (Round 3, NOT FIXED)
**File:** Multiple tool files — only `FindFlatAreaTool`, `QueryBlocksTool`, `QueryEntitiesTool` have the fix.

### MSA-TOOL-004: QueryBlocksTool.GetRequiredInt throws (Round 3, NOT FIXED)
**File:** `Agent.Tools/Tools/QueryBlocksTool.cs` (lines 93-98)

### MSA-TOOL-005: MineBlockTool defaults to oak_log (Round 3, NOT FIXED)
**File:** `Agent.Tools/Tools/MineBlockTool.cs` (line 30)

### MSA-TOOL-006: PlaceBlockTool defaults to cobblestone (Round 3, NOT FIXED)
**File:** `Agent.Tools/Tools/PlaceBlockTool.cs` (line 45)

### MSA-TOOL-007: WanderTool uses GetInt32 not TryGetInt32 (Round 3, NOT FIXED)
**File:** `Agent.Tools/Tools/WanderTool.cs` (line 31)

### MSA-TOOL-002: ChatTool silently truncates >256 chars (Round 3, NOT FIXED)
**File:** `Agent.Tools/Tools/ChatTool.cs` (lines 35-37)

### MSA-TOOL-008: No FindReachableBlock / ConstructBlueprint ITool (Round 3, NOT FIXED)
**File:** `Agent.Tools/ActionProtocol.cs` + missing tool files

### MSA-MEM-005: UpdatePageAsync GET/PUT race window (Round 3, PARTIALLY FIXED)
**File:** `Agent.Memory/RestMemoryGateway.cs` (lines 115-135)
**Note:** Fast-path added when `title` is provided. Race window still open when `title` is null.

### MSA-MEM-007: ParseItemSpec no large-content guard (Round 3, NOT FIXED)
**File:** `Agent.Memory/MemorySmithItemRegistry.cs` (line ~130)

### MSA-MGR-006: StateManagerImpl.BuildContext has no logging (Round 3, NOT FIXED)
**File:** `WebUI.Blazor/Managers/StateManagerImpl.cs` (lines 64-76)

### MSA-ADAPT-002: stopState.js is dead code (Round 3, NOT FIXED)
**File:** `MineflayerAdapter/stopState.js`

### MSA-ADAPT-006: DisconnectAsync has three silent catch blocks (Round 3, NOT FIXED)
**File:** `Agent.World.Minecraft/MinecraftAdapter.cs` (lines ~70-110)

### AG-004: ~9 goto() calls lack timeout protection (Round 3, PARTIALLY ADDRESSED)
**File:** `MineflayerAdapter/index.js`
**Note:** TSK-0346 fixed the `move` case only. Other goto() calls remain unprotected.

### NEW — No rate limiting on REST API endpoints (Council finding, NOT FIXED)
**File:** `WebUI.Blazor/Program.cs`
**The issue:** Only LLM chat has rate limiting. REST API POST endpoints have none.

### NEW — BlockState record equality structurally broken
**File:** `Agent.Construction/BlockState.cs`
**The issue:** `IReadOnlyDictionary<string, string>` property uses reference equality. Two semantically identical `BlockState` instances with same `Properties` are NOT equal.
**Recommendation:** Override `Equals`/`GetHashCode` for structural dictionary comparison.

### NEW — BlueprintExecutor no null BlockId guard
**File:** `Agent.Construction/BlueprintExecutor.cs` (line ~37)
**The issue:** No guard against null or empty `BlockId`. Mis-parsed blueprints emit invalid actions.
**Recommendation:** Add validation at start of `Execute`.

### NEW — UpdatePageAsync PUT paths have no error handling
**File:** `Agent.Memory/RestMemoryGateway.cs` (lines ~110, ~131)
**The issue:** Both PUT branches call `EnsureSuccessStatusCode()` with no try/catch.

### NEW — WebSocketBridge silently drops unknown event types
**File:** `Agent.World.Minecraft/WebSocketBridge.cs` (line ~498)
**The issue:** `_ => null` default case has no logging — not even Debug.

### NEW — GoTo( ) timeout gap partially addressed (TSK-0346)
**File:** `MineflayerAdapter/index.js`
**Note:** Only the `move` case was fixed. Other goto() callers still lack timeout protection.

---

## P3 — Low

### MSA-CORE-021: MineAbortedEvent nullable vs non-nullable (overlap with P1)
### MSA-CORE-022: ItemCraftedEvent/ItemConsumedEvent unwired stubs — COMMENTS STALE
### MSA-LLM-001: Typo in IntentManager doc comment — FIXED in Wave A
### MSA-LLM-002: Duplicate XML doc block in ChatInterpreter.ParseIntent
### MSA-LLM-007: ChatIntentType enum is dead code
### MSA-MEM-007: ParseItemSpec no guard
### MSA-ADAPT-010: Monolithic dispatch switch (1400+ lines)
### NEW — Cloud providers silently swallow HTTP errors
### NEW — LlmContextLogger archive extension mislabeled (.zip vs .gz)
### NEW — GeminiProvider null ApiKey produces degraded URL
### NEW — No CORS configuration
### NEW — /api/agent/connect and /api/agent/stop are no-op stubs
### NEW — IgnoreAntiforgeryTokenAttribute unused in MemorySmith.Agent
### NEW — findGroundY catch block silent
### NEW — Stale WorldEvent doc comments
### NEW — SimpleGoal.HasFailed returns false for null predicate
### NEW — CommonMinecraftBlocks.SelfDroppingBlocks and BlockToItemDrop overlap
### NEW — WorldModel.ApplyOutcome ignores PositionChanged/BlockPlaced/BlockMined effect types
### NEW — Duplicate entity parsing in Program.cs and ABS

---

## Architecture Notes

1. **AgentBackgroundService monolith persists**: ~3660 lines, 18+ DI parameters. The Sprint 39 AgentRuntime decomposition is registered but never consumed. 0 of 6 Round 3 findings in this area were fixed.

2. **ITimeProvider adoption stalled**: Despite `ITimeProvider` being available since Sprint 27, 6 new or existing code paths use `DateTimeOffset.UtcNow` directly. This batch includes 3 new Sprint 60 regressions (WorldState.IsInventoryFresh, WorldModel constructor, WorldModel.Observe/ApplyOutcome).

3. **Dual SignalR write surface**: `DashboardPublisherImpl` is registered but dead; ABS has its own inline push methods. If both were ever active, dashboard clients would receive duplicate snapshots.

4. **Event type proliferation**: 34+ event types in `WorldEvents.cs`. No schema versioning strategy. 5 events emitted by the JS adapter never reach C# side.

5. **Tool InputSchema caching inconsistent**: 3 tools use the correct `static readonly JsonDocument` pattern; 11 do not. The fix pattern is established but not applied systematically.

6. **Round 3 fix rate is concerning**: Only ~27% of actionable findings were fixed in Sprint 60 Waves A-C. The remaining ~73% persist. Many are tagged with TSK numbers in Backlog status.

7. **Test coverage gaps persist**: Agent.Vision and Agent.Personality still have zero test coverage. BlueprintExecutor untested. SurviveNight decomposition untested.

---

## Methodology

10 heterogeneous subagents covering independent codebase layers:
1. **Core Models & Events** (22 findings)
2. **Core Interfaces & Runtime** (10 findings)
3. **Core Runtime & WorldState** (9 findings)
4. **HTN Planner & Goal System** (13 findings)
5. **LLM Integration & Chat** (11 findings)
6. **Tool System** (15 findings)
7. **Memory Gateway & Construction** (14 findings)
8. **WebUI Blazor — Infrastructure** (11 findings)
9. **WebUI Blazor — ABS & Managers** (13 findings)
10. **MineflayerAdapter & World** (13 findings)

Each subagent received file-level scope, an audit checklist (bugs, inconsistencies, gaps, guards, error handling, observability, overcoupling, architecture), and was instructed to verify Round 3 fix status and identify new findings.

Total: ~131 findings from swarm → consolidated and deduplicated to **76 total**.

---

## Peer Review Results

5-seat heterogeneous council review completed on 2026-07-11. The council recalibrated severities, merged 17 raw findings into 7 groups, identified 17 new findings, and flagged 3 false positives.

### Council-Recalibrated Severity Distribution

| Severity | Raw Audit | Council Calibrated | Delta |
|:--------:|:---------:|:------------------:|:-----:|
| **P0** | 7 | 5 | −2 (downgraded) |
| **P1** | 22 | 14 | −8 (downgraded) |
| **P2** | 27 | 28 | +1 |
| **P3** | 20 | 32 | +12 |
| **False positives removed** | 0 | 2 | −2 |
| **Total** | **76** | **79** | **+3** |

### Severity Recalibrations (Downgrades)

| Finding | Original | Council | Reason |
|:--------|:--------:|:-------:|:-------|
| MSA-INFRA-001 — ReplanGovernor UtcNow | P0 | **P1** | Testability gap, not runtime correctness failure |
| MSA-WEB-006 — AgentRuntime dead code | P0 | **P2** | Architectural debt, not active crash/security risk |
| MSA-ADAPT-005 — Linux kill on Windows | P0 | **P1** | Force-kill fallback exists; empty catch is the real bug |
| MSA-ADAPT-003/004 — 5 dropped events | P0 | **P1/P2** | Split by severity: blockBelowChanged is P1, others P2 |
| MSA-CORE-001 — ToolResult contradiction | P0 | **P2** | Theoretical — no code sets non-default Outcome |
| MSA-CORE-002 — ActionData mutable dicts | P0 | **P2** | Defensive regression risk, not active corruption path |
| MSA-WEB-004 — Hardcoded version | P2 | **P3** | Startup log already fixed; only `/api/about` remains |

### Severity Recalibrations (Upgrades)

| Finding | Original | Council | Reason |
|:--------|:--------:|:-------:|:-------|
| CreativeProvider ESM/CJS fix incomplete | P1 | **P0** | Wave A fix regressed — .cjs cannot require() ESM logger.js |
| AG-004 — goto() timeout gap | P2 | **P1** | Only `move` case fixed; 9+ other calls still unprotected |
| (Council findings) | — | **P0** | sendEvent crash (P0-002), unhandled rejection (P0-003), pipe buffer hang (P0-004), LLM prompt injection (P0-006) |

### Merged/Deduplicated Findings

The council merged 17 raw findings into 7 groups:

| Merge Group | Findings Absorbed | Council ID |
|:------------|:------------------|:-----------|
| **ITimeProvider gap** | MSA-INFRA-001, WorldState.IsInventoryFresh, WorldModel UtcNow, ActionOutcome factories UtcNow | P1-001 |
| **Goal HasFailed dead pattern** | MSA-PLAN-004 (partial), MSA-PLAN-009, MSA-PLAN-010 | P1-002 |
| **Goal precondition gaps** | MSA-PLAN-009, MSA-PLAN-010 | P2-005 |
| **Runtime decomposition dead code** | MSA-WEB-006, MSA-MGR-001, MSA-MGR-004, MSA-MGR-006 | P2-007 |
| **Tool InputSchema inconsistencies** | MSA-TOOL-003, MSA-TOOL-004, MSA-TOOL-007 | P2-013/014 |
| **Silent catch blocks (Rule E-3)** | MSA-WEB-001/002, MSA-CORE-005, MSA-ADAPT-006, MSA-ADAPT-008 | Distributed |
| **JS event emission gaps** | MSA-ADAPT-003/004, MSA-ADAPT-009, MSA-ADAPT-007 | Distributed |

### Corrections to Report Text (Council-Verified)

1. **MSA-WEB-005 (Blocked command path)**: Finding is **inaccurate** — code at ABS line 1402 uses `pendingResponse ?? "Command was blocked..."`. When `pendingResponse` is non-null, the LLM's actual response IS sent. Should be marked **FIXED** (Sprint 57 Wave D).

2. **SurviveNightGoal.HasFailed triggers on default health**: **FALSE POSITIVE** — default `WorldState.Health = 20`, threshold is ≤ 4. No code path sets Health ≤ 4 by default. **Remove this finding.**

3. **WorldModel.Predict switch has no default**: **FALSE POSITIVE** — the switch has `_ => PredictUnknown(...)` added in Sprint 59 (TSK-0336/0309). **Remove this finding.**

4. **Smelt `finally` block ReferenceError**: **FALSE POSITIVE** — `furnace` is assigned before the `try` block, not inside it. **Remove this finding.**

5. **Round 3 fix rate**: ~27% overall, but **100% of P0 actionable findings** were fixed in Wave A. The remaining ~73% are mostly P2/P3 tracked in Backlog.

### New Findings From Council

The council identified 17 new findings missing from the raw audit:

#### P0 — Critical (Must Add)

| ID | Title | File |
|:---|:------|:-----|
| P0-002 | `sendEvent` has no error handling — can crash Node.js process | `index.js:122-129` |
| P0-003 | No `unhandledRejection` / `uncaughtException` process handlers | `index.js:~2100` |
| P0-004 | Node.js stdout/stderr pipes never read → subprocess hang risk | `MinecraftAdapter.cs:~93` |
| P0-006 | LLM prompt injection — no input sanitization for chat messages | `LlmChatInterpreter.cs:~230` |

#### P1 — High

| ID | Title | File |
|:---|:------|:-----|
| P1-010 | No chat command rate limiting / `cmdQueue` unbounded | `index.js:~240` |
| P1-003 | BuildGoal missing `Id` property (all instances share `Guid.Empty`) | `BuildGoal.cs:~47` |

#### P2 — Medium

| ID | Title | File |
|:---|:------|:-----|
| P2-010 | `/api/agent/stop` and `/api/agent/connect` silent no-op stubs | `Program.cs:~687-688` |
| P2-016 | WaitForPortAsync throws TimeoutException even when cancelled | `MinecraftAdapter.cs:~130-140` |
| P2-018 | Test-TaskRecords.ps1 doesn't validate all required schema fields | `Scripts/Test-TaskRecords.ps1` |
| P2-019 | Verify-AboutDeps.ps1 not run in CI | `.github/workflows/ci.yml` |
| P2-020 | AGENTS.md "Key Interfaces" references Sprint 36 future work (drift) | `AGENTS.md:~620` |

### Verified Accurate (All P0/P1 confirmed by ≥2 council seats)

All P0 and P1 findings in this report were verified as accurate by at least 2 council seats, with source-code verification confirming file paths, line numbers (within ±40 lines), and impact assessments. The exceptions noted above (MSA-WEB-005, 3 false positives) were identified and corrected.

## Task Creation

The following sections document new MCP tasks created for findings without existing task coverage.

### New Tasks Created

| Task Key | Finding | Priority |
|:---------|:--------|:--------:|
| TSK-0387 | sendEvent crash guard (P0-002) | Critical |
| TSK-0388 | unhandledRejection/uncaughtException handlers (P0-003) | Critical |
| TSK-0389 | Node.js pipe buffer hang fix (P0-004) | Critical |
| TSK-0390 | LLM prompt injection guard (P0-006) | Critical |
| TSK-0391 | CreativeProvider ESM/CJS regression fix (P0-001) | Critical |
| TSK-0392 | ToolResult contradictory Success/Outcome fix | High |
| TSK-0393 | ActionData mutable dictionaries fix | High |
| TSK-0394 | ITimeProvider injection batch (ReplanGovernor, WorldState, WorldModel, ActionOutcome) | High |
| TSK-0395 | BlockBelowChangedEvent WebSocketBridge wiring | High |
| TSK-0396 | 5 JS events missing C# ParseEvent handlers | High |
| TSK-0397 | GatherItemDecompose over-mining fix | High |
| TSK-0398 | MineWoodDecompose over-mining fix | High |
| TSK-0399 | Gemini API key move to header | High |
| TSK-0400 | Cloud providers MaxTokens config respect | High |
| TSK-0401 | Memory gateway error handling (GetPageAsync, CreatePageAsync, LocalKnowledgeResolver) | High |
| TSK-0402 | ABS silent catch blocks (Wave A missed) | High |
| TSK-0403 | SignalR push log level Debug→Warning | High |
| TSK-0404 | DashboardPublisherImpl dead code — wire or remove | High |
| TSK-0405 | goto() timeout protection for remaining 9+ calls | High |
| TSK-0406 | craft/smelt stop request guard | High |
| TSK-0407 | BuildGoal.Id property for per-goal correlation | High |
| TSK-0408 | Chat command rate limiting / cmdQueue bounds | High |
| TSK-0409 | No chat command rate limiting / cmdQueue unbounded | High |

### Findings with Existing Task Coverage

| Finding | Existing Task | Status |
|:--------|:-------------:|:-------|
| MSA-INFRA-001 (ReplanGovernor UtcNow) | TSK-0371 | Backlog |
| MSA-WEB-006 (AgentRuntime dead code) | TSK-0369 | Backlog |
| MSA-LLM-003 (MaxTokens) | TSK-0370 | Backlog |
| MSA-MEM-001/002 (memory error handling) | TSK-0374 | Backlog |
| MSA-PLAN-001 (over-mining) | Part of Wave D scope | Planned |
| WorldModel inventory wiring | TSK-0348 | Done (Wave B) |

## Roadmap Updates

The findings from this audit have been added to the Sprint 60 roadmap (Wave F — Post-Wave-C Audit Fixes) and Sprint 61 planning. See `Data/Pages/roadmap.md` for the updated task assignments.

### Sprint 60 Wave F — Post-Wave-C Audit Fixes (NEW)

| Task | Priority | Summary |
|:-----|:--------:|:--------|
| TSK-0387 | **Critical** | sendEvent crash guard for Node.js process |
| TSK-0388 | **Critical** | unhandledRejection/uncaughtException handlers |
| TSK-0389 | **Critical** | Node.js pipe buffer hang fix |
| TSK-0390 | **Critical** | LLM prompt injection guard |
| TSK-0391 | **Critical** | CreativeProvider ESM/CJS regression fix |
| TSK-0392 | High | ToolResult contradictory Success/Outcome fix |
| TSK-0393 | High | ActionData mutable dictionaries fix |
| TSK-0394 | High | ITimeProvider injection batch |
| TSK-0395 | High | BlockBelowChangedEvent WebSocketBridge wiring |
| TSK-0396 | High | JS events missing C# ParseEvent handlers |
| TSK-0397 | High | GatherItemDecompose over-mining fix |
| TSK-0398 | High | MineWoodDecompose over-mining fix |
| TSK-0399 | High | Gemini API key move to header |
| TSK-0400 | High | Cloud providers MaxTokens config respect |
| TSK-0401 | High | Memory gateway error handling |
| TSK-0402 | High | ABS silent catch blocks |
| TSK-0403 | High | SignalR push log level |
| TSK-0404 | High | DashboardPublisherImpl wire or remove |
| TSK-0405 | High | goto() timeout protection for remaining calls |
| TSK-0406 | High | craft/smelt stop request guard |
| TSK-0407 | High | BuildGoal.Id property |
| TSK-0408 | High | Chat command rate limiting |

### Sprint 61 Candidate Additions

These lower-severity findings are deferred to Sprint 61:

| Task | Priority | Summary |
|:-----|:--------:|:--------|
| WorldState.Facts mutable → IReadOnlyDictionary | High | P1 finding |
| CorrelationId Guid→string standardization | High | P1 finding |
| IGoalPrecondition enforcement in planning pipeline | High | P1 finding |
| SurviveNight decomposer implementation | High | P1 finding |
| HTTPS redirection | High | P1 finding |
| SignalR hub authentication | High | P1 finding |
| RestMemoryGateway error handling batch | High | P1 finding |
| No rate limiting on REST API | Medium | P2 finding |
| BlockState record equality fix | Medium | P2 finding |
| BlueprintExecutor null BlockId guard | Medium | P2 finding |
| UpdatePageAsync error handling | Medium | P2 finding |
| Tool InputSchema caching (11 tools) | Medium | P2 finding |
| StopState.js dead code | Medium | P2 finding |
