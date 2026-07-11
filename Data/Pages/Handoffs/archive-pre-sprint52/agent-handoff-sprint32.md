# Agent Handoff — Sprint 32

**Branch:** `sprint-5-tool-safety`
**Incoming HEAD:** `87bc1a5c4da1622e1fd8455158e95eee9ba78cb3`
**Handoff date:** 2026-06-20
**Prepared by:** MemorySmith.Agent Engineering Council (Sprint 31 audit session)
**Companion document:** `Data/Pages/council/sprint31-council-20260620.md`

---

## I. What This Sprint Window Covered

This was a **council review + audit synthesis sprint**, not an implementation sprint. No new C# code was committed. The council reviewed:

1. The Sprint 31 handoff doc (f58a91404f) vs. actual source state
2. New external audits uploaded in commits f0e13c44 and 87bc1a5c
3. Independent source verification of key claims

Key outcome: BLK-01 was re-confirmed independently. A new plausible blocker (BLK-02 — files potentially still stored as base64 text) was identified and requires live build verification before Sprint 32 work begins.

---

## II. Critical Invariants — Read Before Writing Any Code

### BLOCKING FINDING: BLK-01 — Program.cs DI Registration Compile Error

**Status: Confirmed by independent source inspection.**

`BuildGoalDecomposer.cs` constructor signature (confirmed at HEAD):
```csharp
public sealed class BuildGoalDecomposer(HtnTaskLibrary taskLibrary, ILogger<BuildGoalDecomposer> logger) : IGoalDecomposer
```

`Program.cs` call site (confirmed in decoded content at HEAD):
```csharp
reg.Register(new BuildGoalDecomposer(lib));   // ← WRONG: missing ILogger<BuildGoalDecomposer>
```

**Fix required:**
```csharp
// Obtain loggerFactory before this block (e.g., after app.Build()):
// var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
// OR during builder phase:
// var loggerFactory = LoggerFactory.Create(b => b.AddSerilog());
// Then:
reg.Register(new BuildGoalDecomposer(lib, loggerFactory.CreateLogger<BuildGoalDecomposer>()));
```

Read `Program.cs` initialization order fully before writing the fix. The correct `ILoggerFactory` source depends on whether the `DecomposerRegistry` setup occurs before or after `builder.Build()`.

### PLAUSIBLE BLOCKING FINDING: BLK-02 — Files May Be Stored as Base64 Text

**Status: Unconfirmed — requires live `dotnet build`.**

Multiple Sprint 30 commits that decoded or created C# files may have used the GitHub MCP's `create_or_update_file` action and accidentally stored the file content as base64 text rather than actual C# source. This is the same pattern that created the original base64 problem in Sprint 28.

**Affected files to check (in addition to Program.cs):**
- `Agent.Tools/ToolDispatcher.cs` (decode commit c34bd3d8)
- `WebUI.Blazor/ApiKeyMiddleware.cs` (create commit f3b860c5)
- `MemorySmith.Agent.Tests/Sprint30Tests.cs` (create commit 8e1b260c)
- `Agent.Core/WorldStateProjector.cs` (decode commit d457e0a2)

**Sprint 32 MUST run `dotnet build` as its first action and fix all errors found before proceeding.**

If base64 files are found, use the following pattern to decode and re-commit correctly:
```bash
# Download raw file content (not the GitHub API wrapper)
curl -sL "https://raw.githubusercontent.com/TheMasonX/MemorySmith.Agent/sprint-5-tool-safety/<path>" > /tmp/file.cs
# Verify it's base64: if it starts with 'bm' or 'Ly' it's base64
head -c 20 /tmp/file.cs
# Decode if base64:
base64 -d /tmp/file.cs > /tmp/decoded.cs
# Then use github__create_or_update_file with paramsFile (never inline content > 5KB)
```

### Invariant: Do Not Assert Build Correctness

Every sprint from Sprint 26 through Sprint 30 has claimed "build verified" without running `dotnet build`. This is the root cause of accumulated compile errors. Sprint 32 **must** produce actual build output attached to its handoff document.

