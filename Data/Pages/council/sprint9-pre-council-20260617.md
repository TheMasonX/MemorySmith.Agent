# Sprint 9 Pre-Sprint Council Review — Flat-Area Scanner + Deferred Carries

**Date:** 2026-06-17  
**Branch:** sprint-5-tool-safety  
**CI status:** GREEN (c70bc23)  
**Sprint:** 9  
**Review type:** Pre-sprint (5-seat)  
**Seats:** Architecture, Minecraft World, Correctness, Experience, Skeptic+Synthesizer

---

## Plan under review

| ID | Task | File |
|----|------|------|
| Vec3-fix | `new Vec3(cx,cy,cz)` — Vec3 not imported → ReferenceError at runtime | `MineflayerAdapter/index.js` |
| A1 | Widen vertical scan from ±5/6 to configurable ±10/±16 | `MineflayerAdapter/index.js` |
| A2 | Compactness scoring: prefer square regions over thin strips | `MineflayerAdapter/index.js` |
| A5 | Slope penalty: reject components with yRange > maxSlope (default 3) | `MineflayerAdapter/index.js` |
| A3 | Wire FlatAreaFoundEvent → auto-set build origin in AgentBackgroundService + HtnTaskLibrary | `AgentBackgroundService.cs`, `HtnTaskLibrary.cs` |
| A4 | Unit tests: FlatAreaFoundEvent projector + handler | `MemorySmith.Agent.Tests/` |
| S7-D3 | WorldModel endpoint: `?detail=false` summary mode | `WebUI.Blazor/Program.cs` |

---

## Seat 1 — Architecture Chair

**Confidence: 78%**

**Findings:**
- Vec3-fix is the highest-severity, lowest-cost item. The bug means `findFlatArea` has never run successfully in a real bot session.
- A2's scoring weights (0.5 / 0.3 / 0.2) must be **named constants** — three magic floats in one expression violate Matt Pocock's "no magic values" principle. Introduce a `FLAT_SCORE_WEIGHTS` object literal.
- A3 creates a hidden coupling: `SetBuildOrigin("auto", ...)` writes string facts in `AgentBackgroundService`; `HtnTaskLibrary.DecomposeBuild` reads the same strings. The shared strings must be named constants in one module (`BuildFactKeys`). Adding this coupling without named constants is a refactor hazard.
- S7-D3's `?detail=false` boolean flag is fine for Sprint 9. A `BeliefStateSummary` DTO is the right long-term move (Sprint 10).

**Blocking:** Introduce `BuildFactKeys` constants in the same PR as A3.  
**Deferred:** `FlatAreaScoringWeights` config plumbed to C#; `BeliefStateSummary` DTO.

---

## Seat 2 — Minecraft World Chair

**Confidence: 72%**

**Findings:**
- A1's wider scan window is biome-correct for extreme hills / badlands. But in the Nether, ±16 down finds lava lakes and reports them as build surfaces. **Liquid/lava block rejection is blocking.**
- BFS "flat = adjacent Y diff ≤ 1" is fine for connectivity. A5's yRange check is the right global flatness guard — it must run **before** A2's compactness scoring (cheaper to reject early).
- Centroid of the best component is a reasonable origin for compact components. For thin dumbbell shapes (low compactness), centroid can land on the narrow waist. Acceptable for Sprint 9; flag as deferred.
- `FlatAreaFoundEvent` does not include `yRange` or `compactness`. These observability fields would allow the HTN to evaluate candidate quality. Deferred.

**Blocking:** Liquid block rejection in heightMap construction.  
**Deferred:** Distance-from-bot scoring; largest-inscribed-square centroid; biome-aware scan windows; FlatAreaFoundEvent yRange/compactness fields.

---

## Seat 3 — Correctness Chair

**Confidence: 81%**

**Findings:**
- Vec3 import must be the **first commit** of the sprint. Widening the scan (A1) before fixing Vec3 increases the area touched by a throwing code path.
- The BFS over a r=64 scan is O(16K columns × 26Y = ~430K block lookups) synchronously. Mineflayer's `blockAt` is not free. **Async yielding every N columns is blocking** to keep the adapter event loop responsive.
- `AgentBackgroundService` must explicitly handle `FlatAreaFoundEvent` with area < MinUsableFlatArea — a structured info log, not silent discard.
- `HtnTaskLibrary.DecomposeBuild` should use auto-origin from facts when available; but `(0,0,0)` origin with missing auto-origin is ambiguous (genuine world-origin vs. unset). Log a warning if no auto-origin is found.
- Three-fact write for auto-origin (x, y, z) is not atomic. For Sprint 9, accept eventual consistency; document it.

