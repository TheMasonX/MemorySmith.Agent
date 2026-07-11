# Internal Codebase Audit — Sprint 60 (Post-Wave-A)

**Date:** 2026-07-11
**Scope:** MemorySmith.Agent (all layers)
**Type:** 10-agent homogeneous swarm + 5-chair heterogeneous council review
**Commit:** `413b8c3` (Sprint 60 Wave A — high-ROI fixes from Round 3 audit)

## Executive Summary

A 10-agent swarm swept all layers of MemorySmith.Agent, followed by a 5-chair council review (Architecture & Design, Runtime & Debugging, Safety & Security, Completeness & QA, Sprint Impact & Prioritization). The swarm produced **109 raw findings**. After council recalibration (6 removed/merged, 8 upgrades, 12 downgrades, 7 new architectural findings), the final tally is:

| Severity | Count | Description |
|:--------:|:-----:|:------------|
| **P0 — Critical** | 5 | Active data corruption, security, or operational integrity |
| **P1 — High** | 18 | Functional correctness, security posture, or observability gaps |
| **P2 — Medium** | 42 | Maintainability, test coverage, or design debt |
| **P3 — Low** | 38 | Documentation, hygiene, or micro-optimizations |
| **Total** | **103** | After council recalibration |

**4 of 5 P0 findings were already fixed in Sprint 60 Wave A** (creativeProvider.js→.cjs, TaskSequenceGoal infinite loop, .bak cleanup, version derivation, ClearAndEnqueueAsync onStopError). **1 new P0** (runtime decomposition dead code) was discovered by the council.

**Key insight:** The single largest source of findings (~15%) is the Sprint 37+ runtime decomposition — 6 interfaces, 6 stub implementations, and an `AgentRuntime` record that are DI-registered but completely unused by `AgentBackgroundService`. This architectural scaffolding generates 12+ findings across all categories.

---

## Severity Distribution (Post-Council)

| Category | P0 | P1 | P2 | P3 |
|:---------|:--:|:--:|:--:|:--:|
| Agent.Core — Models/Events | — | 1 | 8 | 8 |
| Agent.Core — Runtime/Projector | 1 | 1 | 5 | 5 |
| Agent.Planning — Goals/Decomposers | — | 3 | 5 | 4 |
| Agent.Planning — Planner/LLM | — | 2 | 6 | 6 |
| Agent.Tools — ToolDispatcher | — | 1 | 5 | 4 |
| Agent.World.Minecraft — Bridge/Adapter | 2 | 2 | 6 | 3 |
| MineflayerAdapter/ — Node.js | — | 5 | 4 | 3 |
| WebUI.Blazor — AgentBackgroundService | — | — | 4 | 4 |
| WebUI.Blazor — Program.cs/DI/Dashboard | 1 | 1 | 5 | 5 |
| Infrastructure/Tests/Config | — | 2 | 4 | 5 |

---

## P0 — Critical

### P0-001: Runtime Decomposition Dead Code (Council-Discovered)
**Original:** MSA-RT-006 (was P1) | **Council Upgrade → P0**
**File:** `Agent.Core/Runtime/AgentRuntime.cs`, `WebUI.Blazor/Managers/*Impl.cs`, `WebUI.Blazor/Program.cs`
**The bug:** The entire Sprint 37+ runtime decomposition (6 interfaces: `IIntentManager`, `IPlanningManager`, `IExecutionManager`, `IRecoveryManager`, `IStateManager`, `IDashboardPublisher` + 6 stub implementations + `AgentRuntime` record + DI registrations) is **completely unused** by `AgentBackgroundService`. ABS receives all 15+ dependencies through its primary constructor directly. The 6 `Impl` classes exist but ABS doesn't reference them. `DashboardPublisherImpl` hardcodes `QueuedActions: 0` (WBLZ-005), `IntentManagerImpl` hardcodes `OnlinePlayers: 1` (WBLZ-006), and `RecoveryManagerImpl` has been a stub returning `false` since Sprint 39.
**Impact:** ~800 lines of shipped, maintained, tested, and DI-registered code that does nothing. Generates 12+ maintenance-burden findings across all categories. Creates a maintenance trap where new contributors may wire into the decomposition without realizing ABS doesn't consume it.
**Recommendation:** Delete `AgentRuntime` record, `IRecoveryManager`, `IPlanningManager`, `IExecutionManager`, their stub implementations, and their DI registrations. Consolidate to only managers that have real value (`IIntentManager`, `IDashboardPublisher`, `IStateManager`). This removes 12+ findings in one commit.

