# MemorySmith.Agent — Deep-Dive Audit: Delta Report #8
**Scope:** Full line-by-line read of `LlmChatInterpreter.cs` (710 lines, completed this pass), with targeted cross-verification against `AgentBackgroundService.cs`'s intent-handling switch and `SafetyOptions`/`DefaultDeniedCommands`. This closes out the last major unreviewed file identified in prior reports.
**Format:** Deltas only.

---

## Summary

| # | Type | Item | Severity | Confidence |
|---|------|------|----------|------------|
| 1 | New | The "remember a fact" feature only works when the LLM is degraded/unavailable — during normal operation, the LLM is explicitly instructed to acknowledge memory requests via `"conversation"` intent, which never persists anything | **High** | 92% |
| 2 | Correction/extension to `TSK-0258`/`TSK-0305` | The LLM's prompted combat workaround (`/summon lightning_bolt`) is not a degraded workaround — it is **structurally 100% non-functional**, in every game mode, because `/summon` is permanently on the unremovable safety-floor deny list | High | 95% |

---

## 1 — "Remember" only works when the LLM is down; under normal operation the bot falsely confirms memory it never stores (High, 92%)

**The prompt tells the LLM to do the wrong thing, by design:**

```
// LlmChatInterpreter.cs:347-350
MEMORY: You can remember facts across sessions. If a player says "remember
the password is taco" or "the base is at -200, 70, 300", the system stores
it. If a player asks about previously stored facts, the system recalls them.
Use intent="conversation" with a natural response for memory-related chat.
```

**But the only intent that actually persists anything is `"remember"`, not `"conversation"`:**

```csharp
// AgentBackgroundService.cs — HandleChatEventAsync switch
case "remember":
    _ = RememberFactAsync(key, value, pos);   // ← the only call site for persistence anywhere in chat handling
    ...
case "conversation":
    logger.LogInformation("[chat] conversational response for <{Username}>: '{Response}'", ...);
    break;                                     // ← no persistence, no side effect beyond logging
```

**And the LLM's JSON schema doesn't even offer `"remember"` as a legal value** — the `intent` enum documented earlier in the same prompt (`LlmChatInterpreter.cs:243-244`) lists `"gather" | "build" | "craft" | "smelt" | "navigate" | "cancel" | "status" | "place" | "command" | "help" | "conversation" | "clarify" | "ignore"` — no `"remember"`. So even a maximally-compliant LLM has no way to request persistence for a memory-related message; it's explicitly told to use `"conversation"` instead.

**Where `"remember"` *does* come from:** only the deterministic, non-LLM `ChatInterpreter.ParseIntent` pattern-matcher can produce it (`ChatInterpreter.cs:279`). But `LlmChatInterpreter.InterpretAsync`'s fast-path allowlist (line 97: `if (quick?.Intent is "cancel" or "status" or "help" or "navigate") return quick;`) does **not** include `"remember"` — so even when the pattern-matcher correctly detects a "remember X" phrasing, that result is only used as the final fallback (`return llmResult ?? quick;`, line 175) when the LLM call fails, is rate-limited, or is unavailable.

**Net effect:** with a healthy, available LLM (the normal, default, intended operating condition), a player saying *"remember the base is at -200, 70, 300"* gets an LLM-generated response along the lines of *"Got it, I'll remember that!"* — because the prompt's own MEMORY instruction primes the LLM to write a response implying storage happened (*"the system stores it"*) — while `RememberFactAsync` is never called and the fact is never written anywhere. The feature only actually works in the *degraded* case (LLM down/rate-limited/unparseable), which is the opposite of the reliability profile anyone would want or expect from this feature, and the failure mode is a confident false positive, not a visible error.

**Confidence caveat (92%, not higher):** it's possible the LLM sometimes disregards the explicit instruction and emits `"remember"` anyway (LLMs don't always perfectly follow system-prompt instructions) — if `ParseDecision`/`IntentDraft` don't strictly validate `Intent` against the documented enum (not verified in this pass), a non-compliant LLM output could still reach the `"remember"` case. This would make the bug intermittent rather than absolute, but the *documented, intended* behavior is definitively broken as designed, which is what matters for prioritization regardless of how often real LLM output happens to deviate from its own instructions.

**Why untracked:** searched for any task referencing the memory/conversation-intent conflict — none found. The closest adjacent work (`TSK-0188`, chat eviction policy) is unrelated.

**Recommendation:** Either (a) add `"remember"` to the LLM's intent enum and update the MEMORY prompt section to instruct `intent="remember"` with `item`/appropriately-repurposed fields carrying the key/value to store (mirroring how `"place"` uses `item`/`count`), then wire `RememberFactAsync` to fire from that path; or (b) keep `"conversation"` as the LLM-facing intent but have that case in `HandleChatEventAsync` additionally attempt a lightweight extraction-and-store when the message matches an obvious "remember ..." pattern (essentially re-adding the deterministic detection as a *supplement* to, not a *fallback from*, the LLM path). Option (a) is more consistent with the rest of the system's "LLM decides intent, IntentManager maps to action" architecture and is the more natural fix. Either way, this should be treated as a P1/High — it's a user-facing, silently-broken, actively-misleading feature under normal operating conditions.

---

