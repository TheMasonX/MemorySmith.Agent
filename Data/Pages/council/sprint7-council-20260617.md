# Sprint 7 Council Review — Chat Reliability, Observability, Logging
**Date:** 2026-06-17  
**Branch:** sprint-5-tool-safety  
**Final commit reviewed:** 3f391e5 (whole-word name match)  
**Sprint 7 scope:** LLM chat fixes · Observability APIs · Thinking indicator · Logging cleanup · System prompt hardening

---

## Delivered

| Commit | Change |
|--------|--------|
| `4003b88` | Bot renamed Leo, rate limit 5→20/min |
| `0ec465e` | FindFlatAreaTool.InputSchema use-after-dispose (HIGH) |
| `8efe6b7` | NavigateTo LLM fast-path + playerPos in prompt |
| `a9e91d6` | QueryStatus added to fast-path |
| `bf03b50` | Thinking indicator ("Hmm...") after 1.5s LLM delay |
| `86d2499` | IAgentJournal.Count property |
| `541e2c0` | AgentJournal implements Count |
| `c847e94` | NullAgentJournal singleton |
| `26a6186` | GET /api/agent/journal + /api/agent/worldmodel |
| `4668366` | Serilog: remove EventLog duplicate sink, clean output template |
| `a48c12f` | ChatIntentType.Chat for conversational LLM responses |
| `3f391e5` | ContainsBotName — whole-word match ("hello Leo" now addressed) |
| `5d84785` | RecordBotSpoke always called on addressed messages |
| `f9c553f` | Richer system prompt: health/food/inventory/capabilities, remove "ignore" |

---

## Seat Reviews

### Seat 1 — Source-Grounded Archivist
**Confidence: 0.85**

Sprint 7 directly addresses findings from the Sprint 4b audit council (D1–D7) and the original log-based diagnosis. All changes are consistent with the documented design intent.

**Observations:**
- `ContainsBotName` uses a Regex pattern. Because `botName` is user-configured (from appsettings), `Regex.Escape(botName)` is correctly applied — no injection risk.
- `FormatInventory` is a new private static helper in `LlmChatInterpreter`. It is not tested. Acceptable given it is purely formatting with no side effects.
- The "ignore" intent has been removed from the LLM prompt. This is a breaking change for any existing conversation history that referenced "ignore" — but since history is in-memory only, this has no persistence implications.
- `ChatIntentType.Chat` is added but no handler-side switch case was added; it falls through the existing pattern (`Unknown` → just send response). This is correct but implicit.

**Finding D1-deferred (deferred):** Uncertainty still not in `/api/agent/status`. Noted. Sprint 8.

---

### Seat 2 — Data Model Architect
**Confidence: 0.87**

The data model changes are minimal and correct.

**Positive:**
- `ChatIntentType.Chat` added cleanly to the enum — consistent with existing naming.
- `IAgentJournal.Count` is a non-breaking addition (all existing implementors have been updated).
- `NullAgentJournal.Instance` follows the Null-Object pattern correctly.

**Finding B1 (blocking):** `LlmChatInterpreter.FormatInventory` accepts a nullable `WorldState?` but uses `.Inventory.Count` which would NPE if `Inventory` were null. In practice `WorldState.Inventory` is always initialized to `[]`, but the defensive check should be:
```csharp
if (state is null || state.Inventory is null || state.Inventory.Count == 0) return "empty";
```

The current code does `state.Inventory.Count == 0` which is safe for the initialized record, but the null guard on `state` alone is insufficient to prevent a NRE if Inventory were somehow null. Add the explicit check.

**Finding D1 (deferred):** `ChatIntentType.Chat` falls through to the same handler path as `Unknown`. A future sprint should add an explicit `case ChatIntentType.Chat:` in the handler for clarity, even if the behavior is identical.

---

### Seat 3 — Retrieval Specialist
**Confidence: 0.82**

The observability endpoints are minimal but functional. They expose the journal query surface correctly.

**Positive:**
- `GET /api/agent/journal?limit=50&type=ActionFailed` — clean query API.
- `GET /api/agent/worldmodel` — correctly exposes belief/observed/uncertainty.
- `NullAgentJournal` is registered when agent is disabled, preventing 500s.

**Finding D2 (deferred):** The journal endpoint returns the full `JournalEntry` record including `Details` which is `IReadOnlyDictionary<string, object?>`. This serializes cleanly to JSON with System.Text.Json, but `object?` values may serialize unexpectedly (e.g., boxed ints become JSON numbers, DateTimeOffset values become strings). Consider a typed DTO for the API response in Sprint 8.

**Finding D3 (deferred):** `GET /api/agent/worldmodel` returns `BeliefState` and `ObservationState` which include `RecentObservations` (a list of `Fact`). This could be large. Consider adding a `?detail=false` parameter to return summary-only.

---

### Seat 4 — Human Learning Advocate
**Confidence: 0.90**

Sprint 7 is the most impactful quality-of-life sprint yet from a player experience perspective.

