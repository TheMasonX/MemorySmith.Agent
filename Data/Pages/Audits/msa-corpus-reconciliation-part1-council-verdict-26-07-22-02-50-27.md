# MemorySmith.Agent — Corpus Reconciliation, Part 1: The July-11 Council Verdict vs. Current HEAD

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`) — same commit the council itself audited.

---

## Scope and methodology for this reconciliation

`Data/Pages/Audits/` contains 91 documents (~20,400 lines). Rather than re-mining all 91 independently — most are iterative drafts superseded by later ones — this reconciliation anchors on the corpus's own most-authoritative, most-recent, explicitly-superseding document:

> **`synthesizer-verdict-internal-audit-60-20260711.md`** — dated 2026-07-11 (the same commit date as HEAD), produced by synthesizing a raw 10-agent audit (`internal-audit-60-20260711.md`, 76 findings) against an independent 5-chair council recalibration (`codebase-audit-20260711-10agent-swarm-5chair-council-post-wave-c.md`, 79 findings), with all P0/P1 claims source-verified by a dedicated "Runtime & Debugging" chair. Its closing line: *"This verdict supersedes the raw audit report's uncalibrated severities. All subsequent Sprint 61 planning should use the council-recalibrated severities... rather than the raw... audit."*

This is, by the corpus's own account, the ground truth to reconcile against — not one input among 91 equally-weighted documents. **This report's contribution is not re-finding these 79 items** (they're already excellently documented, with file/line references, severities, and rationale, in that document). **It's checking which of them are still true at HEAD**, since the verdict itself is 10+ days old relative to this exact commit's own timeline of fixes, and since the verdict document explicitly names several items as "currently untracked" — a claim worth re-verifying rather than trusting at face value, per this audit's own standing instruction.

This is **Part 1** of the reconciliation: it covers the document's own "Critical Action Items" checklist and "Task Mapping Gaps" table in full (9 items — the ones the verdict itself flagged as needing follow-up), plus spot-verification of a sample of the "Confirmed Findings" and "New Findings From Council" tables (56+ additional items) rather than all of them exhaustively. See "What Part 2 would cover" at the end for the remaining scope.

---

## Executive Summary

| # | Item (per council verdict) | Council priority | Status at HEAD (verified this pass) |
|---|---|---|---|
| K1 | **P0-006 — LLM prompt injection, no input sanitization for chat messages** | Critical (P0) | **Still completely unfixed.** Both tasks created for it (`TSK-0383`, `TSK-0390`) are `Backlog`, and are themselves an unflagged duplicate pair. Code confirms the raw player chat message is embedded into the LLM prompt with only a length truncation — no sanitization, and (new detail this pass) **no escaping of embedded quote characters**, meaning a message containing a `"` can prematurely close the prompt's own quoting device. This is the single highest-severity open item in the entire council review. |
| K2 | **P0-002/P0-003/P0-004 — Node.js crash-surface findings (sendEvent try/catch, unhandledRejection/uncaughtException guards, stdout/stderr pipe drain)** | Critical (P0) | **Fixed.** All three have a `Done` task (`TSK-0387`, `TSK-0388`, `TSK-0389`) superseding an initially-`Archived` duplicate set (`TSK-0380/0381/0382`). Good news, stated plainly: the most severe process-crash risks the council found were genuinely closed. |
| K3 | **P2-010 — `/api/agent/stop` and `/api/agent/connect` are silent no-op stubs** | Should-add (P2), explicitly listed as "❌ No task" in the verdict | **Still no task, still exactly as described in code** — both endpoints unconditionally return a canned success response regardless of actual agent state. **New, adjacent finding this pass**: `/api/blueprints` (defined two lines above them in `Program.cs`) has the same smell — a hardcoded single-entry array (`"small-house"`) rather than a live query against the blueprint repository. |
| K4 | **P1-003 — `BuildGoal` missing an `Id` property (all instances share correlation identity)** | High (P1), explicitly listed as "❌ No task" | Tasks now exist (`TSK-0384`, and a later, unflagged duplicate `TSK-0407`) — both `Backlog`. Confirmed still true in code: `BuildGoal.cs` has no `Id` field; fact-key correlation runs through `Blueprint.Id` (the blueprint *name*, shared across all instances of the same build), not a per-goal identifier. |
| K5 | **P1-010 — No chat command rate limiting / unbounded `cmdQueue`** | High (P1), explicitly listed as "❌ No task" | Tasks now exist (`TSK-0386`, and a later, unflagged duplicate `TSK-0408`) — both `Backlog`. Not independently re-verified in code this pass (time-boxed; flagging status only). |

**K1 is the standalone headline of this report.** Everything else here is useful bookkeeping; K1 is a live, unmitigated security gap in a project whose own council explicitly rated it Critical two weeks before this exact commit, and it remains two duplicate `Backlog` tickets and zero code changes later.

---

## Detailed Findings

### K1 — LLM prompt injection: confirmed still live, with a sharper technical detail

