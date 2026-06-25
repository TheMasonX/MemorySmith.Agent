# MemorySmith.Agent Deep Code Audit Report
**Scope:** latest commit `18648691d8abd5ad84ee255795b76ffdc0aca131` on `sprint-35-llm-first`  
**Date:** 2026-06-25  
**Review style:** architecture-first, deletion-test minded, fault-path focused, with sprint/task de-duplication checked against the repo’s own handoff and roadmap docs.

## Executive summary

This codebase is moving in the right direction, but it still has a few high-leverage seams where “greenfield” intent is not yet fully enforced in implementation. The strongest pattern I found is that the repo has already started collapsing legacy behavior into explicit intent, typed outcomes, and single-source projections. The weakest pattern is that some of the old sentinel/fallback habits are still alive in the most important runtime paths: origin resolution, inventory projection, REST fallback behavior, and a few best-effort swallow paths in adapter code.

The most important practical finding is that the build/origin path is still brittle when values are partially missing or when `0` is a legitimate coordinate. The current `BuildGoal` / `BuildGoalDecomposer` / `HtnTaskLibrary` chain still uses “any nullable coordinate means explicit origin” plus “all zeros means unset,” which is a classic hidden contract. In a greenfield agent this is the kind of contract that will quietly produce the wrong build location rather than fail loudly.

The second major finding is that world-state inventory projection still appears to lean on block-name-to-item-name mapping in `WorldStateProjector.ApplyBlockMined`, even though the sprint handoff explicitly frames inventory as becoming event-sourced through authoritative item events. That is the right direction architecturally, but the current projector snapshot still risks under/over-counting the actual drops for blocks whose mined block is not the collected item.

The third major finding is that a few subsystems still swallow failures broadly enough to hide root cause: `RestMemoryGateway.UpdatePageAsync`, `WebSocketBridge` reconnect cleanup, and some parse-fallback paths. These are not catastrophic by themselves, but they undermine the repo’s stated goal of making every failure visible and recoverable.

### What is already good

The repo has strong architectural direction: bounded contexts, deep modules, deterministic-first behavior, a single canonical projector, schema validation at the tool boundary, and typed planner results. The latest chat parser migration to `IntentDraft` is coherent, and the current `ChatInterpreter` no longer creates goals directly. The roadmap also shows the team has already identified the right next refactors: `IBuildGoal`, semantic build locations, world KB setup verification, and configurable responses.

### Highest-priority actions

1. Make build origin a first-class typed value instead of relying on nullable ints plus zero sentinels.
2. Finish the inventory event-sourcing transition so drops are recorded by authoritative events, not by guessed block names.
3. Replace broad exception swallowing in gateway/adapter code with typed failures and explicit recovery semantics.
4. Collapse remaining chat-to-goal bridging into one intentional layer so intent ownership stays centralized.
5. Update user-facing docs/versioning so the repo’s current sprint state is visible and trustworthy.

## Risk register

| Rank | Finding | Severity | Confidence | Why it matters |
|---|---|---:|---:|---|
| 1 | Build origin still relies on brittle sentinel logic | High | 92% | Wrong build placement is a silent correctness bug, not a recoverable failure. |
| 2 | Inventory projection still appears to use block-name semantics for drops | High | 88% | This can corrupt the agent’s internal world model and false-complete goals. |
| 3 | Broad swallow paths still hide real integration failures | High | 87% | Hidden failures slow debugging and make runtime recovery unreliable. |
| 4 | Chat/intent layers still have policy drift between deterministic and LLM paths | Medium | 74% | Mixed ownership makes behavior harder to reason about and test. |
| 5 | Docs/roadmap drift from current code state | Medium | 95% | Operator trust and task planning suffer when docs lag the code. |

## Findings

### 1) Build origin is still a hidden-contract system
**Severity:** High  
**Confidence:** 92%

Evidence:
- `BuildGoal.HasExplicitOrigin` returns true when *any* of `OriginX`, `OriginY`, or `OriginZ` is set.
- `BuildGoalDecomposer` treats an explicit origin as scan-center coordinates and fills missing axes with zero.
- `HtnTaskLibrary.DecomposeBuild` and origin resolution treat `(0,0,0)` as a sentinel for “unset” / “scan for flat area.”

