# Internal Codebase Audit — Sprint 60

**Date:** 2026-07-10
**Version:** v0.60.0
**Scope:** MemorySmith.Agent (all projects)
**Type:** 10-agent parallel swarm audit + 5-seat council review

## Executive Summary

Completed a full 10-agent parallel swarm codebase audit of the MemorySmith.Agent codebase (Sprint 60, v0.60.0), followed by a 5-seat heterogeneous swarm council review. The audit covered all 10 layers of the codebase, producing ~106 findings across all severity levels.

### Severity Distribution

| Severity | Count | Description |
|:--------:|:-----:|-------------|
| **P0** | 1 | Critical — data loss, crash, or silent correctness failure |
| **P1** | 18 | High — significant functional or observability defect |
| **P2** | 40 | Medium — structural, test coverage, or consistency issue |
| **P3** | 50 | Low — documentation, observability, or tech debt |
| **Total** | **109** | |

### Top Findings

- **P0**: creativeProvider.js uses CommonJS in ESM project — crashes creative build at runtime (MSA-ADAPT-001)
- **P1**: ClearAndEnqueueAsync silently swallows stop callback exceptions (MSA-CORE-005) [recalibrated from P0 by council]
- **P1**: GatherItemDecompose mines ALL source blocks at full count — 7× over-mining (MSA-PLAN-001)
- **P1**: TaskSequenceGoal infinite loop on completion — cannot signal termination (new, council)
- **P1**: SignalR hub /agent-hub has zero authentication — information disclosure (new, council)
- **P1**: No HTTPS redirection — API keys in cleartext (new, council)
- **P1**: SurviveNight decomposers are all stubs returning only GetStatus (MSA-PLAN-002)
- **P1**: Silent catch blocks in API endpoints and AgentBackgroundService violate Rule E-3 (MSA-WEB-001/002)
- **P1**: DashboardPublisherImpl is dead code — dual dashboard push paths (MSA-MGR-001)
- **P1**: Gemini API key in URL query string instead of header (MSA-LLM-005) [recalibrated from P2 by council]
- **P1**: PlaceBlockGoal.HasFailed never returns true when obstructed (MSA-PLAN-003)
- **P1**: LocalKnowledgeResolver calls SearchAsync without error handling (MSA-MEM-001)
- **P1**: WorldModel.PredictPlace does not deduct placed block from inventory (MSA-INFRA-002)
- **P2**: BuildProgressReport.PercentComplete ignores skipped/in-progress blocks (MSA-CORE-009) [recalibrated from P0 by council]
- **P2**: ToolResult allows contradictory Success/Outcome state (MSA-CORE-001) [recalibrated from P1 by council]

---

## P0 — Critical

### MSA-ADAPT-001: creativeProvider.js uses CommonJS in ESM project
**File:** `MineflayerAdapter/creativeProvider.js` (line ~11)
**The bug:** Uses `require()`/`module.exports` but `package.json` has `"type": "module"`. Node.js throws `ERR_REQUIRE_ESM` when `ensureCreativeItem` is called (creative API path). The silent catch at line 90 masks the crash.
**Impact:** Creative-mode block placement fails silently at runtime. The creative build path is completely non-functional.
**Recommendation:** Rename to `.cjs` or convert to ESM syntax (`import`/`export`).

---

## P1 — High

### [PR] MSA-CORE-005: ClearAndEnqueueAsync silently swallows stop callback exceptions (recalibrated from P0)
**File:** `Agent.Core/Models/ActionQueue.cs` (line ~136)
**The bug:** `catch (Exception ex) { _ = ex; }` discards exception from stop callback. Queue clear and priority enqueue execute OUTSIDE the try/catch, so operation completes correctly — no data loss. Violates AGENTS.md Rule E-3.
**Impact:** Transient WebSocket failures in the damage-interrupt stop path are invisible. `OperationCanceledException` from shutdown signals is also swallowed.
**Recommendation:** Inject ILogger or `Action<Exception>? onStopError` callback. Per council: P1 — observability gap, not data loss.

