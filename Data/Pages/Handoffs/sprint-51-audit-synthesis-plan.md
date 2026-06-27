# Sprint 51 — Audit Synthesis & Wave A Plan

**Date:** 2026-06-26  
**Branch:** `sprint-35-llm-first`  
**Tests:** 731 passing, 0 failing  
**Version:** v0.50.3-dev (Sprint 51 — Audit Synthesis)

---

## Audit Sources

This sprint synthesizes findings from 4 audit reports plus the Sprint 50 handoff:

| Source | Focus | Key Findings |
|--------|-------|-------------|
| `sprint50_audit_delta_2026-06-26.md` | Delta corrections on context carry, navigate fast-path drift, NU1903 suppression | Context merge conflicts with schema validation; navigate fast-path undocumented; NU1903 too broad |
| `sprint50_code_audit_d2ef16a.md` | Code-level audit of head commit | Context carry architecturally inconsistent; TSK-0004 not E2E; LlmChatInterpreter navigate exception |
| `deep_audit_delta_v2.md` | Tightened context-carry assessment | Global context merge incompatible with schema validator; need per-tool allowlist |
| `deep_audit_sprint35_llm_first.md` | Full deep audit | P0: context wiring incomplete; P1: SQLite failure isolation missing, NU1903 too broad; P2: task status drift |
| `sprint-50-complete-next-steps.md` | Handoff from Sprint 50 | Wave D delivered but TSK-0004/TSK-0014 still Open in task store |

---

## Wave A (This Commit) — Warning Policy, Doc Fixes, Task Sync

All delivered in this commit:

### 1. NU1903 Warning Policy Reform
**Files:** `Directory.Build.props`, `WebUI.Blazor.csproj`
- **Removed** the `<NoWarn>$(NoWarn);NU1903</NoWarn>` suppression from `WebUI.Blazor.csproj`
- Added `<WarningsNotAsErrors>NU1903</WarningsNotAsErrors>` to `Directory.Build.props` — this is **not a suppression**: the NU1903 advisory is fully visible in build output as a warning, but does not break the build under `TreatWarningsAsErrors=true`
- Pinned `SQLitePCLRaw.lib.e_sqlite3` to 2.1.11 (latest) in `WebUI.Blazor.csproj`
- Rationale: the advisory (GHSA-2m69-gcr7-jv3q) is in the SQLite native library bundled by the transitive dependency chain (`Microsoft.Data.Sqlite` → `SQLitePCLRaw.bundle_e_sqlite3`), not in our log-write logic. The warning remains visible for human triage.

### 2. LlmChatInterpreter Doc Comment Fix
**File:** `Agent.Planning/LlmChatInterpreter.cs`
- Updated class summary comment (line 24) to accurately state that `navigate` IS a deterministic fast-path (added back in Sprint 43 P0-1)
- Updated inline comment (lines 86-90) to match current code: "CreateGoal fast-path removed — all non-trivial chat reaches the LLM. Sprint 43 (P0-1): re-added fast-path for navigate"
- Resolves audit finding: doc/code mismatch on deterministic fast-paths

### 3. Task Status Sync
**Tasks:** `TSK-0004`, `TSK-0014`
- `TSK-0004` (Wire MoveToTool context injection): Status changed from `Backlog` → `InProgress`. Description updated to reflect dispatcher-side merge delivered, remaining items (per-tool contract, SearchMemory producer, MoveTo consumer, tests)
- `TSK-0014` (Serilog SQLite sink): Status changed from `Open` → `InProgress`. Description updated to reflect sink code delivered, remaining items (failure isolation, retention policy, troubleshooting docs)

---

## Wave B (Next) — Context Carry Contract Fix

### Goal: Fix the architectural inconsistency between context merge and schema validation

The dispatcher currently copies ALL non-internal `Context` entries into `Arguments` before tool validation. But `ToolDispatcher.ValidateAgainstSchema` rejects undeclared properties, causing silent failures.

**Implementation plan:**

1. **Define a per-tool context allowlist** — Add a `IContextAwareTool` interface or a `ContextKeys` property on `ITool` that declares which context keys a tool accepts. Only those keys get merged from `Context` into `Arguments`.

