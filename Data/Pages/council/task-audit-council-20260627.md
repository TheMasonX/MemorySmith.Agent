# Council Review: MemorySmith.Agent Task Store Audit & Remediation

## Decision Statement
Perform a one-time cleanup of the MemorySmith.Agent task store (fixing corrupted JSON, resolving duplicate IDs, normalizing statuses, updating stale records, and adding governance) to restore data integrity, then implement CI validation to prevent recurrence.

## Evidence Reviewed
- `Data/Pages/council/task-audit-evidence-20260627.md` — evidence pack (191 task files, 7 issue categories)
- All 191 task JSON files in `Data/Tasks/` — direct inspection
- Sprint handoffs: Sprint 44 through Sprint 48
- `AGENTS.md` — coding guidelines (Rule E-1/E-2/E-3, ADR D-003/D-011/D-012)
- `Data/Pages/architecture.md` — canonical pipeline
- Repo memory files: sprint44-complete, sprint45-wavea, sprint46-waveb, sprint48-audit-corrections, sprint49-wavec-dashboard
- `Scripts/Test-TaskRecords.ps1` (MemorySmith main repo) — existing validator pattern
- Code files: SmeltableMapping.cs, ChatInterpreter.cs, ApiKeyMiddleware.cs, IntentManager.cs, BuildOrigin.cs, AliasRegistry.cs, ReplanResult.cs, WebSocketBridge.cs

## Findings

| Seat | Recommendation | Confidence | Blocking Concern |
|------|---------------|:----------:|------------------|
| Source-Grounded Archivist | Fix TSK-0082 status drift, resolve 7 duplicate pairs, repair 5 corrupted JSON files, add CI validation | 85% | Cannot confirm extent of PARSE_ERROR issue |
| Data Model Architect | Formalize JSON schema, normalize statuses/casing/ID format, add CI lint, resolve duplicates | 85% | 3 coexisting schemas need consolidation |
| Retrieval Specialist | Dedup IDs (P0), fix corrupted JSON (P0), standardize ID format, normalize status vocabulary | 92% | Duplicate IDs make key-based lookup non-deterministic |
| Skeptical Reviewer | Issues are real but some are overstated — focus on demonstrable harm over cosmetic perfection | 55% | Evidence pack has factual errors (ID format, orphan .md claims) |
| Human Learning Advocate | Dedup IDs as #1 priority, add working-with-tasks guide, document conventions in AGENTS.md | 85% | Duplicates destroy human trust in entire store |
| Synthesizer (Initial) | P0: dedup + fix corrupted JSON. P1: reconcile drifts. P2: triage backlog + CI validation | 80% | 99 backlog items (52%) is too noisy |
| Implementation Engineer | 4-5 hrs total effort; 1.2 hrs quick wins; corrupted JSON fix is 15 min | 85% | tsk-0107 has structural damage beyond \r |
| Process Governor | Add task governance to AGENTS.md, port Test-TaskRecords.ps1, add CI + pre-commit gates | 92% | Agent repo has zero validation — main repo has had this since TSK-0114 |
| Cross-Project Alignment | Do NOT consolidate repos; DO share validator, align status enums, migrate .md→.json | 82% | TSK-NNNN ID numbering collides 100% between repos |
| User Advocate | P0: dedup + fix corrupted JSON + normalize statuses. P1: triage 32 stale Critical/High items | 88% | Consumers need deterministic lookup by key to work reliably |

## Disagreements Resolved

### Dispute 1: ID Format — Old (`tsk-XXXX-slug`) vs New (`TSK-XXXX`)
- **Archivist, User Advocate, Cross-Project Alignment** favor keeping the old format (183 of 191 files use it)
- **Data Model Architect, Retrieval Specialist** favor migrating to new format (`TSK-XXXX` matching `key`)
- **Chairman Decision**: Keep the old `tsk-XXXX-descriptive-slug` format as canonical. It's 96% of files, nothing is gained by renaming 183 files. Create new tasks in the old format going forward. The 3 new-format files (tsk-0193.json, tsk-0194.json, tsk-0197.json) are the unnamed duplicates and should be deleted (their content supersedes the old files — merge before deleting).

### Dispute 2: Severity of Corrupted JSON
- **Skeptical Reviewer**: Cannot reproduce — files parse OK in tooling
- **Retrieval Specialist, Implementation Engineer**: Confirmed `\r` characters exist and cause `JsonReaderException` in `FileTaskService.LoadAll`
- **Chairman Decision**: The files are corrupted per the .NET JSON parser used by the actual task loader. Repair them. The 15-minute fix is worth the confidence gain regardless of whether the corruption is visible in all tools.

### Dispute 3: Should the two repos consolidate?
- **Cross-Project Alignment**: No — domain independence + 100% ID collision
- **Data Model Architect**: Yes — align schemas
- **Chairman Decision**: Do NOT consolidate. Keep independent task stores with independent numbering. Share the validator script and align the status vocabulary. Add a `crossRepoLinks` field to both schemas for explicit cross-references.

### Dispute 4: Should we do a full backlog triage NOW?
- **User Advocate, Human Learning Advocate**: Yes — 32 Critical/High items need triage
- **Implementation Engineer**: 1.5-2 hrs of manual work
- **Chairman Decision**: Defer full triage to a follow-up task. Fix the data integrity issues (corruption, duplicates, statuses) first. Then create a dedicated task for backlog triage with clear scope.

