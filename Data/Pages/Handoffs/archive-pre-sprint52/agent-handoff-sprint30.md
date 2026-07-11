# Agent Handoff — Sprint 30
**Date:** 2026-06-20
**Branch:** sprint-5-tool-safety
**Branch HEAD:** f0e13c44ade7795eb16afe916dab19493507892c ("Another batch of audits")
**Version:** v0.28.0 (bump pending — README/Program.cs/about not yet updated)

---

## Section I: What Just Happened (Sprint 28 + Sprint 29 synthesis)

Sprint 28 delivered the base64 decode sweep plus three code changes. Sprint 29 was a documentation and audit sprint — no production code was changed. This handoff covers both.

### Sprint 28 Code Deliveries (confirmed against source)

**P0-B: BuildGoalDecomposer LogWarning** — VERIFIED.
`ReadOriginFact` now emits `LogWarning` on both the missing-fact and unparseable-fact paths. `ILogger<BuildGoalDecomposer>` is injected via constructor. Silent zero-fallback is gone.

**P0-C: GenericGatherGoal HasFailed key** — VERIFIED.
Key format changed to `goal:Gather:{itemId}:{targetCount}:failed`. The change prevents cross-goal collision between gather-N and gather-M for the same item. **Important caveat**: the fact is only READ in production, never SET. No write site exists in `AgentBackgroundService` or anywhere else. The fix is forward-proofing; it has no runtime effect today. A write site is needed (see Sprint 30 P2-C / DEF-DOC-3).

**P1-A: PlannerRouter IPlanner + originalGoal** — VERIFIED.
Constructor broadened from `HtnPlanner` to `IPlanner`. `ReplanAsync` uses `originalGoal` when provided to route to the correct decomposer, preventing the silent HTN fallback that affected all decomposer-handled goals on replan. Note: `AgentBackgroundService` still calls `PlanAsync(_currentGoal)` — the `ReplanAsync` path is exercised by tests but not by the production replan flow today.

**P1-C: architecture.md journal semantics** — VERIFIED.
Section "Agent Journal Semantics" committed. Closes Deep Code Audit Finding 4.

**Base64 sweep: INCOMPLETE.**
13 files were decoded in Sprint 28. Two additional files remain base64-encoded as of HEAD f0e13c44:
- `Agent.Core/WorldStateProjector.cs` (known Sprint 29 deferral, still not fixed)
- `Agent.Tools/ToolDispatcher.cs` (NEW — missed in Sprint 28 sweep)

### Sprint 29 Deliveries

Sprint 29 added four audit markdown files to `Data/Pages/Audit/`:
- `MemorySmith_Agent_Audit_Sprint26.md` — deep architectural audit (v0.25.0 / Sprint 26 context)
- `memorysmith_agent_code_audit_report(1).md` — code audit against HEAD 6392007a
- `memorysmith_agent_deep_audit_report.md` — deep audit against HEAD 6392007a
- `memorysmith_agent_deep_audit_report (1).md` — same deep audit with additional second-pass section

No production code was changed in Sprint 29.

---

## Section II: Critical Invariants

All invariants from Sprint 28 remain in force. New invariants added in this synthesis:

**From Sprint 29 council review:**

13. `ITool.ExecuteAsync` takes `(JsonElement arguments, CancellationToken)`. All concrete tool implementations MUST use this signature. The `ActionData`-based signature (`ExecuteAsync(ActionData action, CancellationToken ct)`) is the old pre-Sprint-5 API and is no longer valid for `ITool` implementations. Do not add any new tools with the `ActionData` signature.

14. `ToolDispatcher.cs` and `WorldStateProjector.cs` are base64-encoded on disk and will be decoded in Sprint 30. After decoding, verify the content matches the expected C# implementation by checking that the file begins with a valid C# namespace declaration.

15. All `.cs` source files on this branch must be valid UTF-8 C#. A file whose entire content is a single long base64 string is compile-blocking. Sprint 30 P0-A must include a sweep pattern to catch any remaining encoded files before proceeding.

