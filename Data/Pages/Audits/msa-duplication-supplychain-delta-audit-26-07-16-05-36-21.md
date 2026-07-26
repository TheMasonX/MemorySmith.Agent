# MemorySmith.Agent — Delta Audit #2: Duplication, Supply-Chain & AI-Codesmell Findings

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`) — same commit as prior audit.
**This is a delta report.** It contains only findings not already in `msa-bloat-duplication-audit-26-07-15-01-32-34.md` or in any `Data/Tasks/*.json` record. Confirmed-still-open items from the prior report are not repeated here.
**New tooling this pass:** `jscpd` run against the full C# tree (not just JS); `npm audit`; manual verification of every clone jscpd flagged above a size threshold; cross-check of DI/package-registration claims against actual call sites (with one self-correction below — see D2's note).

---

## Executive Summary

| # | Finding | Class | Confidence | Impact |
|---|---|---|---|---|
| D1 | **Confirmed transitive supply-chain vulnerability, committed to the repo**: `package-lock.json` pins two different vulnerable `uuid` versions (8.3.2 and 10.0.0, both < patched 11.1.1) via `mineflayer → minecraft-protocol → prismarine-auth → @azure/msal-node → uuid`. `npm audit` reports 6 moderate-severity findings (GHSA-w5hq-g745-h8pq). The bot never explicitly sets `auth:` in `createBot()`, so it's unclear whether Microsoft-account auth (the feature that pulls this whole subtree in) is even used. | Supply-chain / dependency bloat | 93% | Medium |
| D2 | **Inconsistent HTTP resilience coverage**: of the 3 registered named `HttpClient`s, `"memorysmith"` and `"memorysmith-world"` both get `.AddStandardResilienceHandler()` (TSK-0194), but `"llm"` — the client that talks to external, rate-limited, occasionally-flaky LLM providers — does not. No comment explains the omission; TSK-0194's own scope named only the two memorysmith clients. | Inconsistency / gap in already-"Done" task | 88% | Medium-High |
| D3 | **Type-4 duplicate LLM-JSON-extraction logic with materially different robustness**: `LlmChatInterpreter.ParseDecision` strips ` ```json ` fences, extracts via a brace regex, and falls back to `TryParseTruncatedJson` for cut-off output. `LlmEvaluatorImpl.ExtractJson` does none of that — just `IndexOf('{')`/`LastIndexOf('}')`, no fence-stripping, no truncation recovery. **This is the concrete root cause of prior-report finding F5** (silent swallow in the evaluator's directive parser): a response with a `` ```json `` fence and trailing prose, or a token-limit-truncated response, degrades silently in the evaluator path but would have been recovered in the chat-interpreter path. | Duplicated Code (Type-4) / inconsistent resilience | 91% | High — same root cause as previously-flagged silent-failure risk |
| D4 | **jscpd-confirmed near-verbatim duplicate: `RleCompress` (Agent.Planning/Decomposition/BuildGoalDecomposer.cs:106) vs `RleCompressActions` (WebUI.Blazor/AgentBackgroundService.cs:3057)** — same run-length-encoding algorithm, character-for-character identical logic, different input types (`ActionData` list vs `string` enumerable). | Duplicated Code | 95% (jscpd + hand-verified) | Low-Medium |
| D5 | **jscpd-confirmed internal near-clone: identical "split long chat response into chunks and enqueue" block appears twice inside `AgentBackgroundService.cs`** (~line 1575 and ~line 1702) — one copy in the deterministic-parser branch, one at the end of the LLM/switch branch. Verbatim duplicate within the same class. | Duplicated Code | 95% (jscpd + hand-verified) | Low-Medium |
| D6 | **jscpd-confirmed internal clone: coal-fuel-need formula `Math.Max(1, (count + 7) / 8)` duplicated twice in `HtnTaskLibrary.cs`** (Sprint-15 iron-smelting task method vs. Sprint-44 generic smelt-task method), each independently re-deriving "1 coal smelts 8 items → ceiling division" and independently gathering the coal deficit. | Duplicated Code / domain-logic scatter | 90% | Low-Medium |
| D7 | **4 LLM provider classes (`AnthropicProvider`, `OllamaProvider`, `OpenAICompatibleProvider`, and by strong pattern inference `GeminiProvider`) share identical constructor shape, `IsAvailable` pattern, and cancellation/timeout setup**, copy-pasted rather than factored into a shared base. A future bug fix to the timeout-linking logic requires 4 coordinated edits today. | Duplicated Code / Shotgun Surgery risk | 85% | Medium |
| D8 | **Dead tombstone file**: `Agent.Planning/IntentDraft.cs` contains zero executable code — only a comment stating the type moved to `Agent.Core/Models/IntentDraft.cs` in Sprint 39. Harmless but pure clutter; git history already records the move. | Dead code / clutter | 97% | Trivial |
| D9 | **Primitive obsession + silent fallthrough**: `IntentDraft.Intent` is a raw `string` whose valid 10-value contract is documented only in an XML comment, never enforced by the compiler. `IntentManager.BuildGoalRequest`'s switch (lines 46–70) silently returns `null` for any unrecognized value — no log, no metric — so a case-mismatch or novel LLM output value is indistinguishable from "correctly decided not to build a goal." | Primitive Obsession / silently swallowed error | 87% | Medium |
| D10 | **"Sprint N" comment-as-changelog anti-pattern, quantified**: 1,080 `"Sprint N"` references across 94 production files — one every ~19 lines codebase-wide. `AgentBackgroundService.cs` alone carries 272 (one every ~15 lines), directly compounding the god-class readability problem from the prior report. Stale markers (e.g. "Sprint 4a/4b") persist at Sprint 60 with zero ongoing informational value once a change is baseline behavior. This is a recognizable AI-assisted-development artifact: using inline comments as a permanent substitute for git/CHANGELOG history. | AI code smell / bloat | 89% | Low (compounding, not acute) |
| D11 | Lower-confidence jscpd hits flagged for engineer triage, not hand-verified in depth (time-boxed out of this pass): `WorldStateProjector.cs` self-clone (279–287 vs 398–406); `GoalFactory.cs` self-clone (217–226 vs 229–238 — likely a defensible nullable-int-overload pair, low priority); `HtnTaskLibrary.cs` 3 additional internal clone pairs beyond D6 (lines 234–243/311–320, 500–508/581–589, 508–516/589–597) suggesting the whole file's per-resource task-generation methods share an un-extracted "check inventory → mine deficit → add action" shape; `Agent.Core/CommonMinecraftBlocks.cs` vs `HtnTaskLibrary.cs` (3 clone pairs, likely duplicated block-alias lists). | Duplicated Code (unverified detail) | 60% (mechanically detected, not manually confirmed) | Unknown until triaged |

**Process note (self-correction):** an earlier pass of this audit initially flagged `Microsoft.Extensions.Http.Resilience` as an unused, policy-violating dead package reference. Re-verification (grepping for the actual method name `AddStandardResilienceHandler` rather than a near-miss string) showed the package **is** used — on 2 of 3 clients — and **is** documented in `about.html` under the `Microsoft.Extensions.*` wildcard row. The real, narrower finding (D2, the `"llm"` client's gap) replaces that incorrect draft finding. Flagging this here per your instruction to critically verify claims before reporting them — this is exactly the kind of premature conclusion the exhaustive-verification requirement is meant to catch.

---

## Detailed Findings

### D1 — Transitive `uuid` vulnerability + duplicate versions in committed lockfile

**Evidence.**
```
$ npm audit --audit-level=low   (run against the committed package-lock.json, not a fresh resolve)
uuid  <11.1.1 — Missing buffer bounds check in v3/v5/v6 when buf is provided
  GHSA-w5hq-g745-h8pq
  node_modules/uuid, node_modules/yggdrasil/node_modules/uuid
  via @azure/msal-node <=5.1.4 → prismarine-auth → minecraft-protocol → mineflayer
6 moderate severity vulnerabilities
```
Confirmed directly in the committed `package-lock.json` (not an artifact of a fresh install): `node_modules/uuid` resolves to `8.3.2`, and a second, independently-nested copy under `node_modules/yggdrasil/node_modules/uuid` resolves to `10.0.0` — both below the patched `11.1.1`. Two different vulnerable versions of the same package in one dependency tree is itself a minor bloat signal (npm couldn't dedupe them because `yggdrasil` pins a range incompatible with the top-level resolution).

`MineflayerAdapter/index.js`'s `botOpts` (line 312) never sets `auth:` when calling `mineflayer.createBot()`, so mineflayer's default auth mode applies. This subtree (`prismarine-auth`/`@azure/msal-node`) exists specifically to support Microsoft-account authentication — if this bot only ever connects to offline-mode/dev servers (plausible for a Minecraft-agent research project), the entire vulnerable subtree may be unreachable dead weight that could be avoided by explicitly setting `auth: 'offline'`.

**Recommendation.**
1. Confirm with the team whether Microsoft-account auth is actually exercised at runtime.
2. If not: set `auth: 'offline'` explicitly in `botOpts` (self-documents intent regardless of dependency-tree effects) and evaluate whether a lighter-weight mineflayer configuration avoids pulling `prismarine-auth` at all.
3. If Microsoft auth **is** needed: add an `"overrides"` entry in `package.json` to force `uuid@^11.1.1` across the tree (npm supports overriding transitive versions without waiting on upstream `@azure/msal-node`/`mineflayer` bumps), then re-run `npm audit` to confirm the advisory clears.
4. Either way, this is a good candidate for a recurring CI step (`npm audit --audit-level=moderate` as a non-blocking-initially / later-gating check) given the project already has a documented package-vetting culture (`Data/Pages/policies/package-vetting.md`) on the C# side but nothing equivalent wired for the Node side.

**Confidence: 93%.** `npm audit` output and lockfile version pins are objective; the "is Microsoft auth actually used" question is an open question for the team, not something resolvable from static analysis alone (see Open Questions).

---

### D2 — `"llm"` HttpClient lacks the resilience handler its siblings have

**Evidence** (`WebUI.Blazor/Program.cs`):
```csharp
// line 105-113
builder.Services.AddHttpClient("memorysmith", (sp, http) => { ... })
    .AddStandardResilienceHandler(); // Sprint 53 (TSK-0194): retry with backoff + circuit breaker

// line 121-131
builder.Services.AddHttpClient("memorysmith-world", (sp, http) => { ... })
    .AddStandardResilienceHandler(); // Sprint 53 (TSK-0194): retry with backoff + circuit breaker

// line 231-234 — no .AddStandardResilienceHandler() call
builder.Services.AddHttpClient("llm", http =>
{
    http.BaseAddress = new Uri(chatOpts.ResolvedBaseUrl);
    http.Timeout     = TimeSpan.FromSeconds(chatOpts.LlmTimeoutSeconds + 2);
});
```
TSK-0194's own scope statement names exactly `"memorysmith"` and `"memorysmith-world"` — it never claimed to cover `"llm"`, so this isn't a false-completion in the TSK-0292/TSK-0309 sense from the prior report; it's a scope gap that was never revisited. Given the LLM client is the one calling out to a potentially rate-limited or transiently-unavailable third-party API (Anthropic/OpenAI-compatible/Gemini/Ollama, per `Agent.Planning/Llm/`), it is arguably the highest-value candidate for retry-with-backoff + circuit-breaking of the three — currently it has none.

**Recommendation.** Add `.AddStandardResilienceHandler()` to the `"llm"` client registration. Note the existing per-provider `CancellationTokenSource`+`CancelAfter(options.LlmTimeoutSeconds)` pattern (see D7) already imposes a hard timeout — check for interaction/double-timeout effects between that and the resilience handler's own timeout policy before shipping (the resilience handler's default `TotalRequestTimeout` may need tuning or disabling in favor of the existing per-call cancellation, to avoid two independent timeout mechanisms racing).

**Confidence: 88%.** The gap and the scope-language are directly quoted from the source; the "should this get resilience too" recommendation is a design judgment, hence not higher.

---

### D3 — Type-4 duplicate LLM-response JSON extraction, with a real robustness gap

**Evidence.**

`Agent.Planning/LlmChatInterpreter.cs:537–570` (`ParseDecision`):
```csharp
private static readonly Regex CodeFenceRegex =
    new(@"```(?:json)?\s*(?<body>[\s\S]*?)```", RegexOptions.Compiled | RegexOptions.IgnoreCase);
private static readonly Regex BraceRegex =
    new(@"\{[\s\S]*\}", RegexOptions.Compiled);
...
var json = CodeFenceRegex.IsMatch(content) ? CodeFenceRegex.Match(content).Groups["body"].Value : content;
var m = BraceRegex.Match(json);
if (!m.Success) return TryParseTruncatedJson(json, logger);   // Sprint 20 salvage path
using var doc = JsonDocument.Parse(m.Value);
```

`Agent.Planning/LlmEvaluatorImpl.cs:370–374` (`ExtractJson`):
```csharp
internal static string? ExtractJson(string text)
{
    var start = text.IndexOf('{');
    var end   = text.LastIndexOf('}');
    return start >= 0 && end > start ? text[start..(end + 1)] : null;
}
```

Both methods solve "pull the JSON object out of an LLM's free-text response" — the textbook definition of Type-4 (functionally-equivalent, differently-implemented) duplication. But they are **not equally capable**: `ExtractJson` has no fence-stripping step and no truncated-JSON recovery. This directly explains, and sharpens, the prior report's F5 finding (silent swallow with zero logging on parse failure in the evaluator directive path) — the reason that catch block gets exercised at all in cases where the sibling parser would have succeeded is this capability gap.

**Recommendation.** Extract the fence-strip + brace-match (+ optionally the truncation-salvage) logic into a shared `Agent.Core` (or `Agent.Planning`) utility — e.g. `LlmJsonExtractor.Extract(string responseText)` — and have both `ParseDecision` and `ExtractJson`'s caller use it. This is a natural companion fix to ship alongside the F5 logging fix from the prior report, since they're the same code path.

**Confidence: 91%.**

---

### D4 — Verbatim duplicate: RLE-compression of tool/action lists

**Evidence.** `jscpd` clone (21 lines, csharp):
- `Agent.Planning/Decomposition/BuildGoalDecomposer.cs:106` — `private static string RleCompress(IReadOnlyList<ActionData> actions)`
- `WebUI.Blazor/AgentBackgroundService.cs:3057` — `private static string RleCompressActions(IEnumerable<string> tools)`

Both implement the identical "collapse consecutive repeated tool names into `Name×N`" algorithm (`StringBuilder`, `current`/`count` state, identical `if (sb.Length > 0) sb.Append(" → ")` join logic) — differing only in whether the input is `ActionData` objects or raw `string`s.

**Recommendation.** Extract a single generic helper, e.g. `Agent.Core.RleFormatter.Compress<T>(IEnumerable<T> items, Func<T, string> selector)`, and have both call sites use it (`BuildGoalDecomposer` passing `a => a.Tool`, `AgentBackgroundService` passing an identity selector). Mechanical, low-risk, ~15-minute fix.

**Confidence: 95%** — jscpd-flagged and hand-verified character-for-character.

---

### D5 — Verbatim internal duplicate: chat-response chunking/enqueue block

**Evidence.** `jscpd` clone (22 lines) entirely within `WebUI.Blazor/AgentBackgroundService.cs`, at ~line 1575 (deterministic-chat branch) and ~line 1702 (end of LLM-driven switch branch):
```csharp
if (pendingResponse is not null)
{
    // Sprint 54 (TSK-0199): split long responses into multiple in-game chat messages
    var chunks = SplitResponse(pendingResponse, _chatMaxResponseLength);
    foreach (var chunk in chunks)
    {
        _queue.Enqueue(new ActionData { Tool = "Chat", Arguments = { ["message"] = chunk } });
        _ = PushChatToDashboardAsync("bot", botName, chunk);
    }
    if (chunks.Count > 1)
        logger.LogInformation("[chat] response split into {Count} messages (limit={Limit} chars)",
            chunks.Count, _chatMaxResponseLength);
}
```
Byte-for-byte identical logic (the first copy additionally sets `pendingResponse = null;` afterward — a real, if minor, behavioral difference worth checking whether it matters at the second call site too).

**Recommendation.** Extract to a private `EnqueueChatResponse(string? pendingResponse)` method; call from both branches. Since this is duplication *within a single class*, it's the lowest-risk fix in this entire report — no cross-file coordination needed — and a good first PR to pair with the F1 decomposition work from the prior report (one fewer responsibility to carry when ABS eventually gets split up).

**Confidence: 95%.**

---

### D6 — Duplicated coal-fuel-need arithmetic in `HtnTaskLibrary.cs`

**Evidence.** Two independent occurrences of the same domain formula:
```csharp
// ~line 232 (Sprint 15 P0, iron-ingot task)
var coalNeeded = Math.Max(1, (needIngots + 7) / 8);
...
// ~line 311 (Sprint 44, generic smelt task)
var coalNeeded = Math.Max(1, (count + 7) / 8);
```
Both comments independently explain "1 coal smelts up to 8 items; use ceiling(needed/8) coal" — i.e., the domain knowledge itself was re-derived and re-commented twice, 29 sprints apart, rather than looked up from a shared source. This sits right next to `SmeltableMapping.GetInputBlock(...)`, a utility TSK-0082 already created for the adjacent "which block do I mine for this smeltable" concern — the fuel-math is the natural extension of that same utility that TSK-0082 didn't cover.

**Recommendation.** Add `SmeltableMapping.CoalNeededFor(int itemCount) => Math.Max(1, (itemCount + 7) / 8)` (or a similarly-named method on whatever class ends up owning "furnace math"), and replace both inline formulas with a call to it. Low-risk; also gives a single place to update if a future feature (e.g. blast furnace, which smelts ores in half the time/fuel) needs to change the ratio.

**Confidence: 90%.**

---

### D7 — LLM provider classes share un-factored boilerplate

**Evidence.** `AnthropicProvider`, `OllamaProvider`, and `OpenAICompatibleProvider` (jscpd-flagged pairwise clones, 12–15 lines each) all share:
```csharp
public sealed class XProvider(HttpClient http, ChatOptions options, ILogger<XProvider>? logger = null) : ILlmProvider
{
    public string ProviderName => "...";
    public bool IsAvailable => options.LlmEnabled && /* provider-name match */;

    public async Task<string?> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        if (!IsAvailable) return null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.LlmTimeoutSeconds));
        try { ... } // provider-specific request/response handling from here
