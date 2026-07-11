# Agent Handoff — Sprint 31

**Branch:** `sprint-5-tool-safety`
**Incoming HEAD:** `7da15ee682694e7c677acfd8187b000ecc40a40f`
**Handoff date:** 2026-06-20
**Prepared by:** MemorySmith.Agent Engineering Council (Sprint 30 session)
**Companion document:** `Data/Pages/council/sprint30-council-20260620.md`

---

## I. What Sprint 30 Delivered

Sprint 30 resolved all known compile-blocking issues that predated it, with one critical caveat described in Section II. The following changes are committed and confirmed at HEAD.

### Confirmed Code Changes

| Item | File(s) | Commits | Status |
|------|---------|---------|--------|
| P0-A | `WorldStateProjector.cs`, `ToolDispatcher.cs` | d457e0a2, c34bd3d8 | Confirmed — decoded from base64, valid C# committed |
| P0-B | `SearchMemoryTool.cs`, `CreatePageTool.cs` | f96f842e, a572d88f | Confirmed — `ExecuteAsync` now accepts `JsonElement` per `ITool` interface |
| P0-D | `Program.cs` | 3bbaf593 | Confirmed — version string updated v0.27.0 → v0.28.0; file decoded from base64 |
| P1-A | `Sprint30Tests.cs` | tests_commit | Confirmed — structural verification tests added |
| P1-B | `Sprint30Tests.cs` | tests_commit | Confirmed — 3 reflection-based `ReadOriginFact` logger invocation tests added |
| P1-C | `ApiKeyMiddleware.cs`, `Program.cs` | f3b860c5, 280dbd63 | Confirmed — middleware created and wired via `app.UseWhen` for `/api/*` |
| P1-D | `ChatInterpreter.cs` | 7fe8e117 | Confirmed — `TrimEnd('s')` heuristic removed; explicit-map-only `ResolveItemId` |
| P1-E | `ChatInterpreter.cs` | 7fe8e117 | Confirmed — bare `\bdoing\b` token removed from status regex |
| P2-B/D | `GenericGatherGoal.cs` | 7da15ee6 | Confirmed — `HasFailed` doc comments added |

### What Was NOT Delivered (Deferred from Sprint 30)

| Item | Description | New Priority |
|------|-------------|-------------|
| P2-A | SEC-02: Node.js port 5050 shared secret | P1 (council recommends upgrade) |
| P2-C | `WorldState.SetFact` deprecation annotation | P2 |
| P2-E | Sprint28Tests.cs fixture annotation comment | P2 |
| P2-F | `Register(string, ITool)` collision semantics XML doc | P2 |
| CI | GitHub Actions workflow for `sprint-5-tool-safety` | Infrastructure |

---

## II. Critical Invariants — Read Before Writing Any Code

### BLOCKING FINDING: BLK-01 — Program.cs DI Registration Compile Error

**This is the reason the build is still broken. Sprint 31 must fix this first.**

When Program.cs was decoded from base64 and re-committed in Sprint 30 (commit `3bbaf593`), the decoded content retained a DI registration call with the wrong arity:

```csharp
// CURRENT (BROKEN) — in Program.cs DecomposerRegistry setup:
reg.Register(new BuildGoalDecomposer(lib));
```

`BuildGoalDecomposer`'s constructor (as modified in Sprint 28) requires two parameters:

```csharp
public sealed class BuildGoalDecomposer(HtnTaskLibrary taskLibrary, ILogger<BuildGoalDecomposer> logger) : IGoalDecomposer
```

The fix is to pass an `ILogger<BuildGoalDecomposer>` instance as the second argument. The corrected call should resemble:

```csharp
// CORRECT — Sprint 31 must produce something equivalent to this:
reg.Register(new BuildGoalDecomposer(lib, loggerFactory.CreateLogger<BuildGoalDecomposer>()));
```

