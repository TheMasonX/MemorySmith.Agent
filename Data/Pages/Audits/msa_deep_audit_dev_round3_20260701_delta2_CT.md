# MemorySmith.Agent — Deep Audit Round 3 (DELTA ONLY)

Report: msa_deep_audit_dev_round3_20260701_delta2_CT.md

Scope: **New findings only**, on top of round 1 (`..._1500_CT.md`) and round 2 (`..._delta_CT.md`). This pass completed the areas flagged as not-yet-covered: `WebUI.Blazor/Managers/{State,Recovery,Dashboard,Execution}ManagerImpl.cs`, `WebUI.Blazor/Dashboard/*`, `WebUI.Blazor/Logging/*`, and the remaining `MineflayerAdapter` JS modules (`config.js`, `movements.js`, `logger.js`, `gameModeState.js`). Emphasis this round per request: duplicated code, missing/insufficient error logging, brittle assumptions, unclear ownership, and over-coupling.

---

## New Finding 1 — `DashboardPublisherImpl` is a second, divergent, and buggier reimplementation of logic `AgentBackgroundService` already does live

**Confidence: 90% (Verified)**

This is a new, concrete instance of the dead-manager-layer problem (round 1 §3.1/A-1) — not a repeat of it, because unlike TSK-0310 (a *missing* call), this is a *full duplicate implementation* that actively diverges from and is inferior to the live one.

`AgentBackgroundService` builds and sends its own `AgentStatusUpdate` inline (`AgentBackgroundService.cs:3527-3544`) via `hubContext.Clients.Group("dashboard").SendAsync(DashboardHubEvents.SnapshotUpdated, ...)`. Completely independently, `DashboardPublisherImpl.PublishStatusAsync` builds and sends the **same event, to the same group, with the same DTO type** — and it is never called by anything except its own DI registration (`grep` for `IDashboardPublisher`/`DashboardPublisherImpl` outside its own file returns only `Program.cs` registration and doc comments in `AgentRuntime.cs`).

The two implementations have already drifted apart:

| Field | Live (ABS, line 3527) | Dead (`DashboardPublisherImpl`) |
|---|---|---|
| `QueuedActions` | `_queue.Count` (real) | hardcoded `0` — comment: `// Sprint 40+: wire from ActionQueue` |
| `Status` | 3-way: `"reconnecting"` / `"active"` / `"idle"` | 2-way: `"active"` / `"idle"`, gated on `state.AgentId is not null` — but `AgentId` is populated by `WorldStateProjector`, and the `IStateManager` backing this class is never fed events (confirmed round 1), so `AgentId` is permanently `null` and **this class can never report `"active"`, ever** |
| Error handling on publish failure | `catch (Exception ex) { logger.LogDebug(...) }` | `catch (Exception ex) { _logger.LogWarning(...) }` |

That last row is a small irony worth noting: the *dead* code has the more appropriate log severity (`LogWarning`) for a failed operator-facing SignalR push; the *live* code that actually runs logs it at `LogDebug`, which most production log configurations filter out by default — meaning a real dashboard-push failure in production is likely to go unnoticed (see Finding 2 for the broader pattern).

**Recommendation:** Delete `DashboardPublisherImpl`/`IDashboardPublisher` (and `IStateManager`/`StateManagerImpl` if nothing else consumes it) as part of TSK-0292/0293, rather than trying to "finish wiring" it — the live version has already organically evolved past it (3-way status, real queue depth) and is the one with actual operational history behind it. Also bump ABS's own dashboard-push failure log from `LogDebug` to `LogWarning` independent of the TSK-0292 decision — that's a one-line, zero-risk fix. File as `P2: Delete unused DashboardPublisherImpl/IDashboardPublisher (superseded by ABS's own live implementation)` plus a trivial `P3: Bump dashboard SignalR push failure log level from Debug to Warning`.

---

## New Finding 2 — Both file-based structured loggers (C# and JS) swallow their own I/O failures with zero visibility

**Confidence: 85% (Verified in both languages)**

**Files:** `WebUI.Blazor/Logging/FileChatLogger.cs` (`WriteEntry`), `MineflayerAdapter/logger.js` (`logStructured`)

Both of this project's file-logging helpers — one per language/process — have an empty `catch` around their write call, with **no `ILogger` injected and no fallback signal of any kind**:

