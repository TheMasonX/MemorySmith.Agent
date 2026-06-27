# Sprint 38 Council Review — 2026-06-22

**Branch:** sprint-35-llm-first | **HEAD at review open:** 0f5b12ebf54e3867858bfaf76b4da0e75a2db2d2 ("Sprint 37 audits")  
**Council seats:** Source-Grounded Archivist, Data Model Architect, Retrieval Specialist, Human Learning Advocate, Skeptical Reviewer, Synthesizer  
**Verdict: APPROVED — 1 runtime-bug blocking (P0-resolvable in-session)**

---

## Audit Material Under Review

Three independent audit files were uploaded in commit 0f5b12eb ("Sprint 37 audits") plus a two-round audit-response chain and a v2 fix plan:

| File | Focus | Snapshot |
|------|-------|---------|
| `Data/Pages/Audit/MemorySmith.Agent_Audit_2026-06-22.md` | Sprint 36 deliverables review | Pre-Sprint-37 code state |
| `Data/Pages/Audit/memorysmith_agent_handoff_audit_20260622T164312Z.md` | Sprint 37 handoff audit | `main` branch (not sprint-35-llm-first) |
| `Data/Pages/Audit/memorysmith_agent_repo_review_2026-06-22T000000Z.md` | Current repo review | sprint-35-llm-first HEAD |
| `Data/Pages/Handoff/sprint-37-audit-response-20260622.md` | Refutation of correlation-model claim + v2 plan | sprint-35-llm-first confirmed |
| `Data/Pages/Plans/sprint-37-fix-plan-v2-20260622.md` | 4-issue surgical fix plan (v2) | Ready for implementation |

---

## Audit Finding Triage

### Audit 1 (MemorySmith.Agent_Audit_2026-06-22.md) — reviewed Sprint 36 state

| Finding | Status | Evidence |
|---------|--------|----------|
| 1. Tool-name prompt enrichment not wired in DI | **CLOSED in Sprint 37** | ce439c41: `sp.GetRequiredService<IntentManager>()` wired; RegisteredNames injected via Sprint 36 cb1c56fe |
| 2. `ToolDispatcher.All` loses alias names, nondeterministic | **CLOSED in Sprint 36** | `RegisteredNames` added (5c26e4b9); sorted, includes aliases |
| 3. `CallWithOutcomeAsync` not in dispatch loop | **CLOSED in Sprint 37** | 9e4c28b6: `DispatchActionsAsync` uses `CallWithOutcomeAsync(Guid.Empty, ...)` |
| 4. Duplicate journal entries when new path adopted | **CLOSED in Sprint 37** | db6fd62c: success/failure entries removed from `CallAsync`; `LogOutcome` is sole source |
| 5. Parser still creates goal names | **PARTIAL** — legacy switch remains | 04f26687: fallback retained; Sprint 38 P0 target |
| 6. `ItemConsumedEvent` stub; inventory optimistic | **OPEN** — deferred | 9b2948f5: explicitly "Sprint 37 when full crafting recipe integration arrives" |
| 7. Missing `CallWithOutcomeAsync` + `ItemCraftedEvent` tests | **CLOSED in Sprints 36-37** | c1ea0cd7: `CallWithOutcomeAsync_*` 2 tests; 9b2948f5: `ItemCraftedEvent_*` 2 tests |
| 8. `/api/about` stale (v0.28.0 / Sprint 33) | **CLOSED in Sprint 37** | ce439c41: bumped to v0.37.0 |
| 9. World state coupling — no provenance contract | **DEFERRED** | Multi-sprint architectural concern |
| 10. `HtnTaskLibrary` retry gate depends on stringly-typed fact | **OPEN — NEW** | Sprint 38 tracking item |

**Audit 1 net: 6 CLOSED, 1 PARTIAL (Sprint 38 P0), 2 OPEN/DEFERRED, 1 new tracking item.**

### Audit 2 (memorysmith_agent_handoff_audit_20260622T164312Z.md) — reviewed `main` branch (NOT sprint-35-llm-first)

This audit reviewed the public `main` tree, which has never received any of the sprint-35-llm-first commits. Its high-confidence findings that `IntentManager`, `ActionOutcome`, and `CallWithOutcomeAsync` don't exist are **REFUTED** — they all exist on the feature branch.

