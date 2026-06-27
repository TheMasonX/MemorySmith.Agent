# MemorySmith.Agent Addendum Audit — Further New Findings
**Branch reviewed:** `sprint-35-llm-first`  
**Generated:** 2026-06-24 02:44 UTC

This addendum includes only findings that were not covered in the prior reports.

## 1) The item registry caches “missing” results, so transient misses can become sticky
**Confidence: 93%**

`MemorySmithItemRegistry.GetAsync(...)` caches the returned spec even when the result is `null`. That avoids repeated lookups for genuinely missing pages, but it also means a temporary gateway failure or a newly published page can be treated as absent until the TTL expires. fileciteturn146file0

Why this matters:
- A transient backend outage can create a false negative that outlives the outage.
- Newly added item-registry pages will not be picked up until the cache entry expires.
- The code can mislead the planner into thinking an item truly does not exist.

Recommended improvement:
- Cache negative lookups only for clearly “not found” responses, not for transport failures.
- Use a much shorter TTL for null results, or avoid caching null entirely.
- Log whether a miss came from the gateway, local fallback, or cache.

## 2) Malformed item-registry pages are treated the same as absent pages
**Confidence: 89%**

`MemorySmithItemRegistry.ParseItemSpec(...)` skips malformed lines silently and returns `null` if required fields like `item_id` or `display_name` are missing. That makes a badly authored registry page indistinguishable from a missing page in the rest of the system. fileciteturn146file0

Why this matters:
- A typo in a registry page silently becomes “item not found.”
- The lookup path gives no clue whether the source page exists but is malformed.
- This is hard to debug because the failure looks like ordinary absence.

Recommended improvement:
- Return a structured parse result that distinguishes `NotFound` from `Malformed`.
- Log the page id and missing fields when required metadata is absent.
- Add tests for incomplete item pages and malformed front matter.

## 3) Blueprint creation accepts parsed pages even when required metadata is empty
**Confidence: 91%**

`BlueprintParser.Parse(...)` returns empty metadata on malformed input, and `GoalFactory.CreateAsync(...)` accepts the resulting blueprint without validating that `Id` and `Name` are present. That lets malformed blueprint pages slip into build-goal creation as if they were valid. fileciteturn140file0turn141file0

Why this matters:
- A malformed blueprint can become a build goal with an empty or inconsistent identity.
- Build facts, logs, and completion tracking can drift because the blueprint key is not trustworthy.
- The failure is later and harder to diagnose than rejecting the page up front.

Recommended improvement:
- Reject any blueprint whose id or name is empty before goal creation.
- Add a validation warning that names the page and the missing fields.
- Add a parser test that proves malformed front matter cannot produce a usable build goal.

## 4) GetPageTool accepts an empty pageId and turns it into a misleading lookup
**Confidence: 84%**

`GetPageTool.ExecuteAsync(...)` checks that the `pageId` property exists, but it does not reject `""` or whitespace. That means the tool can issue a lookup with an empty slug and return a generic “not found” instead of a validation error. fileciteturn137file0

Why this matters:
- A malformed request looks like a missing page instead of a bad input.
- It makes LLM/tool debugging noisier because the failure classification is wrong.
- An empty slug should be a caller error, not a lookup miss.

Recommended improvement:
- Reject empty or whitespace `pageId` values before calling the gateway.
- Return a validation-style error message so the caller can repair its input.
- Add a test for `pageId: ""` and `pageId: "   "`.

## 5) The runtime architecture is still split between a live service path and a future contract path
**Confidence: 80%**

`AgentRuntime` documents the intended future manager-based tick loop, but `AgentBackgroundService` still owns the active event-processing and dispatch logic. That means the new runtime contract is not yet the authoritative execution path, so fixes added to the new managers can be bypassed by the still-live background service path. fileciteturn145file0turn103file0

Why this matters:
- Logic can drift between the documented runtime contract and the actual service behavior.
- Bugs can be fixed in the manager layer while the old path continues to execute the old behavior.
- It becomes harder to reason about where a given validation or recovery rule actually lives.

Recommended improvement:
- Pick one execution surface as authoritative for the next sprint and route new behavior through it.
- Add a checklist that every new recovery/validation rule is wired into the live path, not just the contract type.
- Add an integration test that proves the intended runtime path handles a representative chat/event/action flow.

## Recommended follow-up priorities

1. Make negative item-registry lookups non-sticky or clearly cache-scoped.
2. Distinguish malformed item pages from missing pages.
3. Reject malformed blueprints before build-goal creation.
4. Tighten `GetPageTool` input validation.
5. Remove the live/runtime split before adding more behavior to the manager contract.

## Confidence summary

- Sticky negative item-registry caching: **93%**
- Malformed item pages collapse to absence: **89%**
- Malformed blueprints are accepted downstream: **91%**
- Empty `pageId` is treated as a lookup miss: **84%**
- Runtime architecture split can cause drift: **80%**
