# Internal Codebase Audit — Sprint 60 Post-Wave-C

**Date:** 2026-07-11
**Version:** v0.60.0 (commit `f350daf`)
**Scope:** MemorySmith.Agent (all layers, post-Wave-C)
**Type:** 10-agent homogeneous swarm + 5-chair heterogeneous council review
**Previous audit:** `codebase-audit-20260711-10agent-swarm-5chair-council.md` (Wave A baseline)

## Executive Summary

A 10-agent swarm swept all layers of MemorySmith.Agent followed by a 5-chair council peer review (Architecture & Design, Runtime & Debugging, Safety & Security, Completeness & QA, Sprint Impact & Prioritization). The swarm produced **106 raw findings**. After council recalibration (2 removed as false positives, 17 merged into 7 groups, 15 severity changes, 6 new architectural findings, 3 security escalations to P0, 2 security gaps identified), the final tally is:

| Severity | Count | Description |
|:--------:|:-----:|:------------|
| **P0 — Critical** | 5 | Active crash, hang, or data corruption risk |
| **P1 — High** | 14 | Functional correctness, security posture, or observability gaps |
| **P2 — Medium** | 28 | Maintainability, test coverage, or design debt |
| **P3 — Low** | 32 | Documentation, hygiene, or micro-optimizations |
| **Removed** | 2 | False positives identified by council |
| **Total** | **79** | After council recalibration + merges |

### Key Themes

1. **The Node.js subprocess can crash or hang at any time** — `sendEvent()` lacks try/catch (P0, escalated by council), no `unhandledRejection`/`uncaughtException` handlers (P0, escalated), and unread stdout/stderr pipes will fill and block the process (P0, escalated). These three gaps together mean the adapter has no crash guard.

2. **Creative provisioning is still broken** — `creativeProvider.cjs` cannot `require()` ESM `logger.js` (P0). The Wave A rename (`.js`→`.cjs`) fixed the import path from `index.js` but broke the internal `require('./logger')`. Creative build placement remains non-functional.

3. **`ITimeProvider` adoption stalled** — 3+ core components (`ReplanGovernor`, `WorldModel`, `ActionOutcome` factories) hardcode `DateTimeOffset.UtcNow` despite `ITimeProvider` being available since Sprint 27. Time-dependent behavior is untestable.

4. **Goal `HasFailed` is architecturally dead** — 5 of 7 goals use `goal:{Type}:failed` fact keys never written by any code path. Only `PlaceBlockGoal` (inventory check) and `SurviveNightGoal` (health check) have live `HasFailed` logic, and `PlaceBlockGoal`'s is buggy (fires on completion, not failure).

5. **Dashboard Contracts namespace is dead code** — 9 record types (180 lines) shipped but never instantiated anywhere.

### Previous Audit Status

| Wave | Status | Key Fixes |
|:----:|:------:|:----------|
| **Wave A** | ✅ Complete | creativeProvider.js→.cjs, TaskSequenceGoal _isComplete, .bak cleanup, version derivation, onStopError callback |
| **Wave B** | ✅ Complete | ExecutionManager JSON round-trip, replan flooding fix, WorldModel inventory/ActionOutcome wiring |
| **Wave C** | ✅ Complete | Antiforgery filter, secret scanning infra, goto timeout, search scoring investigation |
| **This Audit** | **NEW** | Post-Wave-C findings (see below) |

---

## P0 — Critical

### P0-001: `creativeProvider.cjs` Cannot `require()` ESM `logger.js` (ESM/CJS Boundary)
**File:** `MineflayerAdapter/creativeProvider.cjs` (line 11)
**The bug:** `creativeProvider.cjs` is CommonJS (`.cjs`) but does `require('./logger')`. `logger.js` is ESM (`"type": "module"` in `package.json`, `export function logStructured`). Node.js throws `ERR_REQUIRE_ESM` at runtime. The Wave A rename (`.js`→`.cjs`) fixed the ESM→CJS import from `index.js` but broke the CJS→ESM import inside the module.
**Impact:** When `ensureCreativeItem()` is called in creative mode, the adapter crashes with a module-system error. Creative build placement is completely non-functional.
**Recommendation:** Convert `creativeProvider.cjs` to ESM (`creativeProvider.mjs` + `import` syntax) or pass `logStructured` as a parameter.

