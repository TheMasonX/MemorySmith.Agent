# Agent Handoff — Sprint 34

**Branch:** `sprint-5-tool-safety`
**Incoming HEAD:** `d47ada7f320609b9472cf8a9196c583296ca7c6b`
**Handoff date:** 2026-06-20
**Prepared by:** MemorySmith.Agent Engineering Council (Sprint 33 session)
**Companion document:** `Data/Pages/council/sprint33-council-20260620.md`

---

## I. What Sprint 33 Delivered

Sprint 33 was a **critical build-restoration sprint**. 11 of 12 tasks completed:

**BLK-S33-01 fixed — Program.cs missing section restored:**
- The Options binding, `var agentEnabled` declaration, `IWorldAdapter` registration, and `AddSingleton<IMemoryGateway>` lambda opener were missing from Program.cs since Sprint 28/29 (truncation in the original base64 source, never caught because `dotnet build` was never run in a live environment).
- Reconstructed from Sprint 23 main-branch baseline. Commit `f2108ba2`.

**P0-2 (DEF-S32-A): GoalFactory ILogger wired in DI** — `sp.GetRequiredService<ILogger<GoalFactory>>()` passed as 3rd arg. Commit `f2108ba2`.

**P0-3 (DEF-S32-G): HtnPlanner ILogger wired in DI** — `sp.GetRequiredService<ILogger<HtnPlanner>>()` passed as 2nd arg. Commit `f2108ba2`.

**P1-1 (DEF-S32-F): /api/about Phase** — updated to "Sprint 33 — Build verify, DI logger wiring, base64 sweep, Rule E-2". Commit `f2108ba2`.

**P1-2 (DEF-S32-C): TestHost package** — `Microsoft.AspNetCore.Mvc.Testing 10.0.0` added to test .csproj. Commit `e43e7410`.

**P1-3 (DEF-S32-B): SetFact migration** — 6 deprecated 2-arg `SetFact(string, object?)` calls migrated to `SetFact(string, string, FactSource.Observed)` in 3 test files. Required because `TreatWarningsAsErrors=true` turns CS0618 into a compile error. Commits `66083cbb`, `231545ec`, `d47ada7f`.

**P2-1 (DEF-S32-I): AGENTS.md Rule E-2** — Documents the double-encoding trap and correct pattern. Commit `fac40acd`.

**P2-2 (DEF-S32-H): AdapterSecret startup warning** — `LogDebug` at startup when `AdapterSecret` is null. Commit `f2108ba2`.

**Base64 sweep:** README.md and `Agent.Memory/RestMemoryGatewayOptions.cs` decoded from base64. Commits `47478e4c`, `a69ac953`.

**P0-1 (DEF-S32-E): PENDING** — Live `dotnet build` was launched as a background agent but result was not available before council close.

---

## II. Critical Invariants — Read Before Writing Any Code

### MUST-DO: Run dotnet build (BLK-S33-02, MUST)

**Sprint 33 did NOT confirm a live build result.** Sprint 34 MUST run `dotnet build --configuration Release -p:CopilotSkipCliDownload=true` as its first action and attach the full output.

If the build fails, triage each error as a new blocking finding before proceeding to any other tasks.

Use the sandbox build recipe from memory:
1. Install .NET 10 SDK via `builds.dotnet.microsoft.com`
2. Fetch source tarball: `curl -sL https://codeload.github.com/TheMasonX/MemorySmith.Agent/tar.gz/d47ada7f...`
3. Set up authproxy (authproxy.py pattern — inject Proxy-Authorization into CONNECT requests on 127.0.0.1:9081)
4. `dotnet restore` with proxy env vars (both upper and lowercase HTTPS_PROXY/https_proxy)
5. `dotnet build MemorySmith.Agent.slnx --configuration Release -p:CopilotSkipCliDownload=true`

### Invariant: Check first bytes after every file commit

After `github__create_or_update_file`, fetch the raw file from GitHub and verify the first 10 bytes are not base64 (i.e., do NOT start with `Ly8g` (= `// `), `bmFtZXM` (= `namespace`), `dXNpbmcg` (= `using `), etc.). A 200 HTTP response does not guarantee plain text was stored.

### Invariant: Always pass plain text content (Rule E-2)

Never pass `base64.b64encode(content).decode()` as the `content` field in a `github__create_or_update_file` paramsFile. The MCP action handles GitHub API base64 encoding transparently. Plain text → correct. Base64-encoded text → double-encoding → BLK-02 pattern.

---

## III. CI Status

No `.github/workflows/ci.yml` exists on `sprint-5-tool-safety`. The GitHub MCP OAuth token cannot write to `.github/workflows/`. Use the CI YAML from Sprint 33 handoff section III for manual upload via the GitHub web UI.

---

## IV. Sprint 34 Tasks

Tasks are ordered by priority.

### P0 — Must do first

#### P0-1: Run dotnet build + dotnet test (BLK-S33-02 / DEF-S32-E, MUST)

