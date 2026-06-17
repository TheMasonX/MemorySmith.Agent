# MemorySmith.Agent — Sprint 9 & 10 End-of-Sprint Council Review

**Date:** 2026-06-17  
**Scope:** Sprint 9 (flat-area scanner) + Sprint 10 (build robustness, deferred carries)  
**Branch:** sprint-5-tool-safety  
**CI commit:** 866d637 (green — Phase 0 early-return fix)  
**Seats:** Architecture · Minecraft World · Correctness · Experience · Skeptic+Synthesizer

---

## Seat 1 — Architecture Chair (Confidence: 88%)

**Positive:**
- `BuildFactKeys.cs` is the strongest architectural win — eliminates the string-duplication coupling between `AgentBackgroundService` (writer) and `HtnTaskLibrary` (reader). `BuildProgressIndex(string)` parameterised correctly (blueprint-scoped key, not a static constant).
- `FLAT_SCORE_WEIGHTS = Object.freeze({area:0.5, compactness:0.3, flatness:0.2})` replaces magic numbers; weights sum semantically to 1.0 inside the multiplicand.
- `LIQUID_BLOCK_NAMES = Set([...])` is O(1) membership, extension-friendly.
- `TryGetIntFact` centralises int/long/double/string coercion — without it every reader reinvents boxed-numeric normalisation.
- `AGENTS.md` rewrite matches house style: rules first, patterns second, anti-patterns last.

**Concerns:**
- `WorldState.Facts: Dictionary<string, object?>` is accumulating reader workarounds (`TryGetIntFact`, manual boxing checks). A typed `WorldFact` union would let the compiler help. **Deferred Sprint 11.**
- `ActionData.Context` mutation through `IReadOnlyList<ActionData>` is technically correct but surprising — add an inline comment at the annotation site.
- Three-fact non-atomic auto-origin write. **Deferred — safe while reads stay single-threaded.**
- `CraftingChainOrder` and `RequiresCraftingTable` are parallel structures that must stay in lockstep. Fold into a declarative recipe table. **Deferred Sprint 11.**

**Blocking:** None.

---

## Seat 2 — Minecraft World Chair (Confidence: 90%)

**Positive:**
- Vec3 fix is the single most important shipped change. `ReferenceError` on first call = completely broken. Plain-object substitution is idiomatic Mineflayer.
- `yAbove=10 / yBelow=16` well-tuned for overworld. Per-call override supports nether/end scenarios.
- Composite score (0.5/0.3/0.2) correctly weights compactness. A 5×5 square beats a 1×25 strip — matches Minecraft build intuition.
- Slope filter `yRange > maxSlope=3` runs before scoring (correct ordering — cheaper to reject than score).
- `SmeltItem iron_ore → iron_ingot` correctly reflects vanilla mechanics — `MineBlock iron_ore` does not yield ingots directly.
- CraftingChainOrder extension covers the full early-game vocabulary including 1.20+ cherry wood.
- Async yield every 200 columns prevents Mineflayer event-loop starvation during large scans.

**Concerns:**
- `maxSlope=3` global — a 3×3 hut wants `yRange=0`, a terrace wants 5. Per-blueprint override. **Deferred.**
- `LIQUID_BLOCK_NAMES` excludes `bubble_column`. Minor edge case. **Deferred.**
- `FlatAreaFoundEvent.area >= 25` threshold fixed globally — needs per-blueprint override for large structures. **Deferred.**

**Blocking:** None.

---

## Seat 3 — Correctness Chair (Confidence: 78%)

**Positive:**
- `GroupBy().Sum()` fix is a genuine bug fix: `ToDictionary` was throwing `ArgumentException` on duplicate material block IDs. Aggregate-by-sum is the correct semantic.
- B1 Phase 0 guard (return `[FindFlatArea, GetStatus]` when origin still (0,0,0)) prevents "build at world origin" failures.
- B2 checkpoint correctly carries `blueprintId` + `blockIndex` as action context so `DispatchActionsAsync` knows which fact to write without recomputing.
- `TryGetIntFact` handles all four realistic storage shapes.
- `detail=false` defaults to `true` — zero breaking change for existing consumers.
- `area < 25` events log info but do not overwrite facts — safe.

**Blocking (light):**
- No test-coverage claim for `TryGetIntFact` (4 types), `GroupBy.Sum` duplicate aggregation, B1 early-return shape, B2 resume skip-correctness. Should be in the test suite. **Sprint 11.**

**Deferred:**
- Three-fact non-atomic write safe only while single-threaded.
- `detail=false` JSON schema: missing key vs. null key — document the contract.
- Liquid check is on ground column only; column-above (e.g., rain puddle) not gated.

---

## Seat 4 — Experience Chair (Confidence: 85%)

**Positive:**
- `AGENTS.md` Matt Pocock rewrite is a force-multiplier: "7 architecture rules", "where things live" table, ❌/✅ pairs. A new contributor or agent invocation reads this first.
- Two new `Data/Pages/Guides/` files split the audience correctly: operators vs. contributors.
- `?detail=false` improves polling dashboards that don't need full `RecentObservations`.
- `JournalEntryType.Observation` on `FlatAreaFoundEvent ≥ 25` gives postmortem trails.
- Info log on sub-threshold scans answers "why didn't it pick this spot?"

