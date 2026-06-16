# Agent Handoff — MemorySmith.Agent

**For:** Next agent session continuing MemorySmith.Agent development  
**From:** Session 2026-06-16 (fifth session of the day)  
**Repo:** https://github.com/TheMasonX/MemorySmith.Agent  
**Last green CI commit:** `8e179d241d088ba88c2d2af0824e10b9b2f68b53` (Phase 4a)  
**Phase 5 commits:** pushed, CI pending (one risk: transitive csproj ref in Program.cs)

---

## Suggested Skills

- **improve-codebase-architecture** — apply before and after every significant change
- **handoff** — when completing a major milestone, produce a fresh handoff for the next agent
- **MemorySmith Council Review** — 6-seat structured review; run after every major set of changes; commit to `Data/Pages/council/`

---

## What Was Completed This Session (Phase 5)

### TSK-0012 — Chat Interpretation, Crafting Tools, Interactive Dashboard

**11 files changed/created**

#### Interface change:
- `IGoalFactory` now declares `CreateAsync` — makes the async goal creation path contractual.

#### New C# files:
| File | Purpose |
|------|---------|  
| `Agent.Planning/ChatInterpreter.cs` | Stateful singleton; interprets chat messages → `ChatInterpretation` via regex patterns + alias tables. Directed-at-bot via solo-player/name-prefix/conversation-window heuristics. |
| `Agent.Tools/Tools/ChatTool.cs` | Send in-game chat as bot (ActionProtocol.Chat) |
| `Agent.Tools/Tools/CraftItemTool.cs` | Craft items from inventory (ActionProtocol.Craft) |
| `Agent.Tools/Tools/FurnaceTool.cs` | Smelt items in nearby furnace (ActionProtocol.Smelt) |

#### Modified files:
- `Agent.Tools/ActionProtocol.cs` — added `Craft = "craft"` and `Smelt = "smelt"`
- `Agent.Planning/Interfaces/IGoalFactory.cs` — added `CreateAsync` to interface
- `WebUI.Blazor/Program.cs` — fixed DI wiring (IItemRegistry, IBlueprintRepository, GoalFactory), fixed plan endpoint to use `factory.CreateAsync`, added 5 new endpoints, registered 3 new tools
- `WebUI.Blazor/AgentBackgroundService.cs` — chat event routing, `CancelGoal()`, `SetBuildOrigin()`, `GetPendingActions()`, `ConsecutiveFailures` property, `botName` parameter, `GoalFactory?` parameter
- `MineflayerAdapter/index.js` — chat event now includes `onlinePlayers` count; added `craft` and `smelt` actions
- `WebUI.Blazor/wwwroot/index.html` — **new interactive dashboard** (was previously static about.html only)

#### Tests:
- `MemorySmith.Agent.Tests/ChatInterpreterTests.cs` — 28 tests (heuristics + all intent types)

---

## REST API (current as of Phase 5)

| Method | Path | Description |
|--------|------|----------|
| GET  | `/api/about` | Agent metadata + registered goals |
| GET  | `/api/agent/status` | Health, food, position, inventory, goal, queue count |
| GET  | `/api/goals` | List of registered goal names |
| POST | `/api/agent/plan` | Create and start a goal (async — supports GatherItem: and Build:) |
| DELETE | `/api/agent/goal` | Cancel current goal |
| GET  | `/api/agent/queue` | Current pending action queue |
| POST | `/api/agent/origin` | Set build origin facts for a blueprint |
| POST | `/api/agent/chat` | Send a chat message as the bot |
| GET  | `/api/blueprints` | List known blueprints |

---

## Architecture State (after Phase 5)

```
Agent.Core:          WorldState, ActionData, ActionPlan, IPlan, IGoal, IItemSpecGoal
                     IWorldAdapter, ITool, IToolCaller, IPlanner, IMemoryGateway
                     ItemSpec, WorldStateProjector

Agent.Construction:  Blueprint, PlacementBlock, BlueprintParser, BlueprintExecutor
                     IBlueprintRepository (interface), IBlueprintExecutor (interface)

Agent.Memory:        RestMemoryGateway, MemorySmithItemRegistry, MemorySmithBlueprintRepository

Agent.Planning:      HtnPlanner, HtnTaskLibrary, GoalFactory (implements IGoalFactory)
                     ChatInterpreter (NEW — stateful singleton)
                     Goals: GatherWoodGoal, SurviveNightGoal, GenericGatherGoal, BuildGoal
                     Interfaces: IGoalFactory (updated — CreateAsync), IPlanner

Agent.Tools:         ToolDispatcher, ActionProtocol (Craft + Smelt added)
                     Tools: MoveToTool, MineBlockTool, WanderTool, StatusTool,
                            PlaceBlockTool, SearchMemoryTool, GetPageTool, CreatePageTool,
                            ChatTool (NEW), CraftItemTool (NEW), FurnaceTool (NEW)

Agent.World.Minecraft: MinecraftAdapter, WebSocketBridge

WebUI.Blazor:        Program.cs (DI fixed), AgentBackgroundService (chat + new API)
                     wwwroot/index.html (NEW — interactive dashboard)
                     wwwroot/about.html (static info page)

MineflayerAdapter/:  index.js (chat onlinePlayers, craft, smelt actions added)
```

