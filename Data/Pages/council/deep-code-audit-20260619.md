# MemorySmith.Agent — Deep Code Audit Report
Date: 2026-06-19  
Scope: PR #1 (`sprint-5-tool-safety`), latest visible branch state on GitHub at review time.

## Executive summary

This branch shows real architectural progress: the repo is converging on a clearer split between agent core, planning, tools, memory, and the Mineflayer adapter, and the PR explicitly aims at tool safety, journaling, world-model depth, and planner extensibility. The strongest theme in the current code is "centralize the seams": a single tool dispatcher, a single planner router, and a world-model abstraction that separates observation from belief. citeturn983947view0turn844034view2turn844034view5turn498745view0

The main risk is that some of those seams are still shallow or inconsistent in practice. The highest-confidence issue is the tool-validation boundary: the dispatcher accepts too little schema shape and can misclassify JSON numbers as integers, which can then crash tool implementations that call `GetInt32()` directly. The second tier of risk is state aliasing and concurrent mutation in the world model and journal, which can undermine the determinism this branch is trying to improve. citeturn866736view0turn607972view2turn583382search1turn498745view2turn844034view4

## Top findings

### 1) Tool schema validation is too permissive for integers and too narrow overall
**Confidence: 92%**

`ToolDispatcher.ValidateAgainstSchema` only checks `type`, `properties`, and `required`, and it explicitly accepts unknown schema types. Its integer check relies on `GetRawText().Contains('.')`, so numeric forms like scientific notation can slip through as "integers" even when they are not exact integers. `FindFlatAreaTool` then calls `GetInt32()` directly on the validated element, and `JsonElement.GetInt32()` can throw `FormatException` when the value cannot be represented as an `Int32`. That means the dispatcher can green-light a payload that later blows up inside the tool. citeturn866736view0turn607972view2turn583382search1

Why this matters: the branch's safety boundary is supposed to protect the tool layer from untrusted args, but this check is not strong enough to be a reliable guard rail. The fix is not only "better integer parsing"; it is moving toward either stricter schema enforcement or typed parsing per tool. citeturn866736view0turn844034view0

### 2) The dispatcher assumes tools do not throw, but tool code still throws for invalid inputs
**Confidence: 88%**

`ToolDispatcher.CallAsync` does not wrap `tool.ExecuteAsync(...)` in a try/catch. Several tools are written to throw on missing or invalid arguments rather than returning a `ToolResult` failure. For example, `SearchMemoryTool` throws an `ArgumentException` when `query` is absent, and `FindFlatAreaTool` dereferences numeric args with `GetInt32()`. In practice, that makes the dispatcher's "returns failure result, not throws" contract incomplete. citeturn866736view0turn607972view1turn607972view2

Why this matters: one malformed request from the LLM, API, or adapter can escape the safety layer and fail the dispatch loop harder than intended. The architectural seam here should own both validation and exception normalization. citeturn866736view0turn844034view4

### 3) World-model state is still alias-prone and can leak mutations across snapshots
**Confidence: 86%**

`WorldModel` stores `ObservationState.Inventory` directly into belief, and the constructor seeds both observed and belief state from the same `empty` dictionary instance. `ObservationState` and `BeliefState` only promise `IReadOnlyDictionary`/`IReadOnlyList`, which is an interface contract, not an immutability guarantee. If any caller keeps a mutable reference and mutates it later, the model's historical state can change behind its back. That weakens both prediction/reconciliation correctness and the "observed vs believed" split this branch is trying to formalize. citeturn498745view2turn319892view0turn319892view1

Why this matters: the world model is intended to be the cognitive core, but aliasing turns it into a passive wrapper around external mutable objects. The likely fix is to deep-copy at the seam and keep the internal model structurally immutable. citeturn844034view5turn498745view2

### 4) The journal is bounded, but only approximately so under contention
**Confidence: 72%**

`AgentJournal` uses a `ConcurrentQueue` and trims only one item when `Count` exceeds `MaxEntries`. The comments explicitly describe this as best-effort, not strict, so it is not a correctness bug in the narrow sense; it is a design tradeoff. Still, the current implementation can temporarily exceed the bound under concurrent writes, and `Query`/`Recent` rely on concurrent enumeration plus `Reverse()` rather than a locked snapshot. That is acceptable for observability, but it should be documented as approximate rather than treated as a hard invariant. citeturn498745view1turn844034view4

Why this matters: if downstream code starts depending on a hard cap or deterministic ordering, the current seam will surprise it. This is a good candidate for a deliberately chosen journal strategy: either a true bounded deque/lock, or explicit "eventually bounded" semantics. citeturn498745view1turn844034view4

### 5) Planner routing is improving, but there are two sources of truth for decomposition
**Confidence: 81%**

`PlannerRouter` now selects either a decomposer-backed planner or the HTN planner, but `HtnPlanner` still contains a hardcoded four-path decomposition switch. That means decomposition logic is split across two modules with overlapping responsibilities. The branch is moving toward a registry-based architecture, but the old planner still owns meaningful routing decisions. citeturn844034view2turn133072view0turn844034view1

Why this matters: duplicated routing logic makes the module shallower than it needs to be and raises the risk that new goal types are wired in one place but not the other. From a codebase-architecture perspective, the stronger seam would be one planner-selection module and one decomposition module, not a hybrid of both. citeturn844034view2turn133072view0