---

## Section III: CI Status

**CI status: NOT CONFIRMED (zero check-runs on all inspected SHAs)**

The GitHub Actions check-runs API returns `total_count: 0` for every SHA on the `sprint-5-tool-safety` branch, including HEAD `f0e13c44` and the Sprint 28 implementation commits. This means either:
- No CI workflow file is configured to run on this branch.
- A workflow exists but is not triggered by pushes to `sprint-5-tool-safety`.
- CI ran but check-run records have been purged (unlikely — GitHub retains for 90 days).

Sprint 30 P0-A must diagnose this:
1. Check `.github/workflows/` for existing workflow files.
2. If a workflow exists, verify its `on:` trigger includes `sprint-5-tool-safety` or `pull_request`.
3. If no workflow triggers this branch, the project has no automated build gate and every compile error persists silently.

**The absence of CI explains the persistence of compile-blocking issues across multiple sprints.** Without a green-build gate, base64 files and interface mismatches cannot be caught automatically.

---

## Section IV: Compile-Blocking Defects (Must Fix First)

The branch currently has at minimum 4 compile-blocking defects. No feature work, no test runs, and no CI can succeed until all four are resolved.

### B-1: WorldStateProjector.cs — base64-encoded
**File:** `Agent.Core/WorldStateProjector.cs`
**Evidence:** File content begins `bmFtZXNwYWNlIEFnZW50LkNvcmU7` = base64 for `namespace Agent.Core;`.
**Fix:** `base64 --decode Agent.Core/WorldStateProjector.cs > /tmp/wsp.cs && mv /tmp/wsp.cs Agent.Core/WorldStateProjector.cs`
**Verify:** First line of file starts with `namespace Agent.Core;`

### B-2: ToolDispatcher.cs — base64-encoded (NEW)
**File:** `Agent.Tools/ToolDispatcher.cs`
**Evidence:** File content begins `bmFtZXNwYWNlIEFnZW50LlRvb2xzOg==` = base64 for `namespace Agent.Tools:`.
**Fix:** `base64 --decode Agent.Tools/ToolDispatcher.cs > /tmp/td.cs && mv /tmp/td.cs Agent.Tools/ToolDispatcher.cs`
**Verify:** First line of file starts with `namespace Agent.Tools;`
**Note:** When decoded, ToolDispatcher contains the Sprint 5 schema validation logic. The validation IS present in the encoded content — decoding reveals it.

### B-3: SearchMemoryTool.cs — interface contract mismatch
**File:** `Agent.Tools/Tools/SearchMemoryTool.cs`
**Evidence:** Class declares `class SearchMemoryTool : ITool` but implements `ExecuteAsync(ActionData action, CancellationToken ct)`. `ITool` requires `ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)`. C# compile error: `'SearchMemoryTool' does not implement interface member 'ITool.ExecuteAsync(JsonElement, CancellationToken)'`.
**Root cause:** Sprint 5 changed `ITool` from `ActionData` to `JsonElement`. The file was base64-encoded before it could be updated. Sprint 28 decoded it, restoring the old signature.
**Fix:** Rewrite `ExecuteAsync` to accept `JsonElement` and extract `query` and `limit` from it:
```csharp
public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct)
{
    var query = arguments.TryGetProperty("query", out var q) ? q.GetString()
                : throw new ArgumentException("SearchMemory requires 'query' parameter.");
    var limit = arguments.TryGetProperty("limit", out var l) && l.TryGetInt32(out var li) ? li : 10;
    var results = await _memory.SearchAsync(query!, limit, ct).ConfigureAwait(false);
    return ToolResult.Ok(new { results });
}
```
Also add `InputSchema` property returning the tool's JSON Schema.