```
This constructor/availability/timeout preamble is copy-pasted per provider (a 4th, `GeminiProvider`, almost certainly follows the same shape by naming/structural convention, though it wasn't one of jscpd's flagged pairs — likely just below the min-line threshold). Any future change to the timeout-linking or availability-check semantics (e.g., adding a per-provider circuit breaker, or fixing a bug in how `IsAvailable` is computed) requires 4 coordinated edits today — classic Shotgun Surgery risk.

**Recommendation.** Extract an `abstract class LlmProviderBase(HttpClient http, ChatOptions options, ILogger? logger) : ILlmProvider` owning the constructor fields, the `CreateLinkedTokenSource`+`CancelAfter` setup (as a protected helper method the subclass calls, e.g. `protected CancellationTokenSource CreateTimeoutLinkedSource(CancellationToken ct)`), and a virtual/abstract `IsAvailable` that subclasses can implement with just their provider-name predicate. This is a template-method refactor, not a rewrite — each provider keeps its own request/response DTOs and parsing logic untouched.

**Confidence: 85%.** Pattern is clear from 3 confirmed instances; the 4th (`Gemini`) is inferred by convention, not independently verified, hence the confidence isn't higher.

---

### D8 — Dead tombstone file `Agent.Planning/IntentDraft.cs`

**Evidence.** Full file contents:
```csharp
// Sprint 39 P1-C: IntentDraft moved to Agent.Core so that IAgentRuntimeComponent
// (Agent.Core.Runtime) can reference it without a circular project dependency.
// All Agent.Planning code reaches it via the existing "using Agent.Core;" directive.
// See Agent.Core/Models/IntentDraft.cs for the full definition.
```
Zero types, zero executable code — a comment-only file left behind after a Sprint 39 move. `Agent.Core/Models/IntentDraft.cs` has the real, actively-used `record IntentDraft`.

**Recommendation.** Delete the file. Git history (and this audit) already document why it moved; there's no ongoing value in leaving a breadcrumb file in a compiled project.

**Confidence: 97%.**

---

### D9 — `IntentDraft.Intent` primitive obsession + silent unrecognized-value fallthrough

**Evidence.** `Agent.Core/Models/IntentDraft.cs`'s doc comment: `<param name="Intent">Semantic intent: "gather" | "build" | "craft" | "navigate" | "cancel" | "status" | "help" | "conversation" | "clarify" | "ignore"</param>` — ten string literals, enforced nowhere by the type system. `IntentManager.BuildGoalRequest` (lines 46–70) switches on this string and, for any value outside its 4 explicitly-handled cases (`gather`/`place`/`craft`/`build`; `navigate` is handled with its own coordinate-null fallthrough), silently `return null` — no log line, no metric.

This is the same *shape* of silent-swallow issue as prior-report F5/this-report D3 (a "no signal when something unexpected happens" pattern), but at the type-contract layer rather than the JSON-parsing layer — worth fixing as part of the same defensive-observability pass.

**Recommendation.**
1. Add a `default: logger.LogWarning("Unrecognized intent {Intent} from LLM", intent); break;` (or equivalent) to make silent fallthrough visible.
2. Longer-term: consider a `JsonStringEnumConverter`-backed `enum IntentKind` (or a closed discriminated union, consistent with how `EvaluationDirective` is already modeled elsewhere in this codebase) instead of a raw string, so the compiler — not a comment — enforces the valid-value set, and an unrecognized value from the LLM becomes a deserialization failure that's caught explicitly rather than a value that silently satisfies a `string` parameter and then falls through a switch.

**Confidence: 87%.**

---

### D10 — "Sprint N" comment-as-changelog anti-pattern (quantified)

**Evidence.**
```
$ grep -rho "Sprint [0-9]\+" --include=*.cs (excluding Tests) | wc -l
1080   across 94 files
$ (per-file count, top 5)
272  WebUI.Blazor/AgentBackgroundService.cs
 50  WebUI.Blazor/Program.cs
 44  Agent.Planning/LlmChatInterpreter.cs
 43  Agent.Planning/HtnTaskLibrary.cs
 35  Agent.Core/WorldStateProjector.cs
