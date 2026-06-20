# memorysmith.agent Additional Audit Findings
Date: 2026-06-19  
Scope: second-pass review of the live repo surface currently exposed through GitHub pages/raw files, with emphasis on brittle assumptions, duplicated pathways, and unclear or arbitrary choices.

## Method used

I re-reviewed the code through three internal lenses:

1. **Control-plane safety** — places where the system accepts inputs or changes agent state.
2. **Behavioral semantics** — places where the user-visible behavior can drift from the documented intent.
3. **Code health / duplication** — places where the architecture is splitting one concept into multiple inconsistent paths.

This pass intentionally avoided repeating the earlier findings about dispatcher schema validation, arbitrary command acceptance, and queue thread-safety unless they appeared in a new form.

## Executive summary

The most material new issues are not low-level bugs; they are mismatches between what the code advertises and what it actually does. The clearest examples are the `/api/agent/connect` and `/api/agent/stop` endpoints, which return success without changing agent state, and `/api/blueprints`, which returns a hardcoded list rather than anything backed by the real blueprint repository. Those endpoints are operationally misleading and create false confidence for clients and UI code. citeturn462133view2

A second class of problems is over-broad or brittle interpretation logic. `ChatInterpreter` says it uses the configured `MaxResponseDistanceBlocks` heuristic, but the implementation never reads that option. It also treats any message containing the word `doing` as a status query, which will misclassify normal chat. And its item normalization uses `TrimEnd('s')`, which is a destructive heuristic for legitimate item IDs ending in `s`. citeturn462133view4turn258602view0turn620305view0

Finally, capability discovery is inconsistent. `GoalFactory.RegisteredGoals` advertises dynamic `GatherItem:{itemId}` and `Build:{blueprintId}` prefixes even when the required repositories are absent, so discovery can promise actions that cannot actually be created. That is a maintainability problem and a contract problem, because clients cannot tell “supported in principle” from “available right now.” citeturn622552view0turn622552view3

## Findings

### 1) `/api/agent/connect` and `/api/agent/stop` are success-only stubs
**Severity:** Medium  
**Confidence:** 98%

`Program.cs` maps `/api/agent/connect` to `Results.Ok(new { Status = "connected" })` and `/api/agent/stop` to `Results.Ok(new { Status = "stopped" })`, but neither endpoint mutates agent state, validates prerequisites, or returns a failure path when the agent is disabled. They are informational stubs masquerading as lifecycle controls. citeturn462133view2

**Why this is a problem:** callers will reasonably assume these endpoints control connection state. In practice, they only report a string, which makes operational tooling and UI controls lie by omission.  
**Recommendation:** either wire them to real lifecycle behavior or rename/document them as read-only status shims. If they remain stubs, they should not be presented as control actions.

### 2) `/api/blueprints` is hardcoded and disconnected from the real model
**Severity:** Medium  
**Confidence:** 95%

The blueprint discovery endpoint returns a single hardcoded object: `small-house`. That is independent of the actual planning model, which supports dynamic blueprint IDs through `GoalFactory` and repository-backed build goals. The endpoint therefore cannot be treated as a source of truth for available build options. citeturn462133view2turn622552view3

**Why this is a problem:** API consumers and the UI can easily build a stale mental model of what the agent can build. The codebase already has a richer dynamic source of truth; this endpoint bypasses it.  
**Recommendation:** back the endpoint with the same repository/factory data used by the planner, or clearly label it as a placeholder sample list.

### 3) `MaxResponseDistanceBlocks` is documented but unused
**Severity:** Medium  
**Confidence:** 96%

`ChatOptions` documents `MaxResponseDistanceBlocks` as the “closest agent responds” heuristic, but `ChatInterpreter.IsDirectedAtBot` never reads that property. The directed-at-bot decision currently depends only on player count, whether the message starts with the bot name, and the conversation window. citeturn462133view4turn258602view0

**Why this is a problem:** the configuration surface suggests a spatial heuristic exists, but the implementation does not honor it. That makes the option misleading and invites silent behavior drift between documentation and runtime.  
**Recommendation:** either wire the distance check into the heuristic or remove the option until the supporting data is available.

### 4) Status detection is too broad and will misclassify normal messages
**Severity:** Medium  
**Confidence:** 91%

`ChatInterpreter.ParseIntent` treats any message matching `\b(status|what.?re you doing|what are you doing|report|doing)\b` as a status query. The `doing` branch is especially broad, because ordinary sentences such as “I am doing fine” will match even when the sender is not asking for a status update. citeturn258602view0

**Why this is a problem:** the interpreter is meant to be deterministic and conservative, but this rule is the opposite: it creates false positives from unrelated speech.  
**Recommendation:** remove the standalone `doing` token, tighten the phrase set, or require a stronger conversational prefix before mapping to status.

