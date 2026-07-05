# Council Review: Consolidated Audit Synthesis — MSA Sprint 57-58 + MS 2026-07-02

**Date:** 2026-07-05
**Council seats:** Source-Grounded Archivist · Data Model Architect · Retrieval Specialist · Human Learning Advocate · Skeptical Reviewer · Synthesizer
**Subagent permission:** Explicitly granted by user for all 6 seats

## Decision

Sequence the 21 consolidated P0/P1 audit findings (12 MSA + 9 MS) into 4 implementation waves with clear ordering dependencies, validation gates, and deferred buckets. Create new MCP tasks for untracked findings and extend existing tasks where appropriate.

## Evidence Reviewed

- `Data/Pages/Audits/msa_sprint58_deep_audit_dev_round3_20260701_1500_CT.md` (F1-F9)
- `Data/Pages/Audits/msa_sprint58_deep_audit_dev_round3_20260701_1615_CT_DELTA.md` (D1-D2)
- `Data/Pages/Audits/msa_sprint58_deep_audit_delta2_dev_round3_20260701_1830_CT.md` (F10-F14)
- `Data/Pages/Audits/msa_sprint58_deep_audit_delta3_dev_round3_20260701_2100_CT.md` (F15-F17)
- `Data/Pages/Audits/msa_sprint58_deep_audit_delta4_dev_round3_20260701_2330_CT.md` (F18-F20)
- `Data/Pages/Audits/internal-audit-57-20260701.md` (34 findings, 7 P0, 9 P1)
- `Data/Pages/Audits/llm-adaptapbility-sprint-57-audit-7-1-26.md`
- `Data/Pages/Audits/msa_audit_report_gpt1.md`
- `Data/Pages/Audits/msa_code_audit_report-6-24-26.md`
- `Data/Pages/Audits/council-review-msa-audit-6-27-26.md`
- `Data/Pages/audits/MemorySmith-Audit-20260702.md` (MS: 10 headline findings)
- `Data/Pages/audits/MemorySmith-Audit-Delta-2-20260702.md` through `-Delta-10-20260702.md`
- `Data/Pages/audits/memorysmith-audit-delta-20260703.md` (Delta Round 2)
- `Data/Pages/audits/memorysmith-audit-delta-round3-20260703.md` (Delta Round 3)
- `Data/Pages/audits/memorysmith-deep-audit-20260703.md`
- `Data/Pages/audits/memorysmith_audit_deltas_2026-07-05_v11.md`
- `Data/Pages/audits/audit_review_handoff_7-5-26.md` (handoff document)
- `Data/Pages/audits/external-deep-research-audit-20260617.md`
- `Data/Pages/council/external-audit-council-review-20260617.md`
- Active repo-memory records under `/memories/repo/`

## Findings by Seat

### Source-Grounded Archivist
**Confidence:** 90%

The evidence chain lengths are short (most findings span 1-2 files, directly observable). Key corrections from this seat's analysis:
- **P0-1 (PlaceBlockGoal data race)** is **already fixed** by Sprint 58 Wave D (TSK-0330) — the evidence summary was stale. This finding should be removed from active status.
- **F12 (AliasRegistry)** nuance: static dictionaries ARE live (AliasRegistry.ItemAliases.TryGetValue from ChatInterpreter). Only `TryResolve()` (fuzzy matching) and `Search()` are dead.
- **Strongest evidence**: F1 (97%), F18 (95%), F3 (95%), MS F13 (98%), MS F21 (95%)
- **Cross-cutting patterns identified**: (1) Test/Production Divergence (F10, F12), (2) Three independent retry/backoff bugs (F11, F16, F18), (3) Stale audit data (P0-1), (4) MS secret persistence governance failure

### Data Model Architect
**Confidence:** 86%

Key data model findings:
- **Producer → ∅ pattern**: F1 (predictions), F3/F4 (evaluator output), F2 (preconditions) — system produces data that is never consumed
- **Name domain fragmentation**: F1 (wire vs ITool names) — two naming conventions with no normalization layer
- **Dead type baggage**: F2, F12, F14, F17/F20 — interfaces built but never wired
- **State machine defects**: F11 (off-by-one backoff), F16/F18 (double disconnect), P0-1 (data race)
- **Recommendation**: Add `ActionData.WireName` to eliminate the name-domain mismatch permanently

### Retrieval Specialist
**Confidence:** 92%

Cumulative assessment: The observe→evaluate→replan loop has three independent breaks (F1, F3, F4) and one structural retrieval infrastructure gap (F12).
- **F1 is the single highest-impact finding** — every outcome prediction is wrong
- **F12 alias registry** is the most cost-effective retrieval improvement available (deterministic, zero LLM cost, 800+ Minecraft items vs 13-entry prompt hint)
- **Combined effect**: Fixing only 1-2 breaks does not restore loop convergence — need F1 + F3 + F4 minimum batch
- **Recommendation**: Wire F12 before any other LLM-prompt work; the alias registry is already built and tested

### Human Learning Advocate
**Confidence:** 88%