| Finding | Status | Verdict |
|---------|--------|---------|
| "IntentManager not in repo tree" | REFUTED | af4ebd06 adds `Agent.Planning/IntentManager.cs`; wired in ce439c41 |
| "ActionOutcome not visible" | REFUTED | cbe70577 adds `: IObservationSummary` — confirms the record exists |
| "IToolCaller only exposes CallAsync" | REFUTED | 5ea45e38 adds `CallWithOutcomeAsync` as default interface method |
| LLM not the sole intent authority (hybrid system) | **VALID** | Legacy switch retained per 04f26687; Sprint 38 P0 removes it |
| Background service still owns too many responsibilities | **VALID DEFERRED** | Sprint 38 P3 (AgentRuntime decomposition) |
| Negative-path tests missing | **VALID** | Sprint 38 test additions required |

**Audit 2 net: 3 REFUTED (stale snapshot), 3 VALID findings captured.**

### Audit 3 (memorysmith_agent_repo_review_2026-06-22T000000Z.md) — most current review

| Finding | Status | Verdict |
|---------|--------|---------|
| Hybrid decision system; LLM not yet sole intent authority | **VALID — transitional** | Legacy switch removal is Sprint 38 P0 |
| Schema validator minimal (object/type/properties/required only) | **VALID DEFERRED** | Adequate for current tools; deeper validation Sprint 39+ |
| Alias collision: `Register(string, ITool)` silently overwrites | **VALID NEW** | Add `LogWarning` on overwrite; Sprint 38 P4 |
| Duplicate journaling risk | **CLOSED in Sprint 37** | db6fd62c + 9e4c28b6 eliminated the double-log |
| World/agent KB split conditional | **VALID DEFERRED** | WorldKbUrl null-default documented; deeper enforcement Sprint 39+ |
| ToolDispatcher is now a hard safety boundary | **CONFIRMED STRENGTH** | — |
| IntentManager wired in Program.cs | **CONFIRMED STRENGTH** | ce439c41 |

**Audit 3 net: 1 CLOSED (duplicate logging), 2 VALID NEW, 2 VALID DEFERRED, 2 CONFIRMED STRENGTHS.**

### v2 Fix Plan (sprint-37-fix-plan-v2-20260622.md) — 4 runtime bugs not addressed in Sprint 37

These issues were carried over from Sprint 35 runtime testing and are confirmed production-blocking:

| Issue | Severity | Root Cause | Fix Complexity |
|-------|----------|-----------|----------------|
| **A**: Inventory resets mid-gather | Critical | `GetStatus` in gather plan calls `ApplyStatus` which **replaces** entire inventory, wiping additive `ApplyBlockMined` contributions | 1 line in `HtnTaskLibrary.cs` + 3 lines in `AgentBackgroundService.cs` |
| **B**: MineBlock stops after first dispatch | Critical | `BlockMinedEvent` handler never calls `CompleteCorrelatedActionByTool("MineBlock")` → fire-and-forget guard permanently blocks next dispatch | Converges with Issue A fix (same handler) |
| **C**: FindFlatArea picks distant tower | High | First scan (radius=30) returns area=0 even on flat ground (unloaded chunks); proximity weighting only helps when >0 candidates exist | All-adapter: direct ground-height seed, chunk radius +1→+2, distance log |
| **D**: `bot._client.chat` error | Medium | Defensive only — `bot.chat()` is already correct API; previous error was old code | 1 try/catch in adapter |

**The correlation model (`_correlatedActions`, `ActionLifecycle`, `CompleteCorrelatedActionByTool`) exists on the branch (Sprint 25 P0-D) and is confirmed by audit-response-1 with line-level citations. Issues A and B are 3 files, ~38 lines total. These are production-blocking and must be Sprint 38 P0.**

---

## Seat Reviews

### 1. Source-Grounded Archivist (confidence: 0.91)

**Verdict: APPROVE with blocking P0**

All triage verdicts verified against commit log and file contents. Three key confirmations:

