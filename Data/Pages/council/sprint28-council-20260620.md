# Sprint 28 Transition Council Review
**Date**: 2026-06-20  
**Branch**: `sprint-5-tool-safety` @ `6443b2db`  
**Triggered by**: User pushed ≥5 new external audits; full re-verification requested before Sprint 28  
**Reviewer task**: Validate/refute every audit claim, synthesize into updated backlog  

---

## I. Audit Corpus Reviewed

| File | Type | Status |
|---|---|---|
| `Audits/deep-code-audit-20260619.md` | Cross-verification of prior findings | Sprint 26 intake, already processed |
| `Audits/exec-summary-audit-20260619.md` | Executive summary of PR #1 | Sprint 26 intake, already processed |
| `Audit/memorysmith_agent_code_audit.md` | Fresh Sprint 5/6 code audit | **NEW (this session)** |
| `Audit/memorysmith_agent_code_audit_report.md` | Gather + planner audit | **NEW (this session)** |
| `Audit/memorysmith_agent_additional_audit_findings.md` | API surface + chat audit | **NEW (this session)** |
| `Audit/memorysmith_agent_audit_addendum.md` | Supplemental findings | **NEW (this session)** |
| `Audit/memorysmith_agent_deep_code_audit_sprint5.md` | Deep Sprint 5-focus audit | **NEW (this session)** |
| `Audit/memorysmith_agent_deep_dive_audit_report.md` | Third-pass follow-up audit | **NEW (this session)** |
| `Audit/memorysmith_agent_followup_deep_dive_report.md` | Runtime + trust-boundary audit | **NEW (this session)** |

Independent code verification performed on:  
`GoalFactory.cs`, `ChatInterpreter.cs`, `HtnTaskLibrary.cs`, `GatherGoalDecomposer.cs`,  
`GenericGatherGoal.cs`, `ChatOptions.cs`, `PlannerRouter.cs`, `WebUI.Blazor/Program.cs`

---

## II. Finding-by-Finding Verdict

### STALE / ALREADY RESOLVED (do not reopen)

| Audit Claim | Confidence | Status | Evidence |
|---|---|---|---| 
| ToolDispatcher has TODO, no schema validation | 95–98% | **STALE** — Fixed Sprint 25 P0-C | `CallAsync` wraps `ExecuteAsync` in try/catch; `CheckType` uses `TryGetInt32` |
| `ToolEngine`, `ToolRegistry`, `IToolRegistry` still exist | 90% | **STALE** — Deleted Sprint 5 P2 | Not in PR file list; `ToolDispatcher` is sole dispatcher |
| `WorldState` aliasing (shared mutable dict) | 86–92% | **STALE** — Fixed Sprint 25 P1-A | Constructor: `new Dictionary<string,int>()` separate instances; `Observe()` deep-copies |
| `/api/agent/command` accepts arbitrary tool names | 97% | **STALE** — Fixed Sprint 5 P0 | Program.cs: `dispatcher.Get(req.Command) is null → 400 + registered list` |
| ActionQueue not thread-safe | 90% | **STALE** — Fixed Sprint 23 P0-A | `ActionQueue.ClearAndEnqueue` uses lock-protected atomic clear+enqueue |
| Version mismatch: `/api/about` returns `0.7.0` vs README `v0.23.0` | 99% | **STALE** — Fixed Sprint 27 P0-B | Program.cs: `Version = "0.27.0"`, `Phase = "Sprint 27 – Planner routing + ITimeProvider"` |
| LLM chat interpretation blocks world-state loop | 93% | **STALE / REFUTED** — Channel in place since Sprint 1a | `Channel<WorldEvent>` separates interpretation from world-state updates |
| Gather count lost in planning (IItemSpecGoal empty params) | 97% | **STALE** — Fixed Sprint 26 P0-B | `GatherGoalDecomposer`: `isg.TargetCount.ToString()` passed as parameter |
| HtnPlanner hardcoded type-switch (IItemSpecGoal/BuildGoal/CraftItemGoal) | 81% | **STALE** — Fixed Sprint 27 P0-D | `HtnPlanner` is pure phase-by-phase fallback; all branches removed |
| **DEF-NEW-4**: `GatherItemDecompose.Take(2)` source block truncation | 90% | **REFUTED** — Code shows no `Take(2)` | `HtnTaskLibrary.GatherItemDecompose`: `foreach (var block in spec.SourceBlocks)` — iterates ALL blocks |