### P0-002: `sendEvent` Has No Error Handling — Can Crash Node.js Process (Escalated by Council)
**File:** `MineflayerAdapter/index.js` (lines 122–129)
**The bug:** `agentSocket.send()` can throw (e.g., `ERR_STREAM_DESTROYED`, `ERR_WS_INVALID_LARGE_PAYLOAD`). `sendEvent` has no try/catch. It's called from unguarded event handlers (`bot.on('move')`, `bot.on('health')`, etc.) — a thrown exception propagates to the EventEmitter with no `uncaughtException` handler, terminating the Node.js process.
**Impact:** A transient WebSocket error during a routine event kills the entire adapter, triggering unnecessary C# reconnect.
**Recommendation:** Wrap `agentSocket.send()` in try/catch with structured logging.

### P0-003: No `unhandledRejection` / `uncaughtException` Process Handlers (Escalated by Council)
**File:** `MineflayerAdapter/index.js` (~line 2100)
**The bug:** Only `SIGINT`/`SIGTERM` handlers exist. No last-resort crash guards. In Node.js 22+, unhandled rejections terminate the process by default.
**Impact:** Any unhandled promise rejection kills the adapter without cleanup.
**Recommendation:** Add `process.on('unhandledRejection', ...)` and `process.on('uncaughtException', ...)` handlers that log and attempt clean shutdown.

### P0-004: Node.js stdout/stderr Pipes Never Read → Subprocess Hang Risk (Escalated by Council)
**File:** `Agent.World.Minecraft/MinecraftAdapter.cs` (line ~93)
**The bug:** `RedirectStandardOutput = true` and `RedirectStandardError = true` but no background read loop. When pipe buffers fill (4KB on Windows, 64KB on Linux), the Node.js process blocks on `write(2)` indefinitely.
**Impact:** The bot hard-hangs under sustained logging volume — cannot mine, move, craft, or respond to danger.
**Recommendation:** Either set `RedirectStandardOutput = false` (let Node.js write to console directly) or add background `ReadToEndAsync` tasks.

### P0-005: NuGet Vulnerability Scanning Still Missing from CI (TSK-0363 Unaddressed)
**File:** `.github/workflows/ci.yml`
**The bug:** No `dotnet list package --vulnerable` or `--deprecated` in CI. `NU1903` is explicitly exempted from `TreatWarningsAsErrors` in `Directory.Build.props`.
**Impact:** Vulnerable transitive dependencies (like the Sprint 51 `SQLitePCLRaw` CVE) will not be caught by CI.
**Recommendation:** Implement TSK-0363: add vulnerability/deprecated scanning steps, replace NU1903 exemption with documented time-bounded allowlist.

### [Council-Identified] P0-006: LLM Prompt Injection — No Input Sanitization for Chat Messages
**File:** `Agent.Planning/LlmChatInterpreter.cs` (~line 230)
**The bug:** Chat messages from other players are injected directly into the LLM system prompt via `{historyBlock}`. No sanitization, length limit, or content filter. A malicious player can send injection payloads to take control of the bot.
**Impact:** Any player on the same Minecraft server can execute arbitrary LLM instructions on the bot.
**Recommendation:** Add input sanitization, injection-resistance guard instructions, and rate limiting to the chat input pipeline.

---

## P1 — High

### [Council Merged] P1-001: `ITimeProvider` Not Adopted Across Core (3+ Components)
**Files:** `Agent.Core/ReplanGovernor.cs` (~99,115,165), `Agent.Core/Models/WorldModel.cs` (~46,48,66,183), `Agent.Core/Models/ActionOutcome.cs` (~113-137)
**The bug:** Three core components hardcode `DateTimeOffset.UtcNow` instead of using the `ITimeProvider` abstraction available since Sprint 27. Stall detection, uncertainty decay, and outcome timestamps are non-deterministic in tests.
**Impact:** Time-dependent behavior cannot be unit-tested with `FakeTimeProvider`. Graduated backoff, belief staleness, and outcome timestamps all depend on wall clock.
**Recommendation:** Inject `ITimeProvider` (default `SystemTimeProvider.Instance`) into all three components.

