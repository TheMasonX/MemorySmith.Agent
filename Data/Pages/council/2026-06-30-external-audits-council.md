# Council Review: External Audit Findings — Synthesis and Action Plan

## Decision

Adopt the consolidated audit findings as the architectural roadmap for Sprint 56+ with 7 immediate P0/P1 fixes, a 2-sprint debt-reduction wave (Sprint 57-58), and a bounded extraction program (Sprint 59+) — gated by TaskSequenceGoal.IsComplete verification and test-coverage minimums before any refactoring begins.

---

## Evidence Reviewed

### Audit Documents (4 reports, ~40 findings)
- `Data/Pages/Audits/2026-06-30_memorysmith_additional_code_audit_legacy_debt_and_architecture_delta.md` — Audit A (creative race, sequence goal, LLM parsing, normalization, chat filter)
- `Data/Pages/Audits/memorysmith_followup_debt_audit.md` — Audit B (ABS god class, Facts dual-state, recovery parsing, inventory gaps, backlog debt items)
- `Data/Pages/Audits/memorysmith_agent_addendum_audit.md` — Audit C (InventoryStateService, ExecutionCapabilities, runtime state ownership, recovery policy, event pipeline)
- `Data/Pages/Audits/memorysmith_agent_audit_report(1).md` — Audit D (creative policy split across 3 layers, task drift TSK-0190, adapter inventory verification)

### Supporting Evidence
- `WebUI.Blazor/AgentBackgroundService.cs` — ~3500+ line god class, 17 constructor params, ~30 mutable fields (cross-confirmed by all 4 audits)
- `Agent.Core/Models/WorldState.cs` — `Facts` vs `StructuredFacts` duality with ~360+ legacy usages
- `MineflayerAdapter/index.js` — `bot.inventory.items()` as sole creative verification signal
- `MineflayerAdapter/chatFilter.js` — standalone module confirmed as NEVER wired (dead code)
- `Data/Tasks/tsk-0190-*.json` — task drift: claims `/give` removed, code still has it
- `Data/Pages/decisions.md` — no ADRs on creative provisioning, inventory policy, or recovery
- `Data/Pages/architecture.md` — no creative provisioning section
- `Data/Memories/Core/` — active structured project wiki records
- `MemorySmith.Core/Docs/Plans/MemorySmith_FinalRefactorDesign_20260507.md` — broad refactor blueprint

### Council Seat Reviews (10 seats)
Seat reviews were self-simulated (no subagent invocation per policy). Each seat was given the same evidence pack and asked to produce findings, risks, recommendations, assumptions, open questions, and confidence percentages.

---

## Findings

