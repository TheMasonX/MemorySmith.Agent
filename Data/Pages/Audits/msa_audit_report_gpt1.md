# MemorySmith.Agent Code Audit

## Executive summary

This repo is unusually mature for a greenfield agent system, but it still carries several active compatibility bridges and documentation/version drift that can become technical debt quickly.

Top risks:

1. **Sprint / version drift is real and already visible in code comments and docs.** `README.md` says v0.51.0, `roadmap.md` says v0.51.1 / Sprint 51, while `Program.cs` says v0.52.0 / Sprint 52 and planning files contain Sprint 54-era task IDs. That means the task registry and roadmap are no longer a reliable source of truth for avoiding duplicate work.
2. **Legacy bridges are still on the hot path.** The architecture doc explicitly classifies several temporary shims: context-carry via `ActionData.Context`, context-key merge in `AgentBackgroundService`, and the `IntentDraftToGoal` transition layer. Those are acceptable only if they have a hard removal plan.
3. **Chat interpretation still relies on brittle heuristics.** The deterministic parser is intentionally limited, but `LlmChatInterpreter` also documents known misfires for `help` and `stop` in longer sentences. That is a safety-sensitive path.
4. **At least one production error path is still only logged to `Debug.WriteLine`.** `ActionQueue.ClearAndEnqueueAsync` swallows stop-callback failures without structured logging.
5. **Inventory reconciliation is intentionally non-idempotent in some paths.** `WorldStateProjector` accepts occasional double-count risk for mined items to avoid getting stuck at zero. That is a reasonable stopgap, but it is not a clean invariant.
6. **Some composition rules are hidden ordering contracts.** `Program.cs` requires `PlaceBlockGoalDecomposer` to be registered before `GatherGoalDecomposer` because `CanHandle` overlaps. That is fragile.

Overall confidence: **87%**. The repo is coherent, but the active runtime still depends on several temporary bridges and comment-level contracts that should be tightened before more features land.

## Highest-priority findings

| Priority | Finding | Risk | Confidence | Evidence |
|---|---|---:|---:|---|
| P0 | Documentation / sprint drift | Duplicate work, wrong assumptions, stale task selection | 98% | `README.md` v0.51.0; `roadmap.md` v0.51.1 / Sprint 51; `Program.cs` v0.52.0 / Sprint 52; planning comments reference Sprint 54 task IDs. |
| P0 | Hidden compatibility bridges still on runtime path | Legacy coupling, harder refactors, implicit contracts | 94% | `architecture.md` bridge registry; `ActionData.Context`; `AgentBackgroundService` context merge; `IntentDraftToGoal` transition layer. |
| P1 | Chat parsing remains brittle and safety-sensitive | Misfires on stop/help/navigate, wrong agent behavior | 93% | `ChatInterpreter.cs`; `LlmChatInterpreter.cs` fast-path and caveat comments. |
| P1 | Stop callback failures are not surfaced structurally | Silent failure, hard-to-debug runtime recovery issues | 92% | `ActionQueue.ClearAndEnqueueAsync` catches and writes only `Debug.WriteLine`. |
| P1 | Inventory truth is intentionally provisional in places | Double-count or drift until status reconciliation | 85% | `WorldStateProjector.ApplyBlockMined` and comments describing accepted double-count risk. |
| P2 | Decomposer ordering is a hidden contract | Registration-order bugs, brittle DI composition | 88% | `Program.cs` comment requiring `PlaceBlockGoalDecomposer` before `GatherGoalDecomposer`. |
| P2 | Legacy fact model still coexists with structured facts | Fact drift, duplicated writes, obsolete API surface | 80% | `WorldState.Facts`, `StructuredFacts`, obsolete `SetFact`, `ClearFactsByPrefix`. |
| P2 | LLM JSON salvage is regex-based | Partial parsing, malformed salvage, edge-case truncation bugs | 82% | `LlmChatInterpreter.ParseDecision` and `TryParseTruncatedJson`. |

## Detailed findings

### 1) Sprint/version drift is now a codebase health issue

**Evidence:**
- `README.md` advertises **v0.51.0** and **Sprint 51 Wave A complete**.
- `Data/Pages/roadmap.md` says **Current version: v0.51.1** and **Latest: Sprint 51**.
- `WebUI.Blazor/Program.cs` starts with **v0.52.0 Sprint 52**.
- Planning files contain **Sprint 54** task IDs and comments (`TSK-0200`, `TSK-0203`, `TSK-0205`, `TSK-0208`, etc.).

**Why it matters:**
The task registry, roadmap, and code comments are no longer synchronized. In a greenfield project, that creates exactly the kind of hidden duplication and stale assumptions you want to avoid.

**Confidence:** 98%

**Recommendation:**
Make a single source of truth for sprint/version metadata. The lowest-friction option is a generated manifest (or a `version.json`) that is updated by the sprint completion script and consumed by README, roadmap, and `Program.cs` at build time.

---

### 2) Compatibility bridges are still first-class runtime behavior

**Evidence:**
- `Data/Pages/architecture.md` explicitly lists temporary bridges: context-carry (`nearestX/Y/Z`), context-key merge in `AgentBackgroundService`, and the `IntentDraftToGoal` transition layer.
- `Agent.Core/Models/ActionData.cs` makes `Context` a mutable dictionary shared across the plan dispatch sequence.
- `WebUI.Blazor/AgentBackgroundService.cs` still merges `ActionData.Context` into tool arguments.
- `WorldState` still keeps both `Facts` and `StructuredFacts`, plus obsolete write paths.

