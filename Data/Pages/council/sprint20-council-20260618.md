# Sprint 20 Council Review — Runtime Failures & Planning Recovery

**Date:** 2026-06-18
**Reviewer Council:** 6-seat MemorySmith review panel
**Subject:** Sprint 20 — Progress-hash governor, LLM truncation recovery, system message filter expansion
**CI status at review:** GREEN — e38e2ef97f2bf7ec87d854175e9872f8f810a13d (all tests passing)
**Deferred from Sprint 19:** D-2 (BlockNotFound → Wander conditional), D-3 (LastFlatArea fact), D-7 (governor recovery log)

---

## Changes Under Review

| Area | File | Change |
|------|------|--------|
| P0-A | `WebUI.Blazor/AgentBackgroundService.cs` | Replace per-tool RecordProgress with cycle-settle inventory-delta check; add `_cycleInventorySnapshot`; update STALL log |
| P0-B | `WebUI.Blazor/AgentBackgroundService.cs` | GetStatus injection on SetGoal — REVERTED (broke existing tests; deferred) |
| P0-C | `MineflayerAdapter/index.js` | 3 new SYSTEM_MESSAGE_PATTERNS: /clear, /clear-alt, /give-alt |
| P0-D | `Agent.Planning/LlmChatInterpreter.cs` | Add TryParseTruncatedJson; restore 6-param constructor (ChatHistory, ILogger); logger/history calls |
| P0-D | `Agent.Planning/Llm/OllamaProvider.cs` | Add OllamaOptions + wire LlmMaxResponseTokens to num_predict |
| P0-D | `Agent.Planning/Llm/ChatOptions.cs` | Add LlmMaxResponseTokens = 300 |
| Docs | `Data/Pages/Guides/sprint20-audit-20260618.md` | Runtime failure audit + Gemini+GPT architectural review |
| Tests | `MemorySmith.Agent.Tests/Sprint20Tests.cs` | 3 test classes: governor progress-hash, LLM truncation, system message filter |

---

## Seat 1: Source-Grounded Archivist (Confidence: 82%)

The audit document accurately captures the runtime log evidence. Key findings are grounded:

- The 18-action plan cycling every 2s loop is confirmed in logs (00:31:04 through 00:31:41).
- The stale inventory false-completion is confirmed: "leo get 5 dirt" → immediate completion while agent had no items.
- The teleport message reaching LLM is confirmed in both session logs.
- The LLM JSON truncation is confirmed with the partial JSON snippets shown.

One gap: the audit claims "The ReplanGovernor's STALL never fires because Wander returns 0ms OK and resets the counter" — this was the Sprint 19 diagnosis. Sprint 20 fixed it by moving RecordProgress to the settle block. The audit now serves as the pre-fix baseline, not post-fix. Consider adding a "Status After Sprint 20" section to the audit document in a follow-up.

**Finding A-1 (DEFERRED):** Audit document missing "Sprint 20 resolution" section confirming which issues were fixed.

---

## Seat 2: Data Model Architect (Confidence: 85%)

### Governor change: inventory-delta progress detection

The change correctly removes the per-tool RecordProgress call and replaces it with a cycle-settle comparison:

```csharp
var currentInventorySum = _worldState.Inventory.Values.Sum();
if (_cycleInventorySnapshot >= 0 && currentInventorySum != _cycleInventorySnapshot)
{
    replanGovernor?.RecordProgress();
    logger.LogDebug("[governor] progress detected — inventory Σ {Before}→{After} ...",
        _cycleInventorySnapshot, currentInventorySum);
}
_cycleInventorySnapshot = currentInventorySum;
```

The -1 sentinel for "not yet initialized" is clean and avoids false-positives on the first cycle.

**Concern:** The inventory delta only fires when the SUM changes, not when composition changes. If the bot mines 1 dirt and places 1 stone in the same cycle, the sum is identical and progress is not recorded. This edge case is unlikely in practice but theoretically possible during build phases.

**Finding A-2 (DEFERRED):** Inventory delta uses sum comparison. Could miss same-sum composition changes during simultaneous mine+place. Consider a hash of (sorted item list) instead of sum in a future sprint.

### GetStatus injection deferred

The GetStatus injection into SetGoal was removed because it caused test failures. The underlying problem (stale inventory causing false goal completion) is now UNADDRESSED by Sprint 20. The deferred status is appropriate but should be tracked.

**Finding B-1 (BLOCKING — tracked as Sprint 21 work):** Stale inventory false-completion is NOT fixed in Sprint 20. After admin `/clear`, new goals with stale inventory will still complete instantly. This was the original P0-B requirement. Must be addressed in Sprint 21 with proper test support.

