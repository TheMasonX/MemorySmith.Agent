# Mineflayer Data & Hook Surface Research for MemorySmith.Agent
## A source-grounded paper and adapter implementation plan

**Scope:** Mineflayer core docs and official PrismarineJS plugin documentation for `mineflayer-pathfinder`, `mineflayer-collectblock`, and `mineflayer-tool`.

### Abstract

Mineflayer already exposes a broad enough observation surface to support a significantly more adaptive Minecraft agent than MemorySmith.Agent currently uses. The most valuable signals are not raw packet-level noise, but typed facts about the bot’s world, body, inventory, pathing, tool state, and environmental context. The right design is to normalize Mineflayer events into a compact adapter event model, project those events into canonical world state, and then feed those observations into the LLM for intent evaluation, recovery, and replanning.

### 1. Thesis

The adapter should not try to “understand” the game. It should observe, normalize, and forward structured facts. Mineflayer core and its official plugins already provide enough hooks to support that design.

The practical goal is a three-layer separation:

1. The Mineflayer adapter emits typed observations.
2. The world-state projector reduces those observations into canonical state.
3. The LLM evaluates those observations and decides whether to continue, recover, clarify, or replan.

### 2. Core Mineflayer surface worth exposing

Mineflayer’s core API and docs expose the following categories of signals:

- world and block state, including `bot.blockAt`, `bot.findBlock`, `bot.findBlocks`, `bot.canSeeBlock`, `bot.blockAtCursor`, `bot.blockAtEntityCursor`, and block update events;
- entity and player state, including `bot.players`, `bot.entities`, `entitySpawn`, `entityMove`, `entityGone`, `entityHurt`, `entityDead`, `playerJoined`, `playerUpdated`, `playerLeft`, and `playerCollect`;
- inventory and equipment, including inventory window state, `heldItemChanged`, update-slot events, `equip`, `unequip`, `toss`, and container interactions;
- body and environment state, including `health`, `food`, `breath`, `experience`, `weatherUpdate`, `time`, `game`, `spawnReset`, `death`, `kicked`, `end`, and `error`;
- sensory and UI-ish state, including `chat`, `whisper`, `chatPatterns`, `windowOpen`, `windowClose`, title events, scoreboard/boss-bar events, and particles;
- movement and physics, including `move`, `forcedMove`, `physicsTick`, control-state helpers, and the pathfinder plugin’s richer movement lifecycle.

### 3. Plugin surfaces that are especially valuable

#### mineflayer-pathfinder
This plugin should be treated as a first-class source of planning telemetry. Its key contributions are:

- `goal_reached`
- `path_update`
- `goal_updated`
- `path_reset`
- `path_stop`
- `bot.pathfinder.isMoving()`
- `bot.pathfinder.isMining()`
- `bot.pathfinder.isBuilding()`

It also exposes movement configuration, including flags such as `canDig`, `placeCost`, `digCost`, `allowParkour`, and `searchRadius`. That is valuable because the agent can reason about why a path failed rather than only detecting that it failed.

#### mineflayer-collectblock
This plugin is a higher-level workflow wrapper for pathfinding, tool selection, mining, and pickup. It is not a replacement for direct observations, but it does show a useful abstraction boundary: the bot can be asked to collect a block or item drop, while the adapter still receives lower-level events and updates.

#### mineflayer-tool
This plugin handles tool selection, which makes it useful as a support surface for mining and harvesting workflows. Tool choice is a meaningful observation in itself because wrong or missing tools are a common cause of failure.

### 4. What MemorySmith should expose through the adapter

The adapter should translate Mineflayer signals into a small, typed observation vocabulary. The best version is not “emit everything.” It is “emit the right things.”

Recommended observation types:

- connection and lifecycle changes
- position and movement changes
- health, food, breath, and experience changes
- inventory deltas and pickup events
- held item and equipment changes
- block mined, placed, and updated events
- entity and player appearance/disappearance/movement
- pathfinding status and reset reasons
- visibility and reachability facts
- window/container/crafting/furnace state
- chat, whisper, and chat-pattern matches
- weather, time, dimension, scoreboard, and boss-bar changes
- error, kick, death, spawn, and teleport/forced-move events

### 5. Why the current project needs this

MemorySmith.Agent already has a fairly strict tool-validation boundary, but the current world picture is still too shallow in several places. The most obvious examples are inventory freshness, mining reliability, and fallback-heavy intent handling.

The LLM cannot evaluate a plan well if it is only receiving periodic snapshots. It needs a stream of observations that explain what just happened. That is the bridge between tool execution and replanning.

### 6. Recommended adapter implementation plan

#### Phase 1: Normalize Mineflayer events
Add a single adapter event layer that listens to the core Mineflayer events and selected plugin events. Normalize them into typed internal events.

#### Phase 2: Project typed observations into world state
Use the existing projector pattern to turn those adapter events into authoritative world facts. Prefer pickup events and inventory-slot updates over mining inference when actual item acquisition is known.

#### Phase 3: Summarize observations for the LLM
Build compact observation summaries that include:
- what changed
- whether the bot is moving, mining, building, or idle
- whether inventory changed
- whether pathfinding was reset and why
- whether the target was reached, broken, placed, or collected
- whether environmental context changed

#### Phase 4: Use observations for replanning
Feed the summaries into the LLM evaluator. The LLM should decide whether to:
- continue the current plan
- switch tools
- choose a new target
- rescan terrain
- ask for clarification
- or enter recovery mode

#### Phase 5: Add optional plugin-based workflows
If the project wants to simplify specific workflows, layer in `mineflayer-collectblock` and `mineflayer-tool` as supportive abstractions, while keeping the adapter responsible for normalized observations.

### 7. Risks

- Overexposing raw events and creating an unmanageable reasoning surface.
- Treating helper methods like `findBlock` or `canDigBlock` as truth instead of as planning hints.
- Duplicating responsibility between the adapter and the world-state projector.
- Letting pathfinding plugins become hidden sources of behavior rather than visible sources of evidence.

### 8. Conclusion

Mineflayer already provides enough data to make MemorySmith.Agent substantially more adaptive. The missing piece is not data volume. It is disciplined projection: a small set of typed observations that feed a world-state projector and an LLM evaluator. That architecture will improve mining, building, inventory accuracy, failure recovery, and future observation-driven replanning.

### References

- PrismarineJS mineflayer repository and API docs
- PrismarineJS mineflayer-pathfinder repository and README
- PrismarineJS mineflayer-collectblock repository and README
- PrismarineJS mineflayer-tool repository and README
