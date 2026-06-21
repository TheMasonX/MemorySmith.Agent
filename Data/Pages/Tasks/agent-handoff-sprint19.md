# Sprint 19 Handoff: Planner Architecture Refactor + Runtime Robustness

**Date:** 2026-06-17
**Branch:** sprint-5-tool-safety
**Latest commit:** 7a125b1
**CI status:** GREEN (run 27727986513)
**Previous handoff:** Data/Pages/Tasks/agent-handoff-20260617t.md

## Executive Summary

Three independent analyses (runtime test logs, external codebase audit, external synthesis response) plus a 5-seat council review with anonymous peer review converge on the same conclusion: the planning system's failures are structural, not incidental. The gather planner is too shallow, the replan loop has no progress awareness, item resolution is semantically wrong for stone, and system messages pollute the chat pipeline. The changes are prioritized into P0 (correctness), P1 (structural), and P2 (design-only) tiers to avoid the big-bang integration anti-pattern flagged by the skeptical reviewer.

**Key constraint from council:** Do NOT change StatusEvent full-inventory-replacement in WorldStateProjector. It is a reconciliation feature, not a bug. The Archivist (85% confidence) and peer reviewer both confirmed this.

---

## Priority Tiers

### P0 — Must Ship (Correctness Fixes)

These are independent, isolated fixes. Each can be implemented and tested without the others.

1. **Stone-to-cobblestone alias fix**
2. **System message filtering in JS adapter**
3. **Gather batch size increase + Wander evaluation**

### P1 — Should Ship (Structural Improvements)

These build on P0 but are not prerequisites for each other.

4. **Minimal 2-state replan governor**
5. **IItemAcquisitionRegistry interface + flat dict backing**
6. **findFlatArea radius expansion + max retry**

### P2 — Design Only (Document for Sprint 20)

Do not implement. Document the design in wiki pages for the next sprint.

7. Full 4-state governor (PLANNING/EXECUTING/BACKING_OFF/STALLED)
8. GoalProgressTracker
9. JS-side greedy mining loop
10. NavigateTo race condition investigation

### Deferred (Sprint 21+)

11. Bot fences / protected regions
12. Terrain recovery (water/lava)
13. Full item resolution chain with transformation steps

---

## P0-1: Stone-to-Cobblestone Alias Fix

### Problem
`ChatInterpreter.ItemAliases["stone"] = "cobblestone"` is a semantic bug. In Minecraft, stone and cobblestone are different items. Mining stone without Silk Touch drops cobblestone. A user saying "get stone" gets a goal for cobblestone, which may not be what they want.

### Root Cause (Verified)
- File: `Agent.Planning/ChatInterpreter.cs`
- Line: `["stone"] = "cobblestone"` in the `ItemAliases` dictionary
- `CommonMinecraftBlocks.DirectMineBlocks` includes BOTH "stone" AND "cobblestone"

### Fix
**Step 1:** In `ChatInterpreter.cs`, change the alias:
```csharp
// BEFORE:
["stone"] = "cobblestone",
// AFTER:
["stone"] = "stone",
```

**Step 2:** In `GenericGatherGoal.IsComplete()`, the completion check sums inventory by `SourceBlocks` keys. For a stone ItemSpec where SourceBlocks = ["stone"], the bot mines stone blocks but gets cobblestone in inventory. The IsComplete check must account for the yield mapping.

Option A (simple): Add cobblestone to the stone spec's SourceBlocks list:
```csharp
// In TryMakeBuiltInSpec or the item registry:
// stone spec should have SourceBlocks = ["stone", "cobblestone"]
```

Option B (better): Add a `YieldItem` field to ItemSpec that maps the expected drop. IsComplete checks YieldItem instead of SourceBlocks when set:
```csharp
public record ItemSpec(
    string ItemId,
    string DisplayName,
    IReadOnlyList<string> SourceBlocks,
    int MinHarvestLevel,
    bool RequiresSmelting,
    string? YieldItem = null  // NEW: what actually drops when mining SourceBlocks
);
```
Then in IsComplete: if `YieldItem` is set, check `state.Inventory.GetValueOrDefault(YieldItem) >= targetCount`.

