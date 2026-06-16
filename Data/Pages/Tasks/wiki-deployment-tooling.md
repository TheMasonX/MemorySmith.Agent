# Task: Wiki Deployment Tooling & Standardized Gitignore

Status: **PLANNED**
Confidence: 0.85 (clear scope, builds on working patterns)
Tags: `wiki`, `deployment`, `devops`, `tooling`, `infrastructure`

## Goal

Create reusable, well-documented tooling for deploying a MemorySmith wiki service
alongside any agent or project that wants a seeded wiki. This covers the gitignore
contract, deployment scripts, and the onboarding workflow.

## Motivation

The MemorySmith.Agent repo now has a working `Deploy-CodebaseWiki.ps1` script that
publishes MemorySmith.App from the engine repo and deploys a Windows service pointed
at this repo's `Data/`. The `.gitignore` now has a standardized wiki section.

These patterns should be extracted so any repo — whether a course wiki, an agent
deployment, or a documentation site — can stand up a wiki in minutes without
reinventing the ignore rules or the service lifecycle.

## Deliverables

### 1. Standardized `.gitignore` section (DONE)

A documented block in `.gitignore` covering:

| Pattern | Purpose |
|---|---|
| `Data/memorysmith.db*` | SQLite databases (memory store, metadata) |
| `Data/Keys/*` | Auto-generated data protection keys |
| `Data/Models/`, `*.onnx` | Embedding models |
| `Data/Graph/` | Code-search index artifacts |
| `Data/Events/` | Audit event logs |
| `.service/` | Per-process PID/port/log files from Deploy-* |
| `appsettings.Local*.json`, `[Ss]ecrets.json` | Local overrides |
| `BenchmarkDotNet.Artifacts/` | Benchmark output |

**Evidence**: Added to `D:\@Repos\MemorySmith.Agent\.gitignore`.
**Source reference**: `D:\@Repos\MemorySmith\.gitignore` (lines for `Data/Models/`, `*.onnx`,
`Data/memorysmith.db*`, `Data/Keys/`, `artifacts/`, and local settings).

### 2. `Deploy-WikiService.ps1` — Parameterized, repo-agnostic deployment script (TODO)

Extract the deploy pattern from `Deploy-CodebaseWiki.ps1` into a reusable script that:

- Takes `-DataPath` (path to the `Data/` directory with wiki content)
- Takes `-MemorySmithRepoPath` (where to build MemorySmith.App from)
- Takes `-ServiceName`, `-Port`, etc.
- Handles the full lifecycle: stop → publish → unregister → install → start → health check
- Works with both `.dll` and `.exe` publish output
- Logs to `.service/` under the data root

**Goal**: A single script that can deploy any MemorySmith wiki — used by CMST-341,
MemorySmith.Agent, and future repos identically.

### 3. Service lifecycle companion scripts (DONE for this repo)

```
Stop-CodebaseWikiService.ps1
Get-CodebaseWikiStatus.ps1
Uninstall-CodebaseWikiService.ps1
```

These exist for MemorySmith.Agent. The generic version should follow the same pattern
with parameterized `-ServiceName`.

### 4. Onboarding documentation (TODO)

A markdown page or README section covering:

- Prerequisites (elevated PowerShell, dotnet SDK, MemorySmith engine repo)
- Quick start: clone repo → run deploy script → open wiki
- Gitignore setup for new repos
- Port conflict resolution
- Service management commands

### 5. CI smoke test (STRETCH)

A lightweight validation that:
- The deploy script parses without syntax errors
- The published artifact is a valid .NET assembly
- The service registers and responds on the expected port

## Acceptance Criteria

- [ ] `.gitignore` wiki section is standardized and documented (DONE)
- [ ] `Deploy-WikiService.ps1` accepts `-DataPath`, `-MemorySmithRepoPath`, `-ServiceName`, `-Port`
- [ ] Companion lifecycle scripts accept `-ServiceName` parameter
- [ ] Onboarding doc exists covering setup for a new repo
- [ ] All scripts pass `PowerShell syntax check` (verified via Parser.ParseFile)

## Related

- `Scripts/Deploy-CodebaseWiki.ps1` — first implementation for this repo
- `Scripts/Stop-CodebaseWikiService.ps1`
- `Scripts/Get-CodebaseWikiStatus.ps1`
- `Scripts/Uninstall-CodebaseWikiService.ps1`
- `D:\@Repos\MemorySmith\.gitignore` — canonical ignore reference
- `D:\@Repos\CMST-341\Scripts\Deploy-CourseWikiService.ps1` — parallel implementation

## Next Steps

1. Extract the parameterized deploy script into a repo-agnostic `Deploy-WikiService.ps1`
   that takes `-DataPath` instead of assuming the script's parent repo root.
2. Write onboarding documentation.
3. Add to CI as a syntax-validation gate.

