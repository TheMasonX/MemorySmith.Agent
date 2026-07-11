---
name: msa-codebase-audit
description: 'Sweep the MemorySmith.Agent codebase for bugs, inconsistencies, gaps, weak guards, error handling gaps, observability/logging issues, overcoupling, and architectural fixes. Produces a council-reviewed markdown report with MCP task synthesis. Supports opt-in parallel subagent swarm when a number of subagents is supplied. Use when users request a systematic audit, quality sweep, bug hunt, or pre-sprint review.'
argument-hint: 'Sprint number, report timestamp format, optional focus areas, optional N subagents (e.g., "with 4 subagents")'
user-invocable: true
---

# MSA Codebase Audit & Council Review

Produces an evidence-backed audit report with peer-reviewed findings, plus synthesized MCP tasks for uncovered issues. 

## Outcome

- `Data/Pages/Audits/internal-audit-{sprint}-{YYYYMMDD}.md` — Detailed report with P0–P3 findings
- 4-seat council peer review refining severity, accuracy, and completeness
- New MCP tasks for findings without existing task coverage
- Roadmap updates linking audit findings to sprints
- Optional: parallel subagent swarm for large-scale coverage

## Use When

The user asks for:
- "Sweep the codebase for bugs / issues / gaps / smells"
- "Do a pre-sprint quality review"
- "Find weak guards or error handling problems"
- "Audit the architecture for overcoupling"
- "Create an audit report and peer-review it"
- "Review observability and logging"
- "Audit with N subagents in parallel"

Do NOT use for: single-file reviews, ad hoc Q&A about code, performance profiling (use the `web-perf` skill), or dependency analysis.

## Argument Hint Interpretation

The `argument-hint` accepts:
- **Sprint number**: e.g., `Sprint 60` — included in report header
- **Report timestamp**: e.g., `20260711` — used for filename
- **Focus areas**: e.g., `Agent.Planning, MineflayerAdapter` — narrows scope
- **N subagents**: e.g., `with 4 subagents` or `with 10 subagents` — activates the parallel swarm path (Branch A)

If N is provided, the swarm path (Phase 2a) replaces the manual single-agent exploration. If not provided, the default single-agent deep-dive is used.

---

## Procedure

### Phase 0: Determine Path

Check whether a number of subagents (N) was supplied in the request:

- **N provided** (N ≥ 2): Use **Branch A (Swarm Path)** — partition the codebase into N chunks and launch parallel subagents for exploration. Recommended N: 3–10.
- **No N provided**: Use the **Default Path** — single-agent systematic file-by-file exploration.

Record the chosen path in the audit report Methodology section.

---

## Branch A: Swarm Path (Opt-In Parallel)

### Phase 1a: Gather Context

1. Read the current audit report or sprint handoff the user has open (if any).
2. Check the roadmap (`Data/Pages/roadmap.md`) for current sprint, completed phases, and planned work.
3. Check repo memory (`/memories/repo/`) for known issues and recent fixes.
4. Check recent git history (last 30 commits) for themes, recently fixed bugs, and open issues.
5. Read key context files: `README.md`, `AGENTS.md`, `Program.cs` (DI setup), and any `copilot-instructions.md`.
6. Determine partition strategy — see Phase 2a.

### Phase 2a: Define and Launch the Swarm

1. **Partition the codebase** into N independent chunks. Use the coverage areas below as a guide. Common partition strategies:
   - **By project**: Agent.Core/Models, Agent.Core/Interfaces+Runtime, Agent.Planning/Goals, Agent.Planning/Planner+LLM, Agent.Tools, Agent.World.Minecraft+ABS, WebUI.Blazor, MineflayerAdapter, Agent.Memory+Construction, Tests+Scripts+Config
   - **By concern**: Models → Events → Interfaces → Runtime → Planning → Tools → Adapter → Dashboard → JS → Tests
   - **By layer**: C# models, C# pipelines, JS adapter, config/CI/docs
   - **Custom**: User-specified focus areas

2. **Verify independence** — each partition must be self-contained with no cross-agent dependency.

3. **Write self-contained prompts** for each subagent:
   - Partition description
   - Specific files to explore
   - Categories to check (bugs, inconsistencies, gaps, guards, error handling, observability, overcoupling, architecture)
   - Structured output format: `## {Area} — Findings\n\n### {Severity} — {Title}\n**File:** \`{path}\` (line ~{N})\n**The issue:** ...\n**Impact:** ...\n**Recommendation:** ...\n**New/Existing:** ...`
   - Research-only instruction (no code changes)

