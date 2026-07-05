# MemorySmith.Agent — Deep Audit Delta #3 (Sprint 58 Wave D / dev/round-3)

Report: msa_sprint58_deep_audit_delta3_dev_round3_20260701_2100_CT.md

Generated: 2026-07-01 21:00 CT
Same commit: `57b8fdf79ff7ba6f877e0fcac2211934e0f3279c` (`dev/round-3`)

**Delta-only.** Does not repeat F1–F14 from the prior two reports. New this pass: full reads of `Agent.Construction/*` (`BlueprintParser.cs`, `BlueprintExecutor.cs`, `BlueprintSchema.cs`), `Agent.World.Minecraft/WebSocketBridge.cs` (full, including the reconnect/retry loop), `Agent.Memory/LocalKnowledgeResolver.cs`, and a cross-check of `HtnPlanner`'s duplicate origin-resolution logic against `BuildGoalDecomposer`'s.

---

## Findings Table (New This Pass)

| # | Finding | Class | Impact | Likelihood | Severity | Confidence |
|---|---|---|---|---|---|---|
| F15 | `HtnPlanner` and `BuildGoalDecomposer` each have their own `ReadOriginFact` — the copies have already diverged, and `HtnPlanner`'s is a cruder regression | Verified Issue / Duplication | 2 | 1 (dead path today) | 2 (Low) | 90% |
| F16 | `WebSocketBridge`'s reconnect loop silently gives up after the **first** failed reconnect attempt instead of retrying up to `maxRetries` | Verified Issue | 4 | 4 | **16 (High)** | 90% |
| F17 | Recurring "built but never cut over" pattern — 4 independent, tested subsystems exist solely behind diagnostic API endpoints and have zero integration into the live agent loop | Architectural Theme | — | — | — | 90% |

---

## F16. `WebSocketBridge` reconnect logic silently aborts after one failed attempt instead of retrying
**Classification:** Verified Issue · **Severity 16/25 (High)** · **Confidence: 90%**

`RunReceiveLoopWithRetryAsync` (`WebSocketBridge.cs:162-244`) is documented and structured to retry up to `maxRetries = 3` times with a 5-second delay between attempts:
```csharp
for (var attempt = 0; attempt <= maxRetries; attempt++)
{
    ...
    try { await ReceiveLoopAsync(ct); return; }              // "Normal completion"
    catch (WebSocketException ex) when (attempt < maxRetries) { /* log warning */ }
    catch (Exception ex) when (attempt < maxRetries)          { /* log warning */ }

    try
    {
        await Task.Delay(retryDelayMs, ct);
        _ws?.Dispose();
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(_uri, ct);                     // reconnect
        ... resend handshake ...
    }
    catch (Exception ex) { /* log "Reconnect failed" */ }     // falls through, no continue/break/return
}
// only reached if the loop completes all iterations without an early return
_logger.LogError("All {MaxRetries} reconnect attempts failed. Agent is permanently deaf.");
```
`ReceiveLoopAsync`'s outer condition is `while (_ws is { State: WebSocketState.Open } && !ct.IsCancellationRequested)` (`WebSocketBridge.cs:249`).

Trace of a realistic outage (Node.js adapter down for >5s, a plausible restart/redeploy window):
1. **Attempt 0:** `ReceiveLoopAsync` throws (socket dropped) → caught, warning logged, `attempt < maxRetries` guard passes (0 < 3).
2. Reconnect block runs: `_ws = new ClientWebSocket(); await _ws.ConnectAsync(_uri, ct);` — the Node server still isn't up, so this **throws**, caught by the generic `catch (Exception ex)` a few lines down, which only logs a warning ("Reconnect failed (attempt 1/3)") and falls through — there is no `continue` needed since it's already at the bottom of the loop body, so the `for` naturally proceeds to attempt 1. `_ws` remains the newly-constructed-but-never-successfully-connected `ClientWebSocket` (state `None`/`Closed`, not `Open`).
3. **Attempt 1:** `await ReceiveLoopAsync(ct)` is called again. Its `while` condition checks `_ws.State == Open`, which is **false** (the reconnect never succeeded) — so the loop body never executes and the method **returns immediately, without throwing**.
4. Back in `RunReceiveLoopWithRetryAsync`: `await ReceiveLoopAsync(ct); return; // Normal completion (connection closed cleanly)` — this `return` fires. The method exits **entirely**, treating an unopened socket as if the connection had closed cleanly.
5. The `finally` block runs `_inbound.Writer.TryComplete()`, permanently closing the inbound event channel.
6. **The "All retries exhausted... Agent is permanently deaf" `LogError` is never reached** — the only trace in the logs is a single `LogWarning` for the first failed reconnect. Attempts 2 and 3 (which the code and its own comments claim will happen) never run.