---

## Seat 3: Retrieval Specialist (Confidence: 78%)

### LlmChatInterpreter: TryParseTruncatedJson

The method correctly extracts `addressed` and `intent` from truncated JSON using field-level regexes. The regex patterns are well-formed:

- `@"""addressed""\s*:\s*""(?<v>[^""]+)""` → matches `"addressed": "value"` ✓
- `@"""intent""\s*:\s*""(?<v>[^""]+)""` → matches `"intent": "value"` ✓
- `@"""response""\s*:\s*""(?<v>[^""\\]*(?:\\.[^""\\]*)*)"""` → matches response value ✓

**Note on Sprint 20 process:** The file went through multiple encoding corruption cycles during the sprint. The root cause was that subagents were reading the file as raw text, then passing it through JSON encoding that converted C# verbatim `""` escapes to C-style `\"` escapes. Resolution: direct paramsFile push via mcp__t__ExecuteIntegration bypassing agent intermediaries. This is a known hazard for future sessions with files containing C# verbatim strings.

**Finding C-1 (DEFERRED):** TryParseTruncatedJson does not handle the `"gather"`, `"build"`, or `"navigate"` intents — only cancel/status/help/clarify. For truncated gather commands, the user will see "Didn't catch that" instead of the gathered goal. The method's XML doc comments this intentionally; a future sprint should add parameter-free goal creation for truncated gather/build cases.

### System message filter expansion

Three new patterns added to SYSTEM_MESSAGE_PATTERNS:
```js
/^Removed\s+\d+\s+items?\s+from\s+/i
/^Cleared\s+\S+/i
/^Gave\s+\S+\s+\d+\s+/i
```

The first pattern was corrected from `items?` to `item` (removing the `\s+from\s+` suffix) after a test failure proved `item(s)` didn't match `items?`.

**Finding C-2 (DEFERRED):** The `^Cleared\s+\S+` pattern is broad and could match player messages like "Cleared out the area for you". Low probability in practice, but a more specific pattern like `^Cleared\s+\S+\s+inventory` or `^Cleared\s+\d+` would be safer.

---

## Seat 4: Human Learning Advocate (Confidence: 88%)

The visibility improvements are the most valuable part of Sprint 20 from a human operator perspective:

1. **Governor STALL log now includes inventory sum** — operators can see whether items are actually being collected.
2. **LLM log calls restored** — `[llm] calling ollama...`, `[llm] returned null`, `[llm] failed to parse JSON` are back in the runtime output after being lost during the Blaze encoding corruption.
3. **Audit document** — the comprehensive audit gives future sessions a clear baseline.

**Gap:** D-7 from Sprint 19 remains open — "Add `[governor] recovered via progress — replanning resumed` log line." When the governor exits STALL due to RecordProgress, there is no log line confirming recovery. Operators cannot tell from logs when the stall was cleared.

**Finding D-1 (DEFERRED = Sprint 19 D-7 carried forward):** No log line when governor exits STALL due to RecordProgress. Should be: `logger.LogInformation("[governor] progress detected — stagnation counter reset ({Before}→{After})")` — which IS present in the new code! D-7 is actually RESOLVED by the new progress log in the settle block. Confirm this is sufficient.

Upon review: the new code has:
```csharp
logger.LogDebug("[governor] progress detected — inventory Σ {Before}→{After} (stagnation counter reset)", ...)
```
This IS the D-7 recovery log, but it's at `LogDebug` (file-only in Sprint 19 Serilog config). Operators watching console output won't see it. Consider elevating to `LogInformation`.

**Finding D-2 (DEFERRED):** Recovery log is at LogDebug, not LogInformation. Operators watching console can't confirm stall was cleared.

---

## Seat 5: Skeptical Reviewer (Confidence: 71%)

### Sprint process concerns

Sprint 20 required 13 commits (A through M) to fix encoding corruption issues. The root cause was agents corrupting C# verbatim string escapes when patching files via MCP. This should be codified as a project rule.

**Finding E-1 (BLOCKING):** AGENTS.md needs a new rule: "Never patch C# files with verbatim strings via agent file-read-then-write. Use mcp__t__ExecuteIntegration with paramsFile (raw text, MCP handles base64). Agent intermediaries corrupt `""` escapes to `\"`."

### Deferred GetStatus injection

The P0-B requirement (stale inventory gate) was scoped for Sprint 20 but was removed due to test regression. This creates a false sense of completeness — the sprint goal checklist included this fix but it was not delivered. Future sprints should track this explicitly.

