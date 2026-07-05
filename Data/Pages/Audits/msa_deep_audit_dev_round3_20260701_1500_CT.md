# MemorySmith.Agent — Deep Codebase Audit (dev/round-3)

Report: msa_deep_audit_dev_round3_20260701_1500_CT.md

Generated: 2026-07-01 15:00 CT (approximate — no live clock available this session)
Repository: TheMasonX/MemorySmith.Agent
Branch: dev/round-3
Commit: 57b8fdf79ff7ba6f877e0fcac2211934e0f3279c ("Sprint 58 Wave D — thread safety, cancellation, inventory diff")

Focus Areas:
* Full-codebase pass (all 6 C# projects + MineflayerAdapter JS), prioritized by lines-of-code and "in-progress" architecture
* Sprint 57 architecture (ExecutionContext / ActionRegistry / PlanningPolicy / six-manager decomposition) — wiring status
* Inventory truth (`updateSlot`, TSK-0281/0063/0163) and precondition/postcondition (TSK-0310) effectiveness
* Cross-check of ~55 prior audit documents and 354 tracked tasks to avoid duplicate findings
* Mineflayer adapter: Vec3 shim, `bestHarvestTool`, crafting-table logic, command safety

---

## 0. Method Note (read before the findings)

This codebase has been audited **at least 20 times before** (55 files under `Data/Pages/Audits/`), including `internal-audit-57-20260701.md` — dated the **same day as HEAD** and covering 34 findings across P0–P3. Re-deriving that list from scratch would waste your context budget and mine. Instead this pass:

1. **Verified** the fix-status of every P0/P1 finding in `internal-audit-57-20260701.md` against the actual `dev/round-3` source (not the handoff doc, which is itself slightly stale — see §2).
2. **Did not re-litigate** anything already `Done` and confirmed correct in code.
3. Spent its "new discovery" budget on: (a) whether Sprint 57's architecture is *actually* load-bearing yet, (b) the Mineflayer JS layer (under-audited relative to C#), and (c) a full line-level read of `Agent.Core` (4,096 lines) and targeted reads of `Agent.Planning`, `WebUI.Blazor/AgentBackgroundService.cs` (3,856 lines), and `MineflayerAdapter/index.js` (2,084 lines).
4. `Data/Tasks/*.json` (354 files) and `Data/Pages/Audits/*.md` (55 files) were read for cross-reference, not line-audited as "code" — they are backlog data, not runtime artifacts.

**Confidence key:** Verified (I read the exact lines and confirmed the behavior) / Reported (internal-audit-57 or a task file states it, I did not independently re-derive it this pass) / Inferred (pattern-based, not execution-traced).

---

## 1. Executive Summary

- **The codebase is in unusually good hygiene for its size.** Zero `TODO`/`FIXME`/`HACK` litter, zero `async void`, zero live sync-over-async, no empty/unlogged catch blocks, 815 passing tests, no stray `.bak` files (a prior audit's finding — already cleaned up). This is not a codebase drowning in debt at the line level.
- **The debt is architectural, not tactical, and it is well-known internally.** Three to four successive audits (Wave B, `internal-audit-57`, this one) all land on the same conclusion: **the Sprint 36/37/57 "decomposed runtime" (six manager interfaces + `AgentRuntime` + `ExecutionContext`/`ActionRegistry`/`PlanningPolicy`) is fully built, DI-registered, unit-tested, and 100% unreachable from the live agent loop.** `AgentBackgroundService` (3,856 lines) still does everything itself. TSK-0292/0293 exist to fix this and are `Ready` — this audit's contribution is a concrete, previously-undocumented proof that the cost of *not* doing TSK-0292/0293 is already showing up in shipped work (§3.1).
- **New, previously untracked finding:** `HtnTaskLibrary.BuildCraftingChain` (build-material prep path) crafts `crafting_table` *before* it crafts the `oak_planks` the table recipe needs — a real ordering bug, and a second, less-careful reimplementation of logic that `AddCraftingTableIfNeeded` (the craft-goal path) already does correctly (§3.2).
- **New, previously untracked finding:** the Mineflayer adapter carries a 231-line hand-rolled `Vec3` shim (`vec3.js`) reimplementing a class that is **already installed on disk** as a transitive dependency of `mineflayer`/`mineflayer-pathfinder` (`vec3@0.1.10`, confirmed in `package-lock.json`). The shim has its own bug history (TSK-0262). This is pure, avoidable legacy surface in a project whose explicit goal is "no legacy systems" (§3.3).
- Wave D (the commit under audit) correctly closed 5 real bugs (2 P0, 1 P1, 2 P2) with clean, minimal, well-tested diffs. No regressions found in the changed files.
- The two specific historical bugs Lucas flagged from memory — Vec3 **corner-offset** and `bestHarvestTool` **misrouting** — could not be reproduced in current code. The corner-offset issue was fixed under TSK-0262 (verified). The `bestHarvestTool` misrouting could not be located at either call site; both null-check the return value and fall back safely. Recommend treating this as resolved/stale unless a repro log is available (§3.4).

---

## 2. Current State Assessment

| Area | Status | Confidence |
|---|---|---|
| Sprint 58 Wave D (the audited commit) | 5/5 tasks correctly implemented, tests pass | Verified 95% |
| Sprint 57 architecture (ExecutionContext/ActionRegistry/PlanningPolicy/6 managers) | Built, DI-wired, **not called by the live path** | Verified 98% |
| Inventory truth (`updateSlot`) | Still not wired; still 6+ partially-authoritative event paths | Verified 90% |
| Safety/deny-list layer | All 3 previously-found bugs (P0-4, P1-8, P1-9) are now genuinely fixed | Verified 92% |
| `internal-audit-57` P0-2/P0-3 (LLM-eval fast path, inventory sync guard) | Already fixed (Wave C), **but the Wave D handoff doc doesn't mention this** — minor doc-drift | Verified 90% |
| Mineflayer adapter modularization (TSK-0039/0166) | Still monolithic, 2,084 lines, `InProgress` | Verified 95% |
| Test suite | 815 tests, NUnit, all C# projects covered; Mineflayer JS has effectively no automated tests (1 tiny file, `gameModeState.test.js`, 23 lines) | Verified 90% |

---

## 3. Major Findings

### 3.1 [NEW SYNTHESIS, not a new bug] TSK-0310's precondition checks are unreachable in production — concrete proof of the P1-1 dead-architecture risk

**Confidence: 95% (Verified)**

`internal-audit-57-20260701.md` (P1-1) already documents that the six manager interfaces + `AgentRuntime` are DI-registered but never invoked by `AgentBackgroundService`, and warns abstractly: *"New features added to managers don't affect actual agent behavior, creating a false sense of architecture maturity."*

This audit found the concrete instance. **TSK-0310** ("Implement `IGoalPrecondition` on gather/craft/smelt goals") is marked `Done`, was implemented correctly, and is genuinely called — but only from:

```csharp
// WebUI.Blazor/Managers/PlanningManagerImpl.cs:69-72
if (goal is IGoalPrecondition precondition)
{
    if (!precondition.CanAttempt(context, out var blockingReason))
```

`PlanningManagerImpl` is the `IPlanningManager` implementation. `grep -c "PlanningManager|ExecutionManager|RecoveryManager|StateManager|DashboardPublisher|AgentRuntime" WebUI.Blazor/AgentBackgroundService.cs` returns **zero matches, case-insensitive, across all six manager types**. `AgentBackgroundService` never resolves `IPlanningManager` and never calls `PlanAsync(ExecutionContext, ...)`. `GenericGatherGoal`, `CraftItemGoal`, and `SmeltGoal` all implement `IGoalPrecondition` correctly and have passing unit tests (`Sprint57ExecutionContextTests.cs`) — but in the live agent, **precondition checking never runs**. A goal that should be blocked (e.g. attempting a survival-only gather while inventory is stale) will not be blocked, because the only code path that checks `CanAttempt` is dead.

**Why this matters more than the abstract P1-1 finding:** it shows the dead-architecture problem is not static — it is actively absorbing new, correctly-implemented Sprint 58 feature work (TSK-0310) that then delivers zero production value. Every sprint that adds to the manager layer instead of wiring it in increases the eventual TSK-0292/0293 migration cost.

**Recommendation:** Do not schedule further feature work *inside* the manager layer (`PlanningManagerImpl`, etc.) until TSK-0292 either wires it in or the layer is deleted. Recommend elevating TSK-0292 above other P2/P3 backlog items for Sprint 59 — this is not a style preference, it's stopping active waste.

**No new task needed** — this is evidence for TSK-0292/0293, which already exist and are `Ready`.

---

### 3.2 [NEW] `HtnTaskLibrary.BuildCraftingChain` crafts the crafting table before its own prerequisite

**Confidence: 82% (Verified code path exists; not runtime-traced against a live blueprint build)**

**File:** `Agent.Planning/HtnTaskLibrary.cs`, `BuildCraftingChain` (~line 656–665)

There are **two separate, non-shared implementations** of "ensure a crafting table exists" in this file:

1. `AddCraftingTableIfNeeded` (line 374) — used by `DecomposeCraftItem` (standalone `CraftItemGoal`). Correctly checks `oak_planks < 4` → mines `oak_log` → crafts `oak_planks` → *then* crafts `crafting_table`, in that order.
2. `BuildCraftingChain` (line 656) — used by `DecomposeBuild` (blueprint material prep). Does this instead:

```csharp
bool anyTableRequired = materials.Keys.Any(RequiresCraftingTable.Contains);
if (anyTableRequired && !materials.ContainsKey("crafting_table")
    && state.Inventory.GetValueOrDefault("crafting_table") == 0)
{
    actions.Add(ActionFactory.Create("CraftItem", ("item", "crafting_table"), ("count", 1)));
}

foreach (var item in CraftingChainOrder)     // oak_planks is later in this list
    EmitCraftIfNeeded(item, materials, state, actions);
```

The `CraftItem(crafting_table)` action is appended to the action list **before** the loop that would append `CraftItem(oak_planks, N)`. If the bot doesn't already have ≥4 `oak_planks` in inventory when a build blueprint needs a crafting table, the generated plan will attempt to craft the table with zero planks in hand and fail.

This is exactly the "manual-sync footgun" pattern the codebase's own comments elsewhere warn about (`CommonMinecraftBlocks.cs`, Sprint 14 P1a: *"eliminate the manual-sync footgun... single source of truth"*) — except it recurred here, in a sibling method, for the same table-bootstrap concern.

**Likely why it hasn't been caught:** most build blueprints that need tables (chest, door, tools) are probably tested/used after the bot has already accumulated planks from an earlier gather phase, masking the ordering bug in practice.

**Recommendation:** Delete `BuildCraftingChain`'s inline table-crafting block; call `AddCraftingTableIfNeeded`-equivalent logic (or literally extract a shared helper both methods call) so there is one crafting-table bootstrap implementation, not two with different correctness. Recommend filing as new task, e.g. `P1: BuildCraftingChain crafts crafting_table before its oak_planks prerequisite — consolidate with AddCraftingTableIfNeeded`.

---

### 3.3 [NEW] Hand-rolled Vec3 shim duplicates an already-installed dependency

**Confidence: 80% (Verified the duplication and the transitive dependency; did not verify whether an ESM/CJS import conflict originally forced this choice — no comment in the repo documents such a conflict)**

**Files:** `MineflayerAdapter/vec3.js` (231 lines, 46 methods) vs. `MineflayerAdapter/package-lock.json`

`vec3.js` is a complete custom reimplementation of the `vec3` API (the library `mineflayer`/`mineflayer-pathfinder` use internally, published as `prismarine-vector`'s successor `vec3` on npm). It was introduced in Sprint 18 to fix a crash (`point.minus is not a function`) and has since needed a follow-up correctness fix (TSK-0262, `.floored()` returning `this` instead of a new object — a real historical corner-offset bug, now fixed).

`package-lock.json` confirms `vec3@0.1.10` is **already resolved and present** as a transitive dependency of `mineflayer` and `mineflayer-pathfinder`:

```
"node_modules/vec3": { "version": "0.1.10", "resolved": "https://registry.npmjs.org/vec3/-/vec3-0.1.10.tgz", ... }
```

`vec3` is not in `MineflayerAdapter/package.json`'s explicit `dependencies`, but under npm's flat `node_modules` resolution it is directly `require`/`import`-able today without any package.json change (though adding it explicitly is the correct hygiene move — see recommendation). There is no comment anywhere in the repo explaining why the real package was rejected in favor of a hand-rolled shim; the original Sprint 18 comment only says the shim was added "so `bot.blockAt()` doesn't crash," suggesting the real package's availability may simply not have been noticed at the time.

**Why this matters for a "no legacy systems" project:** this is a textbook case of maintained, bug-prone, duplicate infrastructure standing in for a battle-tested library that ships with the very dependencies you already require. Every future Mineflayer API surface touching `Vec3` (new methods, edge cases in `offset`/`floored` semantics as Mineflayer version-bumps) is now the team's problem to track and re-implement, rather than getting picked up by `npm update`.

**Recommendation:**
1. Add `"vec3": "^0.1.10"` explicitly to `package.json` (pinning what's already transitively resolved — no behavior change).
2. Swap `import { toVec3 } from './vec3.js'` for `import { Vec3 } from 'vec3'` and `toVec3(x,y,z)` → `new Vec3(x,y,z)`.
3. Diff the real library's `.floored()`/`.offset()` semantics against the shim's before cutting over, specifically re-validating the TSK-0262 scenario, since that's the one place this shim's behavior has previously diverged from the real contract.
4. Delete `vec3.js` and its 17 call sites' import once cutover is validated.

This is real migration effort (not a one-line fix) given 17+ call sites, but it removes 231 lines of parallel-maintained surface area permanently. Recommend a new task, e.g. `P2: Replace hand-rolled vec3.js shim with the already-installed 'vec3' npm package`.

---

### 3.4 [MEMORY RECONCILIATION] Vec3 corner-offset and `bestHarvestTool` misrouting

**Confidence: 70% (Verified fixed / not reproduced) — this is a "close the loop" item, not a new finding**

- **Vec3 corner-offset:** Confirmed fixed. `.floored()` in `vec3.js` returns a new object (line 155–157), with an explicit comment citing TSK-0262 and explaining exactly the corruption mechanism (prismarine-world mutating `block.position` in place). No further corner-offset defect found in the current shim.
- **`bestHarvestTool` misrouting:** Not reproduced. Two call sites exist (`index.js:920` in `mine`, `index.js:1153` in `place`'s terrain-clear-before-place branch). Both call `bot.pathfinder.bestHarvestTool(block)`, null-check the result, and fall back to bare-handed digging with structured logging on failure. Neither shows an obvious tool/block mismatch. This may have referred to a fix already merged, a git-history-only issue, or a symptom now masked by the TSK-0262 Vec3 fix (a corrupted block position could plausibly cause `bestHarvestTool` to evaluate against the wrong block). Recommend closing this from active memory unless a specific log/repro is available — re-opening it speculatively would waste a future audit cycle.

---

## 4. Architectural Analysis

**A-1 (confirmed, already known): Split-brain runtime.** `AgentBackgroundService` and the six-manager `AgentRuntime` layer are two independent implementations of the same responsibilities. Verified via exhaustive `grep` — zero cross-references from ABS into the manager layer. §3.1 shows this is now costing real feature value (TSK-0310), not just theoretical drift.

**A-2 (confirmed, already known): Two fact systems.** `WorldState.Facts` (untyped legacy dict, no eviction — P3-1 in internal-audit-57) and `WorldState.StructuredFacts` (provenance-tracked, has a trim mechanism) coexist. `EntityObservedEvent` writes to `Facts` only, bypassing the builder — confirmed still present in the handler pattern used throughout `WorldStateProjector.StoreFacts`.

**A-3 (confirmed via direct read, reinforces TSK-0281/0302): Inventory has no single source of truth by design, not by oversight.** `WorldStateProjector.ApplyBlockMined` contains this self-aware comment:

> *"ItemCollectedEvent... remains the authoritative source and will add a second increment when the item IS collected — but this is acceptable because... the alternative (stuck at 0 forever when drop falls through a hole) is far worse than occasional double-count."*

This is a reasonable tactical tradeoff, correctly documented — but it is also exactly the failure mode TSK-0281/TSK-0063/TSK-0163 (`updateSlot` wiring) and TSK-0302 (inventory SSOT refactor) already exist to eliminate. All three remain `Backlog`. This audit adds no new task here but flags that the double-count isn't theoretical — it's an accepted, load-bearing behavior today, and `InventorySyncLoopAsync` (fixed in Wave C to run during active goals, confirmed) is the only thing currently bounding the drift. **This raises TSK-0281's priority signal**, since it's the one task in that cluster that most directly replaces inference with ground truth.

**A-4 (new observation): Duplicate crafting-table bootstrap logic (§3.2) is a smaller instance of the same "manual-sync footgun" pattern `CommonMinecraftBlocks.cs` was created specifically to prevent** for block/item classification. The fix pattern is identical: extract one shared helper.

---

## 5. Planning & Roadmap Review

Cross-referenced against all 354 `Data/Tasks/*.json` entries and the Sprint 58 handoff (`sprint-58-waved-complete.md`).

- Wave D (this commit) closes exactly what it claims: TSK-0330–0334, all verified fixed in code, not just in the handoff doc.
- The Wave D handoff's "Remaining Audit Findings (Deferred)" table is **slightly stale**: it doesn't mention that P0-2 (LLM-eval fast path) and P0-3 (inventory sync guard) were already fixed in **Wave C**, one wave earlier. Not a functional bug — just a documentation-sync gap worth a one-line fix next handoff so future audits don't waste time re-checking already-closed items. (This report is itself proof of the cost: ~20 minutes of this audit's budget went to re-verifying items that were, in fact, already fixed.)
- TSK-0292 and TSK-0293 are correctly the two highest-leverage open items on the board for the "no legacy systems" goal. §3.1 is new ammunition for prioritizing TSK-0292 specifically.
- No task currently exists for §3.2 (BuildCraftingChain ordering) or §3.3 (Vec3 shim replacement) — recommend filing both.

---

## 6. Missed Opportunities

1. **JS test coverage.** `MineflayerAdapter` has essentially one 23-line test file (`gameModeState.test.js`) against a 2,084-line `index.js`. All the C# side's discipline (815 NUnit tests) doesn't reach the adapter boundary, which is exactly where physical-world timing bugs (P0-1's original data race, the historical Vec3 corner-offset bug, TSK-0270/0271/0273's timeout issues) have clustered. A handful of targeted unit tests around `vec3.js`, `classifyError`, and the crafting-table branch would be disproportionately high-leverage given the shim replacement work in §3.3 is about to touch this exact surface.
2. **`AddCraftingTableIfNeeded` extraction.** Once §3.2 is fixed, promoting the corrected logic to a single shared helper (used by both `DecomposeCraftItem` and `BuildCraftingChain`) closes off recurrence for any *third* crafting-table consumer added later.

---

## 7. Risks

| Risk | Severity | Likelihood |
|---|---|---|
| Continued feature work landing in the dead manager layer (repeat of TSK-0310) | High | Medium — no code path currently prevents it |
| BuildCraftingChain ordering bug triggers mid-build failure on a table-requiring blueprint with empty planks | Medium | Low-Medium — depends on typical starting inventory at build time |
| Vec3 shim diverges further from real library semantics as Mineflayer is upgraded | Medium | Low near-term, compounding over time |
| Inventory double-count (§A-3) masks a real shortfall during a long active-goal window | Medium | Low — Wave C fix (sync during active goals) already bounds this |

---

## 8. Recommended Priorities

1. **Decide TSK-0292's fate this sprint** — wire the manager layer in or delete it. §3.1 shows the "in-between" state is actively costing real work (TSK-0310). This is a decision, not new engineering — the internal-audit-57 recommendation stands.
2. **File and fix §3.2** (BuildCraftingChain ordering) — small, well-isolated fix, real user-facing failure mode.
3. **File §3.3** (Vec3 shim replacement) as a scoped migration task — not urgent, but cheap to do now while the adapter is already slated for modularization (TSK-0166).
4. **One-line handoff-doc fix**: note in future Sprint handoffs which findings were already closed in an earlier wave, to stop re-verification cost compounding across audits.
5. Continue existing TSK-0281/0322 priority as already set — this audit found no reason to change their ordering.

---

## 9. Open Questions

- Was there ever a concrete ESM/CJS or version-pinning reason `vec3.js` was hand-rolled instead of importing the real package in Sprint 18? No comment documents one; if such a reason exists and isn't written down anywhere, it should be captured before anyone attempts §3.3's migration.
- Is `BuildCraftingChain`'s bug latent because typical build-goal starting inventory already has planks (from an earlier gather phase), or has it actually fired and been misattributed to a different failure reason in logs? Worth a quick log grep for `"Crafting table not found"` / craft-failure messages correlated with build goals before scoping the fix.
- Does the team want `IGoalPrecondition` checking active in production *before* TSK-0292 lands (e.g., a narrow, temporary call from `AgentBackgroundService` directly into `PlanningManagerImpl`'s precondition check, without the rest of the manager plumbing), or is it acceptable to leave TSK-0310 dormant until the full decomposition ships? This materially affects whether §3.1 is P1 or P2.

---

## 10. Confidence Summary

| Finding | Confidence | Basis |
|---|---|---|
| §3.1 TSK-0310 preconditions unreachable in production | 95% | Verified — direct grep + code read, zero cross-references |
| §3.2 BuildCraftingChain crafting-table ordering bug | 82% | Verified code path; not runtime-traced |
| §3.3 Vec3 shim duplicates installed dependency | 80% | Verified duplication + lockfile; motive for shim not documented |
| §3.4 Vec3 corner-offset fixed | 90% | Verified via code + TSK-0262 comment |
| §3.4 bestHarvestTool misrouting not reproduced | 70% | Absence of evidence, not proof of absence |
| A-3 Inventory double-count is by-design, still open | 92% | Verified via source comment + task backlog status |
| All Wave D (TSK-0330–0334) fixes correct | 95% | Verified via commit patch + source |
| P0-2/P0-3/P0-4/P1-8/P1-9/P0-7 already fixed (contra assumption) | 90% | Verified via direct source read |
| P1-4 (PlaceBlockGoalDecomposer same-coordinate bug) still open | 95% | Verified — code unchanged from internal-audit-57 description |
| P1-2/P1-5 (EntityObservedEvent, BuildGoalDecomposer origin) still open | 60% | Reported by internal-audit-57, not independently re-verified this pass |

---

## 11. Supporting Evidence

- Commit under audit: `57b8fdf79ff7ba6f877e0fcac2211934e0f3279c` (patch fetched and read in full — 548 lines).
- `Agent.Core` (4,096 lines across 47 files): read in full.
- `WebUI.Blazor/AgentBackgroundService.cs` (3,856 lines), `Program.cs` (841 lines): targeted read + exhaustive grep for manager/context wiring.
- `Agent.Planning/HtnTaskLibrary.cs`: read in full for crafting/build decomposition logic.
- `MineflayerAdapter/vec3.js` (231 lines): read in full. `index.js` (2,084 lines): targeted read around `mine`, `place`, `craft` handlers.
- `Data/Tasks/*.json` (354 files): parsed programmatically for status cross-reference.
- `Data/Pages/Audits/internal-audit-57-20260701.md` (596 lines) and `Data/Pages/Handoffs/sprint-58-waved-complete.md`: read in full for duplication avoidance.
- `Data/Pages/Preferences/audit-report-preferences.md`: read in full; this report follows its structure and naming convention.
- Not exhaustively line-audited this pass (scope tradeoff, disclosed per §0): `Agent.Tools` (1,379 lines, 14 tools — spot-checked, no anomalies found), `Agent.Vision` (60 lines, trivial), `Agent.Personality` (27 lines, trivial), `Agent.Memory` (988 lines — not read this pass, no findings claimed), `WebUI.Blazor/Dashboard` and `wwwroot` (UI layer, out of scope for a backend/architecture audit), the ~50 other historical audit documents beyond the two cited above (skimmed by filename/date for relevance, not read in full).