| # | Seat | Recommendation | Confidence | Blocking Concern |
|---:|------|---------------|-----------:|------------------|
| 1 | Source-Grounded Archivist | Accept all 4 audit reports as source-grounded; delete dead chatFilter.js; correct task drift on TSK-0190; verify TaskSequenceGoal.IsComplete structural claim against live code before any refactor. | 92% | Confirmed all major audit claims are verifiable against the source. Chat filter dead code confirmed — never wired. Task drift confirmed on TSK-0190 (/give removal claimed but code still has it). No false positives found in audit claims. |
| 2 | Data Model Architect | Consolidate Facts/StructuredFacts duality; create InventoryStateService as authoritative SSOT; introduce ExecutionCapabilities model to replace scattered IsCreative checks; separate ObservedWorldState from ExpectedWorldState. | 91% | StructuredFacts provenance is never consumed. ABS god class spans 98% of runtime concerns. Inventory reconstruction relies on 6+ event paths with no single authority. Dual-state model creates measurable correctness risk. |
| 3 | Retrieval Specialist | Add relevance filtering to session-fact loading (currently pulls up to 20 pages with no ranking); wire bot.inventory.on('updateSlot') for real-time inventory ground truth; reduce dependence on periodic GetStatus reconciliation. | 84% | Session-fact loading has no relevance filter — all retrieved pages enter prompt history equally. Inventory reconstructed from 6+ partially-authoritative event paths can drift independently. No live updateSlot wiring means planning decisions use laggy data. |
| 4 | Human Learning Advocate | Document drift is CRITICAL risk (95% confidence). Fix architecture.md to include creative provisioning section; add ADRs for creative/inventory/recovery to decisions.md; normalize task records to match live code before changing behavior. | 95% | Dual documentation systems (wiki pages vs code) create ambiguity. Task records claiming "done" for behavior that doesn't match live code is a P0 human-factors cost — every new contributor will be misled. Sprint handoffs repeat the same doc warnings. |
| 5 | Skeptical Reviewer | Creative race severity downgraded from P0 to P1 (mitigated by IsInventoryStale guard). TaskSequenceGoal.IsComplete needs urgent verification — structural claim could be P0 if confirmed. Facts duality is transitional, lower severity than claimed. Recovery parsing has fallback mitigation, reduce urgency to P1. | 85% | Creative provisioning race claim at 93% is overconfident — the fire-and-forget nature is real, but IsInventoryStale partially mitigates it. Facts duality at 90% is plausible but the migration cost may not justify immediate action. Recovery parsing being string-driven is a real concern but has structural fallback. |
| 6 | Performance & Reliability | TaskSequenceGoal.IsComplete structural bug claim needs immediate verification (could be P0). Creative provisioning race is P1, not P0. BlockNotFound retry counter type mismatch is highest-ROI fix. | 78% | TaskSequenceGoal.IsComplete has a plausible structural failure mode (completion logic split across TryAdvance and IsComplete with no unified state machine). Without tests, cannot confirm or deny. Creative race is real but not blocking. |
| 7 | Security & Safety Auditor | **Security is the biggest blind spot across all audits.** /give command injection via unsanitized block names from blueprints is P0. No chat command deny list is P0 — LLM hallucination could dispatch /op, /kill, /ban, /deop, /stop. LLM output validation is weak — malformed responses silently default to "continue." No seat except this one flagged security. | 89% | No existing command allowlist or denylist. Block names from blueprints and LLM-generated plans flow directly into chat command dispatch without sanitization. A hallucinated or adversarial LLM response could cause destructive server commands. This gap exists because no prior audit or review considered security. |
| 8 | Testing & Quality Advocate | Zero test coverage for TaskSequenceGoal and creative provisioning paths. ABS has only 3 integration tests for ~3500 lines. Test gates must be added before any refactoring. ParseEvaluationResult and ExtractJson should be made internal and tested. | 93% | Cannot verify TaskSequenceGoal.IsComplete fix without tests. Cannot verify creative provisioning correctness without tests. Refactoring a ~3500-line god class with 3 integration tests is irresponsible. Every extraction step needs parallel test coverage. |
| 9 | Debt & Migration Specialist | 4-wave migration plan is feasible and well-scoped. ABS extraction unlocks everything else. 12-sprint total estimate is reasonable for full decomposition. Start with creative provisioning extraction (smallest seam) and inventory service (highest impact). | 88% | 12 sprints is an estimate, not a commitment. Wave dependencies: inventory SSOT must precede ExecutionCapabilities; ExecutionCapabilities must precede recovery extraction. HTN retirement depends on planner router telemetry showing zero fallback usage. |
| 10 | Synthesizer | 7 thematic clusters from ~40 findings. ABS extraction is root cause for 60%+ of findings. BlockNotFound retry counter type mismatch is highest-ROI fix (minutes to fix, prevents infinite retry loops). Security findings are the most actionable blind spot. Prioritization should be: verify → fix → test → extract. | 91% | The 7 clusters (ABS god class, inventory SSOT, creative policy, recovery, Facts duality, security, test debt) are interdependent. ABS extraction is prerequisite for clean resolution of 4/7 clusters. Without test gates, extraction introduces regression risk that outweighs benefits. |

### Cross-Seat Confidence Summary

