# Sprint 54: Thread Safety, Data Model & Cleanup

**Date:** 2026-06-27  
**Parent audit:** [council-review-msa-audit-6-27-26.md](../Audit/council-review-msa-audit-6-27-26.md)  
**Status:** Planned  
**Capacity:** 6 days

---

## Sprint Objective

Close P2 data model, cleanup, CI, and documentation gaps. Complete remaining thread-safety and data normalization work. Establish CI coverage reporting and Node.js adapter testing.

---

## Committed Items (P2)

### Wave A: Data Model Fixes (Days 1-3)

| Task ID | Description | Effort | Depends On |
|---------|-------------|--------|------------|
| [TSK-0197](tsk-0197-actiondata-tool-normalization.md) | Normalize `ActionData.Tool` to canonical form; remove dual-checks | M | — |
| [TSK-0198](tsk-0198-structured-effect-enum.md) | Convert `StructuredEffect.Type` to `EffectType` enum | M | — |
| [TSK-0199](tsk-0199-blueprint-dimension-validation.md) | Validate `Blueprint.Dimensions` against parsed grid | S | — |
| [TSK-0200](tsk-0200-schema-versioning-rfc.md) | Write schema versioning RFC document | M | — |

### Wave B: CI & DevOps (Days 3-5)

| Task ID | Description | Effort | Depends On |
|---------|-------------|--------|------------|
| [TSK-0195](tsk-0195-coverage-threshold-ci.md) | Add minimum coverage threshold to CI | S | — |
| [TSK-0201](tsk-0201-coverage-report-generation.md) | Add coverage report generation to CI | M | TSK-0195 |
| [TSK-0202](tsk-0202-nodejs-adapter-ci.md) | Add Node.js adapter testing to CI | M | — |
| [TSK-0203](tsk-0203-vulnerability-scan-ci.md) | Add dependency vulnerability scan to CI | S | — |
| [TSK-0204](tsk-0204-source-link-git-hash.md) | Add SourceLink for `GitHash` embedding | S | — |
| [TSK-0205](tsk-0205-global-json-sdk-pin.md) | Add `global.json` SDK pinning | S | — |

### Wave C: Cleanup & Docs (Days 5-6)

| Task ID | Description | Effort | Depends On |
|---------|-------------|--------|------------|
| [TSK-0206](tsk-0206-move-operational-classes.md) | Move `ActionQueue`/`WorldModel` to `Runtime/` | S | — |
| [TSK-0207](tsk-0207-delete-bak-files.md) | Delete `.bak` files, add `*.bak` to `.gitignore` | S | — |
| [TSK-0208](tsk-0208-remove-eventlog-sink.md) | Remove unused `Serilog.Sinks.EventLog` | S | — |
| [TSK-0209](tsk-0209-fix-version-drift.md) | Fix version drift (README, `/api/about`, `home.md`) | S | — |
| [TSK-0210](tsk-0210-update-stale-wiki.md) | Update stale wiki pages (`getting-started.md`, `architecture.md`) | S | — |
| [TSK-0211](tsk-0211-scripts-relative-paths.md) | Fix hardcoded paths in scripts (use `$PSScriptRoot`) | M | — |

---

## Stretch Items (if Sprint 53 stretch items spill over)

- [TSK-0212](tsk-0212-vision-test-stub.md) Stub `Agent.Vision` test structure
- [TSK-0213](tsk-0213-minecraft-adapter-contract-tests.md) Add `MinecraftAdapter`/`WebSocketBridge` contract tests
- [TSK-0214](tsk-0214-search-result-kind-enum.md) Convert `SearchResult.Kind` to enum
- [TSK-0215](tsk-0215-belief-observation-consolidation.md) Consolidate `BeliefState`/`ObservationState` shared interface

---

## Exit Criteria

- [ ] `ActionData.Tool` normalized; dual-checks removed
- [ ] `StructuredEffect.Type` is an enum
- [ ] `BlueprintParser` validates dimensions vs grid
- [ ] Schema versioning RFC written
- [ ] CI generates and publishes coverage reports
- [ ] Node.js adapter tested in CI
- [ ] Vulnerability scan in CI (zero results)
- [ ] `.bak` files deleted; operational classes moved
- [ ] All version strings consistent (README, `/api/about`, wiki pages)
- [ ] Scripts use relative paths
- [ ] `global.json` present
- [ ] SourceLink configured
- [ ] All 750+ tests pass; 0 build warnings

---

## Capacity Assumptions

- 1 developer × 6 days focused
- S = <2 hours, M = 2-4 hours, L = 4-8 hours

---

## References

- Council review: [council-review-msa-audit-6-27-26.md](../Audit/council-review-msa-audit-6-27-26.md)
- Sprint 53 plan: [sprint-53-security-test-coverage.md](sprint-53-security-test-coverage.md)