**Sprint 31 must audit Program.cs to confirm:**
1. Where `loggerFactory` is obtained (from `builder.Services.BuildServiceProvider()`, `WebApplication.Services`, or `builder.Logging` — the exact pattern depends on Program.cs initialization order).
2. That no other constructor call sites have similar arity mismatches after Sprint 28's logger parameter addition.
3. That `dotnet build` exits 0 after the fix.

**History note:** This bug was latent since Sprint 28 (when the logger parameter was added to `BuildGoalDecomposer`). It was invisible while Program.cs was base64-encoded because the base64 content was never compiled. Sprint 30 exposed the bug by decoding the file. The bug is not new to Sprint 30, but it is now blocking.

### Invariant: Do Not Merge Until Build Is Green

No commits to `sprint-5-tool-safety` should be merged to any integration branch until `dotnet build` exits 0 and the full test suite passes. This has been the implicit invariant since Sprint 28 and is now explicit.

### Invariant: Reflection Tests Are Fragile

`Sprint30Tests.cs` tests private methods on `BuildGoalDecomposer` via `System.Reflection`. If private method signatures change, these tests will not produce compile errors — they will either throw at runtime or silently find no target and produce false negatives. Every private method rename or parameter change must be accompanied by a review of Sprint30Tests.cs.

### Invariant: Program.cs Is No Longer Base64-Encoded

As of commit `3bbaf593`, `Program.cs` is stored as plain C# in the repository. The same is true for `WorldStateProjector.cs` (d457e0a2) and `ToolDispatcher.cs` (c34bd3d8). Do not re-encode these files. If a future commit needs to modify them, edit the C# source directly.

### Invariant: ApiKeyMiddleware Applies Only to /api/* Routes

The middleware is wired with `app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), ...)`. Routes outside `/api/*` are not protected by API key enforcement. This is intentional for Sprint 30 scope. If the protection boundary needs to change, the `UseWhen` predicate must be updated.

---

## III. CI Status

**CI is not running on this branch.**

No `.github/workflows/` CI trigger exists for `sprint-5-tool-safety`. Every build verification claim in every sprint to date has been asserted without a live build environment. This is the root cause of why BLK-01 was not caught during Sprint 30 development.

Sprint 31 should treat CI setup as infrastructure-blocking. The definition of "build is green" cannot be verified without a CI pipeline.

**Minimum viable CI for Sprint 31:**

```yaml
# .github/workflows/ci.yml (target state)
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
          dotnet-version: '8.0.x'
      - run: dotnet build --configuration Release
      - run: dotnet test --no-build --configuration Release
```

Until this workflow exists and is passing, all "build confirmed" claims are assertions, not verifications.

---

## IV. Sprint 31 Tasks

Tasks are ordered by priority. Sprint 31 must not proceed past P0 until P0 acceptance criteria are met.

---

### P0 — Blocking: Restore Build to Compilable State

#### P0-1: Fix BuildGoalDecomposer DI Registration in Program.cs

**File:** `Program.cs`
**Commits to produce:** One focused commit fixing the `reg.Register(new BuildGoalDecomposer(lib))` call to include `ILogger<BuildGoalDecomposer>`.

**Acceptance criteria:**
- [ ] `reg.Register(new BuildGoalDecomposer(lib, <logger>))` compiles without error
- [ ] The logger argument is sourced correctly from the ASP.NET Core DI container or `ILoggerFactory` — not hardcoded or null
- [ ] No `#pragma warning disable` or `null!` suppression used to paper over the error
- [ ] The fix is reviewed by a second set of eyes before commit

**Investigative step required:** Before writing the fix, read the full `Program.cs` initialization sequence to determine where `ILoggerFactory` is available. The fix pattern will depend on whether `WebApplication.CreateBuilder` or `Host.CreateDefaultBuilder` is used and where the `DecomposerRegistry` is configured relative to `app.Build()`.

#### P0-2: Verify `dotnet build` Exits 0