- `BlockMinedEvent` handler (AgentBackgroundService.cs, lines 403-408) has an explicit comment "don't transition yet — the mine loop may continue" but no `CompleteCorrelatedActionByTool("MineBlock")` call. The v2 plan's diagnosis is correct. ✓
- `GatherItemDecompose` in `HtnTaskLibrary.cs` ends with `actions.Add(MakeAction("GetStatus"))` — confirmed source of inventory wipe. The one-line removal is the correct minimal fix. ✓
- Sprint 37's 10 tests do NOT include any test for Issue A or B. The runtime bugs are genuinely unaddressed. ✓

**Blocking finding (BLK-1):** Issues A+B must be fixed before Sprint 38 is considered shippable. The v2 plan's two-change surgical fix (remove GetStatus from gather + add CompleteCorrelatedActionByTool + clear stale flag) is the narrowest safe path. Tests required: gather-no-GetStatus, BlockMinedEvent-clears-stale, BlockMinedEvent-completes-correlation, gather-goal-via-blockMined-only.

---

### 2. Data Model Architect (confidence: 0.88)

**Verdict: APPROVE**

Model concerns addressed:

1. **GoalRequest record** (IntentManager) uses `IReadOnlyDictionary<string, object?>?` — consistent with existing ChatInterpretation pattern. Long-term D-1 from Sprint 37 council (typed parameter records) remains deferred. Acceptable.

2. **IntentAssessment** is `sealed record` — correct for an immutable value object. `RiskLevel { Low, Medium, High }` is adequate scaffolding; confidence vs risk separation is correctly modeled.

3. **IGoal.Id as Guid** — correct type. The `Guid.Empty` placeholder in `DispatchActionsAsync` is a technical debt to close in Sprint 38 P1.

4. **Inventory event-sourcing completeness**: `ItemCollectedEvent` ✓, `ItemCraftedEvent` ✓ (additive), `ItemConsumedEvent` ✗ (stub). Until ItemConsumedEvent is wired, inventory is optimistic between craft and status reconciliation. This is acceptable IF all callers document the assumption.

**Deferred finding D-1:** `ItemConsumedEvent` wiring must land same sprint as any feature that depends on accurate ingredient counts. Mark as Sprint 38 P4 with explicit pre-requisite note.

---

### 3. Retrieval Specialist (confidence: 0.87)

**Verdict: APPROVE**

LLM intent pipeline assessment:

1. **PRINCIPLE-1 enforcement is 70% complete.** `IntentManager.BuildGoalRequest` is the canonical path and is wired in DI. The legacy switch in `LlmChatInterpreter.ParseDecision` is explicitly labeled "Sprint 38 target: remove it." This is controlled technical debt, not drift.

2. **`TryParseTruncatedJson` still has an inline switch** — not migrated to `IntentManager` yet. This is the second Sprint 38 P0 leg.

3. **`IChatInterpreter.InterpretAsync` signature**: currently returns `ChatInterpretation` (contains `GoalName`). Changing it to return `IntentDraft` directly (Sprint 38 P0-C) removes the last goal-string escape path from the parser.

4. **Tool-name injection to LLM**: `RegisteredNames` is now passed to `LlmChatInterpreter`. The LLM receives a sorted, alias-aware list. ✓ The `Registered tools: ...` system prompt line is active.

5. **World KB routing**: `SearchMemoryTool` and `CreatePageTool` route to world KB; `GetPageTool` routes to agent KB. The split is intentional and documented. The conditional fallback when `WorldKbUrl` is null is acceptable transitional behavior.

---

### 4. Human Learning Advocate (confidence: 0.85)

**Verdict: APPROVE**

Readability and learnability review:

1. **The `Guid.Empty` goalId placeholder** is clearly labeled in source (comment added in 9e4c28b6). Once `IGoal.Id` is added in Sprint 38 P1, every `ActionOutcome` will carry a real correlation key. Until then, outcomes are ungrouped. Acceptable for Sprint 37/38 boundary.

2. **Audit 2's concern about AgentBackgroundService size** (~1400 lines / 80KB per Sprint 35 notes) is valid. The AgentRuntime decomposition (Sprint 38 P3) will address this, but it's complex enough that it should be deferred if runtime bugs take more time.

3. **Test coverage of failure edges**: The three audits independently call out missing negative-path tests (schema failure, ambiguous chat, outcome errors). These should be Sprint 38 P4 alongside ItemConsumedEvent tests.

4. **AGENTS.md should be updated** to document the IntentManager → GoalRequest → GoalFactory pipeline, the FactSource expansion, and the correlation model's role in Issue B resolution.

