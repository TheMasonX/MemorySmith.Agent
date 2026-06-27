# Sprint 46 Wave C — "Tightening the Contracts"

**Date:** 2026-06-24
**Theme:** Safety + Trust
**Based on:** `sprint46-wavec-plan-council-2026-06-24.md`

## Completed (Wave A ✅ — Wave B ✅)

| Task | Priority | Status |
|---|---|---|
| TSK-0100: WebSocketBridge ReceiveLoopAsync | P0 | ✅ Done |
| TSK-0101: 7 catch→null blocks with logging | P0 | ✅ Done |
| TSK-0102: ChatServices cross-repo request | P0 | ✅ Done (doc created) |
| TSK-0105: Documentation drift | P2 | ✅ Done |
| TSK-0099: Alias dictionary extraction | P2 | ✅ Done |
| TSK-0103: BuildOrigin value object (absorbs TSK-0098) | P1 | ✅ Done |
| TSK-0104: ReplanResult type + ReplanGoalContext | P1 | ✅ Done |
| TSK-0106: Error-path tests | P2 | ✅ Done |

## Wave C Tasks — ✅ Complete

| Task | Priority | Theme | Description | Status |
|---|---|---|---|---|
| **TSK-0109** | P1 | Metadata | Fix `/api/about` — version → 0.46.0, phase → "Sprint 46" | ✅ Done |
| **TSK-0110** | P1 | Auth safety | ApiKeyMiddleware — explicit `AllowUnauthenticatedApi` flag | ✅ Done |
| **TSK-0111** | P2 | Error handling | Sweep 3 remaining bare catches (RestMemoryGateway, WebSocketBridge×2) | ✅ Done |

### Wave C Change Summary

| Task | Files Changed | Key Changes |
|---|---|---|
| **TSK-0109** | `WebUI.Blazor/Program.cs`, `Sprint46Tests.cs` | Version 0.37.0→0.46.0, Phase → "Sprint 46 — Tightening the Contracts". Added `ApiAbout_ReturnsCorrectVersionAndPhase` test. |
| **TSK-0110** | `ApiKeyMiddleware.cs`, `Sprint32Tests.cs` | Added `AllowUnauthenticatedApi` config (default false). Fail-closed when no key + no opt-in. Updated 1 test, added 1 new test. |
| **TSK-0111** | `WebSocketBridge.cs` (lines 96, 480, 458), `RestMemoryGateway.cs` | Added `ILogger` + `LogWarning` at all 3 bare-catch sites. Upgraded `ParseEvent` `Debug.WriteLine` to proper logger. |

### Validation
- `dotnet build`: 0 warnings, 0 errors
- `dotnet test`: 666/666 passed, 0 failed (new + updated tests)

## Deferred to Sprint 47+

| Task | Priority | Notes |
|---|---|---|
| TSK-0107 | P3 | Runtime decomposition planning (needs scoping doc) |
| TSK-0108 | P3 | Redundant state cleanup (requires TSK-0107 scope) |
| TSK-0083 | P3 | Checkpoint tests ~50% remainder |
| TSK-0084 | P3 | ApplySmeltComplete (no current consumer) |
| TSK-0085 | P3 | HasFailed dead code |
| TSK-0093 | P1 deferred | ParseItemSpec structured result (breaking, no consumer) |
| TSK-0096 | P1 deferred | Mining double-counting (need real-world evidence) |

## New Audit-Derived Tasks (Sprint 47+)

| Task | Priority | Source Audit(s) | Confidence |
|---|---|---|---|
| **TSK-0112**: Fix CraftItem prerequisite count scaling | P1 | code_audit + followup_audit | 96% |
| **TSK-0113**: Add drop-resolution table (mined block ≠ inventory item) | P2 | code_audit + sprint35_audit | 88% |
| **TSK-0114**: Preserve structured exception metadata in ToolDispatcher | P1 | code_audit + followup_audit + sprint35_audit | 93% |
| **TSK-0115**: Unify ActionQueue synchronization | P2 | code_audit + followup_audit | 84% |
| **TSK-0116**: Move creative-mode build into decomposer layer | P2 | followup_audit | 90% |
| **TSK-0117**: Post-craft/post-smelt inventory reconciliation | P2 | sprint35_audit | 98% |
| **TSK-0118**: Resolve chat interpretation split-brain | P2 | sprint35_audit | 99% |

## Validation

- All tasks: `dotnet build` → 0 warnings, 0 errors
- All tasks: `dotnet test` → 0 failures, no regressions
- Council report: `Data/Pages/council/sprint46-wavec-plan-council-2026-06-24.md`
