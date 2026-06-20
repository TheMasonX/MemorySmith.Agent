# Agent Handoff — Sprint 33

**Branch:** `sprint-5-tool-safety`
**Incoming HEAD:** `6b941958d113dcdaa40c34d319ac647f44b8fb94`
**Handoff date:** 2026-06-20
**Prepared by:** MemorySmith.Agent Engineering Council (Sprint 32 session)
**Companion document:** `Data/Pages/council/sprint32-council-20260620.md`

---

## I. What Sprint 32 Delivered

Sprint 32 was a full implementation sprint. All 10 tasks completed:

**P0 — Build restoration:**
- 5 C# files + index.js decoded from base64 and recommitted as plain UTF-8 source
- BLK-01 fixed: `Program.cs` now passes `ILoggerFactory.CreateLogger<BuildGoalDecomposer>()` as second arg to `BuildGoalDecomposer` constructor
- Build is believed to compile (static inspection; live build not run — see DEF-S32-E below)

**P1 — Security and quality:**
- SEC-02 (adapter shared secret): `MinecraftAdapterConfig.AdapterSecret` → `WebSocketBridge` sends `{"type":"handshake","secret":"..."}` → `index.js` per-connection `isAuthenticated` state → WS_TOKEN injected in child process env
- ApiKeyMiddleware: 5 rejection-path tests (happy path, missing key, invalid key, non-API route)
- `WorldState.Builder.SetFact(string, object?)` marked `[Obsolete(...)]`

**P2 — Documentation and code quality:**
- GoalFactory: `Debug.WriteLine` replaced with `ILogger<GoalFactory>` warnings (2 distinct paths)
- HtnPlanner: `ReplanAsync` logs `failureReason` via `ILogger<HtnPlanner>`
- Sprint28Tests fixture annotation added
- ToolDispatcher `Register(string, ITool)` collision semantics documented in XML doc
- Sprint30Tests reflection stability contract documented

---

## II. Critical Invariants — Read Before Writing Any Code

### MUST-DO: Run dotnet build (DEF-S32-E)

**Sprint 32 did NOT run a live build.** All deliveries are based on static source inspection. Sprint 33 MUST run `dotnet build --configuration Release -p:CopilotSkipCliDownload=true` as its first action and attach the output to the Sprint 34 handoff document.

If the build fails, triage each error as a new blocking finding before proceeding to any other tasks.

Use the sandbox build recipe from the memory: install .NET 10 SDK, download source tarball, restore via authproxy, build with CopilotSkipCliDownload.

### Invariant: github__create_or_update_file — plain text content

When committing files using `github__create_or_update_file` with paramsFile, pass `content` as **plain UTF-8 text** (not base64-encoded). The MCP layer handles GitHub API base64 encoding transparently. Passing `base64.b64encode(content)` results in double-encoding (base64 text stored in git, which is the original BLK-02 problem). Rule E-2 should be added to AGENTS.md.

### Invariant: GoalFactory and HtnPlanner use NullLogger in production

Sprint 32 wired ILogger for GoalFactory and HtnPlanner but did NOT update Program.cs DI. In production, both use `NullLogger`. DEF-S32-A and DEF-S32-G must be addressed to make the new logging visible.

---

## III. CI Status

CI is still not running on this branch. No `.github/workflows/ci.yml` exists for `sprint-5-tool-safety`. The GitHub MCP OAuth token cannot write to `.github/workflows/`. Provide the YAML to the user for manual upload:

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

---

## IV. Sprint 33 Tasks

Tasks are ordered by priority.

### P0 — Must do first

#### P0-1: Run dotnet build (DEF-S32-E, MUST)

**Acceptance criteria:**
- [ ] `dotnet build --configuration Release -p:CopilotSkipCliDownload=true` exits code 0 in a live environment
- [ ] Build output (stdout/stderr) captured and included in Sprint 34 handoff
- [ ] Zero compile errors
- [ ] If new compile errors surface, triage as new blocking findings before continuing

#### P0-2: Wire GoalFactory ILogger in Program.cs (DEF-S32-A)

**File:** `WebUI.Blazor/Program.cs`

Current wiring:
```csharp
builder.Services.AddSingleton<GoalFactory>(sp => new GoalFactory(
    sp.GetRequiredService<IItemRegistry>(),
    sp.GetRequiredService<IBlueprintRepository>()));
```

Required change:
```csharp
builder.Services.AddSingleton<GoalFactory>(sp => new GoalFactory(
    sp.GetRequiredService<IItemRegistry>(),
    sp.GetRequiredService<IBlueprintRepository>(),
    sp.GetRequiredService<ILogger<GoalFactory>>()));
```

**Acceptance criteria:**
- [ ] GoalFactory registered with ILogger<GoalFactory> from DI
- [ ] Build exits 0 after change

#### P0-3: Wire HtnPlanner ILogger in Program.cs (DEF-S32-G)

Current: `new HtnPlanner(sp.GetRequiredService<HtnTaskLibrary>())`

Required: `new HtnPlanner(sp.GetRequiredService<HtnTaskLibrary>(), sp.GetRequiredService<ILogger<HtnPlanner>>())`