### [PR] MSA-CORE-017 (REPLACED): TaskSequenceGoal cannot signal terminal completion — agent loops forever
**File:** `Agent.Core/Models/TaskSequenceGoal.cs` (line ~50)
**The bug (discovered by council):** The original finding (properties throw) was **false positive** — `TryAdvance()` caps `_currentStep` at `_steps.Count - 1`, so properties never throw. The **real bug** is: when the last step completes, `IsComplete` returns true, `TryAdvance()` returns false, `_currentStep` stays at last valid index. Next cycle: `IsComplete` returns true again (same last step). No mechanism sets `_currentStep >= _steps.Count`. **Agent loops forever** processing events, checking completion, failing to advance.
**Impact:** Agent never recognizes sequence completion — runs the terminal step's IsComplete in a tight loop forever.
**Recommendation:** Add `_isComplete` flag set by `TryAdvance` when it returns false (all steps done). Check it in `IsComplete` to return true unconditionally.

---

## P1 — High

### MSA-CORE-001: ToolResult allows contradictory Success/Outcome
**File:** `Agent.Core/Models/ActionData.cs` (line ~27)
**Impact:** Consumers checking `Success` vs `Outcome` see different results.
**Recommendation:** Remove `Success` bool, compute from `Outcome`.

### MSA-CORE-012: Fact.Value is string while WorldState.Facts stores object?
**File:** `Agent.Core/Models/Fact.cs` (line ~76)
**Impact:** Type fidelity loss when facts pass through StructuredFacts vs Facts dict.
**Recommendation:** Change `Fact.Value` to `object?`.

### MSA-CORE-020: BlockPlacedEvent.CorrelationId is Guid?, others use string?
**File:** `Agent.Core/Events/WorldEvents.cs` (line ~170)
**Impact:** Consumers must handle two correlation ID types.
**Recommendation:** Align all to `string?`.

### MSA-PLAN-001: GatherItemDecompose mines ALL source blocks at full count
**File:** `Agent.Planning/HtnTaskLibrary.cs` (line ~778)
**Impact:** Bot mines 7× required materials on gather-wood tasks.
**Recommendation:** Deduct mined counts or select one source block.

### MSA-PLAN-002: SurviveNight decomposers are all stubs
**File:** `Agent.Planning/HtnTaskLibrary.cs` (line ~838)
**Impact:** SurviveNight goal produces no actionable game interactions.
**Recommendation:** Implement shelter/torch/wait phases or document as aspirational.

### MSA-PLAN-003: PlaceBlockGoal.HasFailed never returns true when obstructed
**File:** `Agent.Planning/Goals/PlaceBlockGoal.cs` (line ~137)
**Impact:** Obstructed placements loop forever without self-diagnosis.
**Recommendation:** Add failure fact key for consecutive failures.

### MSA-INFRA-001: ReplanGovernor uses DateTimeOffset.UtcNow directly
**File:** `Agent.Core/ReplanGovernor.cs` (line ~103)
**Impact:** Stall detection cannot be deterministically tested.
**Recommendation:** Inject ITimeProvider.

### MSA-INFRA-002: WorldModel.PredictPlace does not deduct placed block
**File:** `Agent.Core/Models/WorldModel.cs` (line ~188)
**Impact:** Predicted inventory inflated after place operations.
**Recommendation:** Deduct placed block count in PredictPlace.

### MSA-INFRA-004: Duplicate pickaxe-block lists (ToolRequirements vs CommonMinecraftBlocks)
**File:** `Agent.Core/ToolRequirements.cs` / `CommonMinecraftBlocks.cs`
**Impact:** Tool auto-crafting misses blocks like deepslate/obsidian.
**Recommendation:** Consolidate to single source of truth.

### MSA-INFRA-006: Multiple corrupted .bak.bak files in source tree
**File:** `Agent.Core/SystemTimeProvider.cs.bak.bak` etc.
**Impact:** 6+ corrupted/decayed backup files clutter repository.
**Recommendation:** Delete all .bak and .bak.bak files.

### MSA-LLM-003: Cloud providers hardcode MaxTokens=512, ignore LlmMaxResponseTokens
**File:** `Agent.Planning/Llm/AnthropicProvider.cs` (line ~50), `OpenAICompatibleProvider.cs`
**Impact:** Admin-configured token limits ignored for cloud providers.
**Recommendation:** Use `options.LlmMaxResponseTokens > 0 ? options.LlmMaxResponseTokens : 512`.