**Acceptance criteria:**
- [ ] `dotnet build --configuration Release` exits with code 0 in a live build environment (not asserted — actually run)
- [ ] Zero build errors in the output
- [ ] Build output (stdout/stderr) is captured and attached to the Sprint 31 handoff document
- [ ] If new compile errors surface (beyond BLK-01), they are triaged and either fixed in Sprint 31 or escalated as new blocking findings

#### P0-3: Verify `dotnet test` Exits 0

**Acceptance criteria:**
- [ ] `dotnet test --no-build --configuration Release` exits with code 0
- [ ] All Sprint30Tests.cs tests pass (structural + reflection-based)
- [ ] No tests in any existing test file are newly failing
- [ ] Test output is captured and attached to the Sprint 31 handoff document

---

### P1 — High Priority: Complete Deferred Sprint 30 P2 Items

These items were P2 in Sprint 30. The council recommends SEC-02 be promoted to P1 given the security posture of the project.

#### P1-1: SEC-02 — Node.js Port 5050 Shared Secret (Promoted from P2-A)

**Context:** Inter-process communication between the .NET agent and any Node.js component on port 5050 is currently unauthenticated. Any process on the same host can reach this endpoint.

**Acceptance criteria:**
- [ ] A shared secret is established for port 5050 communication (environment variable preferred over hardcoded value)
- [ ] The .NET side validates the secret on incoming connections from the Node.js component
- [ ] The Node.js side sends the secret in each request
- [ ] The secret is not logged at any log level
- [ ] A test verifies that a request without the secret is rejected

#### P1-2: ApiKeyMiddleware Rejection Path Test

**Context:** The council and both peer reviewers flagged that `ApiKeyMiddleware` has no test coverage for the rejection path (invalid/missing key → 4xx response). This is a correctness gap for a security component.

**Acceptance criteria:**
- [ ] At least one test exercises the happy path (valid key → request proceeds)
- [ ] At least one test exercises the rejection path (missing key → 401)
- [ ] At least one test exercises the rejection path (invalid key → 401 or 403)
- [ ] Tests are added to `Sprint30Tests.cs` or a new `Sprint31Tests.cs` file

#### P1-3: WorldState.SetFact Deprecation Annotation (from P2-C)

**File:** `WorldState.cs`
**Acceptance criteria:**
- [ ] `[Obsolete("Use ... instead")]` attribute applied to `SetFact`
- [ ] The replacement method or pattern is named in the obsolete message
- [ ] No existing call sites produce errors (warnings are acceptable; errors are not)

---

### P2 — Normal Priority: Documentation and Code Quality

#### P2-1: Sprint28Tests.cs Fixture Annotation Comment (from P2-E)

**Acceptance criteria:**
- [ ] Comment added to the relevant fixture in `Sprint28Tests.cs` explaining the test setup or any non-obvious behavior

#### P2-2: Register(string, ITool) Collision Semantics XML Doc (from P2-F)

**File:** Wherever `Register(string, ITool)` is defined (likely `ToolRegistry.cs` or similar)
**Acceptance criteria:**
- [ ] XML doc comment on `Register(string, ITool)` specifies what happens when a key is already registered: exception, silent overwrite, or return value indicating collision

#### P2-3: Address Reflection Test Fragility (New)

**Context:** `Sprint30Tests.cs` uses `System.Reflection` to test private methods on `BuildGoalDecomposer`. This approach breaks silently on signature changes.

**Acceptance criteria (one of the following):**
- [ ] Private methods under test are promoted to `internal`, and `[assembly: InternalsVisibleTo("...Tests")]` is added to the main project; OR
- [ ] Tests are rewritten to test observable behavior rather than private implementation; OR
- [ ] A code comment in Sprint30Tests.cs explicitly documents the fragility and names the method signatures that must be kept stable

---

### Infrastructure — CI Setup

#### INF-1: GitHub Actions Workflow for sprint-5-tool-safety

**File to create:** `.github/workflows/ci.yml`