**Finding E-2 (DEFERRED):** Sprint 20 handoff must clearly mark P0-B (GetStatus/inventory freshness) as DEFERRED, not delivered. Tests for this behavior must be written before the feature is re-implemented.

### RecordProgress timing race

The new inventory-delta check runs at the END of the settle block (after 300ms). During those 300ms, `ProcessEventsAsync` is updating `_worldState.Inventory` from incoming `blockMined` events. There is no synchronization between `_worldState` writes (ProcessEventsAsync) and `_worldState` reads (DispatchActionsAsync settle block).

`_worldState` is a record (immutable snapshots), written as full reference replacements. The race is: DispatchActionsAsync reads `_worldState.Inventory` while ProcessEventsAsync is writing a new `_worldState`. Due to the single-reference-swap pattern, partial reads are not possible, but DispatchActionsAsync might see either the old or new reference depending on CPU ordering.

In practice, .NET memory model guarantees visibility for reference writes within the same process (weakly ordered but coherent for single-reference writes). This is likely safe in practice but is a theoretical concern.

**Finding E-3 (DEFERRED):** `_worldState` reference read from multiple tasks without explicit synchronization. Low risk in practice (C# memory model + .NET GC), but worth adding a `volatile` or `Interlocked` note in a future sprint.

---

## Seat 6: Synthesizer (Overall Confidence: 81%)

### Sprint 20 delivers

Sprint 20 successfully addresses the PRIMARY runtime failure: the perpetual replan stall. The governor now correctly tracks whether the world state actually changed between plan cycles. This is the most impactful single fix — without it, the bot loops forever.

The system message filter expansion and LLM truncation recovery are incremental improvements that address real observed behavior from the session logs.

### Blockers requiring resolution

**B-1 (BLOCKING from Seat 2):** P0-B (stale inventory false-completion) is undelivered. Must be tracked explicitly in the handoff as "Sprint 21 P0."

**E-1 (BLOCKING):** AGENTS.md rule for verbatim string files. Must be added before next session to prevent repeat encoding corruption.

### Deferred findings (for sprint 21+)

| ID | Finding | Priority |
|----|---------|---------|
| A-1 | Audit doc missing "Sprint 20 resolution" section | Low |
| A-2 | Inventory delta uses sum, not composition hash | Low |
| C-1 | TryParseTruncatedJson doesn't handle gather/build/navigate | Medium |
| C-2 | `^Cleared\s+\S+` pattern could match player messages | Low |
| D-1 | D-7 was Sprint 19 — confirmed RESOLVED by new settle log | - |
| D-2 | Governor recovery log at Debug, not Info | Medium |
| E-2 | Handoff must clearly mark GetStatus deferred | Medium |
| E-3 | _worldState multi-task reference race (theoretical) | Low |

### Acceptance criteria verification

| # | Criterion | Status |
|---|-----------|--------|
| T1 | Governor stall fires after 3 identical cycles with no inventory change | VERIFIED (Sprint20GovernorProgressTests) |
| T2 | Governor NOT stalled when inventory increases | VERIFIED (Sprint20GovernorProgressTests) |
| T3 | GetStatus enqueued on SetGoal | REVERTED — deferred to Sprint 21 |
| T4 | Stale inventory gate | NOT DELIVERED — Sprint 21 |
| T5 | System message filter: /clear response filtered | VERIFIED (Sprint20SystemMessageFilterTests) |
| T6 | Player messages not filtered | VERIFIED (Sprint20SystemMessageFilterTests) |
| T7 | LLM JSON truncation recovery | VERIFIED (Sprint20LlmTruncationTests, 4 tests) |
| T8 | No PlanAsync during STALL period | VERIFIED by existing governor logic (Evaluate returns Stalled) |

---

## Council Verdict

**APPROVED WITH CONDITIONS**

Blocking findings that must be resolved before Sprint 21 proceeds:

1. **B-1:** Add P0-B (GetStatus/inventory freshness) to Sprint 21 as explicit P0 requirement with failing test written first.
2. **E-1:** Add AGENTS.md rule against agent-patching of C# verbatim string files.

These are low-effort, documentation-only fixes that do not require code changes. They can be resolved as part of the Sprint 20 handoff document.

**Approved items (CI green, tests passing):**
- Progress-hash governor (P0-A)
- LLM truncation recovery (P0-D)
- OllamaProvider num_predict (P0-D)
- System message filter expansion (P0-C)
- Sprint 20 audit document

---

*Council review conducted per MemorySmith Agent development protocol.*
*CI verification commit: e38e2ef97f2bf7ec87d854175e9872f8f810a13d*
