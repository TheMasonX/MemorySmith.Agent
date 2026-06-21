# MemorySmith.Agent — Sprint 26 Audit Council Review
**Date**: 2026-06-19  
**Branch**: `sprint-5-tool-safety` @ d5832d4  
**Version**: v0.25.0  
**Council format**: 6-seat (Source-Grounded Archivist, Data Model Architect, Retrieval Specialist, Human Learning Advocate, Skeptical Reviewer, Synthesizer)  
**Peer review**: Anonymous (2 reviewers, blinded)  
**Purpose**: Cross-validate two external audits against current codebase; define Sprint 26 scope

---

## Materials Reviewed

1. `Data/Pages/Audits/exec-summary-audit-20260619.md` — External executive summary
2. `Data/Pages/Audits/deep-code-audit-20260619.md` — External deep code audit
3. `Data/Pages/Tasks/agent-handoff-sprint26.md` — Sprint 25 handoff (Sprint 26 backlog)
4. Live branch code at d5832d4: ToolDispatcher.cs, WorldModel.cs, GatherGoalDecomposer.cs, HtnPlanner.cs, IItemSpecGoal.cs, IGoal.cs, test directory listing

---

## Seat 1: Source-Grounded Archivist
**Confidence: 88%**

Cross-referencing external audit findings against actual source at HEAD:

**Confirmed resolved (Sprint 25):**
- Finding 1 (integer validation): ToolDispatcher.CheckType uses `!value.TryGetInt32(out _)`. Line-verified in base64-decoded content. ✅
- Finding 2 (no try/catch): `CallAsync` has try/catch around `tool.ExecuteAsync`. OperationCanceledException re-throws; generic Exception → ToolResult(false). ✅
- Finding 3 (WorldModel aliasing): Constructor uses separate `new Dictionary<string,int>()` per state. `Observe()` uses `new Dictionary<string,int>(observation.Inventory)`. ✅

**Confirmed open:**
- Finding 4 (journal bounds): Still ConcurrentQueue + single-dequeue trim. Intentional. ✅ (as deliberate)
- Finding 5 (planner split): HtnPlanner has IItemSpecGoal, BuildGoal, CraftItemGoal type-switch alongside PlannerRouter. Source-confirmed. ✅ (as open)
- Finding 6 (chat filter): SYSTEM_MESSAGE_PATTERNS still regex-based. ✅ (as open)

**New finding discovered during intake:** `IItemSpecGoal.cs` (SHA 8ff8d7c4) declares only `ItemSpec Spec { get; }` — no `TargetCount`. GatherGoalDecomposer.cs (SHA 1dee6515) has `IItemSpecGoal isg => (isg.Spec, Array.Empty<string>())` which silently zeroes out target count for any IItemSpecGoal implementor that isn't GenericGatherGoal or GatherWoodGoal. HtnPlanner.cs has the same issue in its IItemSpecGoal branch (`isg is GenericGatherGoal ggg ? [...] : Array.Empty<string>()`). Fix requires adding `int TargetCount => 1;` as DIM to IItemSpecGoal. **This is Sprint 26 P0-B scope.**

---

## Seat 2: Data Model Architect
**Confidence: 85%**

Assessment of structural changes needed for Sprint 26:

**IItemSpecGoal TargetCount (P0-B):**  
The cleanest fix is a default interface method `int TargetCount => 1;`. This:
- Doesn't break existing IItemSpecGoal implementors (they get default=1)
- GenericGatherGoal and GatherWoodGoal already have public `TargetCount` properties that satisfy the interface
- Allows GatherGoalDecomposer and HtnPlanner to call `isg.TargetCount` directly, eliminating the `is GenericGatherGoal` cast
- Default of 1 is conservative (better than the old DecomposeGatherItem default-count-of-10)

*Concern*: C# DIM semantics — if an implementor has `public int TargetCount { get; }` on the class but doesn't explicitly implement the interface property, the class member takes priority when called through the concrete type but NOT through the interface reference. This is subtle. Given that GenericGatherGoal and GatherWoodGoal are the primary implementors and both have the property, this will work correctly. Future implementors must be documented to provide this property.

**ActionQueue.ClearAndEnqueue (P0-A test target):**  
Already lock-protected as of Sprint 23 B-3 fix. The integration test must exercise the full chain: HealthEvent delta computation → threshold comparison → ActionQueue.ClearAndEnqueue → emergency stop via IWorldAdapter. MockWorldAdapter at 1514 bytes suggests it's minimal; the test may need `SendEmergencyStop` call capture.

