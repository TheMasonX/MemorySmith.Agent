# Sprint 37 Council Review — 2026-06-22

**Branch:** sprint-35-llm-first | **Version target:** v0.37.0  
**Council seats:** Source-Grounded Archivist, Data Model Architect, Retrieval Specialist,
Human Learning Advocate, Skeptical Reviewer, Synthesizer

---

## Changes Under Review

**P0-A** ActionOutcome implements IObservationSummary (one-liner)  
**P0-B** Wire DispatchActionsAsync → CallWithOutcomeAsync; add IToolCaller.CallWithOutcomeAsync default; remove redundant journal entry from ToolDispatcher.CallAsync  
**P1-A** IntentManager class: extracted intent→goal mapping from ParseDecision  
**P1-B** LlmChatInterpreter.ParseDecision delegates to IntentManager when injected  
**P1-C** IntentAssessment record: { IntentDraft, RiskLevel, RequiresConfirmation, ReasoningSummary }  
**Tests** Sprint37Tests.cs — 10 new tests

---

## Seat Reviews

### 1. Source-Grounded Archivist (confidence: 0.92)

**Verdict: APPROVE**

All claims verified against source:

- `ActionOutcome : IObservationSummary` — one-liner, correct. `IObservationSummary.Summary => ObservationSummary` is explicit interface implementation, won't pollute the record's public surface. ✓
- `IToolCaller.CallWithOutcomeAsync` default method — uses `async Task<(...)>` which is valid C# 8+ default interface method syntax. All existing implementors automatically inherit the default. ✓
- ToolDispatcher.CallAsync: removing the final `_journal?.Log(entry)` (ActionCompleted/ActionFailed) is safe. Error entries (validation failure, exception, unknown tool) are untouched. ✓
- DispatchActionsAsync: replacing `toolCaller.CallAsync` with `toolCaller.CallWithOutcomeAsync(Guid.Empty, ...)` + `_journal?.LogOutcome(outcome)` eliminates the two manual journal entries (ActionCompleted from success path, ActionFailed from failure path). Timeout/exception journal entries are preserved since they occur outside CallAsync. ✓
- IntentManager: switch statement matches exactly what was in ParseDecision. GoalRequest record is new and clean. ✓
- IntentAssessment: record fields match Sprint 36 locked architecture. ✓

**Concern (deferred):** `Guid.Empty` as goalId is a placeholder. Tracked: IGoal.Id property needed in Sprint 38.

---

### 2. Data Model Architect (confidence: 0.90)

**Verdict: APPROVE**

P0-A is correct. The explicit interface implementation `string IObservationSummary.Summary => ObservationSummary;` is the right pattern when the property name differs (ObservationSummary ≠ Summary). This avoids polluting `ActionOutcome`'s public API with a second `Summary` property.

P0-B IToolCaller interface change is non-breaking. The default method pattern means:
- Existing test doubles (StubToolCaller, MockWorldAdapter) compile unchanged.
- ToolDispatcher.CallWithOutcomeAsync already matches the interface signature exactly → implicit implementation. No `override` keyword needed for records/classes with matching signatures.

P1-A: GoalRequest is a proper value object. Parameters as `IReadOnlyDictionary<string, object?>?` is consistent with existing GoalFactory / ChatInterpretation patterns. ✓

P1-C: IntentAssessment uses `sealed record` — correct for an immutable value object. RiskLevel enum is simple and sufficient for Sprint 37. ✓

**Deferred finding D-1:** GoalRequest could eventually replace `(string GoalName, IReadOnlyDictionary<string, object?>?)` used in ChatInterpretation — considered out of scope for Sprint 37.

---

### 3. Retrieval Specialist (confidence: 0.88)

**Verdict: APPROVE**

LlmChatInterpreter change is well-structured. The two-path approach (IntentManager when injected, legacy local switch when not) gives a clean migration path without breaking existing tests.

Confirmed: LlmChatInterpreterTests.cs does not inject IntentManager (it tests ChatInterpreter and LlmChatInterpreter without this parameter) → all existing tests exercise the legacy path → no regression.