### 5) Item normalization uses an unsafe plural-stripping heuristic
**Severity:** Medium  
**Confidence:** 89%

`ResolveItemId` first tries aliases, then applies `raw.TrimEnd('s')`, then tries aliases again, then falls back to a regex. Because `TrimEnd('s')` removes every trailing `s`, it can corrupt valid identifiers such as `glass` → `glas` or `moss` → `mos` when the name is not already covered by an alias. That is a brittle normalization strategy for a system that is supposed to accept stable item IDs. citeturn620305view0turn620305view2

**Why this is a problem:** the alias table is partly curated and partly heuristic. The heuristic can silently break valid IDs for items ending in `s`, which is the opposite of what an identifier resolver should do.  
**Recommendation:** replace the blanket plural-stripper with a constrained normalization map or a dedicated inflector that only rewrites known plural patterns.

### 6) Goal discovery advertises capabilities that may not exist
**Severity:** Medium  
**Confidence:** 86%

`GoalFactory.RegisteredGoals` always returns `[.. Creators.Keys, "GatherItem:{itemId}", "Build:{blueprintId}"]`, but `CreateAsync` can still return null if the item registry or blueprint repository is not injected. The public discovery surface therefore advertises dynamic goal families even when they are unavailable in the current runtime. citeturn622552view0turn622552view1turn622552view3

**Why this is a problem:** clients cannot distinguish “supported by design” from “currently available.” That makes the `/api/goals` contract leaky and makes UI features harder to reason about.  
**Recommendation:** expose two lists: one for registered patterns and one for currently available goals, or return capability flags alongside each entry.

### 7) The chat surface is split into two inconsistent pathways
**Severity:** Medium  
**Confidence:** 93%

`/api/agent/chat` enqueues a fixed `Chat` action with a message payload, while `/api/agent/command` enqueues whatever `req.Command` contains as the tool name. Those two HTTP paths represent the same conceptual “tell the agent something” surface, but they are implemented with different semantics and no shared validation layer. citeturn462133view2

**Why this is a problem:** duplicated control surfaces usually drift. Here the drift is already visible: one endpoint hardcodes a single action shape, the other accepts arbitrary tool strings. This makes the queueing layer harder to secure, test, and evolve.  
**Recommendation:** normalize both endpoints through one request model and one routing/validation function so the queue only receives vetted, structured actions.

## Consolidation and refactoring opportunities

The biggest codebase-health improvement opportunity is to collapse normalization and routing logic into single shared primitives.

`ChatInterpreter` currently has separate rules for bot addressing, status detection, item resolution, and blueprint resolution, each with its own ad hoc normalization path. That makes it hard to test the semantics as a unit. A better shape would be one small normalization pipeline followed by explicit intent handlers. citeturn258602view0turn620305view0

Similarly, `GoalFactory` already separates static and dynamic goals, but discovery and creation are not surfaced as separate runtime capabilities. Splitting those concepts would make the API honest: what is registered, what is available now, and what requires external dependencies would become explicit. citeturn622552view0turn622552view3

The HTTP surface also needs consolidation. The presence of `/api/agent/chat`, `/api/agent/command`, `/api/agent/connect`, and `/api/agent/stop` suggests the same control plane is being modeled four different ways, with different levels of validation and behavior. That is a sign the edge should be simplified rather than expanded. citeturn462133view2

## Assumptions

- I treated the GitHub-exposed files as the current source of truth for this pass.
- I assumed the documented intent in comments and XML docs reflects the intended runtime contract.
- I assumed the `/api/*` endpoints are meant for real clients, not just temporary scaffolding. citeturn462133view2turn462133view4

## Open questions

- Are `/api/agent/connect` and `/api/agent/stop` intentionally placeholders, or were they expected to mutate state?
- Should `/api/blueprints` be wired to the actual repository, or is it intentionally a sample endpoint?
- Is `MaxResponseDistanceBlocks` planned for a future Mineflayer/world-state integration, or should the option be removed until it is supported?
- Should `GoalFactory.RegisteredGoals` reflect current availability, or only theoretical capability? citeturn462133view2turn462133view4turn622552view0

## Confidence summary

- No-op lifecycle endpoints: **98%**
- Hardcoded blueprint discovery: **95%**
- Unused response-distance option: **96%**
- Over-broad status regex: **91%**
- Unsafe plural stripping: **89%**
- Capability discovery mismatch: **86%**
- Split chat/control surfaces: **93%**

## Sources reviewed

The findings above are grounded in the live `WebUI.Blazor/Program.cs`, `Agent.Planning/ChatInterpreter.cs`, `Agent.Planning/Llm/ChatOptions.cs`, and `Agent.Planning/GoalFactory.cs` surfaces currently visible in GitHub. citeturn462133view2turn258602view0turn462133view4turn622552view0