**Net effect:** the retry mechanism only tolerates a reconnect succeeding on the very first try after the very first drop. Any outage where the Node.js side takes longer than one `5s` delay to come back up causes the C# agent to go permanently deaf, silently, with log output that understates the severity (a `LogWarning` instead of the intended `LogError`) and gives no indication that 2 of the 3 documented retry attempts were skipped.

**Why untested:** no test file exercises `WebSocketBridge`'s reconnect path at all (confirmed by repo-wide search — the only test file referencing the class name is `WorldStateProjectorTests.cs`, incidentally, not a reconnect test).

**Recommendation:** Distinguish "the receive loop returned because the socket was never opened" from "the receive loop returned because the connection closed cleanly." The simplest fix: after a failed reconnect (`_ws.State != Open`), don't fall through and re-enter `ReceiveLoopAsync` — either `continue` explicitly, only entering `ReceiveLoopAsync` when `_ws.State == Open` is confirmed true post-reconnect, or track a `reconnectSucceeded` bool and skip straight to the next retry-delay cycle if the reconnect itself failed, rather than treating it as loop-worthy input to `ReceiveLoopAsync`. Add a test that fails the first reconnect attempt (mock `ConnectAsync` to throw once) and asserts the loop tries a second and third time before giving up, and that the final "permanently deaf" `LogError` actually fires when all three are exhausted.

---

## F15. `HtnPlanner` and `BuildGoalDecomposer` each maintain their own `ReadOriginFact` — and the copies have diverged
**Classification:** Verified Issue / Duplication · **Severity 2/25 (Low, currently unreachable)** · **Confidence: 90%**

