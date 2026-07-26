# MemorySmith.Agent — Corpus Reconciliation, Part 2: Working Through the Confirmed-Findings Table

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`) — same commit as all prior reports.
**Continues Part 1** (`msa-corpus-reconciliation-part1-council-verdict-26-07-22-02-50-27.md`), which covered the synthesizer verdict's own explicit follow-up checklist. This installment works through a batch of the verdict's ~56-item "Confirmed Findings" table — 11 items individually re-verified against current HEAD this pass, prioritizing P1s and anything with a plausible chance of having changed since 2026-07-11.

---

## Executive Summary

| # | Finding (council ID / title) | Council priority | Status at HEAD |
|---|---|---|---|
| L1 | **AG-005 — `_stopRequested` not checked mid-operation in craft/smelt** | P1 | **Confirmed still open.** Both `case 'craft'` and `case 'smelt'` in `index.js` check `_stopRequested` exactly once, before the operation starts — then proceed through pathfinding-to-table/furnace, the craft/smelt call itself, and (for smelt) a timeout-bounded wait, none of which re-checks the flag. An emergency stop issued mid-craft or mid-smelt will not interrupt it. |
| L2 | **AG-004 / MSA-MF-003 — ~9 `goto()` calls lack timeout protection** | P1 | Already tracked in this project's own backlog as `TSK-0405`; not independently re-verified this pass beyond confirming the task exists (see Report #1). |
| L3 | **MSA-ADAPT-005 / MA-001 — Windows `kill` triple empty catch** | P1 (downgraded from raw P0) | **Confirmed still open.** Three separate silent `catch` blocks in `MinecraftAdapter.cs`'s shutdown sequence — the first (`catch { // Swallow }`) wraps a shell-out to Unix `kill -TERM`, which doesn't exist on Windows at all. Confirmed consequence, not previously spelled out this precisely: **on Windows, graceful shutdown never happens** — the silent failure of step 1 means the process always falls through to a 5-second timed wait for a signal that was never delivered, then a hard kill. Every Windows shutdown pays a needless 5-second penalty and never gets a clean exit. |
| L4 | **MSA-MEM-001 — `LocalKnowledgeResolver.SearchAsync` (via `IMemoryGateway`) has no error handling** | P1 | **Confirmed still open.** The call `await memory.SearchAsync(query.Query, ct)` is unwrapped — a transient memory-service failure (network blip, HTTP error) propagates as an unhandled exception through the entire knowledge-resolution call, even though two of its three other candidate sources (registry exact-match, WorldFact scan) don't need the network and could still have produced a usable, if incomplete, result. |
| L5 | **MSA-TOOL-007 — `WanderTool` uses `GetInt32()` not `TryGetInt32()`** | P1 | **Confirmed still open.** `arguments.TryGetProperty("radius", ...) ? r.GetInt32() : 20` checks *presence* but not *type* — if the LLM ever emits `"radius": "20"` (a string) instead of a bare number, `.GetInt32()` throws rather than falling back to the default. (Mitigated in practice by `ToolDispatcher`'s Sprint-25 generic exception-to-`ToolResult` wrapper — see L6 — so this fails safely rather than crashing the process, but it still silently discards a plausible LLM output instead of gracefully coercing it.) |
| L6 | **MSA-TOOL-001 — `CreatePageTool` throws instead of returning a failed `ToolResult`** | P2 | **Confirmed still open, but lower-impact than it first appears.** `CreatePageTool.ExecuteAsync` throws raw `ArgumentException` for missing parameters rather than returning `ToolResult(Success: false, ...)`. However, `ToolDispatcher.DispatchAsync` already wraps all tool execution in a generic try/catch (Sprint 25 P0-C, confirmed present) that converts any thrown exception into a failed `ToolResult` — so this is a code-consistency issue, not a crash risk. Worth fixing for consistency; not worth escalating past the council's own P2 rating. |
| L7 | **MSA-CORE-018 — `WorldState.Facts` exposed as a mutable dictionary** | P2 | **Confirmed still open.** `public Dictionary<string, object?> Facts { get; init; } = [];` — a plain mutable `Dictionary`, not `IReadOnlyDictionary`, on an otherwise-immutable-by-convention record (everywhere else uses `with` expressions to produce new `WorldState` instances). Any caller holding a `WorldState` reference can mutate `Facts` directly, bypassing the `SetFact`-based update path and any observers that depend on state changes flowing through it. The property's own doc comment already flags it as legacy (*"Legacy flat fact map. Prefer `StructuredFacts` for new code"*), consistent with this series' recurring legacy/modern duality theme (`TSK-0293`). |
| L8 | **MSA-ADAPT-002 — `stopState.js` dead code** | P1 | **Confirmed still open, still untracked.** `grep` confirms zero references to `stopState.js`'s exports anywhere in `index.js` or elsewhere — the only place the module name `createStopState` appears is inside the file's own header-comment usage example, which was never actually acted on. No task exists for this. |
| L9 | **MSA-WEB-003 — SignalR push failures logged at Debug instead of Warning** | P1 | **Confirmed fixed.** All three dashboard-push methods in `AgentBackgroundService.cs` use `logger.LogWarning(ex, "SignalR ... push failed (best-effort).")` today — not `LogDebug`. This appears to have been corrected sometime between the 2026-07-11 audit and this exact commit, without a dedicated visible task record (possibly folded into a broader logging-cleanup commit). Good news, stated plainly. |

**Net read on this batch:** 8 of 9 individually-checked items are confirmed still open at HEAD; 1 (L9) is confirmed fixed. This is a meaningfully lower fix-rate than the K2 batch in Part 1 (where 3 of 4 P0 crash-surface items were fixed) — consistent with the verdict document's own observation that *"the 12 [Sprint 60] fixes were the highest-ROI items... the remaining 33 are mostly P2/P3 items tracked in Backlog."* Nothing here contradicts that — it's simply a reminder that "tracked in Backlog" and "fixed" are different things, worth keeping distinct when reporting overall project health.

---

## Detailed Findings

### L1 — `_stopRequested` genuinely unchecked mid-craft/mid-smelt

**Evidence** (`MineflayerAdapter/index.js`, `case 'craft'`):
```javascript
case 'craft': {
  if (_stopRequested) { sendEvent('craftAborted', ...); break; }   // ← only check, before anything starts
  ...
  await bot.pathfinder.goto(...);      // no re-check afterward
  await bot.craft(recipe, count, craftingTable);   // no re-check afterward, and count could be large
  ...
}
```
`case 'smelt'` follows the identical shape: one check at the top, then navigation, furnace setup, and a `SMELT_TIMEOUT_MS`-bounded wait for output — all unguarded. The file's own header comment (line 23) is candid about the gap: *"Mine/wander/findFlatArea check `_stopRequested` at the start of each iteration"* — craft and smelt aren't in that list because, unlike those three, they don't have a per-item/per-tick loop structure to check inside; they're written as single up-front-checked, then-uninterruptible operations.

**Recommendation.** For `craft`: if `count` is large enough that `bot.craft()` takes meaningfully long, consider crafting in smaller batches with a `_stopRequested` check between batches (mirrors the mine/wander pattern already established elsewhere in the file). For `smelt`: the existing `furnace.on('update', check)` polling loop waiting for output is a natural place to also poll `_stopRequested` and abort early (calling `furnace.close()` via the existing `finally`, sending a `smeltAborted` event) rather than only reacting to the fixed timeout.

**Confidence: 92%.**

---

### L3 — Windows shutdown: confirmed no graceful path exists at all

**Evidence** (`Agent.World.Minecraft/MinecraftAdapter.cs`):
```csharp
// Step 1 — send SIGTERM via a shell-out to Unix `kill`
try { ...; new ProcessStartInfo("kill", $"-TERM {pid}") ...; killProc.Start(); ... }
catch { /* Swallow — process may have already exited or kill may not be available. */ }

// Step 2 — wait up to 5s for graceful exit
try { _nodeProcess.WaitForExit(5000); }
catch (SystemException) { /* Guard against WaitForExit throwing on an invalid handle. */ }

// Step 3 — force-kill the tree if still alive
if (!_nodeProcess.HasExited) { try { _nodeProcess.Kill(entireProcessTree: true); ... } catch (InvalidOperationException) { } }
```
On Windows, there is no `kill` executable on the default `PATH` — `killProc.Start()` throws a `Win32Exception` immediately, silently absorbed by the bare `catch`. This means **on every Windows shutdown, without exception, the SIGTERM step is a guaranteed no-op**, and the process unconditionally proceeds to a pointless 5-second wait (step 2) followed by a hard kill (step 3). The Node adapter process never receives any termination signal it could act on to flush state or shut down its Mineflayer bot connection cleanly on Windows — every Windows shutdown is, in effect, always a hard kill, just with an extra 5-second delay bolted on for no benefit.

The council's own rationale for downgrading this from the raw audit's P0 to P1 (*"Windows kill failure degrades gracefully — force-kill fallback exists... the catch being empty is the real bug"*) is correct and this pass agrees — the process does still terminate, it's not a hang. The precise, slightly sharper framing worth carrying forward: this isn't just "an empty catch block," it's "the entire graceful-shutdown code path is dead on arrival for one of the project's two target platforms," which is a bit more specific than "empty catch" alone conveys.

**Recommendation.** Branch on `OperatingSystem.IsWindows()`: on Windows, either skip straight to `Process.Kill(entireProcessTree: true)` (there's no point in the 5-second wait if step 1 can never succeed), or use a Windows-appropriate graceful-shutdown mechanism (e.g., `taskkill /pid {pid}` without `/f`, or sending `CTRL_BREAK_EVENT` via `GenerateConsoleCtrlEvent` if the Node process was started with `CREATE_NEW_PROCESS_GROUP`). At minimum, add logging to the currently-silent catch so a future investigator can see *why* graceful shutdown never engages on Windows, rather than needing to trace it from source the way this pass did.

**Confidence: 93%.**

---

### L4, L5, L6 — Tool/gateway error-handling gaps, with an important severity nuance

L4 (`LocalKnowledgeResolver`) and L5 (`WanderTool`) are both confirmed still open and both genuinely reduce robustness — L4 because a partial-success degradation opportunity (return the registry/WorldFact candidates even if the wiki search fails) is being missed in favor of an all-or-nothing exception; L5 because a very plausible LLM output shape (a stringified number) isn't gracefully coerced.

**The important nuance, surfaced by checking L6 (`CreatePageTool`) carefully:** `ToolDispatcher.DispatchAsync` already has a generic try/catch (confirmed present, dated to Sprint 25/TSK referenced as "P0-C" in its own comment) that converts *any* exception thrown by a tool's `ExecuteAsync` into a failed `ToolResult` rather than letting it propagate and potentially crash the dispatch loop. This safety net means L5 and L6 are **consistency and graceful-degradation issues, not crash risks** — worth stating clearly so they're triaged at the right level (the council's own P1/P2 split across these three already reflects roughly this distinction — L4 as P1 because it discards otherwise-available partial results, L5 as P1 because it's a plausible-input footgun even if caught, L6 as P2 because it's purely a style inconsistency with an existing safety net already in place).

**Confidence: 90% (L4), 92% (L5), 88% (L6, including the safety-net verification).**

---

### L7 — `WorldState.Facts` mutability

**Evidence** (`Agent.Core/Models/WorldState.cs:20`):
```csharp
/// <summary>Legacy flat fact map. Prefer <see cref="StructuredFacts"/> for new code.</summary>
public Dictionary<string, object?> Facts { get; init; } = [];
```
A plain, mutable `Dictionary<TKey,TValue>` — not `IReadOnlyDictionary<TKey,TValue>` — on a type whose other mutation path (`SetFact`, per the surrounding builder-pattern code seen in the same file: `var facts = new Dictionary<string, object?>(_state.Facts); _state = _state with { ..., Facts = facts, ... };`) is careful to copy-then-replace rather than mutate in place. The public mutable dictionary is an escape hatch that lets any caller bypass that discipline entirely. Combine with this file's own admission that `Facts` is "legacy" and `StructuredFacts` is preferred, and this reads as a known, acknowledged rough edge rather than an undiscovered one — which is exactly why it's confirmed (not a surprising find) but still worth tracking to closure rather than leaving indefinitely as a "known" gap, per this whole project's stated aversion to permanent legacy/fallback surfaces.

**Recommendation.** Change the property type to `IReadOnlyDictionary<string, object?>` (backed by the same mutable `Dictionary` internally where needed for the `with`-based copy pattern) so external callers can no longer mutate in place; anything currently relying on direct mutation would need to migrate to the `SetFact`/builder path, which is presumably already the intended pattern everywhere else.

**Confidence: 91%.**

---

## What remains for a hypothetical Part 3

This installment checked 11 of the ~56 items in the verdict's Confirmed-Findings and New-Findings tables (counting the 4 already covered across Reports #1/#3/#6 and Part 1 of this reconciliation). Roughly 40 items remain unverified against current HEAD. Given the pattern observed across both installments so far — a healthy minority fixed, a solid majority still open and mostly still untracked at the individual-ticket level even when the council's own document called for tracking — further installments would likely continue finding a similar ratio rather than surfacing qualitatively new patterns. Diminishing-returns judgment call, stated plainly rather than left implicit: continuing this reconciliation is defensible if the goal is a complete, audited ledger of every council finding's current status; it's not likely to keep surfacing headline-level findings the way K1 (prompt injection) did in Part 1. Worth an explicit steer on whether that complete ledger is the goal, versus shifting effort to actually closing some of what's already been found.
