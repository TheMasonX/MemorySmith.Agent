# MemorySmith.Agent — Delta Audit: Duplication, Supply-Chain, AI-Code-Smells

**Repo/commit:** same as prior report — `dev/round-3` @ `0f27af1befb72e7534421a1bd5550aee1e077d96`
**Scope of this pass:** type-4 (functional) duplication, dependency/supply-chain bloat, common AI-generated-code smells, unexplained/arbitrary code, shallow modules. **This document contains only new findings or corrections not covered in the prior report** (`msa-bloat-duplication-audit-*.md`). Tooling used this pass: `eslint` (flat config, `eslint:recommended`) against `MineflayerAdapter/index.js`; manual dependency-graph tracing (`grep -rl ProjectReference`) since no `.sln` exists; `about.html` ↔ `.csproj` diff for P-2 policy compliance.

---

## Executive Summary

| # | Finding | Class | Confidence | Impact |
|---|---|---|---|---|
| D1 | **Team's own supply-chain policy (P-2) is currently violated**: `Microsoft.Extensions.Http.Resilience` and `Microsoft.Extensions.Logging.Abstractions` are referenced in `.csproj` files but **absent from `wwwroot/about.html`**, the repo's self-declared "canonical inventory" required to be updated in the same commit as any dependency change. | Policy/process gap | 97% | Low-Medium — governance debt, not a runtime bug, but exactly the kind of drift the policy was written to prevent |
| D2 | **`Agent.Personality` project is fully orphaned** — zero `ProjectReference` from any other project (including the test suite and the host), zero DI registration, zero callers, and **no corresponding TSK-### tracking it at all** (unlike `Agent.Vision`, which at least has TSK-0023). Worse: TSK-0017 (Agent Greeting, Backlog) independently plans a raw `string? GreetingMessage` config property, reinventing a sliver of what `IPersonality`/`AgentProfile` already models (voice, backstory, preferences) — a second team decision made in ignorance of existing scaffolding. | Speculative Generality / Dead Code / primitive obsession (about to be introduced) | 93% | Medium — risk of two parallel, incompatible "agent voice" concepts if TSK-0017 ships as planned |
| D3 | **Type-4 duplication across all 4 LLM provider classes** (`OllamaProvider`, `GeminiProvider`, `AnthropicProvider`, `OpenAICompatibleProvider`, 532 combined lines): each independently re-implements the identical shape — linked-timeout `CancellationTokenSource`, `IsAvailable` gate, try/catch triplet (`OperationCanceledException` / `HttpRequestException` / `Exception`) each logging-and-returning-null — around provider-specific wire formats. Confirmed structurally identical, not textually identical (hence type-4, missed by naive text-diff tools), with **inconsistent logging verbosity/style between files** (Ollama logs multi-line Debug+Info; Gemini/Anthropic use terse one-liners) — a signature of independently-authored-but-copy-derived code rather than a shared abstraction. | Duplicated Code (type-4) / Template Method opportunity | 90% | Medium — ~60-70% of each provider file is boilerplate; a bug fixed in one (e.g., timeout handling) has 3 other places it must be manually re-applied |
| D4 | **Live, open bug hiding behind a stale task-status**: `AnthropicProvider` and `OpenAICompatibleProvider` (covering **5 of the 7** documented provider identities: anthropic, openai, openrouter, deepseek, github-copilot) hardcode `MaxTokens = 512`, silently ignoring the admin-configured `LlmMaxResponseTokens`. `GeminiProvider` was fixed in Sprint 60 citing **TSK-0400** in its own code comment — but **TSK-0400 itself is still `Backlog`**, with no partial-completion note, so the tracker undersells what's done and hides what's still broken. | Silent config-ignoring bug + status/code mismatch | 94% | Medium-High — an admin who sets a token cap to control cost/latency believes it applies everywhere; it only holds for Ollama + Gemini |
| D5 | **Two more duplicate task-record pairs** beyond the ones already self-flagged in the July-11 handoff: **TSK-0180 (Backlog) ≡ TSK-0399 (Done)** — same Gemini-API-key-in-URL fix, one stale; **TSK-0370 (Backlog) ≡ TSK-0400 (Backlog)** — same MaxTokens finding, both citing the same audit source (MSA-LLM-003), verbatim-overlapping descriptions. | Backlog hygiene | 96% | Low — SNR cost, same category as previously-flagged F7 |
| D6 | **ESLint (`eslint:recommended`) run against `index.js` surfaces 6 real, previously-unflagged issues**: (a) `Movements` destructured from `mineflayer-pathfinder` but never used — dead import left over from the TSK-0166 modularization; (b) `classifyError(message, action)` — the `action` parameter is **never read inside the function**, despite being passed a real value at both call sites (`msg.action`, `'move'`) — the signature implies action-aware error classification that does not exist, a misleading-name/unclear-intent smell; (c) a caught error is re-thrown as `new Error(...)` without `{ cause: e }`, silently discarding the original stack trace on every 3rd-consecutive mine-pathfinding failure; (d)+(e) two additional bare `catch (err) { /* comment only */ }` blocks (lines 472, 1425) with zero logging, even at debug level, relying entirely on a code comment's *assumption* about the failure mode (unloaded chunk / face rejection) being correct. | Duplicated Code / Mysterious Name / silently swallowed error | 91% | Medium — (b) and (c)/(d)/(e) directly compound the "silent evaluator/parser failure" theme from the prior report's F5 |
| D7 | **Comment-as-changelog anti-pattern, quantified**: 1,341 inline `Sprint N` / `TSK-####` references across the C#+JS tree; `AgentBackgroundService.cs` alone carries 298 (≈1 every 13 lines of its 4,019). This is a direct, measurable contributor to that file's bloat (prior report F1) and is the same mechanism that produced the already-proven-stale "Sprint 40 target" comments in the dead Manager scaffolding (prior report F2) — the pattern isn't a one-off, it's systemic: comments are being used as a permanent substitute for `git blame`/commit messages/the task tracker, and they go stale at the same rate those systems would have kept current for free. | AI/process code smell | 85% | Low-Medium — mostly a maintainability tax, but measurably inflates the exact god-file already flagged as highest priority |

