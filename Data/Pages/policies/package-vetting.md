# Package Vetting Policy — MemorySmith.Agent

**Adopted:** Sprint 51 (2026-06-26)
**Scope:** All NuGet and npm dependencies in the MemorySmith.Agent solution.

## Policy

### P-1: No package shall be added without a documented justification

Every new dependency must have a written answer to:
1. **What problem does it solve?** — Specific feature or capability it enables.
2. **Why not use what we already have?** — Check existing deps for overlap.
3. **What is the license?** — Must be MIT, Apache-2.0, BSD-2/3-Clause, or similar permissive license. No GPL/AGPL.
4. **What is the maintenance status?** — Is the package actively maintained? Last release date? Open issue count?
5. **What is its dependency chain?** — Run `dotnet list package --include-transitive` and inspect for known vulnerabilities.
6. **What is the attack surface?** — Does it run native code? Open network connections? Write to disk? Access system APIs?

### P-2: Every dependency must be listed in the About page

`WebUI.Blazor/wwwroot/about.html` is the canonical inventory of third-party dependencies
with license and use description. Adding or removing a package requires updating the About page
in the same commit.

### P-3: Vulnerable packages are a P0 blocker

- `dotnet list package --vulnerable` must return zero results before any sprint ships.
- Any NU1903 or higher warning is a blocking defect, not a warning to suppress.
- If a transitive dependency has an unpatched CVE, the parent package must be replaced or removed.

### P-4: Deprecated packages are prohibited

- NuGet-deprecated packages (like `SQLitePCLRaw.lib.e_sqlite3`) shall not be used.
- If a dependency is deprecated upstream, migrate to the suggested alternative or remove the feature.

### P-5: Direct pinning of transitive dependencies requires justification

- Direct `PackageReference` to a transitive dependency (like pinning `SQLitePCLRaw.lib.e_sqlite3`)
  is a code smell. It indicates the parent package hasn't updated its dependency range.
- If pinning is unavoidable, document:
  - The parent package and its minimum version that would fix the issue
  - An issue link tracking the parent's update
  - A scheduled task to remove the pin when the parent updates

## Current Dependency Inventory

| Package | Version | License | Use |
|---|---|---|---|
| Serilog.AspNetCore | 10.0.0 | MIT | Structured logging pipeline |
| Serilog.Sinks.Console | 6.1.1 | MIT | Console log output |
| Serilog.Sinks.File | 7.0.0 | MIT | Persistent file-based log storage |
| Serilog.Sinks.EventLog | 4.0.0 | MIT | Windows Event Log sink |
| Microsoft.Extensions.Logging.Abstractions | 10.0.9 | MIT | Logging abstractions |
| coverlet.collector | 10.0.1 | MIT | Code coverage collection |
| GitHubActionsTestLogger | 3.0.4 | MIT | CI test annotations |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.9 | MIT | Integration test host |
| Microsoft.NET.Test.Sdk | 18.7.0 | MIT | Test runner |
| NUnit | 4.6.1 | MIT | Test framework |
| NUnit.Analyzers | 4.14.0 | MIT | NUnit analyzers |
| NUnit3TestAdapter | 6.2.0 | MIT | VS test adapter |

## Removed (Sprint 51)

| Package | Reason |
|---|---|
| Serilog.Sinks.SQLite 7.0.0 | Transitively depends on deprecated SQLitePCLRaw.lib.e_sqlite3 |
| SQLitePCLRaw.lib.e_sqlite3 2.1.11 | Deprecated, unpatched CVE-2025-6965 (CVSS 7.2 High) |

## Sprint 51 Incident (Post-Mortem)

**What happened:** `Serilog.Sinks.SQLite` was added in Sprint 50 Wave D for runtime telemetry.
It transitively depended on `SQLitePCLRaw.lib.e_sqlite3` which had a known high-severity CVE.
The fix attempt *pinned the vulnerable package directly* in the csproj, which made the situation
worse — it locked us to the exact vulnerable version and suppressed the transitive upgrade path.

**Root cause:** No package vetting policy existed. The dependency was added without:
- Checking the transitive dependency chain for vulnerabilities
- Noticing the package was deprecated upstream
- Evaluating whether the File sink already met the requirement

**Fix:** Removed both packages. File sink provides equivalent persistent log storage.

**Prevention:** This policy (P-1 through P-5) must be followed for all future dependency changes.
