# Dashboard Infrastructure Refactor

## Goal

Introduce a proper dashboard state model and event bus without breaking any existing APIs.

This sprint should be mostly additive.

---

# 1. Create Dashboard Contracts

## New File

```text
WebUI.Blazor/Dashboard/Contracts/DashboardSnapshot.cs
```

### Add

```csharp
namespace MemorySmith.Agent.WebUI.Dashboard.Contracts;

public sealed record DashboardSnapshot(
    AgentStatusSnapshot Status,
    GoalSnapshot? Goal,
    InventorySnapshot Inventory,
    QueueSnapshot Queue,
    IReadOnlyList<ChatMessageSnapshot> RecentChat,
    IReadOnlyList<JournalEntrySnapshot> RecentJournal,
    DateTimeOffset TimestampUtc);
```

---

## New File

```text
WebUI.Blazor/Dashboard/Contracts/AgentStatusSnapshot.cs
```

```csharp
public sealed record AgentStatusSnapshot(
    string State,
    double Health,
    double Food,
    PositionSnapshot Position,
    int ConsecutiveFailures);
```

---

## New File

```text
WebUI.Blazor/Dashboard/Contracts/PositionSnapshot.cs
```

```csharp
public sealed record PositionSnapshot(
    double X,
    double Y,
    double Z);
```

---

## New File

```text
WebUI.Blazor/Dashboard/Contracts/GoalSnapshot.cs
```

```csharp
public sealed record GoalSnapshot(
    string GoalName,
    string Description,
    DateTimeOffset StartedUtc);
```

---

## New File

```text
WebUI.Blazor/Dashboard/Contracts/InventorySnapshot.cs
```

```csharp
public sealed record InventorySnapshot(
    IReadOnlyDictionary<string,int> Items);
```

---

## New File

```text
WebUI.Blazor/Dashboard/Contracts/QueueSnapshot.cs
```

```csharp
public sealed record QueueSnapshot(
    int Count,
    IReadOnlyList<QueueActionSnapshot> Actions);
```

---

## New File

```text
WebUI.Blazor/Dashboard/Contracts/QueueActionSnapshot.cs
```

```csharp
public sealed record QueueActionSnapshot(
    string Tool,
    IReadOnlyDictionary<string,string> Arguments);
```

---

# 2. Dashboard Event Bus

Current dashboard updates are emitted directly from background service → hub.

That coupling will become painful.

---

## New File

```text
WebUI.Blazor/Dashboard/Services/IDashboardEventBus.cs
```

```csharp
public interface IDashboardEventBus
{
    ValueTask PublishAsync(
        DashboardEvent dashboardEvent,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<DashboardEvent> SubscribeAsync(
        CancellationToken cancellationToken = default);
}
```

---

## New File

```text
WebUI.Blazor/Dashboard/Services/DashboardEventBus.cs
```

```csharp
public sealed class DashboardEventBus : IDashboardEventBus
```

### Internal implementation

Use:

```csharp
Channel<DashboardEvent>
```

with:

```csharp
BoundedChannelOptions(5000)
{
    FullMode = BoundedChannelFullMode.DropOldest
}
```

---

## New File

```text
WebUI.Blazor/Dashboard/Contracts/DashboardEvent.cs
```

```csharp
public abstract record DashboardEvent;
```

---

## Add Derived Events

```csharp
public sealed record StatusChangedEvent(...)
public sealed record GoalChangedEvent(...)
public sealed record QueueChangedEvent(...)
public sealed record ChatReceivedEvent(...)
public sealed record JournalAppendedEvent(...)
```

---

# 3. Dashboard Snapshot Service

## New File

```text
WebUI.Blazor/Dashboard/Services/IDashboardSnapshotStore.cs
```

```csharp
public interface IDashboardSnapshotStore
{
    DashboardSnapshot Current { get; }

    void Update(
        DashboardEvent dashboardEvent);
}
```

---

## New File

```text
WebUI.Blazor/Dashboard/Services/DashboardSnapshotStore.cs
```

```csharp
public sealed class DashboardSnapshotStore
    : IDashboardSnapshotStore
```

Responsibilities:

* owns latest snapshot
* thread-safe
* immutable replacement updates

Use:

```csharp
Interlocked.Exchange(...)
```

for snapshot replacement.

---

# 4. Dashboard Broadcaster Service

Current:

```text
AgentBackgroundService
   -> HubContext
```

Direct dependency.

Remove.

---

## New File

```text
WebUI.Blazor/Dashboard/Services/DashboardBroadcastService.cs
```

```csharp
public sealed class DashboardBroadcastService
    : BackgroundService
```

### Constructor

```csharp
public DashboardBroadcastService(
    IDashboardEventBus eventBus,
    IDashboardSnapshotStore snapshotStore,
    IHubContext<AgentHub> hubContext,
    ILogger<DashboardBroadcastService> logger)
```

### Method

```csharp
protected override async Task ExecuteAsync(
    CancellationToken stoppingToken)
```

Behavior:

* consume bus
* batch events for 100ms
* publish latest snapshot

SignalR Event:

```csharp
Clients.Group("dashboard")
       .SendAsync(
           DashboardHubEvents.SnapshotUpdated,
           snapshot);
```

---

# 5. AgentBackgroundService Modifications

## File

```text
WebUI.Blazor/AgentBackgroundService.cs
```

Current code likely contains:

```csharp
PushStatusToDashboardAsync(...)
PushGoalToDashboardAsync(...)
```

Search and replace.

---

## Constructor Add

```csharp
IDashboardEventBus dashboardBus
```

Store field:

