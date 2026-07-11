# MemorySmith.Agent — Deep-Dive Audit: Delta Report #6
**Scope:** Continuation of the audit series. This pass covered `AgentHub.cs`, `Dtos.cs`, `Options/SafetyOptions.cs`, `Dashboard/DashboardHubEvents.cs`, `Agent.Core/Models/ActionQueue.cs` (all full), and — following the doc-comment trail from `AgentHub.cs`/`Dtos.cs` — the actual SignalR wiring end-to-end: every `SendAsync` call site in `AgentBackgroundService.cs` cross-referenced against the one and only client listener file, `WebUI.Blazor/wwwroot/index.html`.
**Format:** Deltas only.

---

## Summary

| # | Type | Item | Severity | Confidence |
|---|------|------|----------|------------|
| 1 | Correction | `TSK-0329` ("Fix SignalR event name drift") is marked Done but was only partially applied — the stale JS listener it says to remove is still present, and 2 of the 3 "centralized" event-name constants it exists to enforce are themselves unused/incorrect | Medium | 93% |
| 2 | New, minor | `ActionQueue.cs` reviewed in full — no new issues found; noted here only to confirm coverage | — | — |

Also explicitly re-verified: `AgentQueue`'s locking, `ClearAndEnqueueAsync`'s stop-before-clear ordering, and its documented rationale for swallowing the stop-callback exception (no `ILogger` available at that layer, by design) are all sound — no correction needed.

---

## 1 — `TSK-0329` incompletely applied: stale JS listener still present, and `DashboardHubEvents` constants don't match 2 of 3 real event names (Medium, 93%)

`TSK-0329` ("P2: Fix SignalR event name drift," `Done`) describes a three-way mismatch (`DashboardHubEvents.SnapshotUpdated` constant vs. a raw `"StatusUpdated"` string in the then-live `AgentBackgroundService` path vs. the JS client's Sprint-5A dual-listener fallback) and states the fix as: *"Migrate ABS to use the canonical `DashboardHubEvents.SnapshotUpdated` constant and remove the JS fallback for `StatusUpdated`."*

**What was actually done, verified against current code:**
- ✅ `AgentBackgroundService.PushStatusToDashboardAsync` now uses `DashboardHubEvents.SnapshotUpdated` (`AgentBackgroundService.cs:3544`) — the ABS-side migration is real and correct.
- ❌ The JS fallback was **not** removed. `WebUI.Blazor/wwwroot/index.html:514` still contains `signalRConn.on('StatusUpdated', update => {...})` immediately alongside the canonical `signalRConn.on('SnapshotUpdated', ...)` listener at line 518, with the original Sprint-5A migration comment (line 513) still in place verbatim. The task's own stated acceptance condition wasn't met even though it was marked `Done`.

**A second, closely-related gap the task didn't cover (and doesn't appear to be tracked anywhere else):** `DashboardHubEvents` defines three constants specifically so that "all dashboard SignalR events... use these strings to prevent drift between the C# hub and the JavaScript client" (its own class doc). In practice, only one of the three is actually used on the send side:

| Constant | Value | Actually sent by live code? |
|---|---|---|
| `SnapshotUpdated` | `"SnapshotUpdated"` | ✅ Yes — `PushStatusToDashboardAsync` uses the constant |
| `GoalUpdated` | `"GoalUpdated"` | ❌ No — `PushGoalToDashboardAsync` (`AgentBackgroundService.cs:3571`) sends the raw literal `"GoalUpdate"` (no trailing "d") |
| `ChatReceived` | `"ChatReceived"` | ❌ No — `PushChatToDashboardAsync` (`AgentBackgroundService.cs:3558`) sends the raw literal `"ChatMessage"` |

