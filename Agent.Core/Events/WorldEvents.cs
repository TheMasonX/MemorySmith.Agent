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
/// </summary>
public sealed record FlatAreaFoundEvent(
    int X, int Y, int Z,
    int Area,
    int MinX, int MaxX, int MinZ, int MaxZ,
    DateTimeOffset Timestamp) : WorldEvent(Timestamp);
