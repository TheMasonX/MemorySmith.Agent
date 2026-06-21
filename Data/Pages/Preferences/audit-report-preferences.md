MemorySmith User Preference: Audit & Report Standards

User prefers professional engineering audit reports modeled after staff/principal engineer design reviews and architecture assessments.

Report Naming Convention

All generated reports should use:

msa_{primary_focus}_{YYYYMMDD_HHMM_CT}.md

Examples:

* msa_planning_mineflayer_audit_20260621_1437_CT.md
* msa_architecture_review_20260621_1441_CT.md
* msa_sprint26_code_audit_20260621_1455_CT.md
* msa_planning_gathering_pathfinding_mineflayer_audit_20260621_1508_CT.md

Rules:

* Timestamp should be Central Time (CT).
* Include date and minute precision.
* Include the primary audit focus in the filename.
* Prefer descriptive names over generic names like report.md or audit.md.

Required Report Metadata Header

Every report should begin with:

# Report Title

Report: <filename>

Generated: YYYY-MM-DD HH:MM CT
Repository: <repo>
Branch: <branch>
Commit: <commit hash>

Focus Areas:

* Area 1
* Area 2
* Area 3

Audit Philosophy

User prefers:

* Evidence-based findings only.
* No speculative claims presented as facts.
* Explicit confidence percentages for findings.
* Distinction between:

  * Verified issues
  * Probable issues
  * Architectural concerns
  * Open questions
* Avoid duplicate recommendations already planned in sprint/task documents.
* Review roadmap, sprint plans, handoff documents, and active tasks before making recommendations.
* Verify implementation status against code before suggesting work.

Preferred Audit Structure

1. Executive Summary
2. Current State Assessment
3. Major Findings
4. Architectural Analysis
5. Planning & Roadmap Review
6. Missed Opportunities
7. Risks
8. Recommended Priorities
9. Open Questions
10. Confidence Summary
11. Supporting Evidence

Architecture Review Style

Favor principles associated with strong codebase architecture reviews:

* Deep modules over shallow modules.
* Strong boundaries and seams.
* High cohesion.
* Low coupling.
* Locality of behavior.
* Deletion tests.
* Refactoring opportunities.
* Architectural simplification.
* Long-term maintainability.
* Avoid accidental complexity.
* Reduce duplicate sources of truth.

When auditing a codebase, specifically look for:

* Bugs
* Vulnerabilities
* Race conditions
* Concurrency issues
* Reliability concerns
* State synchronization problems
* Architectural design flaws
* Refactoring opportunities
* Technical debt
* Test coverage gaps
* Planning inconsistencies
* Documentation drift
* Missed product opportunities

MemorySmith-Specific Preferences

For MemorySmith.Agent reviews:

Always examine:

* Planning
* Goal decomposition
* Gathering
* Pathfinding
* World representation
* Memory systems
* Action lifecycle
* Tool architecture
* Agent autonomy
* Mineflayer adapter
* HTN planning
* World state projection
* Journal/event systems

Particular interest should be given to:

* Representation quality
* Missing observations
* Missing telemetry
* Planner quality
* Agent reasoning depth
* World model fidelity
* Scalability of architecture

Recommendations should be prioritized by:

1. Impact
2. Risk reduction
3. Architectural leverage
4. Development effort

User values grounded realism over optimism and prefers findings that can be directly traced back to code, documentation, tests, or observed behavior.
