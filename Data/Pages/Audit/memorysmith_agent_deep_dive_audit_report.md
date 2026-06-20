# memorysmith.agent Deep-Dive Audit Report
Date: 2026-06-19  
Scope: current repository state exposed through GitHub pages/raw files, plus the sprint/roadmap documents and PR #1 plan. I could not verify branch-only deltas beyond the surfaced files, so findings are grounded in what is directly visible here.

## Executive summary

The codebase has a solid architectural direction: the repo is organized around clear bounded contexts, a consolidated tool dispatcher, and a roadmap that explicitly prioritizes safety controls, journaling, and planner extensibility. The problem is that several of the most important safety claims in the roadmap and design docs are not yet reflected in the live code paths I inspected. In particular, the dispatcher still skips argument schema validation, and the `/api/agent/command` endpoint accepts arbitrary command strings rather than restricting execution to registered tools. Those two gaps directly weaken the “deterministic first” safety model that the roadmap and sprint plan describe. citeturn780377view1turn780377view2turn636414view0turn194199view0turn958425view1

The next most important risk is concurrency: the action queue is a plain `Queue<ActionData>` without synchronization, while queue operations are reachable from HTTP endpoints and background service logic. That creates a realistic race-condition surface for lost actions, corrupted queue state, or hard-to-reproduce scheduling bugs. citeturn281854view4turn958425view1turn201601view0turn201601view2

There are also medium-confidence architecture gaps around chat routing and configuration consistency. The chat options expose a “closest agent responds” distance limit, but the deterministic `ChatInterpreter` never uses that value in its directed-at-bot heuristic. Separately, the sample config surfaced here does not include an `Agent:Chat` section even though `Program.cs` binds one, which makes the live defaults easy to misunderstand. citeturn186741view0turn249998view4turn958425view1turn138925view0

## Highest-priority findings

### 1) Tool argument validation is still not enforced in the dispatcher
**Severity:** Critical  
**Confidence:** 98%

The design docs and sprint plan state that `ToolDispatcher.CallAsync` validates tool arguments against `InputSchema` before execution. The actual dispatcher code still contains a TODO for exactly that validation and then invokes the tool immediately. That means a caller can reach tool execution with structurally invalid inputs, which is the exact class of failure the sprint 5 safety work was meant to prevent. citeturn780377view2turn636414view0turn194199view0

**Why this matters:** this is not just a missing test; it is a control-plane safety gap. Every tool execution path inherits the risk until validation is enforced centrally.  
**Recommendation:** implement schema validation in `ToolDispatcher.CallAsync`, fail closed on invalid payloads, and add tests that prove invalid argument shapes never reach tool execution.  

### 2) `/api/agent/command` is not locked to registered tool names
**Severity:** Critical  
**Confidence:** 97%

The sprint plan explicitly calls for the command endpoint to accept only registered tool names. The exposed API endpoint currently enqueues `req.Command` directly as an action without checking whether it maps to a registered tool. This means arbitrary command strings can be injected into the queue, bypassing the intended registry boundary. citeturn636414view0turn780377view2turn958425view1

**Why this matters:** it undermines the same “tool safety” boundary from a different angle. Even if the dispatcher later rejects or mishandles a bad command, the API is still allowing untrusted names into the system.  
**Recommendation:** validate command names at the HTTP boundary against the tool registry, return a structured 4xx error for unknown names, and add regression tests for both known and unknown commands.

### 3) The action queue is not thread-safe
**Severity:** High  
**Confidence:** 90%

`ActionQueue` wraps a plain `Queue<ActionData>` with no locking or concurrent collection. At the same time, the public API endpoints enqueue actions, and the background service also reads and drains actions. That combination is enough to create real race conditions under concurrent load. citeturn281854view4turn958425view1turn201601view0turn201601view2

**Why this matters:** even if the queue is “usually fine” in light usage, the system has multiple asynchronous producers. Queue corruption or lost actions here would be hard to diagnose and would look like random agent behavior.  
**Recommendation:** move to a thread-safe queue abstraction or guard all queue access with a single synchronization strategy. Add a stress test that concurrently enqueues and dequeues from multiple producers.

## Architecture and codebase-health assessment

The repository’s architecture is already better than average because it separates agent core, planning, tools, memory, and world-adapter concerns, and the roadmap explicitly frames these as bounded contexts. That separation gives you a strong foundation for the kind of Matt Pocock-style “make the architecture express the domain” cleanup work the prompt asked for. citeturn780377view1turn674559view0

The main architectural issue is not structure, but enforcement. Several docs describe safety and routing guarantees as if they are already present, while the code I inspected shows those guarantees are either incomplete or missing. That kind of documentation/code drift is dangerous because it creates false confidence around the exact layers that should be trusted most. citeturn780377view2turn636414view0turn194199view0turn958425view1

