# MemorySmith.Agent — Dashboard & Observability Reference

**Date:** 2026-07-10  
**Author:** Agent 8/10 (Memory Audit Sweep)  
**Confidence:** 95%

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [SignalR Hub (`/agent-hub`)](#2-signalr-hub-agent-hub)
3. [REST Endpoints (Complete API Surface)](#3-rest-endpoints)
4. [Dashboard Contracts (Typed DTOs)](#4-dashboard-contracts)
5. [LiveLogBuffer](#5-livelogbuffer)
6. [DashboardLogSink (Serilog Integration)](#6-dashboardlogsink)
7. [DashboardPublisherImpl](#7-dashboardpublisherimpl)
8. [AgentBackgroundService — Inline SignalR Push](#8-agentbackgroundservice--inline-signalr-push)
9. [Static HTML Dashboard (`wwwroot/index.html`)](#9-static-html-dashboard)
10. [Serilog Configuration](#10-serilog-configuration)
11. [Dual Write Surface](#11-dual-write-surface)
12. [Health Check Situation](#12-health-check-situation)
13. [Known Gaps & Hardcoded Values](#13-known-gaps--hardcoded-values)
14. [Pending Work for Future Waves](#14-pending-work-for-future-waves)
15. [File Paths Quick Reference](#15-file-paths-quick-reference)
16. [Tags](#16-tags)

---

## 1. Architecture Overview

The dashboard & observability subsystem lives entirely within the `WebUI.Blazor` project — the Blazor Server host process that also owns the agent loop (`AgentBackgroundService`). There is no separate queue, database, or broker. All components run in-process.

```
┌──────────────────────────────────────────────────────────────┐
│                  WebUI.Blazor (host process)                  │
│                                                              │
│  ┌──────────┐   ┌─────────────────┐   ┌──────────────────┐   │
│  │  Static   │   │  REST API       │   │  SignalR Hub     │   │
│  │  HTML     │◄──│  /api/agent/*   │   │  /agent-hub      │   │
│  │  Dashboard│   │  /api/dashboard/│   │  (AgentHub.cs)   │   │
│  │  index.html│   │  /api/goals     │   │                  │   │
│  └──────────┘   └─────────────────┘   └──────────────────┘   │
│                        │                      ▲              │
│                        ▼                      │              │
│  ┌──────────────────────────────────────────────────────┐    │
│  │           AgentBackgroundService (~3880 lines)        │    │
│  │  ┌──────────────────────┐  ┌──────────────────────┐  │    │
│  │  │ PushStatusToDashboard│  │ PushChatToDashboard  │  │    │
│  │  │ PushGoalToDashboard  │  │ (inline IHubContext)  │  │    │
│  │  └──────────────────────┘  └──────────────────────┘  │    │
│  │  ┌───────────────────────────────────────────────────┘    │
│  │  │ DashboardPublisherImpl (IDashboardPublisher)           │
│  │  │   → reads IStateManager → pushes via IHubContext      │
│  └──────────────────────────────────────────────────────────-┘
│                                                              │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  LiveLogBuffer (1000-entry ring buffer) ◄── Serilog  │    │
│  │  DashboardLogSink                                    │    │
│  └──────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────┘
```

**Key insight:** There is a **dual write surface** for dashboard status — ABS pushes inline via its own `PushStatusToDashboardAsync` (called on every event tick, disconnect, and reconnect), AND `DashboardPublisherImpl.PublishStatusAsync` exists via the `IDashboardPublisher` interface but is **not actively called** from the main agent loop yet (Sprint 39 P2 target deferred).

---

## 2. SignalR Hub (`/agent-hub`)

**File:** `WebUI.Blazor/AgentHub.cs`

### Hub Identity

| Property | Value |
|----------|-------|
| Hub class | `AgentHub` (sealed, extends `Hub`) |
| Namespace | `WebUI.Blazor` |
| Default route | `/agent-hub` (SignalR default convention — no explicit `MapHub` in `Program.cs`) |
| Group | `"dashboard"` — all clients join on connect |

### Connection Lifecycle

```csharp
public override async Task OnConnectedAsync()
{
    await Groups.AddToGroupAsync(Context.ConnectionId, "dashboard");
    await base.OnConnectedAsync();
}

public override async Task OnDisconnectedAsync(Exception? exception)
{
    await Groups.RemoveFromGroupAsync(Context.ConnectionId, "dashboard");
    await base.OnDisconnectedAsync(exception);
}
```

All dashboard push goes through `IHubContext<AgentHub>.Clients.Group("dashboard")`.

### Event Name Constants

**File:** `WebUI.Blazor/Dashboard/DashboardHubEvents.cs`

| Constant | String Value | Description |
|----------|-------------|-------------|
| `DashboardHubEvents.SnapshotUpdated` | `"SnapshotUpdated"` | Full state snapshot pushed after every event tick |
| `DashboardHubEvents.GoalUpdated` | `"GoalUpdated"` | Goal change notification (set/cancel) |
| `DashboardHubEvents.ChatReceived` | `"ChatReceived"` | New chat message (player or bot) |
| _(inline constant)_ | `"ChatMessage"` | Used by ABS inline `PushChatToDashboardAsync` — **not** in `DashboardHubEvents` |
| _(inline constant)_ | `"GoalUpdate"` | Used by ABS inline `PushGoalToDashboardAsync` — **not** in `DashboardHubEvents` |

**Note:** The inline ABS pushes use `"ChatMessage"` and `"GoalUpdate"` strings, while `DashboardHubEvents` defines `ChatReceived` and `GoalUpdated`. The JS client listens for `"ChatMessage"` and `"GoalUpdate"` — so the inline ABS sends and the JS client are aligned, but there is **drift** between `DashboardHubEvents` constants and actual wire events.

### Wire Event Contracts

All events are pushed to `Clients.Group("dashboard")`.

#### SnapshotUpdated

**Sender:** Both ABS inline (`PushStatusToDashboardAsync`) and `DashboardPublisherImpl.PublishStatusAsync`

**Payload type:** `AgentStatusUpdate` (record in `WebUI.Blazor/Dtos.cs`)

```json
{
  "status": "active|idle|reconnecting|disconnected",
  "goal": "string|null",
  "goalDescription": "string|null",
  "health": 0-20,
  "food": 0-20,
  "x": int,
  "y": int,
  "z": int,
  "queuedActions": int,
  "consecutiveFailures": int,
  "inventory": { "item_name": count },
  "nearbyEntities": [ { "name", "type", "hostile", "x", "y", "z", "distance", "health?", "username?" } ],
  "blockBelow": "string|null"
}
```

#### ChatMessage (inline ABS only)

**Sender:** ABS `PushChatToDashboardAsync`

**Payload:** Anonymous object `{ type: string, who: string|null, text: string }`

Types: `"player"`, `"bot"`, `"sys"`

**Note:** `DashboardHubEvents` defines `ChatReceived` but the actual wire uses `"ChatMessage"`. The JS handler listens for `"ChatMessage"`.

#### GoalUpdate (inline ABS only)

**Sender:** ABS `PushGoalToDashboardAsync`

**Payload:** Anonymous object `{ goal: string|null, description: string }`

**Note:** `DashboardHubEvents` defines `GoalUpdated` but the actual wire uses `"GoalUpdate"`. The JS handler listens for `"GoalUpdate"`.

### JS Client Binding

```javascript
// From wwwroot/index.html
signalRConn.on('StatusUpdated', update => { ... });       // Legacy — still handled
signalRConn.on('SnapshotUpdated', snapshot => { ... });   // Current
signalRConn.on('ChatMessage', msg => { ... });
signalRConn.on('GoalUpdate', g => { ... });
signalRConn.onreconnecting(() => { ... });
signalRConn.onclose(() => { ... });
```

---

## 3. REST Endpoints

All endpoints defined as minimal APIs in `WebUI.Blazor/Program.cs`. Guarded by `ApiKeyMiddleware` when `Agent:ApiKey` is configured.

### `/api/about`

| Method | Path | Response |
|--------|------|----------|
| GET | `/api/about` | `{ name, version, phase, license, repository, dashboard, registeredGoals }` |

### `/api/agent/*`

| Method | Path | Parameters | Response |
|--------|------|-----------|----------|
| GET | `/api/agent/status` | — | Full agent state: status, goal, health, food, position, currentAction, inventory, nearbyEntities, blockBelow, queuedActions, consecutiveFailures, uncertainty |
| POST | `/api/agent/plan` | `{ goalName, parameters? }` | Goal set result with plan phases, action count, first 5 actions |
| DELETE | `/api/agent/goal` | — | `{ status: "cancelled" }` |
| GET | `/api/agent/queue` | — | `{ count, actions: [{ tool, args }] }` |
| POST | `/api/agent/origin` | `{ blueprintId, x, y, z }` | `{ status: "origin set", blueprintId, x, y, z }` |
| POST | `/api/agent/chat` | `{ message }` | `{ status: "queued" }` |
| POST | `/api/agent/connect` | — | `{ status: "connected" }` (stub) |
| POST | `/api/agent/stop` | — | `{ status: "stopped" }` (stub) |
| POST | `/api/agent/command` | `{ command }` | Execute arbitrary tool by name |
| GET | `/api/agent/journal` | `limit=50, type?` | Journal entries as `JournalEntryDto[]` |
| GET | `/api/agent/worldmodel` | `detail=true` | WorldModel state (belief, observed, uncertainty) |
| GET | `/api/agent/resolve` | `q, types?, confidenceThreshold?, topN=5` | Knowledge resolution results |

### `/api/dashboard/*`

| Method | Path | Parameters | Response |
|--------|------|-----------|----------|
| GET | `/api/dashboard/logs` | `limit=100, level?, source?` | `{ count, returned, entries: DashboardLogEntry[] }` |
| GET | `/api/dashboard/timeline` | `limit=50` | Merged journal + log entries, newest-first: `{ count, entries }` |

### `/api/goals`

| Method | Path | Response |
|--------|------|----------|
| GET | `/api/goals` | `string[]` — list of registered goal names |

### `/api/blueprints`

| Method | Path | Response |
|--------|------|----------|
| GET | `/api/blueprints` | Hardcoded: `[{ id: "small-house", name: "Small Survival House", tags: ["house", "starter"] }]` |

### Request DTOs

```csharp
// From Program.cs inline record definitions
record PlanRequest(string GoalName, Dictionary<string, object>? Parameters);
record OriginRequest(string BlueprintId, int X, int Y, int Z);
record ChatRequest(string? Message);
record CommandRequest(string Command);
```

---

## 4. Dashboard Contracts

### Typed DTOs (`WebUI.Blazor/Dtos.cs`)

| Record | Fields | Purpose |
|--------|--------|---------|
| `AgentStatusUpdate` | Status, Goal, GoalDescription, Health, Food, X, Y, Z, QueuedActions, ConsecutiveFailures, Inventory, NearbyEntities?, BlockBelow? | SignalR payload for `SnapshotUpdated` |
| `ObservedEntityDto` | Name, Type, Hostile, X, Y, Z, Distance, Health?, Username? | Entity display in dashboard |
| `JournalEntryDto` | Timestamp (ISO 8601 "O"), Type, Summary, Details (string-keyed) | REST API journal output |

### Snapshot Contracts (`WebUI.Blazor/Dashboard/Contracts/`)

These are the **structured snapshot model** used internally. Not yet wired into production push paths — reserved for Sprint 5A+ broadcast service migration.

| Record | Fields | Purpose |
|--------|--------|---------|
| `DashboardSnapshot` | Status, Goal?, Inventory, Queue, RecentChat, RecentJournal, TimestampUtc | Composite full-state snapshot |
| `AgentStatusSnapshot` | State, Health, Food, Position, ConsecutiveFailures | Agent vitals subset |
| `GoalSnapshot` | GoalName, Description, StartedUtc | Goal metadata |
| `InventorySnapshot` | Items (dictionary) | Item counts |
| `QueueSnapshot` | Count, Actions (list) | Action queue |
| `QueueActionSnapshot` | Tool, Arguments (dictionary) | Single queued action |
| `ChatMessageSnapshot` | Type, Who?, Text, TimestampUtc | Chat message |
| `JournalEntrySnapshot` | TimestampUtc, EntryType, Summary | Lightweight journal entry |
| `PositionSnapshot` | X, Y, Z | Position coordinates |
| `ViewportSnapshot` | Source, Type, Url | **Reserved** for future spatial/video UI |

---

## 5. LiveLogBuffer

**File:** `WebUI.Blazor/Dashboard/Logging/LiveLogBuffer.cs`

| Property | Value |
|----------|-------|
| Type | `ConcurrentQueue<DashboardLogEntry>` ring buffer |
| Default capacity | 1000 entries |
| Registration | Singleton (`builder.Services.AddSingleton<LiveLogBuffer>()`) |
| Thread safety | Yes — `ConcurrentQueue` with dequeue loop when over capacity |

### Methods

| Method | Signature | Behavior |
|--------|-----------|----------|
| `Add` | `Add(DashboardLogEntry)` | Enqueues entry; drops oldest if at capacity |
| `GetLatest` | `GetLatest(int count = 100)` | Returns latest `count` entries, oldest-first (for auto-scroll UI) |
| `Clear` | `Clear()` | Drains all entries |
| `Count` | Property | Current entry count |

### DashboardLogEntry

**File:** `WebUI.Blazor/Dashboard/Logging/DashboardLogEntry.cs`

```csharp
public sealed record DashboardLogEntry(
    DateTimeOffset TimestampUtc,
    string Level,       // "Warning" | "Information" | "Error" | "Fatal"
    string Source,      // SourceContext (e.g. "WebUI.Blazor.AgentBackgroundService")
    string Message,
    string? Exception = null);
```

---

## 6. DashboardLogSink

**File:** `WebUI.Blazor/Dashboard/Logging/DashboardLogSink.cs`

| Property | Value |
|----------|-------|
| Base class | `Serilog.Core.ILogEventSink` |
| Captured levels | `Warning`, `Information`, `Error`, `Fatal` |
| Filtered levels | `Debug`, `Verbose` — silently dropped |
| Source extraction | From `logEvent.Properties["SourceContext"]` |

### Registration (in Serilog config lambda)

```csharp
var buffer = services.GetRequiredService<LiveLogBuffer>();
loggerConfig.WriteTo.Sink(new DashboardLogSink(buffer));
```

**Critical ordering:** `LiveLogBuffer` singleton is registered **before** the Serilog config lambda runs, so the sink can resolve it from the service provider.

---

## 7. DashboardPublisherImpl

**File:** `WebUI.Blazor/Managers/DashboardPublisherImpl.cs`  
**Interface:** `Agent.Core.Runtime.IDashboardPublisher` (in `IAgentRuntimeComponent.cs`)

### Interface

```csharp
public interface IDashboardPublisher : IAgentRuntimeComponent
{
    Task PublishStatusAsync(CancellationToken ct = default);
}
```

### Constructor Dependencies

| Dependency | Type | Source |
|-----------|------|--------|
| `_hubContext` | `IHubContext<AgentHub>?` | DI (null = skip) |
| `_stateManager` | `IStateManager` | DI (required) |
| `_logger` | `ILogger<DashboardPublisherImpl>` | DI |

### External State Setters

| Method | Sets | Called By |
|--------|------|-----------|
| `SetCurrentGoal(string?, string?)` | `_currentGoalName`, `_currentGoalDescription` | `AgentBackgroundService.SetGoal()` |
| `SetConsecutiveFailures(int)` | `_consecutiveFailures` | ABS after failure/success |
| `SetNearbyEntities(IReadOnlyList<ObservedEntityDto>?)` | `_nearbyEntities` | ABS entity observation (Sprint 55 Wave C) |
| `SetBlockBelow(string?)` | `_blockBelow` | ABS block-below observation (Sprint 55 Wave C) |

### PublishStatusAsync Flow

1. Guard: `if (_hubContext is null)` → log debug and return
2. Read `_stateManager.Current` (health, food, position, inventory)
3. Build `AgentStatusUpdate` with:
   - Status: `"active"` if both `AgentId` and goal set, else `"idle"`
   - `QueuedActions`: **hardcoded to 0** — Sprint 40+ deferred
   - `Inventory`: from `state.Inventory`
   - `NearbyEntities`: from `_nearbyEntities`
   - `BlockBelow`: from `_blockBelow`
4. Send via `_hubContext.Clients.Group("dashboard").SendAsync(DashboardHubEvents.SnapshotUpdated, update, ct)`
5. Exception handling: `OperationCanceledException` rethrown; other exceptions logged as warning.

### DI Registration (WebUI.Blazor/Program.cs)

```csharp
builder.Services.AddSingleton<IDashboardPublisher>(sp => new DashboardPublisherImpl(
    sp.GetService<IHubContext<AgentHub>>(),
    sp.GetRequiredService<IStateManager>(),
    sp.GetRequiredService<ILogger<DashboardPublisherImpl>>()));
```

### Known Gap: Not Called from Main Loop

`DashboardPublisherImpl` is registered and injected into `AgentRuntime`, but `PublishStatusAsync` is **not called** from the main agent loop. The `AgentRuntime` record is defined but its `TickAsync` is not wired — ABS still runs its own inline loop without calling `runtime.TickAsync()`. This means the `IDashboardPublisher` path is **dormant**.

---

## 8. AgentBackgroundService — Inline SignalR Push

**File:** `WebUI.Blazor/AgentBackgroundService.cs`

The ABS has its own direct SignalR push methods that bypass `DashboardPublisherImpl` entirely. These are the **active** dashboard push path.

### Three Inline Push Methods

#### PushStatusToDashboardAsync (line ~3528)

**When called:**

| Context | Line | Trigger |
|---------|------|---------|
| Reconnect attempt | 601 | `_connectionStatus = "reconnecting"` |
| Disconnect (max retries exhausted) | 668 | `_connectionStatus = "disconnected"` |
| Every event tick | 1120 | After `ProcessEventsAsync` processes a `WorldEvent` |

**Flow:**
1. Read `_worldState.Inventory`, `_worldState.Facts["nearbyEntitiesRaw"]`, `_worldState.Facts["blockBelow"]`
2. Build `AgentStatusUpdate` with real `_queue.Count` for `QueuedActions` (unlike DashboardPublisherImpl's hardcoded 0)
3. Send via `hubContext.Clients.Group("dashboard").SendAsync(DashboardHubEvents.SnapshotUpdated, update, ct)`
4. Exception → log debug (best-effort)

#### PushChatToDashboardAsync (line ~3576)

**When called (15 call sites in ABS):**

| Context | Line | Trigger |
|---------|------|---------|
| Player chat received | 1282 | `<Player> message` |
| Blocked command response | 1367 | Safety policy denied a command |
| Command dispatch confirmation | 1393 | `/command` dispatched |
| Split chat response chunks | 1542, 1670 | Long response split into multiple messages |

**Payload:** Anonymous `{ type, who, text }` — wire event name `"ChatMessage"`

#### PushGoalToDashboardAsync (line ~3590)

**When called (5 call sites):**

| Context | Line | Trigger |
|---------|------|---------|
| SetGoal completes | 382, 417 | New goal set |
| Sequence step advancement | 1698 | TaskSequenceGoal.TryAdvance |
| Goal completion (event path) | 1729 | TryCompleteCurrentGoalFromWorldUpdate |

**Payload:** Anonymous `{ goal, description }` — wire event name `"GoalUpdate"`

---

## 9. Static HTML Dashboard

**File:** `WebUI.Blazor/wwwroot/index.html`

### Technical Stack

| Component | Detail |
|-----------|--------|
| SignalR client | `@microsoft/signalr` 8.0.0 (CDN) |
| Styling | Hand-rolled CSS, dark theme (`#0d1117` GitHub-style) |
| Polling fallback | 2s status poll, 3s queue poll, 2s live log poll, 4s timeline poll |
| Version badge | `v0.55.1` (hardcoded in HTML) |

### Tabbed Views

#### Overview (default)

| Panel | Content |
|-------|---------|
| Set Goal | Dropdown of registered goals, dynamic params (item/count, gather count, blueprint) |
| Build Origin | Blueprint ID + X/Y/Z inputs, "Use Bot Pos" button |
| Agent Status | Health/food bars, position coords, block below, current action detail, queue count, failures, errors/warnings, uncertainty, position history trail (last 30 dots) |
| Inventory | 3-column grid, sorted by count descending |
| Action Queue | Tool + args display, max 30 visible |
| Nearby Entities | Sprint 55 Wave C — entity cards sorted by distance, hostile vs passive vs item-drop |
| Live Log | Persistent mini-log, last 50 entries, All/Warnings/Errors filter buttons, auto-scroll toggle, polls `/api/dashboard/logs` every 2s |
| Chat | Message log + input box, sends via `POST /api/agent/chat` |

#### Logs

| Feature | Detail |
|---------|--------|
| Level filter | Dropdown: All, Information, Warning, Error, Fatal |
| Source filter | Text input (contains match) |
| Refresh | Manual button + auto-poll every 3s |
| Display | 200 entries max, level-colored borders |

#### Timeline

| Feature | Detail |
|---------|--------|
| Data source | Merged journal + log entries |
| Sort | Newest-first |
| Refresh | Manual button + auto-poll every 4s |
| Display | 100 entries max, journal vs log styling |

### JavaScript Architecture

| Function | Purpose |
|----------|---------|
| `init()` | Load goals, refresh status, connect SignalR, start polling intervals, uptime counter |
| `connectSignalR()` | Build HubConnection, register 5 event handlers, auto-reconnect |
| `applyStatusUpdate(u)` | SignalR push handler — updates all UI panels |
| `applyStatus(s)` | HTTP poll handler — same rendering logic |
| `refreshStatus()` | HTTP GET `/api/agent/status` (skipped when SignalR connected) |
| `refreshQueue()` | HTTP GET `/api/agent/queue` every 3s (always polls) |
| `fetchLogs()` / `fetchTimeline()` | HTTP GET for log/timeline views |
| `startLiveLogPolling()` | HTTP GET `/api/dashboard/logs?limit=100` every 2s |
| `setGoal()` / `cancelGoal()` | POST / DELETE goal via REST API |
| `sendChat()` | POST /api/agent/chat |
| `setOrigin()` / `useCurrentPos()` | Build origin management |

### SignalR vs Polling Logic

- When SignalR is **connected**: status HTTP poll is skipped (`if (signalRConnected) return;` in `refreshStatus()`)
- Queue, logs, timeline, live log always **poll** regardless of SignalR state
- Error/warning badges are updated from live log poll data

---

## 10. Serilog Configuration

**File:** `WebUI.Blazor/Program.cs` (Serilog lambda in `builder.Host.UseSerilog`)

### Configuration Lambda

```csharp
builder.Host.UseSerilog((context, services, loggerConfig) =>
{
    // Level from config
    var defaultLevelName = loggingConfig.GetValue<string?>("Default") ?? "Information";
    Enum.TryParse<LogEventLevel>(defaultLevelName, true, out var configuredDefault);

    // Overrides from config
    foreach (var overrideEntry in loggingConfig.GetSection("Overrides").GetChildren())
        ...Enum.TryParse<LogEventLevel>(overrideEntry.Value, true, out var overrideLevel)
            => loggerConfig.MinimumLevel.Override(overrideEntry.Key, overrideLevel);

    // Hardcoded Microsoft overrides
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Extensions.Http", LogEventLevel.Warning)

    // Enricher
    .Enrich.FromLogContext()
```

### Sinks

| Sink | Output Template | Details |
|------|----------------|---------|
| Console | `[{Timestamp:HH:mm:ss}] {Message:lj}{NewLine}{Exception}` | Standard format, no level prefix |
| File | `[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}` | Rolling daily, 14-day retention, shared=true, restricted to Debug+ |
| DashboardLogSink | (in-memory) | Writes to LiveLogBuffer, Warning+ only |

### AppSettings Configuration

```json
{
  "Logging": {
    "Default": "Debug",
    "Microsoft.AspNetCore": "Warning",
    "System.Net.Http": "Warning"
  },
  "Agent": {
    "Logging": {
      "Default": "Debug",
      "Overrides": {
        "WebUI.Blazor.AgentBackgroundService": "Debug"
      }
    }
  }
}
```

### Request Logging Suppression

Dashboard polling endpoints (`/api/dashboard/*`, `/api/agent/queue`, `/api/agent/status`) are demoted to `Debug` level in `app.UseSerilogRequestLogging()` to prevent noise.

```csharp
app.UseSerilogRequestLogging(opts =>
{
    opts.GetLevel = (ctx, elapsed, ex) =>
    {
        if (ex is not null) return LogEventLevel.Error;
        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/api/dashboard/") ||
            path.Equals("/api/agent/queue") ||
            path.Equals("/api/agent/status"))
            return LogEventLevel.Debug;
        return LogEventLevel.Information;
    };
});
```

---

## 11. Dual Write Surface

There are **two parallel paths** for pushing status to the dashboard:

### Path A: ABS Inline (Active)

- **Methods:** `PushStatusToDashboardAsync`, `PushChatToDashboardAsync`, `PushGoalToDashboardAsync`
- **Hub injection:** Via constructor `IHubContext<AgentHub>?` (optional, null-safe)
- **Status source:** `_worldState` directly (real-time)
- **Chat source:** Direct call from event handlers
- **Goal source:** Direct call from SetGoal/CancelGoal/completion
- **QueuedActions:** Real `_queue.Count` (accurate)
- **Wire events:** `SnapshotUpdated`, `ChatMessage`, `GoalUpdate`

### Path B: DashboardPublisherImpl (Dormant)

- **Interface:** `IDashboardPublisher.PublishStatusAsync`
- **Hub injection:** Via constructor `IHubContext<AgentHub>?` (optional, null-safe)
- **Status source:** `IStateManager.Current` (indirect)
- **QueuedActions:** Hardcoded `0`
- **Wire events:** `SnapshotUpdated` (via `DashboardHubEvents.SnapshotUpdated`)
- **Called:** Registered in DI, injected into `AgentRuntime`, but `AgentRuntime.TickAsync` is **not wired** into the main loop

### Why Two Paths?

Sprint 39 P2 defined `AgentRuntime` as the target decomposition. The plan is for ABS to eventually delegate to `runtime.TickAsync()` which calls `IDashboardPublisher.PublishStatusAsync()` on each tick. Until that refactor is complete, ABS's inline methods remain the active path.

---

## 12. Health Check Situation

**There is no `/health` endpoint.**

The existing reference mentions `GET /api/agent/status/health` but this endpoint **does not exist** in `Program.cs`. The only health-adjacent endpoint is `GET /api/agent/status` which includes the agent's lifecycle state (`active`, `idle`, `disabled`).

The `/api/agent/connect` and `/api/agent/stop` endpoints exist as stubs returning fixed responses.

---

## 13. Known Gaps & Hardcoded Values

### DashboardPublisherImpl

| Issue | Detail | File:Line |
|-------|--------|-----------|
| `QueuedActions` hardcoded to 0 | `QueuedActions: 0,` — comment says "Sprint 40+: wire from ActionQueue" | `Managers/DashboardPublisherImpl.cs:99` |
| `OnlinePlayers` not tracked | Status has no player count field | — |
| `Blueprints` hardcoded | `/api/blueprints` returns fixed `[{ id: "small-house" }]` | `Program.cs:647` |
| No `SetOnlinePlayers` method | ABS tracks `OnlinePlayers` from `ChatEvent` but publisher has no corresponding setter | — |

### SignalR Wire Drift

| DashboardHubEvents Constant | Actual Wire Event Used | JS Listens For |
|----------------------------|----------------------|----------------|
| `ChatReceived` | `"ChatMessage"` (inline ABS) | `"ChatMessage"` |
| `GoalUpdated` | `"GoalUpdate"` (inline ABS) | `"GoalUpdate"` |
| `SnapshotUpdated` | `"SnapshotUpdated"` ✅ | `"SnapshotUpdated"` + legacy `"StatusUpdated"` |

### REST Endpoints

| Issue | Detail | File |
|-------|--------|------|
| No `/api/agent/status/health` | Referenced in old docs but doesn't exist | — |
| `/api/agent/connect` stub | Returns fixed `{ Status: "connected" }` | `Program.cs:648` |
| `/api/agent/stop` stub | Returns fixed `{ Status: "stopped" }` | `Program.cs:649` |
| `/api/blueprints` hardcoded | Single entry, not backed by runtime blueprint registry | `Program.cs:647` |
| `/api/tools` missing | Registered goals are listed but registered tools are not exposed | — |

### Dashboard UI

| Issue | Detail | File |
|-------|--------|------|
| Version hardcoded as `v0.55.1` | Stale — Program.cs says `v0.55.0` | `index.html` |
| Blueprint dropdown hardcoded | Only `"small-house"` option | `index.html` |
| No loading states | Empty states use static placeholder text | `index.html` |
| No auto-retry on SignalR failure | Falls to polling silently | `index.html` |

---

## 14. Pending Work for Future Waves

### Sprint 5A / Broadcast Service Migration

As documented in `Data/Pages/Tasks/blazor-dashboard-improvement-v2.md`, the planned architecture replaces the dual write surface with:

1. **DashboardEventBus** — in-memory channel for dashboard events
2. **DashboardSnapshotStore** — holds latest composite snapshot
3. **DashboardBroadcastService** — `IHostedService` that periodically reads snapshot store + LiveLogBuffer and pushes merged updates via SignalR

This would replace both the ABS inline pushes and the `DashboardPublisherImpl` with a single, polled broadcast service.

### Planned Endpoints (Not Yet Implemented)

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/dashboard/bootstrap` | GET | Full `DashboardSnapshot` (composite) |
| `/api/dashboard/timeline` | GET | Already implemented |
| `/api/tools` | GET | List registered tools with schemas |

### Deferred Tasks

| Task | Title | Priority | Status |
|------|-------|----------|--------|
| TSK-0047 | Program.cs DI wiring + DashboardSnapshot endpoints | Med | Drafted in task file |
| — | Wire IDashboardPublisher.PublishStatusAsync into main loop | Med | Blocked on AgentRuntime.TickAsync |
| — | Resolve SignalR wire event name drift (ChatReceived vs ChatMessage) | Low | Cosmetic |
| — | Fix QueuedActions hardcoded to 0 in DashboardPublisherImpl | Low | One-liner |

---

## 15. File Paths Quick Reference

| File | Purpose |
|------|---------|
| `WebUI.Blazor/AgentHub.cs` | SignalR hub, group management, 25 lines |
| `WebUI.Blazor/AgentBackgroundService.cs` | ~3880 lines — inline dashboard push (3 methods, ~22 call sites) |
| `WebUI.Blazor/Program.cs` | DI registration, Serilog config, all REST endpoints |
| `WebUI.Blazor/Dtos.cs` | `AgentStatusUpdate`, `ObservedEntityDto`, `JournalEntryDto` |
| `WebUI.Blazor/Dashboard/DashboardHubEvents.cs` | Event name constants (3) |
| `WebUI.Blazor/Dashboard/Contracts/*.cs` | 10 snapshot record types (reserved for future) |
| `WebUI.Blazor/Dashboard/Logging/LiveLogBuffer.cs` | 1000-entry ring buffer |
| `WebUI.Blazor/Dashboard/Logging/DashboardLogSink.cs` | Serilog sink → LiveLogBuffer |
| `WebUI.Blazor/Dashboard/Logging/DashboardLogEntry.cs` | Log entry record |
| `WebUI.Blazor/Managers/DashboardPublisherImpl.cs` | IDashboardPublisher impl (dormant) |
| `WebUI.Blazor/wwwroot/index.html` | Static HTML dashboard (~1100 lines) |
| `WebUI.Blazor/wwwroot/about.html` | About page |
| `WebUI.Blazor/appsettings.json` | Logging config, Agent settings |
| `WebUI.Blazor/ApiKeyMiddleware.cs` | API key guard for `/api/*` |
| `Agent.Core/Runtime/IAgentRuntimeComponent.cs` | `IDashboardPublisher` interface (line 137) |
| `Agent.Core/Runtime/AgentRuntime.cs` | Composite runtime record (not wired) |

---

## 16. Tags

```
dashboard, observability, signalr, real-time, monitoring, serilog,
livelogbuffer, dashboardpublisher, rest-api, agent-hub, static-html,
blazor, logging, telemetry, health-check, agent-background-service,
sprint-49, sprint-50, sprint-55, kind:reference, audience:agent,
scope:dashboard, scope:observability, agent-8
```
