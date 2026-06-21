# Agent Handoff — Sprint 22 Complete

**For:** Next agent session
**From:** Sprint 22 session (2026-06-18)
**Repo:** https://github.com/TheMasonX/MemorySmith.Agent
**Branch:** sprint-5-tool-safety (open PR #1, extended dev branch)
**CI:** queued on b19fc435 (latest push: post-sprint council)
**Council:** APPROVED (sprint22-council-20260618.md, 89% avg, 0 blockers, 13 deferred)

---

## What Was Done This Sprint

Two tracks: planner completeness first (three Sprint 21 deferred P0s), then world KB separation.

### Track A: Planner Completeness

**P0-A: CraftItemGoal.IsInventoryStale gate (DELIVERED)**
Root cause: after admin `/clear`, `CraftItemGoal.IsComplete` returned true immediately on stale inventory. Fix mirrors Sprint 21 GenericGatherGoal pattern exactly.
- `CraftItemGoal.IsComplete`: `if (state.IsInventoryStale) return false;` before the inventory check
- Tests: 5 tests in `Sprint22CraftItemGoalTests` (stale=true, fresh+sufficient, fresh+insufficient, empty, stale-then-fresh cycle)

**P0-B: HtnPlanner quantity propagation (DELIVERED)**
Root cause: `HtnPlanner` called `library.DecomposeGatherItem(isg.Spec, [], state)` with empty parameters. `GatherItemDecompose` defaulted to count=10 for every gather request regardless of `GenericGatherGoal.TargetCount`. "get 100 sand" produced a count=10 plan.
- Fix: `var parameters = isg is GenericGatherGoal ggg ? new[] { ggg.TargetCount.ToString() } : Array.Empty<string>();`
- Tests: 4 tests in `Sprint22QuantityPropagationTests` (count 100, count 1, count 10, multi-block spec)

**P0-C: Health-critical check in ProcessEventsAsync (DELIVERED)**
Root cause: no health monitoring in the agent loop. Bot drowned in Session 1 without any recovery attempt.
- `HealthCriticalThreshold = 6` (3 hearts) named constant in `AgentBackgroundService`
- After every `_projector.Apply`, checks `Health is > 0 and < 6` with active goal: enqueues `GetStatus`
- Ensures next plan cycle starts with accurate WorldState. Note: health detection is StatusEvent-driven; proactive Node.js health events deferred to Sprint 23.
- Tests: 3 threshold boundary tests in `Sprint22HealthCheckTests`

**P1-A (D-1): Staleness debug log at IsComplete call site (DELIVERED)**
In `DispatchActionsAsync`, added `LogDebug` when `IsInventoryStale=true` before the IsComplete check. Fires only between SetGoal and first GetStatus arrival — infrequent, won't flood logs.

### Track B: World KB Separation

**P1-B: World KB infrastructure (DELIVERED)**
Adds a dedicated MemorySmith instance path for Minecraft world data, separate from the agent codebase KB.

Files changed:
- `RestMemoryGatewayOptions`: `WorldKbUrl` (default `http://127.0.0.1:6869`), `WorldApiKey`, `WorldTimeoutSeconds` (30s)
- `Program.cs`: named HttpClient `"memorysmith-world"` + `AddKeyedSingleton<IMemoryGateway>("world", ...)`; version bumped to `0.22.0`
- `appsettings.json`: `WorldKbUrl`, `WorldApiKey`, `WorldTimeoutSeconds` configuration entries
- `Data/Pages/Guides/world-kb-deployment.md`: comprehensive deployment guide (standalone, Docker, multi-world, troubleshooting)

**Status:** Infrastructure ready, tools not yet routed to world KB. `SearchMemoryTool` and `CreatePageTool` still use the agent KB. Tool routing is Sprint 23 P0.

**P1-C (E-1): AGENTS.md verbatim-regex pattern (DELIVERED)**
"Additional warning: verbatim regex files" section added to AGENTS.md. Describes the safe fetch-from-raw-URL + `str.replace()` patching pattern for files like `LlmChatInterpreter.cs` and `WorldStateProjector.cs`.

---

## Sprint 23 Priorities

### P0: Tool routing to world KB
Wire `SearchMemoryTool` and `CreatePageTool` to use `[FromKeyedServices("world")] IMemoryGateway worldMemory` in Program.cs. The world-keyed singleton is registered; only the tool registration in the `ToolDispatcher` factory needs updating.

### P0: Node.js proactive health event
Add `bot.on('health', () => ...)` handler in `MineflayerAdapter/index.js` to emit a typed `healthChanged` event with `{ health, food }` payload. C# side: add `HealthChangedEvent` to `WorldEvents.cs`, handle in `WorldStateProjector`, route in `ProcessEventsAsync`. This closes the gap where drowning is only detectable after a GetStatus — the bot can now respond in real-time to health drops.

### P1: Health check rate-limit guard
Add `_lastHealthCheckAt` field in `AgentBackgroundService`. In the health-critical check block, only enqueue GetStatus if `(UtcNow - _lastHealthCheckAt) >= TimeSpan.FromSeconds(2)`. Prevents queue flood when multiple events arrive while health is low.

### P1: WorldKbUrl null default + startup reachability warning
Change `RestMemoryGatewayOptions.WorldKbUrl` default from `http://127.0.0.1:6869` to `null`. Update the fallback logic in Program.cs accordingly. Add a startup log warning: `LogWarning("World KB URL not configured — world observations will be stored in agent KB (run world-kb-deployment.md to set up separation)")`.

### P1: GatherGoalDecomposer TargetCount propagation
`GatherGoalDecomposer` (used by `DecomposerRegistry`/`PlannerRouter`) also calls `DecomposeGatherItem` with potentially empty parameters. Add a comment in `GatherGoalDecomposer.Decompose` noting the TargetCount pattern, and fix if `PlannerRouter` is ever promoted to `IPlanner`.

### Council deferred (Sprint 23 candidates)
From sprint22-council:
- D-1 (Chair 2): Integration test for health-critical GetStatus enqueue
- D-1 (Chair 4): Startup reachability warning for WorldKbUrl
- D-2 (Chair 4): Pin MemorySmith version in deployment guide
- D-3 (Chair 4): Sprint 23 P0 — tool routing (also in P0 above)

---

## Architecture Notes

### World KB flow (post-wiring)
```
User: "what's at my build site?"
↓
SearchMemoryTool → [FromKeyedServices("world")] worldMemory
→ GET http://127.0.0.1:6869/api/search?q=build+site
→ Returns world observation pages (block locations, exploration notes)

Agent: "sprint docs"
↓  
GetPageTool → [default] agentMemory
→ GET http://127.0.0.1:6868/api/pages/...
→ Returns sprint docs, council reviews (NOT world data)
```

### Health-critical recovery flow (current)
```
StatusEvent arrives (from GetStatus in a plan)
↓ WorldStateProjector.ApplyStatus → _worldState.Health = 4
↓ ProcessEventsAsync: Health(4) < HealthCriticalThreshold(6) && goal active
↓ _queue.Enqueue(GetStatus)  ← next plan cycle starts with fresh state
↓ Next cycle: planner re-evaluates goal with accurate health data
```

### Health-critical recovery flow (after Sprint 23 Node.js fix)
```
Bot takes drowning damage (continuous)
↓ bot.on('health') → WS send { action: 'healthChanged', health: 4, food: 20 }
↓ C#: HealthChangedEvent → WorldStateProjector updates Health
↓ ProcessEventsAsync: triggers recovery without waiting for GetStatus
```

---

## Files Changed This Sprint

| File | Change |
|------|--------|
| `Agent.Planning/Goals/CraftItemGoal.cs` | IsComplete: IsInventoryStale gate |
| `Agent.Planning/HtnPlanner.cs` | IItemSpecGoal branch: pass TargetCount as parameters[0] |
| `WebUI.Blazor/AgentBackgroundService.cs` | HealthCriticalThreshold const + health check + D-1 staleness log |
| `AGENTS.md` | E-1: verbatim-regex safe patch pattern |
| `Agent.Memory/RestMemoryGatewayOptions.cs` | WorldKbUrl, WorldApiKey, WorldTimeoutSeconds |
| `WebUI.Blazor/Program.cs` | World KB HttpClient + keyed singleton + v0.22.0 |
| `WebUI.Blazor/appsettings.json` | WorldKbUrl + WorldTimeoutSeconds |
| `Data/Pages/Guides/world-kb-deployment.md` | NEW: deployment guide |
| `MemorySmith.Agent.Tests/Sprint22Tests.cs` | NEW: 14 tests (4 fixtures) |
| `Data/Pages/council/sprint22-pre-council-20260618.md` | NEW: pre-sprint council |
| `Data/Pages/council/sprint22-council-20260618.md` | NEW: post-sprint council |

---

## 8 Non-Negotiable Rules (carry forward)

1. TreatWarningsAsErrors = true — all warnings are errors
2. Never call SendEmergencyStop from SetGoal
3. Using directives BEFORE file-scoped namespace in test files (AGENTS.md Rule 3)
4. All timeouts/TTLs/retry counts must be named constants or configurable options
5. Never push C# verbatim-string files via agent intermediary (E-1)
6. Each sprint: implement → push → CI green → council review → fix blockers → next sprint
7. GitHub MCP: use mcp__t__ExecuteIntegration with paramsFile for file pushes
8. When patching verbatim-regex files (LlmChatInterpreter.cs, WorldStateProjector.cs): fetch via raw URL → str.replace() in Python → json.dump with ensure_ascii=False (AGENTS.md "Additional warning")