### Invariant: Program.cs Is No Longer Base64 (post-fix)

After Sprint 32 resolves all base64 and DI errors, Program.cs must be committed as actual C# source. The same applies to all other affected files. Do not re-encode.

### Invariant: Reflection Tests Are Fragile

`Sprint30Tests.cs` tests private methods on `BuildGoalDecomposer` and `ChatInterpreter` via `System.Reflection`. These tests will NOT produce compile errors if method signatures change — they fail silently at runtime. If private method signatures change, update these tests manually.

### Invariant: ApiKeyMiddleware Applies Only to /api/* Routes

Wired via `app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), ...)`. Routes outside `/api/*` are not gated. Intentional for Sprint 30 scope.

---

## III. CI Status

**CI is not running on this branch.**

No `.github/workflows/ci.yml` exists for `sprint-5-tool-safety`. All "build verified" claims to date are assertions, not verifications. Sprint 32 should create the CI workflow.

**Minimum viable CI:**
```yaml
# .github/workflows/ci.yml
on:
  push:
    branches: [sprint-5-tool-safety]
  pull_request:
    branches: [sprint-5-tool-safety]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet build --configuration Release -p:CopilotSkipCliDownload=true
      - run: dotnet test --no-build --configuration Release -p:CopilotSkipCliDownload=true
```

Note: `-p:CopilotSkipCliDownload=true` is required to avoid GitHub.Copilot.SDK CLI download from npm. The target is net10.0.

Note on GitHub MCP: The MCP OAuth token lacks the `workflow` scope and cannot write to `.github/workflows/`. Provide this YAML to the user to add manually via the GitHub web UI (Settings → Actions → New workflow).

---

## IV. Sprint 32 Tasks

Tasks are ordered by priority. Sprint 32 must not proceed past P0 until all Gate 1 (build) acceptance criteria are met.

---

### P0 — Blocking: Restore Build to Compilable State

#### P0-1: Investigate and decode any remaining base64 files

**First action of Sprint 32. Do not skip.**

1. Fetch each suspect file using `curl -sL https://raw.githubusercontent.com/TheMasonX/MemorySmith.Agent/sprint-5-tool-safety/<path>`
2. Check first 20 bytes — if it starts with ASCII letters that form base64 (e.g., `bm`, `Ly`, `ZW`, `dX`), it is stored as base64 text
3. For each base64 file: decode it, verify the decoded content is valid C#, then re-commit using `github__create_or_update_file` with `paramsFile` (never inline content > 5KB per AGENTS.md Rule E-1)
4. Check ALL files changed in Sprint 28–30 commits: BuildGoalDecomposer.cs (likely clean), ToolDispatcher.cs, WorldStateProjector.cs, ApiKeyMiddleware.cs, Sprint30Tests.cs, Program.cs

**Acceptance criteria:**
- [ ] All C# source files checked for base64 storage
- [ ] Any base64-stored files decoded and re-committed as actual C# source
- [ ] Commit message: `fix(core): decode remaining base64 files (Sprint 32 P0-1)`

#### P0-2: Fix BLK-01 — BuildGoalDecomposer DI Registration

**File:** `WebUI.Blazor/Program.cs`

**Acceptance criteria:**
- [ ] `reg.Register(new BuildGoalDecomposer(lib, <logger>))` compiles without error
- [ ] The `<logger>` argument is sourced correctly from ASP.NET Core DI (not hardcoded, not null)
- [ ] No `#pragma warning disable` or `null!` suppression used to paper over the error
- [ ] Commit message: `fix(di): pass ILogger to BuildGoalDecomposer registration (Sprint 32 P0-2 BLK-01)`

#### P0-3: Verify `dotnet build` Exits 0

**Acceptance criteria:**
- [ ] `dotnet build --configuration Release -p:CopilotSkipCliDownload=true` exits with code 0 in a live environment (not asserted — actually run)
- [ ] Build output (stdout/stderr) captured and included in Sprint 33 handoff document
- [ ] Zero errors in the output
- [ ] If new compile errors surface beyond BLK-01, triage each as new blocking findings before continuing

