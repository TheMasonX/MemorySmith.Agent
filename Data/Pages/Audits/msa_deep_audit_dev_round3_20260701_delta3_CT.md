# MemorySmith.Agent — Deep Audit Round 4 (DELTA ONLY)

Report: msa_deep_audit_dev_round3_20260701_delta3_CT.md

Scope: **New findings only**, on top of rounds 1–3. This pass read, in full: all four `Agent.Planning/Llm/*Provider.cs` classes + `ILlmProvider`/`LlmProviderFactory`/`ChatOptions`, `Agent.Planning/ChatRateLimiter.cs`, and the shipped `WebUI.Blazor/appsettings.json` cross-referenced against the safety-related tasks already closed (TSK-0319 et al.).

---

## New Finding 1 — 3 of 4 LLM providers silently swallow HTTP failure responses; the 4th (sitting in the same folder) does it correctly

**Confidence: 90% (Verified — direct side-by-side code comparison)**

**Files:** `Agent.Planning/Llm/{Anthropic,Gemini,OpenAICompatible}Provider.cs` vs `OllamaProvider.cs`

All four providers share the same shape (`CompleteAsync` → build request → POST → check `IsSuccessStatusCode` → parse). `OllamaProvider` does the failure path right:

```csharp
if (!response.IsSuccessStatusCode)
{
    var body = await response.Content.ReadAsStringAsync(cts.Token);
    logger?.LogWarning("[ollama] HTTP {Status} from /api/chat: {Body}",
        (int)response.StatusCode, body.Length > 200 ? body[..200] : body);
    return null;
}
```

The other three all do this instead:

```csharp
if (!response.IsSuccessStatusCode) return null;
```

No status code, no response body, no log line at all. If a production deployment's Anthropic/Gemini/DeepSeek/OpenRouter API key expires, gets rate-limited (429), or a request is malformed (400) — which, given `LlmApiKey: null` is the checked-in default (see Finding 3) and must be supplied via environment/secrets override, is a very plausible first-deploy failure mode — the operator gets **silent `null` completions with zero diagnostic trail**, indistinguishable in the logs from "the LLM legitimately had nothing to say." The one provider that *does* log this detail (`OllamaProvider`) is the local/no-auth option least likely to ever hit an auth or rate-limit failure in the first place — the three providers most likely to need this diagnostic (all require API keys, all can be rate-limited) are the ones missing it.

This is textbook copy-paste drift: the four classes were clearly written from a shared template (identical `catch` blocks, near-identical structure), but the one meaningfully useful line in the reference implementation didn't make it into the other three.

**Recommendation:** Port `OllamaProvider`'s failure-body logging (status code + truncated body, `LogWarning`) into `AnthropicProvider`, `GeminiProvider`, and `OpenAICompatibleProvider`. This is a mechanical, low-risk, four-line-per-file change. Given how much of these four classes is identical (request-building boilerplate, three near-identical `catch` blocks, the `IsAvailable` pattern), also worth flagging as a **consolidation opportunity**: a shared base class or helper (`SendChatCompletionAsync(HttpRequestMessage, Func<T,string?> extractText, ...)`) handling the timeout/cancellation/logging/catch scaffolding once, with each provider only supplying its wire-format request/response DTOs and endpoint, would eliminate this entire class of "one provider gets a fix, the other three don't" drift going forward. File as `P2: Add HTTP-failure status/body logging to Anthropic/Gemini/OpenAICompatible providers (Ollama already has it)` and, separately, `P3: Consider extracting shared HTTP-call scaffolding across the four ILlmProvider implementations to prevent future logging/error-handling drift`.

---

## New Finding 2 — `GeminiProvider` puts the API key in the request URL instead of a header

**Confidence: 60% (Verified as written; real-world exposure risk is config-dependent and currently mitigated — see below)**

**File:** `Agent.Planning/Llm/GeminiProvider.cs:305`

```csharp
var endpoint = $"/v1beta/models/{options.LlmModel}:generateContent?key={options.LlmApiKey}";
```

This matches Google's documented API shape, so it's not "wrong" — but it means the API key is part of the request URI rather than a header. `IHttpClientFactory`'s default logging handlers log the outgoing request URI (typically at `Information`/`Debug` under the `System.Net.Http.HttpClient`/`Microsoft.Extensions.Http` categories). **Checked:** the shipped `appsettings.json` explicitly sets both of those categories to `"Warning"`, which suppresses that default request-URI logging today — so under the as-shipped configuration this is not currently an active leak. It becomes one the moment anyone bumps those two categories to `Information`/`Debug` for unrelated troubleshooting (e.g., diagnosing an HttpClient connectivity issue), which is exactly the kind of temporary, easy-to-forget-to-revert change operators make under pressure — and this project's own `appsettings.json` already runs everything else at `Debug` by default (see Finding 3), so the "just bump one more category" step is small.

Google's Generative Language API also accepts the key via an `x-goog-api-key` header as an alternative to the query parameter — switching to that removes the exposure vector categorically, independent of anyone's logging configuration, at effectively zero cost.

**Recommendation:** Switch `GeminiProvider` to send `x-goog-api-key` as a request header instead of embedding it in the URL. Given this project's own history with committed/leaked secrets (per prior audits), removing an available secret-in-URL pattern is cheap insurance. File as `P3: Move Gemini API key from URL query param to x-goog-api-key header (defense-in-depth, not currently actively exploited under shipped log config)`.

---

## New Finding 3 — The checked-in `appsettings.json` re-enables the exact settings TSK-0319 made safe-by-default

**Confidence: 88% (Verified — direct read of the only committed appsettings file + git-ignore list)**

**Files:** `WebUI.Blazor/appsettings.json`, `.gitignore`

