# MemorySmith.Agent — Sprint 23 Handoff

**Prepared:** 2026-06-19  
**Branch:** `sprint-5-tool-safety`  
**Version:** 0.23.0  
**Council Disposition:** APPROVED (post-sprint council 2026-06-19)  
**Council Document:** `Data/Pages/council/sprint23-council-20260619.md`

---

## Table of Contents

1. [Sprint 23 — What Was Done](#sprint-23--what-was-done)
2. [Architecture Notes](#architecture-notes)
   - [Health Interrupt Flow](#health-interrupt-flow)
   - [World KB Routing](#world-kb-routing)
3. [Files Changed This Sprint](#files-changed-this-sprint)
4. [Sprint 24 Priorities](#sprint-24-priorities)
5. [Non-Negotiable Rules](#non-negotiable-rules)

---

## Sprint 23 — What Was Done

Sprint 23 delivered five items against branch `sprint-5-tool-safety` (base commit `3bd22e0b`). All five items were in scope at the pre-sprint council, which issued a CONDITIONAL approval with five blocking requirements. All five blockers were resolved in the shipped code.

---

### P0-A — Real-Time Damage Interrupt (resolves all 5 pre-council blockers)

The core deliverable. When the bot's health drops between consecutive `HealthEvent` observations, the agent now synthesizes a `DamageTakenEvent`, evaluates whether the active goal permits an interrupt, and — if so — atomically clears the action queue and enqueues a priority `GetStatus` to force immediate re-evaluation.

**What was added:**

**`DamageTakenEvent`** (`Agent.Core/Events/WorldEvents.cs`):  
A `sealed record` inheriting from `WorldEvent`. Fields: `PreviousHealth`, `Health`, `Delta` (always negative — HP lost), `Food`, `Timestamp`. Synthesized C#-side by `AgentBackgroundService.ProcessEventsAsync()` from consecutive `HealthEvent` comparisons. Not received from the Node.js wire. Resolves pre-council D-4 (record type pattern).

**`IGoal.DamageInterruptThresholdHp`** (`Agent.Core/Interfaces/IGoal.cs`):  
`int? DamageInterruptThresholdHp => null;` added as a default interface implementation. Semantics:
- `null` (default) — use system default threshold (6 HP / 3 hearts)
- `0` — never interrupt; reserved for future combat goals where the goal itself manages damage response
- Any positive `int` — goal-specific threshold in HP (e.g., a fragile exploration goal might use 10 HP for earlier interrupts)

All existing `IGoal` implementations inherit the `null` default without any code changes. Resolves pre-council B-2 (boolean `AllowsDamageInterrupt` replaced with expressive threshold form).

**`ActionQueue.ClearAndEnqueue(ActionData)`** (`Agent.Core/Models/ActionQueue.cs`):  
Lock-protected atomic clear-plus-enqueue. Acquires `_lock`, clears the `ConcurrentQueue<T>`, enqueues the priority action, releases the lock. `EnqueueAll` was also made lock-protected in the same change to prevent a bulk plan enqueue from a concurrent planner task from interleaving with the interrupt. Single-item `Enqueue` remains lock-free (intrinsically thread-safe via `ConcurrentQueue<T>`). Resolves pre-council B-3 (non-atomic clear-then-enqueue).

**New constants in `AgentBackgroundService`:**
- `DamageInterruptCooldownSeconds = 3` — minimum seconds between successive interrupt triggers; prevents queue flooding during drowning or lava damage at 1 Hz
- `HealthCheckCooldownSeconds = 2` — minimum seconds between passive `GetStatus` enqueues (distinct gate)

**New fields in `AgentBackgroundService`:**
- `_previousHealth = -1` — health baseline for delta computation; -1 is a sentinel meaning "uninitialized, skip delta on first event"
- `_lastDamageInterruptAt = DateTimeOffset.MinValue` — rate-limit clock for the interrupt path
- `_lastHealthStatusEnqueuedAt = DateTimeOffset.MinValue` — rate-limit clock for the passive health check path; also updated by the interrupt path to prevent immediate re-enqueueing (D-6 resolution)

**`SetGoal()` resets** all three new fields on every goal change, preventing inter-goal health state contamination (D-7 resolution).

**`ProcessEventsAsync()` delta logic:**  
After each event is applied to the projector, reads `_worldState.Health`, compares to `_previousHealth`. If `_previousHealth > 0 && currentHealth > 0 && currentHealth < _previousHealth`: synthesizes `DamageTakenEvent`, applies it to the projector (stores facts), calls `TryInterruptOnDamage`. Baseline update (`_previousHealth = currentHealth`) is guarded by `currentHealth > 0` so death events (hp=0) do not become the baseline.

**`TryInterruptOnDamage(DamageTakenEvent)`** — new private method:  
Implements a five-guard interrupt sequence:
1. No active goal → skip
2. `DamageInterruptThresholdHp == 0` (combat mode) → `LogDebug` + return
3. `health >= threshold` (damage not severe enough) → `LogDebug` + return
4. Rate-limited (elapsed < `DamageInterruptCooldownSeconds`) → `LogDebug` + return
5. **Trigger**: `LogWarning` with structured fields, `SendEmergencyStop()`, `ClearAndEnqueue(GetStatus)`, update both rate-limit timestamps, write journal entry

Structured log fields on trigger (`LogWarning`): `PrevHp`, `CurrHp`, `Delta`, `Goal`, `Threshold`, `QueueDepth` (before clear). Resolves B-4 (no logging requirements in original scope).

**`WorldStateProjector`** (`Agent.Core/WorldStateProjector.cs`):  
`DamageTakenEvent` added to the `Apply` switch as `DamageTakenEvent e => StoreFacts(current, e)`. `StoreFacts` gained a `DamageTakenEvent` case writing four facts: `event:DamageTaken:PreviousHealth`, `event:DamageTaken:Health`, `event:DamageTaken:Delta`, `event:DamageTaken:Food`. Health state is not re-applied by this path (the originating `HealthEvent` already updated `_worldState.Health`).

---

### P0-B — World KB Tool Routing

`SearchMemoryTool` and `CreatePageTool` now receive the world-keyed `IMemoryGateway` instance at construction time. `GetPageTool` continues to use the default (agent KB) gateway.

**`SearchMemoryTool`** (`Agent.Tools/Tools/SearchMemoryTool.cs`):  
Constructor accepts `IMemoryGateway memory`. `Description` updated: "Searches the world knowledge base for spatial observations, block data, biome notes, and in-world exploration history. Routes to the world KB instance (see WorldKbUrl in appsettings). Use GetPage to retrieve agent knowledge base entries such as sprint docs or code documentation." Resolves B-5 (LLM tool description not reflecting new routing).

**`CreatePageTool`** (`Agent.Tools/Tools/CreatePageTool.cs`):  
Constructor accepts `IMemoryGateway memory`. `Description` updated: "Creates or updates a page in the world knowledge base to record in-world observations, block discoveries, or exploration notes. Routes to the world KB instance. Use GetPage for agent knowledge base retrieval." Resolves B-5.

**`Program.cs`** DI wiring:  
```csharp
var worldMemory = sp.GetKeyedService<IMemoryGateway>("world") ?? memory;
d.Register(new SearchMemoryTool(worldMemory)); // world KB
d.Register(new GetPageTool(memory));           // agent KB
d.Register(new CreatePageTool(worldMemory));   // world KB
```
The `?? memory` fallback ensures no null reference if the keyed service is unexpectedly absent. The `"world"`-keyed registration (established in Sprint 22) falls back to `BaseUrl` when `WorldKbUrl` is null, so in practice both gateways point to the same endpoint when world KB is not configured — the startup warning (P1-A) makes this visible.

---

### P1-A — WorldKbUrl Null Default

**`RestMemoryGatewayOptions.WorldKbUrl`** (`Agent.Memory/RestMemoryGatewayOptions.cs`):  
Default changed from `"http://127.0.0.1:6869"` to `null` (no initializer). XML doc updated with "Sprint 23 B-1 MIGRATION NOTE" block documenting the change, the previous default, the fallback behavior, and the `appsettings.json` path. Resolves B-1 (no migration notice in original scope).

**Startup warning** (`Program.cs`):  
```csharp
if (string.IsNullOrWhiteSpace(memCfg.WorldKbUrl))
{
    app.Logger.LogWarning(
        "World KB URL is not configured (WorldKbUrl is null). World observations will be stored " +
        "in agent KB. Set WorldKbUrl in Agent:Memory:WorldKbUrl to enable world KB separation. " +
        "See Data/Pages/Guides/world-kb-deployment.md");
}
```
At `LogWarning` level; includes configuration path and doc reference. Fires at every startup when `WorldKbUrl` is null so the misconfiguration is visible in the startup log.

**Migration action for existing deployments:** If you were relying on the implicit `http://127.0.0.1:6869` default, add `"WorldKbUrl": "http://127.0.0.1:6869"` (or your actual world KB URL) to `appsettings.json` under `Agent:Memory`. If you have no world KB instance, the null default is correct — the startup warning is informational and can be suppressed by setting `WorldKbUrl` to the same value as `BaseUrl`.

---

### P1-B — Health Check Rate-Limit Guard

The Sprint 22 passive health check (health below threshold → enqueue `GetStatus`) was unbounded: every `HealthEvent` while health was low would add another `GetStatus` to the queue, producing ~20 enqueues over a 20-second drowning event.

Sprint 23 adds a 2-second gate (`HealthCheckCooldownSeconds = 2`) tracked in `_lastHealthStatusEnqueuedAt`. The passive check only fires when at least 2 seconds have elapsed since the last `GetStatus` enqueue. This field is also updated by `TryInterruptOnDamage`, so a damage interrupt suppresses the passive check for 2 seconds after the interrupt fires (D-6 resolution — prevents the passive path from double-enqueueing immediately after an interrupt).

---

### Tests — 15 New Tests in 4 Fixtures

All tests in `MemorySmith.Agent.Tests/Sprint23Tests.cs`.

**`Sprint23DamageThresholdTests` (3 tests):**
- `DefaultGoal_DamageInterruptThresholdHp_IsNull` — inherits default, returns null
- `CombatGoal_DamageInterruptThresholdHp_IsZero` — overrides to 0 (never interrupt)
- `FragileGoal_DamageInterruptThresholdHp_ReturnsCustomValue` — overrides to 10 HP

**`Sprint23ActionQueueAtomicTests` (4 tests):**
- `ClearAndEnqueue_ClearsExistingItems_AndEnqueuesNew` — 3 items cleared, 1 GetStatus present
- `ClearAndEnqueue_OnEmptyQueue_EnqueuesOne` — empty-queue edge case
- `ClearAndEnqueue_AfterClear_PriorityActionPresent` — no stale items survive
- `ClearAndEnqueue_ConcurrentEnqueue_PriorityActionAlwaysPresent` — key concurrency regression test; 1000 concurrent `Wander` enqueues vs. one interrupt; GetStatus count == 1 after drain

**`Sprint23WorldKbRoutingTests` (4 tests):**
- `SearchMemoryTool_CallsWorldGateway` — injection verified
- `SearchMemoryTool_DoesNotCallAlternateGateway` — isolation verified
- `CreatePageTool_CallsWorldGateway` — injection verified
- `GetPageTool_CallsAgentGateway` — agent KB isolation verified

**`Sprint23DamageTakenEventTests` (4 tests):**
- `DamageTakenEvent_Delta_IsNegative` — sign convention guard
- `DamageTakenEvent_AllFields_Accessible` — all 5 fields readable
- `DamageTakenEvent_ValueEquality_AsRecord` — record value equality
- `DamageTakenEvent_InheritsWorldEvent` — type hierarchy guard

---

## Architecture Notes

### Health Interrupt Flow

The diagram below shows the complete damage interrupt sequence as implemented in Sprint 23. This supersedes the passive health check flow from Sprint 22 (which remains active as the fallback `else if` branch).

```
Node.js (Mineflayer)
    |
    | bot.health changes
    | sendEvent('health', { hp, food })
    v
WebSocketBridge (C#)
    |
    | deserialize → HealthEvent(Health, Food, Timestamp)
    v
ProcessEventsAsync [single await foreach loop]
    |
    | _projector.Apply(_worldState, healthEvent)
    |   → _worldState.Health = healthEvent.Health
    |
    | currentHealthNow = _worldState.Health
    |
    |── if (_previousHealth > 0 && currentHealth > 0 && currentHealth < _previousHealth)
    |       delta = currentHealth - _previousHealth  [negative]
    |       damageTaken = new DamageTakenEvent(PreviousHealth, Health, Delta, Food, Now)
    |       _projector.Apply(_worldState, damageTaken)
    |         → StoreFacts: event:DamageTaken:PreviousHealth, Health, Delta, Food
    |       TryInterruptOnDamage(damageTaken)
    |           |
    |           |── guard: no goal → return
    |           |── guard: threshold == 0 → LogDebug + return   [combat mode]
    |           |── guard: health >= threshold → LogDebug + return
    |           |── guard: rate-limited → LogDebug + return
    |           |
    |           └── TRIGGER:
    |               LogWarning([damage] INTERRUPT triggered: prev curr delta goal threshold depth)
    |               SendEmergencyStop()  [fire-and-forget to Node.js → stops current action]
    |               _queue.ClearAndEnqueue(GetStatus)  [atomic: lock → clear → enqueue → unlock]
    |               _lastDamageInterruptAt = UtcNow
    |               _lastHealthStatusEnqueuedAt = UtcNow  [sync passive gate]
    |               Journal.Log(DamageInterrupt)
    |
    |── else if (health < HealthCriticalThreshold && goal active)
    |       [passive check — rate-limited to HealthCheckCooldownSeconds = 2s]
    |       LogInformation([health] below critical — queuing GetStatus passive check)
    |       _queue.Enqueue(GetStatus)
    |       _lastHealthStatusEnqueuedAt = UtcNow
    |
    | _previousHealth = currentHealth  [guarded: only if currentHealth > 0]
    v
DispatchActionsAsync [separate concurrent task]
    |
    | _queue.Dequeue() → GetStatus action
    v
ToolCaller.CallAsync("GetStatus")
    |
    | Node.js returns current position, health, inventory
    v
StatusEvent → WorldStateProjector.ApplyStatus
    |
    | fresh world state available for re-planning
    v
Next planning cycle: PlanAsync(_currentGoal, _worldState)
```

**Threshold resolution:**  
`threshold = _currentGoal.DamageInterruptThresholdHp ?? HealthCriticalThreshold`  
where `HealthCriticalThreshold = 6` (system default, 3 hearts out of 10).

**Rate limits:**  
- Interrupt: 3 seconds (`DamageInterruptCooldownSeconds`)
- Passive check: 2 seconds (`HealthCheckCooldownSeconds`)
- Both clocks reset in `SetGoal()`

**Note on ordering invariant:** The `HealthEvent` is applied to the projector before the `DamageTakenEvent` is synthesized. This means `_worldState.Health` already reflects the new health value when `TryInterruptOnDamage` is called. `damageTaken.Health == _worldState.Health` is a semantic dependency, not a coincidence. Do not reorder these two projector calls.

---

### World KB Routing

The diagram below shows the tool-to-gateway routing as of Sprint 23.

```
LLM decision
    |
    |── SearchMemory("diamond ore vein near spawn")
    |       → SearchMemoryTool(_worldMemory)
    |           → RestMemoryGateway(WorldKbUrl)   [world observations]
    |
    |── CreatePage("Iron vein at 100,40,-200", "...")
    |       → CreatePageTool(_worldMemory)
    |           → RestMemoryGateway(WorldKbUrl)   [world observations]
    |
    └── GetPage("sprint-23-notes")
            → GetPageTool(_memory)
                → RestMemoryGateway(BaseUrl)      [agent KB: sprint docs, code]

DI composition (Program.cs):
    _memory       = sp.GetRequiredService<IMemoryGateway>()         [default, agent KB]
    _worldMemory  = sp.GetKeyedService<IMemoryGateway>("world")     [keyed, world KB]
               ?? _memory                                            [fallback if unconfigured]

Startup behavior when WorldKbUrl is null:
    - "world" keyed registration falls back to BaseUrl (same endpoint as agent KB)
    - LogWarning at startup: "World KB URL is not configured..."
    - Both _memory and _worldMemory point to the same MemorySmith instance
    - No data loss; world observations accumulate in agent KB until WorldKbUrl is set

Configuration (appsettings.json):
    "Agent": {
      "Memory": {
        "BaseUrl":    "http://localhost:5000",   // agent KB
        "WorldKbUrl": "http://localhost:6869"    // world KB (null = not configured)
      }
    }

Technical debt note (D-5, open):
    IMemoryGateway is currently shared between agent KB and world KB.
    Both stores use the same interface despite different semantic content
    (agent KB: code docs / sprint notes; world KB: spatial observations / block data).
    A future sprint should introduce IWorldObservationGateway as a subtype or
    companion interface to enable world-KB-specific operations (e.g., BlockPosition
    metadata, retention policies) without polluting IMemoryGateway.
```

---

## Files Changed This Sprint

| File | Change Type | Summary |
|------|-------------|---------|
| `Agent.Core/Events/WorldEvents.cs` | Modified | Added `DamageTakenEvent` sealed record; updated `HealthEvent` XML doc with cross-reference; updated file header with Sprint 23 addendum |
| `Agent.Core/Interfaces/IGoal.cs` | Modified | Added `int? DamageInterruptThresholdHp => null` default interface implementation with comprehensive XML doc |
| `Agent.Core/Models/ActionQueue.cs` | Modified | Added `ClearAndEnqueue(ActionData)` with lock; made `EnqueueAll` lock-protected; updated class header with Sprint 23 addendum |
| `Agent.Core/WorldStateProjector.cs` | Modified | Added `DamageTakenEvent` to `Apply` switch and `StoreFacts` case; updated class header with Sprint 23 note |
| `Agent.Memory/RestMemoryGatewayOptions.cs` | Modified | `WorldKbUrl` default changed to `null`; XML doc updated with B-1 migration notice |
| `Agent.Tools/Tools/SearchMemoryTool.cs` | Modified | `Description` updated to world KB semantics; XML class doc updated |
| `Agent.Tools/Tools/CreatePageTool.cs` | Modified | `Description` updated to world KB semantics; XML class doc updated |
| `WebUI.Blazor/AgentBackgroundService.cs` | Modified | Added 2 constants, 3 fields; updated `SetGoal` to reset fields; updated `ProcessEventsAsync` with delta logic and passive rate-limit; added `TryInterruptOnDamage` method |
| `WebUI.Blazor/Program.cs` | Modified | Version bumped to 0.23.0; world KB gateway resolved and passed to `SearchMemoryTool` / `CreatePageTool`; startup `LogWarning` for null `WorldKbUrl` |
| `MemorySmith.Agent.Tests/Sprint23Tests.cs` | Added | 15 tests in 4 fixtures: `Sprint23DamageThresholdTests`, `Sprint23ActionQueueAtomicTests`, `Sprint23WorldKbRoutingTests`, `Sprint23DamageTakenEventTests` |

---

## Sprint 24 Priorities

These priorities are derived from: (1) pre-council deferred findings D-1 through D-11, (2) post-council new observations PC-D-1 through PC-D-3, and (3) items explicitly deferred to Sprint 24 in the post-sprint council synthesis.

---

### P0 — Must-Have

**P0-1: Integration test for the damage interrupt path**  
Pre-council D-1 (carried from Sprint 22) and post-council concern. `TryInterruptOnDamage` is the core Sprint 23 deliverable and has no unit tests. The five guard conditions (no goal, threshold=0, health above threshold, rate-limited, trigger) should each be independently exercised. This requires a fake or stub `IWorldAdapter` that can feed controlled `HealthEvent` sequences, and either real clock delays or the D-8 clock abstraction. Recommend implementing D-8 first (see P1-1) and then writing the rate-limit boundary test.

Test scenarios to cover at minimum:
- Health drops by 3 HP at 16 HP total → no interrupt (health above threshold)
- Health drops by 8 HP, lands at 5 HP → interrupt fires
- Interrupt fires at T=0; health drops again at T=2s → suppressed by rate limit
- Interrupt fires at T=0; health drops again at T=3.5s → second interrupt fires
- Goal with `DamageInterruptThresholdHp == 0` (combat mode) → no interrupt regardless of health
- Goal with `DamageInterruptThresholdHp == 10`; health lands at 9 → interrupt fires (below custom threshold)
- No active goal → no interrupt

**P0-2: GatherGoalDecomposer TargetCount verification**  
Deferred from Sprint 22. Verify that `TargetCount` is correctly propagated through the HTN decomposer to the generated action sequence. This was flagged as a potential silent correctness issue where the gather goal generates the correct number of gather steps. Confirm by reading the decomposer and adding a test that exercises a gather plan with a non-default target count.

---

### P1 — Should-Have

**P1-1: Clock abstraction for rate-limit fields (D-8)**  
Replace direct `DateTimeOffset.UtcNow` calls in `AgentBackgroundService` with injected `TimeProvider` (from `System.TimeProvider`, available in .NET 8+). Add a constructor parameter `TimeProvider? clock = null` defaulting to `TimeProvider.System`. This unblocks deterministic unit tests for rate-limit boundary conditions without real clock delays. Apply to: `_lastDamageInterruptAt` comparison in `TryInterruptOnDamage`, `_lastHealthStatusEnqueuedAt` comparison in the passive check, and `_lastReplanAt` comparison in `DispatchActionsAsync` (pre-existing `DateTimeOffset.UtcNow` usage).

This is the highest-leverage testability improvement available after Sprint 23. Estimate: single PR touching `AgentBackgroundService.cs` and any affected tests.

**P1-2: IWorldObservationGateway note in architecture docs (D-5)**  
Add a note to the architecture documentation (or the features reference guide) acknowledging that `IMemoryGateway` is currently shared between the agent KB and world KB, and that the two stores have different semantic content. Record the technical debt: a future sprint should introduce `IWorldObservationGateway` as a subtype or companion interface when world-KB-specific operations (block position metadata, retention policies, spatial query extensions) are needed. This prevents the shared-interface pattern from solidifying as permanent without a written record of the decision.

**P1-3: IGoal interface version-note comment (D-1)**  
Sprint 21 established a convention of documenting interface evolutions in a header comment block in `IGoal.cs`. Sprint 23 used per-property `Added in Sprint 23` attribution instead. Add a version-history comment block to `IGoal.cs` covering all interface additions by sprint: `HasFailed` (Sprint 21), `DamageInterruptThresholdHp` (Sprint 23). This is low-effort and high-traceability-value.

---

### Deferred — No Target Sprint

**D: Combat goal implementation**  
When the first combat goal is introduced, its `DamageInterruptThresholdHp` should return `0` (never interrupt — the goal manages its own damage response). The interface contract is already in place from Sprint 23. The implementation team should also revisit D-10 at that time: the fire-and-forget `SendEmergencyStop()` delivery guarantee may be insufficient for fast PvP scenarios where the bot cannot afford to execute one more action after the interrupt fires.

**D: Per-tool gateway routing log (D-9)**  
Add `LogDebug` to `SearchMemoryTool.ExecuteAsync` and `CreatePageTool.ExecuteAsync` indicating which gateway instance was used. This enables post-hoc diagnosis of routing bugs where the wrong backend was queried. Low priority until a routing bug is observed in practice.

**D: `ActionQueue.Enqueue` documentation (PC-D-1)**  
Add a comment to the single-item `Enqueue` method explaining why it is not lock-protected while `ClearAndEnqueue` and `EnqueueAll` are. This prevents future maintainers from adding an unnecessary lock (which could create deadlock) or questioning whether the absence of a lock is an oversight.

**D: P1-C delivery verification (PC-D-2)**  
Confirm whether the `GatherGoalDecomposer` TargetCount comment (P1-C in the Sprint 23 scope) was implemented. If not, add a code comment explaining the `TargetCount` propagation pattern in the next sprint that touches `GatherGoalDecomposer`.

**D: ProcessEventsAsync ordering invariant comment (PC-D-3)**  
Add a comment in `ProcessEventsAsync` at the `DamageTakenEvent` synthesis point noting that `HealthEvent` must be applied to the projector before `DamageTakenEvent` is synthesized, and that `damageTaken.Health == _worldState.Health` is a semantic invariant, not a coincidence. This prevents a future refactor from reordering the projector calls.

---

## Non-Negotiable Rules

These rules carry forward from prior sprints, updated to reflect Sprint 23 conventions. All new work on this codebase must comply.

1. **One sprint, one branch, one council.** Each sprint runs on a named branch (`sprint-N-[descriptor]`). No work merges to main without a post-sprint council APPROVED disposition. Pre-sprint and post-sprint council documents live in `Data/Pages/council/`.

2. **Events are sealed records.** All `WorldEvent` subtypes are `sealed record` types inheriting `WorldEvent(Timestamp)`. Never use a class for a world event. Never add mutable state to an event record. `DamageTakenEvent` established the pattern for synthesized (non-wire) events: document in the XML class doc that the event is C#-side computed, not received from Node.js.

3. **`IGoal` is a versioned interface.** Adding a property to `IGoal` is a breaking change for all implementations. New properties must have a default interface implementation. When adding a property, document the semantic conventions (e.g., null = system default, 0 = reserved, positive = custom) in the XML doc and add a `Added in Sprint N` attribution. Threshold-style properties are preferred over booleans for policy decisions.

4. **`ActionQueue.ClearAndEnqueue` is the only atomic clear-plus-enqueue.** Never call `queue.Clear()` followed by `queue.Enqueue()` as separate operations in concurrent code. The two-step sequence is not atomic and will race with `ChatConsumerAsync` and `DispatchActionsAsync`. Always use `ClearAndEnqueue` for interrupt-style operations that must discard the current plan and insert a priority action.

5. **Damage interrupts are rate-limited and reversible only at the next planning cycle.** The interrupt path is irreversible from the queue's perspective — cleared actions are gone. The interrupt should only fire when health drops below the active goal's threshold AND the rate limit has elapsed. Do not lower `DamageInterruptCooldownSeconds` below 2 seconds without analyzing the impact on drowning-at-1Hz scenarios. Do not raise it above 5 seconds without analyzing the impact on fast-damage scenarios (explosion, fall).

6. **World KB and agent KB are routed by tool, not by parameter.** `SearchMemoryTool` and `CreatePageTool` route to the world KB. `GetPageTool` routes to the agent KB. There is no runtime switching. If you add a new tool that accesses memory, decide at construction time which gateway it uses and document the decision in the tool's `Description` property so the LLM knows what kind of content to expect from that tool.

7. **Structured logging is required for all irreversible operations.** An operation is irreversible if it discards state (queue clear), modifies Node.js runtime state (emergency stop), or changes the active goal. All three must emit a `LogWarning` or higher with structured properties sufficient to diagnose false positives. The `[damage] INTERRUPT triggered` log entry is the model: six named properties, prefixed with `[damage]`.

8. **Tests must cover the interface contracts, not just the happy path.** For each new `IGoal` property, test the null/zero/custom-value cases. For each new `ActionQueue` method, test empty-queue, populated-queue, and concurrent-access cases. For each new tool routing, test that the tool calls the injected gateway and does not call an alternate gateway. The Sprint 23 test fixtures (`Sprint23Tests.cs`) are the reference for this pattern.

---

**Document prepared by:** Post-sprint council, 2026-06-19  
**Next sprint:** Sprint 24  
**Sprint 24 opens with:** P0-1 (damage interrupt integration test), P0-2 (GatherGoalDecomposer TargetCount), P1-1 (TimeProvider abstraction)