### Acceptance Criteria
- [ ] "leo gather stone" creates a Gather:stone goal (not Gather:cobblestone)
- [ ] Mining stone blocks and receiving cobblestone counts toward stone goal completion
- [ ] "leo gather cobblestone" still works (separate alias entry preserved)
- [ ] Existing tests pass; add test: GatherStone_MinedStoneYieldsCobblestone_GoalCompletes

### Council Confidence: 85% (all 5 seats agree)

---

## P0-2: System Message Filtering in JS Adapter

### Problem
Teleport messages like "Teleported TheMasonX23 to Leo" reach the ChatInterpreter, pass the IsDirectedAtBot() heuristic (especially in solo play where ALL messages pass), trigger a 15-second LLM call via Ollama, which returns null, and waste time. Server system messages should never reach the LLM path.

### Root Cause (Verified)
- File: `MineflayerAdapter/index.js`
- The `bot.on('chat', (username, message) => ...)` handler only filters `bot.username`. All other chat is forwarded to C# as a ChatEvent.
- No layer in C# filters system messages either.

### Fix
**In `MineflayerAdapter/index.js`**, add a system message filter before the WebSocket send:

```javascript
// Add near the top of the file:
const SYSTEM_MESSAGE_PATTERNS = [
  /^Teleported\s+\S+\s+to\s+\S+/i,           // Teleport confirmations
  /^\S+\s+joined\s+the\s+game$/i,              // Join messages
  /^\S+\s+left\s+the\s+game$/i,                // Leave messages
  /^\[Server\]/i,                               // Server-prefixed messages
  /^Set\s+the\s+time\s+to\s+/i,                // Time set
  /^Set\s+\S+\s+game\s+mode\s+to\s+/i,         // Gamemode changes
  /^Killed\s+/i,                                // Kill notifications
];

function isSystemMessage(username, message) {
  // No username or empty username = server message
  if (!username || username.trim() === '') return true;
  // Check against known patterns
  return SYSTEM_MESSAGE_PATTERNS.some(re => re.test(message));
}

// In the bot.on('chat') handler, before sendEvent:
if (isSystemMessage(username, message)) {
  // If it's a teleport of the bot, emit a position update event instead
  const teleportMatch = message.match(/^Teleported\s+(\S+)\s+to\s+(\S+)/i);
  if (teleportMatch && teleportMatch[1].toLowerCase() === bot.username.toLowerCase()) {
    // Bot was teleported — emit position update from bot.entity.position on next tick
    setTimeout(() => {
      if (bot.entity) {
        sendEvent('move', {
          x: Math.floor(bot.entity.position.x),
          y: Math.floor(bot.entity.position.y),
          z: Math.floor(bot.entity.position.z),
        });
      }
    }, 100);
  }
  return; // Do not forward system messages as chat
}
```

### Acceptance Criteria
- [ ] Teleport messages do not trigger LLM calls
- [ ] Teleport of the bot emits a MoveEvent to update WorldState.Position
- [ ] Player chat still works normally (no over-filtering)
- [ ] Join/leave messages are silently dropped
- [ ] Add test: verify isSystemMessage matches known patterns

### Council Confidence: 92% (Seats 1+4 agree, no opposition)

---

## P0-3: Gather Batch Size Increase + Wander Evaluation

### Problem
The gather decomposition produces 4 actions: SearchMemory, Wander, MineBlock(count), GetStatus. The MineBlock count defaults to 10 (or TargetCount via Sprint 18 fix). But the Wander action sends the bot to a random position within radius 40 BEFORE mining, which means the bot walks away from resources it could mine locally. Combined with the 2-second replan interval, this creates the "mine a bit, wander away, mine a bit" pattern.