**Council's original finding** (`P0-006`, `codebase-audit-20260711-...-post-wave-c.md` via the synthesizer verdict): *"LLM prompt injection — no input sanitization for chat messages,"* located at `LlmChatInterpreter.cs:~230`.

**Re-verified this pass** (`Agent.Planning/LlmChatInterpreter.cs`):
```csharp
// line 70-72
var effective = message.Length > options.MaxMessageLength
    ? message[..options.MaxMessageLength]
    : message;
...
// line 127
var userMessage = $"{username} says: \"{effective}\"";
...
// line 134
var raw = await provider.CompleteAsync(systemPrompt, userMessage, ct);
```
The **only** transformation applied to a raw, player-controlled chat message before it becomes part of the LLM's user-turn content is a length truncation. There is no filtering, escaping, or delimiter-hardening of any kind.

**Sharper detail found this pass, not called out in the original finding:** `effective` is interpolated directly between literal `\"` characters with **no escaping of quote characters inside the player's own message**. A chat message containing a `"` character breaks the intended "this is quoted, untrusted text" framing at the string level, not just at the semantic/LLM-instruction-following level — meaning the quoting device the code appears to rely on for delimiting untrusted content is trivially defeated by a single character, independent of whatever the LLM itself would or wouldn't comply with. This doesn't change the finding's severity (already correctly rated Critical/P0 by the council) but gives whoever fixes it a slightly more precise starting point: at minimum, escape or strip embedded quote characters as a first-pass hardening step, independent of (and in addition to) any semantic prompt-injection defenses (delimiter tokens, instruction-hierarchy prompting, output-schema validation) that would address the deeper problem.

**Tracking status:** `TSK-0383` ("Add LLM prompt injection protection — sanitize chat input") and `TSK-0390` ("Add LLM prompt injection guard for chat messages") both exist, both `Backlog`, and — per this series' recurring backlog-hygiene finding (Report #1 F7, Delta #4 G5) — **are themselves an unflagged duplicate pair**, not caught by the Wave D/E handoff's own cleanup pass (which caught other pairs but not this one, same pattern as the `GatherItemDecompose` duplicate found in Delta #4).

**Recommendation.** Given this is the single highest-severity confirmed-open item across the entire reconciled corpus: merge `TSK-0383`/`TSK-0390` into one task, and treat it as the top of the backlog rather than letting its `Backlog` status understate it next to items like the (comparatively low-stakes) documentation nits elsewhere in this audit series. A reasonable minimum-viable fix: (1) escape/strip quote characters and control characters from `effective` before interpolation (addresses the sharper detail above), (2) wrap the untrusted content in an explicit, LLM-provider-appropriate delimiter (e.g., XML-style tags naming it as untrusted user input, a pattern several LLM providers are specifically trained to respect), (3) validate the LLM's JSON response against the expected schema strictly enough that a successful injection which gets the model to emit an unexpected `"intent"` value or extra fields fails closed rather than being trusted.

**Confidence: 95%** on the current-code claims (direct read); the severity assessment is inherited from the council's own P0 rating, which this pass has no basis to disagree with.

---

### K2 — Node.js crash-surface: confirmed fixed (stated for balance)

Verified via task records: `TSK-0387` (sendEvent crash guard), `TSK-0388` (unhandledRejection/uncaughtException handlers), `TSK-0389` (pipe buffer hang fix) are all `Done`, superseding an initial `Archived` set (`TSK-0380`, `TSK-0381`, `TSK-0382` — themselves the duplicate-pair pattern, correctly cleaned up in this instance). This wasn't independently re-verified line-by-line in code this pass (time-boxed — the task records' specificity and the fact they're marked `Done` rather than found to be another false-completion instance is treated as reasonably trustworthy here, unlike the `AgentRuntime`/`ExecutionContext` cases in Reports #1/#6 where "Done" repeatedly didn't hold). **Stated for balance**: this audit series has spent a lot of space on false completions and dead scaffolding; it's worth being equally direct that the most severe, most acute findings (process-crash risks) from the same review cycle were the ones that actually got fixed — a reasonable prioritization outcome, not a universal failure to execute.