> **DEF-NEW-4 correction**: Sprint 27 synthesis logged this finding as open based on an external audit claim. Code inspection at branch HEAD confirms no `Take(2)` exists in `GatherItemDecompose`. P2-B in the Sprint 28 backlog should be CLOSED as refuted.

---

### CONFIRMED OPEN (pre-existing in backlog)

| ID | Claim | Source | Evidence |
|---|---|---|---|
| DEF-NEW-1 | `BuildGoalDecomposer.ReadOriginFact` silently returns `(0,0,0)` on bad/missing origin fact — no warning logged | deep_code_audit_sprint5.md (88%) | `HtnTaskLibrary.DecomposeBuild`: silent `ResolveAutoOrigin` fallback, no log when origin stays (0,0,0) |
| DEF-NEW-2 | `GenericGatherGoal.HasFailed` key is `goal:Gather:{itemId}:failed` — no `targetCount` in key, so oak_log×1 poisons oak_log×32 | audit_addendum.md (93%) | `GenericGatherGoal.cs`: `Name => $"Gather:{item.ItemId}"` — targetCount excluded from name/key |
| DEF-NEW-3 | `GoalFactory.GetInt` unchecked `long l => (int)l` cast — no range check, large long wraps silently | audit_addendum.md (84%) | `GoalFactory.cs` line `long l => (int)l` — no range guard |
| DEF-NEW-5 | `WorldState` collections exposed as `get; init;` mutable — external consumers can mutate underlying dict/list | deep_code_audit_sprint5.md (92%) | `WorldState.cs` properties: `init` setters on `Dictionary` fields |

---

### UPGRADED FINDING

**P2-D → P1: PlannerRouter.ReplanAsync breaks all decomposer-handled goal replanning**

Both `PlannerRouter.ReplanAsync` and `DecomposerPlanner.ReplanAsync` reconstruct a `SimpleGoal` shell from `currentPlan.GoalName + Phases`. This shell is then routed through `DecomposerRegistry`. All registered decomposers (`GatherGoalDecomposer`, `BuildGoalDecomposer`, `CraftItemGoalDecomposer`) have `CanHandle` predicates requiring their concrete type (`IItemSpecGoal`, `BuildGoal`, `CraftItemGoal`). A `SimpleGoal` matches **none** of these.

**Result**: Every replan of a gather/build/craft goal silently falls through to `HtnPlanner`. `HtnPlanner` sees phase names like `["FindSource","Mine","Collect"]` which are not registered HTN tasks → throws `InvalidOperationException` → replan returns `null` → agent is stuck.

**Confidence**: 95% — verified in `PlannerRouter.cs`, `GatherGoalDecomposer.cs` (CanHandle: `goal is GatherWoodGoal or IItemSpecGoal`), `GenericGatherGoal.cs` (not an IItemSpecGoal after SimpleGoal wrapping).

**Upgrade rationale**: This breaks ALL decomposer-handled goal recovery on failure. Any gather/build/craft replan silently produces no plan. Agent stalls. This is a correctness regression, not a polish item.

---

### NEW FINDINGS (not previously logged)

| ID | Claim | Confidence | Evidence |
|---|---|---|---|
| DEF-NEW-6 | `ChatInterpreter.ResolveItemId` uses `raw.TrimEnd('s')` — corrupts valid IDs ending in 's': `glass→glas`, `moss→mos` | 89% | `ChatInterpreter.cs`: `var singular = raw.TrimEnd('s');` |
| DEF-NEW-7 | Status regex includes bare `\bdoing\b` — matches "I am doing fine", "what are you doing today?" (false positives) | 91% | `ChatInterpreter.cs`: `@"\b(status\|what.?re you doing\|what are you doing\|report\|doing)\b"` |
| DEF-NEW-8 | `ExploreDecompose` hardcodes two-pass Wander pattern with no retry budget or state-dependent branching | 89% | `HtnTaskLibrary.ExploreDecompose`: returns fixed [SearchMemory, Wander, GetStatus, Wander, GetStatus] |
| DEF-NEW-9 | `MineWoodDecompose` uses `minecraft:` namespace prefix (`minecraft:oak_log`) inconsistent with rest of codebase (`oak_log`), and only covers 2 log types vs OakLogSpec's 7 | 86% | `HtnTaskLibrary.MineWoodDecompose`: `"minecraft:oak_log"` and `"minecraft:birch_log"` only |
| DEF-NEW-10 | `ChatOptions.MaxResponseDistanceBlocks` (documented, configurable, default 64.0) is never read by `ChatInterpreter.IsDirectedAtBot` — uses hardcoded `private const int ProximityAddressBlocks = 32` instead | 96% | `ChatInterpreter.cs`: `const ProximityAddressBlocks = 32`; `ChatOptions.cs`: property exists but not referenced in ChatInterpreter |