A second architectural smell is that runtime behavior appears to depend on a mix of central dispatcher logic, endpoint-specific checks, and background-service conventions. That makes the system harder to reason about than it needs to be. The cleaner model would be: HTTP/API validates request shape, dispatcher validates tool schema, queueing is thread-safe, and the executor is the only place where action semantics are applied. citeturn958425view1turn194199view0turn281854view4

## Sprint-plan overlap check

I checked the surfaced roadmap so I would not duplicate already-planned work. The upcoming Sprint 24 items already include: an integration test for `TryInterruptOnDamage`, a `GatherGoalDecomposer` `TargetCount` pass-through fix, a `TimeProvider` abstraction for testable time-dependent logic, and an `IWorldObservationGateway` design note. Those should be treated as existing backlog items rather than re-proposed work. citeturn780377view0

That means the issues I’m calling out above are mostly not duplicates of the listed Sprint 24 tasks. Instead, they are either missing enforcement of already-documented safety goals, or cross-cutting code health issues that should be added to the backlog explicitly. citeturn780377view0turn636414view0turn780377view2

## Detailed supplemental findings

### A. Dispatcher safety gap
- The dispatcher is supposed to be the central safety gate.
- The code still has a direct “execute first” path with validation deferred.  
- This is the highest-value fix because it reduces the blast radius of bad inputs across the whole system. citeturn194199view0turn780377view2

### B. HTTP command boundary gap
- The `/api/agent/command` endpoint should be treated as a trust boundary.
- Right now it acts as a thin enqueue wrapper and does not verify registry membership.
- This should be tightened before adding more tools or exposing the endpoint more broadly. citeturn958425view1turn636414view0

### C. Queueing/concurrency risk
- A normal `Queue<T>` is fine in single-threaded code.
- It is not fine when used by multiple producers/consumers without a lock.
- This is one of those bugs that may stay invisible until the agent is under load, which makes it especially worth fixing early. citeturn281854view4turn201601view0turn201601view2

### D. Chat routing mismatch
- `ChatOptions` exposes `MaxResponseDistanceBlocks`.
- The deterministic interpreter’s directed-at-bot heuristic does not use that value.
- In a multi-agent or multi-player setup, that means the effective behavior is less precise than the configuration implies. citeturn186741view0turn249998view4

### E. Configuration/documentation drift
- `Program.cs` binds `Agent:Chat`.
- The surfaced sample config does not show that section.
- That makes the runtime defaults less discoverable and increases the chance of “it works on my machine” configuration mismatch. citeturn958425view1turn138925view0

### F. Goal-planning consistency note
- The current goal factory exposes `GatherItem:{itemId}` and `Build:{blueprintId}` dynamic goals, but I did not find a `TargetCount` symbol in the factory file I inspected.
- The roadmap’s Sprint 24 item for a `GatherGoalDecomposer` `TargetCount` pass-through fix therefore looks like a valid upcoming task rather than duplicate work, but the decomposer itself should be checked before locking the sprint scope. citeturn109152view1turn109152view2turn780377view0

## Recommended action order

1. Enforce schema validation in `ToolDispatcher.CallAsync`.  
2. Lock `/api/agent/command` to registered tool names and fail closed.  
3. Make action queue access thread-safe.  
4. Reconcile chat-routing configuration with actual interpreter logic.  
5. Add one pass of documentation cleanup so design docs and runtime behavior match. citeturn194199view0turn958425view1turn281854view4turn186741view0turn249998view4

## Assumptions and open questions

**Assumptions**
- This audit is based on the current repository state surfaced through GitHub pages/raw files, not a local checkout.
- The surfaced roadmap is current enough to represent the active sprint plan.
- The safety and routing docs are intended to describe the actual runtime behavior, not just the desired future state. citeturn780377view0turn780377view1turn780377view2

**Open questions**
- Does the PR #1 branch contain additional code not visible in the surfaced pages that closes any of the gaps above?
- Is the command endpoint intentionally permissive for some internal-only workflow, or is the current behavior simply unfinished?
- Is the intended concurrency model single-threaded by construction, or should the queue be hardened for real multi-producer use? citeturn636414view0turn958425view1turn281854view4

## Evidence base

Primary sources reviewed included the repository root/README, PR #1 discussion, roadmap and architecture pages, AGENTS.md, `ToolDispatcher`, `ActionQueue`, `ChatInterpreter`, `ChatOptions`, `GoalFactory`, `Program.cs`, and `appsettings.json`. citeturn674559view0turn636414view0turn780377view0turn780377view1turn780377view2turn641960view0turn194199view0turn281854view4turn249998view4turn186741view0turn109152view1turn958425view1turn138925view0
