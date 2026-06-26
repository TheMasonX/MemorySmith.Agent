# MemorySmith.Agent UI Operator Console Roadmap
**Date:** 2026-06-25  
**Purpose:** A detailed implementation plan for turning the current Blazor dashboard into a practical operator console that can replace coarse terminal logging during active development.

---

## 1. Why this document exists

The current dashboard should not be treated as a finished product. It has enough runtime plumbing to justify investing in the UI now, but the highest-value work is still ahead: surfacing the agent’s internal state in a way that makes debugging fast, visible, and routine.

The goal of this plan is not “make the dashboard prettier.” The goal is:

> **Reduce the time it takes to understand what the agent is doing, why it is doing it, and why it failed.**

That means the first version of the UI should answer questions like:

- What goal is active right now?
- What task or action is currently in flight?
- What changed in the world state?
- What was the last failure?
- Why did the planner replan?
- What did the LLM say, and what was kept or rejected?
- What tool call is currently blocking progress?

If the UI cannot answer those questions faster than terminal logs, it is not yet doing its job.

---

## 2. Design principles

### 2.1 Build for debugging first
The dashboard is an operator console, not a marketing site. The fastest path to value is a UI that makes failures obvious, not a UI that is visually busy.

### 2.2 Keep components thin
Razor components should mostly display state and forward user intent. They should not contain business logic, transport logic, or projection logic.

### 2.3 Separate truth from evidence
Use a clear distinction between:

- **Current state**: what is true right now
- **Event history**: what happened recently
- **Logs**: diagnostic evidence

These should be visually related, but not merged into one amorphous feed.

### 2.4 Favor stable contracts over ad hoc payloads
Every UI page should bind to a small, stable read model or DTO. Do not let pages depend directly on raw SignalR payloads or background-service internals.

### 2.5 Ship the “replace terminal logs” version first
The first useful release should be a small, sharp dashboard that reduces console scrolling. Fancy visualizations can come later.

---

## 3. Current-state assumptions

This roadmap assumes the backend already has enough foundation to justify a richer UI:

- a runtime read model exists
- dashboard publishing already exists
- SignalR is already in the stack
- world state is already being projected into a read model
- the runtime is already split into manager-style services

That means the UI effort should build on top of the current runtime, not wait for a perfect new backend architecture.

---

## 4. Product outcome: what “done” looks like

The first real UI milestone should feel like this:

1. Open the dashboard.
2. See the agent’s current goal, state, position, and connection status immediately.
3. Watch the latest events appear in a timeline.
4. Click into the last failure and inspect what happened.
5. Open the log viewer and search for the exact warning or exception.
6. Look at the active planner / action / tool state without opening terminal output.
7. Keep that tab open during development and rarely need to switch to logs unless something is deeply wrong.

That is the target.

---

## 5. Recommended UI structure

The dashboard should start with a small number of high-value pages. The first release should not try to become a giant control room.

### 5.1 Primary pages

#### Overview
The default landing page. It should answer, in one glance:

- is the agent connected?
- what is the current goal?
- is it planning or executing?
- what is the health / food / position?
- is the inventory stale?
- are there any active warnings?

This is the “open the page and know whether the bot is alive” page.

#### Timeline
A chronological feed of recent agent activity. This should combine:

- world events
- goal changes
- planner actions
- tool calls
- failures
- recoveries
- chat decisions

This page replaces the need to scroll through coarse terminal output just to reconstruct a sequence of events.

#### Planner
The planner page should expose the current planning state:

- current goal
- plan steps
- decomposition tree
- last replan reason
- current task
- active recovery state
- plan fingerprint or equivalent identifier

This page is for answering “why is it doing that?”

#### World State
A direct inspector for the current read model:

- position
- health
- food
- inventory
- inventory freshness
- current facts
- world events that affected state

This page is for answering “what does the runtime believe right now?”

#### Logs
A structured log viewer that supports:

- level filtering
- text search
- source filtering
- time range filtering
- event correlation
- auto-scroll toggle

This page is for diagnostic evidence, not as the primary source of truth.

#### Tools
A view of recent tool execution:

- tool name
- parameters
- duration
- result
- error
- retry count
- correlation id

This page is useful for troubleshooting the action layer and for spotting slow or flaky tools.

---

## 6. Secondary pages

These can come after the first useful release.

