# MemorySmith.Agent code audit
**Repository:** TheMasonX/MemorySmith.Agent  
**Snapshot:** `ad2215edf6a51b8b452ab5946551012bdffe1db5`  
**Audit date:** 2026-06-24

## Executive summary

This snapshot is not a simple “sprint 35” codebase anymore. The visible repo state shows a later roadmap (`v0.40.0`, `Sprint 41 in progress`) while the README still advertises `v0.35.0 — Sprint 35 complete`. That is the clearest sign of version/documentation drift, and it affects every downstream assumption about the sprint plan, completed work, and what still needs attention. fileciteturn13file0 fileciteturn14file0

The strongest code-level finding is that chat-direction heuristics are internally inconsistent: `ChatOptions` still exposes `MaxResponseDistanceBlocks`, but the active deterministic interpreter no longer uses it, and name matching is done with a substring check rather than a whole-word boundary. That is a real behavior/config contract break, not just a documentation issue. fileciteturn9file0 fileciteturn16file0

The second major theme is hidden coupling in “stringly typed” contracts: goal creation, build origin handling, and tool routing all rely on implicit string keys and partial parameter conventions. These are workable, but they are brittle and easy to regress when sprint work spreads across several modules. fileciteturn6file0 fileciteturn18file0

### Risk snapshot

| Area | Risk | Confidence | Why |
|---|---:|---:|---|
| Repo docs vs live state | High | 98% | README and roadmap disagree on version/sprint status. |
| Chat addressing logic | High | 96% | A documented option is unused and name matching is substring-based. |
| Build origin handling | Medium-High | 84% | Partial origin parameters are accepted without an invariant that all coordinates are present. |
| Goal/tool contracts | Medium | 87% | Goal names, prefixes, and context keys are implicit cross-module contracts. |
| LLM fail-soft behavior | Medium | 80% | Provider failures collapse to `null`, which improves resilience but reduces diagnosability. |

## Findings

### 1) Documentation drift is severe enough to mislead sprint planning
The README says `v0.35.0 — Sprint 35 complete — 501+ tests`, while the roadmap says `Current version: v0.40.0 | Latest: Sprint 41 (in progress)` and includes later phases/sprints. That means the repo has at least two competing “truths” about its state. Anyone using the README as a sprint guide will duplicate work or miss active in-progress items. fileciteturn13file0 fileciteturn14file0

**Why it matters:**  
This repo is sprint-driven and heavily self-documenting. When docs drift this far, architectural decisions get re-litigated, and audit work becomes harder to trust.

**Recommendation:**  
Treat the roadmap as the canonical sprint source and either update the README banner from build metadata or generate the version/sprint line from a single source of truth at build time.

**Confidence:** 98%

---

### 2) `MaxResponseDistanceBlocks` is documented but not actually used
`ChatOptions` documents `MaxResponseDistanceBlocks` as the distance beyond which the bot ignores unaddressed messages. In the active `ChatInterpreter`, that setting is not consulted. The interpreter now uses only bot-name substring matching, solo-player detection, and a conversation-window heuristic. fileciteturn9file0 fileciteturn16file0

**Why it matters:**  
A config option that appears to control behavior but is ignored is worse than no option at all: operators will tune it, believe it is working, and then misdiagnose the bot’s behavior.

**Recommendation:**  
Either wire the option back into `IsDirectedAtBot` or remove it from public config until the behavior returns. If the current intent is “always respond to direct mention, otherwise use conversation window only,” document that explicitly and delete the dead knob.

**Confidence:** 96%

---

### 3) Bot mention detection is too permissive
The active interpreter uses `message.Contains(botName, StringComparison.OrdinalIgnoreCase)` to decide whether a message is addressed to the bot. That is a substring check, not a whole-word match. A bot named `Leo` will be triggered by messages containing `Leopold`, `helios`, or similar accidental substrings. fileciteturn16file0

**Why it matters:**  
This creates false positives in multi-player chat and makes “who was this message for?” decisions noisy, especially if the bot name is short.

**Recommendation:**  
Use a compiled whole-word boundary regex or tokenized name matching. If the code wants the old “whole word” behavior, the implementation should match the comment, not just the intent.

**Confidence:** 92%

---

### 4) Build origin handling still relies on a fragile implicit contract
`GoalFactory` accepts `originX`, `originY`, and `originZ` independently and passes them into `BuildGoal`. `BuildGoal` considers the origin “explicit” if any one of those values is present (`OriginX.HasValue || OriginY.HasValue || OriginZ.HasValue`). That means a partially specified origin can be treated as a fully specified one, even though the build location is really incomplete. fileciteturn6file0 fileciteturn11file0