### Root Cause (Verified)
- File: `Agent.Planning/HtnTaskLibrary.cs`, method `GatherItemDecompose`
- Produces: SearchMemory → Wander(radius=40) → MineBlock(source, count) → GetStatus
- Mineflayer's `bot.findBlock({matching: blockType, maxDistance: 64})` already finds the NEAREST block — the Wander action fights this by moving the bot to a random position first

### Fix
**Step 1:** In `HtnTaskLibrary.GatherItemDecompose`, make two changes:

```csharp
// BEFORE:
actions.Add(MakeAction("Wander", new { radius = 40, maxDistanceFromSpawn = 200 }));

// AFTER — conditionally include Wander only when the bot has no known resource location:
// Move Wander to be the FALLBACK, not the default.
// The mine action should come first. If mine returns BlockNotFound,
// THEN Wander on the next replan cycle.
```

Concrete implementation: reorder to SearchMemory → MineBlock → GetStatus (3 actions). The Wander becomes a fallback when MineBlock fails (triggered by the replan after BlockNotFoundEvent).

**Step 2:** Increase the MineBlock count to batch more mining per plan cycle:
```csharp
// BEFORE (in GatherGoalDecomposer):
var count = gg.TargetCount; // could be 1, 10, 100

// AFTER: cap at a reasonable batch size
var count = Math.Min(gg.TargetCount, 32);
```

This keeps the bot mining longer before the plan cycle ends and replan fires.

**Step 3:** Add a conditional Wander: if the last plan cycle resulted in a BlockNotFoundEvent, include Wander in the next plan. This requires checking WorldState.Facts for a recent blockNotFound fact:
```csharp
// In GatherItemDecompose:
bool hasBlockNotFound = state.Facts.TryGetValue("lastError:action", out var errAction)
    && errAction?.ToString() == "mine";
if (hasBlockNotFound)
{
    actions.Add(MakeAction("Wander", new { radius = 40, maxDistanceFromSpawn = 200 }));
}
```

### Acceptance Criteria
- [ ] Default gather plan is 3 actions (SearchMemory, MineBlock, GetStatus) — no Wander
- [ ] Wander only appears in plan after a BlockNotFound/mine error
- [ ] MineBlock batch count is capped at 32 per plan cycle
- [ ] Existing gather tests pass; add test: GatherPlan_NoBlockNotFound_OmitsWander
- [ ] Add test: GatherPlan_AfterBlockNotFound_IncludesWander

### Council Confidence: 75% (Seat 5 strongest advocate for this approach; Seat 3 agrees it helps but insists governor is also needed)

---

## P1-4: Minimal 2-State Replan Governor

### Problem
When the action queue empties, the agent loop replans unconditionally (subject only to the 2-second interval guard). When the environment has no resources to offer, this produces an infinite loop of identical plans. The "get 100 sand" test showed 300+ identical replan cycles over 10+ minutes.

### Root Cause (Verified)
- File: `WebUI.Blazor/AgentBackgroundService.cs`, method `DispatchActionsAsync`
- Replan triggers when: queue empty AND active goal AND no action dispatched this cycle
- No check for: "is this plan identical to the last one?" or "has the bot made any progress?"

### Design (Council-approved, Seat 3)
Implement a MINIMAL 2-state governor (not the full 4-state design — that is Sprint 20):

```csharp
public interface IReplanGovernor
{
    ReplanVerdict Evaluate(ReplanContext context);
    void RegisterPlan(string planFingerprint);
    void RecordProgress();
    void Reset();
}

public enum ReplanVerdict { Proceed, Defer, Stalled }

public record ReplanContext(
    int ConsecutiveIdenticalPlans,
    TimeSpan TimeSinceLastProgress,
    string CurrentPlanFingerprint
);
```

**State machine (2 states):**
- **ACTIVE**: Allow replanning. Track plan fingerprints. If 3+ consecutive identical plans with no inventory/position change, transition to STALLED.
- **STALLED**: Stop replanning. Log warning. Set goal fact `goal:{Name}:stalled = true`. Stay stalled until: (a) world state changes materially, (b) user issues new command, or (c) 60-second timeout expires and one more attempt is allowed.