**Why it matters:**
These are useful migration shims, but they are also the place where future bugs and implicit behavior will hide. The more the system grows, the harder these are to remove safely.

**Confidence:** 94%

**Recommendation:**
Assign explicit removal sprint targets to every bridge and make them enforceable. Replace the mutable cross-action bag with a typed `PlanContext`/`DispatchContext` as soon as the next routing layer is ready.

---

### 3) Chat interpretation still has brittle heuristics in a safety-sensitive path

**Evidence:**
- `Agent.Planning/ChatInterpreter.cs` has deterministic regex fast-paths for stop/status/help/inventory/navigation.
- `Agent.Planning/LlmChatInterpreter.cs` documents a caveat that the deterministic parser can misinterpret messages containing `help` or `stop` inside longer sentences.
- `LlmChatInterpreter` parses model output with regex extraction plus a truncated-JSON salvage path.

**Why it matters:**
This is the most visible behavior surface for the bot. False positives on stop/help or misparsed navigation can produce disruptive or unsafe behavior.

**Confidence:** 93%

**Recommendation:**
Keep the deterministic fast-paths only for truly zero-risk commands. For everything else, move toward a typed intent schema with stronger validation, and replace regex-based salvage with a structured incremental parser or bounded JSON parser.

---

### 4) `ActionQueue.ClearAndEnqueueAsync` still swallows stop failures too quietly

**Evidence:**
- `Agent.Core/Models/ActionQueue.cs` catches exceptions from `stopCallback` and writes only to `System.Diagnostics.Debug.WriteLine`.

**Why it matters:**
The queue clear still happens, but the failure is not visible in structured telemetry. That is the exact kind of observability hole that produces “it recovered, but why did the bot keep moving?” reports.

**Confidence:** 92%

**Recommendation:**
Inject a logger into `ActionQueue` or move the stop/send responsibility to the caller so failures are logged at warning level with action correlation IDs.

---

### 5) Inventory reconciliation is intentionally non-idempotent in one edge path

**Evidence:**
- `WorldStateProjector.ApplyBlockMined` adds inventory for self-dropping blocks.
- The comments explicitly accept a potential double-count if `ItemCollectedEvent` also fires, because the alternative is inventory stuck at zero forever.

**Why it matters:**
The current behavior is pragmatic, but it is not cleanly idempotent. It can bias inventory counts until a later status reconciliation corrects them.

**Confidence:** 85%

**Recommendation:**
Prefer a single authoritative source for each item class, or add a short-lived reconciliation buffer keyed by correlation/event ID so self-drop and collect events can be merged deterministically.

---

### 6) Decomposer registration order is a hidden contract

**Evidence:**
- `WebUI.Blazor/Program.cs` states `PlaceBlockGoalDecomposer MUST be registered before GatherGoalDecomposer` because `CanHandle` overlaps.

**Why it matters:**
This makes DI order part of correctness. That is brittle and easy to break during refactors or future feature additions.

**Confidence:** 88%

**Recommendation:**
Replace order-based resolution with explicit priority or mutually exclusive `CanHandle` predicates. At minimum, add a startup assertion that validates the intended ordering.

---

### 7) `WorldState` still exposes a legacy fact model alongside structured facts

**Evidence:**
- `WorldState` keeps `Facts`, `StructuredFacts`, and an obsolete `SetFact` path.
- `ClearFactsByPrefix` mutates both stores.

**Why it matters:**
Dual sources of truth increase the chance of mismatched planner behavior, especially when new facts are added in one path but not the other.

**Confidence:** 80%

**Recommendation:**
Treat `StructuredFacts` as the canonical store and make `Facts` a compatibility view only, or remove `Facts` entirely after the last downstream consumer is migrated.

---

### 8) LLM response parsing is still regex-salvage based

**Evidence:**
- `LlmChatInterpreter.ParseDecision` searches for the first brace-delimited blob and then parses JSON from it.
- `TryParseTruncatedJson` falls back to regex extraction for partially emitted JSON.

**Why it matters:**
This works until the model emits nested braces, code snippets, or malformed structured output. It is a reasonable recovery path, but not a strong long-term contract.

**Confidence:** 82%

**Recommendation:**
Keep the truncated salvage as a last-resort fallback only. For the main path, require a stricter schema envelope and validate it before any goal or action routing occurs.

## Implementation guidance

1. Sync versioning and sprint metadata first.
2. Put every temporary bridge in one registry with an owner, a removal criterion, and a sprint target.
3. Replace mutable cross-action context with a typed context object.
4. Promote all stop / recovery failures into structured logs with correlation IDs.
5. Make decomposer selection deterministic by design, not by registration order.
6. Shrink legacy truth stores until there is one canonical fact path.

## Assumptions and open questions

- I assumed the authoritative branch is `main` because that is what GitHub’s repo page and directory views exposed.
- I treated the latest accessible repository state as the source of truth, not the README or roadmap alone.
- I did **not** assume the Sprint 54 comments are a typo; they may indicate the codebase has already advanced beyond the roadmap document.
- Open question: is the sprint/task registry intentionally lagging behind the code, or was a documentation update missed?
- Open question: should `ActionData.Context` be kept as a temporary compatibility bridge, or can it be replaced in the next planning pass?
- Open question: is the double-count acceptance in `WorldStateProjector` still operationally safe for your current Minecraft adapter versions?

## Review scope note

This audit is grounded in the files that were directly accessible through the repository browser and file fetcher, plus the most relevant runtime/doc files. The main runtime pipeline, planning layer, world state projection, and deployment wiring were reviewed deeply; some nested files were only surfaced through directory listings and may still need targeted follow-up.