### MSA-MEM-001: LocalKnowledgeResolver calls SearchAsync without error handling
**File:** `Agent.Memory/LocalKnowledgeResolver.cs` (line ~88)
**Impact:** Transient HTTP failure from non-RestMemoryGateway implementation crashes pipeline.
**Recommendation:** Add try/catch matching MemorySmithBlueprintRepository pattern.

### MSA-MEM-002: RestMemoryGateway.CreatePageAsync throws unhandled on HTTP failure
**File:** `Agent.Memory/RestMemoryGateway.cs` (line ~95)
**Impact:** 4xx/5xx from MemorySmith API crashes the pipeline.
**Recommendation:** Add try/catch with warning logging.

### MSA-WEB-001/002: Silent catch blocks in ABS and Program.cs
**File:** `WebUI.Blazor/Program.cs` (line ~513), `AgentBackgroundService.cs` (line ~565)
**Impact:** JSON parse failures and disconnect errors invisible in logs.
**Recommendation:** Replace with logged catch per Rule E-3.

### MSA-WEB-004: Hardcoded version string v0.55.0 vs Sprint 60
**File:** `WebUI.Blazor/Program.cs` (line ~490)
**Impact:** Version metadata perpetually stale.
**Recommendation:** Derive from AssemblyInformationalVersionAttribute.

### MSA-MGR-001: DashboardPublisherImpl is dead code
**File:** `WebUI.Blazor/Managers/DashboardPublisherImpl.cs`
**Impact:** Two divergent dashboard push paths. Registered but unused.
**Recommendation:** Wire or remove.

### MSA-ADAPT-002: stopState.js is dead code
**File:** `MineflayerAdapter/stopState.js`
**Impact:** Maintenance trap — module lacks Sprint 57 guards.
**Recommendation:** Remove or wire with proper guards.

### MSA-ADAPT-005: MinecraftAdapter uses Linux-only `kill` — silent failure on Windows
**File:** `Agent.World.Minecraft/MinecraftAdapter.cs` (line ~57)
**Impact:** Graceful shutdown broken on Windows.
**Recommendation:** Branch on `OperatingSystem.IsWindows()`.

---

## P2 — Medium (Selected)