**Plan fingerprint:** Hash of action type sequence + goal key (NOT action parameters, since Wander coordinates change):
```csharp
string Fingerprint(ActionPlan plan) =>
    $"{plan.Name}:{string.Join(",", plan.Actions.Select(a => a.Tool))}";
```

### Implementation Location
- New file: `Agent.Core/ReplanGovernor.cs` (implements IReplanGovernor)
- Modify: `WebUI.Blazor/AgentBackgroundService.cs` — inject IReplanGovernor, call Evaluate before replanning
- DI registration: `builder.Services.AddSingleton<IReplanGovernor, ReplanGovernor>();`

### Integration with Existing Systems
The governor absorbs `_consecutiveFailures` logic:
- `RecordProgress()` called when a progress-signal tool succeeds (resets fingerprint tracking)
- `Reset()` called in `SetGoal()` and `CancelGoal()`
- The existing `MinReplanIntervalSeconds = 2` remains as a hard floor

### Acceptance Criteria
- [ ] After 3 identical plan fingerprints with no progress, replan stops (STALLED state)
- [ ] STALLED state logs a clear warning message
- [ ] STALLED auto-recovers after 60 seconds (one retry attempt)
- [ ] New goal resets governor (ACTIVE state)
- [ ] Progress (inventory change, position delta > 5 blocks) resets fingerprint counter
- [ ] Add tests: Governor_ThreeIdenticalPlans_Stalls, Governor_ProgressResetsCounter, Governor_StalledAutoRecovery

### Council Confidence: 70% (Seat 3: 85%, Seat 1: 55%, Peer review: "ship minimal, not full design")

---

## P1-5: IItemAcquisitionRegistry Interface

### Problem
`ChatInterpreter.ItemAliases` is a flat dictionary that conflates user-intent aliasing, harvest mapping, and transformation chains. This works for simple items but fails for stone (where source block != drop item != goal item).

### Root Cause (Verified)
The flat alias table has no concept of:
- What the user MEANS by the word ("stone" = building material)
- What block to MINE (stone block in the world)
- What DROPS when mined (cobblestone)
- What TRANSFORMATION is needed (smelt cobblestone to get stone item)

### Design (Council Seat 2, 72% confidence)

```csharp
// New file: Agent.Core/IItemAcquisitionRegistry.cs
public interface IItemAcquisitionRegistry
{
    /// <summary>
    /// Resolve a user's natural-language item term to a canonical item ID.
    /// "stone" -> "cobblestone" (user wants building material)
    /// "wood" -> "oak_log"
    /// </summary>
    string ResolveUserIntent(string userTerm);

    /// <summary>
    /// Get the acquisition plan for a canonical item ID.
    /// Returns null if the item can be obtained by direct mining with no transformation.
    /// </summary>
    ItemAcquisitionPlan? GetPlan(string canonicalItemId);
}

public record ItemAcquisitionStep(
    string InputItemId,
    string OutputItemId,
    AcquisitionAction Action
);

public enum AcquisitionAction { Mine, Smelt, Craft }

public record ItemAcquisitionPlan(
    string GoalItemId,
    IReadOnlyList<ItemAcquisitionStep> Steps
);
```

### Sprint 19 Implementation (Flat Dict Backing)

```csharp
// New file: Agent.Core/StaticItemAcquisitionRegistry.cs
public sealed class StaticItemAcquisitionRegistry : IItemAcquisitionRegistry
{
    // Migrate ChatInterpreter.ItemAliases data here
    private static readonly Dictionary<string, string> UserIntentMap =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["wood"] = "oak_log", ["log"] = "oak_log",
        ["cobble"] = "cobblestone", ["cobblestone"] = "cobblestone",
        ["stone"] = "cobblestone", // User says "stone", they usually mean cobblestone
        // ... etc
    };

    public string ResolveUserIntent(string userTerm)
    {
        if (UserIntentMap.TryGetValue(userTerm, out var canonical))
            return canonical;
        // Fallback: normalize and passthrough
        return userTerm.ToLowerInvariant().Replace(' ', '_');
    }

    // Sprint 19: return null for all items (no transformation chains yet)
    // Sprint 20+: return actual plans for stone, iron, etc.
    public ItemAcquisitionPlan? GetPlan(string canonicalItemId) => null;
}
```

