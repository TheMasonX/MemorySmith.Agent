# MemorySmith.Agent code audit
**Scope:** `themasonx/memorysmith.agent`, branch `sprint-5-tool-safety` as the active public branch view, with the current repo tree showing 690 commits and the WebUI/Blazor + agent code split. The review is aligned to the architecture-improvement guidance that emphasizes surfacing architectural friction and deepening opportunities rather than just adding new interfaces. citeturn622825view0turn363058view0

## Executive summary

Overall assessment: the codebase is moving in a good direction, but the current sprint line still has a few high-impact reliability gaps concentrated in time handling, damage interruption, and gather/goal decomposition. The strongest pattern I see is that the system is becoming more modular, but several critical behaviors still depend on branch-specific conventions, direct clock reads, and narrowly covered event paths. citeturn761332view1turn803461view0turn668986view9

### Top findings

| Priority | Finding | Confidence | Why it matters |
|---|---|---:|---|
| P0 | Replan timing still depends on direct wall-clock reads instead of the injectable time abstraction the roadmap already calls for. | 94% | This is a determinism and flakiness risk, especially around stall recovery and tests. citeturn803461view0turn761332view0 |
| P0 | The damage-interrupt path is complex and explicitly still missing its deferred integration test. | 88% | This is one of the most failure-prone runtime paths and the sprint plan already flags it as unfinished. citeturn761332view0turn668986view9turn668986view8 |
| P1 | Gather semantics are fragmented across goal creation, decomposer work, discovery tools, and target-count fixes. | 77% | This increases the chance of duplicated logic, drift, and “works for one path but not another” regressions. citeturn761332view0turn996870view0turn915495view2turn801090view8 |
| P2 | ToolDispatcher consolidation is sound, but the test surface is still mostly happy-path registration/lookup behavior. | 69% | The architecture wants a single safe dispatcher, but the current tests do not strongly prove schema rejection, malformed input handling, or tool-safety constraints. citeturn761332view3turn949462view1 |

### What looks healthy

The repo already has the right architectural instincts: bounded contexts, a consolidated tool dispatcher, typed goal decomposers, and a planner/router split that reduces direct branching in the core planner. That is the kind of “deep module” evolution the architecture guide is aiming for. citeturn761332view1turn761332view3turn801090view8turn363058view0

## Confirmed findings

### 1) ReplanGovernor still needs a real time abstraction
**Severity:** High  
**Confidence:** 94%

`ReplanGovernor` still uses `DateTimeOffset.UtcNow` directly for stall timestamps and recovery timing. The branch roadmap already lists an `ITimeProvider`/`TimeProvider` abstraction as a Sprint 24 priority, which is a strong signal that this is recognized technical debt rather than optional polish. The existing tests also validate timeout recovery with a real delay, which makes the suite slower and more timing-sensitive than it needs to be. citeturn803461view0turn761332view0turn949462view0

**Why this is a problem**
- Stall recovery is time-based, so direct wall-clock access makes behavior harder to reproduce.
- Tests become less deterministic and more vulnerable to CI scheduling noise.
- The repo already has a `SystemTimeProvider` pattern elsewhere, so this looks like an inconsistency in adoption rather than an intentional design choice. citeturn761332view0turn803461view0

**Recommendation**
Thread the time abstraction into `ReplanGovernor` and any other timing-sensitive policy objects before adding more policy state. This is a better “deepening” move than layering more special cases on top of direct `UtcNow` checks. citeturn363058view0turn761332view0

### 2) The damage interrupt path needs the deferred integration test
**Severity:** High  
**Confidence:** 88%

The sprint roadmap explicitly calls out a deferred integration test for `TryInterruptOnDamage`. That matters because the current event loop has several interacting branches: it synthesizes `DamageTakenEvent` on health drops, rate-limits interrupts, suppresses interrupts in combat mode via threshold `0`, and separately enqueues passive `GetStatus` checks when health is low but no delta event was detected. citeturn761332view0turn668986view9turn668986view8

**Why this is a problem**
- The behavior spans at least three concerns: event ingestion, queue mutation, and interrupt policy.
- A regression here would be high impact because it affects survival behavior, not just planner convenience.
- The code is already careful enough to warrant a targeted integration test, which is a good sign that the path is important and brittle. citeturn668986view9turn668986view8

**Recommendation**
Add one end-to-end test that proves the whole chain: health drop → synthesized damage event → interrupt decision → queue replacement with `GetStatus`. That gives much more confidence than isolated unit coverage on the helper methods. citeturn761332view0turn363058view0

### 3) Gather behavior is split across too many layers
**Severity:** Medium  
**Confidence:** 77%

There are at least four overlapping gather-related tracks in play: `GoalFactory` already supports `GatherItem:{itemId}` and a built-in fallback for direct-mine blocks, Sprint 24 calls out a `GatherGoalDecomposer` target-count pass-through fix, task `0010` is a broader “gather arbitrary items, including mods” backlog item, and task `0013` adds `ListBlocks/ListItems` discovery because hardcoded names cause gather loops. That is not necessarily duplication, but it is a clear sign that the same user intent is being modeled in several different places. citeturn761332view0turn996870view0turn915495view2turn801090view8

