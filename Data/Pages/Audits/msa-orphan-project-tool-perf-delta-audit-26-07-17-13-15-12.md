# MemorySmith.Agent — Delta Audit #3: Orphan Project, Tool Schema Perf, Minor Fixes

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`) — same commit as prior two reports.
**This is a delta report.** Contains only findings not already in the two prior reports (`msa-bloat-duplication-audit-26-07-15-01-32-34.md`, `msa-duplication-supplychain-delta-audit-26-07-16-05-36-21.md`) or in `Data/Tasks/*.json`.
**This pass covered:** `Agent.Vision`, `Agent.Tools/Tools/*` (all 14 tool classes), `Agent.Memory/*`, `Agent.Construction/*`, and the remaining `WebUI.Blazor` infrastructure files (`ApiKeyMiddleware`, `AntiforgeryValidationMiddleware`, `AgentHub`) not yet individually inspected in prior passes.

---

## Executive Summary

| # | Finding | Class | Confidence | Impact |
|---|---|---|---|---|
| E1 | **`Agent.Vision` is an entirely orphaned project.** Its own `.csproj`, 3 files, 60 total lines — referenced by *zero* other project (no `ProjectReference` to it anywhere in the repo), never registered in DI, never touched since its creation commit (2026-06-15), zero test coverage. This is a cleaner, more complete example of the "dead scaffolding" pattern already flagged in Delta #1 (`AgentRuntime`/Managers) — here there isn't even a DI registration attempt. | Dead code / Speculative Generality | 95% | Low-Medium (mostly a clarity/expectation issue) |
| E2 | **All 14 `ITool` implementations re-parse a static JSON schema string via `JsonDocument.Parse(...).RootElement` on every access to `InputSchema`** — it's a computed property, not a cached field. Confirmed hit at least **twice per dispatched action** (`ToolDispatcher.DispatchAsync` validation + `AgentBackgroundService`'s per-dispatch context-merging step), across every mine/place/craft/move/etc. the agent performs. Same anti-pattern independently repeated in all 14 files — no caching, no shared base. | Duplicated Code + Performance smell | 90% | Medium (compounds with session length; real but not urgent) |
| E3 | **Stale/inaccurate doc comment in `ApiKeyMiddleware`**: the XML doc on `IsLocalConnection` says *"Uses `Connection.LocalIpAddress` which is reliable on all platforms"* — the method actually (and correctly) uses `Connection.RemoteIpAddress`. Code is correct; comment describes the wrong property. | Documentation accuracy | 92% | Trivial |
| E4 | **Existing backlog item TSK-0204 ("Add visual screenshot feedback via existing vision pipeline") overstates what exists.** The "existing vision pipeline" it refers to is, in full, a 24-line `WorldVision` class with two trivial fact-lookup methods and no image capture, no vision model client, and no wiring into the runtime (see E1) — not a pipeline in any working sense. Recommend correcting the task's framing before someone scopes work assuming more exists than does. | Task-tracker accuracy (not a code bug) | 85% | Low |

**No security/silent-failure findings this pass** — this round's targets (`Agent.Vision`, `Agent.Tools`, `Agent.Memory`, `Agent.Construction`, remaining `WebUI.Blazor` infra) were comparatively clean; `ApiKeyMiddleware`'s null-IP handling and `AntiforgeryValidationMiddleware`'s fallback logic were both read in full and are correctly implemented (fail-closed on null `RemoteIpAddress`, matching the fix this team already knows to apply from the sibling MemorySmith/KMS repo's earlier audit finding — good evidence the lesson transferred).

---

## Detailed Findings

### E1 — `Agent.Vision` orphan project

**Evidence.**
```
$ grep -rl "Agent.Vision" --include=*.csproj .
./Agent.Vision/Agent.Vision.csproj      # only self-reference; no other project depends on it
$ git log --format="%ai %s" --diff-filter=A -- Agent.Vision/WorldVision.cs
2026-06-15 16:15:09 -0500 chore: add Agent.Vision/WorldVision.cs
$ git log --oneline -- Agent.Vision/
8a0f7e3 chore: add Agent.Vision/WorldVision.cs
686bd94 chore: add Agent.Vision/Interfaces/IVisionModel.cs
1d5167c chore: add Agent.Vision/Interfaces/ISpatialAnalyzer.cs
fd55267 chore: add Agent.Vision/Agent.Vision.csproj
```
Four commits, all "chore: add," none since June 15 — a month untouched at the time of this audit. `WorldVision.cs` (24 lines) is a working, if minimal, class (`GetBlockAt`, `GetNearbyEntities` reading from `WorldState.Facts`), but nothing in `WebUI.Blazor/Program.cs` or `AgentBackgroundService.cs` constructs or resolves it — no DI registration at all, not even an unwired one. `ISpatialAnalyzer` and `IVisionModel` are empty interface shells (21 and 15 lines) with no implementations anywhere in the tree.

This is worth distinguishing from the Report #1 `AgentRuntime`/Managers finding: that one was *registered in DI but never consumed* (an active, if silent, cost — a live singleton nobody uses). This one is simpler: a fully isolated, unreferenced project that could be deleted today with a one-line change (remove it from whatever build script enumerates projects, since there's no `.sln`) and zero behavioral impact anywhere else in the codebase.

**Why report this given TSK-0005/TSK-0023 already exist:** those tasks track "implement SpatialAnalyzer" / "Agent Vision" as backlog *feature* work — they don't flag that the *existing* code in this project is disconnected scaffolding that should either be wired up as part of that work or removed until it's ready to be. Absent that distinction, a future contributor picking up TSK-0023 might reasonably (and incorrectly) assume `WorldVision` is already integrated and build on top of it without checking.

**Recommendation.** Two legitimate paths, either is fine:
1. **If vision work is still wanted** (per TSK-0005/0023): leave the project, but add a one-line comment at the top of `WorldVision.cs` and in the relevant task records clarifying it is not yet wired into `AgentBackgroundService`/DI, so the next implementer starts from an accurate baseline.
2. **If vision work has been deprioritized indefinitely**: delete the project outright per the stated "no legacy scaffolding" ethos — it's zero-risk to remove since nothing references it, and it can be recreated from git history if the feature is picked back up later.

**Confidence: 95%.** Zero-reference claim is a direct, exhaustive grep across all `.csproj`/`.cs` files — the strongest possible evidence for a "nothing uses this" claim.

---

### E2 — Tool `InputSchema` re-parsed on every access, across all 14 tools, no caching

**Evidence.** Representative pattern (`Agent.Tools/Tools/MineBlockTool.cs`, and structurally identical in all 13 sibling tool classes — confirmed via `grep -c "JsonDocument.Parse"` returning exactly 1 per file across `ChatTool`, `CraftItemTool`, `CreatePageTool`, `FurnaceTool`, `GetPageTool`, `GetStatusTool`, `MineBlockTool`, `MoveToTool`, `PlaceBlockTool`, `QueryBlocksTool`, `QueryEntitiesTool`, `SearchMemoryTool`, `WanderTool`, and 2 in `FindFlatAreaTool`):
```csharp
public JsonElement InputSchema => JsonDocument.Parse("""
    { "type": "object", "properties": { ... }, "required": [...] }
    """).RootElement;
```
This is a **property getter**, so `JsonDocument.Parse` — which allocates a new `JsonDocument` and parses the full schema string — runs fresh on every single access, of which there are confirmed **at least two per dispatched action**:
1. `Agent.Tools/ToolDispatcher.cs:113` — `var schema = tool.InputSchema;` inside `DispatchAsync`, used to validate arguments before every tool execution.
2. `WebUI.Blazor/AgentBackgroundService.cs:2206–2208` — inside the per-action-dispatch context-merging step (`if (tool?.InputSchema.ValueKind == JsonValueKind.Object) { var schema = tool.InputSchema; ... }`) — note this snippet itself accesses the property **twice** in three lines, so a single pass through this branch triggers 2 parses on its own.
3. `Agent.Planning/LlmChatInterpreter.cs:404` — `ExtractToolParams(tool.InputSchema)` — likely once per chat-driven decision rather than per dispatch, but adds to the total.

For a long-running Minecraft agent, tool dispatch is the core hot loop — every mine/place/craft/move/wander action re-triggers this. None of it is expensive in isolation (small JSON strings), but it's wasted, entirely avoidable work multiplied across 14 call sites and however many thousands of dispatches a session accumulates, for content that is 100% static per tool.

**Recommendation.**
1. Mechanical, low-risk fix per tool: change `public JsonElement InputSchema => JsonDocument.Parse("""...""").RootElement;` to a `private static readonly JsonElement _inputSchema = JsonDocument.Parse("""...""").RootElement;` field (parsed once, at type-init time) with `public JsonElement InputSchema => _inputSchema;`.
2. Given this exact shape is repeated 14 times, consider whether an abstract `ToolBase` class (there currently is none — each of the 14 tools independently implements `ITool` from scratch) could take a constructor parameter for the schema JSON string and own the caching once, centrally, rather than fixing the same pattern in 14 places. This would also be a natural place to consolidate the `TryGetProperty`/`GetInt32` argument-extraction boilerplate that's likewise duplicated across all 14 `ExecuteAsync` methods (not separately quantified here, but visible in every tool file inspected this pass) — following the same "extract shared base, don't fix N copies" principle applied elsewhere in this audit series (cf. Report 2's D7 recommendation for the LLM provider classes).

**Confidence: 90%.** The re-parse-per-access fact and the ≥2-calls-per-dispatch fact are both directly confirmed via grep/read; the "this matters at scale" severity claim is a reasonable inference from "tool dispatch is the hot loop of an autonomous agent" rather than a benchmarked measurement (no profiler was run — see Open Questions).

---

### E3 — Stale doc comment in `ApiKeyMiddleware.IsLocalConnection`

**Evidence** (`WebUI.Blazor/ApiKeyMiddleware.cs:78–86`):
```csharp
/// <summary>
/// Returns true if the request originates from the loopback address.
/// Uses Connection.LocalIpAddress which is reliable on all platforms.
/// </summary>
private static bool IsLocalConnection(HttpContext context)
{
    var remoteIp = context.Connection.RemoteIpAddress;
    if (remoteIp is null) return false;
    return System.Net.IPAddress.IsLoopback(remoteIp);
}
```
The code is correct and, notably, already fails closed on a null `RemoteIpAddress` (unlike the sibling MemorySmith/KMS repo's previously-flagged fail-open bug on the same kind of check — good evidence that lesson carried over to this codebase). The doc comment simply names the wrong property (`LocalIpAddress` instead of `RemoteIpAddress`), which could mislead a future reviewer skimming the comment rather than the body.

**Recommendation.** One-line comment fix: `Uses Connection.RemoteIpAddress...`. Trivial, but included per the "no code without solid explanation" instruction — a wrong explanation is arguably worse than none.

**Confidence: 92%.**

---

### E4 — TSK-0204's premise overstates the current vision pipeline

See E1 for the underlying code evidence. TSK-0204 ("Add visual screenshot feedback via existing vision pipeline," currently Backlog) frames its scope around an "existing vision pipeline" — the actual existing code is the 24-line, unwired `WorldVision` stub from E1, not a pipeline capable of receiving or processing screenshots. Recommend a one-line correction to TSK-0204's description (or a linked note) so whoever picks it up doesn't discover the gap mid-implementation.

**Confidence: 85%** — this is an interpretation of the task's wording against the code, not a hard fact; reasonable people could read "existing vision pipeline" more loosely than this report does.

---

## Assumptions & Open Questions

1. **E2's performance-impact severity is not benchmarked.** No profiler was run against the live agent (no Minecraft server available in this sandbox); the claim rests on "this code path is the dispatch hot loop, and re-parsing static JSON there is pure waste" rather than a measured allocation/latency number. If the team has profiling data suggesting this is genuinely immaterial (e.g., dispatch rate is low enough that this never shows up), the fix is still good hygiene but the priority should be adjusted down accordingly.
2. **E1's recommendation (wire vs. delete) is a product decision**, not a code-correctness one — this report doesn't have visibility into whether vision capability is still a near-term roadmap item, so both options are presented rather than a single directive.
3. **Test coverage for `Agent.Tools/Tools/*` was not separately audited this pass** — E2's fix (caching the schema) is low-risk enough that it likely doesn't need new tests, but worth a quick check that no existing test asserts on `InputSchema` returning a *new* `JsonElement` instance per call (unlikely, but caching would change reference-equality behavior if anything relied on it, which would itself be a code smell worth knowing about).

---

## Suggested Sequencing (additive to prior reports)

1. E3 (doc-comment fix) — trivial, bundle with any other small fix in flight.
2. E2 (cache `InputSchema` per tool) — mechanical, 14 small independent edits (or one shared-base refactor, see recommendation) — good candidate to pair with Report 2's D7 (LLM provider base-class extraction), since both are "same shape repeated N times, extract once" refactors in adjacent-in-spirit parts of the codebase.
3. E1/E4 — no code risk either way; needs a 5-minute product/roadmap decision (wire vs. delete Agent.Vision) before either task's tracker entry is corrected.