Why this is a bug:
- A single coordinate set by accident can flip the goal into “explicit origin” mode.
- `0` is a legitimate world coordinate; sentinel logic makes that value ambiguous.
- A partially specified origin can silently degrade into an unintended scan or build location rather than failing fast.

Recommendation:
- Replace nullable `int?` axes with a dedicated `BuildOrigin` value object that carries all three coordinates plus an origin source enum (`Explicit`, `Fact`, `AutoScanned`).
- Require either a fully specified origin or none.
- Remove `0`-as-unset semantics from planner code.

Why it is not duplicate work:
- The roadmap already lists `IBuildGoal` and semantic build locations as future work, but not the typed-origin refactor itself. Keep this as a distinct implementation task, not a repeat of the roadmap item.

### 2) World-state inventory projection still looks semantically lossy
**Severity:** High  
**Confidence:** 88%

Evidence:
- `WorldStateProjector.ApplyBlockMined` strips namespace and adds inventory under the mined block key.
- The sprint handoff explicitly says inventory should become event-sourced via authoritative `itemCollected` / post-action status updates.
- The bridge already parses `itemCollected` and `mineComplete`, which suggests the architecture is moving toward better signals.

Why this is a bug:
- `stone` does not actually become `stone` in inventory; it usually becomes `cobblestone`.
- `diamond_ore` does not become `diamond`.
- Any gather/craft completion logic built on guessed item keys can false-complete or never complete.

Recommendation:
- Make `ItemCollectedEvent` (and later `ItemCraftedEvent` / `ItemConsumedEvent`) the authoritative inventory source.
- Keep `BlockMinedEvent` as a mining telemetry event, not an inventory mutation event.
- Add a compatibility shim only where the adapter genuinely cannot provide the item-level event.

Why it is not duplicate work:
- Sprint 36/46 planning already talks about inventory event sourcing, but the current projector snapshot still exposes the old behavior. This is a live correction, not a duplicate task.

### 3) Several failure paths still swallow root cause broadly
**Severity:** High  
**Confidence:** 87%

Evidence:
- `RestMemoryGateway.UpdatePageAsync` catches all exceptions while reading the existing page and then continues with a fallback title.
- `WebSocketBridge` has a best-effort `catch {}` during reconnect cleanup, plus parse-fallback paths that only warn.
- The repo’s own docs emphasize “make every failure observable,” so these silent recoveries are now architectural debt, not convenience.

Why this is a bug:
- A permissions error, schema regression, transient outage, or deserialization bug can be flattened into “fallback behavior” and become much harder to diagnose.
- In a greenfield system, silent fallback is often more expensive than a visible failure because it trains the system to keep operating in a partially broken state.

Recommendation:
- Narrow catches to the exact expected failure class where possible (for example 404 vs auth vs parse).
- Surface structured error reasons upstream instead of substituting fallback behavior silently.
- Keep “best effort cleanup” only for disposal code paths where failure is genuinely non-actionable.

### 4) Intent ownership is better, but chat routing still has policy drift
**Severity:** Medium  
**Confidence:** 74%

Evidence:
- `IChatInterpreter` now returns `IntentDraft?`, and the deterministic `ChatInterpreter` is clearly documented as a parser-only fallback.
- `LlmChatInterpreter` still fast-paths a small set of intents, including `navigate`, even though the sprint handoff says only cancel/status/inventory/help should stay deterministic.

Why this matters:
- Mixed policy makes it harder to prove that intent classification behaves the same across LLM and non-LLM paths.
- If one path stays deterministic while the other is LLM-led, you will get subtle inconsistencies around reachability, distance gating, and follow commands.

Recommendation:
- Decide explicitly whether `navigate` is a safe deterministic fast-path or should be routed through the same LLM-first pipeline as other non-trivial intents.
- Keep the rule in one place only; right now the contract is split between docs, comments, and implementation.

### 5) Docs and sprint state are drifting from each other
**Severity:** Medium  
**Confidence:** 95%