4. **Launch all N subagents simultaneously** via the `runSubagent` tool (one call per subagent). Use the current agent (omit `agentName`).

5. **Await results** — collect all N individual outputs.

### Phase 3a: Synthesize Swarm Results

1. **Merge**: Collect all structured findings into a unified dataset.
2. **Deduplicate**: Remove overlapping or redundant findings across partitions.
3. **Resolve conflicts**: If subagents disagree, flag both positions with evidence.
4. **Summarize**: Produce executive summary, severity distribution table, and cross-cutting patterns.
5. **Proceed to Phase 4** (Create Audit Report) with the synthesized data.

---

## Default Path (Single-Agent Deep-Dive)

### Phase 1b: Gather Context

1. Read the current audit report or sprint handoff the user has open (if any).
2. Check the roadmap (`Data/Pages/roadmap.md`) for current sprint, completed phases, and planned work.
3. Check repo memory (`/memories/repo/`) for known issues and recent fixes.
4. Check recent git history (last 30 commits) for themes, recently fixed bugs, and open issues.
5. Read key context files: `README.md`, `AGENTS.md`, `Program.cs` (DI setup), and any `copilot-instructions.md`.

### Phase 2b: Systematic Codebase Exploration

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

---

## Common Phases (Both Paths)

### Phase 4: Create Audit Report

Write to `Data/Pages/Audits/internal-audit-{sprint}-{YYYYMMDD}.md`.

Structure:
```markdown
# Internal Codebase Audit — Sprint {N}

**Date:** {date}
**Scope:** {projects covered}
**Type:** {audit type}
**Path:** {Default or Swarm (N subagents)}

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
How the audit was conducted (Default or Swarm path with N subagents).

## Peer Review Results (added after Phase 5)
Reviewer summaries, disputed findings table, missing task tracking.
```

### Phase 5: Council Peer Review

Launch **4 independent subagents** using the [`subagent-swarm`](../subagent-swarm/SKILL.md) skill in **Branch B (Heterogeneous Swarm)** mode, each with a distinct role:

| Reviewer | Focus |
|----------|-------|
| **Architecture & Design** | Accuracy, severity calibration, missing findings, duplication, design patterns, merging suggestions |
| **Runtime & Debugging** | Verify P0/P1 claims against actual source code, confirm code snippets, catch inaccurate descriptions, check cross-cutting patterns |
| **Safety & Security** | Verify safety/security findings against source, check for additional vulnerabilities, identify escalation opportunities |
| **Completeness & QA** | Test coverage gaps, JS-side issues, SignalR/dashboard path, task mapping, .bak/hygiene issues, missing task IDs, schema drift, documentation drift |

Each reviewer receives:
- The full draft audit report content (or synthesized swarm findings)
- Instructions to verify claims against source code
- A structured response format: confidence per finding, corrections with evidence, additional findings, severity recalibrations

### Phase 6: Finalize Report

1. Apply all verified corrections from peer review.
2. Update severity ratings per reviewer consensus.
3. Add `[PR]` markers to findings added or corrected by peer review.
4. Append §Peer Review Results section with reviewer feedback table and "No Existing Task Tracking" warnings.
5. Every P0 finding without an existing task must be called out.
6. If the Swarm Path was used, include the partition strategy and N value in the Methodology.

### Phase 7: Create Tasks

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

### Phase 8: Update Roadmap

Update `Data/Pages/roadmap.md`:
1. Add all new tasks to the next available sprint section.
2. Include severity, source reference, and summary.
3. Defer non-urgent items to future sprints if the current sprint is already full.

---

## Decision Branch Summary

| Aspect | Default Path (No N) | Swarm Path (N supplied) |
|--------|---------------------|------------------------|
| **Exploration** | Single agent reads files directly | N parallel subagents explore partitions |
| **Best for** | Focused audits, small codebases, targeted areas | Large codebases, broad sweeps, time-constrained audits |
| **N recommendation** | N/A | 3–10 (3–5 typical; use 10+ only for very large codebases) |
| **Coverage** | Manual, deep per file | Parallel, broad across partitions |
| **Synthesis** | Single agent knows all findings | Requires explicit merge + dedup step |
| **Token cost** | Lower | Higher (N agents × their context) |

---

## References

- Task creation: MCP `memorysmith_task_create` (do NOT edit task JSON files directly)
- Subagent swarm: `.github/skills/subagent-swarm/SKILL.md` (used for both swarm and council phases)
- Roadmap: `Data/Pages/roadmap.md`
- Repository memories: `/memories/repo/`
- Related skill (broader scope): `.github/skills/codebase-audit-sprint-planner/SKILL.md` (for full sprint planning after audit)