The JS client happens to match the *actual* (non-constant) names correctly today — `index.html` listens for `'ChatMessage'` and `'GoalUpdate'`, which is what the server really sends — so there is **no live functional break** here. But `GoalUpdated`/`ChatReceived` are dead, misleadingly-named constants that don't describe reality, and the real event names remain unprotected magic strings duplicated by hand between `AgentBackgroundService.cs` and `index.html` — precisely the setup that already caused the `StatusUpdated`/`SnapshotUpdated` drift `TSK-0329` was filed to fix. If either side is edited without the other (e.g., someone "helpfully" migrates `PushGoalToDashboardAsync` to use the `GoalUpdated` constant, reasonably assuming that's what it's for), it reintroduces the exact same class of bug in a new spot.

**Supporting evidence this is a real, easy-to-fall-into trap, not a hypothetical:** the doc comments on `AgentHub.cs` (class-level: *"receive StatusUpdated, ChatMessage, and GoalUpdate events"*) and `Dtos.cs` (`AgentStatusUpdate`: *"Sent via ... `SendAsync("StatusUpdated", ...)`"*) both still describe the **pre-migration** names — `AgentHub.cs`'s comment is accidentally half-right (correctly names the real `ChatMessage`/`GoalUpdate` wire strings, for the wrong reason — it was never updated — while being wrong about `StatusUpdated`/`SnapshotUpdated`), and `Dtos.cs`'s comment is simply stale. Anyone using these comments (rather than grepping live `SendAsync` call sites, as this audit did) to understand the wire protocol would get a confusing, partially-wrong picture.

**Recommendation:**
1. Remove the stale `signalRConn.on('StatusUpdated', ...)` listener from `index.html` (lines 513-517) — this is TSK-0329's own already-stated, still-unmet acceptance criterion; reopen or file a fast-follow rather than leaving it marked `Done`.
2. Migrate `PushGoalToDashboardAsync` and `PushChatToDashboardAsync` to use `DashboardHubEvents.GoalUpdated`/`.ChatReceived`, **and** update the corresponding `index.html` listeners to match — both sides in the same commit, exactly as `TSK-0329` did correctly for the snapshot case. This is the one remaining piece of the "centralize event names" work the `DashboardHubEvents` class was created to complete.
3. Update the stale doc comments in `AgentHub.cs` and `Dtos.cs` to reference the (post-fix) canonical constant names, not raw strings.
4. Suggest a lightweight guard against this recurring: a small integration/smoke test that asserts every string literal passed to `hubContext.Clients.Group("dashboard").SendAsync(...)` matches one of `DashboardHubEvents`'s constant values — cheap to write, directly prevents this exact class of drift from recurring a third time.

---

## Files reviewed with no new findings this pass

- `Agent.Core/Models/ActionQueue.cs` (152 lines, full read) — locking discipline, atomicity guarantees, and the documented stop-callback exception-swallowing rationale all check out; no correction to any existing task needed.
- `WebUI.Blazor/Options/SafetyOptions.cs` (43 lines, full read) — consistent with the already-tracked `TSK-0318`/`TSK-0319` denylist-normalization fix history; nothing new.

---

## Assumptions & Open Questions (this pass)

1. Confirmed via direct `find`/`grep` that this repository snapshot contains **no `.razor` files anywhere** despite the project being named `WebUI.Blazor` and despite numerous `Data/Tasks/*.json` entries describing Blazor-component-level UI work (e.g. `TSK-0257` "entity card rendering," `TSK-0259` "right panel width," `TSK-0170` "dashboard UI improvements"). The actual dashboard front-end is a single static `wwwroot/index.html` with inline `<script>`, consumed via a generic ASP.NET Core Web SDK project (not a Blazor Server/WASM SDK). This is very likely just this branch/tarball's snapshot boundary (the UI-component source may live in a different repo, branch, or was intentionally simplified to static HTML at some point) rather than a bug — flagging only because it affects how much confidence to place in any *future* audit finding that assumes Razor component files exist to review; there's nothing to review there in this snapshot, and any such task should be treated as out-of-scope for this repo rather than "missing work."
2. Did not verify whether `about.html` (the other `wwwroot` file, referenced by `Scripts/Verify-AboutDeps.ps1` per the `.csproj`'s POLICY P-2 comment) is itself in sync with current `PackageReference`s — out of scope for this pass, flagged only as a quick sanity check worth doing given the project's explicit self-imposed policy on this exact topic.