#### Chat
Show chat input interpretation, classification, and the outcome of the chat-to-goal pipeline.

#### Memory
Show page reads, searches, writes, and memory routing decisions.

#### Construction
Show build progress, blueprint selection, placement failures, corrections, and completion state.

#### Mining
Show mining target, reachable block selection, dig attempts, and recovery events.

#### Runtime
Show connected services, background loops, queue status, and overall subsystem health.

#### Diagnostics
A deeper technical page for raw events, snapshot inspection, replay, and correlation tracing.

---

## 7. Recommended information architecture

The dashboard should be organized around questions, not around class names.

### Question 1: “What is the agent doing?”
Answer with:
- Overview
- Planner
- Timeline

### Question 2: “What just happened?”
Answer with:
- Timeline
- Logs
- Tools

### Question 3: “What does the runtime believe?”
Answer with:
- World State
- Planner
- Memory

### Question 4: “Why did it fail?”
Answer with:
- Logs
- Timeline
- Tools
- Chat
- Diagnostics

This organization is much more useful than a page-per-subsystem layout.

---

## 8. Suggested implementation architecture

The dashboard should be built as a thin presentation layer on top of a small set of UI-facing services.

### 8.1 Core UI services

#### Dashboard state service
A UI read model that aggregates the state needed by the pages.

Responsibilities:
- hold the current dashboard snapshot
- expose a thread-safe current view
- publish change notifications to components

#### Event feed service
A bounded stream of recent events for the timeline and log viewer.

Responsibilities:
- retain a rolling history
- merge events from multiple sources
- provide filtering and search-friendly indexing

#### SignalR adapter
A transport adapter that receives runtime updates and forwards them into the dashboard state service.

Responsibilities:
- connect to the runtime feed
- normalize incoming payloads
- avoid putting transport details into page components

### 8.2 Component rule
Razor pages should talk to the UI services, not to the runtime directly.

That separation makes the UI testable and makes future refactors much easier.

---

## 9. Recommended dashboard snapshot model

Even if the backend does not yet expose a single canonical snapshot object, the UI should behave as though one exists.

### 9.1 Main snapshot
The UI should assemble a dashboard snapshot with sections like:

- connection
- agent status
- current goal
- planner state
- world state
- active action
- recent events
- recent logs
- recent tool calls
- warnings / alerts

### 9.2 Principles for the snapshot
- immutable once published
- replace, do not mutate
- include timestamps
- include correlation ids where available
- preserve source event ids
- avoid transport-specific shapes

### 9.3 Why this matters
If every page consumes the same shared snapshot, the dashboard feels coherent and you avoid the “every page is secretly doing its own thing” problem.

---

## 10. Page-by-page MVP scope

## 10.1 Overview page

### Goal
Make the bot status obvious in under five seconds.

### Contents
- connection badge
- current goal
- planner mode or execution mode
- health / food
- position
- inventory freshness
- current action
- last failure summary
- last replan reason
- alert banner if something is wrong

### Good interactions
- click current goal to open Planner
- click last failure to open Timeline / Logs
- click position to open World State

### Do not overbuild
No charts yet. No complex layout. This page should be brutally practical.

---

## 10.2 Timeline page

### Goal
Reconstruct the last 5–20 minutes of behavior without terminal logs.

### Contents
Each entry should show:
- timestamp
- event type
- source
- short summary
- correlation id if present
- severity marker if applicable

### Expandable details
When expanded, show:
- payload
- related goal
- related action
- related tool
- exception details if any
- before/after state when available

### Filtering
- event type
- severity
- source
- goal
- correlation id

### Why this page matters
This is the page that replaces “scroll up in the console and guess.”

---

## 10.3 Planner page

### Goal
Explain the reasoning structure of the current goal.

### Contents
- active goal
- task hierarchy or decomposition tree
- current task
- current primitive action
- last replan time
- replan count
- stall state
- failure reason
- plan fingerprint or equivalent identifier

### Helpful extras
- show the last few planner decisions
- show which candidate goals were rejected
- show when the planner is waiting on world updates

### Future enhancement
Add a tree view for goal decomposition.

---

## 10.4 World State page

### Goal
Inspect the runtime’s current belief about the world.

### Contents
- agent position
- health / food
- inventory
- active flags
- world facts
- staleness indicators
- environmental context if available

### Good interactions
- copy raw state JSON
- highlight changed fields
- compare current state to previous state
- link to related timeline entries