### [Council Merged] P1-002: Goal `HasFailed` Broken or Unreachable in 6 of 7 Goals
**Files:** `BuildGoal.cs` (~95), `CraftItemGoal.cs` (~52), `GatherWoodGoal.cs` (~31), `GenericGatherGoal.cs` (~65-66), `SmeltGoal.cs` (~54), `PlaceBlockGoal.cs` (~82)
**The bug:** 5 goals use `goal:{Type}:failed` fact keys never written by any code path — `HasFailed` always returns false. `PlaceBlockGoal.HasFailed` returns true when `inventory[_item] <= 0` (fires on completion, not failure). Only `SurviveNightGoal` has correct live logic.
**Impact:** Goals cannot self-diagnose failure. The consecutive-failure counter in ABS is the only real mechanism, but it's goal-agnostic.
**Recommendation:** Either wire the consecutive-failure counter to write fact keys, or remove dead `HasFailed` implementations and document that failure is handled externally.

### P1-003: BuildGoal Missing `Id` Property (All Instances Share `Guid.Empty`)
**File:** `Agent.Planning/Goals/BuildGoal.cs` (~47)
**The bug:** `BuildGoal` does not override `Guid Id` — inherits `Guid.Empty` from `IGoal`. All other 6 goals have per-instance `Id = Guid.NewGuid()`. Every `ActionOutcome.GoalId` for build-derived outcomes is `Guid.Empty`.
**Impact:** Per-goal outcome correlation for the entire build pipeline is broken.
**Recommendation:** Add `public Guid Id { get; } = Guid.NewGuid();` to `BuildGoal`.

### P1-004: `MineCompleteEvent` Never Completes MineBlock Correlation
**File:** `WebUI.Blazor/AgentBackgroundService.cs` (~720-970)
**The bug:** The `ProcessEventsAsync` switch has no `case MineCompleteEvent:` handler. `BlockMinedEvent` (individual blocks) and `MineAbortedEvent` do complete correlation, but `MineCompleteEvent` (definitive "mine loop done") falls through to `default:` and `TryRouteAsError`.
**Impact:** Every successful mine action that completes via `mineComplete` incurs a 30s sweep timeout delay before the correlation transitions to `TimedOut`.
**Recommendation:** Add `case MineCompleteEvent:` that calls `CompleteCorrelatedActionByTool("MineBlock")`.

### P1-005: WebSocketBridge `_ws` Data Race
**File:** `Agent.World.Minecraft/WebSocketBridge.cs` (~81, ~196)
**The bug:** `_ws` is read in `SendAsync()` without synchronization while `RunReceiveLoopWithRetryAsync()` disposes and replaces it during reconnect. No lock.
**Impact:** Corrupt WebSocket frames or `ObjectDisposedException` during concurrent send/reconnect cycles.
**Recommendation:** Guard `_ws` access with a lock or use `ImmutableInterlocked` for atomic swap.

### P1-006: Nearby Entity Display Bug Duplicates Hostile Info in LLM Prompt (Escalated by Council)
**File:** `Agent.Planning/LlmChatInterpreter.cs` (~200)
**The bug:** The fallback `entityBlock = hostileBlock` means when `nearbyEntities` fact is absent but `nearbyHostiles` is present, the hostile list appears twice — wasting LLM context and potentially inflating perceived threat count.
**Impact:** LLM makes threat-assessment decisions based on duplicated data.
**Recommendation:** Change fallback to empty string `""` — the `hostileBlock` is already included separately.

### P1-007: `playerCollect` Fallback Chain Always Returns `'unknown'`
**File:** `MineflayerAdapter/index.js` (playerCollect handler)
**The bug:** The handler uses `entity?.metadata?.name` but `metadata` is an array of `MetadataPropertyValue` objects, not a direct property accessor. Item names always resolve to `'unknown'`.
**Impact:** Every item collected in multiplayer is recorded as `'unknown'`. Inventory tracking silently broken.
**Recommendation:** Replace with version-safe pattern: `entity?.metadata?.find(m => m?.value?.name)?.value?.name ?? entity?.name ?? 'unknown'`.

### P1-008: `connectBot()` Leaks Event Listeners on Reconnect
**File:** `MineflayerAdapter/index.js` (connectBot + registerBotEventHandlers)
**The bug:** Old bot event listeners never removed. After N reconnects, each event fires N times.
**Impact:** Performance degradation: N duplicate C# events, N duplicate inventory updates, N duplicate entity scans.
**Recommendation:** Remove old listeners before re-registration.

