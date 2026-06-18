# AGENTS.md — MemorySmith.Agent Contributor Guide

> **Read this before touching any code.** It takes 5 minutes and will prevent you from
> making the 6 most common mistakes. Every section is a non-negotiable rule, not a suggestion.

---

## What this codebase is

A modular autonomous Minecraft bot (name: **Leo**) written in .NET 10 C# + Node.js.

- **C# host** owns the brain: planning (HTN), memory, LLM chat, REST API, SignalR dashboard.
- **Node.js adapter** owns the hands: Mineflayer WebSocket bridge to the actual Minecraft server.
- **MemorySmith** is the long-term memory: a wiki REST API the bot reads/writes during execution.

The two runtimes communicate over WebSocket. The C# side sends JSON action objects; the Node side sends JSON event objects back. Both sides define typed contracts — never pass freeform strings.

---

## Architecture rules — never break these

**1. Deterministic first, LLM optional.**
The HTN planner and ChatInterpreter pattern-matcher are the primary decision paths.
The LLM is a fallback for unrecognized intents — it must never be in the critical path for
actions that can be handled deterministically (gather, navigate, craft, status).
File: `Agent.Planning/ChatInterpreter.cs` (fast-path), `Agent.Planning/LlmChatInterpreter.cs` (LLM).

**2. Typed events, not stringly-typed payloads.**
Every event from the Node adapter is a `sealed record` in `Agent.Core/Events/WorldEvents.cs`.
If you're writing `worldEvent.GetType().Name` or dictionary lookups anywhere, something is wrong.
`WorldStateProjector.cs` pattern-matches on event subtypes — add your case there.

**3. Named constants, never magic numbers.**
Any timeout, radius, retry count, or tunable value must be a named constant or config option.
In C#: `private const int X = n` or `public int XSeconds { get; init; } = n` in an Options class.
In JS: `const X_CONSTANT = n` at the top of `MineflayerAdapter/index.js`, overridable via `args`.
The scoring weights in `findFlatArea` use `FLAT_SCORE_WEIGHTS.area` — not `0.5`. Do the same.

**4. `BuildFactKeys` for shared fact strings.**
Any string that is written in one file and read in another must be a constant in `Agent.Core/BuildFactKeys.cs`.
Never duplicate a fact key string across files. If you find one, fix it before adding more.

**5. No C# `using` after `namespace`.**
File-scoped namespace declarations break relative type resolution in `MemorySmith.Agent.Tests`.
All `using` directives go **before** the `namespace` line. Every file, always.

**6. The chat interpreter fast-path must stay fast.**
`ChatInterpreter.ParseIntent` runs on every addressed message. It must be pure, synchronous,
and allocate nothing beyond the `ChatInterpretation` record it returns.
If you add regex: use the compiled `static readonly Regex` pattern at class level.

**7. Thread safety: `WorldModel.Reconcile` must hold `_lock` for the entire body.**
The `Queue<double>` inside `WorldModel` is guarded by `_lock`. Don't "optimize" it by
splitting the lock scope — the enqueue+trim+cache-update must be atomic.

**8. Warnings are errors — fix them before pushing.**
`Directory.Build.props` sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` globally.
Any warning you introduce will fail CI. Fix the warning; do NOT suppress it without a comment
explaining exactly why the suppression is necessary and why the code is still correct.
If a third-party SDK emits a warning you cannot control, suppress it with `#pragma warning disable <ID>`
on the specific line, with a comment like `// false positive from Serilog — field is read-only after init`.
Suppressing a whole file or project is not acceptable unless all engineers have reviewed it.

---

## Where things live