**Acceptance criteria:**
- [ ] Workflow triggers on `push` and `pull_request` to `sprint-5-tool-safety`
- [ ] `dotnet build --configuration Release` step present and blocking
- [ ] `dotnet test --no-build --configuration Release` step present and blocking
- [ ] Workflow passes (green) on the HEAD commit after P0 fixes are in place
- [ ] Workflow badge visible in repository README or linked from the handoff document

---

## V. Definition of Done for Sprint 31

Sprint 31 is complete when all of the following are true. Items are listed in the order they must be verified.

### Gate 1 — Build (required before anything else)
- [ ] **`dotnet build --configuration Release` exits 0** — verified in a live environment, not asserted
- [ ] Build output attached to Sprint 32 handoff document
- [ ] Zero warnings treated as errors (or all remaining warnings are explicitly suppressed with a comment explaining why)

### Gate 2 — Tests
- [ ] **`dotnet test --no-build --configuration Release` exits 0**
- [ ] Test output attached to Sprint 32 handoff document
- [ ] All tests in Sprint30Tests.cs pass
- [ ] ApiKeyMiddleware rejection path covered by at least two tests

### Gate 3 — Security
- [ ] SEC-02 (port 5050 shared secret) implemented and tested
- [ ] ApiKeyMiddleware rejection path tests pass (see Gate 2)
- [ ] No API key or shared secret value is committed to the repository in plaintext

### Gate 4 — Deferred Items
- [ ] `WorldState.SetFact` marked `[Obsolete]` with a named replacement
- [ ] Sprint28Tests.cs fixture comment added
- [ ] `Register(string, ITool)` collision semantics documented

### Gate 5 — CI
- [ ] `.github/workflows/ci.yml` exists, triggers on the branch, and is green
- [ ] A link to the passing CI run is included in the Sprint 32 handoff document

### Gate 6 — Council Review
- [ ] Sprint 31 council session conducted with the 7-seat format
- [ ] No BLOCKING findings open at session close
- [ ] Session average confidence >= 78%
- [ ] Sprint 32 handoff document published to `Data/Pages/Tasks/agent-handoff-sprint32.md`

---

## Appendix: File Locations for Sprint 31 Agent

| File | Purpose | Note |
|------|---------|------|
| `Program.cs` | Entry point, DI wiring | Fix BLK-01 here first |
| `BuildGoalDecomposer.cs` | Goal decomposition, 2-param constructor | Do not change constructor signature without updating Program.cs |
| `ApiKeyMiddleware.cs` | API key enforcement for `/api/*` | Add rejection path tests |
| `WorldState.cs` | State store | Add `[Obsolete]` to `SetFact` |
| `ChatInterpreter.cs` | Intent resolution | Do not reintroduce `TrimEnd('s')` heuristic |
| `Sprint30Tests.cs` | Test file | Add middleware tests here or in Sprint31Tests.cs |
| `.github/workflows/ci.yml` | CI workflow | Create this file |
| `Data/Pages/council/sprint30-council-20260620.md` | Sprint 30 council session | Reference for context |

---

## Appendix: Commit Reference

| Commit | Description |
|--------|-------------|
| `6a284654` | Sprint 29/30 handoff (sprint window start) |
| `d457e0a2` | WorldStateProjector.cs decoded from base64 |
| `c34bd3d8` | ToolDispatcher.cs decoded from base64 |
| `f96f842e` | SearchMemoryTool.cs ExecuteAsync → JsonElement |
| `a572d88f` | CreatePageTool.cs ExecuteAsync → JsonElement |
| `3bbaf593` | Program.cs v0.27.0 → v0.28.0, decoded from base64 |
| `f3b860c5` | ApiKeyMiddleware.cs created |
| `280dbd63` | ApiKeyMiddleware wired into ASP.NET pipeline |
| `7fe8e117` | ChatInterpreter: TrimEnd removed, regex token removed |
| `7da15ee6` | GenericGatherGoal HasFailed doc comments (sprint HEAD) |

---

*Handoff issued 2026-06-20 by the MemorySmith.Agent Engineering Council.*
*Next review: Sprint 31 council session — schedule after Gate 1 (build passing) is confirmed.*
