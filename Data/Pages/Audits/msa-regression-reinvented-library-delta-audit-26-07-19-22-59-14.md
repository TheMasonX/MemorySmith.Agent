# MemorySmith.Agent — Delta Audit #5: Regressed Anti-Pattern, Reinvented Library, Reconnect Gap

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`) — same commit as all prior reports.
**This is a delta report.** Contains only findings not already in the four prior reports or in `Data/Tasks/*.json`.
**This pass covered:** `Agent.World.Minecraft/MinecraftAdapter.cs` (previously unaudited), the remaining `MineflayerAdapter/*.js` helper modules (`vec3.js`, `movements.js`, `config.js`, `stopState.js`, `gameModeState.js` — `index.js` and `WebSocketBridge.cs` were already covered in earlier reports), and `WebUI.Blazor/Options/SafetyOptions.cs` including its enforcement path in `AgentBackgroundService.cs`.

---

## Executive Summary

| # | Finding | Class | Confidence | Impact |
|---|---|---|---|---|
| H1 | **A previously-fixed, team-known anti-pattern regressed 10 days after being closed.** TSK-0312 ("Fix Debug.WriteLine silent exception swallowing in Release builds," `Done` 2026-07-01) fixed exactly this issue in `ActionQueue.cs`/`HtnPlanner.cs`. On 2026-07-11 — in the commit literally titled *"Implement top 5 highest-ROI fixes from Sprint 60 audit"* — two new `System.Diagnostics.Debug.WriteLine(...)` calls were added in `MinecraftAdapter.cs` to pipe the Node subprocess's stdout/stderr. `Debug.WriteLine` is a no-op in Release builds (compiled out via `[Conditional("DEBUG")]`), so in any Release deployment this "diagnostics channel" is silently dead — reintroducing the identical failure mode the team had just spent a task fixing elsewhere, in the same "fix things" commit that was supposed to be reducing risk. | Silently swallowed error / **regression of a fixed defect class** | 95% | Medium-High — directly undermines visibility into the one subprocess most likely to fail unpredictably (a live Mineflayer bot) |
| H2 | **`MineflayerAdapter/vec3.js` is a 231-line, hand-rolled reimplementation of the `vec3` npm package** (46 methods, matching the real `prismarine-vector`/`vec3` API) — despite `vec3@0.1.10` already being present in the project's own `node_modules`/`package-lock.json` as a transitive dependency of `mineflayer`. The team has already run one dedicated bug-fix cycle (TSK-0262, `Done`) against a correctness bug in this shim's `.floored()` method — a bug class that plausibly cannot occur in the real, upstream-maintained implementation the shim is trying to imitate. | Duplicated Code (reimplemented library) / unnecessary maintenance burden | 93% | Medium — mechanical, high-value fix once decided on; risk is in the migration, not in leaving it |
| H3 | **The `Movements` pathfinder singleton is still never rebound after a bot reconnect**, confirmed present and unfixed at HEAD, and — unlike the AgentRuntime pattern in Report #1 — **no task has ever been filed for it**. `movements.js`'s own doc comment states plainly: *"the bot argument is only used during construction... subsequent calls ignore it."* Given the Movements factory (TSK-0213, Sprint 55) and the auto-reconnect feature (TSK-0263, Sprint 56) shipped one sprint apart, this reads as a genuine coverage gap between two otherwise-correct, independently-completed features rather than a deliberate simplification. | Unwired/stale state across a documented reconnect boundary | 85% | Medium (severity depends on whether reconnects in practice ever hit a different Minecraft version/registry — see Open Questions) |
| H4 | **TSK-0166 ("Modularize MineflayerAdapter," `Backlog`) understates work already done.** `stopState.js`, `gameModeState.js`, `movements.js`, and `config.js` have already been extracted from `index.js` across Sprints 37–55 (per their own header comments, several explicitly citing TSK-0166 as their origin) — the monolith has already shrunk from whatever it once was to today's 2,189 lines. The task's description doesn't reflect this partial progress, which risks someone re-scoping it as a from-scratch effort. | Task-tracker accuracy | 80% | Low |
| H5 | **Positive finding, checked for balance**: `SafetyOptions.DeniedCommands`/`AllowDestructiveCommands` enforcement in `AgentBackgroundService`'s `"command"` intent handler was read in full and is correctly implemented — command-string parsing strips arguments before matching (`/op steve` correctly matches a denied `/op` entry), the configured deny list is merged with, never replaces, the built-in safety floor, and intents without a leading `/` are explicitly rejected and logged rather than silently passed through. No bug found. | — (verification, not a defect) | 95% | — |
| H6 | **Minor: `BLOCK_MINING_ALIASES` in `config.js` has exactly one entry** (`dirt: ['dirt', 'grass_block']`), despite being structured as a general-purpose aliasing table (per its own comment: "when asked to mine a block, also accept blocks that drop the same item"). If the intent is a general mechanism, it's currently only exercised for one case; worth a decision on whether to broaden it (e.g. ore variants, log rotations) or rename/scope it explicitly as a dirt-specific special case so a future reader doesn't assume broader coverage exists. | Nit / narrow-scope naming | 75% | Trivial |

**Framing note for H1 and H2 together:** both findings share a root cause worth naming explicitly — in a codebase already carrying meaningful "reimplement rather than reuse" and "reintroduce a fixed pattern" risk (per Reports #1, #2, #4's G1/G4), the fixes that would most durably prevent recurrence are process-level (a lint rule banning `Debug.WriteLine` in non-test projects; a `package.json` `dependencies` entry + import for `vec3` instead of a local shim) rather than one-off code fixes — the code fixes alone don't stop a third occurrence.

---

## Detailed Findings

### H1 — `Debug.WriteLine` anti-pattern regression in `MinecraftAdapter.cs`, 10 days after being fixed elsewhere

**Evidence.**
```
$ python3 -c "TSK-0312 completedAtUtc"
2026-07-01T13:38:02Z — "Fix Debug.WriteLine silent exception swallowing in Release builds"
   (fixed: ActionQueue.cs catch block, HtnPlanner.ParseLlmActions)

$ git log -L 140,150:Agent.World.Minecraft/MinecraftAdapter.cs
2026-07-11 02:05:57 -0500  "Implement top 5 highest-ROI fixes from Sprint 60 audit"
+ _nodeProcess.OutputDataReceived += (_, args) => {
+     if (!string.IsNullOrEmpty(args.Data))
+         System.Diagnostics.Debug.WriteLine($"[adapter:stdout] {args.Data}");
+ };
+ _nodeProcess.ErrorDataReceived += (_, args) => {
+     if (!string.IsNullOrEmpty(args.Data))
+         System.Diagnostics.Debug.WriteLine($"[adapter:stderr] {args.Data}");
+ };
```
`GoalFactory.cs`'s own Sprint-32 comment documents the team's own prior conclusion on this exact API: *"Replaced System.Diagnostics.Debug.WriteLine with structured ILogger warnings"* — i.e., this isn't a subtle .NET gotcha the team hasn't encountered; it's a specifically-named, specifically-fixed anti-pattern (TSK-0312) that reappeared in a different file 10 days later, in a commit whose own title claims to be implementing "highest-ROI fixes." The surrounding code in this exact hunk is the TSK-0389 pipe-drain fix (preventing the Node subprocess from hanging due to a full stdout/stderr OS pipe buffer) — that functional fix is correct and unaffected by this issue (draining the pipe via `BeginOutputReadLine()` works regardless of what the event handler does with the data). The bug is narrower and specifically about **visibility**: the comment directly above these lines calls this a *"secondary diagnostics channel"* for when the adapter's structured JS-side logger doesn't capture something — but in a Release build, this channel is unconditionally empty, silently, with nothing to indicate that's happening.

**Why this is worse than a first occurrence:** the team has already paid the cost of learning this lesson once (TSK-0312) and explicitly writing it down (the `GoalFactory.cs` comment). A second, independent occurrence 10 days later in unrelated code suggests the lesson is documented but not enforced — nothing catches a new `Debug.WriteLine` call in production code before it ships.

**Recommendation.**
1. Immediate code fix: replace both `Debug.WriteLine` calls with `logger.LogDebug(...)` (a logger is already accessible in this class's usage context via DI, or can be threaded through if `MinecraftAdapter` doesn't currently take one — check current constructor).
2. Process fix (addresses the *recurrence*, not just this instance): add a Roslyn analyzer rule, an `.editorconfig` banned-API entry (`BannedSymbols.txt` via the `Microsoft.CodeAnalysis.BannedApiAnalyzers` package, which is free and mechanical to add), or at minimum a pre-commit grep check, that flags any new `System.Diagnostics.Debug.WriteLine` call in a non-test project. This is the single most mechanically-preventable finding across this entire audit series — the fix pattern is a one-line rule, not a design discussion.

**Confidence: 95%.** Both the regression timeline and the Release-mode no-op behavior of `Debug.WriteLine` are objective, well-documented .NET facts, not inferences.

---

### H2 — `vec3.js` reimplements a library already in the dependency tree

**Evidence.**
```
$ python3 -c "package-lock.json packages['node_modules/vec3']['version']"
0.1.10
```
`vec3.js`'s own header comment: *"This module exports a single function `toVec3(x, y, z)` that creates a plain JS object implementing the FULL prismarine-vector Vec3 API surface... Vec3 API methods verified against prismarine-vector 2.x... Sprint 41: Moved from inline function in index.js to standalone module with the complete Vec3 API (46 methods total)."* `vec3` **is** the npm package `prismarine-vector` is published as — this isn't a similar-but-different library, it's the actual thing being reimplemented, sitting one `node_modules` lookup away.

The clearest evidence this reimplementation carries real cost: TSK-0262 (`Done`) was a dedicated bug-fix task for `.floored()` returning `this` instead of a new object — *"Previously, `.floored()` returning `this` caused prismarine-world's `block.position = pos.floored()` to leak the shim into Mineflayer's internal geometry calculations, corrupting aim points."* This is precisely the kind of subtle contract bug that a hand-maintained reimplementation of a well-specified library is prone to, and that using the real library would have avoided by construction — the real `vec3` package's `.floored()` behavior is already correct, tested, and used internally by every other part of the Mineflayer stack this adapter talks to.

**Recommendation.**
1. Add `"vec3"` as an explicit direct dependency in `package.json` (pin to whatever version `mineflayer`'s tree currently resolves, e.g. `^0.1.10`, or check for a more current major if compatible) — today it's only present as an undeclared transitive/phantom dependency, which is itself a minor risk (nothing guarantees it stays available if `mineflayer`'s own dependency tree changes).
2. Replace `import { toVec3 } from './vec3.js'` / `toVec3(x, y, z)` call sites with `import { Vec3 } from 'vec3'` / `new Vec3(x, y, z)`. Given `toVec3`'s output is designed to match the real API 1:1 (that was explicitly the goal per the header comment), this should be a largely mechanical swap — the main risk is any place the code relies on `toVec3` returning a plain object (e.g., `JSON.stringify`-ing it directly, or property enumeration) rather than a class instance, which is worth a grep pass before removing the shim (see Open Questions).
3. Delete `vec3.js` (231 lines) once the migration is verified. This also removes a whole category of future bug reports like TSK-0262 by construction, since the "spec" and the "implementation" become the same artifact.

**Confidence: 93%.** The presence of the real package and the described bug history are both directly verified; the "migration is mechanical" claim carries slightly more uncertainty since this pass did not attempt the migration or diff every call site (see Open Questions).

---

### H3 — `Movements` pathfinder singleton not rebound after bot reconnect

**Evidence** (`MineflayerAdapter/movements.js`):
```js
let _instance = null;
export function createMovements(bot) {
  if (!_instance) {
    const m = new Movements(bot);
    m.canOpenDoors = true;
    _instance = m;
  }
  return _instance;   // bot parameter ignored on every call after the first
}
```
Own doc comment: *"Uses a lazy singleton — the first call constructs and caches one Movements instance... The bot argument is only used during construction to access bot.registry; subsequent calls ignore it."* `index.js`'s reconnect logic (`connectBot()`, documented at TSK-0263) reassigns the module-level `bot` variable to a **new** Mineflayer bot instance on reconnect — but every one of the 7 call sites (`grep -c "createMovements(bot)"` across `index.js`) passing that new `bot` into `createMovements` will silently receive the stale singleton bound to the pre-reconnect bot's `registry`.

**Severity depends on what changes across a reconnect** — if the bot always reconnects to the same Minecraft server/version, `bot.registry` (block ID/data mappings) is presumably unchanged and this is latent rather than actively harmful. If a reconnect could plausibly happen against a different server or after a server-side version change, pathfinding would silently operate against a stale registry — a class of bug that would be very difficult to diagnose from symptoms alone (pathfinding failures or wrong-block interactions with no obvious cause).

**Recommendation.** Add a `resetMovements()` export (or an optional `force` parameter to `createMovements`) and call it from the reconnect path in `index.js` alongside the other reconnect-time state resets already present there (per TSK-0263's own scope, which presumably re-registers event handlers — this should be added to that same reset routine). Low-risk, mechanical; the harder part is verifying reconnects are rare/tested enough that this hasn't already caused a hard-to-explain field issue (see Open Questions).

**Confidence: 85%.** The code fact (singleton, ignored parameter after first call) is certain; the real-world severity is an estimate since it depends on operational patterns (how often/under what circumstances reconnects actually occur) not observable from static code.

---

### H4 — TSK-0166 doesn't reflect already-completed partial modularization

**Evidence.** Four of `index.js`'s originally-inline concerns have already been extracted into standalone modules, each citing sprint/task provenance in its own header:
- `stopState.js` — *"Extracted from index.js Sprint 52 modularization (TSK-0166)"*
- `config.js` — *"Extracted from index.js Sprint 52 modularization (TSK-0166)"*
- `movements.js` — Sprint 55, TSK-0213 (a separate task, but same spirit)
- `vec3.js` — Sprint 41

TSK-0166 itself remains `Backlog`, titled "Modularize MineflayerAdapter: split `index.js` into focused modules" — its description (per Report #1's citation of it) doesn't currently distinguish "not started" from "partially done, `index.js` still 2,189 lines but four modules already carved out." This doesn't block anything, but a future contributor scoping this task from its description alone would reasonably underestimate how much groundwork already exists (established module boundaries, an established comment convention for citing the task in extracted files) to build on.

**Recommendation.** Update TSK-0166's description with a short "already extracted: stopState, config, movements, vec3 — remaining: mining/placement/crafting/movement action handlers, roughly N lines each" breakdown, so the next pass at it starts from an accurate map rather than rediscovering the same territory covered across four separate prior sprints.

**Confidence: 80%.** The extraction history is directly evidenced by file headers and git log; whether this rises to "worth a task update" vs. "fine as-is since anyone opening the directory would see the same thing" is a judgment call.

---

### H5 — Safety command deny-list: verified correct (no defect)

Included per this audit's stated goal of thorough, evidence-based coverage rather than only reporting problems. `AgentBackgroundService`'s `case "command":` handler (~line 1384–1440) was read in full:
- `intent.Item.Split(' ')[0].ToLowerInvariant()` correctly isolates the base command before matching, so `/op steve` is caught by a denied `/op` entry rather than only matching an exact full-string `/op steve`.
- `DeniedCommands` (the effective property) always merges the built-in `DefaultDeniedCommands` floor with any configured additions — configuration can only *add* denials, never remove the floor (verified at lines 592–603).
- Intents without a leading `/` are explicitly rejected via the `else` branch (`logger.LogWarning("[command] missing item or no leading slash...")`) rather than falling through to dispatch unfiltered.
- Both the "blocked" and "allowed via explicit opt-in" paths are logged at `Warning` level with the goal name and command included — good operational visibility, consistent with what's recommended elsewhere in this audit series for other silent-failure findings.

No recommendation — noted as a clean bill of health on a safety-critical path.

**Confidence: 95%.**

---

### H6 — `BLOCK_MINING_ALIASES` currently covers exactly one case

**Evidence** (`MineflayerAdapter/config.js`):
```js
export const BLOCK_MINING_ALIASES = Object.freeze({
  dirt: ['dirt', 'grass_block'],
});
```
Comment: *"when asked to mine a block, also accept blocks that drop the same item when mined."* As a general statement this applies to many other Minecraft block families (e.g. different log-rotation states, various ore "deepslate" variants that drop the same item as their stone counterparts, different sapling/leaf types) — but only the dirt/grass_block pair is currently populated.

**Recommendation.** Not a defect — flagged as a scoping question for the team: either (a) this is intentionally narrow (dirt/grass was the one case that actually caused a field bug, per Report #1's F6, and there's no present need for more), in which case a one-line comment saying so would help a future reader avoid assuming broader coverage exists, or (b) more cases are known to be missing and worth adding opportunistically as they're discovered. Either resolution is fine; the only actionable ask is closing the ambiguity.

**Confidence: 75%.**

---

## Assumptions & Open Questions

1. **H1's fix assumes `MinecraftAdapter` can access an `ILogger`** — this wasn't independently verified (the class doesn't currently take one; adding it means a constructor signature change, with a corresponding DI-registration update in `Program.cs`, which is more than a one-line swap). Scope this fix as "add ILogger to MinecraftAdapter" rather than assuming it's already trivially available.
2. **H2's "mechanical migration" claim is not fully verified.** This pass did not run the real `vec3` package's methods against `vec3.js`'s call sites to confirm 1:1 behavioral parity beyond what the shim's own header comment claims, nor did it check whether any code relies on `toVec3`'s return value being a plain object (e.g., structural equality checks, `JSON.stringify` on positions sent over the WebSocket bridge — worth specifically checking, since `Vec3` class instances serialize differently than plain objects with some `JSON.stringify` configurations, though typically fine for simple `{x,y,z}` shapes). Recommend a focused spike/spot-check before committing to the full removal.
3. **H3's real-world severity is genuinely unknown from static analysis** — whether reconnects in this project's actual usage ever cross a version/registry boundary (vs. always reconnecting to the same dev server) determines whether this is "fix eventually" or "fix now." Worth a quick question to whoever operates the bot day-to-day.
4. **No profiling or dynamic testing was performed** for any finding in this report (consistent with all prior reports in this series) — all findings are static-evidence-based.

---

## Suggested Sequencing (additive to prior reports)

1. H1 (fix + add the banned-API analyzer rule) — high-leverage: the analyzer addition prevents this exact class of regression from recurring a third time, which is the more valuable half of this fix.
2. H5 — no action needed, informational only.
3. H6 — 5-minute decision + comment, whenever convenient.
4. H4 — task-tracker text update, no code risk, bundle with any other task-hygiene pass (e.g. Report #1's F7, Report #4's G5).
5. H3 — moderate priority pending the Open Questions #3 answer; low-risk fix once scoped.
6. H2 — the largest single effort in this report; do it as its own focused PR given the Open Questions #2 caveat, ideally with the existing test coverage (if any exists for position/vector handling) run before and after to catch behavioral drift.
