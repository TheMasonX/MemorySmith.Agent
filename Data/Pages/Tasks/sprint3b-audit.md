# Sprint 3b Audit — Findings & Tasks
**Source commit:** `6fef0c36589b33e3b263e881b084a62bb01fd5f8`  
**Auditor verdict:** Approve with revisions  
**Recorded:** 2026-06-17

---

## HIGH — FindFlatAreaTool.InputSchema lifetime bug

`FindFlatAreaTool.InputSchema` calls `JsonDocument.Parse(...).RootElement` and returns the
element directly. The `JsonDocument` is not stored anywhere — it is disposed immediately
after the property returns, leaving the returned `JsonElement` backed by freed memory. This
is a correctness bug in any code that later reads the element (e.g., `ToolDispatcher`
schema validation).

**Fix:** Cache the `JsonDocument` as a `static readonly` field (or call `.Clone()` on the
root element before returning).

**Status:** ✅ Fixed in `sprint-5-tool-safety` branch (static cached document pattern).

---

## MEDIUM/HIGH — Flat-area scan: narrow vertical window + area-only scoring

The Node.js `findFlatArea` handler samples only `botY + 4` down to `botY - 6` (a 10-block
vertical band). Valid build sites above or below that window are silently missed. Scoring
is purely by BFS connected-cell count, so a long irregular strip can win over a more
compact, usable pad.

**Fix:** Widen the vertical scan window (configurable, default ±15). Add a compactness
score (`area / boundingBoxArea`) to penalise irregular shapes and favour square pads. Expose
`clearanceAbove` check (N air blocks) to confirm the site is not under a ceiling.

**Status:** 🔲 Tracked — see `MineflayerAdapter/index.js` `findFlatArea` handler.

---

## MEDIUM — FlatAreaFoundEvent is observable but not yet actionable

`FlatAreaFoundEvent` arrives, is logged, and is stored as world-state facts, but nothing
in the planner turns the result into a build-origin selection. The `FindFlatArea →
GetStatus` HTN decomposition produces no follow-through.

**Fix:** In `HtnTaskLibrary`, after `FindFlatArea` succeeds, read the `flatArea:center:x/y/z`
facts and call `AgentBackgroundService.SetBuildOrigin(blueprintId, x, y, z)` before
queuing the Build phase. This closes the loop from scan → origin → build.

**Status:** 🔲 Tracked — Sprint 4 candidate.

---

## LOW/MEDIUM — Missing tests for new event surface

`ParseEvent`, `FlatAreaFoundEvent`, the typed event hierarchy, and the flood-fill scanner
have no direct unit tests. The most protocol-sensitive code in this commit is untested.

**Fix:** Add:
- `WorldEventParserTests.cs` — round-trip ParseEvent for each typed event including
  `flatAreaFound` (use a JSON fixture matching the Node wire format).
- `FindFlatAreaAdapterTests.cs` — verify BFS flood-fill edge cases: empty radius,
  single-cell result, result exactly at minFlatArea threshold.

**Status:** 🔲 Tracked — Sprint 4 candidate.

---

## Wins (keep)

- Typed event hierarchy (`WorldEvent` → sealed record subtypes) is a genuine upgrade over
  `WorldEvent(string, Dictionary<string, object?>, ...)`. Pattern-matching in the projector
  is now compile-time checked.
- `IItemRegistry` TTL cache is a real throughput improvement with coverage (hit, miss,
  expiry, disabled-cache).
- `WorldStateProjector` is now much easier to reason about and extend.

---

## Error→LLM Recovery (user idea, already partially implemented)

`AgentBackgroundService.TryRecoverFromGameErrorAsync` was added in `cb64b33`. It passes the
current error string to `chatInterpreter.InterpretAsync` (with `onlinePlayers: 1` to force
addressing) when `consecutiveFailures >= 2`. This is the right seam.

**Planned improvements (future sprint):**
- Include current inventory and available tools in the recovery prompt so the LLM can
  suggest concrete alternatives (e.g., "mine spruce instead of oak").
- Trigger immediately for specific high-confidence errors (blockNotFound, recipeMissing)
  rather than waiting for 2 consecutive failures.
- Add `ErrorRecovery` as a first-class `JournalEntryType` to make recovery attempts
  visible in the journal.