### P0-002: WebSocketBridge `_ws` Data Race (Council Upgrade)
**Original:** WSB-002 (was P1) | **Council Upgrade → P0**
**File:** `Agent.World.Minecraft/WebSocketBridge.cs` (lines ~107, ~194)
**The bug:** `_ws` is mutated without synchronization. `SendAsync` reads `_ws` to check `IsOpen` while the reconnect loop in `RunReceiveLoopWithRetryAsync` disposes and replaces `_ws`. No lock — send can race with reconnect, reading a disposed socket or writing to a closing one. This is a memory-safety-level structured concurrency failure.
**Impact:** Corrupt WebSocket frames, silent data loss, or `ObjectDisposedException` during concurrent send/reconnect cycles.
**Recommendation:** Guard `_ws` access with a lock or use `ImmutableInterlocked` for atomic swap.

### P0-003: Orphaned Node.js Processes from Double-Connect (Council Upgrade)
**Original:** MA-003 (was P1) | **Council Upgrade → P0**
**File:** `Agent.World.Minecraft/MinecraftAdapter.cs` (line ~33)
**The bug:** `ConnectAsync` has no guard against existing connection. If called twice, `_nodeProcess` and `_bridge` are replaced without disposing the previous instances. Orphaned Node.js processes consume GPU/memory indefinitely. MA-006 (Dispose doesn't call DisconnectAsync) compounds this — every graceful shutdown also leaks.
**Impact:** A single reconnect storm spawns zombie processes that accumulate memory and GPU resources. In production-adjacent deployments, this is an operational integrity failure.
**Recommendation:** Add a `DisconnectAsync()` call at the start of `ConnectAsync` to cleanly close any existing connection before creating a new one.

### P0-004: Zero Vulnerability Defense in CI Pipeline (Council Upgrade)
**Original:** INFRA-002/003 + BUILD-001 (were P1) | **Council Upgrade → P0** (combined)
**File:** `.github/workflows/ci.yml`, `Directory.Build.props`
**The bug:** No `dotnet list package --vulnerable` or `--deprecated` step in CI. Simultaneously, `NU1903` is explicitly exempted from `TreatWarningsAsErrors` in `Directory.Build.props`, meaning all NuGet vulnerability advisories pass silently. Combined, the project has **zero vulnerability defense** in the build pipeline.
**Impact:** Vulnerable transitive dependencies (like the Sprint 51 SQLitePCLRaw CVE incident) will not be caught by CI. The Sprint 51 incident demonstrated this exact failure mode.
**Recommendation:** Add `dotnet list package --vulnerable --include-transitive` and `--deprecated` steps to CI. Remove the NU1903 exemption or replace it with a documented, time-bounded allowlist in `Data/Pages/policies/`.

### ~~P0-005: SignalR Hub No Authentication~~ (Council Upgrade)
**Original:** WBLZ-002 (was P1) | **Council Upgrade → P0**
**File:** `WebUI.Blazor/AgentHub.cs`, `WebUI.Blazor/Program.cs`
**The bug:** `AgentHub` at `/agent-hub` has no `[Authorize]` attribute. The `ApiKeyMiddleware` only gates `/api/*` routes. Anyone on the network can connect to `/agent-hub` and receive real-time streaming of agent position, health, inventory, goals, nearby entities (including players), and chat messages.
**Impact:** Any network peer can surveil the agent and players interacting with it. This is a privacy and information-disclosure integrity failure.
**Recommendation:** Deferred to Sprint 61+ (TSK-0181). Requires SignalR auth middleware.

---

## P1 — High (Selected Highlights)

### P1-001: GatherItemDecompose 7× Over-Mining
**File:** `Agent.Planning/HtnTaskLibrary.cs` (DecomposeGatherItem)
**The bug:** The gather-item decomposition adds 7 MineBlock actions per source block type without deducting expected yield from inventory projections. Mining 7 oak_logs when only 3 are needed wastes agent time and inventory space.
**Recommendation:** Track per-block expected yield and adjust target count dynamically.

### P1-002: PlaceBlockGoal.HasFailed Never True
**File:** `Agent.Planning/Goals/PlaceBlockGoal.cs`
**The bug:** `HasFailed` always returns `false` — there is no failure detection mechanism. If placement is obstructed forever, the goal never fails, creating an infinite loop.
**Recommendation:** Add consecutive-failure detection to `PlaceBlockGoal.HasFailed`.

### P1-003: All 9+ goto() Calls Lack Timeouts
**File:** `MineflayerAdapter/index.js` (7 locations)
**The bug:** Every `bot.pathfinder.goto()` call has zero timeout protection. A stuck pathfinder computation blocks the entire dispatch queue indefinitely. The `findReachableBlock` path has the correct pattern (`getPathTo` with `timeout` option) but it's not replicated.
**Impact:** Already tracked as TSK-0159 (Backlog, Critical).
**Recommendation:** Add `Promise.race()` with configurable timeout to all goto() calls.

### P1-004: craft/smelt Never Check `_stopRequested`
**File:** `MineflayerAdapter/index.js` (craft, smelt handlers)
**The bug:** The craft and smelt action handlers never check `_stopRequested`. Emergency stop during crafting/smelting (multi-second operations) cannot abort. Neither handler resets `_stopRequested = false` unlike `mine`, `place`, `wander`, and `findFlatArea`.
**Recommendation:** Add `_stopRequested` checks and reset to craft/smelt handlers.

### P1-005: playerCollect Handler Uses Wrong Fallback Chain (Worse Than Reported)
**File:** `MineflayerAdapter/index.js` (playerCollect handler)
**The bug:** The handler uses `entity?.metadata?.name ?? entity?.displayName ?? 'unknown'` but AGENTS.md documents the version-safe fallback as `entity?.metadata?.find(m => m?.value?.name)?.value?.name ?? entity?.name ?? 'unknown'`. The documented pattern correctly handles Mineflayer's `MetadataPropertyValue` wrapper (where `metadata` is an array of `{ key, value }` objects, not a direct property accessor). Since `metadata` is an array, `.name` on an array always returns `undefined`, so item names **always** resolve to `'unknown'`.
**Impact:** Every item collected by other players in multiplayer is recorded as `'unknown'`. Inventory tracking is silently broken for multiplayer scenarios.
**Recommendation:** Replace the fallback chain with the version-safe pattern documented in AGENTS.md.

### P1-006: connectBot() Leaks Event Listeners on Reconnect
**File:** `MineflayerAdapter/index.js` (connectBot + registerBotEventHandlers)
**The bug:** `connectBot()` creates a new bot and calls `registerBotEventHandlers()` without removing old handlers. On reconnect, old bot event listeners accumulate. Each reconnect adds duplicate `end`, `spawn`, `health`, `move`, `death`, `kicked`, `error`, `game`, `playerCollect`, `physicsTick` (×2), `chat` listeners — each firing independently on subsequent events.
**Impact:** After N reconnections, each event fires N times, causing N duplicate C# events, N duplicate inventory updates, N duplicate entity scans. Performance degrades linearly with reconnect count.
**Recommendation:** Remove old listeners before registering new ones, or use `bot.removeAllListeners()` before re-registration.

### P1-007: `/api/about` Version Hardcoded
**File:** `WebUI.Blazor/Program.cs` (line ~517)
**The bug:** Sprint 60 Wave A fixed the startup log to derive version from `FileVersionInfo`, but the `/api/about` REST endpoint still hardcodes `Version = "0.55.0"`. Any consumer reading the version endpoint gets Sprint 55 data.
**Recommendation:** Derive version from assembly metadata in the endpoint (same pattern as the startup log fix).

### P1-008: 9 CI Gaps (No Vulnerability/Deprecated Scanning, No Coverage Report, etc.)
**File:** `.github/workflows/ci.yml`
**Details:** CI collects coverage but never reports it; no `dotnet list package --vulnerable`; no `--deprecated`; no `Verify-AboutDeps.ps1`; no NuGet caching; no secret scanning; no .bak file gate.
**Recommendation:** Implement TSK-0144, TSK-0145, TSK-0349 as a consolidated CI hardening wave.

---

## P2 — Medium (Summary by Area)

| Area | Key Findings |
|:-----|:------------|
| **Agent.Core** | PredictCraft doesn't deduct ingredients (CORE-007); Missing deepslate ore smelt mappings (CORE-008); ToolRequirements missing golden tier (CORE-012); ActionRegistry no sync (CORE-015); BuildProgressReport vs StructuredFacts divergence (CORE-023) |
| **Agent.Planning** | BraceRegex greedy match corrupts multi-block LLM responses (PLN-001); IntentManager/Builder silently return null on failure (PLN-002, PLN-009); All LLM providers silently discard non-200s (PLN-013); ChatHistory GC pressure (PLN-019) |
| **Agent.Tools** | ActionProtocol.FindReachableBlock defined but no ITool (TOOL-012); CreatePageTool throws instead of returning ToolResult (TOOL-017); No tool-level timeout (TOOL-019) |
| **WebSocketBridge** | No max incoming message size (WSB-004); Dispose bypasses graceful close (WSB-005); Fire-and-forget with no error routing (WSB-007); Node stderr pipe deadlock (MA-004); Round-robin event distribution (MA-005) |
| **MineflayerAdapter** | scanNearbyEntities uses floored Y (MF-009); System message patterns incomplete (MF-010); /give fallback reports success without verifying (MF-011); 3 empty catch blocks (MF-012/013); craft no timeout (MF-014); Dev-mode connection race (MF-015) |
| **AgentBackgroundService** | 3 silent catch blocks (ABS-001/002); _blocksPlacedThisCycle++ race (ABS-004); _consecutiveFailures race (ABS-005); SetGoal partial init risk (ABS-018) |
| **Dashboard/DI** | Dual SignalR write surface (WBLZ-003); Event name drift (WBLZ-004); Hardcoded blueprints (WBLZ-007); Stub connect/stop endpoints (WBLZ-008); LiveLogBuffer ordering fragility (WBLZ-009) |
| **Infrastructure** | CI coverage not published (INFRA-001); No NuGet cache (INFRA-005); 35/354 task records missing timestamp fields (TASK-003); No regression test for _isComplete fix (TEST-001); No CI integration test (TEST-002) |

---

## P3 — Low / Observability (Summary)

38 findings across documentation staleness (README v0.55.0→v0.60.0), stale Python scripts in `Scripts/`, hardcoded dashboard version, CORS not configured, minor logging gaps, convention violations, and micro-optimizations. See the full finding tables in the council chair reports for complete details.

---

## Architecture Notes

### Cross-Cutting Themes (Identified by Council)

1. **Fire-and-forget concurrency without error routing** — Found in WSB-007, ABS-001/002/003, PLN-016, MA-001/002, WBLZ-018. The pattern is `_ = Task.Run(...)` or `try { } catch { /* silent */ }`. This is the single most recurrent finding across all layers.

2. **Dead code from Sprint 37+ runtime decomposition** — MSA-RT-001 through MSA-RT-006, WBLZ-003/005/006/015 are symptoms of the same architectural decision. ~800 lines of dead interfaces, dead implementations, and dead DI registrations generate 12+ findings.

3. **Rule E-3 violations are pervasive** — Despite being documented in AGENTS.md, silent catch blocks appear in: WSB-003, MA-001/002, MF-012/013, PLN-016, ABS-001/002/003, WBLZ-018. The rule is known but not enforced by code review or static analysis.

4. **No end-to-end integration test** — No test covers the full pipeline (chat → interpretation → goal → plan → dispatch → adapter → event → projector → dashboard). The WebSocketBridge has zero unit tests despite hosting P0-level bugs.

5. **Configuration/README drift** — README-001/002, WBLZ-001, WBLZ-014 are all instances of stale version strings. No version-bump automation or post-release audit step.

### Design Concerns (from Architecture Chair)

**Over-engineering:** The Sprint 37+ runtime decomposition (6 interfaces + 6 impls + record + DI registration) is ~800 lines of unused architectural scaffolding. A lighter approach would have been to document interfaces and implement incrementally.

**Under-engineering:** WorldState is a monolith record with 10+ fields that has grown organically since Sprint 3. No health check/liveness endpoint exists. WebSocketBridge has zero tests.

**Abstraction Leaks:** DashboardPublisherImpl in WebUI.Blazor references Agent.Core types AND SignalR. AgentRuntime lives in Agent.Core but its implementations are in WebUI.Blazor, requiring cross-project reads to understand the architecture.

**Council-recommended Sprint 61 pattern:**
1. Kill the dead runtime decomposition
2. Supervise the adapter process with a `ProcessSupervisor`
3. Add pre-dispatch input validation layer
4. Add `/healthz` + `/readyz` endpoints
5. Add end-to-end integration test

---

## Methodology

- **10-agent homogeneous swarm (Branch A):** Each agent swept one partition independently with structured output format.
- **5-chair heterogeneous council (Branch B):** Each chair received all findings with a specific lens.
- **Verification:** Chair 2 (Runtime & Debugging) verified all P0/P1 claims against actual source code.
- **Recalibration:** Chair 1 (Architecture & Design) provided severity calibration with 74 findings High/Medium confidence, 3 removed, 8 upgrades, 12 downgrades.
- **Output format:** This unified report, plus council chair reports as appendices.

---

## Peer Review Results

| Reviewer | Confidence | Key Recalibrations |
|----------|:----------:|:-------------------|
| Architecture & Design | High (51), Medium (18), Low (5) | 3 findings removed, 8 upgraded, 12 downgraded; 7 missing architectural findings |
| Runtime & Debugging | Verified all P0/P1 | MSA-MF-006 worse than described; WSB-001 merged into MF-002; MF-007 downgraded to P2 |
| Safety & Security | Confirmed all findings | WBLZ-002 escalated to P0; identified missing CSRF/SecurityHeaders |
| Completeness & QA | 96% | 11/22 P0/P1 untracked (50% gap); 42/354 task records null timestamps |
| Sprint Impact | Full prioritization | 11 findings for S60 (~12.75h), 6 deferred, 12 quick wins |

### Findings Removed After Council Review
- CORE-002/003/004: Telemetry events ARE stored as facts via the outer `Apply` default branch
- PLN-018: StreamWriter.Dispose on FileStream is not a real deadlock pattern
- WSB-001: Merged into MSA-MF-002 (symptom of reconnect counter reset)

### No Existing Task Tracking (Findings Needing New Tasks)
See §Task Synthesis below.

---

## Task Synthesis

The following findings have NO existing task coverage and need new task records:

| Priority | Finding ID | Title | Sprint |
|:--------:|:-----------|:------|:------:|
| **P0** | P0-001 | Remove dead runtime decomposition (6 interfaces, 6 stubs, AgentRuntime) | S61 |
| **P0** | P0-002 | Fix WebSocketBridge _ws data race (lock or ImmutableInterlocked) | S60 |
| **P0** | P0-003 | Fix MinecraftAdapter double-connect guard (orphaned Node processes) | S60 |
| **P0** | P0-004 | Add vulnerability/deprecated scanning to CI, remove NU1903 exemption | S60 |
| **P1** | P1-001 | Fix GatherItemDecompose over-mining — deduct counts per source block | S60 |
| **P1** | P1-002 | Add failure detection to PlaceBlockGoal.HasFailed | S60 |
| **P1** | P1-005 | Fix playerCollect fallback chain (always returns 'unknown') | S60 |
| **P1** | P1-006 | Fix connectBot event listener leak on reconnect | S60 |
| **P1** | P1-007 | Fix /api/about hardcoded version string | S60 |
| **P1** | P1-008 | Add HTTPS redirection middleware | S60 |
| **P1** | — | Fix ReplanGovernor to use ITimeProvider instead of UtcNow directly | S60 |
| **P1** | — | Add error handling to LocalKnowledgeResolver and RestMemoryGateway | S60 |
| **P2** | — | Delete stale Python scripts from Scripts/ directory | S60 |
| **P2** | — | Add WebSocketBridge unit tests | S61 |
| **P2** | — | Fix task record null timestamps (42 records) | S60 |

### Existing Tasks Covering Audit Findings

| Finding | Existing Task | Status |
|:--------|:-------------|:-------|
| P0-001 (partial: DashboardPublisher) | TSK-0292 (partial) | Backlog |
| P0-005 (SignalR auth) | TSK-0181 | Backlog |
| P1-003 (goto timeouts) | TSK-0159 | Backlog |
| P1-004 (craft/smelt stop) | New (covered by this audit's MF-004) | — |
| P1-008 partial (secret scanning) | TSK-0349 | Ready |
| WorldModel PredictPlace | TSK-0348 | InProgress |
| Legacy fallback removal | TSK-0293 | Ready |
| LLM provider tests | TSK-0191 | Backlog |
| Gemini key URL→header | TSK-0180 | Backlog |

---

## Appendix

Full council chair reports available on request:
- Chair 1: Architecture & Design — severity recalibrations, missing findings, design concerns
- Chair 2: Runtime & Debugging — verified P0/P1 claims against source, new cross-cutting observations
- Chair 3: Safety & Security — confirmed all security findings, identified CSRF/SecurityHeaders gaps
- Chair 4: Completeness & QA — task mapping, test coverage gaps, schema drift analysis
- Chair 5: Sprint Impact — 11 findings for S60 (~12.75h), 6 deferred, 12 quick wins
