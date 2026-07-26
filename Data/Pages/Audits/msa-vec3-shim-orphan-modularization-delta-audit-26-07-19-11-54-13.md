# MemorySmith.Agent — Delta Audit #5: Reinvented-Wheel Shim, Orphaned Modularization, Live Reconnect Bug

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`) — same commit as prior four reports.
**This is a delta report.** Contains only findings not already in the four prior reports or in `Data/Tasks/*.json`.
**This pass covered:** the remainder of `Agent.World.Minecraft` (`MinecraftAdapter.cs`, `MinecraftAdapterConfig.cs`), and the smaller `MineflayerAdapter/*.js` helper modules not yet individually read (`config.js`, `movements.js`, `stopState.js`, `gameModeState.js`, `vec3.js`) — the last unaudited corner of the repo identified at the end of Delta #4.

---

## Executive Summary

| # | Finding | Class | Confidence | Impact |
|---|---|---|---|---|
| H1 | **`MineflayerAdapter/vec3.js` is a 231-line, 46-method hand-rolled reimplementation of the `vec3` npm package — which is already present in the installed dependency tree** (`node_modules/vec3@0.1.10`, pulled in transitively by `mineflayer`/`prismarine-world`). The shim's own doc comment documents a real production bug this reimplementation caused (Sprint 56 / TSK-0262: `.floored()` returning `this` instead of a new object, "corrupting aim points") — a bug class that structurally cannot occur if the real, upstream-maintained library is used directly. 25 call sites in `index.js`. | Not-Invented-Here / legacy-shim that should be removed | 93% | Medium-High (both a bug-recurrence risk and meaningful LOC/maintenance-burden removal) |
| H2 | **`stopState.js` is a fully orphaned module — never imported anywhere.** Its header claims it was "Extracted from index.js Sprint 52 modularization (TSK-0166)," but `index.js` never imports it; instead `index.js` maintains its own duplicate, independent `_stopRequested` variable and inline `handleStop()` function doing the same job. This means TSK-0166's modularization effort was **partially executed and then abandoned mid-migration**: `movements.js`, `gameModeState.js`, and `config.js` (the other 3 "Sprint 52" extractions) *are* correctly imported and used; `stopState.js` alone was extracted and never wired in. | Dead code / incomplete migration (JS-side analog of Report 1's `AgentRuntime` finding) | 95% | Medium (confuses anyone touching stop-handling logic — two implementations exist, only one is live) |
| H3 | **Confirmed still-live: `createMovements()`'s module-level singleton is never invalidated on bot reconnect.** `connectBot()` assigns a genuinely new `bot` object to the module-level `bot` variable on every (re)connect (confirmed: `bot = mineflayer.createBot(botOpts)`), but `movements.js`'s cached `_instance` is only ever constructed once, on the *first* call — its own doc comment states outright: *"the bot argument is only used during construction... subsequent calls ignore it."* After any reconnect (network blip, kick, disconnect), all 7 pathfinding call sites continue receiving a `Movements` instance bound to the stale, disconnected bot. Not tracked by any `TSK-###` despite the underlying risk being structurally identical to already-fixed reconnect-related bugs in this same file (`WebSocketBridge` reconnect, `AgentBackgroundService` reconnect delays). | Stale-state / unwired invalidation on reconnect | 88% | Medium-High (silent pathfinding corruption after any disconnect — one of the more operationally-relevant bugs found in this audit series, since disconnects are a normal, expected event for this system, not an edge case) |
| H4 | **`MinecraftAdapterConfig` has two independently-settable, overlapping connection-target fields**: `WebSocketUrl` (default `"ws://localhost:3000"`, used by `MinecraftAdapter.ConnectAsync` to construct `WebSocketBridge`) and `WebSocketPort` (default `3000`, used separately by `StartNodeProcessAsync`'s `WS_PORT` env var and by `WaitForPortAsync`'s TCP probe). Changing only `WebSocketPort` in configuration produces a working Node-side listener on the new port, a successful `WaitForPortAsync` probe (which correctly checks the new port) — followed by a `WebSocketBridge` connection failure, because the bridge still connects to the old port baked into the unchanged `WebSocketUrl` string. No validation ties the two together. | Data Clump / brittle implicit contract | 85% | Medium (a real, if infrequent, misconfiguration trap — "I changed the port and it broke" with a non-obvious cause) |
| H5 | **TSK-0312 ("Fix Debug.WriteLine silent exception swallowing in Release builds," marked Done) did not cover all instances of the pattern it targeted.** Its scope was limited to `ActionQueue.cs` and `HtnPlanner.ParseLlmActions`. `Agent.World.Minecraft/MinecraftAdapter.cs` has two more `Debug.WriteLine` calls (lines 143, 148, forwarding the Node subprocess's stdout/stderr) that silently no-op in Release builds — the same underlying mechanism TSK-0312 was created to eliminate, just in a location outside its stated scope. | Incomplete fix / recurring pattern outside original scope | 87% | Low-Medium (explicitly documented as a "secondary diagnostics channel," so lower severity than TSK-0312's original targets, but still a genuine gap) |
| H6 | **Speculative/lower-confidence**: `config.js`'s `BLOCK_MINING_ALIASES` map has exactly one populated entry (`dirt: ['dirt', 'grass_block']`) despite the mining pipeline handling many block families with meaningful variants (wood log types, ore types). This mechanism looks like it was designed to solve the same "accept any equivalent variant" problem that `GatherItemDecompose`/`MineWoodDecompose` (TSK-0397/TSK-0398, still Backlog) instead work around by emitting one `MineBlock` action per variant on the C# side. Possibly an intentionally minimal, single-purpose fix (Sprint 40 P0-C) that was never revisited to cover the broader case — flagged as a design question for the team rather than an asserted bug. | Possible missed opportunity / incomplete design | 55% | Unknown — needs team input |

**Standout finding this round: H1.** Across five audit passes, this is the single clearest "delete the legacy system, use the real dependency" opportunity found — a maintained, versioned, already-present library sitting one `import` away from replacing 231 lines of hand-maintained shim code that has already caused at least one documented production bug from drifting out of sync with the real library's contract.

---

## Detailed Findings

### H1 — Hand-rolled Vec3 shim duplicates an already-installed npm package

**Evidence.**
```
$ python3 -c "import json; d=json.load(open('MineflayerAdapter/package-lock.json')); print([k for k in d['packages'] if k.endswith('vec3')])"
['node_modules/vec3']   # version 0.1.10, PrismarineJS's official vec3 package
```
`MineflayerAdapter/vec3.js`'s own header:
> *"Mineflayer 4.x internally relies on prismarine-vector's Vec3 class... This module exports a single function `toVec3(x, y, z)` that creates a plain JS object implementing the FULL prismarine-vector Vec3 API surface... Sprint 41: Moved from inline function in index.js to standalone module with the complete Vec3 API (46 methods total)."*

And, critically, the same file's changelog comment:
> *"Sprint 56 (TSK-0262): Fixed `.floored()` to return a NEW object instead of `this`, and stopped flooring in the constructor. This matches the real prismarine-vector Vec3 contract. Previously, `.floored()` returning `this` caused prismarine-world's `block.position = pos.floored()` to leak the shim into Mineflayer's internal geometry calculations, corrupting aim points."*

This is direct, first-party evidence that the reimplementation strategy has already produced a real bug (corrupted aim/dig/place geometry) from subtly deviating from the real library's semantics — precisely the risk class that hand-maintaining a "compatible" reimplementation of a well-specified upstream API always carries, and precisely the risk that disappears if the real library is used. `node_modules/vec3/index.js` confirms the real package exports a `class Vec3 { constructor(x, y, z) {...} }` — a directly compatible drop-in for the 25 `toVec3(x, y, z)` call sites in `index.js` (`import { Vec3 } from 'vec3'; const toVec3 = (x, y, z) => new Vec3(x, y, z);` would be a minimal-diff migration, or call sites could be updated to `new Vec3(...)` directly for a cleaner result).

**Why this happened (inferred, not certain):** `vec3` isn't a *direct* dependency in `package.json` today — it's only present because `mineflayer`/`prismarine-*` pull it in transitively. It's plausible the shim was written before this was noticed, or to avoid a direct dependency on a transitive package (a reasonable instinct in general, but one that backfired here since the hand-rolled copy still has to track the real API surface exactly, and did drift once already).

**Recommendation.**
1. Add `"vec3"` as an explicit direct dependency in `package.json` (pinning the version already resolved, `^0.1.10`, or checking for a newer stable release) rather than relying on it being present only transitively — this de-risks the removal (if a future `mineflayer` upgrade changes its dependency tree and stops pulling `vec3` in, the shim's replacement would silently break without a direct pin).
2. Replace `vec3.js`'s `toVec3` export with a thin wrapper around the real `Vec3` class (or update call sites directly) and delete the 231-line hand-rolled implementation.
3. Run the existing test suite (there is at least `test/gameModeState.test.js`; check for any Vec3-specific coverage first) plus a manual smoke test of dig/place/pathfind actions before merging, given this touches geometry used throughout the adapter's hot paths.
4. This is a good candidate to tackle *before* or *alongside* TSK-0166 (the broader `index.js` modularization) rather than after — it reduces the surface area that modularization work would otherwise need to carry forward.

**Confidence: 93%.** The "package is already present" and "the shim already caused a bug from drift" facts are both directly documented/verified. The recommendation's low-risk framing is slightly tempered by not having run the test suite in this sandbox (no `npm install` of dev-dependencies/test runner was executed this pass) — see Open Questions.

---

### H2 — `stopState.js`: an orphaned module from an incomplete Sprint-52 modularization

**Evidence.**
```
$ grep -n "createStopState" MineflayerAdapter/index.js
(no results)
$ grep -n "^import" MineflayerAdapter/index.js
...
import { emitGameModeEvent, normalizeGameMode } from './gameModeState.js';   // used
import { toVec3 } from './vec3.js';                                          // used
import * as C from './config.js';                                            // used
import { createMovements } from './movements.js';                            // used
(no import of stopState.js)
```
`index.js` instead defines its own module-level `let _stopRequested = false;` (line 76) and its own `function handleStop() { ... }` (line 96) — functionally covering the same responsibility `stopState.js`'s `createStopState()` factory was written to own (`handleStop`, `clearStop`, `isStopRequested`, all bound to a `bot` closure). The two implementations aren't merely similar — they solve the identical problem (emergency-stop flag + pathfinder cancellation) via genuinely different code, and only one of them executes.

This is worth flagging distinctly from Report 1/2's `TSK-0166` framing ("modularize the monolith," treated as *entirely future* work): modularization work under that banner has already partially happened (3 of 4 extracted files are live), and cleaning up the abandoned 4th extraction is a much smaller, more contained task than the full monolith-splitting effort TSK-0166 currently describes.

**Recommendation.** Two options, either resolves it:
1. **Delete `stopState.js`** if the inline `index.js` implementation is considered final (simplest, since the inline version is what's actually been battle-tested in production).
2. **Or actually wire it in** — replace `index.js`'s inline `_stopRequested`/`handleStop` with `const stop = createStopState(bot, sendEvent);` and update the ~10+ inline checks (`if (_stopRequested)`) to `if (stop.isStopRequested())`. This is the "real" completion of what Sprint 52 started, and would give TSK-0166 a working template for how the rest of the monolith's extractions should be wired in (since this module already has the right shape — it just needs the switchover step).

Either way, recommend adding a one-line note to TSK-0166's record pointing at this finding, so whoever picks up the broader modularization task starts with an accurate picture of what's already been attempted.

**Confidence: 95%.** Exhaustive-grep-confirmed zero-import, plus direct comparison of the two duplicate implementations.

---

### H3 — `createMovements()` singleton never invalidated on bot reconnect (confirmed still live)

**Evidence.**
```javascript
// movements.js
let _instance = null;
export function createMovements(bot) {
  if (!_instance) {
    const m = new Movements(bot);
    m.canOpenDoors = true;
    _instance = m;
  }
  return _instance;   // <-- bot param silently ignored on every call after the first
}
```
```javascript
// index.js
function connectBot() {
  _reconnectAttempts = 0;
  bot = mineflayer.createBot(botOpts);   // <-- genuinely new object, confirmed via reassignment of module-level `bot`
  bot.loadPlugin(pathfinder);
  registerBotEventHandlers();
  return bot;
}
```
`bot.on('end', ...)` (line 419) schedules a reconnect via `_reconnectTimer`, which calls `connectBot()` again — producing a new `bot` object every time. All 7 call sites of `createMovements(bot)` in `index.js` (lines 665, 698, 1268, 1483, 1862, 1897, 1957) pass the *current* `bot` reference at call time, but per the factory's own documented behavior, only the very first call's `bot` is ever actually used to construct the `Movements` instance — every subsequent call, including all calls made after a reconnect, silently receives the stale instance built against the original (now possibly-disconnected) bot object.

**Operational relevance:** disconnects are not a rare edge case for this system — network blips, server restarts, and kicks are treated as first-class, expected events (there's dedicated reconnect-with-backoff logic on both the JS and C# sides, per Reports 1/2's coverage of `_reconnectAttempts`/`DefaultReconnectDelays`). That means this stale-singleton bug is positioned to fire on a routine, not exceptional, operational path — any session that survives past its first disconnect+reconnect cycle is, from this point on, running all pathfinding against a `Movements` object wired to a dead bot reference.

**Recommendation.** Add a `resetMovements()` export to `movements.js` (`export function resetMovements() { _instance = null; }`), and call it from `connectBot()` before returning the new `bot`, or from the `bot.on('end', ...)` handler. This is a small, targeted, low-risk fix.

**Confidence: 88%.** The stale-reference mechanism is directly confirmed by reading both files. The severity claim ("this actually causes pathfinding problems, not just a theoretical staleness") is a reasoned inference from how `mineflayer-pathfinder`'s `Movements` class is documented to use `bot` internally (for `bot.registry`, block/item lookups, and per the module's own comment, "the pathfinder calls `updateCollisionIndex()` internally before each path computation" — which operates on `this.bot`), rather than something exercised end-to-end in this sandbox (no live Minecraft server available — see Open Questions).

---

### H4 — `MinecraftAdapterConfig`'s `WebSocketUrl`/`WebSocketPort` split is a brittle implicit contract

**Evidence** (`Agent.World.Minecraft/MinecraftAdapterConfig.cs`):
```csharp
public string WebSocketUrl { get; init; } = "ws://localhost:3000";
public int WebSocketPort { get; init; } = 3000;
```
Consumers, in `MinecraftAdapter.cs`:
- `ConnectAsync`: `_bridge = new WebSocketBridge(config.WebSocketUrl); await _bridge.ConnectAsync(...)` — uses **only** `WebSocketUrl`.
- `StartNodeProcessAsync`: `psi.EnvironmentVariables["WS_PORT"] = config.WebSocketPort.ToString();` — uses **only** `WebSocketPort`.
- `WaitForPortAsync(config.WebSocketPort, ...)` — uses **only** `WebSocketPort`.

No constructor validation, no derivation of one from the other, nothing enforcing they describe the same endpoint. The defaults happen to agree (`3000` in both) purely because someone kept them in sync by hand when the record was written. Anyone reconfiguring the port for a non-default deployment (e.g., running multiple agents on one host, each needing a distinct port) has to remember to update both fields — and the failure mode if they don't (Node starts fine, port-wait succeeds, WebSocket connect fails against the stale port) doesn't obviously point back to "you forgot to update `WebSocketUrl`."

**Recommendation.** Collapse to a single source of truth — either:
- Keep `WebSocketPort` as the configured value and compute `WebSocketUrl` from it (`public string WebSocketUrl => $"ws://localhost:{WebSocketPort}";`, removing the separate settable property), or
- Keep `WebSocketUrl` as the configured value and parse the port out of it where needed (`new Uri(WebSocketUrl).Port`), removing the separate `WebSocketPort` field.
Either direction removes the manual-sync requirement entirely. The first is probably simpler given `WebSocketPort` is also independently useful as a bare int for the `WS_PORT` env var and the TCP probe.

**Confidence: 85%.** The two-independent-fields structure and its consumers are directly confirmed by reading the code; "this will actually bite someone" is a reasonable but not certain severity claim (default single-agent deployments would never hit it).

---

### H5 — TSK-0312's `Debug.WriteLine` fix left two instances uncovered

**Evidence.** TSK-0312 (`Done`) explicitly scoped itself to `ActionQueue.cs` and `HtnPlanner.ParseLlmActions`. Confirmed remaining instances of the same pattern:
```csharp
// Agent.World.Minecraft/MinecraftAdapter.cs:143, 148
_nodeProcess.OutputDataReceived += (_, args) => {
    if (!string.IsNullOrEmpty(args.Data))
        System.Diagnostics.Debug.WriteLine($"[adapter:stdout] {args.Data}");
};
_nodeProcess.ErrorDataReceived += (_, args) => {
    if (!string.IsNullOrEmpty(args.Data))
        System.Diagnostics.Debug.WriteLine($"[adapter:stderr] {args.Data}");
};
```
The surrounding comment already acknowledges this is a "secondary diagnostics channel" (the adapter's own `logger.cjs` file-based logging is primary) — which meaningfully lowers this finding's severity relative to TSK-0312's original targets (which were exception-swallowing in control-flow-critical paths). But it's still the exact same mechanism (`Debug.WriteLine` compiles to a no-op outside `DEBUG` builds) applied to data that could matter during an incident — e.g., a Node process crash or startup failure that occurs *before* the file logger is initialized would only be visible via this stdout/stderr forwarding, and in a Release deployment, it's silently discarded.

**Recommendation.** Replace both calls with `ILogger`-based logging at `Trace` or `Debug` level (would need `AgentBackgroundService` or `MinecraftAdapter` to accept an `ILogger` — check whether `MinecraftAdapter` currently has one available via DI; if not, threading one in is a small constructor change). Low priority relative to H1–H3 above, but a natural "while you're in the area" fix if `MinecraftAdapter.cs` is touched for any other reason, and worth linking to TSK-0312 so the pattern's full footprint is documented in one place.

**Confidence: 87%.**

---

### H6 — Speculative: `BLOCK_MINING_ALIASES` may be an intentionally-narrow fix that never got generalized

**Evidence.** `config.js`:
```javascript
export const BLOCK_MINING_ALIASES = Object.freeze({
  dirt: ['dirt', 'grass_block'],
});
```
One entry, dated to "Sprint 40 P0-C" per the surrounding comment — a specific, narrow fix (per the comment: "also accept blocks that drop the same item when mined"). Meanwhile, `GatherItemDecompose`/`MineWoodDecompose` (TSK-0397/TSK-0398, both `Backlog`) describe the C# side working around a conceptually similar problem — "an item has multiple source-block variants" — via a different, less efficient mechanism (emitting one full-count `MineBlock` action per variant, rather than accepting any variant as satisfying a single mining goal). It's plausible these are two independent, non-conflicting mechanisms solving different sub-problems (this one is about mining a *specific requested block* leniently; the C# one is about *gathering an item* that has multiple source blocks) — in which case there's no real connection and this note can be disregarded. But it's also plausible that generalizing `BLOCK_MINING_ALIASES` to cover wood-log/ore-family variants would let TSK-0397/TSK-0398 be solved more simply (accept-any-variant at the JS layer, rather than count-tracking across variants at the C# layer) — worth a five-minute conversation with whoever scopes TSK-0397/TSK-0398 next, not a concrete recommendation on its own.

**Confidence: 55%.** This is explicitly a connect-the-dots hypothesis across two different files/languages, not a directly observed bug — flagged for team judgment rather than as an actionable finding on its own.

---

## Assumptions & Open Questions

1. **H1's migration risk wasn't validated by running the adapter.** No live Minecraft server was available in this sandbox to smoke-test dig/place/pathfind behavior after a hypothetical `vec3.js` → real-`vec3` swap. The existing single JS test file (`test/gameModeState.test.js`) doesn't cover Vec3 behavior at all — recommend adding basic unit coverage for whichever replacement approach is chosen, given geometry bugs here are exactly the kind that surface as "the bot digs the wrong block" rather than a crash.
2. **H3's severity is inferred from `mineflayer-pathfinder`'s documented internals, not observed at runtime** in this pass — flagged as the most operationally important finding in this report on that reasoning, but the team is better positioned than this audit to confirm via logs/incident history whether stale-Movements-after-reconnect symptoms have actually been observed in practice (which would raise confidence further) or whether something else already incidentally mitigates it (e.g., if the process is typically restarted rather than truly reconnecting in the deployment pattern actually used — worth checking `AutoStartNode`/deployment conventions).
3. **H4's real-world likelihood** depends on deployment patterns this audit has no visibility into (single-agent-per-host vs. multi-agent) — flagged as a robustness improvement regardless, but its priority should be weighted by the team's actual deployment topology.

---

## Suggested Sequencing (additive to prior reports)

1. **H3** (reset Movements on reconnect) — small, targeted, addresses a bug on a routine operational path; do this first given the operational-relevance argument in the finding.
2. **H2** (resolve the `stopState.js` orphan — delete or wire in) — small, contained, removes a confusing duplicate-implementation trap before anyone touches stop-handling logic for an unrelated reason.
3. **H1** (replace the Vec3 shim) — larger and touches hot-path geometry code, so sequence after H2/H3 land and stabilize, and pair with adding basic Vec3 test coverage.
4. **H5** (Debug.WriteLine → ILogger in MinecraftAdapter.cs) — trivial, bundle with any other small fix touching that file.
5. **H4** (collapse WebSocketUrl/WebSocketPort) — independent, low urgency unless multi-agent-per-host deployment is actually planned.
6. **H6** — not an action item; raise as a question when TSK-0397/TSK-0398 are next scoped.