### P1-009: Secret Scan CI Step Still Report-Only (Council Downgraded from P0)
**File:** `.github/workflows/ci.yml` (~55)
**The bug:** `continue-on-error: true` with no documented plan to make it blocking.
**Impact:** Secrets committed post-Wave-C will not block CI.
**Recommendation:** Create follow-up task to clean baseline and flip to blocking.

### [Council-Identified] P1-010: No Chat Command Rate Limiting / `cmdQueue` Unbounded
**File:** `MineflayerAdapter/index.js` (~240)
**The bug:** The `cmdQueue` array has no max length. Command flood from malicious player or buggy adapter causes unbounded memory growth.
**Impact:** OOM-based denial of service.
**Recommendation:** Cap `cmdQueue` size and add rate limiting to the chat/command pipeline.

---

## P2 — Medium (Selected)

| ID | Finding | File |
|:---|:--------|:-----|
| P2-001 | WorldState mutable dictionary exposure (recalibrated from P1) | `WorldState.cs` ~25-30 |
| P2-002 | ErrorEvent StoreFacts drops ReasonCode/Position (recalibrated from P0) | `WorldStateProjector.cs` ~400 |
| P2-003 | ToolResult.Success/Outcome invariant not enforced (recalibrated from P0) | `ActionData.cs` ~33 |
| P2-004 | Non-Ollama LLM providers fall back to NullLogger (recalibrated from P1) | `LlmProviderFactory.cs` ~37 |
| P2-005 | 3 goals missing IGoalPrecondition implementation (merged) | `BuildGoal.cs`, `GatherWoodGoal.cs`, `PlaceBlockGoal.cs` |
| P2-006 | Zero error handling for local file I/O in 2 memory/blueprint classes (merged) | `ItemRegistry.cs`, `BlueprintRepository.cs` |
| P2-007 | AgentRuntime decomposition aspirational — 0 managers consumed by ABS (merged) | `AgentRuntime.cs`, `Program.cs` ~403 |
| P2-008 | IgnoreAntiforgeryTokenAttribute defined but never used | `AntiforgeryExemptAttribute.cs` ~12 |
| P2-009 | DashboardPublisherImpl field mutations lack thread synchronization | `DashboardPublisherImpl.cs` ~45-70 |
| P2-010 | `/api/agent/stop` and `/api/agent/connect` are silent no-op stubs | `Program.cs` ~687-688 |
| P2-011 | `findGroundY` silently swallows all errors (escalated by council) | `index.js` ~442-457 |
| P2-012 | `move` action doesn't reset `_stopRequested` (escalated by council) | `index.js` ~628 |
| P2-013 | Missing min/max schema constraints on 5 tools' numeric parameters | Multiple tool files |
| P2-014 | Inconsistent `InputSchema` document lifetime (cached vs disposable) | All tool files |
| P2-015 | WorldState.Facts dictionary has no size cap (slow leak) | `WorldState.cs` ~18 |
| P2-016 | WaitForPortAsync throws TimeoutException even when cancelled | `MinecraftAdapter.cs` ~130-140 |
| P2-017 | UpdatePageAsync PUT failure path silently propagates | `RestMemoryGateway.cs` ~126-129 |
| P2-018 | Test-TaskRecords.ps1 doesn't validate all required schema fields | `Scripts/Test-TaskRecords.ps1` |
| P2-019 | Verify-AboutDeps.ps1 not run in CI | `.github/workflows/ci.yml` |
| P2-020 | AGENTS.md "Key Interfaces" references Sprint 36 future work (drift) | `AGENTS.md` ~620 |

---

## P3 — Low (Selected)