### Migration
1. Create new types in Agent.Core (zero breaking changes)
2. Create StaticItemAcquisitionRegistry backed by migrated ItemAliases data
3. Register in DI: `builder.Services.AddSingleton<IItemAcquisitionRegistry, StaticItemAcquisitionRegistry>();`
4. Mark `ChatInterpreter.ItemAliases` as `[Obsolete("Use IItemAcquisitionRegistry")]`
5. Update `ChatInterpreter.ResolveItemId` to delegate to the registry

### Acceptance Criteria
- [ ] IItemAcquisitionRegistry is registered in DI
- [ ] ChatInterpreter delegates to registry for item resolution
- [ ] All existing item aliases continue to work identically
- [ ] ItemAliases dictionary is marked [Obsolete]
- [ ] Add test: Registry_ResolveUserIntent_Stone_ReturnsCobblestone
- [ ] Add test: Registry_GetPlan_ReturnsNull_ForAllItems (Sprint 19 placeholder)

### Council Confidence: 72% (Seat 2 strongest advocate; Seat 5 says defer but agrees interface cost is low)

---

## P1-6: findFlatArea Radius Expansion + Max Retry

### Problem
"build house" triggers findFlatArea which consistently returns scan area=0 below minimum 25. The plan re-fires findFlatArea every 2 seconds indefinitely. The bot was in a sand/water area where no flat terrain exists nearby.

### Root Cause (Verified)
- File: `MineflayerAdapter/index.js`, the findFlatArea action
- The scan uses a fixed radius. If no flat area is found, it returns area=0 with no expansion or fallback.
- File: `Agent.Planning/HtnTaskLibrary.cs`, `DecomposeBuild` — when no origin is set, returns a single FindFlatArea action. This action fails, the plan completes, replan fires, same plan, same failure.

### Fix
**Step 1:** In `MineflayerAdapter/index.js`, modify findFlatArea to expand search:
```javascript
// In the findFlatArea handler:
async function findFlatArea(args) {
  const radii = [args.radius || 16, 32, 48]; // Expand search
  for (const radius of radii) {
    const result = scanForFlatArea(bot, radius, args.minArea || 25);
    if (result.area >= (args.minArea || 25)) {
      return result;
    }
  }
  // After all radii exhausted, return error instead of area=0
  return { error: 'no_flat_area', searchedRadii: radii, message: 'No suitable flat area found within search range' };
}
```

**Step 2:** In `AgentBackgroundService` or the replan governor, detect consecutive findFlatArea failures and stop retrying:
- After 2 consecutive findFlatArea results with area < minimum, set a WorldState fact: `flatArea:searchExhausted = true`
- `DecomposeBuild` checks this fact and returns an error plan rather than another FindFlatArea action

### Acceptance Criteria
- [ ] findFlatArea expands search radius on failure (16 -> 32 -> 48)
- [ ] After exhausting all radii, returns an error result (not area=0)
- [ ] Build goal does not endlessly retry findFlatArea after search exhaustion
- [ ] Add test: FindFlatArea_ExpandsRadius_OnFailure
- [ ] Add test: BuildGoal_StopsRetrying_AfterSearchExhausted

### Council Confidence: 88% (Seat 4 primary; no opposition)

---

## P2 — Design Only (Document, Do Not Implement)

### P2-7: Full 4-State Governor (Sprint 20)
Design from Seat 3. States: PLANNING → EXECUTING → BACKING_OFF → STALLED.
- Budget-based 1.5x exponential backoff, cap 30s, budget 8
- Progress defined by multi-signal: inventory delta, position delta > 5 blocks, action success rate > 25%
- Governor absorbs _consecutiveFailures and stall detection
- Separate injected IReplanGovernor service
Full interface spec is in the council review document.

