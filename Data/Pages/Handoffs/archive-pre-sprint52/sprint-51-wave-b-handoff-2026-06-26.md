# Sprint 51 Wave B — Runtime Bug Fixes Handoff

**Date:** 2026-06-26
**Branch:** `sprint-35-llm-first`
**Agent:** SteveBot (MemorySmith.Agent)
**Status:** ✅ Wave B Complete
**Build:** 0w/0e, **Tests:** 742 passed, 0 failed

---

## Wave B Summary

Wave B focused on critical runtime bugs discovered during live agent testing rather than the originally planned CI enforcement tasks. The agent was observed in an infinite replan loop where creative mode builds failed because PlaceBlock actions required inventory items that were never provisioned.

### Root Cause Analysis

The agent ran in creative mode and attempted to build a house. The C# creative path (`HtnTaskLibrary.DecomposeBuild`) short-circuits inventory checks for creative mode, but the adapter's `PlaceBlock` handler still checks `bot.inventory.items()` — which was always empty. The creative provisioning (`/give @p <item>`) was unreliable because:

1. It requires OP permissions on the Minecraft server
2. Even if `/give` worked, `ItemCollectedEvent` doesn't fire for command-given items
3. Chat responses from `/give` are filtered by `SYSTEM_MESSAGE_PATTERNS`

**Secondary bug:** The consecutive failure counter (`_consecutiveFailures`) was reset on every cycle by `MoveTo` being in `IsProgressSignalTool`. This prevented the max-failure guard from ever triggering (counter stayed at 1/3), causing an infinite replan loop.

---

## Fixes Delivered (3 changes)

### Fix 1 — Creative Inventory Fallback in Adapter
**File:** `MineflayerAdapter/index.js`
**Change:** In the `place` action handler, when the bot is in creative mode (`bot.game.gameMode === 1`) and the target material is not in inventory, use `bot.creative.setInventorySlot()` to select the item from creative inventory before placing.
**Impact:** Creative mode builds now work without requiring `/give` commands or OP permissions.

### Fix 2 — Remove Non-Progress Tools from Failure Reset
**File:** `WebUI.Blazor/AgentBackgroundService.cs`
**Change:** `IsProgressSignalTool` no longer includes `MoveTo`, `FindFlatArea`, and `Wander`. Only `MineBlock`, `PlaceBlock`, `CraftItem`, and `SmeltItem` reset the consecutive failure counter — these are tools that produce inventory changes.
**Impact:** The `_consecutiveFailures` counter now correctly reaches `maxFailures` (3) on repeated PlaceBlock failures, triggering goal abandonment instead of infinite replan.

### Fix 3 — Direct Gather Recovery for Missing Materials
**File:** `WebUI.Blazor/AgentBackgroundService.cs`
**Change:** Added `TryExtractMissingMaterial` helper that parses "not in inventory" game errors to extract the missing block name. When detected, `TryRecoverFromGameErrorAsync` creates a `GatherGoalRequest` for the missing material directly, bypassing the LLM round-trip.
**Impact:** When a PlaceBlock fails because an item is missing, the agent immediately switches to gathering that item instead of waiting for LLM recovery.

---

## Infrastructure (Wave B Partial)

### Verify-AboutDeps.ps1 — Fixed & Working
**File:** `Scripts/Verify-AboutDeps.ps1`
**Changes:**
- Fixed path resolution (was joining `$PSScriptRoot/../` incorrectly)
- Fixed HTML regex to use `(?s)` singleline flag for multiline matching
- Fixed wildcard pattern replacement (`'\*'` not `'\\*'`)
**Status:** Script now correctly verifies all 12 csproj packages against 16 about.html entries. Clean output: `✅ All dependencies in sync`.

### WebUI.Blazor.csproj — Reminder Comment
**File:** `WebUI.Blazor/WebUI.Blazor.csproj`
**Change:** Added P-2 policy reminder: "Adding/removing a PackageReference requires updating about.html in the same commit."

---

## Remaining for Future Waves

### Deferred from Wave A
| Task | Priority | Description |
|:-----|:--------:|:------------|
| TSK-0134 | High | DI startup failure logging + health check endpoints |
| TSK-0133 | High | Fix parameter preservation on replan |
| TSK-0132 | High | Fix page search Score=0.0 under-ranking |
| TSK-0137 | Medium | Fix consecutive failure guard reset on partial progress |

### CI Enforcement (originally Wave B)
| Task | Priority | Description |
|:-----|:--------:|:------------|
| TSK-0144 | Critical | Enforce package vetting policy in CI (`dotnet list package --vulnerable` must fail build) |
| TSK-0145 | High | Run `Verify-AboutDeps.ps1` in CI |

---

## Key Files Changed

| File | Change |
|:-----|:-------|
| `MineflayerAdapter/index.js` | Creative inventory fallback for PlaceBlock |
| `WebUI.Blazor/AgentBackgroundService.cs` | IsProgressSignalTool narrowed; TryExtractMissingMaterial + direct gather recovery |
| `WebUI.Blazor/WebUI.Blazor.csproj` | P-2 reminder comment |
| `Scripts/Verify-AboutDeps.ps1` | Created + fixed (HTML parsing, path resolution, wildcard matching) |

---

## Validation

```
Build: 0 warnings, 0 errors
Tests: 742 passed, 0 failed, 0 skipped
dotnet list package --vulnerable: 0 results
dotnet list package --deprecated: 0 results
Scripts/Verify-AboutDeps.ps1: ✅ All in sync
```

---

## Running Again After Fixes

The fixes require no configuration changes. To restart the agent:

```bash
# Terminal 1: Mineflayer adapter
cd MineflayerAdapter && npm start

# Terminal 2: C# agent host
dotnet run --project WebUI.Blazor
```

With Fix 1, creative mode builds should now work without `/give` commands — the adapter will pull blocks from creative inventory automatically when placing. With Fix 2, if a build does fail, the agent will correctly detect repeated failures and abandon the goal (rather than infinite-looping). With Fix 3, missing-material errors trigger immediate gathering recovery.

---

**Commit message:** `Sprint 51 Wave B: Fix creative build infinite replan + creative inventory fallback`
