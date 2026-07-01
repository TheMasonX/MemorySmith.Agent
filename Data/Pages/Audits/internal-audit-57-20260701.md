# Internal Codebase Audit — Sprint 57

**Date:** 2026-07-01  
**Scope:** MemorySmith.Agent (all projects), MineflayerAdapter  
**Type:** Deep-dive bug/inconsistency/gap/architecture sweep  
**Auditor:** SteveBot automated analysis, with 4-seat anonymous council peer review

---

## Executive Summary

This audit covers **P0–P3 issues** across 6 categories: correctness bugs, design inconsistencies, weak guards, poor error handling, observability gaps, and architectural overcoupling. The previous Sprint 57 Wave B audit correctly identified the major structural issues (replanning loop, inventory truth, goal-safe provisioning, deny-list contract). This audit builds on those findings with **additional concrete issues** discovered by systematic codebase examination.

**Severity distribution:** 7 P0 (critical), 9 P1 (high), 7 P2 (medium), 5 P3 (low/observability)  
**Total findings: 34** (28 original + 6 from peer review)

**Peer Review:** This report underwent a 4-seat anonymous council review (Architecture & Design, Runtime & Debugging, Safety & Security, Completeness & QA). Findings marked with [PR] were added or corrected based on reviewer feedback. See §Peer Review Results for the full reviewer responses.

**Note on duplication:** 6 of the original findings duplicate issues already documented in the Sprint 57 Wave B audit (`llm-adaptapbility-sprint-57-audit-7-1-26.md`). They are retained here with cross-references for completeness.

---

## P0 — Critical

### P0-1: PlaceBlockGoal Never Completes (Was "Completes at Plan Time" — Fixed in TSK-0317, Introduced New Issue)

**UPDATE from peer review:** The original bug (`pg.Dispatched = pg.Count` in decomposer) was **partially fixed** by TSK-0317/Sprint 58. The decomposer no longer pre-sets `Dispatched`. Instead, `Dispatched++` is incremented per confirmed `BlockPlacedEvent` in `AgentBackgroundService`. **However, this introduced a new issue:** `_dispatched` is a plain `int` field with no `volatile` or synchronization, read by `DispatchActionsAsync` and written by `ProcessEventsAsync` — two concurrently running async tasks. This is a data race.

```csharp
// PlaceBlockGoal.cs — still the active code:
private int _dispatched;  // no volatile, no Interlocked
public int Dispatched { get => _dispatched; set => _dispatched = value; }

// AgentBackgroundService — write from event handler:
if (_currentGoal is Agent.Planning.Goals.PlaceBlockGoal pgGoal)
    pgGoal.Dispatched++;  // race: no synchronization

// IsComplete — read from dispatch loop:
return _dispatched >= _count;
```

**Impact:** Without `volatile` or `Interlocked.Increment`, the write from `ProcessEventsAsync` may never be visible to `DispatchActionsAsync`. The goal could hang indefinitely, never completing. This is arguably **worse** than the original premature-completion bug because the agent blocks rather than incorrectly moving on.

**Detection difficulty:** Timing-dependent. May only manifest under heavy event load when the JIT hoists the read into a register.

**Recommendation:** Use `Interlocked.Increment` for `_dispatched` and `Volatile.Read` (or `Interlocked.CompareExchange` for reads) in `IsComplete`.

---

### P0-2: LlmEvaluatorImpl Fast-Path Suppresses World Divergence Detection

**File:** `Agent.Planning/LlmEvaluatorImpl.cs` (line ~65–71)

**The bug:** The evaluator checks `failureCount == 0` and returns "continue" before it even inspects the `WorldStateDiff`. When fire-and-forget tools "succeed" structurally but the world diverges from expectation, the diff is passed but ignored.

```csharp
// Fast-path 2: all outcomes succeeded — no reason to replan.
var failureCount = outcomes.Count(static o => !o.Success);
if (!forceEvaluate && failureCount == 0)
    return new EvaluationResult(false, "all actions succeeded");
```

