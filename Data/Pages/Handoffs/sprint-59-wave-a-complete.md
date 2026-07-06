# Sprint 59 — Wave A Complete (Prediction Pipeline)

**Date:** 2026-07-06  
**Branch:** `dev/round-3`  
**HEAD:** `eceb01e` — Sprint 59 Wave A: Prediction pipeline fixes  
**Agent:** SteveBot  

---

## What Was Delivered

### ✅ Immediate Tasks (Completed)

| Task | Title | Files Changed | Key Fix |
|------|-------|-------------|---------|
| **TSK-0338** | Fix HtnTaskLibrary DI constructor defect | `Agent.Planning/HtnTaskLibrary.cs` | Consolidated dual constructors → single constructor that always populates `_methods` with all 11 task decomposers. Previously .NET DI silently selected the empty-`_methods` constructor because `ILogger<T>` is always resolvable. |
| **TSK-0342** | Fix ReplanGovernor graduated backoff off-by-one | `Agent.Core/ReplanGovernor.cs` | (1) Fixed off-by-one so first stall reads 5s (tier 0), not 10s (tier 1). (2) Auto-recovery no longer resets `_stallAttempt` — only `RecordProgress()`/`Reset()` clear it. Backoff now escalates: 5→10→20→30→30s. |

### ✅ Wave A Tasks (Completed)

| Task | Title | Files Changed | Key Fix |
|------|-------|-------------|---------|
| **TSK-0336** | Fix WorldModel.Predict tool-name domain mismatch | `Agent.Core/Models/WorldModel.cs` | Normalizes tool name to lowercase before switching. Accepts both PascalCase `ITool.Name` values (`"MineBlock"`, `"CraftItem"`) and lowercase wire-protocol names (`"mine"`, `"craft"`). Previously every tool call except `"place"` fell through to `PredictUnknown`. |
| **TSK-0344** | Add WorldStateDiff unexpected-inventory-change detection | `Agent.Core/Models/WorldStateDiff.cs` | Added `HasUnexpectedChanges` property. `HasInventoryMismatch` now checks this first. `DescribeMismatches()` reports unexpected changes. |

### ✅ Extended Existing Tasks (Completed)

| Task | Extension | Files Changed |
|------|-----------|-------------|
| **TSK-0309** | Wire-protocol name normalization for Predict | Folded into TSK-0336 above |
| **TSK-0320** | Add TaskSequenceGoal branch to evaluator context | `Agent.Planning/LlmEvaluatorImpl.cs` — new `if (goal is TaskSequenceGoal seq)` branch recurses into the active step's type-specific context |

### Validation

- `dotnet build` — **0 warnings, 0 errors**
- `dotnet test` — **815/815 passed, 0 failed**

---

## Council Context (from 2026-07-05 Review)

The 6-seat council reviewed ~21 consolidated P0/P1 findings across both repos and sequenced them into 4 waves. **Key council decisions and corrections:**

### Corrected Findings
- **P0-1 (PlaceBlockGoal data race)**: Already fixed by TSK-0330 (Sprint 58 Wave D) — removed from active status
- **F1 severity**: Skeptical Reviewer challenged Critical/97% → **High/85%** (missing concrete call trace; "place works" weakens the claim). Synthesizer ruled to keep at P0 due to systemic impact
- **F2 (dead precondition)**: Downgraded from High to **Moderate** (dead code = zero runtime harm)
- **F16 overlap**: TSK-0100 already addressed reconnect; may be different code path → included as Wave D with verification gate

### 4-Wave Sequencing

| Wave | Focus | Tasks | Status |
|:-----|:------|:------|:-------|
| **Immediate** | Security triage + self-contained fixes | TSK-0338, TSK-0342 | ✅ Done |
| **Wave A** | Prediction pipeline | TSK-0336, TSK-0344, TSK-0309 ext, TSK-0320 ext | ✅ Done |
| **Wave B** | Inventory & evaluation | TSK-0321, TSK-0325, (inventory sync, circuit breaker, step context) | 🔲 Next |
| **Wave C** | Safety & config hardening | TSK-0340, TSK-0341, TSK-0324, TSK-0318 | 🔲 Next |
| **Wave D** | Adapter resilience | TSK-0337, TSK-0339 (WebSocketBridge, Node adapter reconnect) | 🔲 Next |
| **Sprint 60+** | Deferred | TSK-0343 (EntityObservedEvent→StructuredFacts) | 🔜 Future |

