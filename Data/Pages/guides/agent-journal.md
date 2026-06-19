# Agent Journal Guide

The `IAgentJournal` provides an append-only, bounded event log of everything the agent does. It's the primary observability surface for understanding agent behavior over time.

**Introduced:** Sprint 6

---

## Overview

The agent journal is a **ring buffer** of `JournalEntry` records. It holds the last 1000 entries and drops the oldest when the buffer is full. All significant agent events are appended automatically.

```
Every significant event → IAgentJournal.Append(entry)
                              ↓
                    AgentJournal (ConcurrentQueue-backed)
                              ↓
                    GET /api/agent/journal → last N entries
```

---

## Event Types

| Type | Trigger | Fields |
|---|---|---|
| `GoalSet` | `SetGoal` called | `GoalName`, `Parameters` |
| `GoalComplete` | `IsComplete` returns true | `GoalName`, `Elapsed` |
| `GoalFailed` | `HasFailed` returns true | `GoalName`, `FailureReason`, `Elapsed` |
| `GoalCancelled` | `CancelGoal` called | `GoalName` |
| `PlanCreated` | `PlanAsync` returns | `GoalName`, `ActionCount`, `Actions` |
| `ActionDispatched` | Before tool call | `ToolName`, `GoalName`, `Args` |
| `ActionCompleted` | After successful tool call | `ToolName`, `GoalName`, `Elapsed` |
| `ActionFailed` | After tool call failure | `ToolName`, `GoalName`, `FailureReason`, `Error` |
| `ReplanTriggered` | `ReplanAsync` starts | `GoalName`, `AttemptNumber`, `Reason` |
| `AgentStarted` | Hosted service `StartAsync` | — |
| `AgentStopped` | Hosted service `StopAsync` | — |
| `ToolValidationFailed` | Schema validation failure | `ToolName`, `ValidationError` |
| `ToolRegistrationFailed` | `RegisterTool` error | `ToolName`, `Error` |

---

## JournalEntry Record

```csharp
public record JournalEntry(
    JournalEventType EventType,
    string? GoalName,
    string? ToolName,
    string? Details,
    IReadOnlyDictionary<string, object>? Data,
    DateTimeOffset Timestamp
);
```

---

## IAgentJournal Interface

```csharp
public interface IAgentJournal
{
    void Append(JournalEntry entry);
    IReadOnlyList<JournalEntry> GetRecent(int count = 100);
    void Clear();
}
```

---

## AgentJournal Implementation

`AgentJournal` uses `ConcurrentQueue<JournalEntry>` internally:

- Thread-safe append via `ConcurrentQueue.Enqueue`
- Trim uses single-dequeue loop (Sprint 6 B1 fix — eliminates race condition)
- Clear uses `Interlocked.Exchange` for atomic replacement (Sprint 6 B2 fix)
- Maximum 1000 entries (oldest dropped when exceeded)

---

## DI Registration

```csharp
// Program.cs
builder.Services.AddSingleton<IAgentJournal>(new AgentJournal());
```

---

## REST API

```bash
# Get last 100 journal entries
curl http://localhost:5000/api/agent/journal

# Get last 20 entries
curl "http://localhost:5000/api/agent/journal?limit=20"

# Filter by event type
curl "http://localhost:5000/api/agent/journal?type=ActionFailed"
```

Example response:

```json
[
  {
    "type": "ActionCompleted",
    "toolName": "MineBlock",
    "goalName": "GatherItem:oak_log",
    "details": "mined 8 oak_log",
    "timestamp": "2026-06-19T14:00:05Z"
  },
  {
    "type": "ActionDispatched",
    "toolName": "MineBlock",
    "goalName": "GatherItem:oak_log",
    "details": "{block=oak_log, count=8}",
    "timestamp": "2026-06-19T13:59:58Z"
  }
]
```

---

## Call Sites

Journal entries are automatically appended at these locations:

**AgentBackgroundService (11 sites):**
- `GoalSet` — on `SetGoal`
- `GoalCancelled` — on `CancelGoal`
- `PlanCreated` — after `PlanAsync` succeeds
- `ActionDispatched` — before each tool call
- `ActionCompleted` — after successful tool call
- `ActionFailed` — after failed tool call
- `ReplanTriggered` — on replan
- `AgentStarted` — on `StartAsync`
- `AgentStopped` — on `StopAsync`
- `GoalComplete` — when `IsComplete` returns true
- `GoalFailed` — when `HasFailed` returns true

**ToolDispatcher (4 sites):**
- `ToolRegistrationFailed` — on `RegisterTool` error
- `ToolValidationFailed` — on schema validation failure
- `ActionCompleted` — on execution success
- `ActionFailed` — on execution failure

---

## Using the Journal in Tests

```csharp
var journal = new AgentJournal();
var sut = new AgentBackgroundService(/* ... */, journal);

await sut.SetGoalAsync(goal, CancellationToken.None);

var entries = journal.GetRecent(10);
Assert.That(entries, Has.Some.Matches<JournalEntry>(
    e => e.EventType == JournalEventType.GoalSet && e.GoalName == "GatherItem:oak_log"));
```