**Acceptance criteria:**
- [ ] `dotnet build --configuration Release -p:CopilotSkipCliDownload=true` exits code 0
- [ ] Full build output (stdout/stderr) attached to Sprint 35 handoff
- [ ] If build fails: triage all errors as new blocking findings before continuing
- [ ] `dotnet test` exits code 0 with 276+ passed, 0 failed
- [ ] Test output attached to Sprint 35 handoff

#### P0-2: Comprehensive base64 sweep (DEF-S33-D)

Scan ALL .cs files on the branch (approximately 100 files across Agent.Core, Agent.Planning, Agent.Tools, Agent.Construction, Agent.Memory, Agent.Personality, Agent.Vision, Agent.World.Minecraft, WebUI.Blazor, MemorySmith.Agent.Tests) for base64 encoding.

Detection heuristic: fetch raw file from GitHub; if first 50 bytes match the base64 alphabet only (A-Za-z0-9+/=) and contain no C# tokens (no spaces, tabs, `{`, `}`, `;`), it's base64-encoded.

**Acceptance criteria:**
- [ ] All .cs files scanned
- [ ] Any base64-encoded files decoded and committed as plain text
- [ ] Report: number of files scanned, number found encoded, any new files decoded

---

### P1 — High Priority (after P0)

#### P1-1: Verify WebApplicationFactory entrypoint (DEF-S33-A)

**File:** `MemorySmith.Agent.Tests/Sprint32Tests.cs`

Check that `WebApplicationFactory<T>` generic type argument is the correct entry-point class from `WebUI.Blazor`. In .NET minimal-API projects, this is typically `Program` (implicitly defined by top-level statements) or the explicit `IWebHost` type.

If `WebApplicationFactory<Program>` cannot resolve because `Program` has no public declaration, add `public partial class Program {}` at the end of `WebUI.Blazor/Program.cs`.

**Acceptance criteria:**
- [ ] `WebApplicationFactory<T>` in Sprint32Tests.cs resolves without compile error
- [ ] ApiKeyMiddleware integration tests pass

#### P1-2: "Add files via upload" audit (DEF-S33-E)

Commit `87bc1a5c4da1622e1fd8455158e95eee9ba78cb3` ("Add files via upload") was pushed on 2026-06-20T13:46. Its contents are unknown. Check which files it added or modified and verify none are base64-encoded.

---

### P2 — Normal Priority

#### P2-1: AGENTS.md post-commit verification step (DEF-S33-C)

After Rule E-2, add a verification recipe:
```
After github__create_or_update_file, verify the file was stored as plain text:
curl -sL https://raw.githubusercontent.com/<owner>/<repo>/<branch>/<path> | head -c 4 | xxd
# Expected: first 4 bytes are a valid C# token (e.g. "2F 2F 20" = "// ", "75 73 69" = "usi", "6E 61 6D" = "nam")
# NOT: "4C 79 38 67" (= "Ly8g" = base64-encoded "// ")
```

#### P2-2: AGENTS.md Rule E-2 clarification (DEF-S33-B)

Add: "The `content` value in the params JSON must be a raw text string, not a filename, blob reference, or byte array."

---

## V. Definition of Done for Sprint 34

### Gate 1 — Build
- [ ] `dotnet build --configuration Release` exits 0
- [ ] Build output (zero errors) attached to Sprint 35 handoff
- [ ] If new errors: triage and fix all before proceeding to Gate 2

### Gate 2 — Tests
- [ ] `dotnet test --no-build --configuration Release` exits 0
- [ ] 276+ tests passed, 0 failed
- [ ] Sprint32Tests.cs ApiKeyMiddleware tests pass

### Gate 3 — Quality
- [ ] Full .cs base64 sweep complete
- [ ] No new base64-encoded files found (or all found ones decoded)
- [ ] WebApplicationFactory<T> entrypoint verified

### Gate 4 — Council
- [ ] Sprint 34 council session conducted (6-seat + anon)
- [ ] No blocking findings open at session close
- [ ] Sprint 35 handoff published

---

## VI. Commit Reference (Sprint 33)

| Commit | Description |
|---|---|
| `d47ada7f` | fix(tests): migrate deprecated SetFact in Sprint21Tests |
| `231545ec` | fix(tests): migrate deprecated SetFact in Sprint19Tests |
| `66083cbb` | fix(tests): migrate deprecated 2-arg SetFact to 3-arg signature |
| `e43e7410` | fix(tests): add Microsoft.AspNetCore.Mvc.Testing for ApiKeyMiddleware tests |
| `fac40acd` | docs(agents): add Rule E-2 — plain text in github__create_or_update_file |
| `a69ac953` | fix(memory): decode RestMemoryGatewayOptions.cs from base64 |
| `47478e4c` | fix(docs): decode README.md from base64 + update to v0.28.0 Sprint 33 |
| `f2108ba2` | fix(core): restore missing Options section + wire DI loggers + Sprint 33 changes |

---

*Handoff issued 2026-06-20 by the MemorySmith.Agent Engineering Council (Sprint 33 session).*
*Next review: Sprint 34 council session — schedule after Gate 1 (build passing) is confirmed with attached build output.*
