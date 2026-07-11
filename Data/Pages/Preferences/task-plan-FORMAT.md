---
name: "Task Plan Format"
description: "A concise, professional format for phased implementation plans."
---
# Task Plan Format

Use this for phased implementation plans, sprint planning, and task breakdowns. The format is designed to be concise, professional, and focused on actionable items.

## Report Format

Name format: `{task-description}-audit-{dd-mm-yy-hh-mm-ss}.md`

```report-format
# Report Title

**Task Description:**
**Author:**
**Timestamp:**
**Branch:**
**Commit:**

## Executive Summary

## Goals

### Non-Goals

## Requirements

## Constraints

## Phases

### Phase 1: {Phase Name}

#### Goal

#### Deliverables

#### Success Criteria

## Sprint Plan

// Depending on the task, this may be a few sprints or multiple phases, each with sets of sprints. Each phase should have a clear goal, deliverables, and success criteria.

| Sprint | Description | Tasks |
| ------ | ----------- | ----- |

## Task Table

// Master task table, severity sorted
| ID | Sprint | Name | Description | Confidence |
| -- | ------ | ---- | ----------- | -------- | ------ | ----------- | ---------- |

## Task List

### {TSK}-001: Task Name

#### Description

#### Detailed Steps

#### Test Plan

#### Sources

...

## Supplemental Data

## Out Of Scope

## Assumptions

## Open Questions

## Requested Data

// Include any data or information that the agent needs to complete the task, such as logs, configuration files, source code, or external resources.

## Next Steps

// Include any next steps for the agent to take, including any follow-up tasks, research, or implementation work that needs to be done.

```

## Rules

- State all assumptions and open questions.
- Provide realistic, grounded confidence values as %. It is better to be conservative and under-promise than to over-promise and under-deliver.
- Use evidence based claims and cite sources as appropriate. Include links to relevant code, documentation, or other sources that support your claims.
- Think critically and do not take claims at face value without evidence.
- Provide assistance with planning and implementation guidance where possible to give the agent an easy time following along
- Avoid prose or explanation heavy sections unless they are critical. That bloat dillutes the SNR and context.
