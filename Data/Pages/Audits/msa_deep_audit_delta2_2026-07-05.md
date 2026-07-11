# MemorySmith.Agent — Deep-Dive Audit: Delta Report #2
**Scope:** Continuation of the Sprint 59 audit (`dev/round-3` @ `eceb01e`). This pass targeted areas not yet covered: `WebUI.Blazor/Managers/*`, `Agent.Planning/Decomposition/*`, `WebSocketBridge.cs`, `ToolDispatcher.cs`, and the `MineflayerAdapter/index.js` `dispatch()` body, cross-checked line-by-line against `internal-audit-57-20260701.md` (34 findings) and current `Data/Tasks/*.json` status to isolate what's genuinely new vs. already tracked.
**Format:** Deltas only — new findings and corrections to existing tasks/reports. No restatement of prior findings.

---

## Summary

| # | Type | Item | Severity | Confidence |
|---|------|------|----------|------------|
| 1 | Correction | TSK-0322 targets code that is currently unreachable (dead manager layer) | Medium | 93% |
| 2 | New, untracked | `BuildGoalDecomposer` mislabels stored-fact origins as `AutoScanned` | Medium | 95% |
| 3 | Correction | P1-7 (`RememberFactAsync`) is stale — failure logging already exists; only player-facing feedback is missing | Low | 90% |
| 4 | Correction | TSK-0321 doesn't implement the 60s/active-goal tier its own description and Wave B's G2.1 call for | Low | 95% |
| 5 | New, cosmetic | Orphaned/duplicated `<summary>` doc block, `AgentBackgroundService.cs:3177-3195` | Low | 98% |
| 6 | New, consolidation | 7× duplicated pathfinder-movements setup in `index.js` `dispatch()` | Low | 97% |
| 7 | New, consolidation | 3× near-identical creative-mode `/give` one-liners in `HtnTaskLibrary.cs` | Low | 95% |
| 8 | New, brittle/hardening | `WebSocketBridge.SendAsync` silently `.ToString()`s any non-primitive argument instead of emitting real JSON | Low | 85% |

Also explicitly re-verified and **ruled out as false leads** (included for traceability, not as findings): `ToolDispatcher`'s registry lookup for `PlaceBlockGoalDecomposer`'s raw `Tool = "place"` action is correctly covered by an explicit alias registration (`Program.cs:289`); `dispatch()`'s lack of an inner `catch` is intentional and safe (caller `drainQueue` wraps every dispatch in `.catch()`); the three "creative-mode guard" decomposers (Craft/Smelt/Gather) all correctly delegate to `HtnTaskLibrary` methods that already implement `IsCreativeMode` checks per TSK-0306; the six `.bak` files noted in `internal-audit-57` (PR-3) no longer exist in the tree.

---

## 1 — TSK-0322 targets dead code (Medium, 93%)

TSK-0322 ("P0: Fix ExecutionManagerImpl JSON round-trip type fidelity loss," still `Backlog`/`Critical`) is a faithful restatement of `internal-audit-57`'s P0-6. Both correctly describe the bug in `WebUI.Blazor/Managers/ExecutionManagerImpl.cs:56-58` (serialize→parse round-trip losing type fidelity). What neither document states: `ExecutionManagerImpl` is one of the six manager classes confirmed dead by the same audit's P1-1 ("6 registered implementations, 0 used by the live path"). Re-verified directly: zero call sites for `ExecutionManagerImpl`/`IExecutionManager.DispatchAsync` anywhere in `AgentBackgroundService.cs` or any other live-path file.

**Why this matters:** as filed, TSK-0322 reads as a `Critical`, standalone production bug. It isn't reachable today — fixing it changes nothing about current agent behavior. Leaving it filed at `Critical` risks a future sprint spending effort on it under false urgency, or (worse) someone "fixing" it and considering the manager-layer question closed when the actual decision (wire vs. delete the whole layer, per P1-1's own recommendation) still hasn't been made.

