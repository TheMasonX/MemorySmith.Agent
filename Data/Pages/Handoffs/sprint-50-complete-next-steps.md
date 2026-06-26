# Sprint 50 Complete — Handoff & Next Steps

**Date:** 2026-06-26
**Branch:** `sprint-35-llm-first` (Wave A: `153fbd6`, Wave B: `3da01c1`, Wave C: latest)
**Author:** SteveBot
**Tests:** 731 passing, 0 failing (0 warnings, 0 errors)

---

## What Was Delivered in Sprint 50

### Wave A — Dashboard Usability + Build Placement Fixes
*Commit `153fbd6`*

| Task | Title | Priority | Change |
|:----:|:------|:--------:|:-------|
| TSK-0121 | Rehome-to-origin removed | Critical | Removed redundant `MoveTo(origin)` before every block placement |
| TSK-0122 | Terrain clearance before PlaceBlock | Critical | ~60 replaceable blocks cleared before placement (grass, flowers, mushrooms, etc.) |
| TSK-0123 | Skip PlaceBlock at bot's position | Critical | Self-position placement blocked; waits for replan |
| — | Dashboard Overview UI | — | Persistent live log strip, error/warning badges, position trail, current action display, auto-scroll toggle |

**Files:** `WebUI.Blazor/wwwroot/index.html`, `HtnTaskLibrary.cs`, `Program.cs`, `AgentBackgroundService.cs`

### Wave C — Dashboard Landing Page, Navigation & Status Panels
*Commit (latest on `sprint-35-llm-first`)*

| Area | Change |
|:-----|:-------|
| **Landing page** | Root `/` now redirects to `/index.html` (dashboard) instead of showing a plain text message |
| **Header nav** | Added Dashboard/About nav links in header; version badge (`v0.50.1`) shown next to title |
| **SignalR status** | New connection status indicators in Status panel: live SignalR connected/disconnected state, polling fallback indicator |
| **Uptime counter** | Real-time uptime display (h:m:s) since page load |
| **Uncertainty display** | World model uncertainty score shown as percentage in metrics row |
| **Enhanced metrics** | Metrics row now includes: Queue, Failures, Errors, Warnings, Uncertainty — each with tooltips |
| **Inventory badge** | Shows "N types, M total" count above inventory grid |
| **About page** | Updated to v0.50.1 with Sprint 50 Wave C feature list; Phase 4 renamed to "Dashboard & Observability" |
| **Version bump** | v0.50.0 → v0.50.1 (wave = minor version) |
| **Docs update** | README.md, roadmap.md, handoff document all updated with Sprint 50 Wave C |

**Files changed:** `Program.cs`, `wwwroot/index.html`, `wwwroot/about.html`, `README.md`, `Data/Pages/roadmap.md`, `Data/Pages/Handoffs/sprint-50-complete-next-steps.md`

### Wave B — BuildOrigin Migration + Creative Cleanup + Council Review
*Commit `3da01c1`*

| Task | Title | Priority | Verdict |
|:----:|:------|:--------:|:-------:|
| TSK-0107 | BuildOrigin sentinel elimination | High | ✅ **Done** — `DecomposeBuild` accepts `BuildOrigin?` instead of raw `int originX/Y/Z`. The `(0,0,0)` sentinel is eliminated via `BuildOriginSource.Explicit`. 9 files changed. |
| TSK-0116 | Creative build dead code removal | P2 | ✅ **Done** — Removed `CreateCreativeBuildActions` from `HtnPlanner` (dead code since `PlannerRouter` prefers `BuildGoalDecomposer`). |
| TSK-0117 | Inventory reconciliation | P2 | ✅ **Verified Complete** — C# side already done (`ApplyCraftComplete`, `ApplySmeltComplete`). Adapter already fires `sendBotStatus()`. |
| TSK-0118 | Chat split-brain | P2 | ⏳ **Deferred** to Sprint 51 — Sprint 35 already fixed dangerous paths |
| TSK-0093 | Structured ParseItemSpec | Med | ⏳ **Deferred** — No consumer need since Sprint 43 |
| TSK-0096 | Mining double-counting | Med | ❌ **Won't Fix** — Documented tradeoff (over-count safer than under-count) |

**Council:** 7-seat adversarial review conducted. Report at `Data/Pages/Handoffs/sprint-50-waveb-council-buildorigin-creative.md`.

---

## Current Production State

```
Build:   0 warnings, 0 errors
Tests:   731 passing, 0 failing, 0 skipped
Branch:  sprint-35-llm-first (pushed to origin)
Version: v0.50.1  Sprint 50 — Dashboard Wave C: Landing Page, Navigation & Status Panels
```

### Active Agent Capabilities
- **Build**: Full build pipeline with origin resolution (Explicit/AutoScanned/PlayerPosition), terrain clearance, checkpoint resumption, creative mode
- **Craft/Smelt**: Full crafting chain with prerequisite gathering, smelting, tool ensures
- **Gather**: MineBlock with direct-mine and smeltable resolution via `CommonMinecraftBlocks`
- **Chat**: LLM-first interpretation with IntentDraft → IntentManager → GoalFactory pipeline
- **Dashboard**: Tabbed UI (Overview/Logs/Timeline), landing page, header navigation, real-time live log, error/warning badges, position trail, connection status, uptime counter, uncertainty display, enhanced metrics
- **Replan**: Graduated retry with stall detection, failure reason logging
- **Recovery**: Emergency stop (damage interrupt), action timeout sweep

