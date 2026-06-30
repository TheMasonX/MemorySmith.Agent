namespace Agent.Core;

/// <summary>
/// Typed world event hierarchy — replaces the legacy
/// <c>WorldEvent(string, Dictionary&lt;string, object?&gt;, DateTimeOffset)</c> record.
///
/// Sprint 3a: Every event from the world adapter is now a sealed record with
/// named, typed properties instead of an opaque string-keyed dictionary.
/// Consumers use pattern matching instead of string switches and TryGetValue calls.
///
/// Sprint 23 P0-A: Added <see cref="DamageTakenEvent"/> — a synthetic event computed
/// C#-side from consecutive HealthEvent health-delta comparisons. Not received from
/// the Node.js wire; created by AgentBackgroundService and applied through the projector.
///
/// Sprint 35 P0-A: Added <see cref="ItemCollectedEvent"/> — fired by the Mineflayer
/// playerCollect event, providing the actual dropped item name (not the block name).
/// This fixes BUG-1: mining diamond_ore now yields "diamond" in inventory, not "diamond_ore".
///
/// Sprint 35 P0-B: Added <see cref="MineCompleteEvent"/> — fired at the end of the
/// mine action loop to signal completion and carry the correlationId for lifecycle tracking.
///
/// Sprint 35 P2-A stubs: <see cref="ItemCraftedEvent"/> and <see cref="ItemConsumedEvent"/>
/// are defined but not yet wired in WorldStateProjector. Full wiring in Sprint 36.
/// </summary>
public abstract record WorldEvent(DateTimeOffset Timestamp);