**Recommendation:** Re-tag TSK-0322 as blocked-by / sequenced-after a dedicated decision task for P1-1 ("wire the 6 managers into AgentBackgroundService" vs. "delete the dead layer"). If the decision is "delete," TSK-0322 can likely be closed as moot rather than implemented. If "wire," TSK-0322 becomes a real pre-requisite and should stay Critical. Either way, its current priority is only correct under an assumption (the layer will be revived) that isn't yet decided anywhere in the tracked backlog.

---

## 2 — `BuildGoalDecomposer` origin mislabeling is still live and untracked (Medium, 95%)

`internal-audit-57`'s P1-5 flagged this; re-verified against current HEAD — **still present, unchanged**:

```csharp
// Agent.Planning/Decomposition/BuildGoalDecomposer.cs:60
var source = bg.HasExplicitOrigin ? BuildOriginSource.Explicit : BuildOriginSource.AutoScanned;
```

Cross-checked against `Data/Tasks/*.json`: no task references this specific mislabeling. The closest matches — `TSK-0107` (origin sentinel `(0,0,0)` ambiguity) and `TSK-0098` (duplicate `ReadOriginFact` consolidation) — are both about *reading* the origin value, not about *how its provenance is labeled* once read. This is a distinct, still-open gap.

**Impact (unchanged from original audit):** any build whose origin came from a previously-stored fact (a prior explicit placement, a REST API call, or a resumed session) is logged and reported to the LLM evaluator as `AutoScanned` — indistinguishable from a genuine flat-area scan result. This muddies exactly the kind of provenance signal the project has otherwise been careful about (`StructuredFacts`, `FactSource` enum elsewhere in the codebase follow this discipline; this is one of the few places that doesn't).

**Recommendation:** Add `BuildOriginSource.StoredFacts` (or reuse the existing `FactSource` enum if one already models this distinction — worth checking for reuse before adding a new one) and set it in the non-explicit branch when the coordinates were read from `WorldState.Facts` rather than freshly scanned. File as new task; no existing task should be extended for this since none currently reference the file/line.

---

## 3 — Correction to P1-7: `RememberFactAsync` failure handling is more complete than documented (Low, 90%)

`internal-audit-57`'s P1-7 states: *"Fact persistence to MemorySmith is fire-and-forget with no error reporting... If the MemorySmith API call fails... the fact is silently lost."* Re-checked against current code:

```csharp
// WebUI.Blazor/AgentBackgroundService.cs:3157-3175
private async Task RememberFactAsync(string key, string value, Position? pos = null)
{
    ...
    catch (Exception ex)
    {
        logger.LogWarning(ex, "[memory] failed to store fact '{Key}': {Message}", key, ex.Message);
    }
}
```

Failure **is** logged at Warning today — this part of P1-7 is stale (it's possible this was fixed in an unrelated pass and the audit doc was never updated, or the audit was written against an older snapshot). The one part of P1-7 that's still accurate: since the call site is `_ = RememberFactAsync(...)` (fire-and-forget, line 1336) and the player-facing chat response is generated independently, the player still always receives "I'll remember that" regardless of whether the persist actually succeeds — there's no path for a late failure to retract or correct that message.

**Recommendation:** No task currently tracks P1-7 by name; if/when one is filed, scope it to just the player-feedback gap (e.g., a follow-up chat message on failure: "actually, I couldn't save that") rather than the already-resolved logging gap, to avoid re-doing completed work.

---

## 4 — Correction to TSK-0321 / Wave B G2.1: no active-goal-tiered sync interval was actually implemented (Low, 95%)

TSK-0321's own description says: *"Sync periodically regardless of goal state, possibly at a longer interval during active goals (e.g., 60s instead of 30s)."* The council's Wave B acceptance gate is more specific: **"G2.1 | Inventory sync fires at 60s during active goal."** Re-checked the shipped fix:

```csharp
// AgentBackgroundService.cs:162
private static readonly TimeSpan InventorySyncInterval = TimeSpan.FromSeconds(30);
// ...used identically at lines 1235 and 1252, with no goal-state branch anywhere in InventorySyncLoopAsync.
```

The `_currentGoal is not null` guard was correctly removed (the actual P0-3 bug) and the stacked-delay bug was correctly fixed (check-before-sleep, matches G2.2) — both verified correct. But there is no 60s/30s split; the loop unconditionally uses 30s whether idle or mid-goal.

**Impact:** Functionally this is arguably *fine* — a flat 30s is simpler and gives fresher data during builds, at the cost of one extra `GetStatus` dispatch per minute during long active goals versus the originally specified 60s tier. Not a bug, but worth recording so a future audit doesn't treat G2.1 as unimplemented and re-open work that was already deliberately simplified, and so nobody spends time looking for a nonexistent branch when debugging sync timing.

**Recommendation:** Either implement the tier (small, low-risk addition: check `_currentGoal is not null` to select `60s : 30s` when computing the delay) or close G2.1 as "implemented, simplified" in the council doc/task history so the discrepancy doesn't get rediscovered as a bug later.

---

## 5 — Orphaned/duplicated doc-comment block (Low/cosmetic, 98%)

```csharp
// AgentBackgroundService.cs:3175-3196
    }

    /// <summary>
    /// <summary>
    /// Sprint 18: resolves "leo stop" not stopping Node.js and goal completion not aborting
    /// in-progress mine loops.
    ///
    /// Sprint 54 (TSK-0205): TryAdvanceSequence for multi-step chaining follows.
    /// </summary>

    // ── Enhanced stall diagnostics (Sprint 54) ──────────────────────────────

    /// <summary>
    /// Builds a human-readable detail string about what the current goal is
    /// stuck on. ...
    /// </summary>

    // ── Sprint 55 (TSK-0155): WorldStateDiff computation ───────────────────

    /// <summary>
    /// Computes the delta between pre-dispatch and post-dispatch world state.
```

Two consecutive doc-comment blocks (lines 3177-3183 and 3187-3195) are **not attached to any declaration** — they sit between a closing brace and a section-header line comment, describing methods (`TryAdvanceSequence`, a stall-detail builder) that either moved elsewhere in the file or were refactored away. The first block additionally has a literal duplicated `/// <summary>` opening tag (line 3177 and 3178), which is itself malformed XML doc syntax. Repo-wide check confirms this exact duplicated-tag pattern occurs **exactly once** in the whole codebase — an isolated copy-paste artifact, not a systemic issue.

**Impact:** None at runtime (comments compile fine); purely a readability/IDE-tooltip hazard — a future reader hovering in this region gets stale, disconnected documentation.

**Recommendation:** Delete both orphaned blocks (or relocate their content to the methods they actually describe, if those methods still exist under different names — worth a quick check for `TryAdvanceSequence` before deleting, in case the doc comment is the only place its historical rationale is recorded).

---

## 6 — Duplicated pathfinder-movements setup, `index.js` (Low/consolidation, 97%)

The two-line pattern
```js
const movements = createMovements(bot);
bot.pathfinder.setMovements(movements);
```
appears **verbatim 7 times** inside the `dispatch()` mega-function: lines 650-651 (`move`), 666-667 (`mine`), 1236-1237, 1451-1452, 1795-1796, 1826-1827, and 1886-1887 (remaining movement-dependent action handlers).

**Impact:** Low runtime risk, but every future movement-tuning change (dig cost, allow/disallow parkour, block-breaking cost, etc.) requires editing 7 call sites identically — exactly the kind of drift risk the project has repeatedly hit elsewhere (e.g. `SmeltableMapping` was extracted under TSK-0082 for precisely this reason).

**Recommendation:** Extract a one-line helper, e.g. `function ensureMovements() { bot.pathfinder.setMovements(createMovements(bot)); }`, and replace all 7 sites. Zero behavior change, ~7 lines removed, one point of control going forward. Worth bundling into TSK-0166 (Mineflayer modularization) as a quick win during the `dispatch()` extraction work already recommended in the prior report, rather than filing separately.

---

## 7 — Near-identical creative-mode `/give` branches, `HtnTaskLibrary.cs` (Low/consolidation, 95%)

Three of the four `IsCreativeMode` early-return branches in `HtnTaskLibrary.cs` (lines 207, 303, and 726 — craft, smelt, and generic gather respectively) are functionally identical modulo variable name:

```csharp
if (state.IsCreativeMode)
{
    return [ActionFactory.Create("Chat", ("message", $"/give @p {itemOrInputOrSpecItem} {count}"))];
}
```

The fourth site (line 475, inside `DecomposeBuild`) is legitimately different — creative builds need per-block handling, not a single `/give` — and should **not** be folded into this consolidation.

**Recommendation:** Extract a private helper `GiveViaCreative(string item, int count) => [ActionFactory.Create("Chat", ("message", $"/give @p {item} {count}"))];` and call it from the three matching sites. Trivial, safe, removes 3-way duplication of a safety-relevant string-formatting pattern (a future change to the give-command format, e.g. adding an explicit count-per-give cap, currently has to be made in 3 places and is easy to miss one).

---

## 8 — `WebSocketBridge.SendAsync` silently stringifies non-primitive arguments (Low/brittle, 85%)

```csharp
// Agent.World.Minecraft/WebSocketBridge.cs:123-132
switch (kv.Value)
{
    case int i:    writer.WriteNumber(kv.Key, i);  break;
    case long l:   writer.WriteNumber(kv.Key, l);  break;
    case double d: writer.WriteNumber(kv.Key, d);  break;
    case float f:  writer.WriteNumber(kv.Key, f);  break;
    case bool b:   writer.WriteBoolean(kv.Key, b); break;
    case null:     writer.WriteNull(kv.Key);        break;
    default:       writer.WriteString(kv.Key, kv.Value.ToString()); break;
}
```

Any argument value that isn't one of the five scalar types above — a `List<T>`, `Dictionary<string,object?>`, array, or custom object — falls into `default` and is serialized via `.ToString()`. For a `Dictionary`/`List`, `.ToString()` produces something like `"System.Collections.Generic.Dictionary\`2[System.String,System.Object]"` — a garbage string silently sent to Node.js as if it were valid data, with no error, warning, or log anywhere in this path.

**Current risk level:** confirmed **latent, not active** — a repo-wide check of every `ActionData.Arguments[...]` assignment across `Agent.Tools`, `Agent.Planning`, and `WebUI.Blazor` found no tool currently passing a non-scalar argument value. All current tools pass only `int`, `string`, `bool`, or `double`.

**Why flag it anyway:** this is exactly the kind of trap the project's stated "no legacy systems, no technical debt" standard is meant to catch *before* it's exercised, not after. The backlog already contains forward-looking work that's a plausible trigger — e.g. `TSK-0234` (custom inline blueprint build goal with an agent-supplied block layout) or any future batch-action tool — where a natural implementation choice would be to pass a list of coordinates or a nested object as a single argument. If/when that happens, this code will silently corrupt the payload instead of failing loudly.

**Recommendation:** Add a final `default` case that either (a) recursively serializes via `JsonSerializer.SerializeToUtf8Bytes`/writes a proper JSON array or object instead of calling `.ToString()`, or (b) throws/logs an explicit error for unsupported argument shapes so a future violation fails fast at the send boundary instead of silently transmitting garbage. Low urgency given no active trigger today, but cheap to fix now and worth doing before `TSK-0234`-class work lands.

---

## Assumptions & Open Questions (this pass)

1. Did not exhaustively re-verify every one of the 34 findings in `internal-audit-57-20260701.md` against current HEAD — spot-checked those most likely to have drifted (P0-3/TSK-0321, P1-7) or to interact with in-progress work; the remainder were cross-referenced against task status only (title/description match), not re-read against source.
2. `ChatInterpreter.cs`/`LlmChatInterpreter.cs` and the full `AgentBackgroundService.cs` chat/command-handling region (~lines 1264-1900) were not line-by-line reviewed this pass — flagged as the next-highest-value target if further depth is wanted, given their size and the safety-relevant content (deny-list, command dispatch) already known to live there.
3. Item 2's recommendation to reuse the existing `FactSource` enum vs. adding a new `BuildOriginSource` member is a suggestion, not a verified-compatible design — the two enums may model different axes (data provenance vs. spatial-origin provenance) and warrant a quick compatibility check before implementation.