Both classes read the same world-state fact (`build:{blueprintId}:origin:{axis}`) via independently-written private methods:
- `BuildGoalDecomposer.ReadOriginFact` (`BuildGoalDecomposer.cs:86-105`) — 4-arg, `out bool found`, lets the caller distinguish "fact present" from "defaulted to 0," and uses that to pick `BuildOriginSource.Explicit` vs `.AutoScanned` correctly-ish (this is the already-tracked P1-5 mislabeling issue from the prior audit — it still doesn't have a third state for "found via facts," but at least tracks presence).
- `HtnPlanner.ReadOriginFact` (`HtnPlanner.cs:155-...`) — 3-arg, no `found` output at all, and the caller unconditionally hardcodes `BuildOriginSource.AutoScanned` regardless of whether the fact existed (`HtnPlanner.cs:66`). This is a cruder regression of the same bug P1-5 already flags in `BuildGoalDecomposer` — here there isn't even an attempt to distinguish sources.

This is currently low-impact because `HtnPlanner`'s `BuildGoal` branch is dead in practice: `PlannerRouter` always tries `DecomposerRegistry` first, and `BuildGoalDecomposer.CanHandle(IBuildGoal)` matches every real `BuildGoal`, so `HtnPlanner` never actually sees one in production (per its own class doc, confirmed accurate for this specific case unlike the doc-drift instances in F6/F12 — this part of the comment is true). But it's a second, independently-maintained copy of build-origin resolution logic sitting in a codebase already flagged for having the exact type of overlapping-responsibility problem (P2-5, F6) between these two classes — and it demonstrates concretely what happens when duplicated logic isn't kept in sync: the copies already disagree on correctness, not just style.

**Recommendation:** Bundle with the P2-5/F6 cleanup (removing `HtnPlanner`'s redundant type-switch branches entirely, since `BuildGoalDecomposer`/`CraftItemGoalDecomposer`/`GatherGoalDecomposer` already cover these types via the registry). If any origin-reading logic needs to survive for a genuine fallback case, extract a single shared `static BuildOriginResolver.ReadOrigin(WorldState, blueprintId)` helper used by both, so there's exactly one implementation to keep correct.

---

## F17. Recurring pattern: sophisticated subsystems built and tested, never cut over into the live agent loop
**Classification:** Architectural Theme · **Confidence: 90%**

This pass adds a fourth confirmed instance of a pattern first noted with the dead manager layer (prior audit, A-1) and TSK-0310's `IGoalPrecondition` (F2): a fully-built, reasonably sophisticated, unit-tested subsystem that is **only reachable through a diagnostic HTTP endpoint**, with zero call sites in the actual chat → intent → goal → plan → dispatch pipeline that runs the live agent.

Confirmed instances as of this audit:
1. **Manager layer** (`PlanningManagerImpl`, `RecoveryManagerImpl`, `ExecutionManagerImpl`, `StateManagerImpl`, `IntentManagerImpl`, `DashboardPublisherImpl`) — prior audit, TSK-0292/0293.
2. **`IGoalPrecondition.CanAttempt`** (TSK-0310, Sprint 58) — only called from the dead manager layer above (report 1, F2).
3. **`AliasRegistry.TryResolve`/`Search` fuzzy item-name matching** (Sprint 57, TSK-0304) — only called from `Sprint30Tests.cs`; the live `LlmChatInterpreter` uses only the static prompt-hint text (report 2, F12).
4. **`LocalKnowledgeResolver`/`IKnowledgeResolver`** (Phase 7-B) — a genuinely well-designed multi-source resolver (registry → wiki search → world facts, with confidence scoring and ambiguity detection) registered in DI (`Program.cs:155-156`) and exposed only via `GET /api/agent/resolve` (`Program.cs:791-825`). Zero references in `AgentBackgroundService.cs` or anywhere in the `Agent.Planning` chat/goal pipeline.

None of these are individually mysterious — each has a plausible, documented reason to exist (diagnostics, a future feature, a phased rollout). But four independent instances of the same shape — "we built the smarter version, wired it to an admin API for inspection, and never flipped the switch on the actual agent" — is a pattern worth naming explicitly rather than re-discovering piecemeal each audit. It also means: (a) test-suite green does not imply these features affect production behavior, and audits (including this one) need to keep explicitly checking "is this called from `AgentBackgroundService`?" as a standing question rather than assuming test coverage implies live-path coverage, and (b) there may be real, currently-unrealized value sitting in the codebase already — `LocalKnowledgeResolver` in particular looks like it would directly help the F12 problem (no runtime safety net for LLM item-name mismatches) if it were wired into `GoalFactory`'s item-resolution step instead of (or in addition to) `AliasRegistry`.

**Recommendation:** When scoping future sprints, add an explicit "wire into `AgentBackgroundService`/live path" acceptance criterion for any new resolver/precondition/manager-style subsystem, separate from "unit tests pass." Consider a lightweight audit script (or a standing manual checklist item) that greps for new public classes registered in `Program.cs` and confirms at least one call site exists outside `Program.cs`/tests/other diagnostic endpoints before marking a task complete — this would have caught #2 and #3 above at merge time.

---

## Updated Confidence Summary (This Pass)

| Confidence band | Findings |
|---|---|
| 90% | F15, F16, F17 |

F16 is the standout of this pass — a concrete, high-severity reliability bug in the connectivity layer between the C# agent and the Minecraft world, untested and silent when it fires. F17 is the more valuable long-term takeaway: it reframes several previously-separate findings (across all three reports) as one recurring process gap rather than four unrelated bugs.