**WorldModel full immutability (P2):**  
`ObservationState.RecentObservations` is `IReadOnlyList<Fact>`. In `Observe()`, the list is projected via `.Select(...).ToList()`, creating a fresh list each time. This is already safe. Full copy-on-write at the projector boundary is genuinely P2.

---

## Seat 3: Retrieval Specialist
**Confidence: 82%**

Evaluating test coverage gaps and Sprint 26 test requirements:

**P0-A: TryInterruptOnDamage integration test (zero existing tests — 3rd deferral)**

Required coverage:
1. Consecutive HealthEvents with large delta (≥6 HP) → interrupt triggered → emergency stop + queue clear
2. Small delta (<6 HP) → no interrupt
3. Rapid second hit within cooldown window (3s) → suppressed
4. Second hit after cooldown expires → triggers again  
5. Goal with `DamageInterruptThresholdHp = 0` → never interrupts

The existing `AgentBackgroundServiceTests.cs` (17747 bytes) is the right fixture host. `MockWorldAdapter.cs` (1514 bytes) should have a `SendEmergencyStop` call tracker. Sprint26Tests.cs can use the same setup pattern.

**P0-B: GatherGoalDecomposer TargetCount**

Required coverage:
1. GatherGoalDecomposer with IItemSpecGoal goal carrying TargetCount=50 → MineBlock action uses count=50
2. GatherGoalDecomposer with GenericGatherGoal TargetCount=25 → still passes 25 (regression guard)
3. HtnPlanner IItemSpecGoal branch with non-GenericGatherGoal → uses TargetCount from interface

**P1-C: Journal semantics decision**  
Requires only a documentation commit to architecture.md — no new test needed.

**Test count projection**: Sprint 26 should add ~8 new tests (5 P0-A, 3 P0-B).

---

## Seat 4: Human Learning Advocate
**Confidence: 79%**

Assessing developer experience and operational clarity:

**Positive:**
- Audit intake process (annotating external findings against current source) is excellent practice; this council review formalizes what "audit-driven sprint planning" looks like.
- The handoff format with explicit P0/P1/P2 tiers, deferred table, and invariant checklist is clear and actionable.
- TreatWarningsAsErrors + Rule E-1 together form a strong quality gate. Both are enforced by existing tooling and the CI pipeline.

**Concerns:**
1. The sprint-5-tool-safety branch has been diverging from main since Sprint 6. At Sprint 26, this is 20+ sprints of un-merged work. Any reviewer looking at the PR for the first time will be confused by the gap between PR description ("Sprint 5/6 tool safety") and current scope (v0.25.0, Sprints 5–25). A PR description update is overdue.

2. The Sprint 26 P0-A test for TryInterruptOnDamage has been deferred 3 times (Sprints 24, 25, and now forced to P0 in Sprint 26). This suggests the test infrastructure for integrating AgentBackgroundService events is genuinely hard. The Sprint 26 implementation must address this directly rather than deferring again.

3. `AGENTS.md` Rule E-1 mentions verbatim-string patching but future agents may not read it. Consider adding a `[FRAGILE: DO NOT PATCH VIA INTERMEDIARY]` comment directly in the most dangerous verbatim-string files.

---

## Seat 5: Skeptical Reviewer
**Confidence: 76%**

Challenging assumptions:

**Challenge 1: Is P0-B actually P0?**  
The IItemSpecGoal catch-all arm in GatherGoalDecomposer has been wrong since Sprint 6 (when GatherGoalDecomposer was introduced). The code paths that matter today all use GenericGatherGoal or GatherWoodGoal — both correctly match before the IItemSpecGoal catch-all. No user-reported bug has been attributed to this. Classifying this as P0 (blocking) seems aggressive for a latent issue.

*Counter-argument*: The Sprint 24/25 handoffs explicitly name this as P0-B with escalation due to deferral. Keeping it as P0 is a process commitment, not just a technical judgment.

**Challenge 2: Will the TryInterruptOnDamage test be stable?**  
Damage interrupt logic involves timing (`_lastDamageInterruptAt`, `DamageInterruptCooldownSeconds`). Tests without a FakeTimeProvider (ITimeProvider abstraction — still P2) will either be flaky or use real `Task.Delay` calls, making them slow. Until ITimeProvider is added, the tests must use design patterns that avoid real time (e.g., set cooldown to 0 for "no cooldown" test, use a very long cooldown for "suppressed" test).

*Recommendation*: Write tests that don't require real timing by parameterizing the cooldown or using `DateTimeOffset.UtcNow` injection via constructor parameter (simple form of ITimeProvider).