### 6) The Mineflayer chat filter is better, but still brittle by construction
**Confidence: 65%**

The adapter now filters a broad set of server/system messages, including teleports, join/leave, time, gamemode, kills, give, and clear responses. That is a genuine improvement over forwarding everything into the LLM pipeline. But the mechanism is still a pattern list, and new server phrasing will require continual maintenance. citeturn812356view0turn812356view1turn719665view4

Why this matters: this seam works today, but it is a classic "regex firewall" and will age poorly unless the adapter grows a more structured message classification path. citeturn812356view0turn844034view5

## Architecture and codebase-health opportunities

### A. Make tool execution strongly typed at the seam
Current state: tools expose `JsonElement InputSchema`, the dispatcher performs a partial schema check, and each tool then parses its own arguments. That splits responsibility in a way that is easy to get subtly wrong. A better seam would be: validate once, parse once, then call a typed tool implementation. That would deepen the tool layer and reduce repeated `TryGetProperty` / `GetInt32` logic. citeturn866736view0turn607972view2turn607972view0

### B. Deepen the world model into immutable snapshots
Current state: `WorldModel` reads and writes through mutable dictionaries/lists hidden behind read-only interfaces. A better seam would make `ObservationState`, `BeliefState`, and internal model storage immutable by construction, with explicit copy-on-write only at the projector boundary. That would improve locality and make prediction/reconciliation easier to test. citeturn498745view2turn319892view0turn319892view1turn844034view5

### C. Collapse planner selection into one obvious place
Current state: `PlannerRouter` is a new seam, but `HtnPlanner` still contains legacy routing logic. A cleaner architecture would make the router the only selection point and turn the HTN planner into a pure decomposition engine. That matches the repository's own "deterministic-first" direction and reduces codepaths to audit. citeturn844034view2turn133072view0turn974324view0

### D. Turn journal semantics into a deliberate choice
Current state: `AgentJournal` is a concurrent queue with approximate bounds. That is fine if the product requirement is "good enough tracing," but not if the journal becomes a source of truth. Decide whether the journal is an operational log or a reliable event store, and implement the seam accordingly. citeturn498745view1turn844034view4

## Supplemental implementation notes

### Tool safety
The dispatcher is now the choke point for tool registration and execution, which is the right architectural direction. The biggest remaining gap is that the schema validator is intentionally minimal, and the code still lets some malformed payloads escape into tools that throw. citeturn866736view0turn607972view2

### World model
The branch has a real observation/belief split, plus reconciliation and uncertainty tracking. That is a meaningful design step forward. The next leverage point is eliminating shared mutable state so the model's predictions are actually trustworthy snapshots. citeturn498745view2turn844034view5

### Planner extensibility
The registry-based decomposer pattern is a good move. It becomes significantly better once the old hardcoded HtnPlanner routing path is deleted or demoted to a pure fallback with no special-case branching. citeturn844034view1turn844034view2turn133072view0

### Mineflayer bridge
The system-message filter is broad enough to block obvious server spam and prevent avoidable LLM calls. It should still be treated as an evolving allow/deny seam, not a permanent solution. citeturn812356view0turn812356view1

## Assumptions

- I reviewed the branch as exposed through GitHub's current public PR view and raw file paths, not a local clone. citeturn983947view0turn775980view0
- I treated the branch named `sprint-5-tool-safety` as the current PR head because GitHub shows PR #1 merging that branch into `main`. citeturn983947view0
- I focused on the most recently visible sprint work and the highest-risk seams; I did not attempt a line-by-line audit of every file in the repository. citeturn621867view2turn621867view4

## Open questions

- Should tool execution be allowed to throw, or is `ToolResult` supposed to be the only failure channel? **HUMAN REVIEWER:** toolresult only, channel all failures into traceable results and explicit control flow (modern result pattern)
- Is the world model intended to preserve historical snapshots immutably, or is aliasing acceptable for now? **HUMAN REVIEWER:** snapshots should be immutable states in time
- Should `PlannerRouter` fully replace the hardcoded branch logic in `HtnPlanner`, or is the current overlap intentional? **HUMAN REVIEWER:** modernize and prefer flexible over hardcoded
- Is the journal meant to be a bounded diagnostic buffer or a durable source of truth?

## Suggested priority order

1. Fix the tool-validation / exception boundary.
2. Eliminate mutable aliasing in the world model.
3. Collapse planner routing into one seam.
4. Decide whether the journal needs strict bounds or merely approximate observability.

## Source references used
- PR and branch context: `turn983947view0`, `turn775980view0`
- Tool dispatcher: `turn866736view0`
- Tool test coverage: `turn150531view0`, `turn109336view0`
- Find-flat-area tool: `turn607972view2`
- Search-memory tool: `turn607972view1`
- Agent journal: `turn498745view1`, `turn844034view4`
- World model: `turn498745view2`, `turn844034view5`, `turn319892view0`, `turn319892view1`
- Planner router / HTN planner / decomposer registry: `turn844034view1`, `turn844034view2`, `turn133072view0`
- Mineflayer adapter: `turn812356view0`, `turn812356view1`, `turn719665view4`
- Matt Pocock architecture skill: `turn518291view0`