**Blocking:** Async BFS yield; `FlatAreaFoundEvent` < min area log; `BuildFactKeys` constants.  
**Deferred:** DecomposeBuild explicit exception for truly missing origin (Sprint 10 after callsite audit); fact store atomicity.

---

## Seat 4 — Experience Chair

**Confidence: 70%**

**Findings:**
- Auto-origin selection has zero observability without a structured log line including area, coordinates, and score. **The log line is non-negotiable** for post-deploy diagnosis.
- S7-D3 WorldModel summary mode is useful only if the UI defaults to it — wire the UI change in the same sprint, otherwise the endpoint flag is academic.
- No way for the operator to manually clear an auto-origin once set. Acceptable for Sprint 9 (just set a new one via `/api/agent/origin`). Track as a UX gap.

**Blocking (light):** Structured log line in A3 handler.  
**Deferred:** UI default for `?detail=false`; "clear auto-origin" operator command; score breakdown in dashboard.

---

## Seat 5 — Skeptic + Synthesizer

**Confidence: 65%**

**Challenges:**
- **Raise `minFlatArea` default from 9 to 25.** The council-standard 5×5 is the smallest structure the HTN builds. 9 (3×3) is too small and a footgun for build goals.
- A2 (compactness scoring) has zero telemetry backing. Deferring to Sprint 10 after candidate data is collected would be cleaner. However, since A5 (slope) and A1 (wider window) are in the same file, adding A2 in the same commit is low friction — shipping all three together is acceptable if weights are named constants (Architecture Chair).
- ±16 below is not dimension-aware (lava floor in Nether, void in End). For Sprint 9, liquid rejection partially mitigates this; full fix is Sprint 10.
- CI is green but `findFlatArea` is not exercised by any C# test. A Node smoke test is needed for true coverage.

**Final verdict:** CONDITIONAL PASS with 6 blocking items addressed. A2 may ship if weights are named constants.

---

## Consolidated Blocking Findings

| # | Finding | Seat |
|---|---------|------|
| B1 | Vec3 import (or plain-object fix) — first commit | Correctness |
| B2 | Liquid/lava block rejection in heightMap | Minecraft World |
| B3 | Async BFS yield every ~200 columns | Correctness |
| B4 | `minFlatArea` default raised to 25 | Skeptic |
| B5 | `BuildFactKeys` named constants for shared fact keys | Architecture |
| B6 | A3 handler: log events with area < min; don't silently discard | Correctness+Experience |
| B7 | A2 scoring weights must be a named object literal | Architecture |

---

## Testable Acceptance Criteria

1. `findFlatArea` calls `bot.blockAt({x, y, z})` (plain object) — no Vec3 import — and does not throw ReferenceError.
2. Any candidate column overlapping a liquid block (`water`, `lava`, `flowing_water`, `flowing_lava`) is excluded from `heightMap`.
3. `findFlatArea` yields to the event loop at least once per 200 columns.
4. Default `minFlatArea` is 25 in both JS adapter (`FLAT_AREA_MIN_SIZE`) and C# constant (`MinUsableFlatArea`).
5. `BuildFactKeys.AutoOriginX/Y/Z/AutoBlueprintId` are referenced in both `AgentBackgroundService` and `HtnTaskLibrary` with no duplicated string literals.
6. A3: `AgentBackgroundService` handles `FlatAreaFoundEvent` with `Area >= 25`, calls `SetBuildOrigin(BuildFactKeys.AutoBlueprintId, ...)`, and logs `[findFlatArea] auto-set build origin (X,Y,Z) area=N`.
7. A3: `FlatAreaFoundEvent` with `Area < 25` triggers an info log and does not write any facts.
8. A2: scoring formula uses `FLAT_SCORE_WEIGHTS.area`, `.compactness`, `.flatness` — no inline magic numbers.
9. A5: any component with `yRange > maxSlope` is filtered before scoring; A5 check runs before A2 scoring.
10. `WorldStateProjectorTests`: two new tests for `Apply_FlatAreaFoundEvent` verify fact storage and no structured-state change.
11. `/api/agent/worldmodel?detail=false` returns payload without `RecentObservations`; `?detail=true` or no param returns full payload.

---

## Open Questions

1. Should A2 ship in Sprint 9 or wait for telemetry? (Skeptic: prefer defer; Architecture: acceptable if weights named — DECISION: ship with named weights)
2. What happens when `findFlatArea` finds zero candidates above `minFlatArea`? (emit with area=0 — already implemented)
3. Is the three-fact auto-origin write atomic? (No — accept eventual consistency for Sprint 9, document)
4. Should the vertical scan window be dimension-aware now or follow-up? (Follow-up — liquid check partially mitigates)
5. Is there a Node test runner in CI today? (No — Node smoke harness is Sprint 10)