### P2-8: GoalProgressTracker (Sprint 20)
Design from Seat 2. A 30-line ConcurrentDictionary that tracks per-goal mining progress independent of StatusEvent resets.
- INVARIANT: Must only be read in IsComplete(). Never use it to answer "what does the bot have."
- Dual check in IsComplete: tracker first, inventory snapshot second.
- Reset on SetGoal(), discard on CancelGoal().

### P2-9: JS-Side Greedy Mining Loop (Sprint 20)
In `index.js`, after mining a block, scan adjacent blocks of the same type and mine the nearest before returning control. This is a JS-only change that requires no C# planner changes.

### P2-10: NavigateTo Race Condition (Sprint 20)
Seat 4 identified: NavigateTo is fire-and-forget from C#, but pathfinding is async in JS. If stop fires before pathfinding setup completes, the bot twitches and stops. Needs investigation of the timing between C# action dispatch and JS pathfinder.setGoal().

---

## Code Verification Results

These facts were verified against the sprint-5-tool-safety branch HEAD (commit 7a125b1) via GitHub API:

| Claim | Status | Evidence |
|-------|--------|----------|
| stone aliased to cobblestone | CONFIRMED | ChatInterpreter.ItemAliases["stone"] = "cobblestone" |
| Projector increments by 1 | FIXED Sprint 15 | ApplyBlockMined uses e.Count, not hardcoded 1 |
| Gather = 4 actions | CONFIRMED | SearchMemory + Wander + MineBlock + GetStatus |
| Replan every 2s | CONFIRMED | MinReplanIntervalSeconds = 2, triggers on empty queue |
| Quantity propagated | CONFIRMED via PlannerRouter path | Sprint 18 fix in GatherGoalDecomposer; legacy HtnPlanner path still defaults to 10 |
| StatusEvent replaces inventory | CONFIRMED (by design) | ApplyStatus calls SetInventory() — this is CORRECT reconciliation |
| No system message filtering | CONFIRMED | No filter at JS adapter, ChatInterpreter, or LlmChatInterpreter |
| Build origin defaults to 0 | CONFIRMED | ReadOriginFact returns 0 if fact absent |

---

## Council Review Summary

### Seats and Confidence Ratings

| Change | Archivist | Data Model | Planner | Robustness | Skeptic | Peer Review |
|--------|-----------|------------|---------|------------|---------|-------------|
| Stone fix (P0) | 85% | - | - | - | MUST-DO | All agree |
| System filter (P0) | 80% | - | - | 92% | DEFER | Ship it |
| Batch size (P0) | - | - | - | - | MUST-DO | All agree |
| Governor (P1) | 55% | - | 85% | - | DEFER | Ship minimal |
| Item registry (P1) | 60% | 72% | - | - | DEFER | Ship interface |
| findFlatArea (P1) | - | - | - | 88% | - | Ship it |
| Inventory truth | 40% | 85% | - | - | Skip | Do not change StatusEvent |
| Cluster model | 35% | 45% | - | - | JS-side | Defer C# model |
| Bot fences | - | - | - | 90% | Sprint 22+ | Defer |

### Key Dissents
1. **Archivist dissents on Governor** — 2s guard adequate for MVP. Counter: Planner Specialist showed 25% confidence that decomposer-only fixes the spin.
2. **Archivist dissents on Inventory** — StatusEvent is reconciliation, not a bug. Counter: Data Modeler agrees but proposes GoalProgressTracker as complementary (scoped, not replacement).
3. **Skeptic dissents on all P1** — scope creep risk. Counter: Peer review showed the 6 changes are isolated (no two touch the same code path), mitigating big-bang risk.

### Peer Review Blind Spots
- Testing strategy for governor (recommend: unit test state machine with mock context, not integration test)
- Rollback plan (recommend: governor behind a `UseReplanGovernor` config flag)
- NavigateTo race condition (needs investigation before Sprint 20 implementation)
- Goal cancellation mid-execution (governor assumes goals run to completion or stall)