**Confidence: 80%** (task-record-based, not independently re-verified in code this pass — lower than this report's other findings for that reason).

---

### K3 — No-op API stubs: confirmed still untracked, still present, plus one adjacent bonus finding

**Council's finding** (`P2-010`): `/api/agent/stop` and `/api/agent/connect` are silent no-op stubs, explicitly listed in the verdict's own "Task Mapping Gaps" table as "❌ No task." Confirmed still no task exists (`grep -i "no-op\|noop\|agent/stop\|agent/connect"` across all task titles/descriptions → no results).

**Confirmed in code** (`WebUI.Blazor/Program.cs:725-726`):
```csharp
app.MapPost("/api/agent/connect", () => Results.Ok(new { Status = "connected" }));
app.MapPost("/api/agent/stop",    () => Results.Ok(new { Status = "stopped" }));
```
Exactly as described — these endpoints report success unconditionally, with no reference to the actual agent's connection/running state.

**New, adjacent finding this pass:** the same file, two lines earlier (`Program.cs:722-723`):
```csharp
app.MapGet("/api/blueprints", () => Results.Ok(new[]
    { new { Id = "small-house", Name = "Small Survival House", Tags = new[] { "house", "starter" } } }));
```
`/api/blueprints` is meant to list available blueprints, but returns a single hardcoded fake entry regardless of what's actually stored via `IBlueprintRepository`/`MemorySmithBlueprintRepository` (the real, working repository this series covered in an earlier pass). This wasn't in the council's original P2-010 finding but shares its exact character — a REST endpoint whose implementation doesn't match its apparent contract — and sits immediately adjacent in the source, suggesting these three endpoints were likely stubbed together at the same time and never revisited.

**Recommendation.** File one task covering all three endpoints together (`/api/agent/connect`, `/api/agent/stop`, `/api/blueprints`) rather than three separate ones, given they're co-located, share the same root cause (early scaffolding never wired to real state), and a developer fixing one will have full context for the others already loaded.

**Confidence: 95%** (direct code read for all three endpoints; the "likely stubbed together" inference is reasonable but not independently confirmed via git blame in this pass).

---

### K4 — `BuildGoal.Id`: confirmed still missing, now duplicated in the backlog

Confirmed via `Agent.Planning/Goals/BuildGoal.cs`: no `Id`/`Guid` field exists on the class. Fact-key correlation (`IsComplete`/`HasFailed`) runs through `$"goal:Build:{Blueprint.Id}:complete"` — `Blueprint.Id` is the blueprint's *name* (e.g., `"small-house"`), shared by every instance of a build of that blueprint, not a per-goal-instance identifier. Two sequential (or, worse, concurrent) builds of the same blueprint would read/write the same fact keys, with no way to distinguish their outcomes.

Tasks: `TSK-0384` ("Add BuildGoal.Id property for outcome correlation") and `TSK-0407` ("Add BuildGoal.Id property for per-goal outcome correlation") — both `Backlog`, both clearly the same task, filed at different times. This is the same duplicate pair already flagged by this series' Report #1 (F7) as needing archival — this reconciliation confirms its origin: both trace back to the council verdict's `P1-003`, which correctly identified it as untracked at the time, and then got ticketed twice independently afterward.

**Confidence: 93%.**

---

## Critical Action Items checklist (from the verdict document itself), reconciled

The verdict document ends with its own 5-item "Critical Action Items" list. Reconciling each against HEAD:

| # | Verdict's action item | Status at HEAD |
|---|---|---|
| 1 | Create urgent tasks for the 4 Node.js crash-surface P0 findings | **3 of 4 done and fixed** (K2). The 4th (prompt injection, K1) has tasks but is unfixed — worth noting this item was phrased as a group of 4, and is 75% resolved by task-count but the unresolved 25% is arguably the most important of the four (a security/trust-boundary issue vs. process-stability issues). |
| 2 | Update the raw audit report to reflect council recalibrations | Not checked this pass — out of scope (a documentation-consistency question about the audit corpus itself, not the codebase). |
| 3 | Transfer Peer Review Results into the raw audit's placeholder section | Same as above — not checked. |
| 4 | Create the 7 recommended new tasks (TSK-0372→0378) and map findings to them | Tasks were created, under different numbers than proposed (`TSK-0380`–`TSK-0390` range) — the substance happened even though the specific numbering didn't match the proposal, which is fine and expected once real task-numbering sequence is applied. |
| 5 | Proceed with Sprint 61 planning using the council's prioritized candidates | Not independently checkable from static repo inspection — would need sprint-planning artifacts dated after this commit, which aren't part of what's audited here. |

---

## What Part 2 would cover, if pursued

This installment covers the verdict document's own explicit follow-up items (9 total) plus 2 spot-verified "Confirmed Findings" table entries used as calibration checks. The verdict document contains **56 distinct `MSA-*`/council-ID findings** in its "Confirmed Findings" and "New Findings From Council" tables that were not individually re-verified against current HEAD in this pass. A Part 2 would:
1. Walk the remaining ~50 confirmed findings one by one, checking each against current code (many are likely already resolved via the Sprint 60 Wave A–C work referenced throughout the verdict; some are likely still open).
2. Reconcile the "Merged / Deduplicated Findings" table's 7 merge groups against current task records, to confirm the merges were actually honored in how tasks got filed (vs. e.g. still having 4 separate tickets for the `ITimeProvider` group that was supposed to collapse into 1).
3. Only after that — if there's appetite for it — expand backward into the other 87 historical documents, primarily to confirm none of them contain a still-relevant finding that didn't make it into the July-11 consolidation (the consolidation process appears rigorous, so this is expected to be low-yield, but "expected low-yield" isn't the same as "confirmed," per this audit's own standard of not taking claims at face value).

Given the size of that remaining scope, it's worth explicitly deciding whether it's the best use of further effort versus, e.g., actually implementing K1 (the prompt injection fix) or the migration task written up earlier in this series — reconciliation has diminishing returns once the highest-severity open items (K1 above) are already surfaced and actionable.
