# MemorySmith.Agent Addendum Audit — Additional Findings
**Branch reviewed:** `sprint-35-llm-first`  
**Generated:** 2026-06-23 21:26 UTC

This addendum includes only new findings that were not covered in the prior reports.

## 1) Partial build origins can slip through as “explicit” and collapse to zeroes
**Confidence: 95%**

The build origin flow still accepts partial coordinates as a valid explicit origin.

`BuildGoal.HasExplicitOrigin` returns true when *any* of `OriginX`, `OriginY`, or `OriginZ` is present. `BuildGoalDecomposer` then copies missing axes to `0` via `?? 0`, and `ReadOriginFact(...)` treats an unparseable fact as “found” while returning `0`. That combination means a build can look explicitly anchored while actually using a partial or corrupted origin. fileciteturn136file0turn122file0

Why this matters:
- A chat request with only one coordinate can silently become a build at or near `(x,0,0)`.
- A malformed stored origin fact can be treated as valid instead of falling back cleanly.
- This creates a very hard-to-debug class of “why did it build there?” failures.

Recommended improvement:
- Treat explicit origin as valid only when all three axes are present.
- Treat malformed stored origin facts as missing, not as `0`.
- Emit a warning that includes the blueprint id and the exact incomplete axis set.

## 2) Blueprint and item lookup do not survive gateway exceptions
**Confidence: 92%**

The repository fallback chains only help when the gateway returns an empty result. They do not help when the gateway throws.

`MemorySmithBlueprintRepository.GetAsync(...)` first calls `memory.GetPageAsync(...)`, then falls back to local files, then search. But there is no exception handling around the gateway call, so a transient HTTP failure short-circuits the entire fallback chain. `MemorySmithItemRegistry.FetchAsync(...)` has the same shape: it calls `GetPageAsync` first and only falls back if the call returns empty, not if it throws. fileciteturn138file0turn146file0

Why this matters:
- A temporary backend outage can make blueprints and item specs disappear even when local pages exist.
- Offline/dev fallback is not reliable if the primary path throws.
- The code advertises a resilient lookup order that it does not actually guarantee.

Recommended improvement:
- Wrap gateway lookups in a narrow try/catch and continue to local/search fallback on network failures.
- Log the exception type and page/item id when falling back.
- Keep the exception from masking malformed-page cases by only falling back on transport/network exceptions.

## 3) Malformed blueprint pages can still be accepted into goal creation
**Confidence: 89%**

`MemorySmithBlueprintRepository.GetAsync(...)` parses the page and returns the resulting `Blueprint` as long as the content is non-empty. `GoalFactory.CreateAsync(...)` accepts that blueprint without validating that required metadata such as `Id` and `Name` are populated. `BlueprintParser.Parse(...)` itself returns empty metadata on malformed input instead of failing. fileciteturn138file0turn140file0turn141file0

Why this matters:
- A malformed blueprint page can become a `BuildGoal` with an empty or inconsistent id/name.
- That can corrupt build-related fact keys and make checkpointing / completion tracking ambiguous.
- The system should reject malformed blueprints early instead of letting them propagate.

Recommended improvement:
- Reject blueprint pages with missing id or name before returning them from the repository.
- Add a warning that explicitly names the malformed page and the missing fields.
- Add tests for “empty id,” “missing name,” and “parseable but malformed” blueprint pages.

## 4) Mining inventory is now at risk of systematic double-counting
**Confidence: 91%**

The current mining pipeline increments inventory in both `ApplyBlockMined(...)` and `ApplyItemCollected(...)`. The adapter emits both `blockMined` and `itemCollected` for the same mining interaction, so the same mined item can be counted twice when the drop is successfully collected. `ApplyBlockMined(...)` explicitly acknowledges that the second increment is expected sometimes. fileciteturn123file0turn124file0turn115file0

Why this matters:
- Inventory can drift high, not just low.
- Crafting and gather goals can appear complete earlier than they should.
- The code is trading one failure mode (stuck at zero) for another (overcount), and the overcount case is likely the common path.

Recommended improvement:
- Make one source authoritative for inventory mutation and turn the other into a pure observation/fact event.
- If both events stay, add deduplication keyed by correlation id + block position + item type.
- Add a regression test that mines a self-dropping block once and asserts the inventory increases by exactly one.

## 5) The new runtime intent bridge is still hardcoded to single-player assumptions
**Confidence: 84%**

`IntentManagerImpl.ProcessChatAsync(...)` always passes `DefaultOnlinePlayers = 1` and `playerPosition: null` into the chat interpreter. That means the chat interpreter cannot use real multiplayer addressing or distance gating in the new runtime bridge layer. The code comments even note that live online player count is a future wiring task. fileciteturn130file0

Why this matters:
- In multiplayer, the bot will over-interpret ordinary player chat as addressed to it.
- The distance gate is effectively disabled in this path.
- This is a current architectural trap if the runtime-manager split gets wired in as documented.

Recommended improvement:
- Pass the actual online player count through the bridge.
- Provide a real player position when available instead of `null`.
- Add a multiplayer integration test that proves the bot ignores unrelated nearby chat.

## 6) Dashboard live log buffering can become expensive under bursty logging
**Confidence: 74%**

`LiveLogBuffer.Add(...)` trims the queue using `_entries.Count` inside a loop. On a `ConcurrentQueue`, repeated `Count` calls are relatively expensive and the loop can churn under bursty log volumes. The buffer also drops old entries silently once capacity is reached. fileciteturn149file0turn147file0

Why this matters:
- Logging bursts can create unnecessary contention and overhead.
- Old entries can disappear without any indication that the dashboard history is incomplete.
- That makes the dashboard less trustworthy during the exact periods when logs matter most.

Recommended improvement:
- Replace the `Count`-based trim loop with a bounded buffer strategy that tracks size explicitly.
- Emit a low-rate notice when the buffer starts evicting entries.
- Add a load test that simulates log bursts and confirms the dashboard remains responsive.

## Recommended follow-up priorities

1. Fix explicit/origin completeness handling for build goals.
2. Make blueprint and item lookup fall back on transport exceptions, not just empty results.
3. Reject malformed blueprints before goal creation.
4. Remove the inventory double-count path in mining.
5. Wire real multiplayer context into the new intent bridge.
6. Tighten the dashboard log buffer eviction strategy.

## Confidence summary

- Partial build origins can silently collapse to zeroes: **95%**
- Gateway exceptions break lookup fallback: **92%**
- Malformed blueprints can become goals: **89%**
- Mining inventory can double-count: **91%**
- Runtime intent bridge is single-player biased: **84%**
- Live log buffer trim is inefficient under bursts: **74%**
