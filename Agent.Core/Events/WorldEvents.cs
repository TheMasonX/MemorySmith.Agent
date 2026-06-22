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

public sealed record BlockMinedEvent(string Block, int Count, Position Pos, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

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
/// </summary>
public sealed record MineCompleteEvent(
    string Block,
    int Mined,
    int TargetCount,
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

public sealed record ErrorEvent(string Action, string Message, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

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

public sealed record BlockPlacedEvent(int X, int Y, int Z, string Block, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

public sealed record WanderCompleteEvent(Position Pos, int TargetX, int TargetZ, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

public sealed record WanderFailedEvent(string Message, Position Pos, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

public sealed record KickedEvent(string Reason, DateTimeOffset Timestamp)
    : WorldEvent(Timestamp);

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