```csharp
// FileChatLogger.cs
catch
{
    // Best-effort — never crash the agent loop on log I/O failure.
}
```

```javascript
// logger.js
try {
  appendFileSync(`${LOG_DIR}/adapter-${dateStr}.log`, entry + '\n');
} catch { /* best-effort — never crash the bot on log I/O failure */ }
```

"Best-effort, don't crash the agent" is the right call for the *primary* failure mode (don't let a full disk take down gameplay). But contrast this with the rest of the codebase's own established pattern — e.g. `MemorySmithBlueprintRepository`'s fallback paths still call `_logger.LogWarning(ex, ...)` before falling through. Here, there is no secondary channel at all: if `logs/chat/` or `LOG_DIR` becomes unwritable (disk full, permissions, path deleted out from under the process), **both the chat audit trail and the full structured adapter diagnostic log go silently blank, indefinitely, with literally nothing in the console, Serilog output, or dashboard to indicate it** — precisely the two logs you'd reach for first when investigating a production incident.

This is a cross-cutting, "who owns log-health visibility" gap — genuinely nobody's job today. It also compounds with Finding 1's `LogDebug` severity issue: even where a failure *is* logged, it may not surface at default log levels.

**Recommendation:**
- `FileChatLogger`: inject `ILogger<FileChatLogger>`, log the swallowed exception at `LogWarning` (once per rotation-window, or throttled, to avoid log-storming if the disk stays full) rather than fully silent.
- `logger.js`: emit a single `console.error('[logger] failed to write adapter log:', err.message)` in the catch — console output already flows to the C# host's captured stdout, so this doesn't need a new plumbing path, just one line.
- Consider a shared "N consecutive log-write failures" counter surfaced via the dashboard status payload (ties into Finding 1's cleanup) so an operator has *some* passive signal without tailing files.

File as `P2: File-based loggers (FileChatLogger, logger.js) have no failure visibility — add warning-level fallback logging`.

---

## New Finding 3 — `movements.js`'s cached `Movements` singleton likely goes stale across mid-process bot reconnects (TSK-0263)

**Confidence: 65% (Inferred — the interaction is real and verified; whether `mineflayer-pathfinder`'s `Movements` class holds a live `bot` reference internally beyond construction-time registry lookup was not independently verified against library source)**

**Files:** `MineflayerAdapter/movements.js`, `MineflayerAdapter/index.js` (`connectBot`, `bot.on('end', ...)`)

Two features were built in different sprints and, as far as this audit can tell, never cross-checked against each other:

- **Sprint 55 (TSK-0213):** `createMovements(bot)` is an explicit lazy singleton — "the first call constructs and caches one Movements instance. Subsequent calls return the cached instance... the bot argument is only used during construction... subsequent calls ignore it." All 7 call sites across `index.js` (move, mine, place, craft, smelt, wander, findFlatArea/findReachableBlock) share this one cached instance for the lifetime of the Node process.
- **Sprint 56 (TSK-0263):** "Reconnect-capable bot creation" — `connectBot()` is explicitly designed so that on `bot.on('end', ...)` (disconnect/kick), the module **re-creates the `bot` object via a fresh `mineflayer.createBot(botOpts)` call inside the same running Node process**, specifically to avoid a full process restart.

Put together: after the *first* reconnect, `bot` is a new object, but every call site still does `createMovements(bot)` and receives back the `Movements` instance built from the **original, now-disposed** bot. The file's own doc comment asserts this is safe because the bot argument is "only used during construction to access `bot.registry`" — but `mineflayer-pathfinder`'s `Movements` class is documented upstream to retain a live `bot` reference used throughout pathfinding (block/entity lookups during `updateCollisionIndex()` and path computation), not just at construction. If that's accurate, every pathfinding-dependent action (move/mine/place/craft/smelt/wander) issued after a reconnect would silently operate against a dead bot's world/entity view.

**Why this is plausible and worth checking rather than dismissing:** TSK-0263's entire purpose is "avoid restarting the process on reconnect" — this is exactly the scenario where a module-level singleton keyed off "first call wins" becomes a footgun, and it's an easy interaction to miss because Sprint 55 and Sprint 56 touch different files with no shared test.

**Recommendation:** Before trusting this is fine, either (a) add a targeted integration test that forces a `bot.on('end')` reconnect cycle and then issues a `move`/`mine` action, checking whether pathfinding succeeds and whether `Movements` was rebuilt, or (b) change `createMovements` to accept a reset hook — e.g. `resetMovements()` called from the `'end'`/reconnect-success handler so the next `createMovements(bot)` call rebuilds against the fresh bot. Given the severity if true (silent, total pathfinding breakage after every mid-session reconnect, which is precisely the failure mode TSK-0263 exists to make routine and low-drama), this is worth a same-sprint spike even at 65% confidence. File as `P1 (pending verification): Movements singleton may reference stale bot object after TSK-0263 reconnect — needs an integration test`.

---

## New Finding 4 (minor) — `FileChatLogger` uses `ReaderWriterLockSlim` for a write-only access pattern

**Confidence: 90% (Verified)**

`FileChatLogger` has exactly one code path that touches `_writer` (`WriteEntry` → `EnterWriteLock`) and no code path ever calls `EnterReadLock`. A `ReaderWriterLockSlim` (heavier, more complex, has upgradeable-lock semantics) buys nothing over a plain `lock (object)` here — there's no concurrent-read scenario this class ever serves. Not a bug, just unexplained/arbitrary complexity matching the pattern this round's request asked about. Low priority. File as `P3: Simplify FileChatLogger to a plain lock — ReaderWriterLockSlim has no read path to justify it` (or fold into whatever ticket eventually touches this file).

---

## New Finding 5 (minor, corroborating) — `RecoveryManagerImpl`'s own doc comment names a circular-dependency smell in the *live* recovery path

**Confidence: 70% (Reported by the code's own comment; not independently traced through `TryRecoverFromGameErrorAsync`'s full call graph this pass)**

Not a repeat of the dead-manager-layer finding — this is about the **live** code. `RecoveryManagerImpl.cs`'s doc comment states the Sprint 40 goal is "extract `TryRecoverFromGameErrorAsync` into this class, eliminating the circular dependency where recovery calls `SetGoal` on ABS." That's the *current, live* `AgentBackgroundService.TryRecoverFromGameErrorAsync` calling back into `AgentBackgroundService.SetGoal` on the same instance — a self-referential coupling that makes the recovery path harder to reason about and impossible to unit-test in isolation from the rest of the 3,856-line class. This wasn't independently re-traced this pass (would require reading `TryRecoverFromGameErrorAsync` and `SetGoal` end-to-end, not yet done), but it's a specific, named, code-authored admission of the "God Object" problem (round 1 A-1) with enough detail to be actionable on its own, independent of the larger TSK-0292 decomposition decision. Worth a follow-up read if a fourth pass targets `AgentBackgroundService`'s recovery/goal-setting interior in detail — not filed as a new task, just flagged as a concrete pointer into the God Object for whoever eventually does the TSK-0292 extraction.

---

## Confidence Summary (this delta only)

| Finding | Confidence | Basis |
|---|---|---|
| 1. DashboardPublisherImpl duplicate/divergent/buggier | 90% | Verified — side-by-side diff of both implementations |
| 2. Silent log-write failure in both FileChatLogger and logger.js | 85% | Verified in both files |
| 3. Movements singleton possibly stale after reconnect | 65% | Interaction verified; upstream library internals not independently confirmed |
| 4. ReaderWriterLockSlim overkill in FileChatLogger | 90% | Verified — no read path exists |
| 5. Circular dependency in live recovery path | 70% | Reported by code's own comment; call graph not independently traced this pass |

## Not yet reviewed (disclosed scope limit, third time flagging — genuinely lowest remaining priority)

`Agent.Vision` (60 lines), `Agent.Personality` (27 lines) — both trivial in size, sampled earlier, no anomalies seen. `WebUI.Blazor/Dashboard/Contracts/*.cs` (9 files) — skimmed this pass as plain DTO records, no logic to audit. Full Mineflayer `index.js` was targeted-read (mine/place/craft/smelt handlers, connect/reconnect logic) but not read top-to-bottom line-by-line; the untouched portions are largely other action handlers (chat, status, findFlatArea body) not yet given the same scrutiny as move/mine/place/craft.
