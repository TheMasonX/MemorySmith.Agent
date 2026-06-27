# Sprint 48 Handoff — Audit-Driven Corrections

**Branch:** `sprint-35-llm-first`  
**Date:** 2026-06-24  
**Status:** Complete ✅

## Summary

Sprint 48 implemented 3 audit-driven tasks from the Sprint 47 backlog, all targeting contract-drift and behavioral gaps identified in the 3 external audit reports.

## Tasks Completed

### TSK-0105 (P2) — Bot name whole-word detection

**Files changed:** `Agent.Planning/ChatInterpreter.cs`, `MemorySmith.Agent.Tests/Sprint48Tests.cs`

**Problem:** `IsDirectedAtBot` used `message.Contains(botName, StringComparison.OrdinalIgnoreCase)`, which matches substrings. A bot named `"Leo"` triggers on `"helios"`, `"Leopold"`, etc.

**Fix:** Replaced with compiled word-boundary regex (`\b{botName}\b`, case-insensitive, culture-invariant). The `MatchesBotName` static method uses `Regex.IsMatch` with `RegexOptions.IgnoreCase | RegexOptions.CultureInvariant`.

**Tests:** 5 new tests covering substring rejection, exact match, embedded-in-sentence, case-insensitive, and end-of-word false positives.

### TSK-0103 (P2) — MaxResponseDistanceBlocks wired into deterministic path

**Files changed:** `Agent.Planning/ChatInterpreter.cs`, `ChatOptions.cs` (already had the field)

**Problem:** `ChatOptions.MaxResponseDistanceBlocks` was only used by `LlmChatInterpreter` as a secondary gate. The deterministic `ChatInterpreter.IsDirectedAtBot` ignored it entirely — operators tuning the setting got no effect for the pattern-matching path.

**Fix:** 
- Added `_maxResponseDistanceBlocks` field to `ChatInterpreter`
- Extracted from `ChatOptions` in the constructor
- Added distance gate in `IsDirectedAtBot`: when the player is far and no other criteria matched, the message is rejected
- Solo players always bypass the distance gate (unchanged)

**Tests:** 4 new tests covering far multi-player (rejected), far solo (accepted), custom distance configuration, and near-within-distance behavior.

### TSK-0082 (P1) — Shared SmeltableMapping class

**Files changed:** `Agent.Planning/SmeltableMapping.cs` (new), `Agent.Planning/Goals/SmeltGoal.cs`, `Agent.Planning/HtnTaskLibrary.cs`

**Problem:** Smeltable item mappings (iron_ore → iron_ingot, etc.) were duplicated across 3 locations: `SmeltGoal.OutputItem` inline switch, `HtnTaskLibrary.DecomposeSmeltItem` inputBlock reverse switch, and `HtnTaskLibrary.IsMineableBlock` ore list. Drift risk on any update.

**Fix:** Created `SmeltableMapping` static class with:
- `InputToOutput` dictionary (input → smelted output)
- `OutputToInput` dictionary (reverse: output → raw input block)
- `SmeltableMineableBlocks` set (all mineable smeltable ores)
- Helper methods: `GetOutput()`, `GetInputBlock()`, `IsSmeltableMineableBlock()`

Updated `SmeltGoal.OutputItem` and `HtnTaskLibrary` references to delegate to the shared mapping.

**Tests:** 15 new tests covering all smeltable items, reverse mappings, mineable block checks, passthrough for unknowns, and SmeltGoal consistency with shared mapping.

## Validation

- **Build:** 0 warnings, 0 errors
- **Tests:** 705/705 passed, 0 failed (was 680, +25 new)
- **Commit:** Pending (will push after this handoff)

## Critical Review Items

1. **TSK-0105 regex performance:** The `MatchesBotName` method uses `Regex.IsMatch` (non-compiled) for each call. For a hot path like `InterpretAsync`, consider caching compiled regexes per bot name if this becomes a bottleneck. Current volume (player messages) is low enough that this is acceptable.

2. **TSK-0082 SmeltableMapping scope:** Only covers smeltable item mappings. Craftable item mappings remain in `AliasRegistry` and `HtnTaskLibrary` dictionaries — these are separate concerns and were not consolidated here.

3. **TSK-0103 distance gate edge case:** When `playerPosition` is `null`, the distance gate is skipped. This matches the existing `LlmChatInterpreter` behavior where null player position means distance is unknown.

4. **Backlog still has TSK-0084 (ApplySmeltComplete)** — this was partially absorbed by TSK-0117 (Sprint 47) but the task in the task service was not closed. Should be marked done.

## Recommended Next Sprint Order

1. **TSK-0085** (P3) — SmeltGoal.HasFailed dead code (quick cleanup)
2. **TSK-0093** (P1 deferred) — ParseItemSpec structured result
3. **TSK-0096** (P1 deferred) — Mining double-counting
4. **TSK-0083** (P3) — Checkpoint tests remainder