| ID | Finding | File |
|:---|:--------|:-----|
| P3-001 | 12+ event types lack structured StoreFacts extraction (recalibrated from P0) | `WorldStateProjector.cs` |
| P3-002 | DescribeMismatches duplicates HasUnexpectedChanges logic (recalibrated from P1) | `WorldStateDiff.cs` |
| P3-003 | LiveLogBuffer.Clear() unreachable dead code (recalibrated from P1) | `LiveLogBuffer.cs` ~47 |
| P3-004 | /api/about still hardcoded v0.55.0 (recalibrated from P1) | `Program.cs` ~578 |
| P3-005 | No tool sets OutcomeType semantic detail (recalibrated from P1) | All tool files |
| P3-006 | ChatIntentType dead code + _intentManager field never used (merged) | `LlmChatInterpreter.cs` |
| P3-007 | Duplicate IsTruthy helper in 4 goal classes (merged) | 4 goal files |
| P3-008 | README version/test count/curl examples stale | `README.md` |
| P3-009 | About page version/sprint badge stale at v0.51.0 | `about.html` ~127 |
| P3-010 | Missing dedicated test files for Sprints 55, 56, 58, 59 | Test directory |
| P3-011 | No regression test for TaskSequenceGoal `_isComplete` fix | Test directory |
| P3-012 | FactSource.Recovery enum value never used | `Fact.cs` + `WorldStateProjector.cs` |
| P3-013 | ToolRequirements duplicate inconsistent data sets | `ToolRequirements.cs` |
| P3-014 | logs/ directory not in .gitignore | `MineflayerAdapter/.gitignore` |
| P3-015 | Config constants drifted from reference documentation | `config.js` vs docs |
| P3-016 | Normalize-TaskRecords.ps1 hardcoded absolute path | `Scripts/Normalize-TaskRecords.ps1` |
| P3-017 | Invoke-SecretScan.ps1 line number detection fragile | `Scripts/Invoke-SecretScan.ps1` |

---

## Council Findings Removed

| Finding | Reason |
|:--------|:-------|
| SurviveNightGoal.HasFailed triggers on default health (0) | **False positive.** Default `WorldState.Health = 20`. `HasFailed` checks `Health <= 4`. No code path sets Health ≤ 4 by default. |
| WorldModel.Predict switch has no default for tool names | **False positive.** Switch has explicit `_ => PredictUnknown(...)` default arm — added in Sprint 59 (TSK-0336/0309). Agent read outdated version. |
| Smelt finally block ReferenceError | **False positive.** `furnace` is assigned before `try` block, not inside it. No `ReferenceError` path exists. |

---

## Cross-Cutting Themes (Identified by Council)

1. **Node.js crash surface is unprotected** — `sendEvent` (no try/catch), no crash guard handlers, unread pipe buffers. Three independent crash/hang risks in the adapter process.

2. **`ITimeProvider` adoption stalled since Sprint 27** — 3+ core components hardcode `DateTimeOffset.UtcNow`. Testability of time-dependent behavior is compromised.

3. **HasFailed is architecturally dead** — 5 of 7 goals use unwritten fact keys. The consecutive-failure counter in ABS is the real mechanism, but it's goal-agnostic.

4. **Dashboard has dual-write corruption** — ABS publishes SignalR inline AND `DashboardPublisherImpl` publishes from a different state snapshot. Users see stale/hardcoded values.

5. **The runtime decomposition (6 interfaces + 6 stubs) remains aspirational** — Created Sprint 36, never integrated. Generates 12+ maintenance-burden findings.

6. **Event correlation gap for newer event types** — Several events defined post-Sprint 40 lack handlers in `ProcessEventsAsync`. New events added without lifecycle/correlation wiring.

7. **LLM prompt injection is an unaddressed security gap** — Chat messages from other players are unsanitized and injected directly into the LLM system prompt.

---

## Methodology

- **10-agent homogeneous swarm (Branch A):** Each agent swept one partition independently (Agent.Core Models/Events, Agent.Core Interfaces/Runtime, Agent.Planning Goals, Agent.Planning Planner/LLM, Agent.Tools, Agent.World.Minecraft + ABS, WebUI.Blazor, MineflayerAdapter, Agent.Memory/Construction, Tests/Scripts/Config).
- **5-chair heterogeneous council (Branch B):** Architecture & Design, Runtime & Debugging, Safety & Security, Completeness & QA, Sprint Impact & Prioritization.
- **Recalibration:** 2 findings removed (false positives), 17 findings merged into 7 groups, 15 severity changes, 6 new architectural findings, 3 security escalations to P0, 2 security gaps identified.
- **Verification:** Chair 2 (Runtime & Debugging) verified all P0/P1 claims against actual source code.

## Peer Review Results