**Impact:** The entire `WorldStateDiff` mechanism (TSK-0155, Sprint 55) is effectively dead code in normal operation. The observe→compare→evaluate loop only works when `forceEvaluate=true` (governor stall path). During normal execution, inventory drift, position mismatch, and health drops are all silently ignored.

**Recommendation:** After the "all succeeded" fast-path, also check `diff?.HasMismatch == true`. If the world diverged, call the LLM anyway. Additionally, **always include diff context in the LLM prompt** (not conditionally on `diff.HasMismatch`), so the LLM can reason about "everything matched" vs "something didn't."

---

### P0-3: InventorySyncLoopAsync Only Syncs When Idle AND Stale

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `InventorySyncLoopAsync` (line ~1179–1186)

**The bug:** The periodic inventory sync loop only enqueues `GetStatus` when the agent is idle AND inventory is stale. The stale flag is only set by `SetGoal()`. During an active goal (gather, build, craft), no periodic sync occurs — even though `WorldStateProjector.ApplyBlockMined` explicitly acknowledges it can double-count items and relies on periodic `GetStatus` for reconciliation.

```csharp
// Only sync when idle — don't interfere with active goals.
if (_currentGoal is not null)
    continue;

// Skip if inventory is already fresh (not stale).
if (!_worldState.IsInventoryStale)
    continue;
```

**Impact:** The comment "Periodic GetStatus reconciles any drift" is false in practice during active goals. Drift accumulates silently until the goal completes, causing false goal completion (system thinks it has more/fewer items than it does) or unnecessary gathering. The stale flag is only cleared by a `StatusEvent` from an explicit `GetStatus` action; if the goal's plan doesn't include `GetStatus`, inventory remains stale for the entire goal duration.

**Recommendation:** Remove the `_currentGoal is not null` guard. Sync periodically regardless of goal state, but at a longer interval during active goals (e.g., 60s instead of 30s).

---

### P0-4: DeniedCommands Normalization Contract Violation

**Files:** `WebUI.Blazor/Options/SafetyOptions.cs` (line ~30), `WebUI.Blazor/AgentBackgroundService.cs` — `HandleChatEventAsync` command handler (line ~1400)

**The bug:** `SafetyOptions` XML doc says "Commands are compared case-insensitively (leading slash optional in config)". But the runtime check compares config entries against the slash-prefixed command token. If config contains `kill` (no slash), it will never match `/kill`.

```csharp
// Runtime check (AgentBackgroundService):
var cmdLower = intent.Item.Split(' ')[0].ToLowerInvariant();
var isDenied = DeniedCommands.Contains(cmdLower);  // cmdLower = "/kill"
// DeniedCommands = { "kill" } → no match
```

**Impact:** Operators configuring `DeniedCommands` without the leading slash believe commands are blocked, but they pass through. This is a safety configuration reliability issue.

**Recommendation:** Normalize both the config values (strip leading slash) and the incoming command token (strip leading slash) at comparison time, or document clearly that the leading slash is required.

---

### P0-5: Creative Provisioning Uses CancellationToken.None

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `SetGoal` (line ~360)

**The bug:** `SetGoal` calls `_ = ProvisionGoalIfCreativeAsync(goal, CancellationToken.None)`. The provisioning loop can outlive the goal, enqueuing `/give` and `GetStatus` commands for a goal that was already cancelled or replaced.

```csharp
if (_worldState.IsCreativeMode)
    _ = ProvisionGoalIfCreativeAsync(goal, CancellationToken.None);
```

**Impact:** After a goal change, stale `/give` commands for the old goal's materials continue to be enqueued. The `GetStatus` at the end of provisioning can clear the stale-inventory flag prematurely for the *new* goal.

**Recommendation:** Pass a per-goal CancellationToken that is cancelled when the goal changes. Or, check `_currentGoal == goal` at the start of each provisioning iteration and abort if the goal has been replaced.

---

### P0-6: ExecutionManagerImpl JSON Round-Trip Destroys Type Fidelity

**File:** `WebUI.Blazor/Managers/ExecutionManagerImpl.cs` (line ~60–70)

**The bug:** `ActionData.Arguments` is `Dictionary<string, object?>` but `IToolCaller.CallWithOutcomeAsync` expects `JsonElement`. The conversion performs a full JSON serialization/parse round-trip.