```csharp
private readonly IDashboardEventBus _dashboardBus;
```

---

## Replace

```csharp
await PushStatusToDashboardAsync(...)
```

with

```csharp
await _dashboardBus.PublishAsync(
    new StatusChangedEvent(...),
    cancellationToken);
```

---

## Replace

```csharp
await PushGoalToDashboardAsync(...)
```

with

```csharp
await _dashboardBus.PublishAsync(
    new GoalChangedEvent(...),
    cancellationToken);
```

---

## Replace

Queue updates.

Chat updates.

Journal updates.

Same pattern.

---

# 6. Dashboard Hub Event Constants

## New File

```text
WebUI.Blazor/Dashboard/DashboardHubEvents.cs
```

```csharp
public static class DashboardHubEvents
{
    public const string SnapshotUpdated = "SnapshotUpdated";

    public const string GoalUpdated = "GoalUpdated";

    public const string ChatReceived = "ChatReceived";
}
```

---

# 7. Fix Existing SignalR Event Drift

Current dashboard contains both:

```javascript
StatusUpdated
```

and

```javascript
StatusUpdate
```

The UI currently listens for both patterns in different sections.

---

## File

```text
WebUI.Blazor/wwwroot/index.html
```

Remove:

```javascript
initSignalR()
```

completely.

Keep:

```javascript
connectSignalR()
```

only.

---

Replace:

```javascript
StatusUpdated
StatusUpdate
GoalUpdate
```

with

```javascript
SnapshotUpdated
```

single source of truth.

---

# Sprint 5B: Runtime Configuration

---

# 8. Runtime Tuning Model

## New File

```text
WebUI.Blazor/Configuration/AgentRuntimeTuning.cs
```

```csharp
public sealed class AgentRuntimeTuning
{
    public int ReplanIntervalMs { get; set; }

    public int DamageInterruptCooldownMs { get; set; }

    public int PassiveHealthCheckCooldownMs { get; set; }

    public int ActionTimeoutSeconds { get; set; }

    public int StallWarningSeconds { get; set; }

    public int DashboardBatchWindowMs { get; set; }
}
```

---

# 9. Runtime Tuning Store

## New File

```text
WebUI.Blazor/Configuration/IAgentRuntimeTuningStore.cs
```

```csharp
public interface IAgentRuntimeTuningStore
{
    AgentRuntimeTuning Current { get; }

    Task UpdateAsync(
        AgentRuntimeTuning tuning,
        CancellationToken cancellationToken);
}
```

---

## New File

```text
WebUI.Blazor/Configuration/AgentRuntimeTuningStore.cs
```

Persist via:

```csharp
IOptionsMonitor
```

today.

SQLite later.

---

# 10. Program.cs

## File

```text
WebUI.Blazor/Program.cs
```

### Add Registrations

```csharp
builder.Services.AddSingleton<
    IDashboardEventBus,
    DashboardEventBus>();

builder.Services.AddSingleton<
    IDashboardSnapshotStore,
    DashboardSnapshotStore>();

builder.Services.AddHostedService<
    DashboardBroadcastService>();
```

---

### Add

```csharp
builder.Services.Configure<AgentRuntimeTuning>(
    builder.Configuration.GetSection("Agent:Runtime"));
```

---

### Add Endpoint

```csharp
app.MapGet(
    "/api/dashboard/bootstrap",
    ...
);
```

Returns:

```csharp
DashboardSnapshot
```

---

# Sprint 5C: Logging & Timeline

---

# 11. Structured Dashboard Log Buffer

Do NOT start with SQLite.

Start in memory.

---

## New File

```text
WebUI.Blazor/Dashboard/Logging/LiveLogBuffer.cs
```

```csharp
public sealed class LiveLogBuffer
```

Implementation:

```csharp
ConcurrentQueue<DashboardLogEntry>
```

bounded:

```csharp
1000 entries
```

---

Methods:

```csharp
void Add(...)
IReadOnlyList<DashboardLogEntry> GetLatest(...)
```

---

# 12. Dashboard Log Sink

## New File

```text
WebUI.Blazor/Dashboard/Logging/DashboardLogSink.cs
```

```csharp
public sealed class DashboardLogSink
    : ILogEventSink
```

Purpose:

Capture:

```text
Warning
Information
Error
```

only.

Avoid trace flood initially.

---

# 13. Dashboard Timeline Endpoint

## File

```text
WebUI.Blazor/Program.cs
```

Add:

```csharp
app.MapGet(
    "/api/dashboard/timeline",
    ...
);
```

Returns:

```csharp
Journal
+
LiveLogBuffer
```

merged chronologically.

---

# Sprint 5D: Future Spatial UI Support

Reserve architecture now.

Do not implement video yet.

---

## New File

```text
WebUI.Blazor/Dashboard/Contracts/ViewportSnapshot.cs
```

```csharp
public sealed record ViewportSnapshot(
    string Source,
    string Type,
    string Url);
```

---

Add to:

```csharp
DashboardSnapshot
```

```csharp
IReadOnlyList<ViewportSnapshot> Viewports
```

---

This patch set is intentionally incremental. It preserves the existing API surface and dashboard while moving the architecture toward a proper event-driven operational console. The most important changes are:

1. Remove `AgentBackgroundService -> HubContext` coupling.
2. Introduce `DashboardEventBus`.
3. Introduce immutable `DashboardSnapshot`.
4. Normalize SignalR events to a single `SnapshotUpdated`.
5. Add runtime-tunable operational parameters.
6. Add bounded buffering and batching before any SQLite work.

Those six changes provide most of the long-term architectural value while touching the fewest moving parts in the current codebase.
