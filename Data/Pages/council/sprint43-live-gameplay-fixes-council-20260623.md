# Council Review: Sprint 43 Live Gameplay Fixes

## Decision
Fix 7 P0/P1 issues identified from live logs before any new feature work. Prioritize correctness (checkpoint fidelity, coordinate rounding, timeout tuning) over new capabilities.

## Evidence Reviewed
- Live adapter logs from 2026-06-23 (PlaceBlock timeout, bot returning to origin, LLM misinterpretation)
- Source files: `AgentBackgroundService.cs`, `index.js`, `HtnTaskLibrary.cs`, `GoalFactory.cs`, `LlmChatInterpreter.cs`, `CommonMinecraftBlocks.cs`, `IntentManager.cs`
- Sprint 42 council report (`sprint42-placement-hygiene-council-20260623.md`)
- Previous audits and handoff documents

## Seat Findings (Condensed Council)

| Seat | Key Finding | Confidence |
|---|---|---|
| **Combined Skeptic + Architect** | 10 issues identified. 3 critical (checkpoint skip, LLM prompt, navigate handler). 3 high (wool, origin warp, rounding). 4 medium (timeout, stale position, dual-path, double-enqueue). | **0.91** |

## Priority Matrix

| # | Issue | Severity | Fix Complexity | Action |
|---|---|---|---|---|
| **P0-1** | Fast-path "navigate" in LlmChatInterpreter (prevents LLM overriding "come here" → "cancel") | Critical | Low | Add "navigate" to short-circuit check at LlmChatInterpreter line ~118 |
| **P0-2** | Add "navigate" docs to LLM prompt | Critical | Low | Add examples in INTENT RULES section |
| **P0-3** | Selective CancelGoal in navigate handler (don't StopNow for every navigate) | Critical | Low | Wrap CancelGoal in conditional |
| **P0-4** | Terrain collision skip must NOT advance checkpoint (TSK-0074/TSK-0075 gap) | Critical | Low | Emit separate event from index.js; handle in ABS without advancing checkpoint |
| **P1-1** | Wool/white_wool alias + DirectMineBlocks | High | Low | Add alias in IntentManager + add to DirectMineBlocks |
| **P1-2** | Proximity-gated MoveTo in build plan | High | Low | Check distance before enqueuing MoveTo(origin) |
| **P1-3** | botPos() use Math.floor() instead of Math.round() | High | Low | Change botPos function |
| **P1-4** | PlaceBlock timeout 2s → 5s (match doc intent) | Medium | Low | Change timeout value |
| **P2-1** | Stale player position for navigate | Medium | Medium | Re-query position at dispatch time |
| **P2-2** | Document/handle navigate dual-path | Low | Low | Add code comments |

## Recommended Changes

### P0-1: Fast-path "navigate" in LlmChatInterpreter
**File:** `Agent.Planning/LlmChatInterpreter.cs`  
**Change:** Add "navigate" to the short-circuit check alongside cancel, status, help at line ~118
```csharp
if (quick is { Intent: "cancel" or "status" or "help" or "navigate" })
```
**Why:** The deterministic pattern matcher (`ChatInterpreter`) correctly maps "come here" → navigate. But this result is currently only used as a fallback when the LLM returns null. Adding "navigate" to the short-circuit ensures the pattern match wins for zero-risk operations.

### P0-2: Document "navigate" in LLM prompt
**File:** `Agent.Planning/LlmChatInterpreter.cs`  
**Change:** Add to INTENT RULES:
```
• "navigate" — when the player says "come here", "come to me", "follow me", "go to"
  Set coords to null for "come here" — the system uses the player's current position.
```

### P0-3: Selective CancelGoal in navigate handler
**File:** `WebUI.Blazor/AgentBackgroundService.cs`  
**Change:** Replace unconditional `CancelGoal()` with conditional:
```csharp
// Only stop if there's an active conflicting goal
// For simple navigates while idle/wandering, just enqueue the move
if (_currentGoal is not null && !IsNonConflictingWithNavigate(_currentGoal))
    CancelGoal();
```
And always clear the queue before enqueuing MoveTo.

### P0-4: Terrain collision skip → new event
**File:** `MineflayerAdapter/index.js` + `WebUI.Blazor/AgentBackgroundService.cs`  
**Change:** Emit `blockPlaceSkipped` (not `blockPlaced`) when terrain occupies position. Add C# handler that completes correlation but does NOT advance checkpoint.

### P1-1: Wool alias
**File:** `Agent.Core/CommonMinecraftBlocks.cs` + `Agent.Planning/IntentManager.cs`  
**Change:** Add `"white_wool"` to DirectMineBlocks. Add `"wool"` → `"white_wool"` alias in IntentManager.

### P1-2: Proximity-gated MoveTo
**File:** `Agent.Planning/HtnTaskLibrary.cs`  
**Change:** Before enqueuing MoveTo(origin) in DecomposeBuild, check if bot position (from state) is within BUILD_SITE_PROXIMITY blocks of origin.

### P1-3: Math.floor() for botPos
**File:** `MineflayerAdapter/index.js`  
**Change:** Replace `Math.round()` with `Math.floor()` in botPos function.

### P1-4: PlaceBlock timeout 5s
**File:** `WebUI.Blazor/AgentBackgroundService.cs`  
**Change:** `["PlaceBlock"] = 2` → `["PlaceBlock"] = 5`

## Evidence Gate
All P0 changes must pass `dotnet test` (608 tests) before merge. P1+ changes should also pass.

## Open Questions
1. Should Math.floor(-0.5) behavior be tested explicitly? (floor(-0.5) = -1, which is correct for -0.5 entity coords)
2. Should build proximity threshold be configurable via appsettings?

**Confidence: 0.88**