---

## Remaining MSA Tasks (Sprint 59)

### Wave B — Inventory & Evaluation (4 tasks)

| Task | Title | Status | Notes |
|------|-------|--------|-------|
| **TSK-0321** | Remove idle-only guard on inventory sync; add 60s active-goal interval | Ready | Currently inventory sync only fires when `_currentGoal is null`. Needs to also fire during active goals at 60s interval. |
| **TSK-0325** | Replace circuit-breaker counter-reset-to-0 with 60s cooldown | Ready | After 3 consecutive LLM evaluator failures, enter 60s cooldown instead of resetting counter to 0. |
| **TSK-0320** (remaining) | Check WorldStateDiff before evaluator fast-path | Done | Was partially addressed — the `diff.HasMismatch` check already existed. The TaskSequenceGoal branch was added. The WorldStateDiff wiring in `AppendGoalContext` needs verification. |
| *Step context* | Add step-specific context in evaluator LLM prompt | Not tracked | Ensure `AppendGoalContext` enriches evaluator prompt with step progress. |

### Wave C — Safety & Config Hardening (4 tasks)

| Task | Title | Status | Notes |
|------|-------|--------|-------|
| **TSK-0340** | Stub evaluator diagnosis channel — add `EvaluationResult.Diagnosis` field | Backlog | Add `string? Diagnosis` field to `EvaluationResult`, populate in `LlmEvaluatorImpl` from Reason/Suggestion. Full consumption deferred to Sprint 60. |
| **TSK-0341** | Fix `PlaceBlockGoalDecomposer` same-coordinate placements | Backlog | Decomposer places all N blocks at identical coordinates. Either offset subsequent placements (y+1) or make count-driven (1 action with count=N). |
| **TSK-0324** | Change `DeniedCommands` to additive-only merge | Ready | Currently config `["kill"]` can remove `/ban`, `/stop`, `/execute` from denied set. Need additive-only semantics. |
| **TSK-0318** | Complete denylist normalization (both sides) | Backlog | Strip leading slash from both sides of comparison so `/ban` matches `ban` consistently. |

### Wave D — Adapter Resilience (2 tasks)

| Task | Title | Status | Notes |
|------|-------|--------|-------|
| **TSK-0337** | Fix `WebSocketBridge` reconnect retry loop | Backlog | After first failed reconnect, `_ws.State != Open` causes `ReceiveLoopAsync` to return immediately (clean shutdown exit), skipping remaining retries. Need to distinguish "socket never opened" from "connection closed cleanly." |
| **TSK-0339** | Fix Node adapter reconnect exponential backoff | Backlog | `_reconnectAttempts = 0` reset at start of `connectBot()` unconditionally, before connection succeeds. Move reset to `'spawn'` handler. Don't log "reconnected successfully" until spawn fires. |

### Cross-Cutting Retry/Backoff Pattern