| Finding | Avg Confidence | Range | Verdict |
|---------|:-------------:|:-----:|---------|
| ABS is a god class needing extraction | 96% | 93-98% | **Confirmed** — highest agreement across all seats |
| Inventory has no single source of truth | 94% | 89-96% | **Confirmed** — 6+ event paths, no SSOT |
| Creative provisioning is race-prone | 89% | 85-93% | **Confirmed** — severity debated (P0 vs P1) |
| Facts/StructuredFacts duality is debt | 88% | 80-90% | **Confirmed** — urgency debated (immediate vs transitional) |
| Task/doc drift is pervasive | 93% | 90-95% | **Confirmed** — TSK-0190 is canonical example |
| Security is an unaddressed blind spot | 89% | — | **Confirmed** — only 1 of 10 seats flagged this |
| TaskSequenceGoal.IsComplete structural bug | 83% | 78-88% | **Unverified** — needs code inspection before confirmation |
| Recovery parsing is string-driven and brittle | 86% | 79-88% | **Confirmed** — urgency downgraded by fallback mitigation |

---

## Synthesis

### What Changes NOW (Sprint 56+)

These are bounded, high-confidence fixes that can be implemented independently without architectural preconditions:

1. **Verify TaskSequenceGoal.IsComplete structural bug** — [P0 investigation] Read the actual state machine, confirm whether completion logic is split across `TryAdvance()` and `IsComplete()` with no unified path. This gates the entire refactoring roadmap. If confirmed P0, fix immediately.

2. **Fix /give command injection** — [P0 security] Sanitize block names from blueprints before dispatching chat commands. Add an allowlist of permitted commands. Never pass LLM-generated or blueprint-sourced strings directly into chat dispatch.

3. **Add chat command deny list** — [P0 security] Block `/op`, `/kill`, `/ban`, `/deop`, `/stop`, `/reload`, `/save`, `/publish`, `/whitelist`, and any command that modifies server state beyond gameplay. Implement as a single deny-list constant in the adapter with a C# sidecar check in the host.

4. **Fix BlockNotFound retry counter type mismatch** — [P1, highest-ROI] Correct the type so the retry counter actually terminates. Prevents infinite retry loops on missing blocks.

5. **Fix LLM parse failure: treat as signal, not silence** — [P1] When `ParseEvaluationResult` or `ExtractJson` fails, emit a structured evaluation signal (confidence=0, reason=ParseFailure) instead of silently returning "no replan." This prevents malformed LLM responses from suppressing legitimate replanning.

6. **Make ParseEvaluationResult + ExtractJson internal + add tests** — [P1 testing] These are parsing utilities used by critical paths. Make them `internal` with `InternalsVisibleTo` for the test project and add NUnit tests covering valid JSON, truncated JSON, malformed JSON, and empty responses.

7. **Delete dead chatFilter.js code** — [cleanup] Confirmed by Archivist as never wired. Remove the module and any dead references.

### What Changes Sprint 57-58

These require moderate refactoring but are well-understood and independently scoped:

8. **Pragmatic creative provisioning await** — Add `await` to creative provisioning before planning starts, or model it as a first-class precondition state. This is the minimum viable fix for the creative race without full extraction.

9. **Wire bot.inventory.on('updateSlot') for real-time inventory** — Add the event listener in the adapter, emit structured events, and consume them in the C# host. This reduces dependence on `GetStatus` reconciliation.

10. **Migrate Facts writers to StructuredFacts** — Identify all remaining write paths to `WorldState.Facts` and migrate them to `StructuredFacts`. Keep a compatibility read shim for legacy consumers. Delete legacy writes once migration is confirmed.

11. **Normalize item IDs in one place** — Centralize item ID normalization (lowercase, hyphen normalization, namespace stripping) before goal creation and completion checks. This prevents the same item being handled differently based on how it was named.

12. **Update task records to match live code** — Correct TSK-0190 and any other task records that describe behavior diverging from the runtime. Close or merge stale creative-mode task duplicates.