**Positive:**
- Thinking indicator ("Hmm...", "...", "Let me think...", "*thinks*") gives immediate feedback for slow LLM calls. This is the right UX pattern.
- Clean log output (no `INF`, no `[Serilog-EventLog-Fallback]` duplication) dramatically improves developer experience.
- Richer system prompt with health/food/inventory/capabilities will significantly improve LLM response relevance.
- Removal of `"ignore"` intent is critical — the 3b model was returning `ignore` for conversational messages, leaving players with silent non-responses.
- `RecordBotSpoke` always fires on addressed messages — conversation window stays open correctly.

**Finding D4 (deferred):** The "Hmm..." thinking message fires even when the LLM's response will be empty (interpreted as "ignore" or the LLM returns null). Players will see "Hmm..." then silence. With "ignore" removed from the prompt, this should be rarer, but the scenario persists for rate-limited fallbacks that return `Unknown` with an empty response. Consider always ensuring `quick.Response` is non-empty for `Unknown` intents in the pattern fallback.

---

### Seat 5 — Skeptical Reviewer
**Confidence: 0.74**

**Blocking finding B2:** `ContainsBotName` uses a `Regex.IsMatch` call on EVERY message. This is called from `IsDirectedAtBot`, which is called from `ChatInterpreter.InterpretAsync`, which is in turn called on every incoming chat message. For small bots (Leo = 3 chars), the regex will also match substrings of words ending without a letter — e.g., "Hello!" would match if the word before "!" ends with "Leo". The pattern `(?<![a-zA-Z0-9])Leo(?![a-zA-Z0-9])` correctly handles this but should also exclude underscore (`_`) since Minecraft usernames can contain underscores. A more robust pattern:
```csharp
$@"(?<![a-zA-Z0-9_]){Regex.Escape(botName)}(?![a-zA-Z0-9_])"
```

**Blocking finding B3:** The `ContainsBotName` regex is compiled each call (`Regex.IsMatch` without `RegexOptions.Compiled` creates a new object every time). Since `botName` changes only at startup, cache the compiled regex:
```csharp
private Regex? _botNameRegex;
private bool ContainsBotName(string message, string botName)
{
    _botNameRegex ??= new Regex(
        $@"(?<![a-zA-Z0-9_]){Regex.Escape(botName)}(?![a-zA-Z0-9_])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    return _botNameRegex.IsMatch(message);
}
```

**Finding D5 (deferred):** The `BuildSystemPrompt` inventory string uses `×` (×) as the multiplication sign. For the LLM this is fine. For the in-game chat response, if the bot echoes inventory info it will appear as the × character which renders correctly in most Minecraft clients. No action needed.

**Dissent on B1 severity:** B1 (FormatInventory null check) is low risk given `WorldState.Inventory` is initialized. I would classify it as deferred, not blocking. Deferring to council majority.

---

### Seat 6 — Synthesizer
**Confidence: 0.87**

**Summary of findings:**

| ID | Seat | Severity | Description |
|----|------|----------|-------------|
| B1 | Architect | BLOCKING | FormatInventory: Inventory null guard incomplete |
| B2 | Skeptic | BLOCKING | ContainsBotName: pattern should also exclude underscore |
| B3 | Skeptic | BLOCKING | ContainsBotName: regex compiled on every call — cache it |
| D1 | Archivist | Deferred | Uncertainty not in /api/agent/status |
| D2 | Retrieval | Deferred | Journal endpoint: typed DTO for API response |
| D3 | Retrieval | Deferred | WorldModel endpoint: large RecentObservations; add summary mode |
| D4 | HLA | Deferred | "Hmm..." fires when LLM will ultimately return empty response |
| D5 | Skeptic | Deferred | Inventory × character in prompt |

**On B1:** The FormatInventory null guard issue is conservative correctness. The `WorldState` record initializes `Inventory = []` so an actual NPE is unlikely. However, adding the explicit null check costs one line and eliminates the edge case entirely. Classify as blocking for clean code hygiene.

**On B2+B3:** The underscore boundary and regex caching are both real correctness issues (underscore exclusion prevents matching "Leo_12" as "Leo") and performance issues. Both are one-liners. Classify as blocking.

**Sprint 7 verdict: CONDITIONAL PASS.** Three small blocking fixes (B1, B2, B3) before merge. All deferred findings tracked for Sprint 8.

---

## Testable Acceptance Criteria

| # | Criterion |
|---|-----------|
| AC-1 | "hello Leo" triggers `IsDirectedAtBot = true` when `onlinePlayers = 2` |
| AC-2 | "Leo_bot hello" does NOT trigger `IsDirectedAtBot` via name match |
| AC-3 | Bot responds to conversational questions with `intent: chat` |
| AC-4 | After any addressed message, the next bare message is also addressed (window active) |
| AC-5 | `GET /api/agent/journal?limit=10` returns JSON with `count` and `entries` |
| AC-6 | Log output has no `[Serilog-EventLog-Fallback]` and no `INF` prefix |
| AC-7 | `FormatInventory(null)` returns `"empty"` without exception |
| AC-8 | CI build-and-test green |