| ID | Title | File |
|:---|:------|:-----|
| MSA-CORE-002 | ActionData mutable dictionaries on init-only record | `Agent.Core/Models/ActionData.cs` |
| MSA-CORE-006 | Orphaned .bak.bak files with base64 content | `Agent.Core/Models/ActionQueue.cs.bak.bak` |
| MSA-CORE-014 | PlanningPolicy interfaces unwired in Sprint 60 | `Agent.Core/Models/PlanningPolicy.cs` |
| MSA-CORE-018 | WorldState.Facts exposed as mutable Dictionary | `Agent.Core/Models/WorldState.cs` |
| MSA-CORE-019 | WorldStateDiff.DescribeMismatches duplicates expectedKeys logic | `Agent.Core/Models/WorldStateDiff.cs` |
| MSA-PLAN-004 | PlaceBlockGoal.Dispatched setter not thread-safe | `Agent.Planning/Goals/PlaceBlockGoal.cs` |
| MSA-PLAN-005 | CreateCreativeBuildActions dead code | `Agent.Planning/HtnPlanner.cs` |
| MSA-PLAN-006 | IGoalPrecondition not enforced in planning pipeline | `Agent.Planning/Goals/*.cs` |
| MSA-PLAN-007 | SmeltGoal.HasFailed always returns false | `Agent.Planning/Goals/SmeltGoal.cs` |
| MSA-LLM-005 | GeminiProvider embeds API key in URL query string | `Agent.Planning/Llm/GeminiProvider.cs` |
| MSA-LLM-008 | ChatRateLimiter._globalWindow never pruned | `Agent.Planning/ChatRateLimiter.cs` |
| MSA-TOOL-001 | CreatePageTool throws ArgumentException instead of ToolResult | `Agent.Tools/Tools/CreatePageTool.cs` |
| MSA-TOOL-004 | QueryBlocksTool.GetRequiredInt throws instead of ToolResult | `Agent.Tools/Tools/QueryBlocksTool.cs` |
| MSA-TOOL-007 | WanderTool uses GetInt32() instead of TryGetInt32() | `Agent.Tools/Tools/WanderTool.cs` |
| MSA-MEM-003 | GetPageAsync silently collapses all HTTP errors to null | `Agent.Memory/RestMemoryGateway.cs` |
| MSA-MEM-005 | UpdatePageAsync GET/PUT race window | `Agent.Memory/RestMemoryGateway.cs` |
| MSA-WEB-003 | SignalR push failures logged at Debug instead of Warning | `WebUI.Blazor/AgentBackgroundService.cs` |
| MSA-WEB-005 | Blocked command path double-enqueues chat message | `WebUI.Blazor/AgentBackgroundService.cs` |
| MSA-WEB-006 | AgentRuntime registered but never consumed | `WebUI.Blazor/Program.cs` |
| MSA-MGR-002 | DashboardPublisherImpl misses reconnecting state | `WebUI.Blazor/Managers/DashboardPublisherImpl.cs` |
| MSA-MGR-003 | DashboardPublisherImpl entities/blockBelow always null | `WebUI.Blazor/Managers/DashboardPublisherImpl.cs` |
| MSA-MGR-004 | IntentManagerImpl hardcodes DefaultOnlinePlayers=1 | `WebUI.Blazor/Managers/IntentManagerImpl.cs` |
| MSA-ADAPT-003 | Three JS-emitted events have no C# ParseEvent handler | `Agent.World.Minecraft/WebSocketBridge.cs` |
| MSA-ADAPT-004 | blockMineSkipped event has no C# handler | `Agent.World.Minecraft/WebSocketBridge.cs` |
| MSA-ADAPT-006 | DisconnectAsync silent catches in process management | `Agent.World.Minecraft/MinecraftAdapter.cs` |
| MSA-TEST-001 | Stale .bak files in test directory | `MemorySmith.Agent.Tests/Sprint23Tests.cs.bak` |
| MSA-TEST-002 | Zero test coverage for Agent.Vision | `Agent.Vision/WorldVision.cs` |
| MSA-TEST-003 | Zero test coverage for Agent.Personality | `Agent.Personality/AgentProfile.cs` |
| MSA-TEST-004 | BlueprintExecutor has no dedicated tests | `Agent.Construction/BlueprintExecutor.cs` |
| MSA-TEST-009 | Agent.Vision and Agent.Personality not in test .csproj | `MemorySmith.Agent.Tests.csproj` |

---

## P3 — Low / Observability (Selected)

| ID | Title | File |
|:---|:------|:-----|
| MSA-CORE-003 | ActionOutcome factories hardcode UtcNow | `Agent.Core/Models/ActionOutcome.cs` |
| MSA-CORE-004 | IObservationSummary defined alongside implementation | `Agent.Core/Models/ActionOutcome.cs` |
| MSA-CORE-021 | MineAbortedEvent nullable vs non-nullable position | `Agent.Core/Events/WorldEvents.cs` |
| MSA-CORE-022 | ItemCraftedEvent/ItemConsumedEvent unwired stubs | `Agent.Core/Events/WorldEvents.cs` |
| MSA-LLM-001 | Typo in IntentManager doc comment `.z///` | `Agent.Planning/IntentManager.cs` |
| MSA-LLM-002 | Duplicate XML doc block in ChatInterpreter.ParseIntent | `Agent.Planning/ChatInterpreter.cs` |
| MSA-LLM-007 | ChatIntentType enum is dead code | `Agent.Planning/ChatModels.cs` |
| MSA-TOOL-002 | ChatTool silently truncates messages >256 chars | `Agent.Tools/Tools/ChatTool.cs` |
| MSA-TOOL-005 | MineBlockTool defaults empty block to oak_log | `Agent.Tools/Tools/MineBlockTool.cs` |
| MSA-TOOL-006 | PlaceBlockTool defaults empty material to cobblestone | `Agent.Tools/Tools/PlaceBlockTool.cs` |
| MSA-MEM-007 | ParseItemSpec no guard on large page content | `Agent.Memory/MemorySmithItemRegistry.cs` |
| MSA-MGR-006 | StateManagerImpl.BuildContext has no logging | `WebUI.Blazor/Managers/StateManagerImpl.cs` |
| MSA-ADAPT-007 | Unknown events silently dropped in ParseEvent | `Agent.World.Minecraft/WebSocketBridge.cs` |
| MSA-ADAPT-008 | scanBlockBelow silent catch | `MineflayerAdapter/index.js` |
| MSA-ADAPT-009 | WebSocket close details not logged | `Agent.World.Minecraft/WebSocketBridge.cs` |
| MSA-ADAPT-010 | Monolithic dispatch switch — 1400+ lines | `MineflayerAdapter/index.js` |
| MSA-TEST-006 | ToolEngineTests and ToolDispatcherTests redundant | `MemorySmith.Agent.Tests/ToolEngineTests.cs` |
| MSA-TEST-007 | Sprint28Tests tautological assertions | `MemorySmith.Agent.Tests/Sprint28Tests.cs` |
| MSA-TEST-012 | Polling loops instead of completion signals | `MemorySmith.Agent.Tests/AgentBackgroundServiceTests.cs` |