### B-4: CreatePageTool.cs — interface contract mismatch (presumed)
**File:** `Agent.Tools/Tools/CreatePageTool.cs`
**Evidence:** Two independent audit reports assert `CreatePageTool` uses `ExecuteAsync(ActionData)` like `SearchMemoryTool`. Not directly verified in this review.
**Fix:** Same pattern as B-3 — rewrite to `JsonElement` signature + add `InputSchema`.
**Verify before fixing:** Check if the file is base64-encoded first. If so, decode it before addressing the interface.

---

## Section V: Sprint 30 Task List

### P0 — Must complete before Sprint 30 is done

**P0-A: Decode all remaining base64 files + CI diagnosis**

Step 1 — Sweep for base64 files:
```bash
# Find any .cs file whose first non-empty line looks like base64 (>60 chars, only base64 chars)
grep -rl --include="*.cs" "^[A-Za-z0-9+/]\{60,\}=\{0,2\}$" . 2>/dev/null
```
At minimum, decode: `Agent.Core/WorldStateProjector.cs` and `Agent.Tools/ToolDispatcher.cs`.

Step 2 — CI diagnosis: check `.github/workflows/` for workflow files. Verify `on:` triggers include this branch. If no workflow covers `sprint-5-tool-safety`, add a trigger or create a minimal workflow.

**P0-B: Fix SearchMemoryTool and CreatePageTool interface compliance**

After decoding any base64 files (P0-A):
1. Check if `CreatePageTool.cs` is base64 or has an interface mismatch.
2. Rewrite `SearchMemoryTool.ExecuteAsync` to accept `JsonElement`.
3. Rewrite `CreatePageTool.ExecuteAsync` to accept `JsonElement`.
4. Add `InputSchema` properties to both tools if absent.
5. Verify no other tools have the old `ActionData` signature.

**P0-C: Verify build compiles clean**

After P0-A and P0-B:
```bash
dotnet build MemorySmith.Agent.slnx -c Release
```
Expected: exit code 0, zero errors. If any errors remain, fix before proceeding.

**P0-D: Bump version to v0.28.0**

Update in all three locations:
- `README.md` (version badge/header)
- Version constant in source (wherever `/api/about` reads it — likely `Program.cs`)
- `Program.cs` if version is defined there directly

Commit as `chore(v0.28.0): version bump`.

---

### P1 — High priority, complete after P0 is green

**P1-A: Confirm dotnet test passes**

After P0-A through P0-C:
```bash
dotnet test MemorySmith.Agent.slnx --logger "console;verbosity=normal"
```
Expected: 261+ tests pass, 0 failures, ~10 skips (CUDA/ONNX-model-dependent).
If any tests fail, diagnose and fix before any P2 work.

**P1-B: DEF-P0-B-logverify — BuildGoalDecomposer logger invocation tests**

Add tests that assert `LogWarning` IS called:
1. When `ReadOriginFact` is called with the origin fact key absent — `LogWarning` invoked exactly once.
2. When `ReadOriginFact` is called with an unparseable value — `LogWarning` invoked exactly once.
3. When a valid integer origin fact is present — `LogWarning` NOT invoked.

Use `FakeLogger<BuildGoalDecomposer>` from `Microsoft.Extensions.Logging.Testing` (available in .NET 8+) or write a minimal `CapturingLogger<T>` test-double.

**P1-C: SEC-01 — API key middleware on REST endpoints**

Add `ApiKeyMiddleware` to the ASP.NET Core pipeline for all `/api/*` routes:
```csharp
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseMiddleware<ApiKeyMiddleware>()
);
```
Key sourced from `appsettings.json` or environment variable `Agent__ApiKey`. Middleware must use `CryptographicOperations.FixedTimeEquals` for timing-safe comparison. Add a config note to AGENTS.md and getting-started.md.

**P1-D: DEF-NEW-6 — ChatInterpreter.ResolveItemId plural map**

Replace `TrimEnd('s')` heuristic with a constrained plural map: an explicit dictionary of known plurals → canonical item IDs, falling back to exact match only. Add regression test that `"grass"` does not match `"gra"`.

**P1-E: DEF-NEW-7 — Status regex bare `doing` token**

