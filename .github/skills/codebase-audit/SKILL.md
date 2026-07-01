---
name: codebase-audit
description: 'Sweep the codebase for bugs, inconsistencies, gaps, weak guards, error handling gaps, observability/logging issues, overcoupling, and architectural fixes. Produce a 4-seat council-reviewed markdown report with task synthesis. Use when users request a systematic audit, quality sweep, bug hunt, or pre-sprint review.'
argument-hint: 'Sprint number, report timestamp format, optional focus areas'
user-invocable: true
---

# Codebase Audit & Council Review

Produces an evidence-backed audit report with peer-reviewed findings, plus synthesized tasks for uncovered issues.

## Outcome

- `Data/Pages/Audits/internal-audit-{sprint}-{timestamp}.md` — Detailed report with P0–P3 findings
- 4-seat anonymous council peer review refining severity, accuracy, and completeness
- New MCP tasks for findings without existing task coverage
- Roadmap updates linking audit findings to sprints

## Use When

The user asks for:
- "Sweep the codebase for bugs / issues / gaps / smells"
- "Do a pre-sprint quality review"
- "Find weak guards or error handling problems"
- "Audit the architecture for overcoupling"
- "Create an audit report and peer-review it"
- "Review observability and logging"

Do NOT use for: single-file reviews, ad hoc Q&A about code, performance profiling (use `web-perf` or Chrome DevTools), or dependency analysis.

## Procedure

### Phase 1: Gather Context

1. Read the current audit report or sprint handoff the user has open (if any).
2. Check the roadmap (`Data/Pages/roadmap.md`) for current sprint, completed phases, and planned work.
3. Check repo memory (`/memories/repo/`) for known issues and recent fixes.
4. Check recent git history (last 30 commits) for themes, recently fixed bugs, and open issues.
5. Read key context files: `README.md`, `AGENTS.md`, appsettings.json, `Program.cs` (DI setup), and any `copilot-instructions.md`.

### Phase 2: Systematic Codebase Exploration

Explore each layer of the codebase. For each file, look for:

| Category | What to Check |
|----------|--------------|
| **Bugs** | Logic errors, race conditions, null refs, premature completion, incorrect state transitions |
| **Inconsistencies** | Divergent patterns (e.g., one goal type uses events for completion, another uses dispatch counters), mismatched naming, config vs code contract violations |
| **Gaps** | Missing handlers in switch statements, unhandled event types, tools without timeouts |
| **Weak guards** | `CancellationToken.None`, missing null checks, fire-and-forget with no error reporting, spin-waits |
| **Error handling** | Silent catch blocks, swallowed exceptions, fire-and-forget that drops failures |
| **Observability** | Logging at wrong level, missing correlation IDs in logs, silent failure paths, truncated context in log messages |
| **Overcoupling** | One class owning too many concerns, dead code that looks alive (registered but unused), two systems with overlapping responsibilities |
| **Architecture** | Interfaces defined but never wired, stub implementations that always return defaults, layers that bypass abstractions |

Minimum coverage:
- `Agent.Core/` — Models, Events, Interfaces, Runtime, WorldStateProjector
- `Agent.Planning/` — HtnPlanner, HtnTaskLibrary, decomposers, goal types, LlmEvaluatorImpl, IntentManager
- `Agent.Tools/` — ToolDispatcher (registration, validation, dispatch)
- `WebUI.Blazor/` — AgentBackgroundService (all code paths), Program.cs (DI wiring), Managers/
- `Agent.Memory/` — RestMemoryGateway
- `MineflayerAdapter/` — index.js, config.js
- `WebUI.Blazor/Options/` — SafetyOptions, ChatOptions

### Phase 3: Create Audit Report

Write to `Data/Pages/Audits/internal-audit-{sprint}-{YYYYMMDD}.md`.

Structure:
```
# Internal Codebase Audit — Sprint {N}

**Date:** {date}
**Scope:** {projects covered}
**Type:** {audit type}

## Executive Summary
- Summary paragraph
- Severity distribution table: P0/P1/P2/P3 counts, total findings
- Peer review note

## P0 — Critical
### {ID}: {Title}
**File:** `{path}` (line ~{N})
**The bug:** {description with code snippet}
**Impact:** {real-world consequences}
**Recommendation:** {actionable fix}

## P1 — High
...same format...

## P2 — Medium
...same format...

## P3 — Low / Observability
...same format...

## Architecture Notes
Structural observations that don't fit a single bug.

## Methodology
How the audit was conducted.

## Peer Review Results (added after Phase 4)
Reviewer summaries, disputed findings table, missing task tracking.
```

### Phase 4: Council Peer Review

Launch **4 independent subagents** (agent name: `Explore`) in parallel, each with a distinct role:

| Reviewer | Focus |
|----------|-------|
| **Architecture & Design** | Accuracy, severity calibration, missing findings, duplication, design patterns |
| **Runtime & Debugging** | Verify P0/P1 claims against actual source code, confirm code snippets, catch inaccurate descriptions |
| **Safety & Security** | Verify safety/security findings against source, check for additional vulnerabilities |
| **Completeness & QA** | Test coverage gaps, JS-side issues, SignalR/dashboard path, task mapping, .bak/hygiene issues, missing task IDs |

Each reviewer receives:
- The full audit report content
- Instructions to verify claims against source code
- A structured response format: confidence per finding, corrections with evidence, additional findings, severity recalibrations

### Phase 5: Finalize Report

1. Apply all verified corrections from peer review.
2. Update severity ratings per reviewer consensus.
3. Add `[PR]` markers to findings added or corrected by peer review.
4. Append §Peer Review Results section with reviewer feedback table and "No Existing Task Tracking" warnings.
5. Every P0 finding without an existing task must be called out.

### Phase 6: Create Tasks

For each actionable finding without existing task coverage:
1. Create an MCP task via `memorysmith_task_create`.
2. Use next available TSK-XXXX key (check highest existing key first).
3. Set priority = finding severity (Critical→P0, High→P1, Medium→P2, Low→P3).
4. Set status = Backlog.
5. Include `sprint-{N}` label and domain/type labels.
6. Source the finding in the audit report filename.

For findings that overlap existing tasks:
1. Add a comment to the existing task linking the audit finding.
2. Update the existing task if the audit reveals new nuance (e.g., a partial fix introduced a data race).

### Phase 7: Update Roadmap

Update `Data/Pages/roadmap.md`:
1. Add all new tasks to the next available sprint section.
2. Include severity, source reference, and summary.
3. Defer non-urgent items to future sprints if the current sprint is already full.

## References

- Task creation: MCP `memorysmith_task_create` (do NOT edit task JSON files directly)
- Roadmap: `Data/Pages/roadmap.md`
- Repository memories: `/memories/repo/`
- Existing skill (broader scope): `.github/skills/codebase-audit-sprint-planner/SKILL.md` (for full sprint planning after audit)
