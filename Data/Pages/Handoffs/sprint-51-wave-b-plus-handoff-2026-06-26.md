# Sprint 51 Wave B+ — Handoff: PlaceBlock Observability + Build Speed

**Date:** 2026-06-26
**Branch:** `sprint-35-llm-first`
**Build:** 0w/0e, **Tests:** 742 passed, 0 failed

---

## What Was Delivered (Wave B+)

### PlaceBlock Logging — Full Visibility
**`AgentBackgroundService.cs`**
- Dispatch log now shows `PlaceBlock cobblestone → (65,4,127) (0ms) bot=(65,5,127)` — material + target coordinates + bot position in one line.
- BlockPlacedEvent confirmation elevated from `Debug` → `Information`: `[place] CONFIRMED: cobblestone placed @ (65,4,127)`.
- Per-cycle progress summary: `[build] cycle complete: 5 blocks placed | remaining: 198 | pos=(...)`.
- New field `_blocksPlacedThisCycle` tracks confirmed placements per dispatch cycle.

---

## Priority Backlog for Next Wave

### 🔴 Critical — Build Speed

| Task | Priority | Description |
|:-----|:--------:|:------------|
| **TSK-0121** | **Critical** | **Remove MoveTo-to-origin from replans.** Every build cycle starts with `MoveTo OK (0ms)` to the build origin — even though the bot is already at the build site. At 1 cycle per 2 seconds × 215 blocks = ~7 minutes for a small house. The `MoveTo` should only appear in the FIRST plan; replans should not re-add it. |

### 🟡 High — Observability & UX

| Task | Priority | Description |
|:-----|:--------:|:------------|
| **TSK-0169** | High | Chat context dashboard — show LLM prompt/response pairs in the dashboard UI |
| **TSK-0170** | High | Dashboard UI improvements — cleaner layout, better error visibility |
| **TSK-0005** | Medium | Implement SpatialAnalyzer in Agent.Vision for terrain assessment |
| **TSK-0013** | Medium | Add ListBlocks/ListItems tool for in-game block discovery |

### 🔵 Deferred from Wave A

| Task | Priority | Description |
|:-----|:--------:|:------------|
| TSK-0134 | High | DI startup failure logging + health check endpoints |
| TSK-0133 | High | Fix parameter preservation on replan (remaining count) |
| TSK-0132 | High | Fix page search Score=0.0 under-ranking |
| TSK-0137 | Medium | Fix consecutive failure guard reset on partial progress |
| TSK-0144 | Critical | Enforce package vetting policy in CI |
| TSK-0145 | High | Run Verify-AboutDeps.ps1 in CI |

---

## Current State

```
Build:   0 warnings, 0 errors
Tests:   742 passing, 0 failing
Branch:  sprint-35-llm-first (pushed)
Version: v0.51.1 — Sprint 51 Wave B+
```

### What's Working
- ✅ Dashboard API accessible from localhost (API key bypass)
- ✅ Creative inventory fallback in adapter (no `/give` needed)
- ✅ Bot-position skip in adapter (no "no reference block" errors)
- ✅ PlaceBlock logging shows material + target + bot position
- ✅ Block placement confirmation logged at Info level
- ✅ HTTP request log spam suppressed
- ✅ LLM context dump to `logs/llm-context/` with rolling + archive
- ✅ Chat I/O logged to `logs/chat/`
- ✅ Version printed at startup

### What's Still Slow
- 🔴 MoveTo to origin runs before every block placement cycle (2s per block)
- Inventory shows `oak_planks: 0/70` — other materials besides cobblestone need gathering/giving

---

## Key Files

| File | Recent Changes |
|:-----|:---------------|
| `AgentBackgroundService.cs` | PlaceBlock material logging, BlockPlacedEvent→Info, cycle progress summary, IsProgressSignalTool narrowed, direct gather recovery |
| `MineflayerAdapter/index.js` | Creative inventory fallback, bot-position skip with warning |
| `Program.cs` | Version logging, HTTP request log suppression, LlmContextLogger DI |
| `ApiKeyMiddleware.cs` | Localhost bypass when no API key configured |
| `HtnTaskLibrary.cs` | TSK-0123 bot-position skip in creative path |
| `LlmContextLogger.cs` | **New** — raw LLM request/response to `logs/llm-context/` with rolling + gzip archive |
| `Scripts/Verify-AboutDeps.ps1` | **New** — validates about.html against csproj packages |
