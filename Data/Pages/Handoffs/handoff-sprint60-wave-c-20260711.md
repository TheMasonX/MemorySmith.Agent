# Handoff — Sprint 60 Wave C: Audit Synthesis Tasks (2026-07-11)

**Previous agent:** SteveBot  
**Branch:** `dev/round-3` (`6159a7d`)  
**Next agent:** SteveBot or Agent Smith  

---

## What Was Completed This Session (Wave B)

### MemorySmith.Agent — 1 Commit

**Commit:** `6159a7d` — Sprint 60 Wave B: Core pipeline hardening — 3 Critical fixes

| Task | Title | What Was Done |
|:----:|-------|---------------|
| **TSK-0322** | Fix ExecutionManagerImpl JSON round-trip type fidelity | Replaced `Serialize()`→`Parse()`→`JsonElement` round-trip with `JsonSerializer.SerializeToElement()` in both `ExecutionManagerImpl.DispatchAsync` and `AgentBackgroundService` dispatch path. Eliminates `long→int` / decimal precision loss and unnecessary intermediate string allocation. |
| **TSK-0345** | Fix replan flooding and loop-preservation | Added `_lastMeaningfulEventAt` tracking in `ProcessEventsAsync` (updated on non-`MoveEvent`). Added `EventSettleTimeout` (500ms) replan guard — skips replanning if events arrived recently. Added `MaxPostDispatchSettle` (5s) timeout. Extended post-dispatch settle from 100ms→200ms. |
| **TSK-0348** | WorldModel inventory accuracy and ActionOutcome wiring | `PredictPlace` now deducts placed block from predicted inventory. `PredictSmelt` predicts output item using inline smeltable mapping (deducts input, adds output). Added `IWorldModel.ApplyOutcome(ActionOutcome)` — applies `ItemCollected`/`ItemConsumed`/`ItemCrafted` effects to belief state, wired into dispatch loop. |

**Files changed:** `Agent.Core/Interfaces/IWorldModel.cs`, `Agent.Core/Models/WorldModel.cs`, `WebUI.Blazor/AgentBackgroundService.cs`, `WebUI.Blazor/Managers/ExecutionManagerImpl.cs`, 4 task JSON files.

**Validated:** ✅ 815 tests pass, 0 failures, 0 warnings, task records valid.

---

## Wave C — Audit Synthesis Tasks (Ready — Up To 4 Tasks)

These are the next wave per the Sprint 60 roadmap. All are `Ready` and unstarted.

### TSK-0346 — Fix adapter goto / pathfinder timeout gap (High)
**Scope:** Close the `goto` timeout / outcome gap so adapter returns deterministic outcomes and publishes correct events. Add E2E test and validate event contract.

**Key files:**
- `MineflayerAdapter/index.js` — adapter logic with `goto()` pathfinder timeout handling
- `MemorySmith.Agent.Tests/` — E2E test for event contract

**Context:** The Node.js adapter's `goto()` call can time out without properly signaling the outcome to the C# side, leaving the bot in an inconsistent state. The fix needs to ensure deterministic outcomes (success, path blocked, timed out) and correct event publishing.

---

### TSK-0347 — Restore search scoring (page-search-score-zero-facts) (High)
**Scope:** Investigate and fix zero-scoring pages in search. Add BM25 indexing or embedding pipeline, ranking telemetry, and CI ranking tests using TestWorld.

**Key files:**
- `MemorySmith.Core/` — search/indexing code
- `MemorySmith.Tests/` — ranking tests

**Context:** The `Page Search Score=0.0` issue (`TSK-0132`) has been known for a while. This task broadens the scope to investigate the root cause — whether the scoring weights are wrong, the index is stale, or the BM25/embedding pipeline is broken. May depend on TSK-3078 (MemoryScorer weights fix) from the MemorySmith base repo.

---

### TSK-0349 — Rotate repo secrets and add secret-scanning/pre-commit hook (Critical)
**Scope:** Rotate all exposed credentials, remove secrets from git history, add CI secret scanner and pre-commit hook to block future commits. Coordinate with ops for rotation and notification.

**Key files:**
- `.git/` — BFG/history rewrite
- `.github/workflows/ci.yml` — add secret scanning step
- `Scripts/` — pre-commit hook

