# MemorySmith.Agent Deep Code Audit
**Scope:** current tip visible on the repository's default branch (`main`), not `master` (the repo reports `main` as default).  
**Commit anchor used for file review:** `b95592634b373d621299499bd70b009d01f6929e` (the commit exposed in the GitHub file URLs returned by the connector).  
**Date:** 2026-06-21

## Executive summary

This branch is much healthier than the earlier audit snapshot. Several of the highest-risk items from the sprint backlog are already closed in the current codebase: tool dispatch now wraps tool exceptions, integer schema validation uses `TryGetInt32`, `FindFlatArea` defaults are aligned with the Mineflayer adapter, the duplicate `StatusTool` has been removed in favor of an alias, the action lifecycle now carries `correlationId`, the world model no longer aliases mutable inventory state, and gather decomposition now preserves `TargetCount`. See `Agent.Tools/ToolDispatcher.cs`, `Agent.Tools/Tools/FindFlatAreaTool.cs`, `Agent.Planning/Decomposition/GatherGoalDecomposer.cs`, `WebUI.Blazor/AgentBackgroundService.cs`, and the sprint-25/26 handoff docs for the implemented state. [ToolDispatcher.cs:17-24, 95-116, 191-203] [FindFlatAreaTool.cs:13-16, 60-78] [GatherGoalDecomposer.cs:12-24, 42-59] [agent-handoff-sprint26.md:14-57]

The biggest remaining issues are not the old safety bugs; they are planning drift, representation depth, and architectural coherence. The repo still has conflicting “source of truth” documents for version/sprint status, the Mineflayer adapter still exposes only a thin slice of the bot/world state, and the adapter’s chat filtering is still a regex firewall that will age poorly. The planning layer also still carries legacy hardcoded decomposition branches in `HtnPlanner` even though `PlannerRouter` now owns the preferred routing seam. [README.md:7-10, 157-170] [roadmap.md:3-8, 59-67] [MineflayerAdapter/index.js:288-303] [HtnPlanner.cs:10-85] [PlannerRouter.cs:51-139]

My strongest recommendation is to treat the next round of work as a **representation upgrade** rather than more one-off fixes: deepen the world observation seam, make the adapter emit richer, typed observation events, and then tighten the planner around those observations. That aligns with the repository’s own “deterministic first” architecture and with the architecture-skill guidance to surface the real seams, deepen modules, and use deletion tests to remove shallow duplication. [architecture.md:45-62, 101-111] [turn617699view0]

## What I deliberately did **not** re-open

I did **not** re-report items that the current branch already appears to have fixed:

- FindFlatArea defaults and integer parsing.
- Status/GetStatus duplication.
- Tool dispatcher exception propagation and validation boundary.
- Action correlation IDs and lifecycle tracking.
- World model inventory aliasing.
- Gather target count pass-through.

Those were backlog items in sprint handoff docs, but the current files show they are already closed. [ToolDispatcher.cs:95-116, 191-203] [FindFlatAreaTool.cs:60-78] [GatherGoalDecomposer.cs:42-59] [agent-handoff-sprint26.md:14-57]

## Findings

### 1) Planning/docs are drifting from code, and some docs contradict each other
**Confidence: 97%**

The repo’s documentation is not internally consistent. `README.md` says `v0.28.0 — Sprint 33 complete — 276+ tests`, while `Data/Pages/roadmap.md` still says `Current version: v0.23.0 | Latest: Sprint 23 (2026-06-19)`. The sprint handoff docs then continue the inconsistency: sprint 25 says sprint 24 was entirely unstarted, sprint 26 says sprint 25 was post-implementation, and the later branch files show multiple items marked deferred in docs but already implemented in code. This is more than cosmetic drift; it increases the chance of duplicate work, stale assumptions, and wrong “done” judgments. [README.md:7-10, 122-170] [roadmap.md:3-8, 59-67] [agent-handoff-sprint25.md:19-24, 55-64] [agent-handoff-sprint26.md:12-57]

Why this matters:
- It weakens sprint planning reliability.
- It makes audits harder because the documents disagree with the implementation state.
- It increases the chance that a future contributor reintroduces already-fixed work.

Best fix:
- Pick one canonical status document.
- Make all sprint pages point to it instead of restating version history.
- Add a “implemented in code as of commit X” field to each task page so the planning layer cannot drift silently.

### 2) The Mineflayer adapter does not yet provide enough world representation for deeper reasoning
**Confidence: 90%**

The adapter currently sends useful but narrow observations. `sendBotStatus()` emits only position, health, food, and aggregated inventory counts. Chat events include `playerX/Y/Z`, and the adapter emits a few action-complete events, but it does not expose orientation, movement velocity, held item, armor/offhand, pathfinding state, nearby entities, block context, chunk-load status, biome/light, or inventory slot-level detail. That makes the world model cheaper to implement, but it also caps how much the agent can represent about the environment. [MineflayerAdapter/index.js:255-271] [MineflayerAdapter/index.js:332-347] [MineflayerAdapter/index.js:360-520]

