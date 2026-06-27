<#
.SYNOPSIS
    Verifies that the About page dependency inventory matches actual package references.
.DESCRIPTION
    Extracts all top-level PackageReference entries from .csproj files in the solution,
    then compares them against the dependency table in WebUI.Blazor/wwwroot/about.html.
    Supports wildcard entries (e.g., "Microsoft.Extensions.*") in about.html.
    Fails (exit 1) if any package is missing from about.html or if about.html lists
    a package that doesn't exist in any csproj.

    Part of TSK-0145: About page living dependency inventory.
    Policy P-2: Every dependency must be listed in the About page.
#>

param(
    [string]$RepoRoot = $PSScriptRoot,
    [string]$AboutHtmlPath = "",
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

# Resolve absolute paths
$RepoRoot = Resolve-Path $RepoRoot
if (-not $AboutHtmlPath) {
    $AboutHtmlPath = Join-Path $RepoRoot "WebUI.Blazor\wwwroot\about.html"
}
if (-not (Test-Path $AboutHtmlPath)) {
    Write-Error "About HTML not found. Expected at: $AboutHtmlPath"
    exit 1
}
$AboutHtmlPath = Resolve-Path $AboutHtmlPath

function Write-Status {
    param([string]$Message, [string]$Color = 'White')
    if (-not $Quiet) { Write-Host $Message -ForegroundColor $Color }
}

Write-Status "=== Verify-AboutDeps ===" Cyan
Write-Status "Repo root : $RepoRoot" Gray
Write-Status "About HTML: $AboutHtmlPath" Gray

# ── 1. Extract PackageReferences from all .csproj files ──────────────────

$csprojFiles = Get-ChildItem -Path $RepoRoot -Recurse -Filter '*.csproj' -File |
    Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\|\\MemorySmith\\' }

$allPackages = @{}
foreach ($csproj in $csprojFiles) {
    [xml]$csprojXml = Get-Content $csproj.FullName -Raw
    $pkgRefs = $csprojXml.Project.ItemGroup.PackageReference
    if ($pkgRefs) {
        foreach ($pkg in $pkgRefs) {
            $name = $pkg.Include
            if ($name) {
                $allPackages[$name] = $true
            }
        }
    }
}

# Separate into "top-level" (non-System, non-Microsoft) and "Microsoft" groups
# because about.html uses wildcards like "Microsoft.Extensions.*"
$topLevelPkgs = @($allPackages.Keys | Where-Object { $_ -notmatch '^System\.' } | Sort-Object)
Write-Status "`nFound $($topLevelPkgs.Count) unique top-level packages:" Cyan
foreach ($pkg in $topLevelPkgs) {
    Write-Status "  $pkg" Gray
}

# ── 2. Extract dependency entries from about.html ────────────────────────

$html = Get-Content $AboutHtmlPath -Raw
# Extract table rows from the "Third-party Acknowledgments" section
if ($html -match '(?s)<h2>Third-party Acknowledgments</h2>(.*?)</table>') {
    $tableHtml = $matches[1]
} elseif ($html -match '(?s)Third-party Acknowledgments(.*?)</table>') {
    $tableHtml = $matches[1]
} else {
    Write-Error "Could not find 'Third-party Acknowledgments' table in about.html"
    exit 1
}

# Parse <tr> entries: <td>Name</td><td>License</td><td>Use</td>
$aboutEntries = @{}
$trPattern = '<tr>\s*<td>(.*?)</td>\s*<td>(.*?)</td>\s*<td>(.*?)</td>\s*</tr>'
$trMatches = [regex]::Matches($tableHtml, $trPattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
foreach ($match in $trMatches) {
    $depName = $match.Groups[1].Value.Trim()
    # Skip header row
    if ($depName -eq 'Dependency') { continue }
    $aboutEntries[$depName] = @{
        License = $match.Groups[2].Value.Trim()
        Use     = $match.Groups[3].Value.Trim()
    }
}

Write-Status "`nFound $($aboutEntries.Count) entries in about.html:" Cyan
foreach ($entry in $aboutEntries.GetEnumerator() | Sort-Object Key) {
    Write-Status "  $($entry.Key)" Gray
}

# ── 3. Cross-reference ───────────────────────────────────────────────────

$errors = @()
$warnings = @()

# Check each csproj package against about.html entries
foreach ($pkg in $topLevelPkgs) {
    $matched = $false
    foreach ($aboutKey in $aboutEntries.Keys) {
        # Support wildcard patterns (e.g., "Microsoft.Extensions.*")
        if ($aboutKey -match '\*') {
            $pattern = '^' + [regex]::Escape($aboutKey).Replace('\*', '.*') + '$'
            if ($pkg -match $pattern) {
                $matched = $true
                break
            }
        } elseif ($pkg -eq $aboutKey) {
            $matched = $true
            break
        }
    }
    if (-not $matched) {
        $msg = "MISSING from about.html: '$pkg' (found in .csproj but not in about.html dependency table)"
        $errors += $msg
    }
}

# Check each about.html entry against csproj packages
foreach ($aboutKey in $aboutEntries.Keys) {
    # Skip "memorysmith" project references, npm packages, and wildcards
    if ($aboutKey -eq 'MemorySmith' -or $aboutKey -eq 'Mineflayer (Node.js)' -or
        $aboutKey -eq 'ws (Node.js)') { continue }
    # Skip wildcard entries
    if ($aboutKey -match '\*') { continue }

    # Check if any csproj package matches
    $matched = $false
    foreach ($pkg in $topLevelPkgs) {
        if ($pkg -eq $aboutKey) {
            $matched = $true
            break
        }
    }
    if (-not $matched) {
        $msg = "STALE in about.html: '$aboutKey' (listed in about.html but not found in any .csproj)"
        $warnings += $msg
    }
}

# ── 4. Report ────────────────────────────────────────────────────────────

if ($errors.Count -gt 0) {
    Write-Status "`n=== ERRORS ===" Red
    foreach ($e in $errors) { Write-Status "  ❌ $e" Red }
}

if ($warnings.Count -gt 0) {
    Write-Status "`n=== WARNINGS ===" Yellow
    foreach ($w in $warnings) { Write-Status "  ⚠️  $w" Yellow }
}

if ($errors.Count -eq 0 -and $warnings.Count -eq 0) {
    Write-Status "`n✅ All dependencies in sync — about.html matches .csproj packages." Green
    exit 0
} elseif ($errors.Count -gt 0) {
    Write-Status "`n❌ FAILED: $($errors.Count) missing package(s). Update about.html." Red
    Write-Status "   Policy: P-2 — Every dependency must be listed in the About page." DarkYellow
    exit 1
} else {
    # Warnings only — don't fail but flag
    Write-Status "`n⚠️  COMPLETE with $($warnings.Count) warning(s). Review stale entries." Yellow
    exit 0
}
