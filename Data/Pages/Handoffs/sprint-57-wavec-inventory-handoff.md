# Sprint 57 — Wave C Handoff: Inventory Fixes + Next Wave

**Date:** 2026-07-01  
**Agent:** SteveBot  
**Status:** ✅ Wave C complete — handing off to next agent

## Completed This Wave

### TSK-0301 ✅ Inventory SSOT fixes (3 root causes)
- Adapter now sends `sendBotStatus()` on spawn — full inventory arrives immediately
- Stale guard rewritten: two branches (enqueue + wait, or just wait), doesn't fall through to PlanAsync
- Plan generation now blocked until `IsInventoryStale` clears via StatusEvent

### TSK-0296 ✅ PlaceBlock "goal was changed" fix
- `handleStop()` suppresses `pathfinder.setGoal(null)` during place/move navigation
- `_dispatchingAction` tracked in `drainQueue`

### TSK-0286 ✅ DeniedCommands config-authoritative
- Removed inline default from `SafetyOptions` — config now fully overrides
- Safety config logged at startup

### Earlier Waves (previously completed)
- TSK-0289/0290/0291/0294/0295 — ExecutionContext, policy objects, action registry, capabilities
- TSK-0297 through TSK-0300 — Backlog tasks from chat log review

## Backlog for Next Wave

### P1 Tasks (next agent should tackle first)

| Task | Priority | Summary |
|------|----------|---------|
| TSK-0303 | **P1** | Add known-commands section to LLM prompt with correct syntax and deny-list filtering |

**Details for TSK-0303:** The LLM frequently misuses Minecraft commands. The system prompt in `LlmChatInterpreter.BuildSystemPrompt` needs a curated list:

```
KNOWN COMMANDS (use exact syntax):
  /tp <target> <destination>  — e.g., /tp Leo TheMasonX23 (teleport Leo to player)
  /summon <entity> <x> <y> <z>  — e.g., /summon minecraft:lightning_bolt 131 5 189
  /give <player> <item> [count]  — e.g., /give Leo oak_log 64
  /locate structure <type>  — e.g., /locate structure village
  /fill <x1> <y1> <z1> <x2> <y2> <z2> <block>
  /execute as <target> at @s run <command>
  /time set <value>  — day, night, noon, midnight
  /weather <clear|rain|thunder> [duration]
  /gamemode <mode> [player]  — survival, creative, adventure, spectator
  /difficulty <peaceful|easy|normal|hard>

IMPORTANT:
- /tp with TWO args teleports target TO destination
- /summon uses WORLD coordinates (not relative to bot)
- /give uses item IDs like oak_log, stone, dirt (not "wood" or "stone block")
- Filter this list against DeniedCommands before including in prompt
```

Search web for Minecraft wiki command reference if syntax details are uncertain. The commands list should be built from `DeniedCommands` — commands on the deny list should NOT appear in the prompt (don't tell the LLM about commands it can't use).

### P2 Tasks

| Task | Priority | Summary |
|------|----------|---------|
| TSK-0304 | P2 | Add queryable block/item registry with fuzzy matching and aliases |
| TSK-0302 | P2 | Refactor inventory system (codesmell — see details below) |
| TSK-0297 | P2 | Fix LLM entity-targeting confusion (/summon targets bot) |
| TSK-0298 | P2 | Chat rate limiter too aggressive |
| TSK-0299 | P2 | Player coordinates missing from LLM chat context |

### Inventory Codesmell (TSK-0302)

The inventory system has 7 codesmells that make it fragile. These should be addressed in a dedicated refactoring sprint (aligned with council Sprint 59+ extraction):

1. **No SSOT** — 6+ event paths update inventory
2. **IsInventoryStale is fragile boolean** — checked in 5+ places, cleared in 1
3. **StatusEvent overwrites** — no merge, late event can have fewer items
4. **No periodic sync** — relies entirely on explicit GetStatus
5. **Stale guard is spin-wait hack** — blocks with 50ms delays
6. **`/give` on LAN silently fails** — needs `bot.creative.setInventorySlot()` API
7. **Immutable record copies** — can drift between StateManagerImpl and ABS

### Deferred (extraction program)

| Task | Reason |
|------|--------|
| TSK-0292 (decompose ABS) | Requires Sprint 59+ extraction |
| TSK-0293 (remove legacy fallbacks) | Requires extraction complete |

## Files Changed This Wave

| File | Change |
|------|--------|
| `MineflayerAdapter/index.js` | `sendBotStatus()` on spawn; `_dispatchingAction` tracking; `handleStop()` pathfinder suppression |
| `WebUI.Blazor/AgentBackgroundService.cs` | Stale guard rewritten (two-branch wait); DeniedCommands property |
| `WebUI.Blazor/Options/SafetyOptions.cs` | Removed inline default (config-authoritative) |
| `WebUI.Blazor/Program.cs` | Safety config startup logging |
| `Data/Tasks/tsk-0296 through tsk-0304` | 9 new tasks created |

## Build & Test Evidence

```
dotnet build → Build succeeded (0 errors, 0 warnings)
dotnet test  → 808 passed, 0 failed
node adapter → Module loads OK
```
