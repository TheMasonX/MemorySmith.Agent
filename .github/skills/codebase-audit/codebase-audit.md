# Codebase Audit — Seed Prompt

This file is the original seed prompt that inspired the skill. The canonical workflow lives in `SKILL.md`.

```
Sweep the codebase for bugs, inconsistencies, gaps, assumptions, weak guards, brittle conditions, code smells, poor error handling or silently swallowed errors.
Look for issues in traceability, observability, and debugging.
Look for key logging improvements of critical info (without being too verbose).
Look for overcoupling and architectural fixes.
Create a detailed markdown report in the audits folder with your findings after the research and then a 4-subagent council review with anonymous peer review to refine the report.
File: internal-audit-{sprint}-{timestamp}.md
```

**See also:** [SKILL.md](./SKILL.md) for the full 7-phase procedure including task creation and roadmap updates.