---

## Implementation Order

**Do these in sequence. Each builds on the previous and has its own test/CI checkpoint.**

1. P0-1: Stone alias fix (Agent.Planning/ChatInterpreter.cs + GenericGatherGoal.cs)
2. P0-2: System message filter (MineflayerAdapter/index.js)
3. P0-3: Gather batch + Wander conditional (Agent.Planning/HtnTaskLibrary.cs + GatherGoalDecomposer.cs)
4. P1-6: findFlatArea expansion (MineflayerAdapter/index.js + HtnTaskLibrary.cs)
5. P1-5: IItemAcquisitionRegistry (new files in Agent.Core + ChatInterpreter migration)
6. P1-4: Minimal governor (new file Agent.Core/ReplanGovernor.cs + AgentBackgroundService integration)

**After each change:** local build + test green, push, verify CI green. Do NOT batch multiple changes into one commit.

---

## Files to Read Before Implementation

The implementing agent MUST read these files before writing any code:

1. `Agent.Planning/ChatInterpreter.cs` — understand ItemAliases, ResolveItemId, GatherRegex
2. `Agent.Planning/Goals/GenericGatherGoal.cs` — understand IsComplete logic
3. `Agent.Planning/HtnTaskLibrary.cs` — understand GatherItemDecompose, DecomposeBuild
4. `Agent.Planning/Decomposition/GatherGoalDecomposer.cs` — understand TargetCount propagation
5. `WebUI.Blazor/AgentBackgroundService.cs` — understand dispatch loop, replan trigger, progress tracking
6. `Agent.Core/WorldStateProjector.cs` — understand ApplyBlockMined, ApplyStatus
7. `MineflayerAdapter/index.js` — understand chat handler, mine handler, findFlatArea
8. `Agent.Core/CommonMinecraftBlocks.cs` — understand DirectMineBlocks set
9. `AGENTS.md` — understand Rule 8 (warnings=errors) and existing conventions
10. `Directory.Build.props` — TreatWarningsAsErrors=true

---

## Testing Requirements

- All new code must have unit tests
- TreatWarningsAsErrors=true — no new warnings allowed
- Run full test suite locally before pushing
- CI must be green after each push
- Do NOT call SendEmergencyStop from SetGoal (AGENTS.md rule — test regression risk)

---

## Assumptions and Open Questions

### Assumptions
- A1: PlannerRouter (not raw HtnPlanner) is the active IPlanner in DI — confirmed by Sprint 18 behavior where "get 1 dirt" correctly passes count=1
- A2: Mineflayer's `bot.findBlock` provides adequate nearby-first block selection at the JS layer
- A3: The inventory "reset" in logs is explained by StatusEvent full-replacement timing, not a projector bug
- A4: The 10-minute spin is caused by resource exhaustion + unconditional replan, not adapter errors
- A5: findFlatArea area=0 means the bot is in terrain (sand/water) with no flat ground nearby

### Open Questions (for Sprint 20)
- Q1: Does the NavigateTo fire-and-forget create a race condition with the async JS pathfinder?
- Q2: Should cluster harvesting live in JS (greedy nearest-neighbor) or C# (spatial model)?
- Q3: When should the full item resolution chain (with smelting/crafting steps) be implemented?
- Q4: How should the bot handle goal interruption (user issues new command during active goal)?
- Q5: Should the governor support per-goal-type strategies (gather vs combat vs build)?

---

## Council Workflow Reminder

After implementation, run the standard council review:
1. Implement → local build/test → push
2. 6-seat MemorySmith Council Review (Source-Grounded Archivist, Data Model Architect, Retrieval Specialist, Human Learning Advocate, Skeptical Reviewer, Synthesizer)
3. Explicit dissent, per-seat confidence, blocking vs deferred triage
4. Write review to Data/Pages/council/sprint19-council-YYYYMMDD.md
5. Fix any blocking findings → verify CI green → proceed