**Deferred:**
- No `/api/agent/flatscan` introspection endpoint for last-N scan attempts.
- No "resumed at block N of M" log line for B2 resume — add to `DispatchActionsAsync`.
- `AGENTS.md` needs a `last-reviewed` header and assigned maintainer.

**Blocking:** None.

---

## Seat 5 — Skeptic + Synthesizer (Confidence: 82%)

**Challenges:**
1. Vec3 was a complete-break bug never caught by CI. A smoke test booting the bot and calling every tool once would catch it in 30 seconds. **Sprint 11 blocking.**
2. B2 checkpoint survival depends on `WorldState.Facts` persistence boundary (in-memory only = not crash-recovery). Document the scope clearly.
3. B1 Phase 0 replanner dependency — if goal is cleared between FindFlatArea and replan, agent stalls. Needs stall detector.
4. Parallel `CraftingChainOrder` + `RequiresCraftingTable` structures will drift.

**Verdict:** SHIP. Vec3 fix + GroupBy.Sum fix + BuildFactKeys consolidation justify both sprints. B2 is correctly framed as "skeleton". The abstractions chosen are extension-friendly. Testing rigor is medium — Sprint 11 owes tests for B2 resume and `TryGetIntFact` coercion.

---

## Synthesis Table

| Finding | Seat | Severity |
|---------|------|----------|
| Vec3 ReferenceError — complete break, no CI gate caught it | Skeptic | HIGH |
| `ToDictionary` → `GroupBy.Sum` fixes duplicate-key crash | Correctness | HIGH ✅ fixed |
| `BuildFactKeys` eliminates string-duplication bug class | Architecture | HIGH ✅ fixed |
| B2 checkpoint persistence boundary not specified | Skeptic | MEDIUM |
| Three-fact non-atomic auto-origin write | Architecture | MEDIUM deferred |
| No test coverage for `TryGetIntFact`, GroupBy.Sum, B1 shape, B2 skip | Correctness | MEDIUM (Sprint 11) |
| `maxSlope=3` global — needs per-blueprint override | Minecraft World | MEDIUM deferred |
| `CraftingChainOrder` / `RequiresCraftingTable` parallel lists | Architecture | MEDIUM deferred |
| `detail=false` schema (missing key vs. null) undocumented | Correctness | LOW |
| Liquid check: ground only, not column-above | Correctness | LOW deferred |
| B2 resume log line missing | Experience | LOW |
| No smoke test gate in CI | Skeptic | HIGH (Sprint 11) |

---

## Testable Acceptance Criteria

1. `findFlatArea` completes without `ReferenceError` (no Vec3 import needed; `bot.blockAt({x,y,z})` succeeds).
2. Blueprint with two `oak_planks` material entries (qty 4 + qty 3) produces `materials["oak_planks"] = 7`, no exception.
3. `DecomposeBuild(origin=(0,0,0), no auto-origin)` returns exactly `[FindFlatArea, GetStatus]` with zero `PlaceBlock` actions.
4. `DecomposeBuild` with `BuildProgressIndex = 4` on a 10-block blueprint emits `PlaceBlock` for indices 5..9 only, each annotated with `PlaceBlockProgressBlockIndex = i`.
5. `TryGetIntFact` returns `(true, 42)` for boxed `int`, `long`, `double`, and `string "42"`; `(false, 0)` for missing key.
6. Candidate region with `yRange = 4, maxSlope = 3` is rejected before A2 score computation.
7. Columns with ground block in `LIQUID_BLOCK_NAMES` are excluded from `heightMap`.
8. `FlatAreaFoundEvent` with `area = 25` → `SetBuildOrigin` called + `Observation` journal entry. With `area = 24` → neither side effect, one info log.
9. `WorldState.Facts[BuildFactKeys.LastFlatArea]` equals `N` after projecting `FlatAreaFoundEvent(Area=N)`.
10. `?detail=false` response excludes `recentObservations`; `?detail=true` includes it.
11. Blueprint needing `iron_ingot` produces a plan with `MineBlock iron_ore` → `SmeltItem iron_ore` → craft steps.
12. Every item in `RequiresCraftingTable` is in `CraftingChainOrder`; no item in 2×2 recipes is in `RequiresCraftingTable`.
13. CI must include a Node.js smoke test calling `findFlatArea` against a stub world. **Sprint 11.**

---

## Open Questions for Sprint 11

1. Are `WorldState.Facts` persisted across restarts? Scope B2 explicitly.
2. Should `BuildOrigin` become a single atomic composite fact?
3. Per-blueprint `maxSlope`, `minArea` configuration — on manifest, goal, or argument?
4. Declarative crafting recipe table — when?
5. Stall detector SLA — "no action for 10s with active goal = warning"?
6. `/api/agent/flatscan?n=10` endpoint — Sprint 11?
7. `AGENTS.md` maintainer assignment + `last-reviewed` header.
8. `bubble_column` and related physics blocks in `LIQUID_BLOCK_NAMES`?
9. `detail=false` schema: omit key or send null?
10. Smoke test CI gate — Node runner + tool callsite coverage.