---

## Priority Backlog for Sprint 51

Based on the council review, here are the actionable backlog items prioritized:

### Tier 1 — High Value, Well-Scoped

| Task | Priority | Estimate | Description |
|:----:|:--------:|:--------:|:------------|
| TSK-0082 | **High** | ~20 min | Extract shared `SmeltableMapping` class. Ore→ingot mapping duplicated in 2 places. Already identified in Sprint 44 — straightforward extraction. |
| TSK-0003 | **High** (InProgress) | ~1 hr | First end-to-end game test (GatherWood). Requires Minecraft server + Mineflayer. |

### Tier 2 — Architecture Consistency

| Task | Priority | Estimate | Description |
|:----:|:--------:|:--------:|:------------|
| TSK-0004 | **High** | ~15 min | Wire `MoveToTool` to read coordinates from `ActionData.Context` instead of `Arguments` |
| TSK-0118 | P2 | ~30 min | Chat split-brain cleanup: remove dead `ChatInterpreter` regex fields, retire `ChatInterpretation.GoalName` backward-compat field |
| TSK-0093 | Med | ~30 min | Structured `ParseItemSpec` result. Logging-only fix is sufficient (3 lines). |

### Tier 3 — Observability & Hardening

| Task | Priority | Estimate | Description |
|:----:|:--------:|:--------:|:------------|
| TSK-0014 | Med | ~20 min | Wire Serilog SQLite sink for agent runtime telemetry persistence |
| TSK-0008 | Med | ~30 min | Add Blazor status panel with SignalR real-time push (currently static HTML dashboard) |
| TSK-0005 | Med | ~45 min | Implement `SpatialAnalyzer.cs` in Agent.Vision |

### Tier 4 — Long-Lead / Experimental

| Task | Priority | Estimate | Description |
|:----:|:--------:|:--------:|:------------|
| TSK-0006 | Med | ~1 hr | Add Microsoft.Extensions.AI + Ollama LLM provider fallback chain |
| TSK-0012 | **High** (Open) | ~2 hr | Deploy a Minecraft-specific MemorySmith wiki for item/blueprint lookup |
| TSK-0013 | Med (Open) | ~30 min | Add `ListBlocks`/`ListItems` tool for in-game block discovery |

### Closed / Won't Fix

| Task | Reason |
|:----:|:-------|
| TSK-0096 | Documented tradeoff. Over-count (ApplyBlockMined + ItemCollected) is safer than under-count. Periodic GetStatus reconciles drift. |
| TSK-0110, TSK-0112, TSK-0113, TSK-0114, TSK-0115 | Already completed in Sprint 49 Waves C–E |
| TSK-0100 through TSK-0106 | Already completed in Sprint 46–48 |

---

## Recommended Wave C (Sprint 50 Continue) or Sprint 51 Wave A

If continuing in this session, the highest-ROI sequence is:

1. **TSK-0082** — `SmeltableMapping` extraction. Small, independent, high confidence
2. **TSK-0004** — `MoveToTool` context wiring. Small, fixes a known inconsistency
3. **TSK-0118** — Chat split-brain cleanup. Sprint 35 already did the hard part; remaining janitorial work is cheap
4. **TSK-0014** — Serilog SQLite sink. Improves runtime debugging

Each task is independent and can be done in any order.

---

## Key Files Reference

| File | Purpose |
|:-----|:--------|
| `Agent.Planning/HtnTaskLibrary.cs` | Core HTN decomposition — `DecomposeBuild`, `DecomposeCraftItem`, `DecomposeSmeltItem`, crafting chain |
| `Agent.Planning/HtnPlanner.cs` | Fallback planner (PlannerRouter prefers decomposers first) |
| `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` | Decomposer for BuildGoal — origin resolution, requireOrigin logic |
| `Agent.Planning/IntentManager.cs` | IntentDraft → GoalRequest mapping |
| `Agent.Planning/LlmChatInterpreter.cs` | LLM-first chat interpretation with pattern fallback |
| `Agent.Core/WorldStateProjector.cs` | Event-sourced state projection — ApplyBlockMined, ApplyItemCollected, ApplyCraftComplete, ApplySmeltComplete |
| `Agent.Core/CommonMinecraftBlocks.cs` | Shared block/item mappings — `BlockToItemDrop`, `SelfDroppingBlocks` |
| `Agent.Core/Models/ActionQueue.cs` | Lock-protected action queue |
| `Agent.World.Minecraft/WebSocketBridge.cs` | WebSocket transport with retry loop |
| `WebUI.Blazor/wwwroot/index.html` | Dashboard SPA (static HTML/JS with SignalR) |
| `MineflayerAdapter/index.js` | Node.js Minecraft bot adapter |
| `Data/Memories/Core/` | Active structured project wiki records |
| `Data/Pages/Handoffs/` | Sprint handoff documents |
| `Data/Tasks/` | Task tracking (`.md` + `.json` per task) |