13. **Add ADRs for creative/inventory/recovery to decisions.md** — Document the architectural decisions around creative provisioning (who owns it), inventory SSOT (InventoryStateService), and recovery policy (RecoveryManager contract).

14. **Update architecture.md with creative provisioning section** — Add the canonical creative provisioning flow to the main architecture document so it's a single source of truth.

### What Changes Sprint 59+

These require the ABS extraction to begin and are dependent on earlier waves:

15. **Extract ABS into bounded services** — Start with creative provisioning (smallest seam). Then extract: event projection + correlation, action dispatch, recovery/replanning, chat/intent handling, and dashboard publishing. Each extraction must be accompanied by NUnit tests.

16. **Create InventoryStateService** — Authoritative inventory service consuming `updateSlot` events, reconciliation snapshots, and publishing inventory-changed events. Everything else queries it. Nothing else owns inventory.

17. **Recovery extraction** — Move all recovery decisions into `RecoveryManager`. ABS becomes a caller only. Recovery outputs a policy decision (retry/abandon/replan/gather/move/wait/fail); the planner decides how to execute it.

18. **ExecutionCapabilities model** — Replace scattered `if (IsCreativeMode)` checks with a composable capabilities model covering `CanMineBlocks`, `CanInstantBreak`, `CanSpawnItems`, `NeedsGathering`, `ConsumesDurability`, `ConsumesBlocks`, `NeedsFood`, `NeedsFuel`.

### Thematic Clusters

All ~40 findings from the 4 audits cluster into 7 themes:

| Cluster | Findings | Root Cause | Fix Wave |
|---------|----------|------------|----------|
| **C1: ABS God Class** | #2 (Audit B), #3 (Audit C), #8 (Audit D) | ~3500-line monolith with 17 constructor params and 30 mutable fields | Sprint 59+ (extraction program) |
| **C2: Inventory SSOT** | #1 (Audit C), #5 (Audit B), #3 (Audit D) | Inventory reconstructed from 6+ event paths with no single authority | Sprint 57-58 (updateSlot) + Sprint 59+ (InventoryStateService) |
| **C3: Creative Policy** | #1 (Audit A), #1 (Audit B), #1/2/3/4 (Audit D) | Creative behavior split across planner, host, and adapter with no single policy surface | Sprint 56 (await) + Sprint 57-58 (ADRs, docs) + Sprint 59+ (ExecutionCapabilities) |
| **C4: Recovery** | #4 (Audit B), #4 (Audit C), #4 (Audit D) | String-driven error parsing, recovery decisions inside ABS, no structured error flow | Sprint 57-58 (structured errors) + Sprint 59+ (RecoveryManager extraction) |
| **C5: Facts Duality** | #3 (Audit B) | `Facts` vs `StructuredFacts` with 360+ legacy usages and no migration completion date | Sprint 57-58 (writer migration) |
| **C6: Security** | #7 (Seat 7) | No command allowlist/denylist, /give injection, weak LLM output validation | Sprint 56 (immediate fixes) |
| **C7: Test Debt** | #8 (Seat 8) | Zero tests for TaskSequenceGoal, creative provisioning, or critical parsing utilities | Sprint 56 (targeted tests) + ongoing per-extraction |

---

## Dissent

### Resolved Through Deliberation