**Deferred D-2**: AGENTS.md update for Sprint 38 P4 (low effort, high value for future agents).

---

### 5. Skeptical Reviewer (confidence: 0.83)

**Verdict: APPROVE with deferred findings**

Challenges and concerns:

**D-3 (alias collision risk, Audit 3 Finding 3):** `ToolDispatcher.Register(string, ITool)` silently overwrites any existing registration. The current codebase has one intentional overwrite ("Status" → "GetStatus"). But a future agent adding a tool named "status" (different casing or intent) would silently replace it with no warning. Add `if (_tools.ContainsKey(name)) _logger?.LogWarning("[dispatcher] Overwriting registration for '{Name}'", name);` before the assignment. Small change, high defensive value.

**D-4 (Issue C diagnostic gap):** The v2 plan's FindFlatArea fix (ground-height seed, chunk radius +2, distance log) may not fully resolve the zero-area root cause if it's a Mineflayer version-specific `blockAt()` null return. The diagnostic additions (direct ground hit log, winning candidate distance) will confirm or refute this at runtime. Explicitly: if Issue C is not resolved by the ground-height seed, a follow-up sprint will need to examine chunk loading callbacks rather than the radius.

**D-5 (stale-flag thread safety, from audit-response Open Question 3):** `_worldState.With(b => b.SetInventoryStale(false))` assignment in `ProcessEventsAsync` is not synchronized with `DispatchActionsAsync`. This is a pre-existing pattern used for health, position, and inventory throughout the file. The last-writer-wins is acceptable given the event-sourced single-writer model, but it should be noted for any future move to a concurrent execution model.

**D-6 (`Guid.Empty` in ActionOutcome):** Until `IGoal.Id` is added (Sprint 38 P1), all `ActionOutcome` records carry `goalId = Guid.Empty`. Any downstream consumer that groups outcomes by goal will silently treat all outcomes as belonging to the same goal. The Sprint 37 P2-B TODO comment documents this. Sprint 38 P1 closes it.

---

### 6. Synthesizer (confidence: 0.90)

**Verdict: APPROVE — Sprint 38 READY after BLK-1 resolved in-session**

Average confidence across 6 seats: **0.874** (above 0.85 threshold).

**1 blocking finding:** BLK-1 (Issues A+B — inventory reset + MineBlock stop) must be resolved in Sprint 38. The fix is fully specified in `sprint-37-fix-plan-v2-20260622.md`: 3 files, ~38 lines total, 4 new tests. This is a clear P0.

**Audit cross-verification summary:**
- 3 new audits uploaded. Audit 1 reviewed Sprint 36 state — 6 of 10 findings already closed by Sprint 37 deliverables. Audit 2 reviewed `main` branch — 3 of 5 high-confidence findings are REFUTED (code exists on sprint-35-llm-first). Audit 3 is the most current — 2 new valid findings (alias collision, hybrid decision system), 1 confirmed closure (duplicate logging fixed Sprint 37).
- The v2 fix plan's runtime bugs are confirmed production-blocking by multiple independent paths.
- Sprint 37 delivered all 5 stated items (P0-A, P0-B, P1-A, P1-B, P1-C) with 10 tests, council-approved at 0.88.

---

## Sprint 38 Priorities (Consolidated)

### P0 — Runtime Bug Fix (BLK-1)
- [ ] `HtnTaskLibrary.cs`: Remove `MakeAction("GetStatus")` from `GatherItemDecompose` (1 line)
- [ ] `AgentBackgroundService.cs`: Add stale-flag clear + `CompleteCorrelatedActionByTool("MineBlock")` + diagnostic warning log in `case BlockMinedEvent e:` (~12 lines)
- [ ] `index.js`: Direct ground-height seed before scan; chunk radius +1→+2; winning-candidate distance log; chat try/catch (~30 lines)
- [ ] 4 new tests: gather-no-GetStatus, BlockMinedEvent-clears-stale, BlockMinedEvent-completes-correlation, gather-goal-via-blockMined-only

