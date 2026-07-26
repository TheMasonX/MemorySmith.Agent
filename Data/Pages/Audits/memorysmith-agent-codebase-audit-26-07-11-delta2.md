# memorysmith-agent-codebase-audit-26-07-11-delta2.md

Delta from [prior delta audit](memorysmith-agent-codebase-audit-26-07-11-delta1.md)

Continuing the audit. I’m keeping this pass focused on **duplication and functional duplication (Type 4)** rather than repeating the previous architecture findings.

I do **not** have an actual SonarQube/CPD/JetBrains duplication scan artifact from this environment, so I’m not claiming a tool-generated metric. This pass is based on static pattern analysis of the reviewed code paths. A real follow-up should run:

* SonarQube duplicated blocks detector
* PMD CPD (copy/paste detector)
* JetBrains Inspect Code “Duplicated code”
* Roslyn analyzers (especially maintainability/cyclomatic complexity)
* NDepend (type dependency cycles, abstractness/instability, code smells)

---

# MemorySmith.Agent delta audit — duplication focused

## New finding 18 — Multiple action identity systems are functionally duplicated

### Evidence

The code currently has several representations of “an action/tool”:

1. `IToolCaller` / `ToolDispatcher`
2. `ActionRegistry`
3. HTN planner tool names
4. adapter wire protocol names
5. blueprint executor action names

Examples:

```csharp
d.Register(new PlaceBlockTool(world));
d.Register("place", new PlaceBlockTool(world));
```

and:

```csharp
registry.Register(
    new ActionDescriptor(tool.Name, tool.Description)
);
```

and planner comments:

```csharp
// Sprint 55: LLM fallback + tool names
```

### Duplication classification

**Type 4 functional duplication**

These are not identical code blocks. They are multiple implementations of the same concept:

> “What actions exist, what are they called, and how are they invoked?”

Currently that knowledge exists in:

* tool registration
* planner metadata
* adapter protocol translation
* blueprint execution

### Risk

Adding a new action requires updating multiple places:

Example:

```
Add "DigTunnel"

Tool class
 ↓
Dispatcher registration
 ↓
ActionRegistry metadata
 ↓
Planner vocabulary
 ↓
Mineflayer adapter mapping
 ↓
Blueprint executor mapping
```

Missing one produces runtime-only failures.

### Recommendation

Create one canonical action manifest:

```csharp
public sealed record AgentActionDescriptor(
    ActionId Id,
    string DisplayName,
    string[] Aliases,
    ToolCapability Capability,
    string Description);
```

Then generate:

* dispatcher registration
* LLM tool schema
* planner actions
* adapter aliases
* UI metadata

from the same source.

### Suggested task

New task:

`TSK-04XX Consolidate action identity and capability registry`

Confidence: **98%**

---

# Finding 19 — Error handling duplication across layers

## Pattern

The same error concepts appear independently:

* adapter errors
* tool execution errors
* planner failures
* recovery manager failures
* chat failures

Each layer creates its own:

* error strings
* reason names
* fallback decisions

Example pattern:

```
Mineflayer adapter
    |
    emits text error
        |
Tool layer
    |
    parses text
        |
Runtime
    |
decides recovery
```

### Duplication classification

**Type 4 functional duplication**

The system repeatedly answers:

> “Why did this action fail?”

but every layer has its own representation.

### Recommendation

Introduce:

```csharp
public enum FailureCategory
{
    Timeout,
    InvalidTarget,
    PermissionDenied,
    AdapterUnavailable,
    WorldChanged,
    Unknown
}
```

with:

```csharp
ActionFailure
{
    ActionId,
    Category,
    RetryPolicy,
    IsRecoverable
}
```

Then:

* adapters create failures
* tools transport failures
* runtime reasons over failures

### Task overlap

Should extend adapter hardening (`TSK-0410`).

Confidence: **96%**

---

# Finding 20 — Status/state projection duplication

## Evidence

State exists in multiple forms:

* `AgentRuntime`
* `AgentBackgroundService`
* dashboard publisher
* `/api/agent/status`
* SignalR payloads

Each reconstructs state.

### Example risk

One path says:

```
CurrentAction = queued action
```

Another says:

```
CurrentAction = executing action
```

### Duplication classification

**Type 4 functional duplication**

Multiple components independently answer:

> “What is the agent currently doing?”

### Recommendation

Create:

```csharp
AgentSnapshot
{
    LifecycleState,
    ActiveAction,
    QueueDepth,
    LastFailure,
    WorldStateVersion,
    Timestamp
}
```

Single producer:

```
Runtime
  |
  v
AgentSnapshot
  |
 +---- API
 +---- SignalR
 +---- Logs
 +---- Tests
```

### Task overlap

Extends dashboard/status cleanup.

Confidence: **95%**

---

# Finding 21 — Configuration migration logic is duplicated

## Evidence

Current configuration concerns appear in multiple places:

* startup migration
* options binding
* runtime consumers

Example:

Legacy:

```
Agent:Chat:Model
```

New:

```
Agent:Chat:LlmModel
```

The compatibility decision exists in startup, but consumers still know fallback behavior exists.

### Duplication classification

**Type 4 functional duplication**

Multiple components know configuration history.

### Recommendation

Create:

```
Raw Configuration
        |
        v
ConfigurationMigrator
        |
        v
Canonical Options
        |
        v
Application
```

No consumer should know old keys exist.

### Task overlap

Extends legacy cleanup work.

Confidence: **97%**

---

# Finding 22 — Null/degraded mode handling is duplicated

Current pattern:

```csharp
GetService<T>()
```

appears as a way to represent optional capability.

Different consumers interpret null differently:

```
null memory gateway
    ?
null dashboard hub
    ?
null world KB
```

### Duplication classification

**Type 4 functional duplication**

Every subsystem invents its own degraded-mode semantics.

### Recommendation

Replace:

```
optional dependency
```

with:

```
explicit capability implementation
```

Example:

```csharp
IMemoryGateway
    |
    +-- RestMemoryGateway
    +-- DisabledMemoryGateway
```

Now the system has one contract.

Confidence: **94%**

---

# Finding 23 — Planner/decomposer matching duplicates domain knowledge

## Evidence

Current model:

```
Goal
 |
DecomposerRegistry
 |
CanHandle()
 |
Planner
```

The registry relies on each decomposer independently determining applicability.

Example:

```csharp
GatherGoalDecomposer.CanHandle()
```

and:

```csharp
PlaceBlockGoalDecomposer
```

already have overlap concerns.

### Duplication classification

**Type 4 functional duplication**

Multiple decomposers encode overlapping knowledge:

* what goals mean
* what goals they own
* priority

### Recommendation

Move ownership into explicit descriptors:

```csharp
GoalCapability
{
    GoalType,
    Priority,
    Preconditions
}
```

The planner chooses based on metadata.

### Task overlap

Planner architecture extension.

Confidence: **92%**

---

# Finding 24 — Logging/event semantics are duplicated

## Pattern

The system appears to have multiple “truth streams”:

* ILogger
* AgentJournal
* ChatHistory
* dashboard events
* adapter events

Each records partially overlapping lifecycle information.

Example:

```
Action started
Action completed
Action failed
```

can exist in multiple places.

### Duplication classification

**Type 4 functional duplication**

Multiple event histories exist.

### Risk

Debugging becomes:

“Which history is authoritative?”

### Recommendation

Introduce domain events:

```csharp
AgentEvent
{
    EventType,
    Timestamp,
    CorrelationId,
    Payload
}
```

Then projections:

```
AgentEventStream
    |
    + Journal
    + Dashboard
    + Logs
    + Metrics
```

Confidence: **90%**

---

# New highest priority cleanup list

| Priority | Finding                                                                 |
| -------- | ----------------------------------------------------------------------- |
| P0       | Consolidate action identity into one registry                           |
| P0       | Introduce canonical action failure model                                |
| P1       | Create single agent snapshot state model                                |
| P1       | Remove configuration migration knowledge from runtime                   |
| P1       | Replace null optional services with explicit capability implementations |
| P2       | Consolidate domain event/history streams                                |

---

## Overall delta confidence

**93%**

The strongest pattern emerging is that MemorySmith.Agent is not suffering from classic copy/paste duplication. The larger risk is **semantic duplication**:

> Multiple subsystems independently encode the same business concepts.

That is the Type 4 duplication category most likely to create long-term technical debt in an agent architecture. The next refactoring phase should prioritize **single sources of truth for concepts**, not just reducing repeated lines of code.