**Why this is a problem**
- It increases the chance that one path respects count/target semantics while another silently drops them.
- It makes it hard to know whether a gather bug belongs in goal creation, decomposition, discovery, or planner routing.
- It makes future refactors risky because the behavior is not yet concentrated in one canonical abstraction. citeturn761332view0turn801090view8

**Recommendation**
Treat gather as one pipeline with a single canonical target model, then fan out into item discovery and execution. The goal should be to move the “what am I gathering?” logic into one deep module, not to keep adding special prefixes and compatibility paths. citeturn363058view0turn761332view0

### 4) ToolDispatcher is a good consolidation, but the proof is still too thin
**Severity:** Medium  
**Confidence:** 69%

The architecture notes describe `ToolDispatcher` as the single consolidated dispatcher for tool calls, with schema validation intended to prevent arbitrary code execution. The tests I reviewed cover basic success/failure lookup, case-insensitive names, and overwrite semantics, but they do not yet prove that malformed input is rejected safely or that the dispatcher behaves correctly under more adversarial payloads. citeturn761332view3turn949462view1

**Why this matters**
- Consolidation is only a win if the central gate is truly trustworthy.
- The most important failures here are negative cases, not happy paths.
- A safe dispatcher should be validated with invalid schema, missing required fields, and payloads that exercise the narrowest possible trust boundary. citeturn761332view3

**Recommendation**
Expand the test suite around unsafe and malformed tool calls before adding more tools. That makes the dispatcher a stronger architectural boundary instead of just a convenient registry. citeturn363058view0turn761332view3

## Architecture-level opportunities

### A) Make timing a first-class dependency
The repo already knows this is needed. The next step is to apply it consistently to every timing policy that influences control flow: replan stall recovery, health interrupt cooldowns, passive health polling, and rate limiting. That would make the codebase easier to test and would remove a class of non-deterministic behavior. citeturn761332view0turn803461view0turn668986view9

### B) Pull damage policy out of the event loop
`AgentBackgroundService` currently contains a lot of nuanced survival behavior in-line. The logic is correct-looking, but it is dense. A dedicated damage-interrupt policy object would give the event loop a narrower job: ingest events, update world state, delegate policy decisions, and queue actions. That is a better deep-module shape than one long method with many interlocked cooldowns. citeturn668986view9turn668986view8turn363058view0

### C) Concentrate gather semantics before broadening content coverage
The current backlog suggests the team is still deciding where discoverability lives versus where execution lives. Before expanding mod/item coverage further, it would help to settle the canonical gather contract and then have the decomposers and discovery tools feed that contract. Otherwise every new item family will add one more special-case path. citeturn996870view0turn915495view2turn761332view0

### D) Keep the planner/router split clean
`HtnPlanner` has already been moved into a fallback role while typed goals are handled by decomposers through `PlannerRouter`. That is the right direction. The main risk now is allowing fallback logic and typed-goal logic to drift apart semantically. The more that happens, the harder it becomes to understand whether a bug is in decomposition, routing, or execution. citeturn801090view8turn761332view1

## Existing sprint work I intentionally did not duplicate

The current public backlog already has several open items that overlap with this audit, so I treated them as in-scope work rather than new findings: task `0011` documents the verified MCP baseline, task `0012` is the Minecraft wiki deployment, task `0013` is `ListBlocks/ListItems` discovery, and task `0014` is the Serilog SQLite telemetry sink. Sprint 24 also already lists the `TryInterruptOnDamage` integration test, the `GatherGoalDecomposer` target-count fix, the time-provider abstraction, and the `IWorldObservationGateway` note. citeturn915495view6turn915495view7turn915495view2turn915495view5turn761332view0

## Assumptions

1. I treated `sprint-5-tool-safety` as the active branch because the public repo view shows that branch selected on the repository page. citeturn622825view0  
2. I treated the roadmap page as the authoritative sprint plan for “what is already tracked,” because it explicitly lists current version, Sprint 23 completion, and Sprint 24 priorities. citeturn761332view0  
3. I assumed the public branch tree is representative of the code you want audited, even though the exact commit SHA was not exposed in the browseable view. citeturn622825view0

## Open questions

1. Should the `TimeProvider` abstraction be propagated into `ReplanGovernor` only, or into every clock-using policy object in one pass?  
2. Is the deferred `TryInterruptOnDamage` test expected as a service-level integration test or as a broader end-to-end scenario?  
3. Should `ListBlocks/ListItems` feed the canonical gather pipeline directly, or remain a separate discovery layer?  
4. Does the `GatherGoalDecomposer` target-count fix already cover every count-bearing gather path, or only the narrow named case in Sprint 24? citeturn761332view0turn996870view0turn915495view2

## Confidence notes

- Replan/time abstraction gap: **94%**
- Damage-interrupt test gap: **88%**
- Gather semantics fragmentation: **77%**
- ToolDispatcher proof surface is thin: **69%**
- Overall codebase health assessment: **81%**

## Bottom line

This branch is not “messy”; it is in the middle of a real modularization push. The main improvement opportunity is to stop spreading the same policy across multiple time-sensitive and goal-sensitive paths, and instead deepen the modules that already exist. That is the shortest path to better testability, fewer regressions, and less sprint-by-sprint duplication. citeturn363058view0turn761332view1turn761332view0
