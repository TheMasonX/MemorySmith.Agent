# MemorySmith Blazor Dashboard Improvement Plan — Delta Audit
**Revision:** 2026-06-25  
**Compared against:** “MemorySmith Blazor Dashboard Improvement Plan (Revision 2)”  
**Scope:** Delta only — this report keeps only the parts that need to change after checking the repo state at commit `18648691d8abd5ad84ee255795b76ffdc0aca131`.

## What still holds up

The original audit’s core warning is still directionally correct: the dashboard should not become a second source of truth that reconstructs runtime state from logs, ad hoc UI state, or transport events. The repo already models world state as a read-model and not as a log buffer, so the design pressure toward a clearer dashboard contract is real. `StateManagerImpl` owns a thread-safe `WorldState` read-model and applies `WorldEvent`s through `WorldStateProjector`, which is exactly the kind of foundation a dashboard snapshot layer would build on. fileciteturn17file0

## What needs to be corrected

### 1) “The dashboard infrastructure is already mostly complete” is too strong

The repo docs still classify Dashboard & Monitoring as **In Progress**, with Sprint 41–42 improvements explicitly queued: event bus decoupling, snapshot store, broadcast service, background-service refactor, and SignalR normalization. That is not a “matured and mostly finished” subsystem yet; it is an active refactor target. fileciteturn14file0turn15file0

### 2) “SignalR disappears as an architectural concept” is not supported

The current implementation still sends dashboard updates through SignalR directly. `DashboardPublisherImpl` injects `IHubContext<AgentHub>`, reads the current `IStateManager` state, and publishes an anonymous payload to the `agentStatusUpdated` client method. It also catches exceptions and only logs a warning. So the correct delta is not “SignalR is gone”; the correct delta is “SignalR is still the transport and still coupled to the UI publisher.” fileciteturn18file0

### 3) “Dashboard as the authoritative read model” is an architectural goal, not the current state

The repo already has a modular runtime decomposition in `Program.cs`: `IIntentManager`, `IPlanningManager`, `IExecutionManager`, `IRecoveryManager`, `IStateManager`, and `IDashboardPublisher` are already separate services. That means the dashboard refactor should be framed as a continuation of the existing manager split, not as a new all-encompassing projection layer that the repo somehow already has. fileciteturn16file0

### 4) The proposed operator-console scope is broader than the repo currently supports

The current dashboard feature doc lists a fairly small surface area: status panel, goal tracker, inventory panel, chat log, tool console, action log, plus four SignalR channels. It does **not** yet document the larger console you proposed (timeline, build visualization, planner drill-down, runtime metrics, editable config panel, multi-agent view, etc.). Those ideas may be good backlog items, but they are not supported as present-tense conclusions from the current branch. fileciteturn14file0

## Revised delta recommendations

1. Reword the dashboard architecture section from “already exists” to “partially implemented, with a real-world read-model foundation already in place.”
2. Replace “introduce a runtime projection layer” with “promote the existing `IStateManager`/`WorldStateProjector` path into an explicit dashboard snapshot contract.”
3. Treat `DashboardPublisherImpl` as an existing coupling point that should be simplified, not as something already replaced by an event bus.
4. Keep the event-bus / snapshot-store / broadcaster plan, but mark it as the **next concrete refactor**, not as a description of the current codebase.
5. Downgrade the operator-console ideas (timeline, metrics, configuration, build visualizer) to future epics until the repo actually contains their backing contracts or components.

## Net delta versus the original audit

The original audit was too advanced in its assumptions about what already exists. The repo is **not** at the “dashboard as authoritative read model” stage yet. What does exist is a good foundation: a thread-safe world-state read model, a dedicated dashboard publisher, and a modular runtime split. The next audit should build on that foundation and focus on how to turn it into a real dashboard snapshot pipeline without overstating the current implementation. fileciteturn16file0turn17file0turn18file0turn14file0
