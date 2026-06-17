# Sprint 4b Audit — Code Audit Council Review

**Date:** 2026-06-16
**Scope:** Full codebase audit + Sprint 4b chat history + SignalR dashboard push
**Trigger:** Prompted code audit from external reviewer
**Commits reviewed:** main, including Sprint 4b (ChatHistory, LlmChatInterpreter, SignalR push)

---

## 1. What Was Audited

Full module-level audit of 10 .csproj projects, 20+ test files, MineflayerAdapter Node.js bridge,
and WebUI.Blazor dashboard. Focus: goal identity, tool safety, runtime reliability,
observable memory, world model depth, planner architecture, memory lifecycle.

---

## 2. Audit Findings

### 2.1 — What the codebase does well

| Strength | Confidence | Evidence |
|----------|-----------|----------|
| Clean module separation (Core/Planning/Tools/Memory/World/WebUI) | 0.95 | 6 well-separated .csproj files with clear dependency direction (Core ← everything) |
| Typed event pipeline (16 sealed WorldEvent subtypes) | 0.95 | WorldEvents.cs → WebSocketBridge.cs → WorldStateProjector.cs → AgentBackgroundService.cs all pattern-match |
| Deterministic-first chat interpretation | 0.90 | ChatInterpreter (regex/alias) runs first; LlmChatInterpreter only falls back to LLM after pattern fail + distance gate + rate check |
| Strong test coverage for Phase 5 | 0.85 | 20+ test files, dedicated mocks (MockWorldAdapter, MockMemoryGateway, MockPlanner), WorldStateBuilder |
| LLM provider abstraction | 0.90 | ILlmProvider with 5 concrete providers via LlmProviderFactory; ChatOptions for all tunables |
| Bounded chat history context window | 0.90 | ChatHistory (5-turn rolling buffer, lock-free via Interlocked.CompareExchange) |
| Action-carried context between tool calls | 0.80 | ActionData.Context dict enables SearchMemoryTool → bestPageId → GetPageTool flow |
| Build goal crafting chain | 0.85 | HtnTaskLibrary.DecomposeBuild emits 5-phase plan with crafting dependencies and coal-for-torch logic |

### 2.2 — Critical gaps found

| # | Gap | Severity | Module | Seat(s) flagging |
|---|-----|----------|--------|------------------|
| 1 | **No tool schema validation** — ToolDispatcher.CallAsync has `// TODO: validate against InputSchema` | **CRITICAL** | Agent.Tools | Safety, Architect, Integrator |
| 2 | **WorldState.Facts unbounded** — every event stores facts, no TTL, no pruning | **HIGH** | Agent.Core | World Modeler, Architect |
| 3 | **Execution context lost across replans** — `_context.Clear()` in ReplanAsync | **HIGH** | WebUI.Blazor | Planner, World Modeler |
| 4 | **No per-action timeout** — DispatchActionsAsync awaits without Timeout | **MEDIUM** | WebUI.Blazor | Runtime, Safety |
| 5 | **ToolRegistry/ToolDispatcher duplication** — incomplete refactor | **MEDIUM** | Agent.Tools | Architect |
| 6 | **No failure classification** — HasFailed is boolean, no reason | **MEDIUM** | Agent.Core | Planner |
| 7 | **No graceful Node.js shutdown** — Kill(entireProcessTree) without SIGTERM | **LOW** | Agent.World.Minecraft | Runtime |
| 8 | **/api/agent/command bypasses tool registration** | **MEDIUM** | WebUI.Blazor | Safety, Runtime |
| 9 | **Hardcoded decomposer registration** — adding a goal requires editing HtnTaskLibrary + HtnPlanner | **MEDIUM** | Agent.Planning | Planner, Architect |
| 10 | **No execution journal persisted to MemorySmith** | **MEDIUM** | Cross-cutting | World Modeler |

### 2.3 — False positives from audit prompt

| Concern from prompt | Actual state | Confidence |
|---------------------|--------------|------------|
| "Goal replaced by generic placeholder during replan" | Goal object (Name/Description/Phases/Parameters) survives unchanged; only ActionPlan is rebuilt | 0.75 |
| "Minecraft logic leaks into planner/memory/tools" | Clean separation — planner operates on IGoal, tools on ITool, world adapter on IWorldAdapter | 0.90 |
| "Magic numbers hidden in code" | Tunable values are named constants (e.g., MaxTurnsDefault, TorchesPerCraft, ItemCacheTtlSeconds) or options (ChatOptions, MinecraftAdapterConfig, RestMemoryGatewayOptions) | 0.85 |
| "Agent.Vision and Agent.Personality are stubs" | True — interfaces exist, no implementations. Expected at this phase per roadmap | 0.95 |

---

## 3. Council Verdicts (6 seats)

### Seat 1 — Architect (Modules & Seams): 🟡 CONDITIONAL PASS

The core module separation holds. Tool dispatch is shallow (no validation, just a dict lookup).
ToolRegistry/ToolDispatcher duplication is an incomplete refactor. IAgent interface is defined
but never implemented. The seams at IWorldAdapter/IMemoryGateway/IPlanner/IChatInterpreter are
correct and already enable testing. **Condition:** deepen ToolDispatcher with schema validation.

