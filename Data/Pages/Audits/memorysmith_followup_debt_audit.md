# Follow-up audit — additional bugs, legacy reduction, and technical-debt cleanup

**Repo:** `TheMasonX/MemorySmith.Agent`  
**Branch / commit focus:** `dev/round-3`, commit `cd60bbe73b44d8b07e7cf824c478f4da34ddd8c6`  
**This report:** additional deltas beyond the prior creative-mode follow-up, focused on more bugs, consolidation opportunities, and debt reduction.

## Highest-confidence new findings

### 1) Creative provisioning is still race-prone because it is fire-and-forget
**Severity:** High  
**Confidence:** 93%

`SetGoal()` kicks off `ProvisionGoalIfCreativeAsync(goal, CancellationToken.None)` without awaiting it, then immediately continues into goal setup and planning. That means a creative goal can start planning before item provisioning has completed, especially when the provisioning path has to queue chat `/give` actions and wait for `GetStatus`. This is a real timing hazard, not just a style concern. fileciteturn31file0

**Impact:** a creative build/gather goal can enter the planner with inventory still stale or incomplete, which reintroduces the same “missing item” failure loop the creative path is supposed to avoid.

**Recommendation:** make creative provisioning part of the goal activation contract rather than a background side effect. Either await the provisioning step before allowing planning, or model it as a first-class precondition state.

---

### 2) `AgentBackgroundService` is a god class with too many responsibilities
**Severity:** High  
**Confidence:** 96%

`AgentBackgroundService` currently owns queueing, goal lifecycle, event processing, damage interrupts, recovery, creative provisioning, dashboard pushes, memory persistence, chat interpretation, replanning, and build checkpoint state. The file spans multiple unrelated execution domains, and the code itself shows repeated “Sprint X” accretion around a single central class. That is a classic monolith pressure point and one of the biggest maintainability risks in the repo. fileciteturn19file0 fileciteturn31file0 fileciteturn35file0 fileciteturn44file0

**Impact:** every new fix adds coupling pressure, makes test setup harder, and raises the risk of hidden regressions when seemingly local changes affect queueing, recovery, or dashboard state.

**Recommendation:** split into narrower services:
- goal activation / cancellation,
- event projection + correlation,
- recovery / replanning,
- chat interpretation,
- creative provisioning,
- dashboard publishing.

---

### 3) The repository still uses a legacy dual-state model for world facts
**Severity:** Medium  
**Confidence:** 90%

`WorldState` still carries both `Facts` and `StructuredFacts`, and the code explicitly labels `Facts` as legacy while telling new code to prefer `StructuredFacts`. That is a transitional design that has not yet been removed, and it leaks into many runtime paths. `WorldStateProjector` and `AgentBackgroundService` still write to `Facts` heavily for build status, chat context, nearby entities, and inventory-adjacent state. fileciteturn14file0 fileciteturn15file0 fileciteturn16file0 fileciteturn35file0

**Impact:** the codebase has two parallel “truth stores,” which makes state reads harder to reason about and raises the chance that one path updates only the legacy structure.

**Recommendation:** define a migration plan that:
- makes `StructuredFacts` the only write path for new state,
- keeps a small compatibility shim for old readers,
- deletes legacy writes once all consumers are migrated.

---

### 4) Recovery parsing is still brittle and partially string-driven
**Severity:** Medium  
**Confidence:** 88%

`TryRecoverFromGameErrorAsync` extracts missing materials from free-form error text using regexes, and the fallback recovery prompt is also built from textual heuristics. This is fragile by design: the runtime depends on error phrasing staying stable, even though the adapter already emits structured `actionFailed` events with reason codes elsewhere. The recovery path should be using structured error data, not parsing prose as a contract. fileciteturn44file0 fileciteturn45file0 fileciteturn28file0

**Impact:** one wording change in an adapter error message can silently break recovery classification and push the agent into the wrong fallback branch.

**Recommendation:** route structured `reasonCode`, `block`, `material`, `item`, and `correlationId` into recovery before touching the free-form message string. Keep the string only as diagnostic context.

---

### 5) Inventory truth is still not fully grounded in real-time adapter state
**Severity:** High  
**Confidence:** 89%

The backlog still explicitly calls out `Wire bot.inventory updateSlot for real-time inventory ground truth`, `Expose motion, equipment, and environment state from Mineflayer to WorldState`, and `Project entity events in WorldStateProjector into WorldState`. That means the repo still considers these gaps unresolved. The current system relies on `GetStatus` reconciliation and event-derived deltas, but that is still a laggy substitute for live inventory updates. fileciteturn10file0

**Impact:** the agent can make planning decisions against stale inventory or incomplete world context, especially during rapid craft/place/mine cycles.

**Recommendation:** prioritize the event stream as the source of truth:
- wire `updateSlot`,
- project more adapter events into `WorldState`,
- reduce dependence on periodic `GetStatus` as a reconciliation crutch.

---

## Additional debt hotspots and cleanup opportunities