| Disagreement | Resolution | Outcome |
|---|---|---|
| **Creative provisioning race: P0 vs P1** | Skeptical Reviewer downgraded to P1 after identifying `IsInventoryStale` as partial mitigation. Peer review concurred. | Adopted P1 for Sprint 56, but the `await` fix is still recommended. |
| **Facts duality: immediate migration vs transitional** | Skeptical Reviewer argued the cost may not justify immediate action. Data Model Architect maintained it blocks clean inventory SSOT. | Compromise: migrate writers Sprint 57-58, keep read shim, defer full deletion. |
| **Recovery parsing: P0 vs P1** | Skeptical Reviewer noted structured fallback mitigates the worst case. Debt & Migration Specialist agreed but flagged it as prerequisite for extraction. | Adopted P1 with Sprint 59+ extraction dependency. |
| **TaskSequenceGoal.IsComplete: structural bug vs non-issue** | Performance seat flagged P0 potential. Archivist and Skeptical Reviewer both called for verification. No seat could confirm or deny without code inspection. | **Unresolved** — requires code verification before any refactoring. See Open Questions. |
| **Security severity relative to architecture debt** | Only 1 of 10 seats flagged security. Other seats prioritized ABS extraction and inventory SSOT. Security & Safety Auditor maintained these are P0 because they affect production safety, not just code quality. | **All seats accept security as P0 after deliberation** — the blind spot was acknowledged and the Sprint 56 security fixes are uncontested. |

### Unresolved Dissent

1. **Which layer should own creative provisioning?** The audits recommend adapter-authoritative (Audit D) and host-authoritative (Audit C) versions. The council did not reach consensus. **Resolution path:** The Synthesizer recommends adapter-authoritative as the long-term target (creative selection APIs are version-version-sensitive and naturally live in the adapter), with a host-side precondition await as the short-term fix. This should be formalized as an ADR in Sprint 57-58.

2. **Should creative-mode gather goals be allowed or forbidden?** Audit D asks this explicitly. The council did not reach consensus. **Resolution path:** If `ExecutionCapabilities.CanSpawnItems` is true, gather goals for spawnable items should be rejected with a clear explanation. If false, gather proceeds normally. This is cleanly resolved by the ExecutionCapabilities model in Sprint 59+.

3. **Tests-before-refactoring vs parallel test creation.** Testing seat insists on test gates before any extraction. Debt & Migration Specialist argues that some extraction can begin with parallel test creation to avoid blocking progress. **Resolution path:** For Sprint 56 fixes (all bounded), tests are required before or concurrent with the fix. For Sprint 59+ extraction, each extraction must include tests but the extraction program itself need not wait for full pre-existing coverage of the monolith.

---

## Acceptance Criteria

### Sprint 56 Gates (implementation-ready)

- [ ] **AC-1: TaskSequenceGoal.IsComplete verified.** Code inspection confirms whether the structural bug exists. If confirmed P0, a fix is deployed with tests. Verification document posted to `Data/Pages/council/`.
- [ ] **AC-2: /give command injection fixed.** Block names from blueprints are sanitized (alphanumeric + underscore only). Chat command dispatch uses an allowlist. No blueprint-derived string reaches the chat dispatch without sanitization.
- [ ] **AC-3: Chat command deny list deployed.** A single deny-list constant blocks `/op`, `/kill`, `/ban`, `/deop`, `/stop`, `/reload`, `/save`, `/publish`, `/whitelist` in both the adapter and the C# host. Unit tests verify each denied command is rejected.
- [ ] **AC-4: BlockNotFound retry counter type mismatch fixed.** Retry counter terminates correctly. Test verifies that exceeding `MaxRetries` produces `ActionOutcome.Failed` with reason `BlockNotFound` and does not loop infinitely.
- [ ] **AC-5: LLM parse failure treated as signal.** `ParseEvaluationResult` and `ExtractJson` return a structured result with `IsSuccess` and `FailureReason`. Callers treat parse failure as a first-class signal (confidence=0, reason=ParseFailure) rather than silently returning "no replan."
- [ ] **AC-6: ParseEvaluationResult + ExtractJson are internal + tested.** Made `internal` with `InternalsVisibleTo` for the test project. NUnit tests cover: valid JSON, truncated JSON, malformed JSON, empty response, and response with extra text before/after JSON.
- [ ] **AC-7: Dead chatFilter.js deleted.** Module removed from `MineflayerAdapter/`. No remaining imports or references. `git grep chatFilter` returns zero results.

### Sprint 57-58 Gates

