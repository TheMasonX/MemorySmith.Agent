# Sprint 49 Wave D — Audit Hardening Wave 2

**Date:** 2026-06-25
**Branch:** `sprint-35-llm-first`
**Author:** SteveBot
**Tests:** 722 passing, 0 failing

---

## Summary

Third wave of Sprint 49 work, driven by the independent deep code audit at `Data/Pages/Audit/memorysmith_agent_deep_audit_2026-06-25_a3be512d.md`. Implements the remaining deferred tasks plus new findings from the audit.

### Audit Findings Addressed

| Finding | Severity | Confidence | Task | Status |
|---------|:--------:|:----------:|------|:------:|
| Finding 2 — Tool outcomes flattened | Med-High | 91% | TSK-0110 | ✅ Done |
| Finding 3 — Damage interrupt coupling | Medium | 82% | TSK-0119 | ✅ Done |
| Finding 4 — Documentation/version drift | Medium | 98% | — | ✅ Done |
| Finding 5 — Duplicate-reg diagnostics muted | Low | 70% | TSK-0120 | ✅ Done |

---

## Changes Implemented

### TSK-0110: Enrich ToolResult with OutcomeType

**Files:** `Agent.Core/Models/ActionData.cs`, `Agent.Tools/ToolDispatcher.cs`

Added `OutcomeType Outcome` as an optional fourth parameter to `ToolResult` (defaults to `OutcomeType.Completed` for backward compatibility). Updated `CallWithOutcomeAsync` to map the outcome type through a new `MapResultToOutcome` method that preserves the rich semantics:

- `OutcomeType.Completed` → `ActionOutcome.Succeeded`
- `OutcomeType.NoProgress` → `ActionOutcome.NoProgress`
- `OutcomeType.Blocked` → `ActionOutcome.Blocked`
- `OutcomeType.Unreachable` → `ActionOutcome.Unreachable`
- `OutcomeType.TimedOut` → `ActionOutcome.TimedOut`
- Default (Success=false, Outcome=default) → `ActionOutcome.Failed`

Tools can now return `new ToolResult(false, "prerequisite missing") { Outcome = OutcomeType.Blocked }` and the dispatcher will preserve that semantic for the LLM evaluator and replan governor.

### TSK-0119: Decouple Emergency Stop Delivery from Queue Mutation

**File:** `Agent.Core/Models/ActionQueue.cs`

Wrapped the stop callback in `ClearAndEnqueueAsync` with a `try/catch` block. If the stop callback throws (e.g. WebSocket send failure), the exception is caught and logged via `Debug.WriteLine`, and the queue clear + priority enqueue **always** happens in the `lock` block that follows. Previously, a transient network failure on the stop path would prevent the queue clear entirely, leaving stale actions in the queue precisely when the bot most needs to stop (e.g. damage interrupt).

### TSK-0120: Wire ILogger into ToolDispatcher Construction

**File:** `WebUI.Blazor/Program.cs`

Changed `new ToolDispatcher(journal)` to `new ToolDispatcher(journal, sp.GetRequiredService<ILogger<ToolDispatcher>>())`. This enables the duplicate-registration `LogWarning` to actually emit, fixing a small observability gap identified by the audit.

### Version Drift Repair (Finding 4)

Updated all version markers to `v0.50.0` / `Sprint 49 — Dashboard Wave 1 + Audit-Driven Hardening`:

| File | Before | After |
|------|--------|-------|
| `WebUI.Blazor/Program.cs` header | v0.37.0 Sprint 37 | v0.50.0 Sprint 49 |
| `WebUI.Blazor/Program.cs` `/api/about` | v0.46.0, Sprint 46 | v0.50.0, Sprint 49 |
| `README.md` | v0.49.0 | v0.50.0 |
| `Data/Pages/roadmap.md` header | v0.40.0, Sprint 41 | v0.50.0, Sprint 49 |

Added Sprint 49 to roadmap sprint history and CI health table. Updated Phase 6 to ✅ COMPLETE and added Phase 7 for Dashboard & Audit Hardening work.

---

## Remaining Deferred Tasks

| Task | Title | Priority | Reason Deferred |
|:---|---|:---:|---|
| TSK-0107 | Build Origin Sentinel — eliminate (0,0,0) overload | High | Requires invasive refactor of `HtnTaskLibrary.DecomposeBuild` signature. Attempted and reverted in Wave C — edit tools have issues with the large method body. Needs direct file write. |
| TSK-0114 | Preserve Structured Exception Metadata in ToolDispatcher | P1 | Already has LogWarning for exception metadata — TSK-0114's journal enrichment is a separate concern. Deferred to Sprint 50. |
| TSK-0115-TSK-0118 | Various backlog items (ActionQueue tests, creative mode, inventory reconciliation, chat split-brain) | P2-P3 | Deferred — not in scope for Sprint 49 audit hardening. |

---

## New Task Records Created

| Task | Title | Priority | Source |
|:---|---|:---:|---|
| TSK-0119 | Decouple emergency stop delivery from queue mutation | P2 Medium | Audit Finding 3 |
| TSK-0120 | Wire ILogger into ToolDispatcher construction | P3 Low | Audit Finding 5 |

---

## Build & Tests

```
Build succeeded. 0 Error(s)
Passed!  - Failed: 0, Passed: 722, Skipped: 0, Total: 722
```
