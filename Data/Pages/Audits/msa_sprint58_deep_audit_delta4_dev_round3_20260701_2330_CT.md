# MemorySmith.Agent — Deep Audit Delta #4 (Sprint 58 Wave D / dev/round-3)

Report: msa_sprint58_deep_audit_delta4_dev_round3_20260701_2330_CT.md

Generated: 2026-07-01 23:30 CT
Same commit: `57b8fdf79ff7ba6f877e0fcac2211934e0f3279c` (`dev/round-3`)

**Delta-only.** Does not repeat F1–F17. New this pass: a full read of `MineflayerAdapter/index.js`'s connection/reconnect lifecycle, `classifyError()`, the `mine` case handler end-to-end, and a cross-check of `ActionFailedEvent.ReasonCode`'s actual C#-side consumption against its documented purpose.

---

## Findings Table (New This Pass)

| # | Finding | Class | Impact | Likelihood | Severity | Confidence |
|---|---|---|---|---|---|---|
| F18 | Node adapter's reconnect exponential backoff is defeated by a premature counter reset — same bug family as F11/F16, now confirmed in a **third** independent retry mechanism | Verified Issue | 3 | 5 | **15 (High)** | 95% |
| F19 | `classifyError()`'s substring matching (`m.includes('no')`) is order-dependent and matches unrelated words (`not`, `node`, `ignore`) | Probable Issue (fragility) | 1 | 3 | 3 (Low — currently only feeds a log line) | 88% |
| F20 | `ActionFailedEvent.ReasonCode` is documented to enable "structured decisions" but is only ever logged, never branched on — a fifth instance of the F17 theme | Verified Issue / Doc Drift | 2 | 5 | 10 (Moderate) | 92% |

---

## F18. Node adapter's reconnect exponential backoff is defeated by a premature counter reset
**Classification:** Verified Issue · **Severity 15/25 (High)** · **Confidence: 95%**

`MineflayerAdapter/index.js` implements exponential backoff for reconnecting to the Minecraft server after a disconnect (Sprint 56, TSK-0263): `delay = min(2000 * 2^_reconnectAttempts, 60000)`, incrementing `_reconnectAttempts` on every disconnect (`index.js:409-434`). The counter is declared once at module scope (`index.js:311`) and has exactly three write sites:
```js
let _reconnectAttempts = 0;                    // index.js:311, initial declaration

function connectBot() {
  _reconnectAttempts = 0;                      // index.js:315 — reset on EVERY call, unconditionally
  bot = mineflayer.createBot(botOpts);
  ...
}

bot.on('end', (reason) => {
  const delay = Math.min(RECONNECT_BASE_DELAY_MS * Math.pow(RECONNECT_BACKOFF_FACTOR, _reconnectAttempts), RECONNECT_MAX_DELAY_MS);
  _reconnectAttempts++;                        // index.js:415
  _reconnectTimer = setTimeout(() => { connectBot(); ... }, delay);
});
```
`connectBot()` is called both for the initial connection at startup **and** for every scheduled reconnect (inside the `setTimeout` callback). Its first line unconditionally resets `_reconnectAttempts` to `0` — not gated on the connection actually succeeding (there is no `bot.once('spawn', ...)`-based reset; the reset happens the instant `connectBot()` is invoked, before `mineflayer.createBot()` even attempts the TCP handshake).

Trace of a sustained outage (Minecraft server down for more than a few seconds):
1. Disconnect → `'end'` fires → `delay = min(2000*2^0, 60000) = 2000ms`, `_reconnectAttempts` → 1.
2. Timer fires → `connectBot()` runs → **`_reconnectAttempts` reset to 0** → new bot object created → registers event handlers.
3. New bot also fails to connect (server still down) → its own `'end'` handler fires → `delay = min(2000*2^0, 60000) = 2000ms` again, because `_reconnectAttempts` was reset to 0 in step 2, before we knew step 2 would fail.
4. Repeat indefinitely — the delay **never exceeds the 2-second base value**, regardless of how many consecutive reconnect failures occur. The documented exponential curve (2s → 4s → 8s → 16s → 32s → capped 60s) never manifests.