| Concern | File |
|---------|------|
| Typed world events | `Agent.Core/Events/WorldEvents.cs` |
| State projection (events -> facts) | `Agent.Core/WorldStateProjector.cs` |
| HTN task decompositions | `Agent.Planning/HtnTaskLibrary.cs` |
| Chat fast-path (no LLM) | `Agent.Planning/ChatInterpreter.cs` |
| LLM chat + history | `Agent.Planning/LlmChatInterpreter.cs` |
| Tool dispatch + schema validation | `Agent.Tools/ToolDispatcher.cs` |
| Minecraft WebSocket bridge (C#) | `Agent.World.Minecraft/MinecraftAdapter.cs` |
| Minecraft WebSocket bridge (JS) | `MineflayerAdapter/index.js` |
| Agent loop + event routing | `WebUI.Blazor/AgentBackgroundService.cs` |
| REST endpoints + DI wiring | `WebUI.Blazor/Program.cs` |
| Shared fact key constants | `Agent.Core/BuildFactKeys.cs` |
| Council reviews | `Data/Pages/council/` |
| Sprint task tracker | `Data/Pages/Tasks/phase6-tasks.md` |
| Guides (how to run, features) | `Data/Pages/Guides/` |
| Architecture decisions | `Data/Pages/decisions.md` |

---

## How sprints work

```
implement -> local build/test -> push via github__create_or_update_file ->
CI green (build-and-test: success) ->
LLM council review (Data/Pages/council/sprint<N>-council-<date>.md) ->
fix blockers -> push fixes -> confirm CI green -> next sprint
```

**Never move to the next sprint with a blocking council finding open.**
**Never skip CI.** If CI fails, look at the annotations endpoint — you don't need admin rights:
```
curl https://api.github.com/repos/TheMasonX/MemorySmith.Agent/check-runs/<id>/annotations
```

**Council seats** (6 for major changes, 5 for focused sprints):
Source-Grounded Archivist · System Architect · Minecraft World Specialist ·
Player/Dev Experience · Skeptical Reviewer · Synthesizer

Each seat must state: confidence %, explicit dissent, blocking vs. deferred.

---

## Common patterns

### Adding a new world event

1. Add a `sealed record FooEvent(... DateTimeOffset Timestamp) : WorldEvent(Timestamp)` to `WorldEvents.cs`.
2. Add a `case FooEvent e:` to `WorldStateProjector.StoreFacts` — store raw facts.
3. If it changes structured state (position, health, inventory), add an `ApplyFoo` method and add it to the `Apply` switch.
4. Handle it in `AgentBackgroundService.ProcessEventsAsync` if it should trigger behavior.
5. Add tests to `WorldStateProjectorTests.cs`.

### Adding a new tool

1. Create `Agent.Tools/Tools/FooTool.cs` implementing `ITool`.
2. Define `InputSchema` as a static cached `JsonDocument` (not `new JsonDocument()` in a property — that disposes on return).
3. Register in `Program.cs` inside the `IToolCaller` singleton factory: `d.Register(new FooTool(world))`.
4. Add the wire-level handler to `MineflayerAdapter/index.js` as a new `case 'foo':` in `dispatch`.
5. Add tests — at minimum, schema validation tests in `ToolDispatchTests.cs`.

### Adding a new HTN task decomposition

1. Add a private static method `FooDecompose(string[] parameters, WorldState state)` to `HtnTaskLibrary`.
2. Register it in `_methods` inside the constructor: `["Foo"] = FooDecompose`.
3. If it returns `PlaceBlock` actions for a build: add `buildProgressBlueprintId` and `buildProgressBlockIndex` context entries to each `PlaceBlock` action for checkpoint tracking.

### Testing the /api/agent/resolve endpoint (Sprint 16+)

`GET /api/agent/resolve` is the single-entry knowledge lookup added in Sprint 16.
Use these `curl` examples to verify the resolver is working after code changes:

```bash
# Look up an item by ID (registry hit expected — oak_log is a DirectMineable item)
curl -s "http://localhost:5000/api/agent/resolve?q=oak_log" | python3 -m json.tool

# Look up a smeltable item
curl -s "http://localhost:5000/api/agent/resolve?q=iron_ingot" | python3 -m json.tool

# Filter by CandidateType (only WikiPage results)
curl -s "http://localhost:5000/api/agent/resolve?q=iron+ore&types=WikiPage" | python3 -m json.tool

# Filter by confidence threshold (only high-confidence matches)
curl -s "http://localhost:5000/api/agent/resolve?q=diamond&confidenceThreshold=0.8" | python3 -m json.tool

# Inspect WorldFact results from live WorldState (Sprint 17+; requires agent running)
curl -s "http://localhost:5000/api/agent/resolve?q=inventory&types=WorldFact&topN=10" | python3 -m json.tool
```

**Notes:**
- `Agent:Enabled=true` must be set in configuration; otherwise the endpoint returns HTTP 500.
- `SearchAsync` in the wiki fallback receives the **raw un-normalized query** (e.g., `"iron ore"` not `"iron_ore"`) — this is intentional; the semantic search engine handles natural-language phrases better than underscored identifiers.
- `wasAmbiguous=true` means the top-2 candidates are within 0.05 confidence of each other. Callers should surface a clarification prompt rather than auto-picking the best result.

### Pushing files via GitHub MCP

```python
# Always use paramsFile — never inline large content
params = { 'owner':'TheMasonX', 'repo':'MemorySmith.Agent',
           'branch':'sprint-5-tool-safety', 'path':'...',
           'message':'...', 'sha':'<current blob SHA>', 'content': content }
pathlib.Path('/tmp/push.json').write_text(json.dumps(params))
# ExecuteIntegration(action='github__create_or_update_file', paramsFile='/tmp/push.json')
```

For **new files**, omit `sha`. For **existing files**, `sha` is the blob SHA from the most recent
`github__get_file_contents` or `github__create_or_update_file` response — not a commit SHA.
**Never push `content` as base64** — the MCP tool base64-encodes internally. Pre-encoding = double-encoding = corruption.

---

## Don't do these things

| Don't | Do instead |
|-------|------------|
| `new Vec3(x, y, z)` in `index.js` | `{ x, y, z }` plain object — `bot.blockAt` reads `.x/.y/.z` |
| `ToDictionary` on a list that may contain duplicate keys | `GroupBy(...).ToDictionary(g => g.Key, g => g.Sum(...))` |
| Lock only the cache-update inside `WorldModel.Reconcile` | Lock the full enqueue+trim+cache-update body |
| Pass a fact key string as a raw literal in two different files | Add it to `BuildFactKeys` first |
| Skip CI before writing a council review | CI must be green before review |
| Call `provider.CompleteAsync` on the fast-path | Fast-path exits before `LlmChatInterpreter` checks `IsAvailable` |
| Scan `findFlatArea` without yielding to the event loop | `await new Promise(r => setImmediate(r))` every 200 columns |
| Accept liquid/water/lava as a valid build surface | Check `LIQUID_BLOCK_NAMES` before adding to heightMap |
| Use `"build:auto:origin:x"` as a raw string in two files | `BuildFactKeys.AutoOriginX` |
| Start a build from index 0 if a checkpoint exists | Read `BuildFactKeys.BuildProgressIndex(blueprintId)` from facts |
| Enqueue a chat response before `SetGoal` or `CancelGoal` | Enqueue AFTER the switch in `HandleChatEventAsync` — both methods call `_queue.Clear()` |
| Use non-thread-safe `Queue<T>` for `ActionQueue` | `ConcurrentQueue<T>` — two tasks access the queue concurrently |
| Declare a field `volatile` AND pass it by `ref` | Remove `volatile`; use `Volatile.Read` / `Interlocked` methods directly |
| Set the same goal the recovery interpreter just abandoned | Check `_lastAbandonedGoalName` in `TryRecoverFromGameErrorAsync` |
| Ignore build warnings | Fix them. `TreatWarningsAsErrors=true`. If you can't fix, suppress with a comment. |

---

## Key ADRs

| ID | Decision | Why |
|----|----------|-----|
| D-002 | MemorySmith wiki as long-term memory | Survives restarts; structured; already exists |
| D-003 | Deterministic-first, LLM as fallback | Latency, reliability, cost |
| D-006 | Blueprints are wiki pages | No separate schema; freely editable |
| D-007 | `.slnx` solution format | VS 2022 native; no `.sln` XML noise |
| D-008 | Node.js for Mineflayer | No .NET Mineflayer equivalent |
| D-010 | Action protocol names are lowercase | Matches Mineflayer convention |

Full decisions: `Data/Pages/decisions.md`.

---

## Running the project

See `Data/Pages/Guides/running-the-agent.md` for the complete quickstart.

**TL;DR:**
```bash
# 1. Start the Node adapter
cd MineflayerAdapter && MC_HOST=localhost MC_PORT=25565 MC_USERNAME=Leo WS_PORT=3000 node index.js

# 2. Start the C# host
dotnet run --project WebUI.Blazor --launch-profile WebUI.Blazor

# 3. Run tests
dotnet test MemorySmith.Agent.Tests
```
- Sprint 20 complete: progress-hash governor, LLM truncation recovery, expanded system message filter. Sprint 21 P0-A: inventory freshness gate (WorldState.IsInventoryStale — SetGoal marks stale, StatusEvent clears).

## Rule: Never patch C# verbatim-string files via agent intermediary

C# verbatim strings use `""` (doubled double-quotes) as the escape for a literal `"`.
When a subagent reads a C# file and passes the content to `github__create_or_update_file`,
the agent's JSON encoding converts `""` to `\"`, corrupting verbatim string literals.

**Always use mcp__t__ExecuteIntegration with a paramsFile for C# file pushes:**

```python
import json
with open('myfile.cs', 'r', encoding='utf-8') as f:
    content = f.read()
params = {"owner": "TheMasonX", "repo": "MemorySmith.Agent",
          "path": "...", "branch": "sprint-5-tool-safety",
          "message": "...", "content": content,
          "sha": "current-file-sha",
          "committer": {"name": "MemorySmith Agent", "email": "agent@memorysmith.dev"}}
with open('/agent/workspace/params.json', 'w') as f:
    json.dump(params, f, ensure_ascii=False)
```
Then: `mcp__t__ExecuteIntegration(action="github__create_or_update_file", paramsFile="/agent/workspace/params.json")`

Also: files fetched via GitHub API are base64-encoded. If a file was previously pushed by an agent
(double-encoded), you must double-decode: `base64.b64decode(base64.b64decode(api_content))`.