| Reviewer | Confidence | Key Actions |
|----------|:----------:|:------------|
| Architecture & Design | High | 2 removals, 15 severity changes, 17 merged, 6 new architectural findings |
| Runtime & Debugging | High (verified against source) | 2 inaccurate findings identified, 6 cross-cutting patterns |
| Safety & Security | 85% | 3 P0 escalations, 2 missing security findings (prompt injection, rate limiting) |
| Completeness & QA | 94% | 18 test gaps, 3 untracked findings, recommended 7 new tasks (TSK-0372→0378) |
| Sprint Impact | Full prioritization | 11 Sprint 60 candidates (~11.5h), 10 quick wins, 1 dependency chain |

## Task Synthesis

### Existing Tasks Covering Audit Findings

| Finding | Existing Task | Status |
|:--------|:-------------|:-------|
| P0-001 (creativeProvider ESM/CJS) | TSK-0359 (Wave A — partial) | Done (but fix incomplete) |
| P0-005 (CI vulnerability scanning) | TSK-0363 | Backlog |
| P1-005 (_ws data race) | TSK-0361 | Backlog |
| P1-004 (MineCompleteEvent correlation) | TSK-0360 (audit task) | Done — needs new task |
| P1-007 (playerCollect fallback) | TSK-0366 | Backlog |
| P1-008 (listener leak) | TSK-0367 | Backlog |
| P0/S61 (runtime decomposition) | TSK-0369 | Backlog |
| P1-003 (BuildGoal missing Id) | No task | **NEW** |
| P1-006 (entity prompt duplication) | No task | **NEW** |
| P1-002 (HasFailed dead pattern) | TSK-0365 (PlaceBlockGoal partial) | Backlog |
| P0-006 (LLM prompt injection) | No task | **NEW** |
| P1-010 (rate limiting/cmdQueue cap) | No task | **NEW** |
| P2-010 (no-op stop/connect stubs) | No task | **NEW** |

### New Tasks Created by This Audit

| Key | Title | Priority | Link |
|:----|:------|:--------:|:----:|
| TSK-0372 | 10-Agent Codebase Audit + 5-Chair Council — Sprint 60 Post-Wave-C | High | This report |
| TSK-0373 | Fix `/api/about` hardcoded version string — derive from assembly metadata | High | P3-004 |
| TSK-0374 | Add error handling and logging to LocalKnowledgeResolver and RestMemoryGateway | High | P1 (untracked) |
| TSK-0375 | Remove stale Python scripts from Scripts/ directory | Medium | P3 (hygiene) |
| TSK-0376 | Add WebSocketBridge unit tests | Medium | P2 (test gap) |
| TSK-0377 | Backfill null timestamp fields in task records | Medium | P2 (hygiene) |
| TSK-0378 | Add regression test for TaskSequenceGoal `_isComplete` fix | Medium | P3-011 |
| TSK-0379 | Add end-to-end integration test for full agent pipeline | High | P2 (test gap) |

### Urgent Findings Needing Immediate Tasks

| Finding | Proposed Key | Priority | Rationale |
|:--------|:-------------|:--------:|:----------|
| P0-002 (sendEvent crash) | New task | **Critical** | Process crash on WebSocket error |
| P0-003 (no crash guard handlers) | New task | **Critical** | Process crash on unhandled rejection |
| P0-004 (pipe buffer hang) | New task | **Critical** | Subprocess hang under log volume |
| P0-006 (LLM prompt injection) | New task | **Critical** | Any player can control the bot |
| P1-003 (BuildGoal missing Id) | New task | High | Goal correlation broken |
| P1-006 (entity prompt duplication) | New task | High | LLM threat assessment corrupted |
| P1-010 (rate limiting) | New task | High | Unbounded command queue growth |

---

## Appendix: Council Chair Reports

Full chair reports available on request from `Data/Pages/Council/10-agent-codebase-audit-council-20260711-post-wave-c/`:
- Chair 1: Architecture & Design — severity recalibrations, merges, architectural findings
- Chair 2: Runtime & Debugging — verified P0/P1 claims against source, cross-cutting patterns
- Chair 3: Safety & Security — escalations, missing security findings, prompt injection
- Chair 4: Completeness & QA — test gaps, task mapping, schema drift, docs drift
- Chair 5: Sprint Impact — 11 Sprint 60 candidates, quick wins, dependency graph