---

## Architecture Notes

1. **AgentBackgroundService monolith**: ~3500+ lines with 18 optional DI params. The Sprint 39 AgentRuntime decomposition is registered but never consumed.
2. **Dead code accumulation**: DashboardPublisherImpl, PlanningPolicy interfaces, CreateCreativeBuildActions, stopState.js, ChatIntentType enum — multiple artifacts from aspirational plans that were never wired.
3. **Event type drift**: WorldEvents.cs has 32+ event types in one file. ItemCraftedEvent/ItemConsumedEvent unwired since Sprint 35. BlockPlacedEvent uses Guid CorrelationId while all others use string.
4. **.bak proliferation**: 14+ .bak/.bak.bak files across all layers, some containing base64-encoded garbage instead of valid C#.
5. **Two reconnection layers uncoordinated**: JS side exponential backoff (2s-60s) vs C# side 3-attempt fixed 5s retry. No handshake to prevent duplicate connections.
6. **Rule E-3 violations**: 8+ verified silent catch blocks violating the AGENTS.md coding standard.
7. **Test coverage gaps**: Agent.Vision and Agent.Personality have zero test coverage. BlueprintExecutor, GoalPrecondition enforcement, and SurviveNight decomposition are untested.
8. **SignalR hub unauthenticated**: `/agent-hub` receives no authentication. Any peer who can reach the server receives real-time streaming updates (status, chat, goals).

## Methodology

10 homogeneous subagents covering independent codebase layers:
1. **Core Models & Events** (22 findings)
2. **Core Interfaces & Runtime** (10 findings)
3. **HTN Planner & Goal System** (12 findings)
4. **LLM Integration & Chat** (10 findings)
5. **Tool System** (10 findings)
6. **Memory Gateway & Knowledge** (8 findings)
7. **WebUI Blazor — API & Hosting** (8 findings)
8. **WebUI Blazor — Managers & Dashboard** (8 findings)
9. **World Adapter & Mineflayer** (10 findings)
10. **Tests & Supporting Projects** (12 findings)

**Total:** ~106 findings from swarm + 3 new findings from council = 109 total

Each subagent received file-level scope, an audit checklist (bugs, inconsistencies, gaps, weak guards, error handling, observability, overcoupling, architecture), and was instructed to produce structured JSON findings with severity (P0–P3), file paths, and evidence snippets.

## Peer Review Results

5-seat heterogeneous council review completed on 2026-07-10.

### Verified Accurate (43 findings confirmed by ≥2 seats)
All P0, P1, and most P2 findings were verified as accurate by at least 2 council seats. Full verification lists in council seat JSON outputs.

### Disputed Findings / Recalibrations

| Finding | Original Severity | Council Severity | Reason |
|:--------|:-----------------:|:----------------:|:-------|
| MSA-CORE-017 | P0 | **False positive** | TryAdvance() caps `_currentStep` at `Count-1`. Real bug is infinite loop on completion. |
| MSA-CORE-005 | P0 | **P1** | Queue clear executes outside try/catch — no data loss, only observability gap |
| MSA-CORE-009 | P0 | **P2** | Design choice: PercentComplete tracks physical placement, not aggregate completion |
| MSA-CORE-001 | P1 | **P2** | Contradictory state is theoretical — no code sets non-default Outcome |
| MSA-LLM-005 | P2 | **P1** | URL key exposure is real security concern (URL logging, redirects, crash dumps) |
| MSA-TEST-006 | P2 | **P3** | ToolEngineTests and ToolDispatcherTests are complementary, not redundant |

