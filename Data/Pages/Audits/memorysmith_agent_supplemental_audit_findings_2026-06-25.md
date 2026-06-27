# Supplemental Audit Findings — New Issues Only

**Repo:** TheMasonX/MemorySmith.Agent  
**Branch/commit reviewed:** `18648691d8abd5ad84ee255795b76ffdc0aca131`  
**Date:** 2026-06-25

This note contains only findings that were not included in the previous report. I kept the scope narrow and focused on newly verified runtime/architecture issues.

## Summary

| # | Finding | Severity | Confidence |
|---|---|---:|---:|
| 1 | `BlockNotFound` retry count never increments past `1` | High | 96% |
| 2 | Explicit build origin `(0,0,0)` is still treated as “missing” | High | 93% |
| 3 | Memory gateway write path hides real failures and ignores caller intent | Medium | 84% |
| 4 | Blueprint repository swallows cancellation and keeps doing fallback work | Medium | 80% |

## 1) `BlockNotFound` retry count never increments past `1`

### What is happening
`TryRouteAsError` reads the existing retry counter for a missed block as `int`, but it writes the updated value back as a string. That means the first miss increments to `1`, but subsequent reads never see the prior count, so the stored value keeps collapsing back to `1`.

### Why this matters
The gather planner uses this count to widen the wander radius on repeated misses. Because the counter does not actually accumulate, the agent never reaches the intended 80/120-block retry tiers and can get stuck in the narrowest search behavior forever.

### Evidence
`WebUI.Blazor/AgentBackgroundService.cs`:
- reads with `pc is int pci ? pci : 0`
- writes with `SetFact(countKey, (prevCount + 1).ToString(), ...)`

`Agent.Planning/HtnTaskLibrary.cs`:
- reads `event:BlockNotFound:Count:{block}` as a string and parses it for the retry radius logic

### Assessment
This is a live behavior bug, not just a logging issue. It directly undermines the progressive retry design.

### Confidence
96%

### Recommended fix
Store and read the counter using one consistent representation. The safest option is to keep it as an integer end-to-end, or centralize access through a typed helper so write/read symmetry cannot drift again.

## 2) Explicit build origin `(0,0,0)` is still treated as “missing”

### What is happening
The `BuildOrigin` value object was introduced specifically to avoid sentinel overloading and to make missing origin explicit via `null`. However, the lower-level build decomposer still checks `originX == 0 && originY == 0 && originZ == 0` as the signal to auto-resolve origin / emit `FindFlatArea`.

### Why this matters
An explicit build origin at the world origin is now impossible to represent faithfully. If a caller legitimately asks to build at `(0,0,0)`, the planner will silently reinterpret that as “no origin supplied” and switch to auto-detection logic.

That violates the new contract in `BuildOrigin` and reintroduces the exact kind of sentinel ambiguity the refactor was meant to eliminate.

### Evidence
`Agent.Planning/Goals/BuildOrigin.cs`:
- “No sentinel overloading: missing origin is represented as `null`, not (0,0,0).”

`Agent.Planning/Decomposition/BuildGoalDecomposer.cs` and `Agent.Planning/HtnTaskLibrary.cs`:
- zero-triplet origin is still used as the auto-origin trigger
- `BuildGoalDecomposer` forwards explicit origin coordinates straight into `DecomposeBuild`

### Assessment
This is an architectural contract break with a real runtime effect. It is especially risky because the failure mode is silent and only appears for a legitimate coordinate corner case.

### Confidence
93%

### Recommended fix
Stop using the coordinate triple itself as the “origin present” sentinel. Thread an explicit `hasOrigin` / `BuildOrigin?` signal all the way down, and let the decomposer decide based on nullability, not `0,0,0`.

## 3) Memory gateway write path hides real failures and ignores caller intent

### What is happening
`RestMemoryGateway.UpdatePageAsync` treats any exception from the initial GET as if it were “404 or parse error”, logs a warning, and then proceeds to issue a PUT upsert. That means auth failures, timeouts, 500s, and other transport problems are all flattened into the same recovery path.

Separately, `CreatePageAsync` accepts a `type` parameter but never uses it; the request always uses `options.DefaultPageRole`.

### Why this matters
The update path can silently convert infrastructure problems into unintended writes. In the worst case, a transient failure on the read side turns into a write attempt with a fallback title, which is the opposite of a safe failure mode.

The ignored `type` parameter is also a contract leak: callers can pass a value that looks meaningful, but it has no effect.

### Evidence
`Agent.Memory/RestMemoryGateway.cs`:
- `catch (Exception ex) { ... "404 or parse error" ... }`
- then unconditional PUT upsert
- `CreatePageAsync(string title, string content, string type, ...)` never reads `type`

### Assessment
The issue is not just that errors are logged loosely; it is that the gateway normalizes unknown failures into a write attempt. That is a correctness and recoverability problem.

### Confidence
84%

### Recommended fix
Only fall back to upsert on the specific “not found” case you actually intend to support. Let other exceptions propagate or return an explicit failure result. For page creation, either remove the unused `type` argument or wire it into the request payload.

## 4) Blueprint repository swallows cancellation and keeps doing fallback work

### What is happening
`MemorySmithBlueprintRepository.GetAsync` catches `TaskCanceledException`, logs a warning, and then continues into local-file and search fallback logic. The same pattern appears in the search method.

### Why this matters
A caller that cancels the operation does not actually get cancellation behavior; the repository may continue to do file I/O and additional network calls after the request should have stopped. That can waste work and surface stale results after the caller has already given up.

### Evidence
`Agent.Memory/MemorySmithBlueprintRepository.cs`:
- `catch (TaskCanceledException ex) { ... content = null; }`
- fallback continues to local pages and search
- same pattern in `SearchAsync`

### Assessment
This is a classic cancellation-contract bug. It is especially undesirable in a greenfield system that is trying to be deliberate about failure visibility and deterministic control flow.

### Confidence
80%

### Recommended fix
Distinguish timeout from caller cancellation. If `ct.IsCancellationRequested` is true, rethrow or return immediately. Only use fallback paths when the failure is a genuine transient lookup problem, not an explicit abort.

## Assumptions and open questions

I assumed the intent of the new build-origin refactor is to eliminate all sentinel-based origin handling, not just move it around. I also assumed the `BlockNotFound` retry counter is supposed to drive the 40 → 80 → 120 radius progression as documented in `HtnTaskLibrary`.

Open question: whether the memory gateway write path should ever upsert after a failed GET, or whether that behavior was intentionally chosen as a convenience tradeoff. If it is intentional, it still needs a much narrower failure filter than “any exception”.