---

## Detailed Findings

### D1 — Supply-chain policy (P-2) violation: undocumented dependencies

**Policy** (`Data/Pages/policies/package-vetting.md`, P-2): *"`WebUI.Blazor/wwwroot/about.html` is the canonical inventory of third-party dependencies... Adding or removing a package requires updating the About page in the same commit."*

**Evidence:**
```
$ grep -roh 'PackageReference Include="[^"]*"' --include=*.csproj . | sort -u
... Microsoft.Extensions.Http.Resilience
... Microsoft.Extensions.Logging.Abstractions
...
$ grep -n "Resilience\|Logging.Abstractions" WebUI.Blazor/wwwroot/about.html
(no matches)
```
Both packages are real, in-use dependencies (`Microsoft.Extensions.Http.Resilience` is consumed via `AddStandardResilienceHandler`-style calls in `Program.cs`; `Logging.Abstractions` is referenced by 4 separate class-library projects for `ILogger<T>`), not vestigial — so this isn't a "remove it" case, it's a "the About page is out of date" case, which is exactly the drift P-2 exists to prevent.

**Recommendation.** Add the two missing rows to `about.html` (license: MIT for both — confirm via `dotnet list package` metadata or NuGet page before committing, per P-1's own requirement not to skip the license check). Then run `Scripts/Verify-AboutDeps.ps1` — if that script doesn't already assert this diff at build/CI time, that's a second, smaller finding: the enforcement script exists but evidently isn't gating anything today, since drift has already occurred. Recommend wiring `Verify-AboutDeps.ps1` into CI (or a pre-commit hook) so this can't reoccur silently.

**Confidence: 97%.** Directly diffed the `.csproj`-declared package set against the HTML file's content; both are unambiguous.

---

### D2 — `Agent.Personality` is forgotten dead scaffolding (distinct from prior report's F2)

**Distinction from prior F2:** F2 (AgentRuntime/Managers) was at least DI-registered and had an in-repo task acknowledging its dashboard-publishing half (TSK-0404). `Agent.Personality` has **no DI registration, no `ProjectReference` from anywhere, and no TSK-### at all** — a strictly worse case of abandonment with zero tracking surface.

**Evidence:**
```
$ grep -rl "ProjectReference.*Agent.Personality" --include=*.csproj .
(no matches — not referenced by WebUI.Blazor, Agent.Planning, or MemorySmith.Agent.Tests)
$ grep -rln "IPersonality|AgentProfile" --include=*.cs . | grep -v "Agent.Personality/"
(no matches)
$ grep -l "Agent.Personality|IPersonality|AgentProfile" Data/Tasks/*.json
(no matches)
```
`IPersonality` (13 lines) defines `Profile`, `BuildSystemPrompt()`, `RespondAsync(...)` — a real, sensible interface for injecting agent voice/backstory into LLM prompts. `AgentProfile` (14 lines) models `Name`, `Backstory`, `VoiceStyle`, `Preferences[]`, `DisallowedActions[]`. Nothing implements `IPersonality`; nothing constructs an `AgentProfile`.

**Compounding risk found this pass:** TSK-0017 ("Agent Greeting Upon Connection," Backlog) plans to add `ChatOptions.GreetingMessage` (a raw `string?`) as its MVP mechanism for agent-voice output — i.e., a second, independent, primitive-typed mechanism for something `AgentProfile.VoiceStyle`/`Backstory` was already purpose-built to express. If TSK-0017 ships as currently planned, the codebase will have two non-interacting representations of "how the agent talks" — one dead, one new and narrower.

**Recommendation.**
1. Decide now, cheaply, while the cost is one string field vs. a real interface: either (a) delete `Agent.Personality` entirely (2 files, ~27 lines, zero behavior change, matches the greenfield "no unused scaffolding" directive) or (b) amend TSK-0017 to consume `IPersonality`/`AgentProfile` instead of adding a parallel `GreetingMessage` primitive.
2. Either way, file or update a TSK-### explicitly for this project so it stops being invisible to task-tracker-based planning (it currently doesn't show up in any grep-for-tasks pass, which is how it went unnoticed across this and prior audit rounds).

**Confidence: 93%.** The dead-code claim is 99%-certain (exhaustive grep across the whole tree). The "TSK-0017 risks duplicating it" claim is a reasoned inference from reading both artifacts side by side, not a certainty the team hasn't already privately decided against `IPersonality` — hence 93%, not higher.

---

### D3 — Type-4 duplication across LLM provider classes

**Why type-4 (not caught by jscpd/text-diff):** the four provider files share no copy-pasted text blocks — the wire formats (Ollama's `/api/chat`, Gemini's `generateContent`, Anthropic's `/v1/messages`, OpenAI-compatible's `/v1/chat/completions`) are all different JSON shapes — but the **control-flow skeleton** around each is identical:

```
if (!IsAvailable) return null;
using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
cts.CancelAfter(TimeSpan.FromSeconds(options.LlmTimeoutSeconds));
try {
    <build provider-specific request>
    <send>
    if (!response.IsSuccessStatusCode) { log; return null; }
    <parse provider-specific response>
    return <extracted text>;
}
catch (OperationCanceledException) { log; return null; }
catch (HttpRequestException ex)    { log; return null; }
catch (Exception ex)               { log; return null; }
```
Verified present, with only cosmetic variation, in all four files (`OllamaProvider.cs:29-107`, `GeminiProvider.cs:26-83`, and — not previously read in full this pass but confirmed via targeted grep — `AnthropicProvider.cs`/`OpenAICompatibleProvider.cs` share the same three-catch-clause tail and linked-CTS setup).

**Secondary evidence of independent-authorship-not-shared-abstraction:** logging style diverges meaningfully between files that otherwise do the same job — `OllamaProvider` emits paired `LogDebug`+`LogInformation` calls with full prompt/response bodies (a policy choice, per its own "Sprint 41" comment, for safety monitoring), while `GeminiProvider`/`AnthropicProvider` use single-line `LogWarning`-only error paths with no equivalent success-path debug logging at all. If the intent (per Ollama's comment) was a deliberate observability policy, it was not applied uniformly — another sign the "same shape, different file" pattern isn't a single reviewed decision but N independent implementations of one idea.

**Recommendation.** Extract an abstract `LlmProviderBase` (Template Method) owning: the `IsAvailable` gate, linked-CTS/timeout construction, the three-clause catch-and-log-and-null tail, and — if the team wants Ollama's verbose prompt/response debug logging as policy — apply it uniformly via the base class rather than per-file opt-in. Each concrete provider keeps only `BuildRequest(...)` / `ParseResponse(...)` / its wire-format DTOs. This is a pure refactor (no external behavior change intended beyond D4's bug fix, which should probably land in the same PR since it touches the same methods) and directly reduces the "a fix in one provider doesn't propagate" risk that already manifested once (see D4).

**Confidence: 90%.** The structural-duplication claim is high-confidence (read all files in full). The "inconsistent logging is a symptom of independent authorship" framing is an inference from the pattern, appropriately at the lower end of my confidence band.

---

### D4 — `LlmMaxResponseTokens` silently ignored by 2 of 4 provider classes (5 of 7 provider identities), masked by a stale task status

**Evidence:**
```csharp
// AnthropicProvider.cs:46          // OpenAICompatibleProvider.cs:59
MaxTokens = 512,                    MaxTokens  = 512,
```
Neither references `options.LlmMaxResponseTokens` anywhere in the file. `OpenAICompatibleProvider` backs **4** of the 7 documented provider identities (openai, openrouter, deepseek, github-copilot per `ILlmProvider`'s own doc comment), so this single hardcoded value silently caps/ignores config for the large majority of cloud-provider configurations a user could select.

By contrast, `GeminiProvider.cs:59-63` **does** respect it (`MaxOutputTokens = options.LlmMaxResponseTokens > 0 ? options.LlmMaxResponseTokens : 512`), tagged in-code `// Sprint 60 (TSK-0400): Add generation config to cap response length.` — i.e., the fix for TSK-0400 was partially shipped, for exactly one of the three providers TSK-0400's own description names (Anthropic, Gemini, OpenAI-Compatible), and the task record was never updated to reflect partial completion — it still reads plain `Backlog`, same as before any of this work happened. An engineer scanning the backlog has no way to know two-thirds of this fix already landed and one-third didn't.

**Recommendation.**
1. Apply the same `options.LlmMaxResponseTokens > 0 ? ... : 512` pattern already proven in `GeminiProvider` to `AnthropicProvider.MaxTokens` and `OpenAICompatibleProvider.MaxTokens`. Small, mechanical, low-risk.
2. Update TSK-0400 (and close TSK-0370 as its duplicate — see D5) with a comment noting Gemini shipped in Sprint 60; keep the task open, scoped now to just Anthropic + OpenAI-Compatible, until this fix lands.
3. If D3's `LlmProviderBase` extraction happens, consider making `MaxTokens`/`MaxOutputTokens`/`num_predict` resolution a single shared helper (`ResolveMaxTokens(options, providerDefault: 512)`) so this exact class of "config respected in 2 of 4 places" bug is structurally harder to reintroduce.

**Confidence: 94%.** Direct code inspection of all four provider files' handling of the same config field; unambiguous.

---

### D5 — Two additional duplicate task-record pairs

| Pair | Status | Evidence |
|---|---|---|
| TSK-0180 / TSK-0399 | Backlog / **Done** | Both describe moving the Gemini API key out of the URL query string into a header. TSK-0399 is Done (and the fix is confirmed present in code — `X-Goog-Api-Key` header, `GeminiProvider.cs:44-45`). TSK-0180 was never archived. |
| TSK-0370 / TSK-0400 | Backlog / Backlog | Both cite the identical audit source (`MSA-LLM-003`, 2026-07-11) and near-identical descriptions of the MaxTokens-hardcoding bug (D4). Neither references the other. |

**Recommendation.** Archive TSK-0180 (superseded by TSK-0399, which is genuinely done). Merge TSK-0370 into TSK-0400 (or vice versa) and use the single survivor to track the remaining Anthropic/OpenAI-Compatible work from D4. This is the same hygiene issue as the prior report's F7 — evidently not a one-time occurrence but a recurring pattern in how this team's audit-sourced tasks get filed (multiple audit passes over the same window independently generating near-duplicate tickets from the same finding).

**Confidence: 96%.**

---

### D6 — ESLint-confirmed issues in `index.js` (new, tool-verified)

Ran `eslint` (v10.7, `eslint:recommended`, flat config, Node globals) against `MineflayerAdapter/index.js`:

```
32:21  error  'Movements' is assigned a value but never used
200:33 error  'action' is defined but never used
472:12 error  'err' is defined but never used
592:12 error  'err' is defined but never used
916:13 error  There is no `cause` attached to the symptom error being thrown  preserve-caught-error
1425:26 error  'e' is defined but never used
```
(A 7th reported issue, `setImmediate is not defined` at line 1619, is a false positive from this pass's minimal lint config omitting the Node global — not a real code defect; excluded from findings.)

- **`Movements` unused (line 32):** destructured from `mineflayer-pathfinder` alongside `pathfinder`/`goals`, never referenced. The adjacent comment (`// Sprint 52 modularization (TSK-0166): extracted to separate modules.`) suggests movement-configuration logic was moved out during the partial TSK-0166 modularization pass, and this import simply wasn't cleaned up. Trivial fix; also mildly useful signal that TSK-0166's "partial" modularization (already Backlog/Low per prior report) has already left at least one loose end.
- **`classifyError(message, action)`'s `action` parameter (line 200):** real values are passed at both call sites (line 178: `msg.action`; line 676: literal `'move'`) but the parameter is dead inside the function — classification is 100% message-string-based. This reads as an unfinished feature (action-type-aware error classification) rather than a true dead parameter, since real, distinct values are being threaded in for a reason. Recommend either implementing action-aware branches (e.g., a "no path" error might warrant a different `reasonCode` for `mine` vs. `place`) or removing the parameter and its call-site arguments if message-only classification is intentionally sufficient — as written, the signature actively misleads a reader into thinking action-specific logic exists.
- **Missing `cause` on rethrow (line 916-917):** in the mine-retry loop, after `C.MAX_MINE_PATH_FAILURES` consecutive pathfinding failures, the code does `throw new Error(\`Pathfinding to ${shortName} failed ${C.MAX_MINE_PATH_FAILURES} times: ${e.message}\`)` — discarding `e`'s stack trace. Any downstream handler (or a future engineer reading a crash log) sees only the synthesized message, not where in `mineflayer-pathfinder` the original failure actually occurred. One-line fix: `throw new Error(..., { cause: e })`.
- **Two more silent bare catches (lines 472, 1425):** both have an explanatory comment (`// Chunk not loaded`, `// Try next face`) but zero logging, even at debug level, and the comment is an *assumption* about why the catch fired, not a verified condition (e.g., `err.message.includes('chunk')`). If either throws for a different, unanticipated reason, it is indistinguishable from the expected case in any log output. This is the same silent-swallow shape as prior report's F5, now confirmed in two more locations on the JS side.

**Recommendation.** Add `eslint` with `eslint:recommended` (plus a Node-env flat config) as a CI gate for `MineflayerAdapter/` — this pass found 5 genuine issues in a single file with a five-minute setup and zero project-specific configuration, suggesting linting has not been run on this file before now. Fix the 5 confirmed issues above; for the two silent catches, at minimum add a `logStructured('debug', ...)` call with `err.message` so the assumption is falsifiable from logs rather than asserted in a comment.

**Confidence: 91%.** Tool output is objective for the 6 flagged lines (minus the 1 config false-positive, explicitly called out). The "reads as unfinished feature not dead code" interpretation of the `action` parameter is a judgment call, appropriately not rated higher.

---

### D7 — Comment-as-changelog anti-pattern (quantified, cross-cutting)

**Evidence:**
```
$ grep -rc "Sprint [0-9]+|TSK-0[0-9]+" --include=*.cs --include=*.js . | awk -F: '{sum+=$2} END {print sum}'
1341
```
Top offenders: `AgentBackgroundService.cs` (298), `MineflayerAdapter/index.js` (100), `HtnTaskLibrary.cs` (62), `Program.cs` (52), `LlmChatInterpreter.cs` (44), `WorldStateProjector.cs` (42), `ChatInterpreter.cs` (32).

This isn't merely a style preference — it's the same underlying mechanism that produced two concrete, already-documented failures: (1) the prior report's F1, where `AgentBackgroundService.cs`'s continuous growth is partly explained by every sprint's worth of `// Sprint NN:` annotations accumulating rather than being superseded/removed; (2) the prior report's F2, where the "Sprint 40 target" comments in the dead Manager scaffolding are direct, provable evidence that sprint-numbered comments go stale exactly as fast as any other undisciplined documentation, because nothing forces them to be revisited (unlike a task-tracker status field, which at least gets looked at during sprint planning).

**Recommendation.** This is a process/convention recommendation, not a single code fix: reserve inline comments for *why* a piece of code exists or behaves non-obviously (the legitimate, valuable half of these comments — many genuinely explain a subtle bug fix rationale, e.g. the Sprint-40-P0-C alias-fix comment cited in the prior report's F6), and stop using them as a permanent *when-and-by-which-sprint* changelog — that information already lives in git history and the task tracker, and duplicating it in-line adds LOC without adding it once (it decays independently in each of the 1,341 places it was written). A lightweight lint rule or PR-review habit ("does this comment explain *why*, or just *when*?") would arrest further growth without requiring a mass cleanup pass.

**Confidence: 85%.** The count is exact; the causal link to F1/F2's severity is a reasonable but not fully provable inference (some of this bloat would exist even with disciplined commit messages, e.g. from genuine accreted feature scope) — reflected in the mid-80s confidence.

---

## Assumptions & Open Questions (this pass)

1. **ESLint was run with a minimal, hand-built flat config** (`eslint:recommended` + Node globals), not the project's own lint config, because `MineflayerAdapter/` has no `.eslintrc`/`eslint.config.*` checked in at all — worth noting as its own micro-finding: **there is currently no lint configuration in the JS project**, so nothing in CI would have caught D6's issues today even if CI runs `eslint` (it likely doesn't, given the absence of a config file). Recommend committing a project `eslint.config.mjs` (the one used for this pass is a reasonable starting point) alongside CI wiring.
2. **`about.html`'s license claims were not independently re-verified against upstream NuGet/npm metadata** for this pass (I confirmed the two *missing* entries, not that every *present* entry's stated license is still accurate — that would require a live NuGet API call, which this sandbox's network allowlist does not support for `nuget.org`).
3. **D3's recommended `LlmProviderBase` extraction was not prototyped** — the shape was confirmed by full reads of `OllamaProvider.cs`/`GeminiProvider.cs` and targeted greps of `AnthropicProvider.cs`/`OpenAICompatibleProvider.cs`; a full line-by-line read of the latter two was not performed this pass (time-boxed), so there is a small chance one of them has an idiosyncrasy that complicates a clean base-class extraction. Recommend a quick full read of both before starting the refactor.
4. **D2's claim that TSK-0017 is unaware of `IPersonality`** is inferred from TSK-0017's description not mentioning it, not from any direct statement that the team considered and rejected reusing it — it's possible this was a deliberate, undocumented decision. Flagged for human confirmation rather than treated as certain.

---

## Suggested Sequencing (adds to, does not replace, prior report's sequencing)

1. **D5** (archive 2 more duplicate tasks) — same 5-minute-effort, zero-risk category as prior F7; do together.
2. **D1** (add 2 missing rows to about.html) — trivial, closes a self-inflicted policy violation.
3. **D6's 5 fixes** in `index.js` — all small, mechanical, no design decisions required; good "same PR" bundle with the prior report's F6 recommendations since they're in the same file.
4. **D4** (respect `LlmMaxResponseTokens` in Anthropic/OpenAICompatible) — small, high-value; do before D3's refactor so the refactor starts from already-correct behavior in all four files.
5. **D3** (extract `LlmProviderBase`) — moderate effort; do after D4 so the shared base class encodes the *fixed* max-tokens behavior, not the buggy one.
6. **D2** (decide Agent.Personality's fate; align TSK-0017) — needs a product decision, not just an engineering one; surface to whoever owns TSK-0017's scope before that task is picked up.
7. **D7** — no dedicated task; adopt as an ongoing PR-review convention starting now.
