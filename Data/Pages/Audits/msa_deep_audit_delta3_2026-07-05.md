# MemorySmith.Agent — Deep-Dive Audit: Delta Report #3
**Scope:** Continuation of the Sprint 59 audit series. This pass targeted the chat/command-handling region of `AgentBackgroundService.cs` (lines ~1264-1680, not previously reviewed line-by-line), `WorldStateProjector.cs` (full), `GoalFactory.cs` (full), `IntentManager.cs` (full), and `MineflayerAdapter/creativeProvider.js` (full) — cross-checked against `Data/Tasks/*.json` and the two prior delta reports to confirm novelty.
**Format:** Deltas only.

---

## Summary

| # | Type | Item | Severity | Confidence |
|---|------|------|----------|------------|
| 1 | New | `HandleChatEventAsync` double-records the bot's chat response into `ChatHistory` for the `"command"` intent | Medium | 95% |
| 2 | New, hardening | `IntentManager.ParseCommandString`'s regex captures multi-word item text verbatim with no space→underscore normalization before alias lookup | Low-Medium | 70% |
| 3 | New, untracked | `GoalFactory.RegisteredGoals` omits `PlaceBlock:{item}`, which `CreateAsync` fully supports | Low | 92% |
| 4 | New, cosmetic | Stale comment in `WorldStateProjector.StoreFacts`'s `BlockMinedEvent` case contradicts the class's own Sprint 40 changelog | Low | 85% |
| 5 | New, cosmetic | Doc-comment corruption (stray `z` merging two lines) in `IntentManager.cs:8` | Trivial | 99% |

