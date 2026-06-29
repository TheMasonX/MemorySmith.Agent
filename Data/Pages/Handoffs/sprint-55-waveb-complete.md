# Sprint 55 Wave B Handoff — Complete

**Date:** 2026-06-29
**Branch:** `dev/round-3`
**Commit:** `6c0d8f6`
**Build baseline:** 746 tests, 0 warnings
**Handoff author:** SteveBot → next agent

---

## 🎯 What Shipped

### Wave B: Observe→Evaluate Feedback Loop ✅

| Capability | Status |
|---|---|
| WorldStateDiff (inventory/position/health/entity) | ✅ |
| ComputeWorldStateDiff in dispatch loop | ✅ |
| ILlmEvaluator with WorldStateDiff parameter | ✅ |
| LlmEvaluatorImpl goal-type-agnostic context | ✅ |
| Action lifecycle telemetry (started/progress/completed/failed) | ✅ TSK-0165 |
| QueryBlocksTool (single pos + bounding box) | ✅ TSK-0230 |
| QueryEntitiesTool (radius + type filter) | ✅ TSK-0230 |
| EntityObservedEvent (passive periodic scan) | ✅ code present, physicsTick disabled |
| MC version range support (1.16.5–1.21.6+) | ✅ documented |

### Bug Fixes

| Fix | Description |
|---|---|
| NRE in evaluator | `_currentGoal` captured before `await` gap (concurrent mutation by DeathEvent/goal cancel) |
| Chat broken | Raw-packet hack reverted — `bot.chat()` works for both 1.16.5 and 1.21.x |
| Version logging | MC version + Mineflayer version + adapter version logged on spawn |
| ESM `require` | `createRequire` shim added for reading package.json in ES module context |

---

## ⚠️ Known Issues (From Live Logs)

### 1. PlaceBlock schema validation failure 🔴 P0

```
[action] place FAIL (0ms): Schema validation failed for 'place':
Unexpected property 'block' is not declared in the tool schema.
```

**Root cause:** The planner emits PlaceBlock actions with a `block` property, but `PlaceBlockTool.InputSchema` only declares `material`, `x`, `y`, `z`, `facing`. The `block` property is rejected by `ToolDispatcher` schema validation.

**Fix:** Either add `block` as an accepted property in `PlaceBlockTool.InputSchema`, or fix the planner to emit `material` instead of `block`.

**Impact:** PlaceBlock actions fail silently (fire-and-forget dispatches but schema rejects them). All 4 place actions in a 2×2 dirt placement fail.

### 2. Build intent missing fields 🟡 P1

```
[intent] build could not create goal request — insufficient fields.
item=dirt, blueprint=null, count=4, x=null, y=null, z=null
```

**Root cause:** When the LLM interprets "place 2×2 dirt blocks", it generates a build intent with `item=dirt, count=4` but no blueprint or coordinates. `IntentManager` cannot create a `GoalRequest` from this.

**Fix:** Enhance the LLM system prompt to request coordinates for build intents, or add a fallback that auto-creates a simple 4-block placement plan at the bot's current position.

### 3. Entity observation disabled 🔵 P2

The `physicsTick` hook is commented out pending a test to confirm it doesn't interfere with chat. To re-enable: uncomment line ~385 in `MineflayerAdapter/index.js`:

```js
bot.on('physicsTick', scanNearbyEntities);
```

---

## 📁 Key Files Changed

| File | Change |
|---|---|
| `WebUI.Blazor/AgentBackgroundService.cs` | NRE fix (capture goal before await) + entity/block event handlers |
| `MineflayerAdapter/index.js` | Chat restore + createRequire shim + version logging + Wave B actions |
| `Agent.Core/Events/WorldEvents.cs` | +ActionFailedEvent, +ActionCompletedEvent, +BlocksQueriedEvent, +EntitiesQueriedEvent, +EntityObservedEvent, +QueriedBlock, +ObservedEntity |
| `Agent.Core/Models/WorldStateDiff.cs` | New — structured expected-vs-actual comparison |
| `Agent.Tools/Tools/QueryBlocksTool.cs` | New — block query tool |
| `Agent.Tools/Tools/QueryEntitiesTool.cs` | New — entity query tool |
| `Agent.World.Minecraft/WebSocketBridge.cs` | Parse all new event types |
| `MineflayerAdapter/config.js` | +ENTITY_SCAN_RADIUS, +ENTITY_SCAN_COOLDOWN_MS |

---

## 🔜 Next Tasks (Priority Order)

### P0 — Fix PlaceBlock schema validation
- Add `block` to `PlaceBlockTool.InputSchema` properties OR fix planner to emit `material`
- File: `Agent.Tools/Tools/PlaceBlockTool.cs`

### P1 — Fix build intent for simple placements
- Handle `build` intent with count but no blueprint/coords
- File: `Agent.Personality/IntentManager.cs` or LLM system prompt

### P2 — Re-enable entity observation
- Uncomment `physicsTick` hook, test with chat, verify no interference
- File: `MineflayerAdapter/index.js` line ~385

### P3 — Fix MC port auto-detection in Start-Mineflayer.ps1
- Current: hardcoded port, changes every LAN session
- Fix: auto-parse from Minecraft "Local game hosted on port XXXXX" message or accept as parameter

---

## 🧪 Validation Gates

- `dotnet test` → 746 tests pass
- `dotnet build` → 0 warnings
- `pwsh Scripts/Test-TaskRecords.ps1` → pass
- Chat: verify `bot.chat()` works on both 1.16.5 and 1.21.x
- PlaceBlock: schema must accept `block` property
