# Task Governance Guide

This guide documents how to work with tasks in MemorySmith.Agent.

## Task Storage

Tasks are stored as individual JSON files in `Data/Tasks/*.json`. Each file represents one task.

## Task Schema

Every task JSON file uses camelCase properties:

```json
{
  "id": "tsk-XXXX-descriptive-slug",
  "key": "TSK-XXXX",
  "title": "Short descriptive title",
  "description": "Detailed description with scope, acceptance criteria, and file references",
  "type": "Task",
  "status": "Backlog",
  "priority": "Medium",
  "assigneeMode": "Custom",
  "assigneeCustomText": "Agent",
  "labels": ["domain:mineflayer", "type:improvement"],
  "comments": [
    {
      "id": "c-unique-id",
      "author": "AgentSmith",
      "body": "Completed with evidence: file paths, test results",
      "createdAtUtc": "2026-06-27T00:00:00Z"
    }
  ],
  "createdAtUtc": "2026-06-27T00:00:00Z",
  "updatedAtUtc": "2026-06-27T00:00:00Z",
  "completedAtUtc": null,
  "revision": 1,
  "isArchived": false
}
```

## Status Lifecycle

```
Backlog → Ready → InProgress → Done
                           ↘ Blocked → InProgress
               ↘ Archived (terminal)
               ↘ Rejected (terminal)
```

## Creating a New Task

1. Determine the next available ID by checking existing `TSK-XXXX` keys in `Data/Tasks/`.
2. Use the template above.
3. Set `id` to `tsk-XXXX-descriptive-slug` and `key` to `TSK-XXXX`.
4. Set `status` to `Backlog`.
5. Set `createdAtUtc` and `updatedAtUtc` to current UTC time.
6. Add relevant `labels` following the prefix convention.
7. Run validation: `pwsh ./Scripts/Test-TaskRecords.ps1`

## Updating a Task

- Always increment `revision` on changes.
- Update `updatedAtUtc`.
- When setting to `Done`, add a comment with evidence.
- When setting to `Archived` or `Rejected`, add a comment with rationale.

## Validation

Run before any commit touching `Data/Tasks/`:
```powershell
pwsh ./Scripts/Test-TaskRecords.ps1
```

This checks:
- All JSON files parse correctly
- All `status` values are in the allowed set
- All `priority` values are in the allowed set
- No duplicate `key` values
- No embedded control characters in string values
- No orphan `.md` files without corresponding `.json`