```
For a 20,609-line production codebase, that's roughly one "Sprint N (TSK-xxxx): ..." narration comment every 19 lines on average, and one every ~15 lines in `AgentBackgroundService.cs` specifically. Many of these are pure historical narration ("Sprint 4a/4b: SignalR push for...") that describes *when* something was added, not *why* the current code behaves as it does — information that git blame/log already preserves durably, and which the team already has a dedicated, git-tracked home for (`Data/Pages/Handoffs/`).

This is a recognizable pattern in AI-assisted/AI-paired development: each incremental change gets a self-narrating comment so the next session (human or AI) can orient quickly, but the comments are rarely pruned once their information is superseded by newer comments describing later changes to the same code — so they accumulate indefinitely rather than being replaced. This measurably inflates file size and reading effort, and it directly compounds the F1 god-class-readability finding from the prior report: a meaningful fraction of `AgentBackgroundService.cs`'s 4,019 lines is sprint-changelog narration rather than logic.

**Recommendation.**
1. Establish a convention: comments should explain *current, non-obvious rationale* ("why does this check exist," "why this order matters"), not *when* something was implemented — the latter belongs in commit messages / handoff docs, both of which already exist for this team.
2. As a cleanup pass (could piggyback on the F1 decomposition work), prune "Sprint N" markers whose described change is now simply baseline behavior with no ongoing decision-relevance (e.g., "Sprint 4a/4b: SignalR push for StatusUpdated..." — 56 sprints later, this is just how the system works, not a decision anyone will revisit).
3. Not urgent, not risky — but worth tracking as a backlog item given it's the single most repeated smell in the entire codebase.

**Confidence: 89%.** The count is exact; the severity/recommendation is a reasoned design opinion.

---

### D11 — Additional jscpd hits flagged for triage (not hand-verified to the same depth)

In the interest of exhaustiveness and honesty about verification depth, these clones were mechanically detected but not manually confirmed as true positives to the same standard as D4–D6 above (time-boxed to keep this pass moving):

| Files | Lines | Note |
|---|---|---|
| `Agent.Core/WorldStateProjector.cs` (self-clone) | 279–287 vs 398–406 | Same file; likely two similar diff-application branches |
| `Agent.Planning/GoalFactory.cs` (self-clone) | 217–226 vs 229–238 | Visually confirmed as a defensible `int`/`int?` overload pair (`GetInt` for non-nullable and nullable default) — **likely a false-positive for "problematic" duplication**, included for completeness only |
| `Agent.Planning/HtnTaskLibrary.cs` (3 more self-clone pairs beyond D6) | 234–243/311–320, 500–508/581–589, 508–516/589–597 | Suggests the file's many per-resource task-generation methods (one per craftable/gatherable/smeltable) share an un-extracted "check inventory → compute deficit → add mining/gathering action" shape across most of its ~40 task methods — a template-method or small builder abstraction could plausibly collapse a meaningful fraction of this file's size, but sizing that opportunity needs a dedicated pass over the whole file, not a spot-check |
| `Agent.Core/CommonMinecraftBlocks.cs` vs `Agent.Planning/HtnTaskLibrary.cs` | 3 clone pairs, 9–13 lines each | Likely duplicated block-alias/lookup lists between a "common blocks" reference file and task-library logic; worth checking whether `HtnTaskLibrary` should simply reference `CommonMinecraftBlocks` instead of restating subsets of it |

**Confidence: 60%** on the "this represents a real, worthwhile consolidation" claim for the unverified rows — high confidence the *clones exist* (jscpd is deterministic), lower confidence on severity/actionability without a closer read.

---

## Assumptions & Open Questions

1. **D1's severity depends on an unanswered question**: does this bot ever authenticate via Microsoft account (online-mode servers), or does it only ever run against offline-mode/dev servers? This determines whether the fix is "stop pulling this dependency subtree entirely" (bigger win) or "just override the vulnerable transitive version" (smaller, safer, faster). Not resolvable from static analysis — needs a one-line answer from whoever configured the deployment environment.
2. **D2's interaction between the resilience handler's own timeout and the existing per-provider `CancellationTokenSource`+`CancelAfter`** (D7) was flagged but not tested — adding `.AddStandardResilienceHandler()` to the `"llm"` client without checking this could produce confusing double-timeout behavior. Recommend testing in a lower environment before shipping.
3. **No `dotnet build`/Roslyn analyzer run was possible** in this sandbox (no `nuget.org` network access) — same limitation as the prior report. All C# findings here are from `jscpd` + manual read, not compiler diagnostics.
4. **D7's inclusion of `GeminiProvider`** is inferred from the pattern shared by the other 3 providers and the existence of the type in the `Llm/` folder — it was not independently read line-by-line against the other three in this pass. Verify before scoping a refactor ticket.
5. **D11's rows are explicitly lower-confidence** — see per-row notes. `GoalFactory.cs`'s clone in particular is probably fine as-is (a common, low-risk C# overload pattern) and is included only for completeness per the "exhaustive" instruction, not because it's a recommended fix.

---

## Suggested Sequencing (additive to the prior report's sequence)

These slot naturally alongside the prior report's plan — D4/D5/D8 are same-tier-of-effort as F7 (trivial, mechanical); D3/D9 pair directly with the prior report's F5 fix (same code area, same PR is reasonable); D1/D2/D6/D7 are independent, moderate-effort items that can be picked up in any order based on team priority.

1. D8 (delete tombstone file) — trivial.
2. D4, D5 (extract RLE helper; extract chat-chunk-enqueue helper) — mechanical, same-class/adjacent-file, near-zero risk.
3. D3 + D9, alongside prior-report F5 — same LLM-parsing subsystem, worth one coordinated PR: shared JSON extractor, evaluator logging, intent-fallthrough logging.
4. D6 (extract coal-math into `SmeltableMapping`) — small, standalone.
5. D2 (add resilience handler to `"llm"` client) — verify timeout interaction first (see Open Questions #2).
6. D1 (uuid/supply-chain) — needs a team decision first (see Open Questions #1), then either an `overrides` entry or an `auth: 'offline'` config change.
7. D7 (LLM provider base class) — moderate refactor, do after D3 lands so the shared JSON-extraction utility can live in the same base/utility layer if that turns out to be a clean fit.
8. D11 rows — triage individually; `HtnTaskLibrary.cs`'s broader pattern (3 unverified clone pairs + D6) may justify a dedicated file-level pass once D6 proves out the "shared furnace/task math" extraction pattern.