#### P0-4: Verify `dotnet test` Exits 0

**Acceptance criteria:**
- [ ] `dotnet test --no-build --configuration Release -p:CopilotSkipCliDownload=true` exits with code 0
- [ ] Test count ≥ 261 (Sprint 30 target — Sprint 31 tests expected to compile now)
- [ ] All Sprint30Tests.cs tests pass
- [ ] No tests that were passing before Sprint 28 are now failing
- [ ] Test output captured and included in Sprint 33 handoff document

---

### P1 — High Priority

#### P1-1: SEC-02 — Node.js Port 5050 Shared Secret (promoted from P2-A)

**Context:** The `.NET` agent and Mineflayer adapter communicate on port 5050 (WebSocket). This connection is unauthenticated — any process on the same host can send arbitrary commands to the bot. SEC-01 (REST API key) was completed in Sprint 30. SEC-02 closes the adapter boundary.

**Acceptance criteria:**
- [ ] Shared secret configurable via env var (e.g., `Agent__AdapterSecret`)
- [ ] .NET side validates the secret on incoming WebSocket connections from the adapter
- [ ] Node.js adapter (`MineflayerAdapter/index.js`) sends the secret in its handshake
- [ ] Secret is never logged at any log level
- [ ] One test verifies that a connection without the secret is rejected

#### P1-2: ApiKeyMiddleware Rejection Path Tests

**Context:** `ApiKeyMiddleware` has no test coverage for the rejection path. Sprint 30 council and both peer reviewers independently flagged this. The middleware is a security component.

**Acceptance criteria:**
- [ ] At least one test: valid key → request proceeds (happy path)
- [ ] At least one test: missing `X-Api-Key` header → 401
- [ ] At least one test: invalid key value → 401
- [ ] Tests added to `Sprint30Tests.cs` or a new `Sprint32Tests.cs` fixture

#### P1-3: WorldState.SetFact Deprecation Annotation (from P2-C)

**File:** `WorldState.cs` (wherever it is — confirm path with source search)

**Acceptance criteria:**
- [ ] `[Obsolete("Use StructuredFacts or Facts dictionary directly. SetFact write-path is not used in production.")]` applied to `SetFact`
- [ ] No existing call sites produce errors (warnings acceptable)
- [ ] XML doc on SetFact mentions the intended replacement pattern

---

### P2 — Normal Priority: New Audit Findings + Documentation

#### P2-1: GoalFactory Null Return Visibility (NEW — from refinement audit)

**Context:** `GoalFactory.CreateAsync` returns `null` for missing registry, missing item, and unknown goal type. All three collapse into one undifferentiated null. Caller code cannot distinguish "goal type unknown" from "service unavailable" from "item not found". Currently uses `Debug.WriteLine`.

**Acceptance criteria (minimum):**
- [ ] Replace `Debug.WriteLine` calls in GoalFactory with structured `ILogger` warnings
- [ ] Each failure path emits a distinct log message identifying which condition failed
- [ ] No breaking change to `IGoalFactory` interface for this sprint (full `GoalCreationResult` type is future scope)

#### P2-2: HtnPlanner.ReplanAsync — Thread failureReason (from DEF-B, 2 independent audits)