## Acceptance Criteria

| # | Criterion | How to Verify | Phase |
|---|-----------|--------------|-------|
| AC1 | All 5 corrupted JSON files parse successfully | `for f in tsk-0107 tsk-0132 tsk-0133 tsk-0134 tsk-0137; do jq . $f.json >/dev/null; done` | 1 |
| AC2 | No duplicate `key` values across `Data/Tasks/*.json` | `jq '[.key]' *.json \| sort \| uniq -d` returns empty | 1 |
| AC3 | All status values ∈ {Backlog, Ready, InProgress, Blocked, Done, Rejected, Archived} | `jq '[.status]' *.json \| sort -u` returns subset of allowlist | 1 |
| AC4 | TSK-0082 status is `Done` | Direct read of the file | 1 |
| AC5 | TSK-0062 closed as merged-into TSK-0159 with comment | File has `absorbedBy: "TSK-0159"` | 1 |
| AC6 | `Scripts/Test-TaskRecords.ps1` runs without errors on `Data/Tasks/` | `pwsh ./Scripts/Test-TaskRecords.ps1` exits 0 | 2 |
| AC7 | CI pipeline has task-validation step | `.github/workflows/ci.yml` contains the step | 2 |
| AC8 | AGENTS.md has Task Governance section | File contains the section | 2 |
| AC9 | All non-standard statuses normalized | jq query matches AC3 | 1 |
| AC10 | Entity/observation chain (TSK-0146–0155) has a decision | Each task has triage comment or status update | 3 |

## Synthesis

### What Changes NOW (Phase 1 — ~1.5 hours)
1. **Repair 5 corrupted JSON files** — strip embedded `\r` from descriptions, fix tsk-0107's structural damage
2. **Resolve 7 duplicate ID pairs** — for each pair, merge into canonical version, delete the duplicate
3. **Normalize 6 non-standard statuses** — "Open"→Backlog, "Completed"→Done, "Closed - Merged→Done" with note
4. **Fix TSK-0082 status drift** — Backlog→Done (completed in Sprint 48)
5. **Fix TSK-0086/TSK-0098 merge statuses** — Done with absorbedBy field
6. **Merge TSK-0062 into TSK-0159** — close TSK-0062 as superseded

### What Changes NEXT (Phase 2 — ~2 hours)
7. **Port `Scripts/Test-TaskRecords.ps1`** from MemorySmith main repo, adapted for Agent schema
8. **Add CI validation step** to `.github/workflows/ci.yml`
9. **Add Task Governance section to AGENTS.md** — schema, status whitelist, ID format, creation process
10. **Create `.github/instructions/task-governance.md`** — task template and checklist for agents
11. **Update TSK-0117 (Community-Standing .md files → .json)** — or evaluate necessity

### What Changes LATER (Phase 3 — ongoing)
12. **Triage 32 stale Critical/High backlog items** — create a dedicated task for this
13. **Entity/observation feature chain (TSK-0146–0155)** — promote to epic or archive
14. **Evaluate `crossRepoLinks` field** for cross-project task dependencies
15. **Migrate orphan .md files** (TSK-0115/0117/0118) to .json or remove

### What NEVER Changes
- Do NOT consolidate the two repos' task stores
- Do NOT rename 183 files to new ID format
- Do NOT remove .md files that contain unique content not representable in JSON schema
- Do NOT force a full, all-fields-required JSON schema on every task

## Dissent (Unresolved)
1. **Skeptical Reviewer** maintains that 5 "corrupted" files are overstated — the embedded `\r` doesn't break all parsers. Recorded but overruled because the actual runtime loader (`FileTaskService.LoadAll`) breaks on them.
2. **Data Model Architect** maintains that a formal JSON Schema + full migration would prevent all future drift. Recorded but deferred — convention-first + CI lint is sufficient for current velocity.

## Open Questions
1. Are the 5 corrupted files the ONLY ones with `\r` issues, or are there more? (Scan all 191 files for embedded 0x0D in JSON string values)
2. Should the entity/observation chain (TSK-0146–0155) be a formal epic (requires `epicId` field population)?
3. Do any external tools/scripts reference task filenames (e.g., `tsk-XXXX-*.json`) that would break if files are renamed/deleted during dedup?
4. Should `memorysmith_task_get` and `memorysmith_task_list` be updated to handle the cross-repo-links field once added?

## Validation Commands
```powershell
# After Phase 1:
# 1. All JSON files parse
Get-ChildItem Data/Tasks/*.json | ForEach-Object { $_ | ConvertFrom-Json -ErrorAction Stop }

# 2. No duplicate keys
Get-ChildItem Data/Tasks/*.json | ForEach-Object { (Get-Content $_ -Raw | ConvertFrom-Json).key } | Group-Object | Where-Object Count -gt 1

# 3. All statuses in allowlist
Get-ChildItem Data/Tasks/*.json | ForEach-Object { (Get-Content $_ -Raw | ConvertFrom-Json).status } | Sort-Object -Unique

# 4. TSK-0082 is Done
(Get-Content Data/Tasks/tsk-0082*.json -Raw | ConvertFrom-Json).status
```
