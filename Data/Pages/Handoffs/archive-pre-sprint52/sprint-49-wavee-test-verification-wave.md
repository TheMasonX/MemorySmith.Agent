# Sprint 49 Wave E — Test Verification Wave

**Date:** 2026-06-25
**Branch:** `sprint-35-llm-first`
**Author:** SteveBot
**Tests:** 731 passing, 0 failing (9 new tests)

---

## Summary

Focused test-verification wave completing two backlogged tasks by adding tests for
code already implemented in previous waves. No production code changes — all additions
are tests that validate existing behavior.

### Tasks Completed

| Task | Title | Priority | New Tests | 
|:---|---|:---:|:---:|
| TSK-0114 | Preserve Structured Exception Metadata in ToolDispatcher | P1 High | 3 |
| TSK-0115 | Unify ActionQueue Synchronization | P2 Medium | 6 |

---

## Changes Implemented

### TSK-0114: Structured Exception Metadata Tests

**File:** `MemorySmith.Agent.Tests/ToolDispatcherTests.cs`

Added 3 tests + `ThrowingTool` + `SpyJournal` test helpers:

| Test | What it verifies |
|------|-----------------|
| `CallAsync_ThrowingTool_CapturesExceptionTypeInJournal` | Exception type (`InvalidOperationException`) and stack trace appear in journal entry `Details` dict |
| `CallAsync_ThrowingTool_ReturnsFailureWithExceptionType` | User-facing `ToolResult(false, ...)` includes exception type name in message |
| `CallAsync_ThrowingTool_JournalHasDetailsDictionary` | All four metadata fields present: `exceptionType`, `stackTrace`, `innerException`, `message` |

The production code (added in Wave C/D) was already logging exception metadata via
`_logger?.LogWarning(ex, ...)` and journal entries. These tests verify the contract
is preserved and that the user-facing boundary stays sanitized.

### TSK-0115: ActionQueue Concurrency Tests

**File:** `MemorySmith.Agent.Tests/CoreModelsTests.cs`

Added 6 tests in new `ActionQueueConcurrencyTests` fixture:

| Test | What it verifies |
|------|-----------------|
| `ClearAndEnqueue_IsAtomic_RelativeToConcurrentEnqueue` | After concurrent `Enqueue` + `ClearAndEnqueue`, priority action is always first |
| `ClearAndEnqueueAsync_StopCallbackFailure_StillClearsQueue` | TSK-0119 fix: stop callback throw doesn't prevent queue clear |
| `ClearAndEnqueueAsync_StopCallbackSuccess_WorksNormally` | Normal path: stop callback invoked, queue cleared, priority action enqueued |
| `ConcurrentEnqueueDequeue_DoesNotCorruptState` | 4 producers + 2 consumers stress test — no corrupted items |
| `AllOperations_UseSameLock_ConsistentCount` | Count/IsEmpty/Peek all reflect consistent state under lock |
| `EnqueueAll_AddsAllActionsInOrder` | FIFO ordering preserved across `EnqueueAll` |

The production code was already fixed in TSK-0113 (single lock for all operations)
and TSK-0119 (stop callback resilience). These tests validate those fixes.

---

## Remaining Deferred Tasks

| Task | Title | Priority | Reason |
|:---|---|:---:|---|
| TSK-0107 | Build Origin Sentinel — eliminate (0,0,0) overload | High | Invasive refactor of `DecomposeBuild` — needs direct file write |
| TSK-0116 | Move creative-mode build handling into decomposer layer | P2 | Architecture consistency — backlog |
| TSK-0117 | Add post-craft/post-smelt inventory reconciliation | P2 | Correctness — backlog |
| TSK-0118 | Resolve chat interpretation split-brain architecture | P2 | Architecture consistency — backlog |

---

## Build & Tests

```
Build succeeded. 0 Error(s)
Passed!  - Failed: 0, Passed: 731, Skipped: 0, Total: 731 (9 new tests)
```
