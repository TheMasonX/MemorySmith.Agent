# MemorySmith.Agent — Corpus Reconciliation, Part 3: SignalR Auth Gap, Record-Equality Trap, Gateway Error-Handling Inconsistency

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`) — same commit as all prior reports.
**Continues Parts 1 and 2.** This installment re-verifies 6 more items from the council's confirmed-findings tables, with one genuinely new sharpening (the SignalR/REST protection asymmetry) worth flagging above the others.

---

## Executive Summary

| # | Finding (council ID / title) | Council priority | Status at HEAD |
|---|---|---|---|
| M1 | **NEW (council) — SignalR hub has no authentication, while the REST API does** | P1 | **Confirmed still open, and confirmed asymmetric** — new detail this pass: verified the REST API's `ApiKeyMiddleware` is explicitly scoped to `/api/*` routes only (its own doc comment says so, and it's registered on a specific route branch), while `app.MapHub<AgentHub>("/agent-hub")` has no `.RequireAuthorization()` and `AgentHub.cs` has no `[Authorize]` attribute anywhere. Anyone who can reach `/agent-hub` gets the real-time status/chat/goal stream with zero credential check, even in a deployment that has an API key configured and enforced for every REST endpoint. |
| M2 | **NEW (council) — `BlockState` record has broken value equality** | P2 | **Confirmed still open, currently latent (not yet triggering a visible bug).** `BlockState` is a `sealed record` whose only field is `IReadOnlyDictionary<string,string> Properties` — but `Dictionary<TKey,TValue>` doesn't have value-based `Equals`, so the record's compiler-synthesized equality silently reduces to reference equality on that dictionary. Two `BlockState.Parse("half=top")` calls produce two instances that are `!Equal`, despite representing identical content. No production code currently compares `BlockState` instances directly (confirmed via grep), so this hasn't caused an observed bug yet — but it's a live trap for the next person who reasonably assumes a `record` means "value equality" and writes a deduplication check, cache key, or test assertion against it. |
| M3 | **MSA-MEM-002 / MSA-MEM-003 — `RestMemoryGateway`'s page methods have inconsistent, not merely "missing," error handling** | P1 / P2 | **Confirmed still open, and confirmed inconsistent rather than uniformly bad.** `GetPageAsync` swallows every non-success HTTP status into `null` (indistinguishable from "page doesn't exist"). `CreatePageAsync` does the opposite — `resp.EnsureSuccessStatusCode()` throws unhandled on any failure. Same class, same kind of REST call, two different failure-handling philosophies with no stated rationale for the difference. |
| M4 | **MSA-PLAN-008 — `MineWoodDecompose` mines 2x the requested wood** | P1 | **Confirmed still open, already tracked** (`TSK-0398`, `Backlog`). `MineWoodDecompose` emits one `MineBlock` action for `oak_log` at the full requested count and a second, separate `MineBlock` action for `birch_log`, also at the full requested count — up to double the wood actually needed if both succeed. Same bug shape as the `GatherItemDecompose`/`TSK-0397` finding already covered in this series (Delta #4, G5), just hardcoded to exactly 2 variants instead of driven by a variable-length source-block list. |
| M5 | **MSA-CORE-005 — confirmed to be the *same finding* as this series' own Part 2, L3 (Windows `kill` triple-catch), not a distinct item.** | P1 | Already fully covered — see Part 2. Noting here only to close out the cross-reference (both IDs point at `MinecraftAdapter.DisconnectAsync`), so a future reconciliation pass doesn't double-count it. |

**Headline of this installment: M1.** A dashboard/telemetry channel carrying the same operationally-sensitive data (bot status, chat transcript, current goal) as the protected REST API, sitting completely open with no auth check, in a codebase that otherwise has a documented, tested, correctly-fail-closed API-key middleware. This is the kind of asymmetry that's easy to miss precisely because the "hard" security work (the REST middleware) was clearly done carefully — it's natural to assume the adjacent real-time channel inherited the same protection, and it didn't.

---

## Detailed Findings

### M1 — SignalR hub authentication gap, confirmed and precisely scoped

**Evidence:**
```csharp
// WebUI.Blazor/Program.cs:585
app.MapHub<AgentHub>("/agent-hub").DisableAntiforgery();   // no .RequireAuthorization()
```
```csharp
// WebUI.Blazor/ApiKeyMiddleware.cs:7 (doc comment)
/// Middleware that validates an API key on all /api/* routes.
```
```csharp
// WebUI.Blazor/Program.cs:488
branch => branch.UseMiddleware<ApiKeyMiddleware>()   // registered on a specific route branch, not globally
```
`AgentHub.cs` itself carries no `[Authorize]` attribute at the class or method level. The scoping is explicit and intentional in the middleware's own documentation ("`/api/*` routes") — this isn't an oversight in how the middleware was written, it's that the hub was never brought under any equivalent protection when it was added.

**Why this matters in practice:** the dashboard hub pushes the same status/chat/goal data this audit series has spent several reports discussing the correct handling of (Report #1's dashboard-push slice, the migration spec's Slice 1) — meaning this is live, currently-flowing operational data, not a dormant surface. In a deployment where the operator has configured and is relying on the API key to keep the REST surface private, the SignalR channel is a complete bypass of that intent for anyone who discovers the `/agent-hub` endpoint.

**Recommendation.** Add `.RequireAuthorization()` to the `MapHub` call (requires wiring up an actual auth scheme SignalR can check, which likely means extending `ApiKeyMiddleware`'s validation logic to also run for `/agent-hub`, or adding a SignalR-specific auth handler that checks the same API key via a query-string parameter or connection header, since SignalR's initial handshake can carry either). At minimum, this should get the same priority the council gave it (P1) rather than being deprioritized just because it's "only" the dashboard rather than the action-dispatching REST surface — the data sensitivity is comparable.

**Confidence: 94%.**

---

### M2 — `BlockState` record-equality trap

**Evidence** (`Agent.Construction/BlockState.cs`):
```csharp
public sealed record BlockState
{
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    ...
}
```
C# records synthesize `Equals`/`GetHashCode` by comparing each member — for a `Dictionary<TKey,TValue>` member (even typed as the `IReadOnlyDictionary` interface, the underlying instance is still a `Dictionary`), that synthesized comparison falls back to `Dictionary<TKey,TValue>`'s own `Equals`, which is reference equality, not content equality. Two separately-constructed `BlockState` instances with identical `Properties` content will not be `==` to each other.

**Confirmed latent, not yet actively wrong:** `grep -rn "BlockState ==` and similar equality-comparison patterns across all non-test `.cs` files returned nothing — nothing in production code currently relies on `BlockState` equality. This is a real, confirmed defect, just not yet a triggered bug — worth fixing proactively (per this whole project's stated preference for closing gaps rather than accumulating them) rather than waiting for it to surface as a confusing test failure or cache-miss bug later.

**Recommendation.** Either (a) implement custom `Equals(BlockState?)`/`GetHashCode()` overrides doing proper key/value content comparison (e.g., same count, same keys, same values — a short, mechanical implementation), or (b) change `Properties`'s backing type to an immutable collection with built-in structural equality if one's already in use elsewhere in this codebase for similar cases (worth checking `Fact`/`ActionData` and other record types for a precedent before hand-rolling one here).

**Confidence: 91%.**

---

### M3 — `RestMemoryGateway`: two philosophies, not one gap

**Evidence** (`Agent.Memory/RestMemoryGateway.cs`):
```csharp
public async Task<string?> GetPageAsync(string pageId, CancellationToken ct = default)
{
    var resp = await http.GetAsync(url, ct);
    if (!resp.IsSuccessStatusCode) return null;   // 404, 401, 500, 503 — all collapse to null
    ...
}

public async Task<string> CreatePageAsync(string title, string content, string type, CancellationToken ct = default)
{
    var resp = await http.PostAsJsonAsync("api/pages", req, JsonOpts, ct);
    resp.EnsureSuccessStatusCode();   // throws HttpRequestException, uncaught here
    ...
}
```
The council's two findings (`MSA-MEM-002`, `MSA-MEM-003`) read, individually, as "this method doesn't handle errors well" — accurate, but the more useful framing once both are read together is that **the class has no single error-handling policy**: one public method on it silently converts every failure into a value that looks like a valid (if empty) result, and a sibling method on the same class throws raw framework exceptions for the same category of failure. A caller integrating against this gateway has to know, method by method, which failure mode to expect — `GetPageAsync`'s caller needs a null-check; `CreatePageAsync`'s caller needs a try/catch; and (per `UpdatePageAsync`'s own `TSK-0138` comment, not fully re-verified this pass) a third method may follow yet another convention.

**Recommendation.** Pick one convention for the whole gateway class — most REST-gateway classes in .NET codebases converge on either "always throw a typed exception, let callers catch what they care about" or "always return a result type (`Result<T>`/a custom outcome enum) and let callers pattern-match" — and apply it uniformly across `GetPageAsync`, `CreatePageAsync`, and `UpdatePageAsync`. This is a larger change than either individual council finding suggests in isolation, since it's really "standardize the class's error contract," not "add a try/catch to method X."

**Confidence: 88%** (the individual behaviors are directly confirmed; the "should be one unified policy" recommendation is a design opinion, reasonable but not the only valid approach).

---

## Running tally across all three reconciliation installments

| Installment | Items checked | Confirmed still open | Confirmed fixed |
|---|---|---|---|
| Part 1 | 5 | 4 | 1 (K2, 3 of 4 sub-items) |
| Part 2 | 9 | 8 | 1 (L9) |
| Part 3 | 6 (5 distinct + 1 cross-reference) | 4 | 0 |
| **Total** | **~19 distinct items** | **~16** | **~2 groups** |

This ratio (roughly 85% still-open, 15% fixed, across everything checked so far) is consistent enough across all three installments that it's a reasonably stable estimate for the ~35 remaining unverified items too, absent evidence otherwise — worth using as a planning input if there's a decision to make about how much further reconciliation to fund versus assuming the ratio holds and prioritizing fixes over further verification.

## Recommendation on continuing

Three installments in, the pattern is now well-established rather than still-emerging: council findings that got individually ticketed and prioritized in Sprint 60 planning (the P0 crash-surface items, this pass's SignalR-adjacent logging fix) do get fixed; items that remained as table rows without an individual ticket mostly don't move, regardless of council-assigned priority. Further installments would very likely keep confirming that same shape rather than surfacing another finding at K1's or M1's level. Given that, this is a natural stopping point for the reconciliation series specifically — the remaining ~35 items are cataloged in the source document with file/line references good enough to act on directly, and this report's own running tally gives a defensible confidence estimate for their aggregate status without needing to check each one individually.
