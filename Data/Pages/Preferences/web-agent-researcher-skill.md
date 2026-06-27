# Web Agent Researcher Skill
Access my public MemorySmith.Agent repo on the latest commit, which will provided.
You will be doing a deepdive codebase audit and create a markdown report for the agents tasked with implementing.

## Research Process
As you go:
- Look for bugs, inconsistencies, gaps, or missed opportunities.
- Look for brittle conditions, assumptions, implicit contracts, poorly handled or silently swallowed errors.
- Look for architectural design bugs, and refactoring and codebase health improvement opportunities.

If they don't fit within the scope of the current task, they need to be added still in an out of scope list to track them and ensure they aren't forgotten.

## Report Format

```
# Report Title

**Task Description:**
**Timestamp:**
**Branch:**
**Commit:**

## Executive Summary

## List of Findings

### {ITEMID}-001: Finding Name

...

## Supplemental Data

## Out Of Scope

## Assumptions

## Open Questions

## Next Steps

```

Name format: `{task-description}-audit-{dd-mm-yy-hh-mm-ss}.md`

## Rules
State all assumptions and open questions. Provide realistic, grounded confidence values as %.
Use evidence based claims and cite sources as appropriate.
Think critically and do not take claims at face value without evidence.
Provide assistance with planning and implementation guidance where possible to give the agent an easy time following along
Avoid prose or explanation heavy sections unless they are critical. That bloat dillutes the SNR and context.
Make it just a summary followed by the actionable findings and recommendations, with rationale and sources.

## Sources
https://github.com/TheMasonX/MemorySmith.Agent/tree/main
https://github.com/TheMasonX/MemorySmith.Agent/commit/d6dc26e54a58b8c6bd9cf5bf776844675dd8a399