Sprint37Tests.cs verifies the new path directly by injecting an IntentManager instance.

**Concern (non-blocking):** TryParseTruncatedJson still has the inline switch. This is explicitly documented as "Sprint 38 target" in the code. Acceptable for Sprint 37.

---

### 4. Human Learning Advocate (confidence: 0.87)

**Verdict: APPROVE**

PRINCIPLE-1 enforcement is significant. The key insight the code communicates well:
- ParseDecision no longer "knows" what a goal is — it asks IntentManager
- The legacy path is clearly labeled and has a target removal sprint
- IntentAssessment provides the semantic scaffolding for future confirmation gates

One readability improvement (deferred, not blocking): `Guid.Empty` as goalId in DispatchActionsAsync is not self-documenting. The comment `// Sprint 37: goalId placeholder until IGoal.Id is defined` is sufficient for now.

---

### 5. Skeptical Reviewer (confidence: 0.83)

**Verdict: APPROVE with deferred findings**

**D-2 (deferred):** The IToolCaller default method could fail silently if an implementor doesn't override it and CallAsync returns a non-standard ToolResult (null Message). The default uses `result.Message ?? "Success"` / `result.Message ?? "Failed"` — acceptable fallback.

**D-3 (deferred):** The sprint notes say "Remove redundant journal entries from CallAsync per-tool path." Strictly, this means removing the SUCCESS/FAILURE entry. But CallAsync's exception and validation-failure journal entries are NOT removed — this is INTENTIONAL because they log different semantics (error detail vs. structured outcome). Confirm the distinction is understood: exception path keeps `_journal?.Log(exEntry)` because LogOutcome only fires for completed calls, not thrown exceptions. ✓

**D-4 (non-blocking):** `GoalRequest.Parameters` as `IReadOnlyDictionary<string, object?>?` uses the same pattern as existing code but `object?` is weakly typed. Future consideration: typed parameter records.

---

### 6. Synthesizer (confidence: 0.90)

**Verdict: APPROVE — SPRINT 37 READY**

Average confidence across 6 seats: **0.88** (above 0.85 threshold).  
**0 blocking findings.**  
**4 deferred findings (D-1 through D-4)** — all confirmed non-blocking.

The sprint delivers:
1. ActionOutcome : IObservationSummary — closes P0-A, enables LLM observation pipeline
2. CallWithOutcomeAsync on IToolCaller + DispatchActionsAsync wiring — closes P0-B, eliminates double-journal
3. IntentManager — closes P1-A, first PRINCIPLE-1 compliant intent routing
4. ParseDecision delegation — closes P1-B, backwards-compatible migration
5. IntentAssessment — closes P1-C, risk assessment scaffolding for Sprint 38
6. 10 new tests

**Sprint 38 priorities (from deferred findings):**
- IGoal.Id property (D-goalId)
- Remove TryParseTruncatedJson legacy switch (D-3 follow-up)
- Remove ParseDecision legacy path (D-2 follow-up)
- Wire IntentManager into Program.cs DI

---

## Acceptance Criteria

All items must be verified before marking Sprint 37 complete:

- [ ] Build passes locally (`dotnet build` on sprint-35-llm-first)
- [ ] `dotnet test` — 10 new Sprint37Tests all pass, no regressions
- [ ] `ActionOutcome` implements `IObservationSummary` — `is` check passes
- [ ] `IToolCaller.CallWithOutcomeAsync` — default implementation compiles on stub implementor
- [ ] `IntentManager.BuildGoalRequest("gather", item="oak_log")` returns `GatherItem:oak_log`
- [ ] `IntentManager.BuildGoalRequest("conversation")` returns null
- [ ] `DispatchActionsAsync` no longer contains duplicate ActionCompleted/ActionFailed journal entries
- [ ] `ToolDispatcher.CallAsync` no longer emits its own success/failure journal entry
- [ ] Version: `/api/about` reports v0.37.0
- [ ] CI green on sprint-35-llm-first