### Why it matters
When a bot seems “confused,” this page shows whether the confusion is actually stale state.

---

## 10.5 Logs page

### Goal
Preserve the usefulness of terminal logging while making it searchable.

### Contents
- timestamp
- severity
- source
- message
- exception summary
- correlation id
- category tags

### Features
- live tail mode
- pause / resume
- text search
- filter by log level
- filter by source
- filter by correlation id
- copy message / copy stack trace

### Rule
Logs are evidence, not truth. The page should make that obvious in the design.

---

## 10.6 Tools page

### Goal
Make tool execution understandable.

### Contents
- tool name
- start / end time
- duration
- arguments
- validation outcome
- result or error
- retry count
- caller or origin if known

### Useful views
- recent successful tools
- recent failed tools
- slowest tools
- repeated failures for the same tool

### Why it matters
Many agent failures are actually tool failures wearing a planner costume.

---

## 11. UI component hierarchy

A sane component split prevents the dashboard from turning into a pile of duplicated rendering logic.

### Recommended structure

#### Layout
- `DashboardShell`
- `TopStatusBar`
- `NavigationRail`
- `AlertStrip`

#### Shared cards
- `StatusCard`
- `MetricCard`
- `EventCard`
- `LogRow`
- `KeyValueGrid`
- `StateInspector`

#### Domain widgets
- `GoalSummaryPanel`
- `PlannerTreePanel`
- `WorldStatePanel`
- `ToolInvocationTable`
- `TimelineFeed`
- `LogViewer`

### Rule of thumb
If a component knows about one specific domain concept and is used on multiple pages, promote it into a shared widget.

---

## 12. Data flow model

The cleanest flow is:

Runtime source  
→ normalized event or snapshot  
→ dashboard state service  
→ page component

Avoid:
- page component talking directly to SignalR
- page component decoding runtime payloads
- UI code reconstructing state from scratch
- multiple competing state stores

### Suggested flow in practice
- runtime publishes an event
- UI adapter receives it
- adapter updates dashboard state
- pages re-render from the state service

That is much easier to test and reason about.

---

## 13. Suggested roadmap by phase

## Phase 1 — Replace the terminal
This phase is about daily usefulness.

### Deliverables
- Overview page
- Log viewer
- Timeline feed
- basic planner summary
- basic world state panel

### Exit criteria
You can leave the dashboard open while developing and use it instead of terminal logs for normal diagnosis.

---

## Phase 2 — Add real introspection
This phase is about understanding behavior, not just seeing status.

### Deliverables
- detailed planner page
- tool invocation page
- richer world state inspector
- chat decision trace
- memory activity page

### Exit criteria
You can answer “why did it do that?” without opening the debugger.

---

## Phase 3 — Add event correlation
This phase is about connecting symptoms to causes.

### Deliverables
- correlation id display
- linked timeline entries
- failure-to-tool trace
- goal-to-task trace
- planner-to-action trace

### Exit criteria
A failure can be traced from symptom back to source in a few clicks.

---

## Phase 4 — Add visualizations
This phase is about speed and pattern recognition.

### Deliverables
- planner tree visualization
- build progress visualization
- mining progress visualization
- world map or spatial view
- event waterfall / causality view

### Exit criteria
The UI makes patterns obvious, not just accessible.

---

## Phase 5 — Add operator controls
Only after the monitoring loop is strong enough should control features expand.

### Deliverables
- pause / resume
- trigger replan
- inject goal
- retry action
- runtime configuration
- diagnostic replay

### Exit criteria
The dashboard becomes an operator console, not just a viewer.

---

## 14. Prioritized backlog

### P0
1. Overview page
2. Timeline page
3. Structured log viewer
4. Current goal / planner summary
5. Basic world state panel

### P1
6. Tool execution history
7. Correlation id plumbing in UI
8. Chat decision trace
9. Failure detail expansion
10. Search/filter across logs and timeline

### P2
11. Planner tree view
12. World state inspector enhancements
13. Memory activity page
14. Build / mining specific detail panels
15. Alert system

### P3
16. Visualizations
17. Replay
18. Runtime configuration
19. Operator actions
20. Multi-agent scaffolding

---

## 15. Implementation sequence

This is the order I would actually build it in.

### Step 1
Create the dashboard shell:
- navigation
- shared header
- alert strip
- page layout

