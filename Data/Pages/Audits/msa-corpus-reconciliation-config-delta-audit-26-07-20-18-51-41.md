# MemorySmith.Agent — Delta Audit #5: Prior-Audit-Corpus Reconciliation + Config/Adapter Findings

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`) — same commit as prior four reports.
**This report opens with an important methodology correction — read this section before the findings table.**

---

## Methodology correction: a large prior-audit corpus exists and I hadn't fully cross-referenced it

While auditing `WebUI.Blazor/Options/SafetyOptions.cs` and `Agent.World.Minecraft` this pass, I found `Data/Pages/Audits/` contains **91 pre-existing audit documents** — including a rigorous, same-commit-era **10-agent-swarm + 5-chair-council review dated 2026-07-11** (`codebase-audit-20260711-10agent-swarm-5chair-council-post-wave-c.md`, synthesized in `synthesizer-verdict-internal-audit-60-20260711.md`) that independently confirmed **79 findings** (5 P0, 14 P1, 28 P2, 32 P3) against this exact codebase, converting the highest-priority subset into 14 tracked tasks (TSK-0373–TSK-0386).

Prior to this pass, my duplication-avoidance check was scoped to `Data/Tasks/*.json` (404 records) — I did not systematically cross-reference the `Data/Pages/Audits/` document corpus itself. Spot-checking my four prior reports' headline findings against this corpus shows meaningful overlap:

| My finding | Prior corpus reference | Overlap |
|---|---|---|
| Report 1, F2 (`AgentRuntime`/`*ManagerImpl` dead scaffolding) | Mentioned in **20** prior audit docs, including `internal-audit-60-20260711.md`'s `MSA-MGR-001` ("DashboardPublisherImpl dead code," confirmed P2) | Already known, independently re-confirmed by me; not novel |
| Report 3, E2 (`InputSchema` re-parsed, no caching, across tool classes) | `synthesizer-verdict-internal-audit-60-20260711.md`: **`MSA-TOOL-003` — "InputSchema caching bug — 11 of 14 tools" — Confirmed P2** | Already known and *more precise* than my version (I said "all 14," the council's source-verified count is 11 of 14 — see Correction below) |
| Report 1, F1 / this-series' recurring `GatherItemDecompose` over-mining finding | `MSA-PLAN-001` — "GatherItemDecompose mines ALL source blocks at full count" — Confirmed P1 (this is the same bug behind TSK-0364/TSK-0397, already flagged as a duplicate pair in Delta #4's G5) | Already known |

**Correction to Report 3, E2:** the council's finding (`MSA-TOOL-003`) states **11 of 14** tools have the caching bug, not all 14 as I reported. I did not re-derive the exact count in this pass (would require diffing which 3 tools differ) — flagging the correction now rather than leaving the overstated count uncorrected. If it matters for scoping the fix, re-check which 3 tools already cache correctly before writing the fix ticket.

**What this changes going forward:** rather than continuing to independently re-derive findings this corpus may already contain (which wastes effort and risks minor factual drift like the above), the higher-value move is to **mine the corpus for confirmed-but-still-untracked findings** — i.e., things 79-finding-strength review processes already verified as real bugs, that never got a `TSK-###` filed (because council bandwidth only converted the top 14 of 79 into new tasks — a reasonable, not negligent, prioritization call, given 28 P2s and 32 P3s were confirmed but evidently didn't all warrant individual tickets). This report's findings below follow that approach: each is either (a) genuinely new and not found in the corpus (verified by grep before inclusion), or (b) confirmed-real-and-still-untracked, explicitly cited to its source document rather than re-argued from scratch.

---

## Executive Summary

| # | Finding | Class | Confidence | Impact | Status |
|---|---|---|---|---|---|
| I2 | **`movements.js`'s `Movements` singleton is never invalidated across a bot reconnect** — confirmed still true at HEAD. This exact finding, with full root-cause analysis and a specific fix, already exists in `Data/Pages/Audits/msa_deep_audit_delta4_2026-07-05.md` (Finding 1, 88% confidence), which explicitly documented *why it has no task*: `TSK-0269` only touches `movements.js` for an unrelated import-style nit. Six sprints later, still no task exists and the bug is still live. This is the single most "ready to ship" item in this report — the analysis is done; it just needs a `TSK-###` filed. | Confirmed pre-existing bug, still untracked | 95% (existence + still-live), 88% (downstream symptom severity, per the original doc's own hedge) | High | **Confirmed still open — recommend filing immediately** |
| I3 | **`MinecraftAdapterConfig` has two independent fields encoding the same connection port** (`WebSocketUrl: "ws://localhost:3000"` and `WebSocketPort: 3000`), used for different purposes (bridge connection string vs. Node-process env var + startup port-poll) with **zero cross-validation**. Changing one without the other silently misconfigures the connection. Compounds with `WaitForPortAsync`'s bare `catch {}` (swallows every exception during the connect-retry poll, zero logging), so the only symptom of this misconfiguration is a generic `TimeoutException` after `NodeStartTimeoutMs` with no diagnostic clue as to why. **Verified not present in the 91-document prior corpus** (zero hits for `WebSocketPort`/`WebSocketUrl` across all audit docs). | Data Clump / Primitive Obsession + silent-error compounding | 90% | Medium | **New** |
| I4 | **`SafetyOptions.DeniedCommands`'s XML doc comment describes stale, pre-fix merge semantics.** It says the built-in defaults are used "as the fallback" when config is empty — implying config *replaces* defaults when non-empty. The actual, current merge logic (`AgentBackgroundService.DeniedCommands` property, correctly documented in *that* file's own comment referencing `TSK-0324`) is a permanent **union** — config can only add denials, never remove the built-in floor. `SafetyOptions.cs`'s own comment was never updated when `TSK-0324` changed the behavior. Safety-adjacent documentation, so worth a one-line fix despite low severity. | Documentation accuracy (safety-adjacent) | 82% (hedged — see note below) | Low-Medium | **New (best available check; not exhaustively verified against all 91 docs — see note)** |
| I5 | **`DefaultDeniedCommands`'s literal `HashSet` initializer contains 3 duplicate entries** (`"/w"`, `"/tm"`, `"/teammsg"` each appear twice). Harmless at runtime (`HashSet` dedupes silently) but a clear copy-paste signal from list extension across sprints without checking existing entries. | Nit / code cleanliness | 97% | Trivial | **New** |

**Note on I4's confidence:** I checked whether the exact "stale doc comment" framing exists in the 14 audit docs that mention `SafetyOptions`/`DeniedCommands`, and did not find that specific framing in a targeted search — but given this pass's discovery of how large and easy-to-miss the corpus is, I'm hedging this one down slightly (82% rather than 90%+) rather than asserting full novelty with high confidence. Worth a quick manual grep by whoever picks this up before treating it as certainly new.

---

## Detailed Findings

### I2 — `Movements` singleton reconnect bug: confirmed still-live, confirmed still-untracked

I'm not re-deriving this finding — `Data/Pages/Audits/msa_deep_audit_delta4_2026-07-05.md` already contains a complete, well-evidenced writeup (root cause, why it's untracked, and a concrete two-option fix). What this pass adds is **re-verification that it's still true 6 sprints later**, and an explicit recommendation to close the loop:

**Re-verification performed this pass:**
```
$ grep -n "createMovements" MineflayerAdapter/index.js   → 7 call sites, all pass a fresh `bot` reference
$ grep -n "_instance = null\|resetMovements" MineflayerAdapter/movements.js MineflayerAdapter/index.js
  → no results anywhere; module-level `_instance` is never reset
$ grep -n "connectBot" MineflayerAdapter/index.js → reconnect path (line ~440) calls connectBot() again,
  which does `bot = mineflayer.createBot(botOpts)` (a brand-new bot object) but never touches `movements.js`'s cache
```
All of the original doc's claims check out unchanged at HEAD.

**Recommendation (unchanged from the original doc, restated for convenience):** either (a) reset `_instance = null` inside `connectBot()` right after assigning the new `bot` — simplest, lowest-risk, since `connectBot()` is the sole reconnection entry point — or (b) key the cache by bot identity if `Movements` exposes its bound bot. File a `TSK-###` now; the analysis is already complete, this just needs to enter the backlog so it doesn't remain permanently un-actioned.

**Confidence: 95%** that the bug and its untracked status are both real and current (direct re-verification); the original doc's own 88% figure is retained for the downstream-symptom-severity claim, which remains an inference about `mineflayer-pathfinder` internals rather than an observed crash.

---

### I3 — `MinecraftAdapterConfig`: port encoded in two unvalidated places, masked by a silent-catch retry loop

**Evidence** (`Agent.World.Minecraft/MinecraftAdapterConfig.cs`):
```csharp
public string WebSocketUrl { get; init; } = "ws://localhost:3000";
public int WebSocketPort { get; init; } = 3000;
```
Both default to port 3000, but they're independent, unvalidated fields. Confirmed distinct consumers (`Agent.World.Minecraft/MinecraftAdapter.cs`):
```csharp
_bridge = new WebSocketBridge(config.WebSocketUrl);                         // line 31 — bridge connects here
psi.EnvironmentVariables["WS_PORT"] = config.WebSocketPort.ToString();       // line 119 — passed to spawned Node process
await WaitForPortAsync(config.WebSocketPort, config.NodeStartTimeoutMs, ct); // line 151 — polls this port before connecting
```
No code anywhere parses `WebSocketUrl`'s port and compares it against `WebSocketPort`, or vice versa. If an operator reconfigures `WebSocketPort` (e.g., to resolve a local port conflict) without also updating `WebSocketUrl`'s embedded port number, `AutoStartNode` would spawn the Node process listening on the *new* port, `WaitForPortAsync` would correctly poll and confirm *that* port opens, and then `WebSocketBridge` would attempt to connect to the *old* port in the stale `WebSocketUrl` — a connection failure that would look, from the operator's perspective, exactly like "the bridge just doesn't work," with no error message pointing at the actual mismatch.

This is compounded by `WaitForPortAsync`'s retry loop:
```csharp
try { using var tcp = new TcpClient(); await tcp.ConnectAsync("127.0.0.1", port, ct); return; }
catch { await Task.Delay(200, ct); }
```
A bare `catch {}` — every connection-refused attempt during the (expected, normal) startup polling window is silently discarded, which is fine for the *expected* case, but it means if the timeout is eventually hit, the resulting `TimeoutException` carries zero information about *why* — was it a port mismatch (this finding), a Node process crash, a firewall block, or something else? All look identical from the outside.

**Recommendation.**
1. Lowest-risk fix: derive the connection port from a single field. Either drop `WebSocketPort` and parse the port out of `WebSocketUrl` via `new Uri(config.WebSocketUrl).Port` wherever an `int` port is needed (for the env var and the poll), or drop `WebSocketUrl` and construct it from `WebSocketPort` (`$"ws://localhost:{WebSocketPort}"`) — either removes the two-source-of-truth problem entirely.
2. If both fields must stay for backward-compat/config-format reasons, add a startup validation check (e.g., in `Program.cs`'s options configuration, or a `ValidateOnStart` on `MinecraftAdapterConfig`) that parses `WebSocketUrl`'s port and throws a clear, actionable error at startup if it doesn't match `WebSocketPort`, rather than allowing the mismatch to surface later as an opaque timeout.
3. Independently: consider whether `WaitForPortAsync`'s catch block should at least capture and log the *last* exception type once the timeout is actually hit (not on every retry — that would be noisy — just attached to the final `TimeoutException` thrown), so a genuine failure (vs. simple/expected startup delay) is diagnosable from logs alone.

**Confidence: 90%.** The two-fields-one-concept fact and the bare-catch fact are both directly confirmed by reading the source; the "this would actually confuse an operator in practice" severity claim is a reasonable but unverified inference (no test of an actual misconfigured deployment was performed).

---

### I4 — `SafetyOptions.DeniedCommands` doc comment describes pre-`TSK-0324` behavior

**Evidence.** `WebUI.Blazor/Options/SafetyOptions.cs`:
```csharp
/// ...When this set is empty in config, the built-in <c>DefaultDeniedCommands</c>
/// in AgentBackgroundService is used as the fallback.
```
`WebUI.Blazor/AgentBackgroundService.cs` (the actual consuming property, correctly documented in place):
```csharp
/// Effective denied commands set. Always includes the built-in <see cref="DefaultDeniedCommands"/>
/// as a safety floor. The user-configured set from <see cref="SafetyOptions"/> is merged (union) on
/// top — it can add more denied commands but never removes the built-in defaults.
/// Sprint 58 Wave C (TSK-0324): changed from XOR-replace to union-merge to prevent
/// accidentally removing protections like /ban, /stop, /execute.
```
The current code (post-`TSK-0324`) is a **permanent union** — the defaults are always included, period, regardless of whether config supplies its own list. `SafetyOptions.cs`'s doc comment describes the *pre-`TSK-0324`* behavior (defaults only apply when config is empty, implying config could otherwise fully replace them) and was evidently never updated when the fix landed elsewhere in the codebase.

**Why worth a one-line fix despite low severity:** this is safety-configuration documentation. An operator reading only `SafetyOptions.cs` (a reasonable thing to do when configuring `appsettings.json`) could come away believing they can override/remove the built-in denial floor by supplying their own `DeniedCommands` list — which is not true, and understanding that it's not true is exactly the kind of thing safety-relevant documentation should get right.

**Recommendation.** Update `SafetyOptions.cs`'s doc comment to match `AgentBackgroundService.cs`'s (or better, have one reference the other via `<seealso>` to avoid a third copy of this explanation drifting independently in the future — this is a small instance of the same "explain it once, reference it elsewhere" principle behind several of this audit series' duplication recommendations).

**Confidence: 82%** — see the hedge in the Executive Summary; genuine novelty not exhaustively confirmed against the full 91-document corpus.

---

### I5 — Duplicate literal entries in `DefaultDeniedCommands`

**Evidence** (`WebUI.Blazor/AgentBackgroundService.cs`, `DefaultDeniedCommands` initializer): direct text scan shows `"/w"`, `"/tm"`, and `"/teammsg"` each listed twice within the same `HashSet<string>` collection initializer. Functionally harmless — `HashSet` silently dedupes — but a clear signal the list was extended (likely across separate sprints, consistent with this codebase's general pattern of sprint-by-sprint accretion without a periodic consolidation pass, cf. Report 2's D10) without checking against existing entries.

**Recommendation.** Trivial cleanup — remove the 3 duplicate literals. Zero behavior change; purely a readability/cleanliness fix. Good candidate to bundle with I4's doc-comment fix in the same small PR, since both touch the same class.

**Confidence: 97%.**

---

## Assumptions & Open Questions

1. **This pass did not attempt to fully reconcile all 91 prior audit documents against all of Reports #1–#4's findings** — that would be a substantial undertaking (91 documents, many with overlapping/superseding content across a month of iteration) and wasn't the ask. The spot-checks performed (Executive Summary's overlap table) are illustrative of the scale of overlap, not exhaustive. If a fully reconciled "master list, deduplicated against the entire prior corpus" is wanted, that's a distinct, larger task from continuing to audit slice-by-slice — worth explicitly deciding whether that's the more valuable next step versus continuing to find incremental new corners.
2. **I2's fix is not implemented in this pass** — per the audit's scope (find and report, not fix), but given the analysis is fully complete in the cited prior document, this is about as close to "just needs a task filed" as a finding gets.
3. **I4's confidence is explicitly hedged** — flagging my own uncertainty about novelty here rather than either suppressing the finding or overclaiming it, consistent with the instruction to use grounded, evidence-based confidence values rather than defaulting to high confidence.

---

## Suggested Sequencing (additive to prior reports)

1. **File a task for I2 now** — zero additional analysis needed; this is the highest-value single action in this report precisely because the hard part (root-causing a `mineflayer-pathfinder` singleton-lifetime bug) is already done and sitting in a six-sprint-old document nobody's converted to a ticket.
2. I5 (remove duplicate literals) + I4 (fix stale doc comment) — bundle into one trivial PR touching `AgentBackgroundService.cs`'s command list and `SafetyOptions.cs`'s doc comment.
3. I3 (consolidate `WebSocketPort`/`WebSocketUrl`) — small, standalone, no dependencies on other queued work.
4. **Process-level recommendation, not sequenced with the above**: consider whether future audit passes (mine or otherwise) should start by grepping `Data/Pages/Audits/` for key terms before deep-diving a file, the way this pass started checking `Data/Tasks/*.json` — the corpus is large enough now that it's a genuine reference source, not just historical clutter.