### Seat 2 — Safety Engineer (Tool Safety): 🔴 FAIL

Schema validation is explicitly TODO. Every tool defines an InputSchema and ignores it.
LLM-generated arguments pass through unvalidated. `/api/agent/command` accepts arbitrary tool names.
This is the highest-risk gap in the codebase. **Condition:** add JSON Schema validation to the
dispatch path and lock down the command endpoint.

### Seat 3 — Planner Specialist (Goal Identity & Replanning): 🟡 CONDITIONAL PASS

Goal identity is preserved through replans. The concern about "generic placeholder goals" is not
matched by the code — IGoal objects with Name/Description/Phases survive replanning. However,
execution context (`_context`) is wiped on replan, losing inter-action state. Decomposers are
hardcoded in HtnTaskLibrary. **Condition:** preserve or reconstruct execution context across replans.

### Seat 4 — World Modeler (World State & Memory): 🟡 CONDITIONAL PASS

WorldStateProjector is a well-designed pure function. But Facts grow without bound — every event
adds debug facts that are never pruned. No distinction between observed/inferred/durable facts.
No execution journal pushed to MemorySmith. **Condition:** cap Facts, add Fact metadata.

### Seat 5 — Runtime Engineer (Reliability): 🟡 CONDITIONAL PASS

Three concurrent tasks with proper CancellationToken plumbing. Exponential backoff reconnect.
ChatConsumerAsync offloads LLM latency correctly. But: no per-action timeout, no process heartbeat,
no plan-staleness detection. Graceful shutdown is missing (SIGTERM before SIGKILL).
**Condition:** add per-action timeout.

### Seat 6 — Integrator (Tests & Mocks): 🟢 PASS

20+ test files with strong coverage of tools, planner, chat interpreter, background service,
and world state projector. Mock infrastructure is clean. Gap: real HtnPlanner is not tested
in CI through AgentBackgroundService (MockPlanner only). No integration tests (expected at this phase).

---

## 4. Prioritized Action Plan (Sprint 5)

| Priority | Task | Effort | Seats |
|----------|------|--------|-------|
| **P0** | Tool schema validation in ToolDispatcher.CallAsync | M | 1,2,6 |
| **P0** | Lock down /api/agent/command to registered tools | S | 2,5 |
| **P1** | Cap WorldState.Facts + add Fact record with Source/Timestamp | M | 1,4 |
| **P1** | Preserve execution context (ActionData.Context) across replans | S | 3,4 |
| **P1** | Add per-action timeout (ActionTimeout) to dispatch loop | S | 2,5 |
| **P2** | Consolidate ToolDispatcher/ToolRegistry — pick one, delete other | S | 1 |
| **P2** | Add FrozenFailureClassifier with at least 3 failure categories | M | 3 |
| **P2** | SIGTERM before SIGKILL in MinecraftAdapter.DisconnectAsync | S | 5 |
| **P3** | Decomposer registry (replace hardcoded HtnTaskLibrary registration) | L | 1,3 |
| **P3** | Push execution journal entries to MemorySmith via RestMemoryGateway | M | 4 |
| **P3** | Implement or delete IAgent interface | S | 1 |

**Council disposition:** APPROVED for Sprint 5, with P0 items as gates. P1 items should be attempted.
P2/P3 items are stretch goals.

---

## 5. Open Questions (for future sprints)

1. **What state must survive reconnects?** Currently: WorldState, CurrentGoal, and pending actions survive.
   Chat history, tool context, and Facts do not. Should they?

2. **Which actions should trigger replanning vs abort?** Currently: maxConsecutiveFailures (3) triggers
   HasFailed on the goal, which triggers a full replan. Should some failures skip replanning and abort directly?

3. **What belongs in MemorySmith vs in-process memory?** Currently: item definitions, blueprints,
   and Search/CreatePage results go to MemorySmith. WorldState, plan, chat history are in-process.
   No execution journal exists. Should the journal go to MemorySmith?

4. **Should the world model be event-sourced?** Currently it's a state snapshot updated by projection.
   The events themselves are not persisted. An event log would enable replay and debugging.

5. **Minimum reliable LLM fallback path?** Currently: patterns → LLM with distance gate + rate limit.
   When LLM fails, it falls back to pattern-only. The planner has no LLM fallback at all — it throws.
   What is the minimum safe LLM planner fallback?

6. **Authoritative sprint/phase status?** README says Phase 0 (Skeleton) is "Done." Code is substantially
   beyond that — Phase 5 (LLM chat, tool dispatch, Minecraft integration) is more accurate based on
   implemented features.

---

## 6. Confidence Summary

| Domain | Confidence |
|--------|-----------|
| Goal identity preservation | 0.75 |
| Tool safety boundary | 0.30 (failing) |
| Runtime reliability (current) | 0.65 |
| Runtime reliability (long-running) | 0.45 |
| World model depth | 0.50 |
| Planner extensibility | 0.45 |
| Memory lifecycle | 0.30 (failing) |
| Test coverage for existing paths | 0.80 |