2. **Update `MoveToTool`** — Add context-aware coordinate fallback: if `x/y/z` absent in `Arguments`, check `Context["nearestX"]`, `Context["nearestY"]`, `Context["nearestZ"]`. Update `InputSchema` to make x/y/z not required when context fallback is available.

3. **Update `SearchMemoryTool`** — Emit structured coordinate hints (`nearestX`, `nearestY`, `nearestZ`, `confidence`) in `ToolResult.Data` when the best result contains location information.

4. **Add `ContextCarryTests`** — Happy path: SearchMemory writes coords, MoveTo reads from context. Failure path: unrelated context keys are rejected/handled gracefully.

5. **Update `AgentBackgroundService`** — Replace the current `foreach (var kv in action.Context)` merge with the allowlist-based approach.

**Files affected:** `ITool.cs`, `ToolDispatcher.cs`, `MoveToTool.cs`, `SearchMemoryTool.cs`, `AgentBackgroundService.cs`, new test file

**Estimate:** ~45 min  
**Priority:** High

---

## Wave C (Next+) — SQLite Telemetry Hardening

### Goal: Meet the acceptance criteria defined in TSK-0014

1. **Add startup failure isolation** — Wrap SQLite sink activation so the agent boots even when the DB path is locked/unwritable. Log a warning and fall through to remaining sinks.

2. **Define retention policy** — Set max file size and retention count for `logs/agent-telemetry.db` so telemetry doesn't grow unbounded.

3. **Add test for locked/unwritable path** — Simulate a locked SQLite DB and verify the agent continues with the file and EventLog sinks still active.

4. **Document troubleshooting queries** — Add at least one query for common incidents (repeated gather loops, LLM null responses).

**Files affected:** `Program.cs`, `appsettings.json`, new test file

**Estimate:** ~30 min  
**Priority:** Medium

---

## Wave D (Backlog) — Chat Path Cleanup

### Goal: Finalize the chat interpretation architecture

1. **Decide official policy for `navigate` fast-path** — Currently documented after this sprint. Either keep as intentional zero-risk shortcut (current state) or remove it to make "LLM owns intent" fully consistent.

2. **Add clarification-question bridge test** — Verify that a low-confidence `clarify` draft from the LLM does not enter goal creation and is properly surfaced to the user.

**Estimate:** ~20 min  
**Priority:** P2

---

## Current Production State

```
Build:   0 errors, 4 NU1903 warnings (visible, not suppressed)
Tests:   731 passing, 0 failing, 0 skipped
Branch:  sprint-35-llm-first
Version: v0.50.3-dev (Sprint 51 — Audit Synthesis)
```

### Active Agent Capabilities
(Same as Sprint 50 — see `sprint-50-complete-next-steps.md` for full list)

### Known Open Items
1. **TSK-0004 (InProgress)**: Context carry needs per-tool contract — context merge can fail schema validation for non-allowlisted keys
2. **TSK-0014 (InProgress)**: SQLite sink needs failure isolation, retention policy, and troubleshooting docs
3. **NU1903 advisory**: Visible warning on `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 — revisit when Microsoft.Data.Sqlite updates its dependency range
4. **Navigate fast-path**: Currently documented as intentional — revisit if "LLM owns intent" becomes absolute

---

## Key Files Changed (This Commit)

| File | Change |
|:-----|:-------|
| `Directory.Build.props` | Added `WarningsNotAsErrors>NU1903</WarningsNotAsErrors>` — visible warning, not suppression |
| `WebUI.Blazor/WebUI.Blazor.csproj` | Removed `<NoWarn>NU1903</NoWarn>` suppression, pinned SQLitePCLRaw.lib.e_sqlite3 to 2.1.11 |
| `Agent.Planning/LlmChatInterpreter.cs` | Fixed doc comments on navigate fast-path to match actual code |
| `Data/Tasks/tsk-0004-*.json` | Status: Backlog → InProgress, description updated with remaining work |
| `Data/Tasks/tsk-0014-*.json` | Status: Open → InProgress, description updated with remaining work |
| `Data/Pages/Handoffs/sprint-51-audit-synthesis-plan.md` | This document — sprint plan synthesized from 4 audit reports |
