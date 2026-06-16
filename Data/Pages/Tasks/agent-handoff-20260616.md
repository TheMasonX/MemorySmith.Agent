# Agent Handoff — MemorySmith.Agent

**For:** Next agent session continuing MemorySmith.Agent development  
**From:** Session ending 2026-06-16  
**Repo:** https://github.com/TheMasonX/MemorySmith.Agent  
**Last green CI commit:** `a2f6895b91` (`build-and-test` ✅, all tests passing)

---

## Suggested Skills

- **improve-codebase-architecture** (https://github.com/mattpocock/skills/blob/main/skills/engineering/improve-codebase-architecture/SKILL.md) — apply before and after every significant change; use deletion test, depth, locality, leverage vocabulary
- **handoff** (https://github.com/mattpocock/skills/blob/main/skills/productivity/handoff/SKILL.md) — when completing a major milestone, produce a fresh handoff for the next agent
- **MemorySmith Council Review** — 6-seat structured review (Source-Grounded Archivist, Data Model Architect, Retrieval Specialist, Human Learning Advocate, Skeptical Reviewer, Synthesizer); run after every major set of changes; commit result to `Data/Pages/council/`

---

## Project Context

MemorySmith.Agent is a modular autonomous Minecraft agent backed by the MemorySmith wiki as long-term memory. Three bounded contexts: **Agent Core** (planning, tools, goals), **MemorySmith Knowledge** (RestMemoryGateway → MemorySmith REST API), **World** (MinecraftAdapter + Node.js/Mineflayer).

**Architecture philosophy:** Matt Pocock deep-module principles. Every module change must pass the deletion test. The interface is the test surface. One adapter = hypothetical seam, two = real seam.

Key decisions are at: `Data/Pages/decisions.md`  
Architecture overview: `Data/Pages/architecture.md`  
Council reviews (3 completed): `Data/Pages/council/`  
Task tracking: `Data/Tasks/` (9 JSON tasks, TSK-0001 through TSK-0009)  
Wiki home: `Data/Pages/home.md`

---

## What Was Completed This Session

### Bug Fixes (all CI-verified, committed)

Three interconnected game-loop bugs were fixed:

1. **Mine pathfinder timeouts were fatal** — one unreachable block killed the entire mine operation. Fix: per-block `try/catch` inside the Node.js mine loop; `pathFailures` counter (max 3) before abandoning.
2. **Bot got stuck on depleted patches** — GatherWoodDecompose now leads with `Wander(radius=40, maxDist=200)` so each plan cycle explores a new area.
3. **`_consecutiveFailures` never incremented** — fire-and-forget tools always return `ToolResult(true)`. Fix: Node.js throws when 0 blocks found → C# processes `error`/`blockNotFound` events into `WorldState.Facts["game.lastError"]` → after the 300ms settle delay, `DispatchActionsAsync` consumes the fact and increments `_consecutiveFailures`. After 3 failures the goal is abandoned cleanly.

### Architecture Review (Candidates 1 & 2 implemented)

An Explore subagent audited 8 friction points against the SKILL.md deepening vocabulary. Full findings are in conversation history.

**Candidate 1 (done):** 39 new tests committed:
- `WorldStateBuilderTests` (11) — `AddInventoryItem`, `SetInventory`, `SetFact`
- `GoalFactoryTests` (8) — name resolution, case-insensitive, parameter parsing
- `SurviveNightGoalTests` (10) — `HasFailed` threshold, `IsComplete` time/shelter predicates
- `ToolDispatchTests` (10+) — verifies `ActionData` shape dispatched to `MockWorldAdapter` for each tool

**Candidate 2 (done):** `ToolDispatcher` created (`Agent.Tools/ToolDispatcher.cs`). Merges `ToolRegistry` + `ToolEngine` into one deep module (deletion test confirmed ToolEngine earned no independent depth). `ToolEngine.cs` kept as a thin shim; `Program.cs` registers `ToolDispatcher` directly.

---

## Remaining Immediate Tasks (do these first)

These three candidates came from the architecture review council. Run `build-and-test` CI after each.

### Candidate 3 — Extract `WorldStateProjector`

**Files:** `WebUI.Blazor/AgentBackgroundService.cs` (god class, ~340 lines)  
**Problem:** Five distinct responsibilities in one class. The event-parsing switch block (`ProcessEventsAsync`, lines 80–200) contains 3 helpers (`ApplyPosition`, `ApplyHealthAndFood`, `ApplyInventorySnapshot`) and handles 6 event types. The `game.lastError` side-channel (written by `ProcessEventsAsync`, consumed by `DispatchActionsAsync` after a 300ms window) is the most dangerous coupling — two async loops communicating via a stringly-typed WorldState fact.

**Solution:**
1. Create `Agent.Core/WorldStateProjector.cs` — a pure, stateless class with a single method:  
   `WorldState Apply(WorldState current, WorldEvent ev)` — handles all event types (health, spawn, move, blockMined, error, blockNotFound, status). Returns the new state; does not touch `game.lastError`.
2. Replace `_worldState.Facts["game.lastError"]` with a `Channel<string> _gameErrors` field in `AgentBackgroundService`. `ProcessEventsAsync` writes to the channel; `DispatchActionsAsync` reads from it after the settle delay. Typed, not stringly-typed.
3. `ProcessEventsAsync` becomes: `await foreach (var ev in worldAdapter.ReceiveEventsAsync(ct)) { _worldState = _projector.Apply(_worldState, ev); }`

**Benefits:** `WorldStateProjector.Apply` is a pure function — trivially testable with `WorldStateProjectorTests`. `AgentBackgroundService` shrinks by ~120 lines. The `game.lastError` timing window becomes explicit and inspectable.

**Note on `Agent.Core` vs `WebUI.Blazor`:** `WorldStateProjector` depends on `WorldState`, `WorldEvent`, and `JsonDocument` (for inventory snapshot). All are in `Agent.Core`. It does NOT depend on the logger (the logger stays in the service). This makes the module reusable.

### Candidate 4 — Remove Dead Metadata

**Files:** `Agent.Planning/HtnTask.cs`, `Agent.Planning/Goals/GatherWoodGoal.cs`, `Agent.Planning/Goals/SurviveNightGoal.cs`  
**Problem:** `HtnTask.cs` defines `record HtnTask(string Name, string Description, string[] SubTasks)` — never instantiated by `HtnTaskLibrary`. `GatherWoodGoal.Phases` returns `["FindTree", "MineWood", "Collect"]` but `HtnPlanner` takes the direct-decomposition path (never reads phases for this goal).  
**Solution:** Delete `HtnTask.cs`. Optionally strip `Phases` from goals where the planner bypasses them — but confirm by checking `HtnPlanner.PlanAsync` logic first.  
**ADR note:** If you decide Phases should be preserved as documentation (even if unused by the planner), record that as an ADR in `Data/Pages/decisions.md` so future agents don't re-suggest removal.

### Candidate 5 — ActionProtocol Constants

**Files:** All tool files in `Agent.Tools/Tools/`, `Agent.World.Minecraft/WebSocketBridge.cs`  
**Problem:** The registered tool name (`"MineBlock"`) differs from the wire action name (`"mine"`) — the mapping is hidden inside each tool's `ExecuteAsync` body. `WebSocketBridge.SendAsync` lowercases `ActionData.Tool` blindly, so `"MineBlock"` becomes `"mineblock"` (wrong); `MineBlockTool` manually sets `Tool = "mine"` to compensate.  
**Solution:** Add `Agent.Tools/ActionProtocol.cs`:
```csharp
public static class ActionProtocol
{
    public const string Move     = "move";
    public const string Mine     = "mine";
    public const string Place    = "place";
    public const string Status   = "status";
    public const string Wander   = "wander";
    public const string Chat     = "chat";
}
```
Update each tool to use `ActionProtocol.Mine` etc. instead of string literals. Update `WebSocketBridge.SendAsync` to NOT lowercase the tool name (the tool is now responsible for passing the correct wire name).

---

## New Feature Direction: Arbitrary-Item Gather Goal

The user wants GatherWood to become a generalised gathering system that works with arbitrary items including modded ones. This is a significant Phase 4-5 feature. Think this through carefully before implementing.

### The Problem with the Current Design

`GatherWoodGoal` is hardcoded:
- Item: `minecraft:oak_log` (hardcoded in HtnTaskLibrary)
- Completion: sums `*_log` inventory keys (hardcoded in GatherWoodGoal)
- Block search: `minecraft:oak_log` or `minecraft:birch_log` (hardcoded in Node.js mine loop)

This cannot handle: iron ore, diamonds, mod items (TConstruct cobalt, Thermal Expansion tin), or items requiring crafting/smelting (you can't "gather" iron ingots directly — you must mine iron ore and smelt it).

### Design Direction

Consider these questions before architecting:

1. **Item specification schema** — what does the agent know about an item? Minimum: `{ id: "minecraft:iron_ore", displayName: "Iron Ore" }`. For smelting chains: `{ id: "minecraft:iron_ingot", raw: "minecraft:iron_ore", requiresSmelting: true }`. For mods: the LLM may need to learn the ID from the user or from wiki memory.

2. **`GenericGatherGoal(itemSpec, count)` vs separate goal classes** — a single generic goal that accepts an item specification is simpler but requires the item spec to be machine-readable. Council question: should `IsComplete` check inventory by exact ID, or by a predicate function?

3. **BlockRegistry / ItemRegistry** — a mapping from item ID to the block(s) that yield it when mined. Vanilla: `iron_ingot → iron_ore (smelt)`, `diamond → diamond_ore (mine)`. Mods: needs to be populated from memory/user input. This could be a MemorySmith wiki page: `"item-registry/iron-ingot"` with fields for the block, mining tool, and any crafting chain.

4. **LLM role** — when the user says "gather some iron", the LLM needs to decompose this into: find iron_ore, navigate to y=16, mine it, smelt in furnace. This requires LLM planning (Phase 4+). For now, a deterministic path works for common Vanilla items if you have the item spec.

5. **Mod support** — the agent cannot know mod item IDs without being told. The architecture: user or admin adds a MemorySmith page for a modded item (`"item-registry/cobalt-ore": { block: "tconstruct:cobalt_ore", mineTool: "pickaxe", harvestLevel: 4 }`); the agent's `SearchMemory("cobalt ore location or definition")` finds this page; the `GenericGatherGoal` reads it.

### Suggested Implementation Path (for the next agent to develop)

1. **Immediate:** Rename `GatherWoodGoal` → `GatherItemGoal(itemId, count)` with configurable item ID and block search pattern. Keep the GatherWoodGoal as a factory convenience (`GoalFactory["GatherWood"]` creates `GatherItemGoal("minecraft:oak_log", 10)`). Backward compatible.

2. **Medium:** Add `ItemRegistry` as a MemorySmith-backed lookup. `GatherItemGoal` calls `SearchMemory("item-registry/{itemId}")` to find the block to mine. This is a real seam — the registry can be populated by the LLM or by the user.

3. **Long-term:** LLM-driven goal decomposition for unknown items. User says "find cobalt" → LLM queries SearchMemory for cobalt, gets the block ID from the registry, creates a `GatherItemGoal`.

4. **Run a council review before implementing.** The design has several seams that need agreement (ItemRegistry interface, LLM fallback shape, mod page schema).

---

## Tech Stack Reference (redacted)

- .NET 10, C# 14, net10.0 target, `.slnx` solution format
- Node.js 22+, ESM modules, mineflayer + mineflayer-pathfinder + ws
- NUnit 4.6.1 + coverlet 10.0.1 + GitHubActionsTestLogger 2.4.1
- GitHub Actions CI: `dotnet test MemorySmith.Agent.slnx`  
- GitHub MCP token: read/write to TheMasonX/MemorySmith.Agent (no workflow scope — can't push to `.github/workflows/` directly)
- MemorySmith instance: running locally on port 6868 (user's machine — the agent doesn't have access to this, only the repo)
- Minecraft server: LAN server — connection details in `WebUI.Blazor/appsettings.json` (not committed to repo for security)

---

## Process Protocol for This Agent

1. **Before any significant change:** explore codebase with Explore subagent, apply deletion test, surface deepening candidates
2. **After implementing a candidate:** commit, wait for CI green, then move to next
3. **After every 3-5 candidates:** run full MemorySmith 6-seat council review, commit result to `Data/Pages/council/`
4. **After council review:** implement any council-identified blockers before moving to new features
5. **Every session:** update `Data/Tasks/` JSON files with current status, update `Data/Memories/Core/` with new facts, update `Data/Pages/roadmap.md`
6. **Before finishing:** write a fresh handoff doc to `/tmp/` following the handoff SKILL

---

## Key ADR-Level Constraints (do not re-litigate)

See `Data/Pages/decisions.md` for the full list. Key ones:
- **D-002:** MemorySmith is the memory backend — do not build a custom knowledge store
- **D-003:** Deterministic-first planning — LLM only for novel/unknown goals or failure recovery
- **D-006:** Blueprints are MemorySmith wiki pages — not a separate database
- **D-007:** `.slnx` solution format (not `.sln`)
- **D-008:** Node.js for Mineflayer (not .NET) — the subprocess boundary is a real seam

---

## Immediate Start Checklist

```
[ ] Read Data/Pages/council/phase3-planner-architecture-council-20260615.md (context for Candidates 3-5)
[ ] Read Data/Tasks/tsk-0004-wire-moveto-context-injection.json (highest priority open task)
[ ] Implement Candidate 3: WorldStateProjector extraction + Channel<string> for game errors
[ ] CI green after Candidate 3 → commit council note to Data/Pages/council/
[ ] Implement Candidate 4: delete HtnTask.cs, verify Phases usage
[ ] Implement Candidate 5: ActionProtocol constants
[ ] Run council review on Candidates 3-5 together
[ ] Design session: GenericGatherGoal + ItemRegistry architecture (council before implement)
[ ] Open TSK-0010 for GenericGatherGoal in Data/Tasks/
[ ] Update Data/Memories/Core/ with session findings
[ ] Write fresh handoff to /tmp/
```