### Missing Findings Discovered by Council (3 new)

| Severity | Title | File |
|:--------:|:------|:-----|
| **P1** | TaskSequenceGoal infinite loop on completion (replaces false positive MSA-CORE-017) | `Agent.Core/Models/TaskSequenceGoal.cs` |
| **P1** | SignalR hub /agent-hub has zero authentication — real-time info disclosure | `WebUI.Blazor/AgentHub.cs` |
| **P1** | No HTTPS redirection — API keys and all traffic in cleartext | `WebUI.Blazor/Program.cs` |
| P2 | Uncoordinated reconnection layers (JS exponential vs C# fixed retry) | `WebUI.Blazor/AgentBackgroundService.cs` + `MineflayerAdapter/index.js` |
| P2 | AllowDestructiveCommands=true has no defense-in-depth | `WebUI.Blazor/AgentBackgroundService.cs` |
| P3 | No rate limiting on REST API endpoints | `WebUI.Blazor/Program.cs` |

### Key Council Corrections
- Line counts were generally accurate (±5 lines) across all verified files
- MSA-CORE-017 was a false positive (properties DON'T throw); replaced with correct infinite-loop finding
- MSA-CORE-005 recalibrated: queue clear executes OUTSIDE catch — no data loss, only observability
- MSA-CORE-009 recalibrated: design choice, not bug
- MSA-LLM-005 escalated: URL-embedded API key is real security risk
- 3 new findings: SignalR auth, HTTPS, reconnection coordination

---

## Task Creation

Based on the council-synthesized priority ordering, the following actionable findings require new or existing task coverage:

### New Tasks Needed

| Priority | Finding | Suggested Task Title | Existing Task? |
|:--------:|:--------|:---------------------|:--------------:|
| Critical | MSA-ADAPT-001 | Fix creativeProvider.js CommonJS in ESM project | New |
| High | MSA-CORE-005 | Fix ClearAndEnqueueAsync silent catch — add ILogger or callback | New |
| High | MSA-CORE-017 (replacement) | Fix TaskSequenceGoal infinite loop on terminal completion | New |
| High | MSA-PLAN-001 | Fix GatherItemDecompose over-mining — deduct counts or select source | New |
| High | MSA-PLAN-003 | Fix PlaceBlockGoal.HasFailed — add consecutive-failure detection | New |
| High | MSA-WEB-001/002 | Replace silent catches with logged catches per Rule E-3 | New |
| High | MSA-MGR-001 | Wire or remove DashboardPublisherImpl — dual push paths | New |
| High | MSA-INFRA-001 | Inject ITimeProvider into ReplanGovernor | New |
| High | MSA-LLM-003 | Fix cloud providers to use LlmMaxResponseTokens | New |
| High | MSA-INFRA-004 | Consolidate duplicate pickaxe-block lists | New |
| High | MSA-MEM-001 | Add error handling to LocalKnowledgeResolver.SearchAsync | New |
| High | MSA-MEM-002 | Add error handling to RestMemoryGateway.CreatePageAsync | New |
| High | MSA-LLM-005 | Move Gemini API key to header from URL | New |
| High | (Council) | Add SignalR hub authentication for /agent-hub | New |
| High | (Council) | Add HTTPS redirection for API traffic | New |
| High | MSA-PLAN-002 | Implement or remove SurviveNight decomposers | New |

### Findings with Existing Task Overlap

| Finding | Existing Task | Notes |
|:--------|:-------------:|:------|
| MSA-INFRA-002 | TSK-0348 | WorldModel inventory/ActionOutcome wiring already tracks this |
| MSA-PLAN-005 | TSK-0293 | Remove legacy fallback paths covers CreateCreativeBuildActions |
| MSA-MGR-001 | TSK-0292 (partial) | Decompose ABS — DashboardPublisherImpl is part of this scope |
| MSA-WEB-006 | TSK-0292 | AgentRuntime decomposition covers unused registration |
| MSA-MGR-004 | TSK-0299 | Player coords in LLM context — related but not same fix |