TSK-0319 (`Done`, cited in round 1 as a verified fix) changed `ChatOptions.CommandExecutionEnabled`'s **code-level default** from `true` to `false`, explicitly calling it out as a breaking, safe-by-default change: *"BREAKING CHANGE: set `CommandExecutionEnabled: true` in Agent:Chat config to restore."*

The one and only `appsettings.json` committed to the repository (confirmed via `.gitignore` — only `appsettings.LocalDevelopment.json`, `appsettings.LocalOverrides.json`, and `appsettings.Secrets.json` are ignored; the base file is tracked) contains:

```json
"Chat": {
  "LlmEnabled": true,
  "CommandExecutionEnabled": true,
  ...
},
"Safety": {
  "AllowDestructiveCommands": true,
  "DeniedCommands": [ ... ]
}
```

So: `git clone` → `dotnet run` with no local overrides gives you an agent with **command execution enabled and destructive commands allowed**, immediately undoing TSK-0319's entire point. The deny-list is still populated and would still block the specific listed commands, so this isn't "no safety net at all" — but the code-level decision to default to the conservative posture is silently overridden by the file everyone actually runs with. If this `appsettings.json` is meant to represent Lucas's own known/trusted local server config (single operator, private world) rather than a "default template" for anyone cloning the repo, that's a reasonable choice — but nothing in the file or repo marks it as such, and it's the only appsettings.json that exists, so it reads as *the* default.

**Recommendation:** Either (a) rename/restructure so a genuinely conservative `appsettings.json` ships by default and this permissive configuration moves to `appsettings.LocalDevelopment.json` (already gitignored, already the documented "personal override" slot), or (b) if this file is intentionally "the maintainer's own trusted config, not a template," add a one-line comment/README note saying so, since a future contributor (or a future audit) reading this file in isolation will otherwise reasonably conclude the safe-by-default fix regressed. File as `P2: Checked-in appsettings.json re-enables CommandExecutionEnabled/AllowDestructiveCommands, undoing TSK-0319's default — move to a gitignored local-override file or document intent`.

---

## New Finding 4 — `ChatRateLimiter.Prune()` is fully implemented and documented as periodic maintenance, but has zero callers

**Confidence: 85% (Verified — grep confirms no call site anywhere outside its own definition)**

**File:** `Agent.Planning/ChatRateLimiter.cs`

```csharp
/// <summary>
/// Evicts per-player entries older than 5 minutes to prevent unbounded growth.
/// Safe to call from a background timer.
/// </summary>
public void Prune() { ... }
```

`ChatRateLimiter` is a DI singleton (`Program.cs:242`) that lives for the process lifetime and accumulates one `_playerTimes` dictionary entry per **distinct player username it has ever rate-limited a chat message for**. `Prune()` was clearly designed to be wired to a periodic timer to bound this growth — but a repo-wide grep for `.Prune()` returns zero matches outside the method's own definition. No background timer, no `IHostedService`, nothing calls it.

Practical impact is low for a small stable-roster private server (the dictionary tops out at "number of distinct people who ever talked to the bot," which is small and finite in that setting) but is a real, unbounded-growth vector for any more open deployment, and — more to this round's theme — it's another instance of "a correctly-designed safeguard that was never actually connected," the same shape of gap as the dead six-manager layer (round 1) and `DashboardPublisherImpl` (round 3), just much smaller in blast radius.

**Recommendation:** Wire `Prune()` into the existing periodic-tick infrastructure `AgentBackgroundService` already has for other maintenance (it already runs `InventorySyncLoopAsync` and similar periodic loops) — call it once every few minutes from there, or register a trivial `IHostedService`/`PeriodicTimer` for it. Small fix, closes the loop on a feature that's otherwise complete. File as `P3: Wire ChatRateLimiter.Prune() into a periodic timer — implemented but never called`.

---

## Confidence Summary (this delta only)

| Finding | Confidence | Basis |
|---|---|---|
| 1. 3/4 LLM providers missing HTTP-failure logging | 90% | Verified — direct code comparison across all 4 files |
| 2. Gemini API key in URL, not header | 60% | Verified as written; real exposure currently mitigated by shipped log-level config |
| 3. appsettings.json undoes TSK-0319 safe-by-default | 88% | Verified — only committed appsettings file, confirmed via .gitignore |
| 4. ChatRateLimiter.Prune() never called | 85% | Verified — zero call sites via grep |

## Not yet reviewed (disclosed scope limit — updated running list)

Still open from round 3: `Agent.Vision`, `Agent.Personality` (trivial, sampled, no anomalies), Dashboard DTO contracts (skimmed, no logic). Newly identified as unread this round: `ChatInterpreter.cs`, `LlmChatInterpreter.cs`, `LlmContextLogger.cs`, `ChatDistance.cs`, `ChatHistory.cs`, `ChatModels.cs`, `GoalFactory.cs`, `AliasRegistry.cs`, `Router/PlannerRouter.cs`, `Interfaces/*`, `HtnTask.cs`, `SmeltableMapping.cs`, and five of eight `Decomposition/*.cs` files (`BuildGoalDecomposer`, `CraftItemGoalDecomposer`, `DecomposerRegistry`, `PlaceBlockGoalDecomposer`, `SmeltGoalDecomposer`, `SurviveNightGoalDecomposer`, `TaskSequenceGoalDecomposer` — only `GatherGoalDecomposer`'s crafting-table interaction and `HtnTaskLibrary` have been read in depth so far), and all of `Agent.Planning/Goals/*.cs`. This is genuinely the largest remaining unread surface in the repo and the most likely place to still be hiding findings if a fifth pass is wanted — `PlannerRouter` and `ChatInterpreter`/`LlmChatInterpreter` in particular are safety/routing-adjacent and worth prioritizing next.
