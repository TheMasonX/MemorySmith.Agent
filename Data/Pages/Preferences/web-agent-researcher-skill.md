# Bug Report Skill

You will be doing a deepdive codebase audit and create a markdown report for the agents tasked with implementing.

## Research Process

As you go:

- Look for bugs, inconsistencies, gaps, or missed opportunities.
- Look for brittle conditions, assumptions, implicit contracts, poorly handled or silently swallowed errors.
- Look for architectural design bugs, and refactoring and codebase health improvement opportunities.

If they don't fit within the scope of the current task, they need to be added still in an out of scope list to track them and ensure they aren't forgotten.

## Report Format

```report-format
# Report Title

**Task Description:**
**Author:**
**Timestamp:**
**Branch:**
**Commit:**

## Executive Summary

## Items Table

// Master findings table, severity sorted
| ID | Name | Description | Severity | Impact | Probability | Confidence |
| -- | ---- | ----------- | -------- | ------ | ----------- | ---------- |

## Items List

### {ITEMID}-001: Finding Name

#### Description

#### Rationale

#### Recommendation

#### Sources

...

## Supplemental Data

## Out Of Scope

## Assumptions

## Open Questions

## Next Steps

```

Name format: `{task-description}-audit-{dd-mm-yy-hh-mm-ss}.md`

## Rules

State **ALL** assumptions and open questions. Provide realistic, grounded confidence values as %.
Use evidence based claims and cite sources as appropriate.
Think critically and **DO NOT** take claims at face value without evidence and personal skeptical review.
Provide assistance with planning and implementation guidance where possible to give the agent an easy time following along.
Avoid prose or explanation heavy sections unless they are critical. That bloat dillutes the context and reduces SNR.
**DO NOT** make assumptions about the codebase without evidence. If you cannot find evidence, state that explicitly and move on. **DO NOT** assume the contents of a file without reading it.