**Effect:** during any outage longer than a couple of seconds, the adapter hammers the Minecraft server (or DNS/network) with a reconnection attempt every 2 seconds indefinitely, rather than backing off — generating log spam, unnecessary load, and (for servers with connection-rate limiting or anti-bot protections) a real risk of the reconnect attempts themselves being throttled or flagged. A secondary, related issue: the `"reconnected successfully"` log line and `sendEvent('reconnected', ...)` (`index.js:426-428`) fire immediately after `connectBot()` returns without throwing — `connectBot()` returning normally only means the JS object was constructed and handlers attached, not that the TCP/protocol handshake with the Minecraft server actually succeeded (that's asynchronous and only confirmed by the later `'spawn'` event). This means the "reconnected successfully" telemetry can be emitted even when the reconnect is about to fail.

**Same bug family as F11 (`ReplanGovernor`, C#) and F16 (`WebSocketBridge`, C#).** This is now the **third** independent retry/backoff mechanism in the codebase found to have a broken escalation or continuation guarantee, each via a different specific mistake (F11: resets on auto-recovery instead of only on confirmed progress; F16: misreads a failed-reconnect state as clean shutdown; F18: resets the attempt counter at the start of the very function whose success it's supposed to be conditioned on). See F17/this report's closing note for the cross-cutting implication.

**Recommendation:** Move the `_reconnectAttempts = 0` reset out of `connectBot()` and into the `'spawn'` handler (i.e., only reset on confirmed successful connection, not on connection *attempt*). Don't log/emit `"reconnected successfully"` until `'spawn'` actually fires; consider removing that message from the `setTimeout` callback entirely and relying on the existing `bot.once('spawn', ...)` handler's own logging. Add a test (or at least a manual verification note, since this file has no test harness at all — see F19's context) simulating two consecutive failed reconnects and asserting the second delay is longer than the first.

---

## F19. `classifyError()` uses fragile, order-dependent substring matching
**Classification:** Probable Issue (fragility) · **Severity 3/25 (Low today)** · **Confidence: 88%**

`classifyError(message, action)` (`index.js:185-206`) classifies Mineflayer error messages into reason codes using chained `String.includes()` checks, e.g. `m.includes('no') && (m.includes('path') || m.includes('route'))`. `.includes('no')` matches the substring `"no"` anywhere in the message — including inside `"not found"`, `"not spawned"`, `"not connected"`, `"not in inventory"`, `"ignore"`, `"node"`, etc., since `"not"` itself contains `"no"`. Because the checks are evaluated as an ordered if/else-if chain, the **first** matching branch wins regardless of which classification is semantically most specific — e.g., a hypothetical message combining "recipe" and "no crafting table" would classify as `missing_recipe` (checked earlier in the chain) rather than `crafting_table_not_found` (checked later), even if the latter is the more actionable/specific classification. This is inherent fragility rather than a confirmed misclassification in current traffic (real Mineflayer error strings are shorter and less composite than a contrived worst case), but the string-matching approach means any future change to Mineflayer's own error message wording could silently shift classifications with no test failure, since **there is no test file for `classifyError` at all**.

**Current real-world impact is low** because — per F20 below — `ReasonCode`/`errorType` is currently only used for a telemetry log line on the C# side, not for behavioral branching. If F20's recommendation (actually wire `ReasonCode` into evaluator/recovery decisions) is adopted, this fragility becomes materially more important to fix first.

**Recommendation:** Replace the ad hoc substring chain with explicit regex patterns anchored to word boundaries (e.g., `/\bnot\s+found\b/` vs. a bare `.includes('no')`), and add a small table-driven unit test asserting each known Mineflayer error string maps to the intended code. Do this before F20 is acted on, not after — otherwise a real decision-relevant path will be riding on the same fragile substring logic that currently only risks a mislabeled log line.

---

## F20. `ActionFailedEvent.ReasonCode` is documented as decision-enabling but is only ever logged — a fifth instance of the F17 pattern
**Classification:** Verified Issue / Documentation Drift · **Severity 10/25 (Moderate)** · **Confidence: 92%**

`ActionFailedEvent`'s XML doc (`Agent.Core/Events/WorldEvents.cs:284-288`) states its purpose explicitly: *"Enables the C# evaluator to make structured decisions about failures (path_timeout, no_block_found, missing_item, etc.) instead of parsing free-form error strings from the generic ErrorEvent."* The only consumption site in the entire C# codebase is `AgentBackgroundService.cs:977-980`:
```csharp
case ActionFailedEvent afe:
    logger.LogWarning(
        "[telemetry] action FAILED: {Action} reason={ReasonCode} detail={Detail} correlationId={CorrelationId}",
        afe.Action, afe.ReasonCode, afe.Detail, ...);
```
`ReasonCode` is interpolated into a log message and nothing else. There is no branch anywhere in `AgentBackgroundService.cs` or `Agent.Planning/*` that reads `afe.ReasonCode` to make a decision — no switch on `"path_timeout"` vs `"missing_item"` vs `"no_block_found"` to choose a different recovery strategy, and `LlmEvaluatorImpl`'s prompt-building code (`AppendGoalContext`, etc.) never surfaces it to the LLM either. The "structured decisions" the doc comment promises do not currently exist anywhere in the codebase.

This is the fifth confirmed instance of the pattern named in the prior report's F17 ("built but never cut over"): the JS side does real, fairly careful work classifying *why* an action failed (`classifyError`, 12 distinct reason codes), that classification survives the wire protocol intact as a first-class typed event (`ActionFailedEvent.ReasonCode`), and then the C# side — which is precisely where the "structured decision" was supposed to happen per the doc comment — discards all of that specificity into a single generic log line. Combined with F19, there is a straight line from "Mineflayer emits an error string" to "gets classified into one of 12 codes" to "arrives at the evaluator as a fully-typed event" to "is thrown away" — the entire pipeline for this feature is built except the last, actually-valuable step.

**Recommendation:** This is a good, low-risk candidate to actually finish rather than delete, unlike some of the other F17-family instances — the data already arrives at the right place (`AgentBackgroundService`'s event-dispatch switch) in the right shape (a typed `ReasonCode` string). A first useful cut: feed `afe.ReasonCode` into `LlmEvaluatorImpl`'s prompt context when the most recent action failed (e.g., "Last action failed: reason=no_block_found"), which directly gives the evaluator/replanner the structured signal the original doc comment promised, with no wire-protocol changes needed — the data is already flowing, just not read.

---

## Cross-Cutting Note: Three Independent Retry/Backoff Mechanisms, Three Independent Escalation Bugs

Across all four reports, three separate parts of the system implement a "back off progressively on repeated failure" mechanism, and all three have been found to not actually escalate as documented:
- **`ReplanGovernor`** (C#, report 2's F11): resets the stall-attempt counter on every auto-recovery, not just on confirmed progress.
- **`WebSocketBridge`** (C#, report 3's F16): misinterprets a failed-reconnect state as a clean connection close, exiting the retry loop after one failure instead of exhausting `maxRetries`.
- **Mineflayer adapter reconnect** (JS, this report's F18): resets the backoff exponent at the start of the very reconnect attempt it should be conditioned on succeeding.

No single mistake is shared between them — each is a distinct root cause in a different language/subsystem — but the *shape* of the mistake (a counter or state flag reset too early, before the outcome it's supposed to be gated on is actually known) recurs three times independently. That suggests this is a good candidate for a project-wide review pass specifically targeted at "every backoff/retry/graduated-delay mechanism in the codebase," rather than treating each as an isolated one-off fix, since the same review lens found three for three so far.

---

## Updated Confidence Summary (This Pass)

| Confidence band | Findings |
|---|---|
| 90–100% | F18 (95%), F20 (92%) |
| 80–89% | F19 (88%) |