Also explicitly re-verified and ruled out: the `BuildGoalDecomposer` origin-mislabeling bug (delta report #1, finding #2) does **not** also occur in `GoalFactory.CreateAsync`'s `Build:{blueprintId}` path — `BuildOrigin.FromNullable` correctly returns `null` unless all three coordinates are present, so the `BuildOriginSource.Explicit` argument passed there is always accurate; the mislabeling is confirmed isolated to `BuildGoalDecomposer`'s own fallback branch. `creativeProvider.js`'s `ensureCreativeItem` is a distinct, intentional last-mile safety net (called once, from the `place` action handler in `index.js`) layered on top of the C#-side proactive `IsCreativeMode` provisioning in `HtnTaskLibrary` — not redundant duplication, and already covered by TSK-0272/0275/0285.

---

## 1 — Duplicate chat-history recording on `"command"` intent (Medium, 95%)

`HandleChatEventAsync` (`AgentBackgroundService.cs`) records the LLM's spoken response to `_chatHistory` **unconditionally, once, before the intent switch**:

```csharp
// line 1308-1316
var pendingResponse = !string.IsNullOrEmpty(intent.Response) ? intent.Response : null;
chatInterpreter.RecordBotSpoke();
if (pendingResponse is not null)
    _chatHistory?.Record(botName, pendingResponse);

switch (intent.Intent.ToLowerInvariant())
{
    ...
```

The `"command"` case then records it **a second time**, in both of its sub-branches:

```csharp
// blocked branch, line 1368-1369
if (pendingResponse is not null)
    _chatHistory?.Record(botName, pendingResponse);
...
// success branch, line 1394-1395
if (pendingResponse is not null)
    _chatHistory?.Record(botName, pendingResponse);
```

`break` only exits the `switch`, not the method, so execution also reaches the generic post-switch handler (line 1659-1678) that turns `pendingResponse` into actual in-game `Chat` actions — but that handler does **not** touch `_chatHistory`, so the actual speech only happens once. The bug is confined to the history buffer, not to double-speaking in-game.

**Comparative evidence this is a genuine oversight, not intentional:** the `"navigate"` case (line 1509-1648) explicitly sets `pendingResponse = null` after consuming it (line 1550) *specifically to prevent the generic post-switch handler from reprocessing it* — proving the author was aware of and guarded against this exact double-handling class of bug elsewhere in the same method. The `"command"` case has no equivalent guard and doesn't need the extra `Record()` calls at all, since the unconditional one at line 1315-1316 already covers it.

**Impact:** `ChatHistory.Record()` (`Agent.Planning/ChatHistory.cs`) has no deduplication — confirmed by reading its source; every call unconditionally appends a new `ChatTurn` with its own timestamp. So every LLM-issued server command (e.g., `/give`, `/tp`, any dispatched `"command"` intent) burns **two** of the 30-turn rolling buffer's slots for one logical utterance, both blocked and allowed paths. Over a session with moderate command usage, this measurably shrinks the effective conversational context the LLM sees (`ChatHistory.MaxTurnsDefault = 30`), which is exactly the kind of "agent appears forgetful" problem `TSK-0188` (turn-count vs. length-based eviction) is trying to address from a different angle — this bug makes that problem worse, independent of which eviction policy is used.

**Recommendation:** Delete the two redundant `_chatHistory?.Record(botName, pendingResponse)` calls inside the `"command"` case (lines 1368-1369 and 1394-1395) — the unconditional one before the switch already covers it. No behavior change needed elsewhere. Small, safe, one-line-removal-times-two fix; no existing task covers this (checked `TSK-0188` and all chat/command-related tasks — none reference duplicate recording).

---

## 2 — `ParseCommandString` doesn't normalize multi-word item text before alias lookup (Low-Medium, 70%)

`IntentManager.ParseCommandString` (used only for `IntentDraft.NextSteps` — the TSK-0205 multi-step chaining follow-up commands, not the primary LLM intent path) uses greedy regexes to extract item names:

```csharp
var gatherMatch = Regex.Match(trimmed,
    @"^(?:gather|mine|get)\s+(?:(?<count>\d+)\s+)?(?<item>.+)$", RegexOptions.IgnoreCase);
...
var item = ResolveItem(gatherMatch.Groups["item"].Value.Trim());
```

`(?<item>.+)` captures everything to the end of the string verbatim, including internal spaces (e.g., a `NextSteps` entry like `"gather 5 oak logs"` captures `item = "oak logs"`). `ResolveItem` looks this up in `AliasRegistry.ItemAliases`, which — confirmed by inspection — contains **zero** space-containing keys; all canonical Minecraft IDs and their aliases are single underscore-joined tokens (`oak_log`, `cobblestone`, etc.). A multi-word capture that isn't already an exact alias match falls through `ResolveItem` unchanged and is passed straight into `GatherGoalRequest("oak logs", ...)`, which almost certainly won't resolve to any real block/item downstream.

**Confidence caveat (70%, not higher):** this path is only exercised when an LLM's `NextSteps` array contains a follow-up command phrased with a multi-word item name — I did not find a live example in logs/tests, and it's plausible the system prompt instructs the LLM to always emit single-token IDs in `NextSteps` (unverified in this pass — would require reading the chat-interpretation system prompt construction in `LlmChatInterpreter.cs`, which was out of scope this round). Flagging as a hardening gap rather than a confirmed active bug.

**Recommendation:** Normalize captured item text before alias lookup — at minimum `item.Replace(' ', '_')` before calling `ResolveItem`, mirroring the defensive normalization already applied elsewhere in the codebase (e.g., `WorldStateProjector`'s `minecraft:` prefix stripping). Cheap, safe, closes the gap whether or not it's currently triggered in practice.

---

## 3 — `GoalFactory.RegisteredGoals` is missing `PlaceBlock:{item}` (Low, 92%)

```csharp
// GoalFactory.cs:56-57
public IReadOnlyList<string> RegisteredGoals =>
    [.. Creators.Keys, "GatherItem:{itemId}", "Build:{blueprintId}", "CraftItem:{itemId}", "SmeltItem:{inputItem}"];
```

`CreateAsync` fully handles a `PlaceBlock:{item}` prefix (lines 157-167, added "Sprint 54" per its own comment, same sprint as the other four entries), but it's absent from this list. Confirmed this list isn't cosmetic — it's surfaced through three REST endpoints in `Program.cs`: `GET /api/about` (`RegisteredGoals` field), `GET /api/goals`, and the `400 Bad Request` error payload from `POST /api/agent/plan` (`Available: factory.RegisteredGoals`).

**Impact:** any external tool, operator, or developer discovering valid goal names via these endpoints (which is exactly what they're for) won't learn that `PlaceBlock:{item}` is a real, working goal type — and if they guess it anyway and it works, the earlier `400` error for a genuinely invalid name would still omit it from the "did you mean one of these" list. Low severity (doesn't affect the live chat-driven agent loop, which reaches `PlaceBlockGoal` via `IntentManager`/`PlaceGoalRequest`, not through this list), but a real, easily-fixed documentation/discoverability gap. No task references `RegisteredGoals` or this omission.

**Recommendation:** Add `"PlaceBlock:{item}"` to the list. One-line fix.

---

## 4 — Stale comment in `WorldStateProjector.StoreFacts`, `BlockMinedEvent` case (Low, 85%)

```csharp
case BlockMinedEvent e:
    // Sprint 35 P0-A: facts only — inventory update removed; ItemCollectedEvent
    // provides the authoritative inventory update with the correct drop name.
    result = result.With(b => { ... });
    break;
```

This comment is accurate about what `StoreFacts` itself does (facts only), but reads as a statement about `BlockMinedEvent` handling in general — and the class's own top-of-file changelog (lines 34-42) documents that **Sprint 40 P0-B explicitly restored** an inventory increment for `BlockMinedEvent` via the separate `ApplyBlockMined` method (which calls into this `StoreFacts` case internally, after already updating inventory). A reader looking at this comment in isolation — which is the realistic way anyone encounters it while scanning the `StoreFacts` switch — would reasonably conclude `BlockMinedEvent` never touches inventory, which is false as of Sprint 40. This is the same class of drift as the `HtnTaskLibrary.GatherItemDecompose` stale comment flagged in the first report (finding #4), just lower-stakes since it doesn't misdescribe an actually-missing behavior, only fails to mention a since-added one.

**Recommendation:** Append a one-line note, e.g. `// (Sprint 40 P0-B: ApplyBlockMined — the caller of this method — does update inventory separately; see class doc.)`, so the two Sprint references don't read as contradictory.

---

## 5 — Doc-comment corruption in `IntentManager.cs` (Trivial, 99%)

```csharp
/// <summary>
/// Maps <see cref="IntentDraft"/> to a typed <see cref="GoalRequest"/> suitable
/// for the GoalFactory pipeline (PRINCIPLE-1: parsers never create goals).z///
/// Sprint 39 P3: GoalRequest refactored from a single loosely-typed record to an
```

Line 8 has a stray `z` character fused into the comment where a line break should be (`goals).z///`), almost certainly a copy-paste/find-replace artifact. Purely cosmetic — compiles fine, but renders oddly in generated docs/IntelliSense. One-character fix.

---

## Assumptions & Open Questions (this pass)

1. Finding 2's severity depends on whether `LlmChatInterpreter.cs`'s system-prompt construction already constrains the LLM to single-token item IDs in `NextSteps` — not verified this pass (out of scope); worth a quick read before prioritizing the fix.
2. Did not review `ChatInterpreter.cs` (the non-LLM/regex-based interpreter, 354 lines) this pass — flagged as the next candidate if further depth is wanted, since `LlmChatInterpreter.cs` and `ChatInterpreter.cs` together represent the largest remaining unreviewed surface area in `Agent.Planning`.
3. Did not verify whether `RecordBotSpoke()` (called once, unconditionally, at line 1311, separate from `_chatHistory?.Record`) has any related double-invocation risk of its own — it's a different mechanism (tracked briefly, appears to just be a "did the bot just speak" flag for rate-limiting/cooldown purposes based on its name) and wasn't in scope for this pass's `ChatHistory`-specific investigation.