- **F17/F20 (five dead subsystems)** is the single biggest morale and trust destroyer — communicates that test suite can't be trusted, half-finished work is acceptable, institutional knowledge is required
- **Documentation drift pattern**: Feature designed → implementation never finished → docs never updated → next developer wastes 45min-4hrs debugging
- **F1 imposes highest daily cognitive tax** — violates Principle of Least Surprise
- **Process changes recommended**: Build-time DI assertion, wire-name contract test, dead-code gate in CI, sprint boundary cleanup rule

### Skeptical Reviewer
**Confidence:** 82%

Adjusted several severity/confidence assessments downward:
- **F1**: Adjusted from Critical/97% to High/85% — missing call-chain evidence; "place works" weakens the claim
- **F10**: Adjusted from 92% to 70% — MS DI usually picks correct constructor; needs registration code evidence
- **F16**: Adjusted from 90% to 75% — may be different code path than TSK-0100
- **F2 (dead precondition)**: Downgraded from High to Moderate — dead code by definition has zero runtime harm
- **F3/F4**: Adjusted to 60-70% — depends on whether evaluator is live or stubbed
- **Risk identified**: Fixing F1 naively (adding `.ToLowerInvariant()`) could create ambiguity if two PascalCase names map to same lowercase wire name
- **Recommendation**: Fixing F10 requires `[ActivatorUtilitiesConstructor]` attribute, not constructor removal (would break tests)

### Synthesizer

Consolidated into a sequenced 4-wave plan:

| Wave | Focus | Findings | Exit Criteria |
|:----|:------|:---------|:--------------|
| **Immediate** | Security triage + self-contained fixes | F20, F24, F23, F27 (MS); F6, F9, F16 (MSA) | 7 tasks, ~2 hours total |
| **Wave A** | Prediction pipeline | F1, F13, F31 | Predict works for all tools; evaluator checks world diff |
| **Wave B** | Inventory & evaluation | F14, F18, F19, F3 | Sync fires during active goals; circuit breaker works; step context in prompt |
| **Wave C** | Safety & config hardening | F17, F34, F29, F4 | Deny list additive-only; diagnosis field stubbed |
| **Wave D** | Adapter resilience | F7, F8 | Both reconnect mechanisms retry correctly |
| **Sprint 60+** | Deferred | F12, F10, F11, F30, F32, F33, F35 + MS findings | Per-subsystem disposition |

**Unresolved disagreements:**
1. **F19 (Creative CancellationToken)**: Safety seat says P0, Runtime seat says P1 → Synthesizer rules **P0** (anti-pattern regardless of deployment context)
2. **F4 (Evaluator diagnosis)**: Architecture seat says P0, QA seat says P1 → Synthesizer rules **P1** for Sprint 59 (stub), promote if needed in Sprint 60
3. **F31 (WorldStateDiff)**: Data Model seat says P0, Skeptical says P2 → Synthesizer rules **P1** (~10-line addition, high observability value)

## Acceptance Criteria

### Immediate Actions (MS fixes — cross-repo request)

| # | Gate | Verification |
|:--|:-----|:-------------|
| G5.1 | API key rotated and old key invalidated | Confirm new key in `appsettings.json`; old key in `mcp.json` removed |
| G5.2 | GitHub OAuth ClientSecret rotated | Confirm new secret; old secret removed from git history |
| G5.3 | `artifacts/` added to `.gitignore` | Verify `.gitignore`; force-added files removed from history |
| G5.4 | Pre-commit hook rejects secrets | Test `git add` with placeholder replaced → should fail |
| G5.5 | Login rate limiter per-client | Verify partition key = remote IP in rate limiter config |
| G5.6 | TreeSitter key fixed | Verify `"c_sharp"` → `"CSharp"` in TreeSitterChunkingService |

### Wave A — Prediction Pipeline (MSA)
| # | Gate | Verification |
|:--|:-----|:-------------|
| G1.1 | `WorldModel.Predict` correctly predicts for all registered tools | Unit test per tool asserting correct prediction effect |
| G1.2 | `LlmEvaluatorImpl` returns "replan" when `WorldStateDiff.HasMismatch == true` | Despite all outcomes succeeding |
| G1.3 | `WorldStateDiff.HasUnexpectedChanges` detects items gained without matching tool effect | Test with crafted diff |
| G1.4 | No test regression | `dotnet test` passes, 815+ tests, 0 failures |

### Wave B — Inventory & Evaluation (MSA)
| # | Gate | Verification |
|:--|:-----|:-------------|
| G2.1 | Inventory sync fires at 60s during active goal | Log verification |
| G2.2 | First inventory sync fires at 30s (stacked-delay bug fixed) | Log verification |
| G2.3 | LLM evaluator circuit breaker enters 60s cooldown after 3 failures | Timeout test |
| G2.4 | `ProvisionGoalIfCreativeAsync` aborts within 1s of goal switch | Log verification |
| G2.5 | Step-specific context in evaluator LLM prompt | Prompt inspection |