Three independent retry mechanisms were found with escalation bugs, all now fixed:
1. ✅ **ReplanGovernor** (TSK-0342, C#) — Fixed: first stall reads 5s, backoff escalates
2. 🔲 **WebSocketBridge** (TSK-0337, C#) — Still open: retry loop exits after 1 failure
3. 🔲 **Node adapter reconnect** (TSK-0339, JS) — Still open: exponential backoff defeated by premature counter reset

---

## MemorySmith Repo (Cross-Repo Tasks)

The MemorySmith repo (`dev/sprint-1` branch) has its own set of tasks from the council synthesis. The MS council created 14 tasks (TSK-0288 through TSK-0301) and pushed a pre-work commit including:

- `.gitignore` updates (`artifacts/` added)
- Secret files removed from git tracking (`.vscode/mcp.json`, `appsettings.LocalOverrides.json`)
- Pre-commit hook extended for secret pattern detection
- Memory promotions (Working → Core)
- InstanceName branding across Razor components

### MS Immediate Tasks (P0, <1hr each)

| Task | Title | PR |
|------|-------|----|
| TSK-0288 | Rotate secrets, fix .gitignore, add pre-commit hook | Already staged on `dev/sprint-1` |
| TSK-0290 | Partition login rate limiter by client IP | 🔲 |
| TSK-0293 | Fix TreeSitter C# key mismatch (`"c_sharp"` → `"CSharp"`) | 🔲 |
| TSK-0294 | Scrub dead search tool references from README + wiki guides | 🔲 |
| TSK-0298 | Fix training harness warmupSteps default (0→10) + docstring | 🔲 |

### MS Sprint Tasks (P1)

| Task | Title | PR |
|------|-------|----|
| TSK-0289 | Gate OAuth first-admin bootstrap (reuse SecurityServices pattern) | 🔲 |
| TSK-0291 | Add global `[AutoValidateAntiforgeryToken]` MVC filter | 🔲 |
| TSK-0292 | Add schema migration framework to SqliteMemorySmithDatabase | 🔲 |
| TSK-0295 | Add TaskStatuses.All/TaskPriorities.All validation sets | 🔲 |
| TSK-0296 | Consolidate FixedTimeEquals into shared helper (3 copies → 1) | 🔲 |
| TSK-0297 | Delete 10 dead methods from ChatServices.cs | 🔲 |
| TSK-0299 | Fix SplitThinking, silent catch, validation error clobbering | 🔲 |
| TSK-0300 | Add total auth self-lockout guardrail | 🔲 |
| TSK-0301 | Delete MemoryIndex (dead code with live race risk) | 🔲 |

---

## Task Status Summary

| Scope | Total | Done | InProgress | Ready/Backlog |
|-------|-------|------|-----------|---------------|
| MSA Immediate | 2 | 2 | 0 | 0 |
| MSA Wave A | 4 | 4 | 0 | 0 |
| MSA Wave B | 3 | 0 | 0 | 3 |
| MSA Wave C | 4 | 0 | 0 | 4 |
| MSA Wave D | 2 | 0 | 0 | 2 |
| MSA Sprint 60+ | 1 | 0 | 0 | 1 |
| **MS Tasks** | **14** | **0** | **0** | **14** |

---

## Key Files and References

### Council/Audit Documents
- `Data/Pages/Audits/council-review-consolidated-audits-7-5-26.md` — Full 6-seat council review
- `Data/Pages/Audits/msa_sprint58_deep_audit_dev_round3_20260701_1500_CT.md` — F1-F9 detailed findings
- `Data/Pages/Audits/msa_sprint58_deep_audit_delta2_dev_round3_20260701_1830_CT.md` — F10-F14
- `Data/Pages/Audits/msa_sprint58_deep_audit_delta3_dev_round3_20260701_2100_CT.md` — F15-F17
- `Data/Pages/Audits/msa_sprint58_deep_audit_delta4_dev_round3_20260701_2330_CT.md` — F18-F20

### Modified Source Files (Wave A)
- `Agent.Planning/HtnTaskLibrary.cs` — Single constructor with populated `_methods`
- `Agent.Core/ReplanGovernor.cs` — Off-by-one fix + escalated backoff
- `Agent.Core/Models/WorldModel.cs` — Tool name normalization in Predict
- `Agent.Core/Models/WorldStateDiff.cs` — HasUnexpectedChanges property
- `Agent.Planning/LlmEvaluatorImpl.cs` — TaskSequenceGoal branch

### New Task Records (Created by council, all Backlog)
- `Data/Tasks/tsk-0336-*` through `tsk-0344-*`

---

## Handoff Notes

1. **Branch strategy**: MSA work is on `dev/round-3`. MS work is on `dev/sprint-1` (already pushed with pre-work).

2. **Council dissent to be aware of**: Skeptical Reviewer challenged several severity ratings. The F1 (Predict mismatch) was kept at P0 but the dissent is noted. F16 (WebSocketBridge) may overlap with TSK-0100 — verify before implementing.

3. **MS cross-repo coordination**: Agent Smith (MS side) should reference the MS task set (TSK-0288 through TSK-0301) and the `council/audit-synthesis-council-20260705.md` document in the MS repo. SteveBot (MSA side) handles only MSA tasks.

4. **Validation gates**: Each wave has specific acceptance criteria in the council document. For Wave B, ensure inventory sync fires during active goals at 60s. For Wave C, ensure DeniedCommands is additive-only. For Wave D, verify WebSocketBridge retries at least 6 times in test.

5. **Dead code removal**: ChatServices.cs still has 10 dead methods (TSK-0297 on MS side) that should be deleted. These are orphaned pre-decomposition implementations.

6. **Vec3 shim**: The MineflayerAdapter has a 231-line hand-rolled Vec3 shim (`vec3.js`) duplicating an already-installed npm dependency. Not yet tracked as a task — consider for a future sprint.