public sealed record SpawnEvent(Position Pos, int Health, int Food, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

/// <summary>
/// Emitted by the Node.js adapter on <em>any</em> health change — damage taken
/// <em>and</em> healing (eating food, regeneration, natural healing).
/// <para>
/// Routes that only care about damage should subscribe to
/// <see cref="DamageTakenEvent"/> instead, which is synthesized C#-side from
/// consecutive HealthEvent comparisons and only fires when health drops.
/// </para>
/// </summary>
public sealed record HealthEvent(int Health, int Food, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

public sealed record GameModeChangedEvent(string Mode, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

/// <summary>
/// Synthesized C#-side (by AgentBackgroundService) from consecutive
/// <see cref="HealthEvent"/> comparisons whenever the new Health is lower than
/// the previous Health. NOT received from the Node.js wire.
/// <para>
/// <c>Delta = Health - PreviousHealth</c> — always <strong>negative</strong>
/// (magnitude of HP lost). Healing/eating does <em>not</em> produce this event.
/// </para>
/// <para>
/// Sprint 23 P0-A: drives the damage interrupt path. When |Delta| meets or
/// exceeds the active goal's <see cref="IGoal.DamageInterruptThresholdHp"/>
/// (or system default 6 HP when goal returns null), the agent atomically clears
/// the action queue and enqueues a priority GetStatus.
/// </para>
/// </summary>
public sealed record DamageTakenEvent(
    int PreviousHealth,
    int Health,
    int Delta,
    int Food,
    DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

/// <summary>Emitted for both "move" and "moveComplete" wire events.</summary>
public sealed record MoveEvent(Position Pos, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

/// <summary>
/// Sprint 40 P0-B: Added <see cref="BlockPosition"/> — the actual coordinates of the
/// mined block (where the block was in the world, not where the bot was standing).
/// Previously only <see cref="Pos"/> (bot position) was available, making it impossible
/// to distinguish "bot at (-241,65,162) mined block at (-241,64,162)".
/// </summary>
public sealed record BlockMinedEvent(
    string Block,
    int Count,
    Position Pos,
    Position BlockPosition,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

/// <summary>
/// Sprint 35 P0-A: Emitted when the bot collects an item entity (Mineflayer playerCollect event).
/// Provides the TRUE item name of the drop — e.g. mining "diamond_ore" produces
/// ItemCollectedEvent(Item="diamond", Count=1), not "diamond_ore".
/// Guard: only fires when collector.username === bot.username (own collections only).
/// WorldStateProjector.ApplyItemCollected uses this as the authoritative inventory source.
/// Periodic GetStatus reconciles any drift.
/// </summary>
public sealed record ItemCollectedEvent(
    string Item,
    int Count,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

/// <summary>
/// Sprint 35 P0-B: Emitted at the end of the mine action loop in Mineflayer.
/// Provides a definitive "mining is done" signal with final counts.
/// Consumed by AgentBackgroundService to transition correlated actions to Completed state.
/// Sprint 40 P0-B: Added <see cref="BlockPosition"/> — position of the LAST mined block.
/// </summary>
public sealed record MineCompleteEvent(
    string Block,
    int Mined,
    int TargetCount,
    Position BlockPosition,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

/// <summary>
/// Sprint 35 P2-A stub: Item crafted at table or furnace.
/// Full wiring in Sprint 36 (ItemCraftedEvent → WorldStateProjector.ApplyItemCrafted).
/// </summary>
public sealed record ItemCraftedEvent(
    string Item,
    int Count,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

/// <summary>
/// Sprint 35 P2-A stub: Item consumed (placed, eaten, used as ingredient).
/// Full wiring in Sprint 36.
/// </summary>
public sealed record ItemConsumedEvent(
    string Item,
    int Count,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

public sealed record ChatEvent(
    string Username,
    string Message,
    int OnlinePlayers,
    Position? PlayerPos,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

/// <summary>
/// Sprint 41: added optional position fields (X, Y, Z, Block, Item, Material) so
/// the JS adapter can include the exact context of a failed action (e.g. which block
/// position a PlaceBlock was targeting). Previously only Action and Message were sent,
/// making it impossible to trace which specific operation failed.
///
/// Sprint 55 (TSK-0165): added <see cref="ReasonCode"/> — machine-readable error
/// classification from the JS adapter's classifyError function. Enables the LLM
/// evaluator and recovery system to make structured decisions without parsing
/// free-form error strings.
/// </summary>
public sealed record ErrorEvent(
    string Action,
    string Message,
    DateTimeOffset Timestamp,
    int? X = null,
    int? Y = null,
    int? Z = null,
    string? Block = null,
    string? Material = null,
    string? Item = null,
    string? ReasonCode = null) : WorldEvent(Timestamp);

/// <summary>
/// Emitted when the bot cannot find a requested block within its search radius.
/// <c>MinedCount</c> is the number of blocks successfully mined before the miss.
/// </summary>
public sealed record BlockNotFoundEvent(string Block, int MinedCount, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

public sealed record CraftCompleteEvent(string Item, int Count, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

public sealed record SmeltCompleteEvent(
    string Input, string Result, int Count, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

public sealed record DeathEvent(Position Pos, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

public sealed record StatusEvent(
    Position Pos,
    int Health,
    int Food,
    IReadOnlyDictionary<string, int> Inventory,
    string? GameMode,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

public sealed record BlockPlacedEvent(int X, int Y, int Z, string Block, DateTimeOffset Timestamp, Guid? CorrelationId = null)
    : WorldEvent(Timestamp);

/// <summary>
/// Sprint 43 (P0-4): Emitted by the adapter when a PlaceBlock target position is occupied
/// by a different block (terrain collision). Completes the correlation so the tool loop
/// continues, but does NOT advance the build checkpoint — preventing permanent holes in
/// the structure. The planner retries the position on the next cycle.
/// </summary>
public sealed record BlockPlaceSkippedEvent(
    int X, int Y, int Z, string Block, string ExistingBlock, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

public sealed record WanderCompleteEvent(Position Pos, int TargetX, int TargetZ, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

public sealed record WanderFailedEvent(string Message, Position Pos, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

public sealed record KickedEvent(string Reason, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

/// <summary>
/// Sprint 40 P0-C: Emitted when the mine action loop is aborted by a stop signal.
/// Carries partial progress information so the C# side knows how many blocks were
/// mined before the stop signal arrived.
/// </summary>
public sealed record MineAbortedEvent(
    string Block,
    int Mined,
    int TargetCount,
    Position? BlockPosition,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

/// <summary>
/// Sprint 40 P0-C: Emitted by the adapter when handleStop() completes.
/// Acknowledges that the emergency stop has been fully processed (pathfinder
/// cleared, command queue drained, in-progress loops exited).
/// </summary>
public sealed record StopCompleteEvent(DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

/// <summary>
/// Sprint 40 P0-B: Emitted when Mineflayer's <c>findReachableBlock</c> action finds
/// a block that the bot can pathfind to. Contains position and distance metrics so
/// the planner can make informed decisions about which block to target.
/// </summary>
public sealed record ReachableBlockFoundEvent(
    string Block,
    int X,
    int Y,
    int Z,
    double EuclideanDistance,
    int PathDistance,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

/// <summary>
/// Result of a findFlatArea tool dispatch. Contains the best candidate's center
/// coordinates, total flat area in blocks, and bounding box extents.
/// <c>Area</c> is 0 when no suitable flat region was found.
/// <para>
/// Sprint 35 P0-C: Added <see cref="SearchedRadius"/> so the C# side can distinguish
/// "searched small area, retry with larger radius" from "searched maximum radius, no flat ground".
/// BuildGoalDecomposer gates retry on SearchedRadius &lt; 48.
/// </para>
/// </summary>
public sealed record FlatAreaFoundEvent(
    int X, int Y, int Z,
    int Area,
    int MinX, int MaxX, int MinZ, int MaxZ,
    int SearchedRadius,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

// ── Sprint 55 (TSK-0165): Action lifecycle telemetry ─────────────────────────

/// <summary>
/// Emitted at the start of every action dispatched to the Mineflayer adapter.
/// Enables C#-side tracking of action lifecycle (started → progress → completed/failed).
/// Distinct from the fire-and-forget dispatch timestamp — this confirms the JS
/// adapter has received and begun processing the action.
/// </summary>
public sealed record ActionStartedEvent(
    string Action,
    string? CorrelationId,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

/// <summary>
/// Emitted periodically during long-running actions (mining, smelting) to report
/// progress before completion. Allows the dashboard and LLM evaluator to see
/// mid-action state rather than waiting for the final outcome.
/// </summary>
public sealed record ActionProgressEvent(
    string Action,
    int Completed,
    int TargetCount,
    int PercentComplete,
    string? CorrelationId,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

/// <summary>
/// Emitted when an action fails with a machine-readable reason code.
/// Enables the C# evaluator to make structured decisions about failures
/// (path_timeout, no_block_found, missing_item, etc.) instead of parsing
/// free-form error strings from the generic ErrorEvent.
/// </summary>
public sealed record ActionFailedEvent(
    string Action,
    string ReasonCode,
    string Detail,
    string? CorrelationId,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

/// <summary>
/// Emitted when an action completes successfully. Provides a generic
/// completion signal for actions that don't have a dedicated completion
/// event type (e.g., smelt, craft). Complements action-specific completion
/// events like MineCompleteEvent, CraftCompleteEvent, and BlockPlacedEvent.
/// </summary>
public sealed record ActionCompletedEvent(
    string Action,
    string? CorrelationId,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

// ── Sprint 55 Wave B: Environment query events ────────────────────────────

/// <summary>
/// Result of a block query (single position or bounding box).
/// Returns the list of blocks found with their positions, Minecraft names, and type IDs.
/// Empty blocks list means air/void at all queried positions.
/// </summary>
public sealed record BlocksQueriedEvent(
    IReadOnlyList<QueriedBlock> Blocks,
    Position From,
    Position To,
    string? CorrelationId,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

/// <summary>A single block result from a queryBlocks action.</summary>
public sealed record QueriedBlock(
    int X, int Y, int Z,
    string Name,
    int Type);

/// <summary>
/// Result of an entity query. Returns entities within the specified radius
/// of the bot's position, sorted by distance.
/// </summary>
public sealed record EntitiesQueriedEvent(
    IReadOnlyList<ObservedEntity> Entities,
    int Radius,
    string? EntityTypeFilter,
    Position BotPosition,
    string? CorrelationId,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

/// <summary>
/// Passively observed entities near the bot. Emitted periodically
/// (cooldown ~3s) when mobs are within ENTITY_SCAN_RADIUS blocks.
/// Includes both hostile and non-hostile mobs; the <see cref="ObservedEntity.Hostile"/>
/// flag distinguishes threat level.
/// Feeds into the observe→evaluate replan loop for threat detection.
/// </summary>
public sealed record EntityObservedEvent(
    IReadOnlyList<ObservedEntity> Entities,
    Position BotPosition,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);

/// <summary>A single entity observed by queryEntities or entityObserved.</summary>
public sealed record ObservedEntity(
    string Name,
    string Type,
    bool Hostile,
    int X, int Y, int Z,
    double Distance,
    int? Health = null,
    string? Username = null);

// ── Sprint 55 Wave C: Passive block observation ───────────────────────────

/// <summary>
/// Emitted when the block below the bot's feet changes (e.g. walking from
/// grass to stone). Includes the block name and its position.
/// </summary>
public sealed record BlockBelowChangedEvent(
    string Name,
    int Type,
    int X, int Y, int Z,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);