**Why it matters:**  
This is the kind of implicit contract that works until one caller supplies a partial map or one parameter parser drops a coordinate. Then builds can start at ambiguous or default locations, and the failure mode is subtle.

**Recommendation:**  
Model origin as a single value object (`BuildOrigin?`) and validate it once. Require either all three coordinates or none. If only some coordinates are present, fail fast with a clear message instead of silently mixing explicit and fallback values.

**Confidence:** 84%

---

### 5) Goal and tool routing are still stringly typed across module boundaries
The planner and runtime rely on many string conventions: `GatherItem:...`, `Build:...`, `CraftItem:...`, `GetStatus`, `Status`, `MoveTo`, and context keys like `build:{blueprint}:origin:x`. These are manageable today, but they create a web of hidden contracts between `GoalFactory`, planners, decomposers, the dispatcher, and the background loop. fileciteturn6file0 fileciteturn10file0 fileciteturn18file0

**Why it matters:**  
When sprint work touches one layer, a silent mismatch in another layer is easy to miss. This is especially risky in a codebase that now has both deterministic and LLM-mediated intent paths.

**Recommendation:**  
Introduce typed request records for goal creation and explicit context objects for build/planning state. Keep the string aliases at the external boundary only, and convert them once into typed internal models.

**Confidence:** 87%

---

### 6) LLM/provider failures are resilient but opaque
`OllamaProvider` catches `OperationCanceledException`, `HttpRequestException`, and a broad `Exception`, logs a warning, and returns `null`. That is a reasonable fail-soft choice, but it collapses all root causes into the same downstream outcome. The caller gets “no answer” instead of a typed failure class. fileciteturn8file0

**Why it matters:**  
This is fine for user-facing robustness, but it weakens diagnosis. If the LLM path is now the primary route for intent parsing, opaque nulls make it harder to separate “model was down” from “model produced junk” from “transport failed.”

**Recommendation:**  
Return a small result type (`Success`, `TimedOut`, `Unavailable`, `InvalidResponse`) or propagate a typed failure token alongside the warning log. That would preserve resilience while making the next layer smarter.

**Confidence:** 80%

## Architecture improvements worth prioritizing

1. **Create a single build-origin abstraction.**  
   A `BuildOriginResolver` that consumes explicit coordinates, world facts, and auto-detect fallback would remove repeated origin logic from factories and planners.

2. **Make config validation explicit at startup.**  
   Validate `ChatOptions`, memory URLs, and build-origin requirements once, so dead options and invalid partial inputs fail fast.

3. **Move from implicit string contracts to typed internal models.**  
   Keep user-facing aliases, but convert them to typed goal commands and tool requests immediately after parsing.

4. **Separate “resilient” from “opaque.”**  
   Keep fail-soft paths, but attach typed error reasons so the runtime can distinguish network failure from model failure from schema mismatch.

5. **Generate version/sprint metadata.**  
   The README, roadmap, and code banners should come from one source. That would eliminate a recurring class of audit noise.

## Assumptions and open questions

- I treated the exact commit hash provided by the user as the audit target.
- The visible repo state appears later than the README banner suggests; I assumed the roadmap is the more current planning source.
- I did not assume sprint 35 task names from earlier branches are still current unless the exact snapshot showed them.
- I did not find evidence that the current planner still swallows `ReplanAsync` exceptions; the exact `HtnPlanner` snapshot now logs failures and returns `ReplanResult.Failure`, which is a net improvement. fileciteturn18file0
- I was not able to inspect every file in the tree through the connector, so this report focuses on the high-risk runtime, planner, and chat-interpreter paths that are most likely to produce user-visible failures.

## Suggested next fixes, in order

1. Fix or remove the dead `MaxResponseDistanceBlocks` contract.  
2. Replace substring bot-name detection with whole-word matching.  
3. Normalize build origin into a typed value object and reject partial origins.  
4. Add typed failure reasons to the LLM provider boundary.  
5. Reconcile the README banner with the current roadmap/source of truth.

## Bottom line

The codebase has real momentum and several recent architectural improvements, but it also carries a few “looks-configurable-but-isn’t” and “looks-typed-but-is-stringly” risks. Those are the kinds of bugs that slow a sprint-driven agent project down more than raw algorithmic defects, because they create invisible mismatches between docs, config, and runtime behavior.