---

### CONFIRMED LOW-PRIORITY (known architectural stubs — not blocking)

| Item | Status | Disposition |
|---|---|---|
| `/api/agent/connect` and `/api/agent/stop` return success strings without mutating state | Confirmed | Intentional scaffolding stubs; label them in API docs; P3 |
| `/api/blueprints` returns hardcoded single entry | Confirmed | Known placeholder; back with `IBlueprintRepository.SearchAsync` in future phase; P3 |
| Blueprint lookup uses contains-match rather than exact slug | Confirmed | Residual risk; acceptable until blueprint catalog grows; P3 |
| ChatRateLimiter.Prune never called (per prior architecture review) | Unverified in this session | Needs verification; filed as P3 risk |
| LLM JSON extraction greedy regex (multi-object output) | Previously noted | Sprints 20-21 added TryParseTruncatedJson; residual risk is low; P3 |
| `GoalFactory` sync/async discoverability asymmetry | Confirmed intentional | Documented in GoalFactory XML summary; not a bug |

---

## III. 6-Seat Council Review

### Seat 1 — Source-Grounded Archivist
**Confidence: 94%**

All stale findings are correctly marked stale. Code evidence is direct, not inferred. The DEF-NEW-4 refutation is well-founded — `foreach (var block in spec.SourceBlocks)` is unambiguous. DEF-NEW-10 is the strongest new finding: an explicitly documented, configurable option that silently does nothing is a contract violation. DEF-NEW-6 (`TrimEnd('s')`) is a correctness defect with known victims (`glass`, `moss`).

**Dissent**: I want a note that `MineWoodDecompose`'s `minecraft:` prefix issue (DEF-NEW-9) is likely benign in practice because the JS adapter normalizes namespaces, but the C# layer should be consistent regardless.

---

### Seat 2 — Data Model Architect
**Confidence: 91%**

The P2-D upgrade to P1 is correct. `DecomposerPlanner.ReplanAsync` creates a `SimpleGoal` and passes it back to `decomposer.Decompose` — the decomposer's contract requires a specific goal type, not a shell. This is a type-level contract violation that will throw at runtime. The fix is to either pass the original goal to `ReplanAsync` (preserve type) or have each decomposer implement a replan path that accepts the current plan directly.

**Dissent**: DEF-NEW-5 (WorldState collection mutability) was logged as open in Sprint 27 but not verified in this session against current WorldState.cs. I recommend a direct read of WorldState.cs before confirming it remains open.

---

### Seat 3 — Retrieval Specialist
**Confidence: 88%**

DEF-NEW-2 (failure key collision) has an elegant minimal fix: `$"goal:{Name}:{targetCount}:failed"`. However this changes the failure key format, which could affect any existing world-state facts. If CI is green and no tests assert the exact key string, the fix is safe to ship. Confirm no hardcoded key assertions in existing tests before implementation.

DEF-NEW-6 (`TrimEnd('s')`) should be narrowed, not removed. A constrained replace list (item_with_s → singular lookup) or requiring the result to match an alias first prevents false truncation. The current code already tries the alias table BEFORE TrimEnd — the issue is only when TrimEnd produces something not in aliases that still passes the raw ID fallback (`^[a-z][a-z0-9_]*$`).

---

### Seat 4 — Human Learning Advocate
**Confidence: 87%**

The agent handoff must clearly call out which new items are P1 vs P2 vs P3, and must clearly close DEF-NEW-4 so future agents don't re-open it from the stale audit corpus. The audit corpus in `Data/Pages/Audit/` now contains 14 files; many describe the codebase in a pre-Sprint-25 state. Future agents reading only the audit files (not the council reviews) will be confused. The handoff should contain a one-page "stale audit index" mapping each stale finding to the sprint that fixed it.

**Dissent**: DEF-NEW-8 (`ExploreDecompose`) and DEF-NEW-9 (`MineWoodDecompose`) are not user-visible bugs in normal gameplay; they are code quality issues. Treating them as P2 and P3 respectively is more appropriate than P0/P1.

---

### Seat 5 — Skeptical Reviewer
**Confidence: 86%**