**Acceptance criteria:**
- [ ] HtnPlanner registered with ILogger<HtnPlanner> from DI
- [ ] Build exits 0 after change

---

### P1 — High Priority

#### P1-1: Update /api/about phase string (DEF-S32-F)

**File:** `WebUI.Blazor/Program.cs`

Change: `Phase = "Sprint 30 — Base64 decode, ITool compliance, SEC-01 middleware"` → `Phase = "Sprint 32 — Base64 decode complete, SEC-02 adapter auth, quality fixes"`

#### P1-2: Verify TestHost package (DEF-S32-C)

**File:** `MemorySmith.Agent.Tests/MemorySmith.Agent.Tests.csproj`

Check whether `Microsoft.AspNetCore.Mvc.Testing` or `Microsoft.AspNetCore.TestHost` is in the project file. If missing, add it. The 5 ApiKeyMiddleware tests in Sprint32Tests.cs require this package.

**Acceptance criteria:**
- [ ] `dotnet test` passes with Sprint32Tests.cs middleware tests

#### P1-3: Sweep WorldState.SetFact call sites (DEF-S32-B)

Search for `Builder.SetFact` call sites where the first overload (`string, object?`) is called. Either suppress with `#pragma warning disable CS0618` (with comment) or migrate to the `SetFact(string, string, FactSource)` overload.

**Acceptance criteria:**
- [ ] No CS0618 warnings in build output for SetFact

---

### P2 — Normal Priority

#### P2-1: AGENTS.md Rule E-2 (DEF-S32-I)

**File:** `AGENTS.md`

Add under the existing Rule E-1 section:

> **Rule E-2: github__create_or_update_file — pass plain text, not base64.**
> When writing C# or JS files via paramsFile, the `content` field must be plain UTF-8 text.
> The MCP layer handles GitHub API base64 encoding. Passing `base64.b64encode(content)` results
> in double-encoding and stores the base64 string as the file content (the original BLK-02 pattern).

#### P2-2: Startup warning for AdapterSecret/WS_TOKEN mismatch (DEF-S32-H)

**File:** `Agent.World.Minecraft/MinecraftAdapter.cs` or `WebUI.Blazor/Program.cs`

Add a warning log at startup if `AdapterSecret` is null/empty (so C# won't send handshake), since any externally-set `WS_TOKEN` on the Node.js side would prevent connection. Optional and low-priority.

#### P2-3: CI workflow to user (INF-1)

Provide the YAML above to the user for manual upload to `.github/workflows/ci.yml` via the GitHub web UI. The MCP token lacks the `workflow` scope and cannot write this path.

---

## V. Definition of Done for Sprint 33

### Gate 1 — Build
- [ ] `dotnet build --configuration Release` exits 0
- [ ] Build output attached to Sprint 34 handoff

### Gate 2 — Tests
- [ ] `dotnet test --no-build --configuration Release` exits 0
- [ ] All Sprint32Tests.cs middleware tests pass
- [ ] Test count >= 276 (Sprint 32 added 7 new tests to Sprint 30's ~269)

### Gate 3 — Quality
- [ ] GoalFactory wired with ILogger<GoalFactory>
- [ ] HtnPlanner wired with ILogger<HtnPlanner>
- [ ] /api/about Phase string updated
- [ ] No CS0618 warnings for SetFact

### Gate 4 — Council
- [ ] Sprint 33 council session conducted (5-seat + anon peer format)
- [ ] No blocking findings open at session close
- [ ] Sprint 34 handoff published

---

## VI. Commit Reference

| Commit | Description |
|---|---|
| `6b941958` | fix(tests): replace Exists() with Any() — BLK-S32-03 |
| `aa9c189b` | fix(sec): inject WS_TOKEN in StartNodeProcessAsync — BLK-S32-02 |
| `2c367c51` | test(sprint32): ApiKeyMiddleware + GoalFactory ILogger tests |
| `c655de3d` | docs(tests): Sprint28Tests fixture annotation |
| `15da08fa` | fix(quality): HtnPlanner logs failureReason |
| `e8b42f2c` | fix(quality): GoalFactory ILogger replaces Debug.WriteLine |
| `413ca593` | fix(quality): SetFact [Obsolete] annotation |
| `73392655` | feat(sec): pass AdapterSecret to WebSocketBridge |
| `9454b136` | feat(sec): WebSocketBridge handshake |
| `e0a0d8d7` | feat(sec): add AdapterSecret to MinecraftAdapterConfig |
| `78d1e816` | feat(sec): decode base64 + SEC-02 handshake in index.js |
| `d8c6e26e` | fix(core): decode WorldStateProjector.cs |
| `11c99361` | fix(tests): decode Sprint30Tests.cs + reflection stability |
| `6883ecec` | fix(core): decode ApiKeyMiddleware.cs |
| `c1cfdedf` | fix(core): decode + XML doc ToolDispatcher.cs |
| `f95f1d68` | fix(core): decode Program.cs + BLK-01 DI fix |

---

*Handoff issued 2026-06-20 by the MemorySmith.Agent Engineering Council (Sprint 32 session).*
*Next review: Sprint 33 council session — schedule after Gate 1 (build passing) is confirmed with attached build output.*