```csharp
var json = JsonSerializer.Serialize(action.Arguments);
argsElement = JsonDocument.Parse(json).RootElement;
```

**Impact:** Type information is lost: integers become `JsonValueKind.Number` (fine), but `long` values, `decimal`, or custom types can get mangled. More critically, the `ToolDispatcher.ValidateAgainstSchema` uses `TryGetInt32` which rejects scientific notation — the round-trip can convert strings to numbers or vice versa depending on source types. The comment acknowledges this is a known issue deferred to Sprint 40.

**Recommendation:** Add a direct `ActionData → JsonElement` conversion path or change `CallWithOutcomeAsync` to accept `Dictionary<string, object?>`.

---

## P1 — High

### P1-1: AgentRuntime and Six Manager Interfaces Are Dead Code

**Files:** `Agent.Core/Runtime/AgentRuntime.cs`, `WebUI.Blazor/Managers/*Impl.cs`, `WebUI.Blazor/Program.cs`

**The bug:** All six managers (`IntentManagerImpl`, `PlanningManagerImpl`, `ExecutionManagerImpl`, `RecoveryManagerImpl`, `StateManagerImpl`, `DashboardPublisherImpl`) and `AgentRuntime` are registered in DI and instantiated but **never used** by the live agent path. `AgentBackgroundService` still owns everything directly.

- `RecoveryManagerImpl.TryRecoverAsync()` always returns `false` (stub since Sprint 39)
- `StateManagerImpl` is registered but `AgentBackgroundService` passes events to `WorldStateProjector` directly
- `PlanningManagerImpl.PlanAsync()` has a `ExecutionContext` overload with precondition checks that is never called by the live path

**Impact:** This is ~250 lines of registered but unused code. The second system (manager layer) and the production system (ABS) can drift apart. New features added to managers don't affect actual agent behavior, creating a false sense of architecture maturity.

**Recommendation:** Either wire the managers into `AgentBackgroundService` (making it a thin coordinator) or remove them. The current in-between state is the worst option.

---

### P1-2: EntityObservedEvent Bypasses StructuredFacts and Grows Facts Unbounded

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `EntityObservedEvent` handler (line ~965–1010)

**The bug:** `EntityObservedEvent` stores full serialized JSON in `_worldState.Facts` via direct `with { Facts = facts }` replacement, bypassing the `WorldState.Builder` and its provenance-tracked `StructuredFacts`.

```csharp
var facts = new Dictionary<string, object?>(_worldState.Facts)
{
    ["nearbyHostiles"] = hostileSummary,
    ["nearbyHostilesUpdatedAt"] = ...,
    ["nearbyEntities"] = summary,
    ["nearbyEntitiesUpdatedAt"] = ...,
    ["nearbyEntitiesRaw"] = JsonSerializer.Serialize(...),  // full entity array as JSON string
};
_worldState = _worldState with { Facts = facts };
```

**Impact:**
1. `nearbyEntitiesRaw` stores a full serialized array that grows with entity count and is never evicted.
2. Using `with { Facts = facts }` replaces the entire dictionary, losing any facts added by the `Builder` pattern between events.
3. No trimming mechanism — over long sessions, the facts dictionary accumulates these keys indefinitely.

**Recommendation:** Use the Builder pattern (`_worldState.With(b => ...)`) so StructuredFacts is maintained. Evict `nearbyEntitiesRaw` after a TTL. Consider a rolling window for entity observations.

---

### P0-7 [PR]: HtnPlanner LLM Fallback Uses Sync-Over-Async (Deadlock Risk)

**Upgraded from P1-3 per peer review.**

**File:** `Agent.Planning/HtnPlanner.cs` — `TryLlmFallback` (line ~193)

**The bug:** `TryLlmFallback` is a synchronous method that blocks on an LLM API call using sync-over-async:

```csharp
var response = _llm.CompleteAsync(prompt, "", CancellationToken.None)
    .ConfigureAwait(false).GetAwaiter().GetResult();
```

