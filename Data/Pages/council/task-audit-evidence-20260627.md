# Council Evidence Pack: MemorySmith.Agent Task Audit

## Decision Question
What changes are needed to the MemorySmith.Agent task store to ensure tasks are valid, sufficient, correct, and aligned with current code state and plans?

## Scope
- **Repository**: MemorySmith.Agent only (NOT MemorySmith main repo)
- **Task count**: 191 JSON files (after dedup), ~186 unique IDs
- **Decision type**: Mixed — task store governance, retrieval quality, agent write behavior

## Current State Summary

### Status Distribution (191 JSON files)
| Status | Count | Notes |
|--------|-------|-------|
| Backlog | 99 | Largest group — many may be stale |
| Done | 74 | Some may have drifted |
| PARSE_ERROR | 5 | Corrupted JSON (embedded \r chars) |
| Open | 3 | Non-standard status |
| InProgress | 2 | TSK-0004, TSK-0166 |
| Ready | 2 | TSK-0144, TSK-0145 |
| Archived | 1 | TSK-0081 |
| Rejected | 1 | TSK-0014 |
| Blocked | 1 | (untracked which) |
| Closed - Merged into TSK-0103 | 1 | TSK-0098 |
| Closed - Merged into TSK-0105 | 1 | TSK-0086 |
| Completed | 1 | TSK-0021 |

### Known Issues

#### 1. Duplicate IDs (7 pairs)
- TSK-0169: "Chat Context Dashboard" [Backlog,Med] vs "Length-based Chat Eviction" [Backlog,High]
- TSK-0171: "Creative Gather Bypass Guard" [Done,Crit] vs "Creative Mode Recovery Guards" [Done,High]
- TSK-0172: "MoveEvent Log Suppression" [Done,High] vs "Respawn Handler Once Gap" [Backlog,High]
- TSK-0173: "Investigate MoveTo Re-added on Replan" [Done,Crit] vs "Ollama Lifetime Management" [Backlog,High]
- TSK-0193: "Search Memory Error Handling" [Backlog,High] vs tsk-0193.json [Done,High] (same title, diff status)
- TSK-0194: "HTTP Retry Resilience" [Backlog,High] vs tsk-0194.json [Done,High] (same title, diff status)
- TSK-0197: "ActionData Tool Normalization" [Backlog,High] vs tsk-0197.json [Done,High] (same title, diff status)

#### 2. Corrupted JSON (5 files)
Embedded 0x0D (CR) characters in description fields:
- tsk-0107 (build origin sentinel)
- tsk-0132 (page search score)
- tsk-0133 (parameter preservation on replan)
- tsk-0134 (DI startup failure logging)
- tsk-0137 (consecutive failure guard)

#### 3. Non-standard Statuses (6 files)
- "Open": tsk-0011, tsk-0012, tsk-0013
- "Completed": tsk-0021
- "Closed - Merged into TSK-0103": tsk-0098
- "Closed - Merged into TSK-0105": tsk-0086

#### 4. ID Format Drift
- 183 tasks use old format: `id = "tsk-XXXX-descriptive-slug"`
- 3 files use new format: `id = "TSK-XXXX"` (matching `key` field)
- New-format files are the unnamed duplicates: tsk-0193.json, tsk-0194.json, tsk-0197.json

#### 5. ID Gaps
Missing: 115, 117, 118, 182-185, 187-188, 190, 192, 195-196
Orphan .md files exist for 115, 117, 118 without corresponding .json

#### 6. Overlapping/Redundant Tasks
- **TSK-0062** (goto timeout) vs **TSK-0159** (goto timeout - broader scope) — same concept
- **TSK-0173** (MoveTo replan root cause) vs **TSK-0174** (MoveTo source investigation) — same concept, both Done
- **TSK-0082** (SmeltableMapping) shows Backlog but was actually completed in Sprint 48

#### 7. Stale Backlog Tasks (Critical/High priority, may need triage)
32 Critical/High priority tasks in Backlog, including:
- Security: Gemini API key header (Critical), SignalR auth (Critical), Chat rate limiter tests (Critical)
- Infrastructure: LLM provider tests (Critical), Pathfinder events (Critical), Goto timeout (Critical)
- Entity/observation chain (7 tasks, High) — planned feature series
- Dashboard improvements (Medium)

### Current Code State (from sprint handoffs)
- Sprint 48 completed: TSK-0105 (bot name detection), TSK-0103 (max distance blocks), TSK-0082 (SmeltableMapping)
- Sprint 46 wave C completed: TSK-0109 (api about), TSK-0110 (api key middleware), TSK-0111 (bare catches)
- Sprint 46 wave B completed: TSK-0099 (alias dictionaries), TSK-0104 (ReplanResult), TSK-0106 (error tests)
- Sprint 45 completed: TSK-0087 (origin typo), TSK-0090 (empty pageId), TSK-0091 (Thread.Sleep), TSK-0088 (try/catch gateway), TSK-0094 (blueprint validation), TSK-0092 (null cache TTL), TSK-0089 (nav contract)
- Sprint 44 completed: TSK-0079 (smelt route), TSK-0080 (search memory), TSK-0081 (tests), P1-1 (GoalName removal), P1-2 (placeBlockContexts cleanup)
- Sprint 41-43 completed: Various build checkpoint, occupancy, scaffolding work

### Key Existing Documents
- Data/Pages/architecture.md — canonical pipeline
- Data/Pages/Tasks/handoff-sprint48-complete.md — latest handoff
- AGENTS.md — coding guidelines, Rule E-1, E-2, E-3, A-1
- Data/Pages/decisions.md — ADRs D-001 through D-013

### Open Questions
- Should the entity/observation/scene feature chain (TSK-0146 through TSK-0155) be promoted/demoted/re-evaluated?
- Are the security tasks (TSK-0180, TSK-0181, TSK-0189, TSK-0191) still relevant given current deployment context?
- Should the overlapping goto timeout tasks (TSK-0062 and TSK-0159) be merged?
- Is the dashboard improvement wave (TSK-0169, TSK-0170) still a priority?
- What should be the canonical ID format going forward?
