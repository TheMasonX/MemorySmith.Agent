# Handoff — Sprint 60 Wave C Complete (2026-07-11)

**Branch:** `dev/round-3` (`dbc949b`)
**Previous agent:** SteveBot
**Next agent:** SteveBot or Agent Smith

---

## Wave C Status

| Task | Priority | Status | Summary |
|:----:|:--------:|:------:|---------|
| **TSK-0350** | **Critical** | ✅ Done | Global antiforgery filter — middleware + validation + tests |
| **TSK-0349** | **Critical** | ✅ Done | Secret scanning infra — pre-commit hook + CI + policy |
| **TSK-0346** | High | ✅ Done | Adapter goto timeout fix — deterministic failure events |
| **TSK-0347** | High | ✅ Done | Search scoring investigation + MSR-001 + band-aid |

---

## TSK-0350 — Global Antiforgery Filter ✅

**What was done:**
- Added `builder.Services.AddAntiforgery()` + `app.UseAntiforgery()` to `Program.cs`
- Created `AntiforgeryValidationMiddleware` that inspects `IAntiforgeryValidationFeature` and explicitly validates via `IAntiforgery.ValidateRequestAsync` for JSON endpoints (since .NET 9+ `UseAntiforgery()` doesn't short-circuit)
- SignalR hub at `/agent-hub` exempted via `.DisableAntiforgery()` (WebSocket negotiate is immune to CSRF)
- Token endpoint at `GET /antiforgery/token` for SPA dashboard
- Dashboard JS updated: fetches token on `init()`, includes in POST/DELETE requests via `antiforgeryHeaders()` helper
- Created `IgnoreAntiforgeryTokenAttribute` for future exemptions
- 6 NUnit tests: GET skip, POST/DELETE rejection, hub exemption, token endpoint, cookie set

**Files changed:**
- `WebUI.Blazor/Program.cs` — antiforgery service + middleware + token endpoint
- `WebUI.Blazor/AntiforgeryValidationMiddleware.cs` — NEW: enforces 400 on invalid tokens
- `WebUI.Blazor/AntiforgeryExemptAttribute.cs` — NEW: marker for endpoint exemptions
- `WebUI.Blazor/wwwroot/index.html` — antiforgery token fetch + header injection
- `MemorySmith.Agent.Tests/Sprint60AntiforgeryTests.cs` — 6 tests

**Known risks:** None. The dashboard still works without tokens for `/antiforgery/token` (GET) and static files. All POST/DELETE endpoints now require a valid token + cookie pair.

---

## TSK-0349 — Secret Scanning Infrastructure ✅

**What was done:**
- Created `Scripts/Invoke-SecretScan.ps1` — scans staged changes (pre-commit mode) or full repo (CI mode) for:
  - API keys, bearer tokens, JWT tokens
  - AWS access/secret keys, GitHub PATs, NuGet API keys, Slack tokens
  - Private SSH keys, connection strings, passwords
  - Configurable false-positive list and file skip list
- Installed `.git/hooks/pre-commit.bat` — calls the scan script (batch wrapper needed for Windows)
- Added CI secret scan step to `.github/workflows/ci.yml` — report-only (`continue-on-error: true`) until baseline is clean
- Created `Data/Pages/policies/secret-management.md` — secret inventory table, rotation procedures, git filter-repo instructions

**⚠️ NOTE:** The CI workflow change (`.github/workflows/ci.yml`) requires the `workflow` OAuth scope which is blocked per AGENTS.md Rule E-X. The user must apply this change manually via the GitHub web UI.

**⚠️ Human dependency:** Actual credential rotation (revoking old keys, generating new ones, updating env vars) requires human action. Documented in `secret-management.md`.

**Files changed:**
- `Scripts/Invoke-SecretScan.ps1` — NEW
- `.git/hooks/pre-commit.bat` — NEW
- `.github/workflows/ci.yml` — added secret scan step
- `Data/Pages/policies/secret-management.md` — NEW

---

## TSK-0346 — Adapter Goto Timeout Fix ✅

**What was done:**
- Wrapped `bot.pathfinder.goto()` in the `move` case with try-catch in `MineflayerAdapter/index.js`
- On success: emits `moveComplete` (unchanged)
- On failure: emits `moveComplete` with `failed: true`, `reasonCode`, and `message`  
  Then re-throws so `drainQueue`'s catch also emits `actionFailed` for the C# outcome correlator
- Prevents actions from being orphaned in `Dispatched → SweepTimedOut` state

**Files changed:**
- `MineflayerAdapter/index.js` — goto() wrapped with deterministic outcome emission

**Known risks:** Other `goto()` callers (place, craft, mine item pickup) are already within try-catch blocks or have their own error handling. Only the `move` case had the silent-drop issue.

---

## TSK-0347 — Search Scoring Zero Investigation ✅

**What was done:**
- **Investigation complete:** Root cause is three-layer gap:
  1. `PageSummary` record lacks `Score` field
  2. `PageService.SearchAsync` computes lexical score via `Score()` method but discards it in `ToSummary()`
  3. `SearchController` hardcodes `null` for page scores in `UnifiedSearchResult`
- `MemoryScorer` weights (TSK-3078) already fixed independently — weights sum to 1.0
- Created **`Data/Pages/MS-Requests/msr-001-page-search-score.md`** — detailed cross-repo fix request for MemorySmith base repo with exact code changes needed
- Applied **agent-side band-aid**: `RestMemoryGateway.cs` now gives pages a minimum score floor of `0.05` when Score is null from the API

**Files changed:**
- `Data/Pages/MS-Requests/msr-001-page-search-score.md` — NEW (cross-repo fix request)
- `Agent.Memory/RestMemoryGateway.cs` — minimum page score floor (0.05)

**Next step:** The MemorySmith base repo should implement the fix per MSR-001. This is scoped to `PageService.cs` and `SearchController.cs` — no new BM25/embedding pipeline needed; the existing `Score()` method just needs to be exposed.

---

## Current State

| Check | Result |
|-------|--------|
| Build | ✅ Succeeds (0 errors, 0 warnings) |
| Tests | ✅ 821 pass, 0 failures |
| Task records | ✅ Valid |
| Branch | `dev/round-3` (`dbc949b`) |

---

## Next Up (Wave D — Planned)

| Priority | Task | What It Involves |
|:--------:|:----:|------------------|
| **Critical** | TSK-0302 | Inventory SSOT refactor — event-sourced, remove IsInventoryStale |
| **Critical** | TSK-0245 | Assess BlockState type change impact |
| **Critical** | TSK-0243 | EvaluationResult discriminated types |

---

## CI Workflow Note

The `.github/workflows/ci.yml` change (secret scan step) requires manual application via GitHub web UI because the `workflow` OAuth scope is blocked on this token. The change is:
```yaml
      - name: Secret scan
        run: pwsh ./Scripts/Invoke-SecretScan.ps1 -ScanAll -ReportPath artifacts/secret-scan-report.json
        shell: pwsh
        continue-on-error: true
```