### 6) Chat handling is overgrown and still contains old-style parsing paths
**Confidence:** 87%

`HandleChatEventAsync` is a large switch that performs interpretation, response generation, command dispatch, navigation fallback, sequence building, and dashboard/chat logging in one place. The backlog already lists “Replace regex-only chat filter with structured message classification” and “Chat Context Dashboard,” which suggests the repo knows this area is still brittle. fileciteturn36file0 fileciteturn37file0 fileciteturn10file0

**Recommendation:** split chat into:
- message classification,
- intent resolution,
- goal creation,
- output/response handling.

That separation will also make it easier to remove regex-driven special cases later.

---

### 7) The planner still carries legacy and transitional branches
**Confidence:** 85%

The planner docs still describe gather as a fixed `SearchMemory → MineBlock → GetStatus` flow, while the codebase already has multiple mode-dependent branches and recovery behaviors that are not reflected there. The backlog also includes “Remove legacy HtnPlanner typed decomposition branches (defer to PlannerRouter)” and “Refactor Mineflayer adapter monolith into bounded modules,” which are both signals that the team already sees the same debt shape. fileciteturn56file0 fileciteturn10file0

**Recommendation:** eliminate direct planner assumptions that are now obsolete:
- move mode branching into decomposers,
- delete dead fallback branches,
- keep `PlannerRouter` as the sole routing surface.

---

### 8) There are still unresolved backlog items that should be treated as debt blockers, not feature ideas
**Confidence:** 84%

The backlog contains several items that are not “nice-to-have” if the goal is to keep the agent reliable: `Wire MoveToTool to read coordinates from ActionData.Context`, `Wire bot.inventory.on('updateSlot') for real-time inventory`, `Refactor AgentBackgroundService to Event Bus`, `Normalize ActionData.Tool to canonical form`, and `Fix MaxResponseDistanceBlocks dead config — wire or remove`. These are not peripheral improvements; they are core debt cleanup. fileciteturn10file0

**Recommendation:** treat these as stability work with explicit acceptance criteria:
- no dead config knobs,
- no dual field naming for tools,
- live inventory updates wired,
- event bus extracted from the monolith.

---

### 9) `TryRecoverFromGameErrorAsync` suppresses duplicate recovery attempts by goal name, which is useful but brittle
**Confidence:** 79%

The recovery path caches `_lastRecoveredGoalName` and returns early for the same goal. That stops loops, but it also assumes that all subsequent failures for the same goal are equivalent enough to suppress. If the error class changes while the goal stays the same, recovery can be skipped even though the failure mode has changed. fileciteturn44file0

**Recommendation:** key recovery throttling on `(goal, error signature)` rather than goal name alone, or use a narrow cooldown window with structured error fingerprints.

---

### 10) Session-fact loading can pollute chat context if the stored pages are noisy
**Confidence:** 73%

`LoadSessionFactsAsync` pulls up to 20 pages and injects them into chat history as one combined system block. That is pragmatic, but it is also a silent prompt-shaping mechanism with no filtering or relevance ranking beyond recency. It is a useful feature, but it deserves explicit safeguards because it can affect planning and interpretation in ways that are hard to trace later. fileciteturn42file0

**Recommendation:** add relevance gating or structured tags to session facts so only high-signal facts enter the prompt history.

---

## Consolidation plan for legacy reduction

### A) Convert the monolith into bounded services
Move by seam, not by feature:
- event projection / world-state update,
- action dispatch and correlation,
- recovery and replanning,
- chat/intent handling,
- creative provisioning,
- dashboard publishing.

That single change would shrink the largest source of technical debt in the repo. fileciteturn19file0 fileciteturn35file0 fileciteturn36file0 fileciteturn44file0

### B) Remove the legacy `Facts` write path
Keep `StructuredFacts` as the long-term contract and downgrade `Facts` to a compatibility read-only shim, then delete it after migration.

### C) Replace text parsing with structured contracts
Anywhere the code currently parses prose from the adapter or the LLM, prefer structured fields or explicit event types.

### D) Turn backlog-debt items into a single cleanup wave
The best next cleanup wave is not a feature sprint. It is a debt wave:
- live inventory updates,
- move-to context wiring,
- event bus split,
- tool name canonicalization,
- dead config removal,
- regex-free classification.

## Suggested near-term priority order

1. Await or otherwise serialize creative provisioning before planning.  
2. Wire live inventory updates (`updateSlot`) to reduce stale-world decisions.  
3. Split the `AgentBackgroundService` monolith at the largest seams.  
4. Replace recovery string parsing with structured error flow.  
5. Remove legacy `Facts` writes and keep only a compatibility read layer.

## Confidence summary

- Creative provisioning race: **93%**
- `AgentBackgroundService` is a monolith / refactor target: **96%**
- Legacy `Facts` vs `StructuredFacts` is ongoing debt: **90%**
- Recovery parsing is brittle and should be structured: **88%**
- Live inventory/world-state gaps remain real backlog items: **89%**
- Chat and planner layers still need consolidation: **85%**