**Context:** `ReplanAsync(IGoal currentGoal, WorldState state, string? failureReason)` accepts `failureReason` but does not use it. Two independent audits converge on this finding (Sprint 25 deep-code-audit + today's refinement audit).

**Acceptance criteria:**
- [ ] `failureReason` is at minimum logged at `LogDebug` or `LogInformation` level at replan entry
- [ ] If `failureReason` is non-null, a journal entry of type `ReplanTriggered` includes the reason in its details
- [ ] No behavior change required for Sprint 32 (just make the context visible; adaptive replanning is future scope)

#### P2-3: Sprint28Tests.cs Fixture Annotation (from P2-1)

**Acceptance criteria:**
- [ ] Comment in the relevant fixture explaining the test setup or non-obvious behavior

#### P2-4: Register(string, ITool) Collision Semantics XML Doc (from P2-2)

**File:** `Agent.Tools/ToolDispatcher.cs`

**Acceptance criteria:**
- [ ] XML doc on `Register(string name, ITool tool)` specifies what happens when `name` is already registered (current behavior: silent overwrite per `_tools[name] = tool`)

#### P2-5: Address Reflection Test Fragility (from P2-3)

**Context:** `Sprint30Tests.cs` uses `System.Reflection` to test private methods on `BuildGoalDecomposer` and `ChatInterpreter`. Signature changes will silently produce wrong results, not compile errors.

**Acceptance criteria (one of):**
- [ ] Private methods promoted to `internal` + `[assembly: InternalsVisibleTo("...Tests")]` added to main project; OR
- [ ] Tests rewritten to test observable behavior (e.g., Decompose output) rather than private implementation; OR
- [ ] Comment in Sprint30Tests.cs explicitly documents which method signatures must be kept stable and why reflection is used

---

### Infrastructure — CI

#### INF-1: GitHub Actions Workflow for sprint-5-tool-safety

**IMPORTANT:** The GitHub MCP OAuth token cannot write `.github/workflows/`. Provide the YAML below to the user to add manually via the GitHub web UI.

**YAML to provide to user:**
```yaml
name: CI
on:
  push:
    branches: [sprint-5-tool-safety]
  pull_request:
    branches: [sprint-5-tool-safety]
jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Restore
        run: dotnet restore
      - name: Build
        run: dotnet build --configuration Release --no-restore -p:CopilotSkipCliDownload=true
      - name: Test
        run: dotnet test --no-build --configuration Release -p:CopilotSkipCliDownload=true
```

**Acceptance criteria:**
- [ ] Workflow triggers on push and PR to branch
- [ ] Build and test steps present
- [ ] Workflow passes (green) on HEAD after P0 fixes are in place
- [ ] Link to passing CI run included in Sprint 33 handoff document

---

## V. Definition of Done for Sprint 32

### Gate 1 — Build (blocks everything)
- [ ] **`dotnet build --configuration Release` exits 0** — verified in a live environment, not asserted
- [ ] Build output attached to Sprint 33 handoff document
- [ ] No base64-stored C# files remain

### Gate 2 — Tests
- [ ] **`dotnet test --no-build --configuration Release` exits 0**
- [ ] Test count ≥ 261 and reported accurately
- [ ] ApiKeyMiddleware rejection path covered by at least 2 tests

### Gate 3 — Security
- [ ] SEC-02 (adapter shared secret) implemented and tested
- [ ] No API key or shared secret committed in plaintext

### Gate 4 — Quality
- [ ] `WorldState.SetFact` marked `[Obsolete]`
- [ ] GoalFactory failures emit structured log messages (not `Debug.WriteLine`)
- [ ] `HtnPlanner.ReplanAsync` threads `failureReason` to a log/journal entry

### Gate 5 — CI
- [ ] CI workflow provided to user for manual upload
- [ ] CI is green on HEAD after fixes

### Gate 6 — Council
- [ ] Sprint 32 council session conducted, 6-seat format
- [ ] No blocking findings open at session close
- [ ] Session average confidence ≥ 75%
- [ ] Sprint 33 handoff published to `Data/Pages/Tasks/agent-handoff-sprint33.md`

---

## VI. Audit Finding Disposition Register

This register is maintained for future auditors to avoid re-investigating closed items.

| Finding ID | Source Audit | Claim | Disposition | Sprint |
|---|---|---|---|---|
| AUD-01 | deep_code_audit_sprint5 | ToolDispatcher has TODO, validation not implemented | **REFUTED** — full ValidateAgainstSchema present | Sprint 31 |
| AUD-02 | deep_code_audit_sprint5 | WorldModel state aliasing unfixed | **STALE** — Sprint 25 P1-A fixed | Sprint 31 |
| AUD-03 | deep_code_audit_sprint5 | ToolDispatcher no exception wrapping | **STALE** — Sprint 25 P0-C fixed | Sprint 31 |
| AUD-04 | exec-summary-audit (Audits/) | CI failure on PR head | **STALE** — Sprint 25 CAS fix | Sprint 31 |
| AUD-05 | exec-summary-audit (Audits/) | Sprint 5/6 goals substantially delivered | **CONFIRMED** | Sprint 31 |
| AUD-06 | exec-summary-audit (Audits/) | Planner GOAP/LLM enum values without routing | **CONFIRMED** — intentional staged design | Sprint 31 |
| AUD-07 | deep_code_audit_sprint5 | Build origin (0,0,0) fallback | **PARTIALLY ADDRESSED** — LogWarning added Sprint 28; hard failure is DEF-E | Sprint 31 |
| AUD-08 | deep_code_audit_sprint5 | Journal trim best-effort semantics | **INTENTIONAL** — documented design decision | Sprint 31 |
| AUD-09 | deep_code_audit_sprint5 | Decomposer routing order-dependent | **VALID — BACKLOG** | Sprint 31 |
| AUD-10 | deep_code_audit_sprint5 | Blueprint lookup too broad | **VALID — BACKLOG** | Sprint 31 |
| AUD-11 | refinement-audit-20260620 | GoalFactory null returns undifferentiated | **VALID — P2-1** | Sprint 32 |
| AUD-12 | refinement-audit-20260620 + deep_audit | HtnPlanner.ReplanAsync ignores failureReason | **VALID — P2-2** | Sprint 32 |
| AUD-13 | refinement-audit-20260620 | Game error channel reads 1 per cycle | **VALID — backlog DEF-C** | Sprint 33+ |
| AUD-14 | refinement-audit-20260620 | Correlation ID not used for completion | **VALID — verify in Sprint 32** | Sprint 32 |
| AUD-15 | refinement-audit-20260620 | Tests use real-clock polling | **VALID — backlog DEF-H** | Sprint 33+ |

---

## VII. Appendix: File Locations for Sprint 32 Agent

| File | Purpose | Note |
|---|---|---|
| `WebUI.Blazor/Program.cs` | Entry point, DI wiring | Fix BLK-01 here; check for base64 encoding first |
| `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` | Goal decomposition, 2-param constructor | Do not change constructor without updating Program.cs |
| `Agent.Tools/ToolDispatcher.cs` | Tool dispatch and validation | Check for base64 encoding |
| `Agent.Core/WorldStateProjector.cs` | Event projection to WorldState | Check for base64 encoding |
| `WebUI.Blazor/ApiKeyMiddleware.cs` | API key enforcement for `/api/*` | Check for base64 encoding; add rejection tests |
| `MemorySmith.Agent.Tests/Sprint30Tests.cs` | Sprint 30 tests | Check for base64 encoding; add middleware tests here |
| `MineflayerAdapter/index.js` | Node.js Minecraft adapter | Add shared secret (P1-1) |
| `Data/Pages/council/sprint31-council-20260620.md` | This sprint's council session | Reference for context |

---

## VIII. Appendix: Commit Reference

| Commit | Description |
|---|---|
| `87bc1a5c` | Current HEAD — audit file uploads only, no C# changes |
| `f58a91404f` | Sprint 31 handoff document |
| `a7fb0cc5` | Sprint 30 council review |
| `8e1b260c` | Sprint30Tests.cs created (check for base64) |
| `280dbd63` | ApiKeyMiddleware wired into pipeline |
| `f3b860c5` | ApiKeyMiddleware.cs created (check for base64) |
| `7fe8e117` | ChatInterpreter TrimEnd + regex fixes |
| `3bbaf593` | Version bump v0.28.0 |
| `c34bd3d8` | ToolDispatcher.cs decode attempt (check result) |
| `d457e0a2` | WorldStateProjector.cs decode attempt (check result) |
| `f96f842e` | SearchMemoryTool ITool compliance |
| `a572d88f` | CreatePageTool ITool compliance |

---

*Handoff issued 2026-06-20 by the MemorySmith.Agent Engineering Council (Sprint 31 audit session).*  
*Next review: Sprint 32 council session — schedule after Gate 1 (build passing) is confirmed with attached build output.*