High-value additional observations:
- Yaw/pitch and camera/look direction.
- Velocity, on-ground, swimming, in-water, in-lava state.
- Held item, selected hotbar slot, armor, offhand.
- Nearby hostile/neutral entities with distance and line-of-sight.
- Pathfinder goal type, remaining path length, path failure reason, and last nav target.
- Chunk load coverage around the bot, biome, brightness, weather, and time-of-day.
- Block underfoot, block in front, and the current support/facing face used for placement.
- Inventory slot-level contents, not just aggregate counts.

What this buys you:
- Better goal decomposition.
- Better failure recovery.
- Better world-model prediction/reconciliation.
- Fewer blind spots when the bot appears “stuck” but is actually path-blocked, underwater, or facing the wrong way.

Best seam:
- Introduce a typed observation payload or `IWorldObservationGateway`-style read-only stream, even if the command adapter stays separate. The existing roadmap already hints at that split. [architecture.md:45-62, 64-85] [agent-handoff-sprint24.md:213-215] [agent-handoff-sprint26.md:112-117]

### 3) The chat/system-message filter is still a regex firewall
**Confidence: 86%**

`MineflayerAdapter/index.js` still classifies server/system chat by matching a pattern list (`SYSTEM_MESSAGE_PATTERNS`) and checking for empty usernames. This is better than forwarding everything to the LLM, but it is still brittle by construction: any phrasing change on the server side can leak new noise into the chat pipeline or suppress a legitimate message. The repo’s own later sprint notes call structured message classification a deferred item, which means this is still an open architectural gap rather than a closed cleanup task. [MineflayerAdapter/index.js:288-303] [deep-code-audit-20260619.md:50-55] [agent-handoff-sprint26.md:119-123]

Why it matters:
- It affects representation quality directly.
- It can create false positives/negatives in the chat-to-goal pipeline.
- It increases maintenance cost whenever server wording changes.

Better direction:
- Classify by event source and semantic type when possible.
- Keep regexes only as a fallback boundary, not the primary classifier.
- Emit an explicit `messageKind` field so the C# side can reason about chat provenance without re-parsing text.

### 4) `HtnPlanner` still carries legacy decomposition branches that overlap with the router
**Confidence: 82%**

`PlannerRouter` now clearly owns the preferred selection seam: it checks `DecomposerRegistry` first and falls back to `HtnPlanner`. But `HtnPlanner` still contains direct type branches for `BuildGoal`, `CraftItemGoal`, `IItemSpecGoal`, and task-name fallback. That is acceptable as compatibility glue, but it means the planning code still has two overlapping decomposition stories. The sprint docs explicitly list planner routing consolidation as deferred work, so this is a known seam rather than an accidental bug. [PlannerRouter.cs:83-139] [HtnPlanner.cs:35-85] [agent-handoff-sprint26.md:107-111]

Why this matters:
- It increases cognitive load for anyone changing planning behavior.
- It makes the router look more complete than it really is.
- It can hide future regressions if a new goal is wired into one path but not the other.

Best architectural move:
- Make `PlannerRouter` the only routing decision point.
- Reduce `HtnPlanner` to a pure fallback decomposition engine.
- Once `CraftItemGoal` has a dedicated decomposer, remove the remaining typed branches from `HtnPlanner` and let the fallback path stay truly generic.

### 5) The Mineflayer adapter is too monolithic for the amount of responsibility it now owns
**Confidence: 78%**

`MineflayerAdapter/index.js` currently owns connection/auth, structured logging, emergency stop semantics, chat filtering, world scanning, movement, mining, placement, wandering, crafting, smelting, and event emission. That’s a lot of mixed responsibilities for one file, even if they are all “adapter-local.” This is the kind of shallow module that works early and becomes expensive later because every new behavior has to be threaded through the same file. [MineflayerAdapter/index.js:38-102] [MineflayerAdapter/index.js:145-240] [MineflayerAdapter/index.js:242-520]

Why this matters:
- It weakens locality.
- It makes deletion tests harder because the module has too many reasons to change.
- It obscures which pieces are truly core adapter semantics versus incidental utilities.

A better split would be:
- `connection/auth`
- `event translation`
- `chat classification`
- `navigation/pathfinding actions`
- `world scanning and geometry`
- `logging/telemetry helpers`

That would deepen the module boundary without changing the public protocol.

### 6) Action lifecycle is better, but the representation is still coarse for partial-progress actions
**Confidence: 74%**

The action-correlation layer is a good step, but the adapter and background service still report progress in a very coarse way for longer-running operations. Mining emits `blockMined` events and a final completion/failure-ish outcome, but the C# side currently uses those mostly as inventory deltas rather than explicit action-progress semantics. The same is true for movement/pathfinding: success, failure, and some position updates are emitted, but not the underlying route quality or why a path failed. [MineflayerAdapter/index.js:69-149] [WebUI.Blazor/AgentBackgroundService.cs:118-140, 178-196]

What more would help:
- path length, path node count, and whether a route was shortened by obstacles
- explicit path failure reason codes
- per-action “started / acknowledged / progress / completed / failed / timed out” metadata
- mining/crafting/smelting progress percentages or remaining work estimates
- placement reference-face selection and failure cause