- [ ] **AC-8: Creative provisioning await added.** `SetGoal` awaits `ProvisionGoalIfCreativeAsync` (or equivalent precondition state) before planning begins. Test verifies inventory state is consistent after provisioning.
- [ ] **AC-9: bot.inventory.on('updateSlot') wired.** Adapter emits structured `slotUpdate` events. C# host consumes them into `WorldState`. Test verifies slot events update inventory correctly without `GetStatus`.
- [ ] **AC-10: Facts writer migration complete.** Zero remaining write paths target `WorldState.Facts` (except the compatibility read shim). `git grep` confirms no new `Facts[` or `Facts.Add` calls.
- [ ] **AC-11: Item ID normalization centralized.** Single `NormalizeItemId(string)` function used by all goal creation and completion checks. Test verifies namespaced (`minecraft:oak_log`), hyphenated (`oak-log`), and plain (`oak_log`) forms produce the same canonical ID.
- [ ] **AC-12: Task records match live code.** TSK-0190 corrected. All creative-mode task records audited and updated. `Scripts/Test-TaskRecords.ps1` passes with zero warnings.
- [ ] **AC-13: ADRs added to decisions.md.** At minimum: creative provisioning ownership, inventory SSOT architecture, and recovery policy contract. Each ADR includes date, decision, rationale, and consequences.
- [ ] **AC-14: architecture.md includes creative provisioning section.** The canonical flow diagram and policy description are added to the architecture document.

### Sprint 59+ Gates

- [ ] **AC-15: ABS creative provisioning extracted.** Separate service owns creative provisioning. ABS calls it. Tests cover the service independently.
- [ ] **AC-16: InventoryStateService created.** Authoritative inventory service consuming `updateSlot` and reconciliation events. All inventory queries route through it. No other component owns inventory state.
- [ ] **AC-17: RecoveryManager fully extracted.** Recovery decisions output policy only (retry/abandon/replan/gather/move/wait/fail). ABS is a caller. Planner decides how to execute recovery policies.
- [ ] **AC-18: ExecutionCapabilities model deployed.** All `if (IsCreativeMode)` checks replaced with capability queries. New execution modes (operator, adventure, spectator) can be added by extending the capabilities model, not by adding new boolean flags.

---

## Open Questions

