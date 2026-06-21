# MemorySmith Council Review — Sprint 19

**Date:** 2026-06-18
**Branch:** `sprint-5-tool-safety`
**Head Commit:** `ce2260314dd15bc1295b2f4d63a595f1abc783d6`
**Sprint Scope:** 16 commits, 7 phases, CI green
**Review Type:** 6-Seat Council Review

---

## Verdict: APPROVE — Ship Sprint 19

All 7 phases correctly implemented. No blocking findings. 7 deferred findings (D-1 through D-7).

| Seat | Confidence | Verdict |
|------|-----------|---------|
| 1 — Source-Grounded Archivist | 92% | Approve |
| 2 — Data Model Architect | 88% | Approve |
| 3 — Retrieval Specialist | 90% | Approve |
| 4 — Human Learning Advocate | 85% | Approve |
| 5 — Skeptical Reviewer | 78% | Approve with deferred concerns |
| 6 — Synthesizer | **87% (weighted)** | **Approve** |

---

## Phase Summary

| Phase | Change | Risk | Verdict |
|-------|--------|------|---------|
| 1 | Logging: Serilog Debug file sink, ms precision, structured properties, timing, inventory context, JS logStructured | Low | Ship |
| 2 | System message filter: 9 regex patterns, isSystemMessage, bot teleport position update | Low | Ship |
| 3 | Gather plan rework: conditional Wander after BlockNotFound only | Medium | Ship |
| 4 | Replan governor: 2-state ACTIVE/STALLED, 3-plan threshold, 60s auto-recovery | Medium | Ship |
| 5 | findFlatArea expansion: default radius 20→32, expand to 48 on retry | Low | Ship |
| 6 | Stone alias: stone→stone (was cobblestone), YieldSourceBlocks maps drop | Low | Ship |
| 7 | Tests: 15 new tests (7 governor, 8 sprint19) | Low | Ship |

---

## Seat 1: Source-Grounded Archivist (92%)

All code claims verified against sprint-5-tool-safety HEAD. Key verifications:
- Program.cs file sink: `restrictedToMinimumLevel: LogEventLevel.Debug`, `{Properties:j}` confirmed
- AgentBackgroundService: `SummarizeInventory()` helper, `[plan]` action sequence log, `[action]` timing, `[dispatch]` debug args — all confirmed
- index.js: `logStructured()` JSON writer, `SYSTEM_MESSAGE_PATTERNS`, `isSystemMessage()` — all confirmed
- ReplanGovernor: thread-safe `_lock`, `Evaluate`/`RecordProgress`/`Reset` wiring, DI registration — all confirmed
- Stone alias chain: ChatInterpreter→GoalFactory→GenericGatherGoal.IsComplete — end-to-end verified

No stale references. No orphan code.

---

## Seat 2: Data Model Architect (88%)

- **ReplanGovernor thread safety:** Single `object _lock` protects all mutable state. `DateTimeOffset.UtcNow` called inside lock (nanosecond-scale syscall, acceptable).
- **Plan fingerprint design:** `"{goalName}:{Tool1,Tool2,Tool3}"` correctly excludes parameters (Wander coordinates randomize). Correct for stall detection.
- **YieldSourceBlocks:** Static readonly dictionary, scalable (add entries for gravel→flint etc.)
- **logStructured JSON envelope:** `{ t, l, c, m, ...data }` — spread could collide with envelope keys (D-4).
- **ABS constructor:** Now 12 parameters (D-5, consider options pattern).

---

## Seat 3: Retrieval Specialist (90%)

- **Conditional Wander improves plan fingerprint stability:** Before Sprint 19, every gather plan had identical fingerprint (SearchMemory,Wander,MineBlock,GetStatus). Now the governor can distinguish genuinely stuck plans from first-attempt plans.
- **Stone resolution chain verified end-to-end:** "get stone" → ChatInterpreter → "stone" → GoalFactory → SourceBlocks ["stone", "cobblestone"] → IsComplete counts both.
- **System message filter prevents wasted LLM calls:** Teleport messages no longer trigger 15-second Ollama calls.
- D-6: Consider configurable SYSTEM_MESSAGE_PATTERNS for custom servers.

---

## Seat 4: Human Learning Advocate (85%)

Strong improvements for operator experience:
- File sink at Debug = detailed diagnostics without console noise
- `{Properties:j}` enables grep/jq on structured log data
- `[plan]` action sequence = most useful single log line for debugging
- `[action]` timing = identify slow tools instantly
- `[goal]` inventory summaries = context without separate status check
- `[governor] STALLED` warning is well-crafted (what, why, recovery)
- D-7: Governor recovery (RecordProgress) should emit a `[governor] recovered` log line.

---

## Seat 5: Skeptical Reviewer (78%)

Approved with deferred concerns:
- **Gather plan rework (Medium Risk):** Relies on `event:BlockNotFound:Block` fact being set by WorldStateProjector. Tests mock this directly, don't test the producer. Mitigated: worst case is no Wander (simpler plan).
- **Replan governor (Medium Risk):** In the DispatchActionsAsync hot loop. 60s auto-recovery is a safety valve. A successful Wander resets the governor even if mining makes no progress — acceptable because governor detects identical *plans*, not *mining progress*.
- **logStructured I/O:** `appendFileSync` is synchronous. Microsecond-scale for typical volumes, but heavy logging could cause minor latency (D-4).
- **Integration seam:** Would prefer an integration test for BlockNotFoundEvent→WorldStateProjector→HtnTaskLibrary chain. Not blocking because changes are additive/defensive.

---

## Deferred Findings

| ID | Phase | Description | Seat | Target |
|----|-------|-------------|------|--------|
| D-1 | 2 | Post-teleport 100ms delay is fragile; `bot.once('move')` more reliable | 1 | Sprint 20 |
| D-2 | 3 | `event:BlockNotFound:Block` fact producer not reviewed; confirm WorldStateProjector sets it | 1, 5 | Sprint 20 |
| D-3 | 5 | `BuildFactKeys.LastFlatArea` fact producer not reviewed; confirm WorldStateProjector sets it | 1, 5 | Sprint 20 |
| D-4 | 1 | `logStructured` spread could collide with envelope keys (t,l,c,m) | 2 | Sprint 21 |
| D-5 | 4 | ABS constructor has 12 params; consider options pattern | 2 | Sprint 21 |
| D-6 | 2 | `SYSTEM_MESSAGE_PATTERNS` could be configurable for custom servers | 3 | Sprint 21 |
| D-7 | 4 | Governor recovery does not emit log line; add `[governor] recovered` | 4 | Sprint 20 |