**Context:** This is a **Critical** security task. Requires human coordination for credential rotation. The pre-commit hook and CI scanner can be implemented agent-side. **Do not push secrets in commits.**

---

### TSK-0350 — Add global antiforgery filter and validate endpoints (Critical)
**Scope:** Implement global antiforgery middleware/filter, audit public endpoints, and add automated tests to assert antiforgery tokens required where applicable. Document exceptions.

**Key files:**
- `WebUI.Blazor/Program.cs` — service configuration
- `WebUI.Blazor/` — controllers/endpoints to audit

**Context:** The Blazor UI (MemorySmith.Agent) needs CSRF protection. Add `[AutoValidateAntiforgeryToken]` as a global filter, then audit all endpoints to identify which should be exempt (e.g., WebSocket, webhook callbacks). Add tests to enforce the policy.

---

## Current State

| Check | Result |
|-------|--------|
| Build | ✅ Succeeds (0 errors) |
| Tests | ✅ 815 pass, 0 failures |
| Task records | ✅ Valid |
| Branch | `dev/round-3` (`6159a7d`) |

### Ready Tasks (Not Yet Started)
22 tasks in `Ready` status. Priority order for waves C/D/E:

| Wave | Priority | Tasks |
|:----:|:--------:|-------|
| **C** | Critical/High | TSK-0346, TSK-0347, TSK-0349, TSK-0350 |
| **D** | Critical | TSK-0302 (inventory SSOT), TSK-0245 (BlockState type change) |
| **D** | Critical | TSK-0243 (EvaluationResult discriminated types) |
| **E** | High | TSK-0144 (CI package vetting), TSK-0134 (DI startup logging), TSK-0133 (replan params), TSK-0132 (page search score), TSK-0271 (build dispatch sequencing) |
| — | High | TSK-0293 (legacy fallback removal), TSK-0248 (autonomy dashboard), TSK-0249 (runtime signal sink) |

---

## Key Decisions & Assumptions

1. **Branch:** `dev/round-3` — same as Wave A/B. No main merge yet.
2. **TSK-0346 (adapter goto):** The Mineflayer adapter code is in `MineflayerAdapter/index.js`. The `goto()` call uses `pathfinder.goto()` with a configurable timeout. The gap is that timeouts don't produce deterministic outcomes or events. The fix likely involves a `path_update` event listener and a promise wrapper with timeout.
3. **TSK-0347 (search scoring):** This may have dependencies on the MemorySmith base repo (TSK-3078 — MemoryScorer weights). If the fix requires changes to the base MemorySmith repo, prepare a request document under `Data/Pages/MS-Requests/` in MemorySmith.Agent rather than editing the base repo directly.
4. **TSK-0349 (secret scanning):** Use tools like `trufflehog`, `git secrets`, or `detect-secrets` for the pre-commit hook and CI step. The actual credential rotation requires human action — document the steps and flag for ops.
5. **TSK-0350 (antiforgery):** In .NET Blazor Server, antiforgery is handled differently than MVC. Use the `[AutoValidateAntiforgeryToken]` approach. WebSocket endpoints (`/agenthub`) are exempt. Check if the Blazor app uses `AddControllers()` or minimal APIs.

---

## Known Risks

1. **TSK-0347 scope creep** — search scoring could be a deep rabbit hole. If the root cause isn't obvious within one session, log findings and defer to a dedicated search sprint.
2. **TSK-0349 human dependency** — secret rotation can't be fully automated. The agent should implement the scanning infrastructure and document the rotation steps, then flag for human action.
3. **TSK-0350 Blazor antiforgery complexity** — .NET Blazor Server uses SignalR circuits; the antiforgery filter applies to MVC controllers but not to Blazor component endpoints. Need careful auditing to avoid breaking Hub connections.

---

## Commands

```powershell
# Build
dotnet build --no-restore MemorySmith.Agent.slnx -p:CopilotSkipCliDownload=true

# Test
dotnet test --no-build

# Task validation
pwsh ./Scripts/Test-TaskRecords.ps1
```

---

**Evidence paths:** `Data/Tasks/` for task records, `WebUI.Blazor/AgentBackgroundService.cs` for Wave B changes, `Agent.Core/Models/WorldModel.cs` for ApplyOutcome/PredictPlace/PredictSmelt.