## 2 — Correction/extension to `TSK-0258`/`TSK-0305`: the `/summon` combat workaround cannot ever succeed, in any game mode (High, 95%)

`TSK-0305` ("P0: fix summon/prompt denylist conflict... enqueue blocked command response to player," **Done**) correctly identified and fixed a real bug: when the LLM's prompted `/summon lightning_bolt` combat workaround got blocked by the deny list, the player received silence instead of feedback. The fix (Sprint 57 Wave D) ensures the player now gets a message.

**What neither `TSK-0305` nor `TSK-0258` state explicitly, and which changes how both should be prioritized/described:** `/summon` is not merely *usually* blocked or blocked in some configurations — it is unconditionally present in `AgentBackgroundService.DefaultDeniedCommands` (`AgentBackgroundService.cs:551`), which per its own doc comment (`lines 564-570`) is *"always included as a safety floor"* — the user-configured `SafetyOptions.DeniedCommands` list can only add to it via union-merge (a fix from `TSK-0324`, specifically to prevent this exact set from ever being weakened). There is **no configuration, game mode, or `AllowDestructiveCommands` setting that permits `/summon` through** — confirmed by reading the full merge logic; `AllowDestructiveCommands` and creative-mode checks are irrelevant to this specific command, since it's on the *hard* floor, not the *configurable* list.

This means the LLM's entire COMBAT prompt section (`LlmChatInterpreter.cs:357-366`) — a full paragraph with a worked example — instructs the model to produce an action that is **mathematically guaranteed to be rejected, every single time, regardless of anything the operator configures.** It is not a "creative mode workaround" as `TSK-0258`'s own description characterizes it (*"resorts to... using /summon lightning_bolt (creative mode workaround)"*) — it has never worked in creative mode either, both before and after `TSK-0305`'s fix. Before `TSK-0305`, it failed silently; after `TSK-0305`, it fails with a message. In neither case does it fight anything.

**Why this matters for prioritization:** `TSK-0258` (Backlog, `High`) proposes the correct real fix — a dedicated `AttackTool` calling `bot.attack(entity)`. Its description frames this as an *upgrade* from a working-but-crude workaround to a proper capability. In reality, there is currently **zero functional combat capability of any kind** — every "punch/attack/kill" request the LLM receives burns a full LLM round-trip and prompt-token budget attempting an action that cannot possibly succeed, then reports failure to the player. This is a stronger case for urgency than "we have a crude workaround and want something better" — it's "we have advertised functionality (an entire prompt section describing how combat works) that provides negative value today (LLM cost + confusing failure) for zero actual capability."

**Recommendation:**
1. Update `TSK-0258`'s description to reflect that the `/summon` approach is not a partially-functional workaround but a guaranteed no-op in all configurations, which may be worth surfacing to whoever prioritizes the backlog (this doesn't necessarily mean re-ranking `Backlog`/`High` further, but the current description understates the status quo).
2. As a cheap, immediate mitigation *not requiring `TSK-0258`'s full `AttackTool` work*: remove or rewrite the COMBAT section of the prompt to skip the summon attempt entirely and go straight to the prompt's own already-written fallback line — *"If commands are disabled, tell the player that combat is not yet available and suggest they handle combat manually"* — unconditionally, rather than gating that fallback behind an `AllowDestructiveCommands`/"commands disabled" check that (per Finding above) is irrelevant to whether `/summon` specifically will work. This would save an LLM round-trip's worth of wasted attempt-then-fail on every combat request until `TSK-0258` ships a real tool, and stop the bot from ever generating a command it cannot execute.

---

## Assumptions & Open Questions (this pass)

1. Finding 1's confidence is capped at 92% pending verification of whether `IntentDraft`/`ParseDecision` strictly validate the `intent` field against the documented enum (rejecting/coercing unexpected values) or accept any string permissively — this determines whether an LLM that ignores its instructions and emits `"remember"` anyway would actually reach the working code path. Not verified in this pass; would require reading `ParseDecision`'s JSON-to-`IntentDraft` mapping in full, which is present later in `LlmChatInterpreter.cs` but wasn't the focus of this read.
2. Did not verify whether `TSK-0297` ("fix LLM entity targeting confusion — summon targets bot instead of entity," presumably `Done` or `Backlog`, not checked) has any bearing on Finding 2 — it addresses a different symptom (wrong target coordinates) of the same underlying `/summon`-for-combat design, and doesn't change the conclusion that the command is unconditionally denied regardless of whether its targeting is correct.
3. This completes line-by-line review of `LlmChatInterpreter.cs`. Combined with the seven prior delta reports, the C#/JS production surface area reviewed across this audit series now covers: all of `Agent.Core`, `Agent.Planning` (including this file), `Agent.Tools`, `Agent.Memory`, `Agent.Construction`, `Agent.Vision`, `Agent.Personality`, `Agent.World.Minecraft`, the `WebUI.Blazor` background service/managers/hub/options/dtos, and the full `MineflayerAdapter` JS surface. Remaining unreviewed pockets are narrow: `Program.cs`'s full DI/startup wiring (only spot-checked for specific registrations across prior reports, never read top-to-bottom), and the `MemorySmith.Agent.Tests` project itself (14k lines) has not been audited as a corpus (only individual tests referenced when checking specific claims).