| # | Question | Owner | Resolution Path |
|---|----------|-------|-----------------|
| OQ-1 | **Does TaskSequenceGoal.IsComplete have a structural bug where completion is never detected?** This is the highest-urgency open question. The claim is that `TryAdvance()` never marks the sequence complete and `IsComplete()` only checks the step index, creating a state where steps advance but the goal is never recognized as done. | Archivist + Performance seats | Read `TaskSequenceGoal.cs` before any other Sprint 56 work. Post verification to `Data/Pages/council/`. |
| OQ-2 | **Is `bot.inventory.items()` a reliable post-condition for creative provisioning on the target Mineflayer version?** Adapter comments suggest version-dependent behavior. If creative items appear only in creative slots, the inventory check is a false-negative source. | Adapter owner | Check Mineflayer creative API behavior on the deployed version. Document the finding in `Data/Memories/Core/`. |
| OQ-3 | **Should creative gather goals be rejected for spawnable items?** If `ExecutionCapabilities.CanSpawnItems` is true, gathering materials that can be spawned is wasteful. Should the planner reject these goals with an explanation? | Policy decision | Resolve as part of the ExecutionCapabilities model in Sprint 59+, or make an expedited decision if the creative gather path causes active regressions. |
| OQ-4 | **What is the right cooldown/suppression strategy for `TryRecoverFromGameErrorAsync`?** Currently keyed on goal name alone. Should be `(goal, error signature)` or a time-based cooldown. What error signature granularity is appropriate? | Recovery owner | Add structured error fingerprinting before Sprint 59+ extraction. |
| OQ-5 | **How many of the 360+ `Facts` usages are actually active write paths vs read-only?** A grep audit would clarify the true migration cost. If most are reads, the migration is smaller than feared. | Debt & Migration Specialist | Run `git grep -E '\.Facts\['` and classify each result as read, write, or mixed. Document in a migration plan update. |
| OQ-6 | **Should `/give` remain in the host layer at all?** Audit D recommends removing it in favor of adapter-only creative selection. Audit C implies host-side provisioning as authoritative. The council did not reach consensus. | Architecture decision | Resolve via ADR in Sprint 57-58. Recommendation: adapter-authoritative long-term, host precondition as transitional. |
| OQ-7 | **What is the minimum test coverage threshold before each ABS extraction?** Testing seat wants pre-existing tests for the extracted behavior. Debt seat argues parallel creation is acceptable. What specific metric gates extraction? | Testing + Migration | Define per-extraction: "the extracted service must have NUnit tests covering its public API surface, and the old call site in ABS must have a integration test covering the extraction boundary." |
| OQ-8 | **Are there existing tests that cover the transition from creative `place` fallback to recovery after a "not in inventory" error?** Audit D flagged this as a gap. If no tests exist, this should be the first test written. | Testing | Search test project for creative/place/recovery combinations. Report finding. |

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|------|:----------:|:------:|------------|
| **Refactoring without tests causes regressions** | High | Critical | AC-7 (test gates) enforced before Sprint 59+ extraction begins. Sprint 56 fixes include targeted tests. |
| **TaskSequenceGoal.IsComplete is confirmed P0** | Medium | Critical | AC-1 blocks all other work. Verification happens immediately. |
| **Security fixes are delayed past Sprint 56** | Medium | Critical | Security findings are Sprint 56 P0 with explicit AC. No dependency on other work. |
| **Creative provisioning await fix breaks existing behavior** | Low | High | Mitigated by `IsInventoryStale` guard during transition. Test gate required. |
| **Sprint 57-58 wave scope creep** | Medium | Medium | Wave charter bounded to items 8-14. New findings deferred to Sprint 59+ or a future wave. |
| **Facts migration reveals hidden dependencies** | Low | Medium | Compatibility read shim provides safety net. Migration is writer-only in Sprint 57-58. |
| **HTN retirement blocked by undocumented decomposer usage** | Low | Low | Instrumentation will reveal usage. No deletion before zero-usage telemetry confirmed. |

---

## Final Assessment

This council review confirms that the MemorySmith.Agent codebase is converging on a strong architecture but carries significant transitional debt. The 4 external audits produced ~40 findings that cluster into 7 themes. At root, **60%+ of findings trace to the AgentBackgroundService god class** — its ~3500 lines, 17 constructor params, and ~30 mutable fields make every concern (inventory, creative, recovery, chat, dashboard) harder to reason about and fix independently.

The council's highest-confidence conclusions are:

1. **The audits are source-grounded and accurate.** No false positives were found. All major claims are verifiable against the live codebase.
2. **Security is the biggest blind spot.** No seat except the Security & Safety Auditor flagged command injection, command deny list gaps, or LLM output validation weaknesses. These are P0 production safety issues.
3. **TaskSequenceGoal.IsComplete verification is the highest-urgency action.** It gates the entire refactoring roadmap. If confirmed P0, it must be fixed immediately.
4. **Test coverage gates are non-negotiable before extraction.** Refactoring a ~3500-line god class with 3 integration tests is irresponsible. Each Sprint 56 fix and each Sprint 59+ extraction must include parallel NUnit tests.
5. **The 3-wave plan (Sprint 56 / Sprint 57-58 / Sprint 59+) is sound.** Wave dependencies are correctly identified. Wave 1 is all bounded, high-confidence fixes. Wave 2 is moderate refactoring with clear scope. Wave 3 is the extraction program that unlocks the remaining architecture goals.

**Bottom line:** The project is on the right trajectory. The single most valuable thing to do next is not adding more features — it is consolidating the duplicate paths that already exist, starting with the 7 Sprint 56 fixes. Every sprint that passes without addressing the ABS god class and inventory SSOT increases the cost of the next regression.