### Step 2
Build the Overview page using existing state data.

### Step 3
Build the Timeline page with a bounded event feed.

### Step 4
Build the Logs page with search and filters.

### Step 5
Build the Planner page with a compact goal/task summary.

### Step 6
Build the World State page.

### Step 7
Add Tool history and failure drill-down.

### Step 8
Add correlation ids and cross-links.

### Step 9
Only then begin visualizations.

This order gives you value early and keeps the architecture from becoming a science project.

---

## 16. Minimum backend support needed

The UI can start now, but it will be much stronger if the runtime exposes these consistently:

- goal id
- action id / correlation id
- event timestamp
- event source
- event category
- failure reason
- replan reason
- active task name
- current tool name
- structured exception summaries

If any of these are missing, add them incrementally.

Do not block the UI waiting for a perfect event schema.

---

## 17. Logging strategy for the dashboard era

Terminal logs are still useful, but they should become one input to the dashboard, not the primary debugging surface.

### Keep terminal logs for
- raw exceptions
- startup failures
- transport errors
- diagnostic fallback

### Move into the dashboard
- normal agent activity
- progress events
- goal changes
- planner transitions
- tool calls
- recoverable failures
- warnings
- event history

### Result
Developers stop reading logs as prose and start reading the system as a live state machine.

---

## 18. UI/UX rules that matter

1. Default to showing the most recent and most important state first.
2. Never force users to open three panels to answer a basic question.
3. Use color sparingly and only for meaning.
4. Make failures visually louder than success.
5. Provide one-click copy for any raw payload.
6. Link related entries together.
7. Keep pages fast and boring in the best possible way.

The best debugging UI feels calm even when the agent is not.

---

## 19. Testing strategy

### Component tests
- pages render from mock snapshot data
- filters work
- empty states are correct
- errors are visible and understandable

### Service tests
- snapshot merge logic
- event feed retention
- correlation linking
- sorting and filtering
- reset / reconnect behavior

### Integration tests
- runtime event arrives
- dashboard state updates
- page reflects the update
- log entry and timeline entry remain consistent

### Manual acceptance test
Use a real agent session and ask:
- can I tell what it is doing?
- can I see what changed?
- can I explain the last failure?
- can I do that without opening the terminal?

If yes, the UI is working.

---

## 20. Risks and mitigations

### Risk: dashboard becomes another source of truth
**Mitigation:** keep the UI as a consumer of projected state only.

### Risk: too many pages too early
**Mitigation:** ship the first six views before adding anything fancy.

### Risk: pages duplicate logic
**Mitigation:** centralize derived state in a shared dashboard service.

### Risk: logs and timeline diverge
**Mitigation:** use shared event ids and consistent timestamps.

### Risk: UI feels “alive” but is not useful
**Mitigation:** test every feature against the question “does this help me debug faster?”

---

## 21. Recommended sprint framing

If you want to attach this to sprint planning, I would frame it like this:

### Sprint A
Dashboard shell, overview, and logging replacement basics

### Sprint B
Timeline and tool history

### Sprint C
Planner and world-state drill-down

### Sprint D
Correlation ids, search, and richer diagnostics

### Sprint E
Visualizations

### Sprint F
Operator controls

That sequence keeps the value curve steep from the beginning.

---

## 22. What not to do yet

Do not start with:
- complex charts
- a big dependency graph
- multi-agent abstractions
- editable runtime config
- replay infrastructure
- spatial world rendering
- a dozen custom widgets before the basics work

Those are all reasonable later features, but they are not the fastest path to replacing terminal logging.

---

## 23. Recommended first implementation ticket

If you want the smallest meaningful first ticket, make it this:

> **Build the Dashboard Overview page backed by a shared dashboard state service, and show live goal, connection, health, position, inventory freshness, and recent warnings.**

That one page will prove the state pipeline, component structure, and update path.

After that, build the Timeline page.

That is the moment the dashboard starts paying rent.

---

## 24. Final recommendation

Treat the dashboard as a debugging product, not as a passive display.

If every page helps answer a real operational question quickly, the UI will become the thing you open first instead of the terminal. That is the right bar.

The plan above is intentionally biased toward fast practical usefulness:
- replace log scrolling
- surface agent state
- expose recent history
- explain planner decisions
- show failures clearly
- keep the architecture simple enough to evolve

That is the best way to get from “server is running” to a dashboard people actually use.