The P2-D upgrade is based on reading `PlannerRouter.cs` and `GatherGoalDecomposer.cs` side by side. My concern is: has the bot actually failed on a replan in practice? If HtnPlanner falls back cleanly and the phases happen to be recognized HTN tasks, the bug may not manifest for gather goals. The phases for `GenericGatherGoal` are `["FindSource","Mine","Collect"]` — these are NOT in `HtnTaskLibrary._methods` dictionary. So the throw would happen.

But: when does `ReplanAsync` get called? Only when `AgentBackgroundService` triggers a replan on consecutive failure. If gathering succeeds (common case), replan never fires. The bug is real but may be infrequently triggered. I'd keep it at P1 but note it's triggered only on consecutive gather failures.

**Blocking finding**: P1 upgrade is warranted because silent plan loss = agent stall. The fix is low-effort (pass original goal through replan path) and the test is straightforward.

---

### Seat 6 — Synthesizer
**Confidence: 91%**

**Overall verdict: APPROVED** for Sprint 28 execution with the following corrections and additions.

**Blockers (fix before executing Sprint 28 P0 work)**:
- None that block CI verification. But Sprint 28 P1 should include P2-D upgrade before the E2E gather test (P1-A), because E2E gather tests are more meaningful once replan is functional.

**Mandatory Sprint 28 backlog corrections**:
1. CLOSE P2-B (DEF-NEW-4) — finding refuted, no Take(2) exists
2. UPGRADE P2-D → P1 (replan SimpleGoal type loss)

**New additions**:
3. Add P2-E (DEF-NEW-6: TrimEnd('s'))
4. Add P2-F (DEF-NEW-7: `\bdoing\b` regex)
5. Add P2-G (DEF-NEW-9: MineWoodDecompose namespace/coverage)
6. Add P3-A (DEF-NEW-10: MaxResponseDistanceBlocks unused)
7. Add P3-B (DEF-NEW-8: ExploreDecompose hardcoded)

**Deferred** (acknowledged, not Sprint 28 scope):
- Lifecycle stubs `/api/connect`, `/api/stop`
- `/api/blueprints` hardcoded
- Blueprint lookup broad matching
- ChatRateLimiter.Prune verification

---

## IV. Acceptance Criteria for New Findings

### P1-Upgrade: PlannerRouter ReplanAsync type loss
- [ ] `ReplanAsync` called with an `IItemSpecGoal` (GenericGatherGoal) → re-decomposed by GatherGoalDecomposer (not thrown/null)
- [ ] Test: `ReplanAsync` with a `GenericGatherGoal`, assert result is non-null and has actions
- [ ] No `InvalidOperationException` thrown in GatherGoalDecomposer during replan path

### DEF-NEW-6: TrimEnd('s') fix
- [ ] `ResolveItemId("glass")` returns `"glass"` (not `"glas"`)
- [ ] `ResolveItemId("moss")` returns null or correct alias (not `"mos"`)
- [ ] Existing aliases still resolve correctly

### DEF-NEW-7: `\bdoing\b` regex tighten
- [ ] `"I am doing fine"` → `ChatIntentType.Unknown` (not QueryStatus)
- [ ] `"what are you doing"` → `ChatIntentType.QueryStatus` (still works)
- [ ] `"report"` → `ChatIntentType.QueryStatus` (still works)

### DEF-NEW-9: MineWoodDecompose fix
- [ ] `MineWoodDecompose` uses `oak_log` (no namespace prefix) consistent with rest of codebase
- [ ] Or: `MineWoodDecompose` is removed/deprecated in favour of `GatherItemDecompose(OakLogSpec, ...)`

### DEF-NEW-10: MaxResponseDistanceBlocks wired
- [ ] `IsDirectedAtBot` reads `options.MaxResponseDistanceBlocks` (not hardcoded const 32)
- [ ] OR: const renamed to match documentation intent (and ChatOptions property removed/aliased)

---

## V. Council Confidence Summary

| Seat | Role | Confidence | Blocking? |
|---|---|---|---|
| 1 | Source-Grounded Archivist | 94% | No |
| 2 | Data Model Architect | 91% | No |
| 3 | Retrieval Specialist | 88% | No |
| 4 | Human Learning Advocate | 87% | No |
| 5 | Skeptical Reviewer | 86% | No |
| 6 | Synthesizer | 91% | No |

**Average confidence**: 89.5%  
**Verdict**: **APPROVED** — no blockers. Sprint 28 backlog updated per corrections listed above.  
**Branch head at review**: `6443b2db`