**Challenge 3: Are the external audits unbiased?**  
Both external audits reference GitHub PR views and raw file paths, not local clones. The exec summary notes confidence 0.84 overall. The deep audit includes `cite turn983947view0` references suggesting LLM-generated content based on GitHub scraping. This is not a disqualification but means both audits should be treated as directional, not authoritative. The council's source-grounded verification (Seat 1) is the authoritative layer.

---

## Seat 6: Synthesizer
**Confidence: 83%**

### Validated Sprint 26 Scope

**BLOCKING (must ship before Sprint 27):**

**P0-A: TryInterruptOnDamage integration test**  
5 tests covering: large-delta trigger, small-delta no-trigger, cooldown suppression, post-cooldown re-trigger, zero-threshold goal. Use `DateTimeOffset` injection or parameterized cooldown to avoid timing fragility. Target: Sprint26Tests.cs.

**P0-B: GatherGoalDecomposer TargetCount + IItemSpecGoal DIM**  
- `Agent.Core/Interfaces/IItemSpecGoal.cs`: add `int TargetCount => 1;`  
- `Agent.Planning/Decomposition/GatherGoalDecomposer.cs`: IItemSpecGoal arm → `new[] { isg.TargetCount.ToString() }`  
- `Agent.Planning/HtnPlanner.cs`: simplify IItemSpecGoal branch to use `isg.TargetCount` directly  
- 3 tests covering: catch-all IItemSpecGoal with count, GenericGatherGoal regression guard, HtnPlanner branch count pass-through

**SHOULD-SHIP (P1):**

**P1-A**: E2E gather integration test — scope permitting, builds on P0-A harness  
**P1-B**: Journal semantics decision — architecture.md doc commit only, 30 minutes  
**P1-C**: Planner routing consolidation — CraftItemGoalDecomposer creation + HtnPlanner branch removal

**NICE-TO-HAVE (P2):**  
Startup constant log, ITimeProvider abstraction, move event throttling, IWorldObservationGateway note

**DEFERRED FROM PREVIOUS COUNCIL (carried forward):**  
DEF-1 (JS correlationId verification), DEF-2 (sendEvent audit), DEF-9 (Register collision docs)

### Blocking Findings

**BLK-1 (RESOLVED MID-SPRINT-25)**: CAS loop in TransitionCorrelatedAction — already fixed

**No new blocking findings** from this audit council. Sprint 26 may proceed with the P0 scope above.

### Acceptance Criteria

Sprint 26 is APPROVED when:
- [ ] 5 TryInterruptOnDamage tests pass (P0-A) — no real-time delays, deterministic
- [ ] IItemSpecGoal.TargetCount DIM added, all 3 test sites pass (P0-B)
- [ ] CI green on sprint-5-tool-safety after all changes
- [ ] Handoff doc written for Sprint 27
- [ ] Council post-sprint review written and committed

---

## Anonymous Peer Review (2 reviewers)

**Peer A** (blind): "The audit intake annotation format is solid. Agree with scope. One note: the GatherGoalDecomposer IItemSpecGoal arm is technically a latent bug but has been shipping for 20 sprints without user impact. The DIM fix is clean and low-risk. Ship it."

**Peer B** (blind): "I'm skeptical about testing TryInterruptOnDamage without ITimeProvider. Recommend parameterizing `DamageInterruptCooldownSeconds` at test setup to 0 for the 'always trigger' cases and 9999 for the 'suppress' case. This avoids the need for real-time injection and makes tests deterministic. The test harness in AgentBackgroundServiceTests already has this pattern for ReplanGovernor threshold."

*Synthesizer note*: Peer B's recommendation is adopted for the P0-A test design. MockWorldAdapter can expose the emergency-stop call count for verification.

---

## Summary Table

| Item | Status | Confidence | Sprint 26 |
|---|---|---|---|
| Tool integer validation | RESOLVED Sprint 25 | 96% | — |
| ToolDispatcher try/catch | RESOLVED Sprint 25 | 96% | — |
| WorldModel aliasing | RESOLVED Sprint 25 | 95% | — |
| Journal approximate bounds | Deliberate, documented | 88% | P1-B (doc) |
| Planner routing duplication | Open | 89% | P1-C |
| Chat filter brittle | Open, deferred | 78% | Backlog |
| CI stability (BLK-1) | RESOLVED Sprint 25 | 95% | — |
| IItemSpecGoal TargetCount | New finding, P0-B | 91% | P0-B |
| TryInterruptOnDamage test | 3rd deferral, P0-A | 93% | P0-A |

**Average council confidence**: 83%  
**Verdict**: APPROVED — Sprint 26 scope as defined above