### P1 — Complete PRINCIPLE-1 Enforcement (from Sprint 37 deferred)
- [ ] Remove legacy switch from `LlmChatInterpreter.ParseDecision`
- [ ] Remove legacy switch from `TryParseTruncatedJson` (migrate to `IntentManager.BuildGoalRequest`)
- [ ] Change `IChatInterpreter.InterpretAsync` to return `IntentDraft` directly (removes `ChatInterpretation.GoalName`)

### P2 — IGoal.Id (closes Guid.Empty placeholder, from Sprint 37 deferred)
- [ ] Add `Guid Id { get; }` to `IGoal` interface
- [ ] Replace `Guid.Empty` in `DispatchActionsAsync.CallWithOutcomeAsync(Guid.Empty, ...)` with `_currentGoal.Id`

### P3 — Observation-Driven Replanning (Sprint 37 P2-B TODO)
- [ ] Accumulate `outcomes[]` in `DispatchActionsAsync` dispatch loop
- [ ] Define `ILlmEvaluator.EvaluateAsync(IGoal goal, ActionOutcome[] outcomes)` → bool
- [ ] Wire `ILlmEvaluator` at Sprint 37 P2-B TODO comment site

### P4 — Test Coverage + Small Fixes (from audit deferred findings)
- [ ] `ItemConsumedEvent` wiring: `WorldStateProjector.ApplyItemConsumed` deducts ingredients from inventory (D-1)
- [ ] Negative-path tests: schema failure, unknown tool, ambiguous chat, outcome-correlation with Guid.Empty (Audit 2 + 3)
- [ ] `ToolDispatcher.Register`: add `LogWarning` on name collision/overwrite (D-3, Audit 3)
- [ ] AGENTS.md: document IntentManager pipeline, FactSource expansion, correlation model (D-2)

### P5 — DEFERRED to Sprint 39
- [ ] AgentRuntime decomposition (`AgentBackgroundService.ExecuteAsync → AgentRuntime.TickAsync()`) — complex, defer if P0 takes time
- [ ] Deeper JSON Schema validation (nested schemas, format constraints)
- [ ] World/agent KB split enforcement (conditional WorldKbUrl → explicit error or config validation)

---

## Acceptance Criteria

All items must be verified before marking Sprint 38 complete:

- [ ] `dotnet build` passes on sprint-35-llm-first
- [ ] `dotnet test` — all Sprint 38 P0 tests pass; no regressions
- [ ] `GatherItemDecompose` output does NOT contain a `GetStatus` action
- [ ] `BlockMinedEvent` handler clears `IsInventoryStale` and calls `CompleteCorrelatedActionByTool("MineBlock")`
- [ ] Runtime Test 1: `gather 10 dirt` — inventory increments monotonically, no mid-gather wipe, goal completes
- [ ] Runtime Test 2: `build a house` — first scan returns area > 0 on flat ground
- [ ] Legacy switch removed from `ParseDecision` and `TryParseTruncatedJson`
- [ ] `IChatInterpreter.InterpretAsync` returns `IntentDraft`
- [ ] `IGoal.Id` property added; `Guid.Empty` placeholder replaced
- [ ] CI green on sprint-35-llm-first

---

## Deferred Findings Tracker

| ID | Priority | Finding | Sprint |
|----|----------|---------|--------|
| D-1 | Sprint 38 P4 | `ItemConsumedEvent` wiring (inventory optimistic until status reconcile) | 38 |
| D-2 | Sprint 38 P4 | AGENTS.md update for IntentManager pipeline + correlation model | 38 |
| D-3 | Sprint 38 P4 | `ToolDispatcher.Register` alias collision LogWarning | 38 |
| D-4 | Sprint 38 (watch) | Issue C (FindFlatArea zero-area) may need follow-up if ground-seed doesn't fix it | 38 |
| D-5 | Sprint 39 | `_worldState` assignment not synchronized across loops (pre-existing pattern) | 39 |
| D-6 | Sprint 38 P2 | `Guid.Empty` in ActionOutcome until `IGoal.Id` added | 38 |
| D-7 | Sprint 39 | Schema validator depth (nested schemas, format constraints) | 39 |
| D-8 | Sprint 39 | World/agent KB split enforcement | 39 |
| D-9 | Sprint 39 | AgentRuntime decomposition (80KB BackgroundService split) | 39 |
| D-10 | Sprint 39 | Typed parameter records (GoalRequest.Parameters as `object?` is weakly typed) | 39 |