Remove the bare `\bdoing\b` token from the status-parsing regex. Add regression test that the string `"doing"` alone does not parse as a valid status.

---

### P2 — Complete if time permits

**P2-A: SEC-02 — Node.js port 5050 shared secret**

Add shared-secret validation to the Node.js bot layer (MineflayerAdapter/index.js):
```javascript
const AGENT_SECRET = process.env.AGENT_SHARED_SECRET;
app.use((req, res, next) => {
    if (req.headers['x-agent-key'] !== AGENT_SECRET) return res.status(401).json({ error: 'Unauthorized' });
    next();
});
```
The .NET layer injects the secret via `IConfiguration` and sends it as an HTTP header.

**P2-B: DEF-DOC-3 — HasFailed write path clarification**

Document in `GenericGatherGoal.cs` that `HasFailed` reads a fact that has no write site in the current production path. The `goal:Gather:{itemId}:{targetCount}:failed` key is reserved for future use when `AgentBackgroundService` is updated to write it. Callers adding a write site must use the exact format documented.

**P2-C: WorldState.SetFact legacy path — deprecation guidance**

Add an `[Obsolete]` attribute or XML doc comment to `Builder.SetFact(string, object?)` explaining that it bypasses `StructuredFacts` provenance and the MaxFacts cap. Direct callers to use the provenanced overload.

**P2-D: DEF-DOC-1 — GenericGatherGoal.HasFailed code comment**

Add comment at `HasFailed` property documenting the full key format `goal:Gather:{itemId}:{targetCount}:failed` so future authors setting this fact can find the expected format without checking commit history.

**P2-E: DEF-DOC-2 — Sprint28Tests.cs P0-B fixture annotation**

Add brief comment to P0-B test fixture: "These tests validate behavioral contracts (return value, not logger invocations). See DEF-P0-B-logverify (P1-B in Sprint 30) for logger-invocation verification."

**P2-F: Register(string, ITool) XML doc comment — DEF-9**

Document the collision semantics: when the same alias name is registered twice, the second registration silently overwrites the first. Callers should check `Get(name)` before registering an alias if collision detection is needed.

**P2-G: DEF-NEW-9 — MineWoodDecompose namespace prefix**

Remove `minecraft:` namespace prefix from `MineWoodDecompose`. The projector's `NormalizeInventory` strips this prefix, so tool parameters should not include it. Also expand log coverage if needed.

---

### P3 — Backlog (Sprint 31+)

- DEF-NEW-8: ExploreDecompose — extract retry budget as named policy
- DEF-NEW-10: ChatInterpreter.IsDirectedAtBot — wire `ChatOptions.MaxResponseDistanceBlocks`
- DEF-1: JS correlationId echo — verification protocol
- AgentBackgroundService God Object — formalize IActionDispatcher, IWorldObserver, IAgentLifecycleManager seams
- IPendingActionRepository — deep module for action persistence
- ITimeProvider in ReplanGovernor — inject abstraction for deterministic timing tests

---

## Section VI: Files Expected to Change in Sprint 30

| File | Reason | Priority |
|---|---|---|
| `Agent.Core/WorldStateProjector.cs` | Decode from base64 (B-1) | P0 |
| `Agent.Tools/ToolDispatcher.cs` | Decode from base64 (B-2) | P0 |
| `Agent.Tools/Tools/SearchMemoryTool.cs` | Fix ITool interface — JsonElement signature (B-3) | P0 |
| `Agent.Tools/Tools/CreatePageTool.cs` | Fix ITool interface — JsonElement signature (B-4) | P0 |
| `README.md` | Version bump to v0.28.0 (P0-D) | P0 |
| `Program.cs` | Version bump (P0-D) | P0 |
| Version constant source file (TBD) | `/api/about` version string (P0-D) | P0 |
| `WebUI.Blazor/ApiKeyMiddleware.cs` (new) | SEC-01 API key middleware (P1-C) | P1 |
| `Program.cs` | Wire API key middleware (P1-C) | P1 |
| `Agent.Core/ChatInterpreter.cs` (likely) | Plural map (P1-D), status regex (P1-E) | P1 |
| `MemorySmith.Agent.Tests/Sprint30Tests.cs` (new) | P1-A confirmation + P1-B logger tests | P1 |
| `Agent.Planning/Goals/GenericGatherGoal.cs` | DEF-DOC-3 code comment (P2-B) | P2 |
| `AGENTS.md` | SEC-01 config notes (P1-C) | P1 |
| `Data/Pages/guides/getting-started.md` | SEC-01 setup instructions (P1-C) | P1 |
| Any additional base64 files (TBD) | Decode if discovered in P0-A sweep | P0 |

