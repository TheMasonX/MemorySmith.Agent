# memorysmith-agent-codebase-audit-26-07-11-delta1.md

Delta from [prior audit](memorysmith-agent-codebase-audit-26-07-11-08-02-16.md)

## 11) `Program.cs` has contradictory migration state: legacy cleanup comments while preserving compatibility paths

### Evidence

The file header says:

> “Sprint 60 — Architectural Stability & Legacy Cleanup”

but several legacy pathways remain intentionally alive:

* legacy model migration:

```csharp
var legacyModel = chatSection["Model"];
if (!string.IsNullOrWhiteSpace(legacyModel) && string.IsNullOrWhiteSpace(chatSection["LlmModel"]))
    chatOpts = chatOpts with { LlmModel = legacyModel };
```

* world memory fallback:

```csharp
var worldMemory = sp.GetKeyedService<IMemoryGateway>("world") ?? memory;
```

* optional memory gateway:

```csharp
memoryGateway: sp.GetService<IMemoryGateway>()
```

### Problem

The codebase is now mature enough that compatibility behavior should be isolated.

Right now migration logic is mixed into the core runtime composition root, meaning:

* every future engineer must reason about old and new contracts simultaneously
* removing old paths becomes risky because they look like normal behavior
* testing has to cover combinations of legacy/new configuration states

### Recommendation

Create an explicit migration boundary:

```
Configuration
    |
    v
LegacyConfigMigrator
    |
    v
CanonicalAgentOptions
    |
    v
Runtime
```

After migration:

* no service should know legacy keys exist
* no runtime component should contain fallback semantics

### Task impact

Likely extension to existing architecture cleanup tasks, not a new feature task.

Confidence: **96%**

---

# 12) Tool registration comments and implementation disagree

### Evidence

The code states:

> “GetStatusTool is registered under its canonical name "GetStatus" and aliased as "Status"”

But implementation:

```csharp
d.Register(new GetStatusTool(world));
d.Register("Status", new GetStatusTool(world));
```

Same issue:

```csharp
d.Register(new PlaceBlockTool(world));
d.Register("place", new PlaceBlockTool(world));
```

### Problem

This is worse than the previous “duplicate instance” finding.

The comments imply aliasing, but the code creates separate objects.

This means the system currently has a false abstraction:

Expected:

```
Tool Identity
    |
    +-- aliases
          |
          +-- same instance
```

Actual:

```
Tool A
Tool B
    |
    +-- same behavior accidentally
```

Future changes can silently diverge:

Example:

```csharp
GetStatusTool
    - add cache
    - add metrics
    - add throttling
```

Now:

```
GetStatus
    -> cached

Status
    -> uncached
```

### Recommendation

Make aliases a first-class dispatcher feature:

```csharp
d.Register(new GetStatusTool(world));
d.Alias("Status", "GetStatus");
```

The registry should own identity.

### Task impact

No direct duplicate found.

New architectural cleanup.

Confidence: **99%**

---

# 13) ActionRegistry duplicates ToolDispatcher as a second source of truth

### Evidence

Current flow:

```csharp
var dispatcher = (ToolDispatcher)sp.GetRequiredService<IToolCaller>();

foreach (var tool in dispatcher.All)
{
    registry.Register(new ActionDescriptor(tool.Name, tool.Description));
}
```

### Problem

This is better than maintaining a separate list, but it still creates a snapshot.

Runtime:

```
ToolDispatcher
      |
      v
ActionRegistry
```

If tools can be dynamically modified later:

```
ToolDispatcher changes
        |
        X
ActionRegistry stale
```

The planner may believe actions exist that no longer do.

### Recommendation

Make `ActionRegistry` a view over the dispatcher:

```csharp
interface IActionCatalog
{
    IReadOnlyCollection<ActionDescriptor> Actions { get; }
}
```

Single ownership:

```
Tool Registry
      |
      +--> Planner
      +--> LLM prompt builder
      +--> UI
```

### Task impact

Potential extension to planner/action metadata work.

Confidence: **94%**

---

# 14) `DecomposerRegistry` has hidden priority semantics

### Evidence

Comment:

> “PlaceBlockGoalDecomposer MUST be registered before GatherGoalDecomposer”

because:

> “GatherGoalDecomposer.CanHandle matches IItemSpecGoal”

### Problem

Registration order is acting as hidden business logic.

Current contract:

```
Registry order == priority
```

That is fragile.

A future engineer adding:

```csharp
reg.Register(new NewGoalDecomposer());
```

can accidentally change behavior.

### Recommendation

Make priority explicit:

```csharp
Register(
    decomposer,
    priority: 100
);
```

Then:

```
PlaceBlock = 100
Gather = 50
Fallback = 0
```

### Task impact

No duplicate task found.

Should become planner architecture cleanup.

Confidence: **97%**

---

# 15) `AgentRuntime` and `AgentBackgroundService` appear to split ownership incorrectly

### Evidence

Both exist:

```csharp
builder.Services.AddSingleton<AgentRuntime>(...)
```

and:

```csharp
builder.Services.AddSingleton<AgentBackgroundService>(...)
builder.Services.AddHostedService(...)
```

`AgentRuntime` owns:

* intent
* planning
* execution
* recovery
* state
* dashboard

But `AgentBackgroundService` still receives:

* planner
* intent manager
* journal
* replan governor
* evaluator
* chat history
* memory gateway

### Problem

There are now two orchestration layers.

Current shape:

```
Hosted Service
      |
      +--> AgentRuntime
      |
      +--> Also directly owns runtime concerns
```

This creates an architectural fork.

Eventually one becomes obsolete, but until then every feature has to decide:

"Does this belong in AgentRuntime or AgentBackgroundService?"

### Recommendation

Make:

```
AgentBackgroundService
        |
        v
AgentRuntime
        |
        +--> everything else
```

The hosted service should only:

* start
* stop
* lifetime management
* cancellation

### Task impact

Extension of the earlier monolith finding.

Confidence: **98%**

---

# 16) Optional dependencies create ambiguous degraded modes

### Evidence

Examples:

```csharp
sp.GetService<IHubContext<AgentHub>>()
```

and:

```csharp
sp.GetService<IMemoryGateway>()
```

### Problem

The system has no explicit degraded-mode model.

Possible states:

```
Memory unavailable
    ?
    - allowed?
    - temporary?
    - fatal?
```

Currently:

```
missing dependency
        |
        v
null
        |
        v
runtime decides later
```

This pushes architecture decisions into random consumers.

### Recommendation

Replace optional dependencies with explicit implementations:

Example:

```csharp
interface IMemoryGateway
{
}

class DisabledMemoryGateway : IMemoryGateway
{
}
```

Now:

```
Agent always has memory gateway
```

but behavior is explicit.

### Task impact

Could extend TSK-0134 startup diagnostics.

Confidence: **95%**

---

# 17) The composition root has become a hidden architecture document

### Evidence

`Program.cs` is currently defining:

* config migration
* logging
* security
* memory topology
* tools
* planners
* runtime
* compatibility behavior

over hundreds of lines.

### Problem

The dependency graph is now too important to live as imperative startup code.

This creates:

* poor discoverability
* merge conflicts
* accidental ordering dependencies

### Recommendation

Move registration into modules:

```
DependencyInjection/

    MemoryModule.cs
    ToolModule.cs
    PlannerModule.cs
    RuntimeModule.cs
    SecurityModule.cs
```

Then:

```csharp
builder.Services
    .AddAgentRuntime()
    .AddAgentTools()
    .AddMemorySubsystem();
```

### Task impact

Architecture improvement.

Confidence: **97%**

---

## Updated priority ordering

The most important new corrections:

| Priority | Item                                                                             |
| -------- | -------------------------------------------------------------------------------- |
| P0       | Remove dual orchestration ownership (`AgentRuntime` vs `AgentBackgroundService`) |
| P1       | Fix fake tool alias abstraction                                                  |
| P1       | Replace decomposer registration ordering with explicit priority                  |
| P1       | Eliminate legacy configuration fallbacks from runtime                            |
| P2       | Make optional dependencies explicit degraded services                            |
| P2       | Split Program.cs composition root                                                |