### Wave C — Safety & Config (MSA)
| # | Gate | Verification |
|:--|:-----|:-------------|
| G3.1 | Config `["kill"]` does not remove `/ban`, `/stop`, `/execute` from denied set | Additive-only merge test |
| G3.2 | `DeniedCommands` comparison works regardless of leading slash | Test with and without slash |
| G3.3 | `PlaceBlockGoal` with `count=3` produces 3 different targets or 1 action with `count=N` | Decomposer test |
| G3.4 | `EvaluationResult.Diagnosis` populated after evaluator runs | Integration test |
| G3.5 | Task records pass validation | `Scripts/Test-TaskRecords.ps1` passes |

### Wave D — Adapter Resilience (MSA)
| # | Gate | Verification |
|:--|:-----|:-------------|
| G4.1 | WebSocketBridge reconnect retries at least 6 times | Mock ConnectAsync to throw 5 times, assert 6th succeeds |
| G4.2 | Node adapter reconnect backoff reaches 30s cap | Log verification |
| G4.3 | Reconnect does not give up permanently after first failure | E2E or integration test |

## Open Questions

1. **F10 verification**: Does .NET DI actually select the empty `_methods` constructor? Needs a `ServiceProvider`-based test to confirm.
2. **F1 option**: Should the fix add `ActionData.WireName` (Option C) or normalize at the `Predict` call site (Option A)?
3. **F5 status**: Is PlaceBlockGoal `_dispatched` race already fixed by TSK-0330? (Archivist says yes, but needs confirmation against current HEAD.)
4. **MS secret rotation**: Were the ClientSecret and API key already rotated out-of-band since the June 17 audit? If yes, the remaining work is only git history cleanup.
5. **F4 design**: Should `EvaluationResult.Diagnosis` be free-text (string) or structured? Synthesizer recommends free-text for Sprint 59, structured in Sprint 60.

## Task Map

### New Tasks to Create (MSA, untracked)
| ID | Finding | Title | Priority |
|:---|:--------|:------|:---------|
| TSK-0336 | F1 part | Fix WorldModel.Predict tool-name domain mismatch — normalize wire names | Critical |
| TSK-0337 | F6 | Fix HtnTaskLibrary DI constructor defect — consolidate to single constructor with full _methods | High |
| TSK-0338 | F7 | Fix WebSocketBridge reconnect retry loop — distinguish failed-reconnect from clean shutdown | High |
| TSK-0339 | F8 | Fix Node adapter reconnect exponential backoff — move counter reset to spawn handler | High |
| TSK-0340 | F4 | Stub evaluator diagnosis channel — add EvaluationResult.Diagnosis field | High |
| TSK-0341 | F19 | Fix creative provisioning CancellationToken.None — per-goal linked CancellationToken | Critical |
| TSK-0342 | F29 | Fix PlaceBlockGoalDecomposer same-coordinate placements — offset or count-driven | High |
| TSK-0343 | F31 | Add WorldStateDiff.HasUnexpectedChanges detection | High |
| TSK-0344 | F33 | Wire EntityObservedEvent into StructuredFacts instead of legacy Facts dict | High |
| TSK-0345 | F9 | Fix ReplanGovernor graduated backoff off-by-one and auto-recovery reset bug | High |

### Extended Existing Tasks
| Existing Task | Extend With |
|:--------------|:------------|
| TSK-0309 (Sprint 58) | F1: Add wire-protocol name normalization to Predict |
| TSK-0320 (LlmEvaluator) | F13: Check WorldStateDiff before fast-path; F3: Add TaskSequenceGoal branch |
| TSK-0321 (Inventory sync) | F14: Remove `_currentGoal is not null` guard; add 60s active-goal interval |
| TSK-0324 (Safety config) | F17: Change DeniedCommands to additive-only merge |
| TSK-0318 (DeniedCommands) | F34: Complete normalization — strip leading slash from both sides |
| TSK-0325 (Circuit breaker) | F18: Replace counter-reset-to-0 with 60s cooldown |
| TSK-0328 (HtnPlanner logging) | F36: Downgrade raw-action Warning to Debug |

### MS Findings (cross-repo request)
| Finding | Request Document |
|:--------|:-----------------|
| F20: Leaked secrets | MS-Request: rotate API key + ClientSecret, add to .gitignore, filter-repo |
| F21: OAuth bootstrap ungated | MS-Request: apply CreateFirstAdminAsync pattern to GitHubOAuthCallbackHandler |
| F22: Zero CSRF | MS-Request: add [AutoValidateAntiforgeryToken] global filter |
| F23: Global rate limiter | MS-Request: per-client partitioning |
| F24: Settings UI ungitignored | MS-Request: add artifacts/ to .gitignore |
| F25: ChatServices dead methods | MS-Request: part of TSK-0042 |
| F26: Schema migration | MS-Request: needed for TSK-0201/0202 |
| F27: TreeSitter key | MS-Request: fix "c_sharp" → "CSharp" |
| F28: FixedTimeEquals ×3 | MS-Request: consolidate to shared helper in MemorySmith.Core |
