# MSR-001: Expose Page Search Score in Search Results

**Date:** 2026-07-11
**Requestor:** SteveBot (MemorySmith.Agent)
**Priority:** High
**Status:** Draft

## Problem

Pages (markdown wiki pages) always return `Score=null` in the `/api/search` endpoint. In the UnifiedSearchResult model (SearchController.cs line 53), page results pass `null` as the score while memory records pass their computed RRF hybrid score. Downstream consumers (MemorySmith.Agent's `RestMemoryGateway`) convert `null` to `0.0`, causing pages to always sort last with zero confidence.

## Root Cause Chain

1. `PageService.SearchAsync` computes a lexical score via `Score()` (line 578) and uses it for internal `OrderByDescending` — but the score is discarded when converting results via `ToSummary()`.
2. `PageSummary` record (line 216) has no `Score` field — the computed score is never stored.
3. `SearchController` passes `null` for page scores in `UnifiedSearchResult` (line 53).
4. Agent's `RestMemoryGateway` converts `null` → `0.0`.
5. `LocalKnowledgeResolver` multiplies base confidence (0.60) by `Score` → 0 confidence for all pages.

## Fix Required (MemorySmith Base Repo)

### File 1: `MemorySmith.App/Services/PageService.cs`

**a) Add `Score` field to `PageSummary` record (line ~216):**
```csharp
public sealed record PageSummary(
    string Slug, string Title, string Snippet,
    double Score,                         // ← NEW: normalized page score
    DateTime LastUpdatedUtc,
    string MinimumRole = PageAccessLevels.Anonymous);
```

**b) Normalize the internal `Score()` return value (line ~578):**
The current `Score()` method returns integers in the range 0–20+. These need to be normalized to a ~0–1 range comparable with the RRF memory scores. Two options:
- **Divide by max:** Add a `maxScore` tracking parameter and divide by the highest observed score.
- **Log-scale:** Use `Math.Log10(score + 1) / Math.Log10(maxScore + 1)`.

Simplest approach: normalize by dividing by a reasonable maximum (e.g., 20.0) or use `Math.Tanh(score / 10.0)` for a smooth 0–1 curve.

**c) Pass score through `ToSummary()` (line ~527):**
```csharp
.ToSummary(score: computedScore)
```

### File 2: `MemorySmith.App/Controllers/SearchController.cs`

**Replace hardcoded `null` at line ~53:**
```csharp
// Before:
page.Score ?? null
// After:
page.Score    // double, already in 0–1 range
```

### File 3: `MemorySmith.Tests/ScoringTests.cs`

Add tests that verify:
- Page search returns scores > 0 for matching queries
- Scores are in the normalized 0–1 range
- Sort order is correct (higher relevance = higher score)

## Agent-Side Workaround (Applied for Sprint 60)

As a temporary measure until the base repo fix is deployed, `RestMemoryGateway.cs` in MemorySmith.Agent sets a minimum score of 0.05 for page results (up from 0.0). This ensures pages appear in search results with a minimal confidence floor.

## Acceptance Criteria

1. `/api/search?q=test` returns page results with `score > 0.0` for matching pages.
2. Page scores are in the 0–1 normalized range, comparable with memory RRF scores.
3. Pages with exact title matches score higher than partial content matches.
4. All existing tests pass.
5. Agent's `SearchMemoryTool` returns page results with non-zero score.

## References

- `MemorySmith.App/Services/PageService.cs:216` — `PageSummary` record
- `MemorySmith.App/Services/PageService.cs:388-402` — `SearchAsync` discarding score
- `MemorySmith.App/Services/PageService.cs:527` — `ToSummary()` missing score param
- `MemorySmith.App/Services/PageService.cs:578-609` — `Score()` method
- `MemorySmith.App/Controllers/SearchController.cs:49-56` — `null` hardcoded
- `Agent.Memory/RestMemoryGateway.cs:49` — `null` → `0.0` conversion
- `Data/Tasks/tsk-3078-fix-scorer-weights.json` — Related (memory scorer, already fixed)