This would make the world model and journal much more informative without forcing the agent to infer too much from final outcomes alone.

## Architecture and codebase-health opportunities

### A. Deepen the world-observation seam
The clearest next seam is the read-only world observation path. The repo already hints at `IWorldObservationGateway` as future work. That would let you keep command dispatch, event translation, and observation snapshots separate, which is exactly the kind of locality improvement that makes a modular system easier to extend. [agent-handoff-sprint24.md:213-215] [agent-handoff-sprint26.md:112-117] [turn617699view0]

### B. Replace “everything in one adapter file” with smaller modules
The adapter should be broken into narrow modules once the observation schema is defined. The deletion-test question to ask is: “Can we delete or replace this helper without touching unrelated transport logic?” If not, the helper probably belongs in its own module or seam. [turn617699view0]

### C. Make planner knowledge flow more explicit
Once the planner routing is fully consolidated, the system will be easier to reason about if each goal type has one obvious decomposition path. Right now, the code is already moving in that direction, but the remaining `HtnPlanner` branches keep the old shape alive. [PlannerRouter.cs:100-139] [HtnPlanner.cs:35-85]

### D. Keep the tool boundary strict, but do not stop at validation
The current tool dispatcher seam is good. The next step is to preserve that shape while making argument parsing more strongly typed per tool, so validation and parsing do not diverge. That is a codebase-health improvement, not a current blocker. [ToolDispatcher.cs:59-127, 129-204]

## What information I would ask the Mineflayer adapter to emit next

If the goal is better agent representation, the highest-value additions are:

- camera orientation: yaw, pitch, look vector
- motion: velocity, on-ground, swimming, in-water, in-lava, fall distance
- equipment: selected slot, held item, armor, offhand
- environment: chunk-load status, biome, light, weather, time-of-day
- local world shape: block underfoot, block in front, support blocks, nearby hazards, nearby entities
- planner context: current pathfinder goal, remaining route length, failure reason, retry count
- inventory detail: slot-level contents, not just aggregate counts
- action detail: start/progress/completion/failure/timed-out events with correlation IDs and reason codes

If you add only one thing, add **orientation + pathfinder state + nearby hazard/entity context**. That trio usually pays off immediately because it explains why movement, mining, or placement is failing even when position alone looks reasonable.

## Assumptions

- I audited the repository tip visible through GitHub tooling on the default branch (`main`), not a branch literally named `master`.
- The commit anchor I used for file review is the one surfaced in the file URLs returned by the GitHub connector.
- The sprint/task docs are part of the repo’s planning system, so inconsistencies between them and code are meaningful.
- `IWorldObservationGateway` is still a design note, not a current implementation requirement.
- The Mineflayer adapter is intended to remain the only Minecraft-specific world adapter for now.

## Open questions

1. Should the repository have one canonical planning/status page, or is historical drift acceptable?
2. Should the world model consume a read-only observation stream with typed fields, or keep inferring from coarse events?
3. Do you want the adapter to prioritize richer telemetry first, or pathfinding/path correction first?
4. Should `HtnPlanner` be kept as compatibility glue, or do you want to remove its typed branches once decomposers exist for the remaining goal types?
5. Is the journal meant to stay a bounded diagnostic buffer, or should it become closer to a durable event log?

## Confidence summary

| Finding | Confidence | Why |
|---|---:|---|
| Documentation / sprint drift | 97% | Direct contradictions between README, roadmap, and sprint handoff docs |
| Adapter observation payload too shallow | 90% | Current events expose only coarse bot state |
| Regex chat/system filter is brittle | 86% | Regex list still drives classification; repo defers structured classifier |
| Planner overlap remains | 82% | Router is centralized, but HtnPlanner still has legacy branches |
| Adapter monolith / weak locality | 78% | One JS file owns many unrelated responsibilities |
| Action progress telemetry is coarse | 74% | Lifecycle exists, but progress semantics remain thin |

## Source notes

Evidence was checked against:
- `README.md`
- `Data/Pages/roadmap.md`
- `Data/Pages/architecture.md`
- `Data/Pages/council/deep-code-audit-20260619.md`
- `Data/Pages/Tasks/agent-handoff-sprint24.md`
- `Data/Pages/Tasks/agent-handoff-sprint25.md`
- `Data/Pages/Tasks/agent-handoff-sprint26.md`
- `Agent.Tools/ToolDispatcher.cs`
- `Agent.Tools/Tools/FindFlatAreaTool.cs`
- `Agent.Planning/Decomposition/GatherGoalDecomposer.cs`
- `Agent.Planning/Router/PlannerRouter.cs`
- `Agent.Planning/HtnPlanner.cs`
- `Agent.World.Minecraft/MinecraftAdapter.cs`
- `MineflayerAdapter/index.js`
- `WebUI.Blazor/AgentBackgroundService.cs`

The architecture-skill guidance I used emphasizes finding the real seams, deepening modules, using deletion tests, and preferring locality over shallow duplication. [turn617699view0]