---

## Known Issues / Next Actions

### 1. Potential CI risk: transitive csproj reference (CHECK FIRST)
`WebUI.Blazor/Program.cs` uses `using Agent.Construction;` relying on transitive project reference resolution. If CI fails with "type Agent.Construction.IBlueprintRepository not found", add:
```xml
<ProjectReference Include="../Agent.Construction/Agent.Construction.csproj" />
```
to `WebUI.Blazor/WebUI.Blazor.csproj`. Get its current SHA first via `github__get_file_contents`.

### 2. Chat received messages not shown in dashboard
The dashboard (`index.html`) only shows sent messages and system events. Received chat requires SignalR or SSE push. Phase 6 task. Current workaround: watch the Minecraft client or `dotnet run` console.

### 3. CraftItemTool: crafting table range
4-block range may be too tight. If the bot fails to craft, add pathfinding to the table before crafting. Phase 6.

### 4. SmeltItem: batch optimization
Current implementation smelts and waits 40s per call. For bulk smelting, extend to place all input items then wait once. Phase 6.

### 5. Build origin requires manual setting
Before running `Build:small-house`, POST to `/api/agent/origin` with X/Y/Z of a flat area. Or use the dashboard's "Build Origin" panel. No auto-site-selection yet (Phase 6: FindFlatAreaTool).

### 6. Dashboard: chat log needs SignalR
Use the Phase 6 implementation to add `Microsoft.AspNetCore.SignalR` and push all WorldEvents to a client-side event stream.

---

## Phase 6 Priority Items

| Priority | Item | Description |
|----------|------|----------|
| P1 | Fix transitive ref (if CI fails) | Add Agent.Construction to WebUI.Blazor.csproj |
| P1 | SignalR real-time events | Push world events + chat to dashboard in real time |
| P2 | FindFlatAreaTool | Scan terrain, find a suitable build origin, set origin facts |
| P2 | CraftItemTool pathfinding | Navigate to nearest crafting table before crafting |
| P2 | SmeltItem batch | Place all input items at once, wait for all output |
| P3 | AgentBackgroundService async chat | Fire-and-forget goal creation to avoid event loop blocking |
| P3 | Complete GatherMaterials phase | Wire CraftItemTool into HtnTaskLibrary for crafted materials (planks, slabs, etc.) |
| P4 | LLM fallback for unknown chat commands | Use Microsoft.Extensions.AI for commands ChatInterpreter can't parse |
| P4 | Persona + tone customization | Let the bot be named/personalized via config |

---

## Testing Sequence (Phase 5)

### Quick REST test (no Minecraft needed):
```bash
# Check goals list
curl localhost:5000/api/goals

# Set a GatherWood goal
curl -X POST localhost:5000/api/agent/plan \
  -H "Content-Type: application/json" \
  -d '{"goalName":"GatherWood","parameters":{"count":5}}'

# Cancel it
curl -X DELETE localhost:5000/api/agent/goal

# Set build origin
curl -X POST localhost:5000/api/agent/origin \
  -H "Content-Type: application/json" \
  -d '{"blueprintId":"small-house","x":100,"y":64,"z":200}'

# Set Build:small-house goal
curl -X POST localhost:5000/api/agent/plan \
  -H "Content-Type: application/json" \
  -d '{"goalName":"Build:small-house"}'
```

### Dashboard test:
- Navigate to `http://localhost:5000/` → should show interactive dashboard
- Select GatherItem goal, enter "oak_log", count 10, click Set Goal
- Observe status panel update

### Minecraft chat test:
1. Join the same server as the bot
2. Type in chat: `get me 32 wood`
3. Bot should respond in chat and start gathering
4. Type: `stop` → bot cancels
5. Type: `build a house` → bot sets Build:small-house goal

---

## ADR Constraints (unchanged from Phase 4b)

- D-002: MemorySmith is the memory backend
- D-003: Deterministic-first; LLM only for novel goals
- D-006: Blueprints are wiki pages
- D-007: .slnx solution format
- D-008: Node.js for Mineflayer
- D-010: ActionProtocol wire names