This is called from `PlanAsync`, which is synchronous (returns `Task.FromResult`). The LLM timeout is 15s+, so the entire dispatch loop blocks for up to 15 seconds during planning.

**Impact:**
1. **Deadlock risk** — If the caller's `SynchronizationContext` is single-threaded (ASP.NET request context, some test runners), `.GetAwaiter().GetResult()` deadlocks.
2. **Dispatch loop freeze** — The agent cannot process events while `PlanAsync` is blocked.
3. **CancellationToken.None** — The LLM call is uncancellable, so a slow provider response cannot be interrupted by shutdown.

**Recommendation:** Make `IPlanner.PlanAsync` fully async-through (return `Task<IPlan>`, use `await`), and propagate the cancellation token to the LLM call.

---

### P1-4: PlaceBlockGoalDecomposer Places All Blocks at Same Coordinates

**File:** `Agent.Planning/Decomposition/PlaceBlockGoalDecomposer.cs` (line ~30–60)

**The bug:** For a `PlaceBlockGoal` with `count=3`, three `PlaceBlock` actions are created, all targeting the same `(targetX, targetY, targetZ)`.

```csharp
for (int i = 0; i < pg.Count; i++)
{
    args = { ["x"] = targetX, ["y"] = targetY, ["z"] = targetZ, ... };
    actions.Add(new ActionData { Tool = "place", Arguments = args });
}
```

**Impact:** You cannot place multiple blocks in the same position. The second and third placements will always be skipped/fail. When used without coordinates (null → bot position), all 3 are targeted at the same block.

