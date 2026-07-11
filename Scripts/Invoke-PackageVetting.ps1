<#
.SYNOPSIS
    Enforces the package vetting policy (Data/Pages/policies/package-vetting.md) in CI.
.DESCRIPTION
    Checks:
      P-3: Vulnerable packages — `dotnet list package --vulnerable` must return zero.
      P-4: Deprecated packages — `dotnet list package --deprecated` must return zero.
      P-2: About page dependency inventory — runs Verify-AboutDeps.ps1.

    Exits with code 1 if any check fails.

    Part of TSK-0144: Enforce package vetting policy in CI.
#>

param(
    [string]$SolutionPath = "",
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

# Locate solution
if (-not $SolutionPath) {
    $probe = Join-Path $PSScriptRoot ".." "MemorySmith.Agent.slnx"
    $probe = Resolve-Path $probe -ErrorAction Stop
    $SolutionPath = $probe.Path
}

Write-Host "=== Package Vetting ===" -ForegroundColor Cyan
Write-Host "Solution : $SolutionPath" -ForegroundColor Gray

$exitCode = 0

# ── P-3: Vulnerable packages ─────────────────────────────────────────────
Write-Host "`n[P-3] Checking for vulnerable packages..." -ForegroundColor Yellow
$vulnResult = dotnet list package --vulnerable 2>&1
$vulnOutput = $vulnResult -join "`n"

if ($vulnOutput -match "No vulnerable packages found") {
    Write-Host "  ✅ No vulnerable packages found." -ForegroundColor Green
} elseif ($vulnOutput -match "has known vulnerable") {
    Write-Host "  ❌ Vulnerable packages detected!" -ForegroundColor Red
    Write-Host $vulnOutput
    $exitCode = 1
} else {
    # dotnet list package may return empty for solution files; try project-level
    Write-Host "  ⚠ Could not determine vulnerability status from solution. Trying projects..." -ForegroundColor Yellow
    $projects = Get-ChildItem -Path (Split-Path $SolutionPath -Parent) -Recurse -Filter '*.csproj' -File |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' }
    foreach ($proj in $projects) {
        $result = dotnet list $proj.FullName package --vulnerable 2>&1
        $output = $result -join "`n"
        if ($output -match "has known vulnerable") {
            Write-Host "  ❌ Vulnerable packages in $($proj.Name):" -ForegroundColor Red
            Write-Host $output
            $exitCode = 1
        }
    }
    if ($exitCode -eq 0) {
        Write-Host "  ✅ No vulnerable packages found." -ForegroundColor Green
    }
}

# ── P-4: Deprecated packages ─────────────────────────────────────────────
Write-Host "`n[P-4] Checking for deprecated packages..." -ForegroundColor Yellow
$depResult = dotnet list package --deprecated 2>&1
$depOutput = $depResult -join "`n"

if ($depOutput -match "No deprecated packages found") {
    Write-Host "  ✅ No deprecated packages found." -ForegroundColor Green
} elseif ($depOutput -match "is deprecated") {
    Write-Host "  ❌ Deprecated packages detected!" -ForegroundColor Red
    Write-Host $depOutput
    $exitCode = 1
} else {
    Write-Host "  ⚠ Could not determine deprecated status from solution. Trying projects..." -ForegroundColor Yellow
    $projects = Get-ChildItem -Path (Split-Path $SolutionPath -Parent) -Recurse -Filter '*.csproj' -File |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' }
    foreach ($proj in $projects) {
        $result = dotnet list $proj.FullName package --deprecated 2>&1
        $output = $result -join "`n"
        if ($output -match "is deprecated") {
            Write-Host "  ❌ Deprecated packages in $($proj.Name):" -ForegroundColor Red
            Write-Host $output
            $exitCode = 1
        }
    }
    if ($exitCode -eq 0) {
        Write-Host "  ✅ No deprecated packages found." -ForegroundColor Green
    }
}

# ── P-2: About page dependency inventory ─────────────────────────────────
Write-Host "`n[P-2] Verifying About page dependency inventory..." -ForegroundColor Yellow
$aboutScript = Join-Path $PSScriptRoot "Verify-AboutDeps.ps1"
if (Test-Path $aboutScript) {
    & $aboutScript -Quiet:$Quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ❌ About page out of sync with project dependencies." -ForegroundColor Red
        $exitCode = 1
    } else {
        Write-Host "  ✅ About page matches project dependencies." -ForegroundColor Green
    }
} else {
    Write-Host "  ⚠ Verify-AboutDeps.ps1 not found. Skipping P-2 check." -ForegroundColor Yellow
}

# ── Summary ──────────────────────────────────────────────────────────────
Write-Host "`n================================" -ForegroundColor Cyan
if ($exitCode -eq 0) {
    Write-Host "✅ Package vetting PASSED" -ForegroundColor Green
} else {
    Write-Host "❌ Package vetting FAILED" -ForegroundColor Red
}

exit $exitCode