Evidence:
- README still says v0.35.0 / Sprint 35 complete and 501+ tests.
- The roadmap at the latest commit says v0.40.0 and Sprint 41 in progress, with later work already completed and a new set of in-progress priorities.
- The sprint handoff and roadmap already list several future items that should not be re-created as new work.

Why this matters:
- Stale operator docs make it harder to trust the repo and easier to duplicate planning work.
- In an AI-agent-maintained codebase, documentation drift is itself a system reliability issue because future tasks are often generated from those docs.

Recommendation:
- Update README and the top-level roadmap summary together, not piecemeal.
- Include a single source of truth for current version/test counts/sprint state.
- Keep handoff docs and roadmap in sync whenever a sprint closes.

### 6) Remaining legacy seams should be removed, not just tolerated
**Severity:** Medium  
**Confidence:** 81%

Evidence:
- `HtnPlanner` still carries compatibility branches for older caller styles.
- `ChatInterpreter` preserves regex alias dictionaries even though gather/build/craft routing has moved to the LLM path.
- Several modules retain “fallback by default” patterns that were useful during migration but are now becoming accidental complexity.

Why this matters:
- These seams increase the chance of divergence between old and new paths.
- Greenfield projects benefit more from a narrow, explicit path than from long-lived compatibility branches.

Recommendation:
- Delete obsolete compatibility branches once tests cover the new pipeline.
- Move alias dictionaries into a single shared registry if they are still needed, and otherwise remove them from the hot path.
- Prefer one explicit intent bridge rather than several “just in case” paths.

## Architecture-level opportunities

The most valuable architecture move now is to consolidate ownership boundaries:

- One place to parse intent.
- One place to map intent to goal requests.
- One place to project world state.
- One place to store authoritative inventory changes.

That is the lever that will remove the most legacy behavior at once.

A good next-step shape would be:
- `IntentDraft` → `IntentManager` → `GoalFactory`
- `WorldEvent` → `WorldStateProjector`
- `ActionOutcome` / authoritative item events → inventory projection
- explicit `BuildOrigin` value object → build planning

That shape would also make future work like semantic build locations and configurable responses much easier, because the extension points will be obvious instead of implicit.

## Duplicate-task check

I cross-checked the roadmap and sprint handoff before making recommendations. I intentionally did **not** re-propose the following because they are already planned or in progress in the repo docs:
- `IBuildGoal` marker interface
- semantic build locations
- world KB setup / deployment verification
- configurable agent responses

I also treated the sprint-49 council sweep as already acknowledged work:
- TSK-0083 checkpoint event tests
- TSK-0085 SmeltGoal.HasFailed cleanup
- the backlog items the council already marked done

## Assumptions and open questions

### Assumptions
- The latest commit on the branch is the appropriate review target, even though some docs still reference earlier sprint numbers.
- The repo’s current direction is to keep moving away from fallback-heavy compatibility code and toward explicit typed flows.
- `0` remains a valid world coordinate and therefore should not be treated as an “unset” sentinel.

### Open questions
- Should `navigate` remain deterministic in `LlmChatInterpreter`, or should it be moved fully into the LLM intent path?
- Should `BlockMinedEvent` ever mutate inventory, or should only item-level events do that from now on?
- Is `RestMemoryGateway.UpdatePageAsync` supposed to upsert on any GET failure, or only on explicit missing-page cases?
- Should partial build origins be rejected outright instead of silently normalized?
- Do we want a hard cleanup pass that removes all old regex-based chat goal routing and compatibility branches in one go?

## Suggested order of execution

1. Replace brittle build-origin sentinels with a typed origin object.
2. Finish authoritative inventory projection.
3. Narrow silent catches in gateway/bridge code.
4. Remove remaining policy drift in chat routing.
5. Update docs/version state after the code changes land.
6. Delete compatibility seams once the new path is proven.

## Bottom line

This is a strong codebase with a clear architecture, but it still carries a few migration-era contracts that can silently break correctness. The biggest wins now are not new features; they are removing hidden assumptions, making state transitions typed, and eliminating fallback paths that can mask real failures.