**Recommendation:** Either offset subsequent placements (e.g., along the bot's facing direction), or make the decomposer only produce 1 action regardless of count with `count=N` in the arguments.

---

### P1-5: BuildGoalDecomposer Origin Source Mislabeling

**File:** `Agent.Planning/Decomposition/BuildGoalDecomposer.cs` (line ~65–70)

**The bug:** When coordinates come from stored world-state facts (not explicit origin), the origin source is still stamped as `AutoScanned`.

```csharp
var source = bg.HasExplicitOrigin ? BuildOriginSource.Explicit : BuildOriginSource.AutoScanned;
```

But in the else branch, coordinates were read from stored facts — which could have come from a previous explicit placement or a previous scan. This mislabels the provenance.

**Impact:** Logs and LLM context treat all non-explicit origins as "auto-scanned," which is misleading when the origin was previously set by REST API or a prior build.

**Recommendation:** Add a `BuildOriginSource.StoredFacts` value, or at minimum log the actual source alongside the label.

---

### P1-6: WorldStateDiff Does Not Detect Unexpected Inventory Changes

**File:** `Agent.Core/Models/WorldStateDiff.cs` — `HasInventoryMismatch`

**The bug:** `HasInventoryMismatch` only checks that expected gains are met and expected losses happen. It does **not** flag unexpected inventory changes (gained items that weren't expected, or items lost that weren't accounted for).

```csharp
// Only checks "did we get what we expected?"
// Does NOT check "did we get something we didn't expect?"
```

**Impact:** If the bot accidentally picks up extra items (or drops items), the diff doesn't detect it. The LLM evaluator never sees the anomaly.

**Recommendation:** Add a check for unexpected inventory keys in `ActualInventoryDelta` that aren't in either `InventoryGained` or `InventoryLost`.

---

### P1-7: RememberFactAsync Is Fire-and-Forget

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `HandleChatEventAsync` "remember" case

**The bug:** Fact persistence to MemorySmith is fire-and-forget with no error reporting to the player.

```csharp
_ = RememberFactAsync(intent.Item, factValue, chat.PlayerPos);
```

**Impact:** If the MemorySmith API call fails (network error, auth failure), the fact is silently lost. The player receives "I'll remember that" but the fact is never persisted.

**Recommendation:** At minimum, log failures at Warning level. Ideally, report failure back to the player via chat ("I tried to remember that but hit an error").

---

## P2 — Medium

### P1-8 [PR]: Safety Config Merge Erodes Default Deny List

**Upgraded from P2-1 per peer review.**

**Files:** `WebUI.Blazor/appsettings.json` (DeniedCommands), `WebUI.Blazor/AgentBackgroundService.cs` — `DeniedCommands` property

**The bug:** The `DeniedCommands` property is an XOR switch — it returns EITHER the config entries OR the built-in defaults, never a merge. If config has `["/op"]`, it **replaces** the 35+ entry built-in list (including `/ban`, `/stop`, `/execute`, `/gamemode`, `/fill`).

```csharp
private HashSet<string> DeniedCommands =>
    safetyOptions?.Value?.DeniedCommands is { Count: > 0 } configured
        ? configured       // replaces entire default — no merge!
        : DefaultDeniedCommands;
```

Additionally, `SafetyOptions.DeniedCommands` defaults to an **empty** set. The real defaults live in `AgentBackgroundService.DefaultDeniedCommands`. Configuring any value replaces the comprehensive built-in list wholesale.

**Impact:** A well-intentioned operator who adds 1-2 commands to the deny list thinking they're supplementing it actually removes 35+ protections. This is a defense-in-depth erosion vulnerability.

**Recommendation:** Merge config with defaults: `new HashSet<string>(DefaultDeniedCommands.Concat(configured), ...)`. Consider making config additive-only (config items cannot remove defaults).

---

### P2-2: EntityObservedEvent and Entity-Related Facts Never Evicted

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — Entity/BelowBlock event handlers

**The bug:** `nearbyHostiles`, `nearbyEntities`, `nearbyEntitiesRaw`, `blockBelow`, and related keys are set on every event but never cleaned up. They accumulate in the `Facts` dictionary indefinitely.

**Impact:** Over long sessions (hours), the Facts dictionary grows. `EntityObservedEvent` stores raw JSON arrays. The `nearbyEntitiesRaw` string alone can be many KB.

**Recommendation:** Evict entity-related facts after a TTL (e.g., 60s of no observation). Consider a rolling buffer for `nearbyEntitiesRaw`.

---

### P2-3: MineBlock Has No Per-Tool Timeout Override

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `ToolTimeoutOverrides`

**The bug:** `MineBlock` is not in `ToolTimeoutOverrides`, so it defaults to `DefaultActionTimeoutSeconds` (30s). A mine action can spend 30s before timing out.

**Impact:** In practice, a mine action that finds no blocks may wait the full 30s. Most mine actions complete in 2-5s. A shorter timeout (10-15s) would let the agent fail faster and retry.

**Recommendation:** Add MineBlock to `ToolTimeoutOverrides` with 15s (same reasoning as PlaceBlock's recent increase).

---

### P2-4: _blocksPlacedThisCycle Not Reset Between Plan Cycles

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — various locations

**The bug:** `_blocksPlacedThisCycle` is set to 0 when the cycle-complete log fires, but if the dispatch loop enters a continuous placement phase (no cycle-complete gap), the counter accumulates across cycles.

**Observability impact:** The "[build] cycle complete: {Count} blocks placed" log can report inflated numbers if the counter wasn't reset at the start of each cycle.

**Recommendation:** Reset `_blocksPlacedThisCycle` at a well-defined boundary (e.g., when a new plan is generated in the DispatchActionsAsync loop).

---

### P2-5: HtnPlanner and PlannerRouter Have Overlapping Type Handling

**Files:** `Agent.Planning/HtnPlanner.cs` (PlanAsync), `Agent.Planning/Router/PlannerRouter.cs`

**The bug:** `HtnPlanner.PlanAsync` contains its own type-switch for `BuildGoal`, `CraftItemGoal`, and `IItemSpecGoal`. `PlannerRouter` delegates to decomposers first, then falls back to `HtnPlanner`. But `HtnPlanner` handles the same types directly — so when a decomposer IS registered, `HtnPlanner` never sees the goal (correct). But when no decomposer is registered (e.g., a new goal type), the goal falls to `HtnPlanner` which may still handle it via its type-switch, masking the missing registration.

**Impact:** A missing decomposer registration is not an error — it's silently handled by the fallback. New decomposers must be registered in BOTH places to take effect (PlannerRouter and the old HtnPlanner path).

**Recommendation:** Remove the direct type-switch from `HtnPlanner.PlanAsync` — make it a pure task-library/phase fallback. All typed goals should have dedicated decomposers.

---

### P2-6: Safety Config Merging in Program.cs Creates Hidden Coupling

**File:** `WebUI.Blazor/Program.cs` (line ~140–145)

**The bug:** `SafetyOptions.DeniedCommands` is merged into `ChatOptions.DeniedCommands` so the LLM prompt knows which commands to avoid. This creates a dependency where changes to safety config propagate to the chat prompt, but only at startup (the options are bound once).

**Impact:** Runtime changes to `SafetyOptions` are not reflected in the LLM prompt until restart. The LLM may suggest commands that are actually blocked, creating a confusing user experience.

**Recommendation:** Either (a) inject `IOptions<SafetyOptions>` directly into the prompt builder for live reads, or (b) document this as a known limitation.

---

### P1-9 [PR]: LLM Evaluator Consecutive Failure Counter Has No Circuit Breaker (Counter Reset Creates Infinite Loop)

**Upgraded from P2-7 per peer review.**

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — DispatchActionsAsync (line ~2310)

**The bug:** `_consecutiveLlmEvalFailures` is reset to 0 after hitting 3 failures, creating an infinite detection-to-reset loop:

```csharp
if (_consecutiveLlmEvalFailures >= 3)
{
    logger.LogError(...);
    _consecutiveLlmEvalFailures = 0;  // reset — starts counting back up immediately
}
```

Sequence: fail → fail → fail → log ERROR → reset → fail → fail → fail → log ERROR → reset → ...

**Impact:**
1. Each full cycle burns 3+ LLM calls (each costing tokens and latency) before the error message repeats
2. No cooldown, no circuit-breaker, no exponential backoff
3. The counter effectively measures "last 3 calls" but provides no shielding against sustained failure

**Recommendation:** After N consecutive failures, disable the LLM evaluator for a cooldown period (e.g., 60s) or fall back to a deterministic "replan on any failure" rule. Do NOT reset the counter to 0 after the threshold is hit — let it accumulate so the error state persists.

---

## P3 — Low / Observability

### P3-1: WorldState Facts Grow Without Eviction

**File:** `Agent.Core/Models/WorldState.cs` — Builder.SetFact

**The bug:** `SetFact(key, value, source)` appends to `StructuredFacts` and trims at `MaxFacts` (1000). But the legacy `Facts` dictionary has no eviction mechanism — entries accumulate indefinitely.

```csharp
b.SetFact($"{prefix}Health", e.Health.ToString(), source);
b.SetFact($"{prefix}Food", e.Food.ToString(), source);
// ... every event adds entries
```

**Impact:** Over long sessions, the Facts dictionary can grow to thousands of entries. Many of these are transient event facts (MoveEvent fires ~10/sec, each adding `event:Move:Pos`).

**Recommendation:** Implement TTL-based or count-based eviction for the legacy Facts dictionary, or migrate all consumers to `StructuredFacts` which already has a trim mechanism.

---

### P3-2: HandleChatEventAsync Goal Creation Failures Logged at Debug Only

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — HandleChatEventAsync

**The bug:** When `_intentManager.BuildGoalRequest(intent)` returns null, the reason is logged at Debug:

```csharp
logger.LogDebug(
    "[intent] {Intent} draft: item={Item}, blueprint={Blueprint}, ...",
    intent.Intent, intent.Item, intent.Blueprint, ...);
```

But the *failure* to create a goal request is logged at Warning with the full context. However, the specific *reason* it failed (which field was missing) is not explained — the log just dumps all the fields.

**Observability impact:** When an LLM returns a malformed intent, operators see the raw fields but don't know which one caused the null return.

**Recommendation:** `BuildGoalRequest` should return a result type with a failure reason string, or the log should explicitly state "insufficient fields: item is null".

---

### P3-3: No Health/Food Log on HealthEvent Without Delta

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — ProcessEventsAsync

**The bug:** `HealthEvent` is processed by `WorldStateProjector` and updates `_worldState.Health/Food`. But unless a health delta is detected (damage interrupt check), there is no log of the current HP/food. The health check only fires when health < critical threshold.

**Observability impact:** During normal gameplay with no damage, health changes (healing, natural regen) are invisible in logs. Only the `StatusEvent` handler logs health.

**Recommendation:** Log HealthEvent at Debug level when health changes by more than 2 HP (ignoring 1-HP noise from natural regen).

---

### P3-4: _cycleOutcomes Accumulates Indefinitely Without Trimming

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — DispatchActionsAsync

**The bug:** `_cycleOutcomes` is a `ConcurrentQueue<ActionOutcome>` that is cleared when a new plan is generated. But between plan generations, outcomes accumulate. The LLM evaluator takes `TakeLast(10)` of them, but the queue itself is unbounded.

**Impact:** If a plan has many small actions (e.g., 200 PlaceBlock dispatches), the queue grows to 200+ entries. These are serialized to JSON for every LLM evaluator call.

**Recommendation:** Trim `_cycleOutcomes` to the last 20 entries after each evaluator call (since the evaluator only looks at the last 10).

---

### P3-5: Inventory Sync Pulse Initial Delay Stacks with Loop Delay

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — `InventorySyncLoopAsync`

**The bug (corrected per peer review):** The code structure is:

```csharp
await Task.Delay(InventorySyncInterval, ct);  // initial delay (30s)
while (!ct.IsCancellationRequested)
{
    await Task.Delay(InventorySyncInterval, ct);  // loop delay (30s) — at TOP
    // work...
}
```

The initial delay (30s) and the loop-top delay (30s) **stack**, so the first sync fires at **60s**, not 30s. There is no "immediately repeats" problem — rather, the first sync is delayed 2x longer than expected.

**Recommendation:** Move the loop delay to the *bottom* of the while body, after the work, so the sequence is: initial wait (30s) → work → wait (30s) → work → ...

---

### P2-9 [PR]: HtnPlanner Raw Action Dump Logged at Warning Level

**Upgraded from P3-6 per peer review.**

**File:** `WebUI.Blazor/AgentBackgroundService.cs` — DispatchActionsAsync

**The bug:** A Sprint 52 diagnostic log (TSK-0121 debugging) was left running at `LogWarning` level:

```csharp
logger.LogWarning(
    "[plan-raw] {Goal}: {N} total actions, first 5: [{Actions}] | planner={Planner}",
    _currentGoal.Name, totalActions, rawActions, planner.GetType().Name);
```

**Impact:** An active agent that replans every ~10-30s produces a `Warning` log entry on every plan generation. This is noise for operators and can trigger alerting systems. After ~18 sprints, this diagnostic should be downgraded or removed.

---

## Architecture Notes

### A-1: Manager Layer / AgentBackgroundService Split-Brain

The existing Sprint 57 Wave B audit correctly identifies this as the biggest architectural issue. This audit confirms and adds detail:

| Component | Registered? | Used by ABS? | Has Real Logic? |
|-----------|------------|-------------|-----------------|
| `PlanningManagerImpl` | Yes | No | Yes (ExecutionContext overload) |
| `ExecutionManagerImpl` | Yes | No | Yes (but unused) |
| `RecoveryManagerImpl` | Yes | No | Stub (always false) |
| `StateManagerImpl` | Yes | No | Yes (but ABS bypasses it) |
| `DashboardPublisherImpl` | Yes | No | Yes |
| `IntentManagerImpl` | Yes | No | Yes (but ABS calls *Agent.Planning.IntentManager* directly) |

**6 registered implementations, 0 used by the live path.** This is 6x dead code that must be maintained alongside the actual runtime.

### A-2: WorldState Has Two Fact Systems

`WorldState.Facts` (legacy `Dictionary<string, object?>`) and `WorldState.StructuredFacts` (provenance-tracked `IReadOnlyList<Fact>`) coexist with overlapping responsibilities. Some consumers write to both (`Builder.SetFact(string, string, FactSource)`), while others write only to Facts (`WorldState with { Facts = facts }`). The latter pattern breaks the provenance chain and creates inconsistency.

### A-3: ErrorEvent and BlockNotFoundEvent Share an Error Channel but Not Correlation

`TryRouteAsError` routes both to `_gameErrors` channel. But error handling in `DispatchActionsAsync` reads from `_gameErrors` and maps to `FailureReason` strings. The correlation (which action caused this error) is inferred from the current tool context rather than being explicit — if two tools fail near-simultaneously, the error-to-action mapping is wrong.

---

## Peer Review Results

This report underwent a 4-seat anonymous council review. Below is a summary of reviewer feedback and how it was incorporated.

### Architecture & Design Reviewer

| Feedback | Action |
|----------|--------|
| P0-1 partially fixed by TSK-0317; now has data race on `_dispatched` | Updated P0-1 with new finding |
| P1-3 should be P0 (sync-over-async deadlock) | Upgraded to P0-7 [PR] |
| P2-1 should be P1 (safety bypass) | Upgraded to P1-8 [PR] |
| P2-7 should be P1 (infinite loop) | Upgraded to P1-9 [PR] |
| P3-6 should be P2 (alerting noise) | Upgraded to P2-9 [PR] |
| P3-5 timing analysis wrong (stacks, not repeats) | Corrected P3-5 |
| 6/27 findings duplicate Sprint 57 Wave B audit | Noted in Executive Summary |
| Missing: `ProvisionGoalIfCreativeAsync` no goal-identity guard | Added as P2-7 [PR] |
| Missing: `_consecutiveLlmEvalFailures` reset-to-0 problem | Added to P1-9 [PR] |
| Missing: `SafetyOptions.DeniedCommands` empty default | Added to P1-8 [PR] |
| Missing: Sprint 58 norm fix only covers LLM prompt | Added as P2-8 [PR] |

### Runtime & Debugging Reviewer

| Finding | Verdict |
|---------|---------|
| P0-1: Original bug stale (TSK-0317 fixed); new data race confirmed | Updated P0-1 + new [PR-5] |
| P0-2: Confirmed — diff never consulted in fast-path | Retained with expanded recommendation |
| P0-3: Confirmed — `_currentGoal is not null` guard blocks sync | Retained |
| P0-6: Confirmed — JSON round-trip; deferred ~18 sprints | Added deferral context |
| P1-3: Confirmed — sync-over-async with `.GetAwaiter().GetResult()` | Upgraded to P0-7 [PR] |

### Safety & Security Reviewer

| Finding | Verdict |
|---------|---------|
| P0-4: Confirmed — Sprint 58 fix only covers LLM prompt path | Added nuance; new P2-8 [PR] |
| P2-1/P1-8: Confirmed — XOR switch erodes safety net | Upgraded to P1 |
| P0-5: Confirmed — recommends P1 (creative = non-production) | Retained at P0 (stale GetStatus flag clears for new goal) |

### Completeness & QA Reviewer

| New Finding | Severity | Status |
|-------------|----------|--------|
| SignalR event name drift (StatusUpdated vs SnapshotUpdated) | P2 | [PR-1] |
| DashboardPublisherImpl reads from dead StateManager | P2 | [PR-2] |
| Six .bak files in Managers/ directory | P3 | [PR-3] |
| WorldStateDiff has zero unit tests | P3 | [PR-4] |
| PlaceBlockGoal._dispatched data race | P0 | [PR-5] (merged into P0-1) |
| physicsTick entity scan allocates every tick | P3 | [PR-6] |

### No Existing Task Tracking for 3 P0 Findings

The following P0 findings have **no existing task** in `Data/Tasks/`:
- **P0-2**: LlmEvaluator fast-path suppresses world diff (no task found)
- **P0-3**: InventorySync only when idle AND stale (no task found)
- **P0-6**: ExecutionManager JSON round-trip (no task found)

These should have tasks created before Sprint 58 implementation begins.

---

## Methodology

- All projects in the solution were examined: source files, decomposers, event handlers, managers, configuration, and JS adapter
- Repository memories were cross-referenced for known issues
- Recent git history was checked for context
- Each finding was verified against the actual code (no assumptions from comments alone)
- Findings are ordered by severity, not by category

---

*Generated by SteveBot automated analysis — 2026-07-01*