---

## Section VII: GitHub and CI Tooling Reminders

**Check CI for a specific commit SHA:**
```
gh api repos/TheMasonX/MemorySmith.Agent/commits/{sha}/check-runs
```
Expected on a healthy branch: at least one `"status": "completed"`, `"conclusion": "success"`.

**Detect base64-encoded C# files:**
```bash
find . -name "*.cs" | while read f; do
    first=$(head -c 200 "$f" | tr -d '\n')
    if echo "$first" | grep -qP '^[A-Za-z0-9+/]{60,}={0,2}$'; then
        echo "BASE64: $f"
    fi
done
```

**Build and test:**
```bash
dotnet build MemorySmith.Agent.slnx -c Release
dotnet test MemorySmith.Agent.slnx --logger "console;verbosity=normal"
```

**Decode a base64 file:**
```bash
base64 --decode {encoded-file} > /tmp/decoded.cs
# Verify it looks like valid C# before replacing
head -5 /tmp/decoded.cs
mv /tmp/decoded.cs {original-file}
```

---

## Section VIII: Definition of Done for Sprint 30

Sprint 30 is complete when all of the following are true:

- [ ] `Agent.Core/WorldStateProjector.cs` begins with `namespace Agent.Core;` (valid C#, not base64).
- [ ] `Agent.Tools/ToolDispatcher.cs` begins with `namespace Agent.Tools;` (valid C#, not base64).
- [ ] Full base64 sweep: no `.cs` file on the branch has content matching the base64-string pattern.
- [ ] `Agent.Tools/Tools/SearchMemoryTool.cs` implements `ITool.ExecuteAsync(JsonElement, CancellationToken)`.
- [ ] `Agent.Tools/Tools/CreatePageTool.cs` implements `ITool.ExecuteAsync(JsonElement, CancellationToken)`.
- [ ] `dotnet build` exits with code 0 — zero errors, zero warnings (TreatWarningsAsErrors=true).
- [ ] `dotnet test` passes all tests (261+ baseline) with zero failures.
- [ ] Version string `v0.28.0` in README, `/api/about` response, and Program.cs.
- [ ] CI check-runs diagnosed: either a `completed`/`success` run confirmed for Sprint 30 HEAD, or a written explanation of why CI is not configured and a plan to configure it.
- [ ] `BuildGoalDecomposer.ReadOriginFact` has at least two tests that assert `LogWarning` is invoked (one for missing key, one for unparseable value) using a logger mock or test-double (DEF-P0-B-logverify).
- [ ] `ChatInterpreter.ResolveItemId` uses an explicit plural map, not `TrimEnd('s')` (P1-D).
- [ ] Status regex no longer matches bare `"doing"` token; regression test added (P1-E).
- [ ] SEC-01 addressed (API key middleware) OR explicitly documented as deferred to Sprint 31 with rationale.
- [ ] Sprint 30 council review document committed to `Data/Pages/council/sprint30-council-20260620.md`.
- [ ] Sprint 31 handoff document committed to `Data/Pages/Tasks/agent-handoff-sprint31.md`.

---

*Handoff authored by Hyperagent review session on 2026-06-20. Validated against source files. All claims backed by direct file inspection.*
