<#
.SYNOPSIS
Builds, deploys, and starts a MemorySmith wiki service for the MemorySmith.Agent codebase.

.DESCRIPTION
Publishes MemorySmith.App from the MemorySmith engine repo and registers a Windows
service that serves this repo's Data/ as a live wiki — architecture docs, guides,
plans, council reviews, and more.

This is the knowledge-store service that MemorySmith.Agent's RestMemoryGateway
connects to at runtime.

.PARAMETER MemorySmithRepoPath
Path to the MemorySmith engine repository (where MemorySmith.App.csproj lives).
Default: "D:\@Repos\MemorySmith"

.PARAMETER PreferredPort
Primary HTTP port for the wiki service.
Default: 6868

.PARAMETER FallbackPort
Fallback HTTP port if the primary is in use.
Default: 6968

.PARAMETER Configuration
dotnet build/publish configuration (Release or Debug).
Default: "Release"

.PARAMETER ServiceName
Windows Service name for the codebase wiki.
Default: "MemorySmith - Agent Wiki"

.PARAMETER ServiceDisplayName
Display name shown in Windows Service Manager.
Default: "MemorySmith - Agent Wiki"

.PARAMETER NoBuild
Skip the dotnet publish step. Uses the existing publish output from
artifacts/MemorySmith.App. Useful when only reinstalling or restarting.

.PARAMETER Force
Stop any existing service before deploying, even if running.

.EXAMPLE
# Full deploy with defaults
.\Scripts\Deploy-CodebaseWiki.ps1

.EXAMPLE
# Custom ports
.\Scripts\Deploy-CodebaseWiki.ps1 -PreferredPort 5050 -FallbackPort 6060

.EXAMPLE
# Refresh service without rebuilding
.\Scripts\Deploy-CodebaseWiki.ps1 -NoBuild

.EXAMPLE
# Specify the MemorySmith engine repo location
.\Scripts\Deploy-CodebaseWiki.ps1 -MemorySmithRepoPath "C:\Projects\MemorySmith"
#>
[CmdletBinding()]
param(
  [string]$MemorySmithRepoPath = "D:\@Repos\MemorySmith",
  [int]$PreferredPort = 6868,
  [int]$FallbackPort = 6968,
  [string]$Configuration = "Release",
  [string]$ServiceName = "MemorySmith - Agent Wiki",
  [string]$ServiceDisplayName = "MemorySmith - Agent Wiki",
  [switch]$NoBuild,
  [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Resolve paths ──────────────────────────────────────────────────────────
$repoRoot      = Split-Path -Path $PSScriptRoot -Parent
$serviceDir    = Join-Path $repoRoot ".service"
$publishDir    = Join-Path $repoRoot "artifacts/MemorySmith.App"
$publishExe    = Join-Path $publishDir "MemorySmith.App.exe"
$publishDll    = Join-Path $publishDir "MemorySmith.App.dll"

$appProject    = Join-Path $MemorySmithRepoPath "MemorySmith.App/MemorySmith.App.csproj"
$sourceData    = Join-Path $repoRoot "Data"
$memoryDir     = Join-Path $sourceData "Memories"
$pagesPath     = Join-Path $sourceData "Pages"
$varsPath      = Join-Path $sourceData "vars.json"
$eventLogPath  = Join-Path $sourceData "Events/audit.log"
$keysPath      = Join-Path $sourceData "Keys"
$modelsPath    = Join-Path $sourceData "Models"

$logFile       = Join-Path $serviceDir "codebase-wiki.log"
$errFile       = Join-Path $serviceDir "codebase-wiki.err.log"
$portFile      = Join-Path $serviceDir "codebase-wiki.port"

# ── Prerequisites ──────────────────────────────────────────────────────────
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet SDK is required but was not found on PATH."
}

if (-not (Test-Path $MemorySmithRepoPath)) {
  throw "MemorySmith engine repo not found: $MemorySmithRepoPath"
}

if (-not (Test-Path $sourceData)) {
  throw "Data directory not found at: $sourceData"
}

# ── Helper functions ───────────────────────────────────────────────────────
function Test-PortAvailable([int]$Port) {
  return -not (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

function Test-IsAdministrator {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = [Security.Principal.WindowsPrincipal]::new($identity)
  return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Stop-CodebaseService {
  $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
  if ($null -ne $svc -and $svc.Status -ne 'Stopped') {
    Write-Host "  Stopping Windows service '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force
    $svc.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
    Write-Host "  Service stopped."
  }
  elseif ($null -ne $svc) {
    Write-Host "  Service '$ServiceName' is already stopped."
  }
  else {
    Write-Host "  Service '$ServiceName' is not installed."
  }
}

function Unregister-CodebaseService {
  if (-not (Test-Path $publishDll)) {
    Write-Host "  Publish DLL not found at $publishDll — skipping unregister."
    return
  }

  $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
  if ($null -ne $svc) {
    Write-Host "  Unregistering Windows service '$ServiceName'..."
    & dotnet $appArtifact uninstall --service-name $ServiceName | Out-Host
    if ($LASTEXITCODE -ne 0) {
      Write-Warning "  Uninstall returned exit code $LASTEXITCODE. Continuing..."
    }
  }
}

# ── Admin check ────────────────────────────────────────────────────────────
if (-not (Test-IsAdministrator)) {
  throw "This script must be run from an elevated PowerShell session (Run as Administrator)."
}

# ── Header ─────────────────────────────────────────────────────────────────
Write-Host "── Deploy-CodebaseWiki ───────────────────────────────────"
Write-Host "MemorySmith engine repo : $MemorySmithRepoPath"
Write-Host "Agent repo (data)       : $repoRoot"
Write-Host "Data directory          : $sourceData"
Write-Host "Service name            : $ServiceName"
Write-Host ""

# ── Stop any existing service ──────────────────────────────────────────────
Stop-CodebaseService

# ── Publish MemorySmith.App ────────────────────────────────────────────────
if (-not $NoBuild) {
  if (-not (Test-Path $appProject)) {
    throw "MemorySmith.App project not found at: $appProject"
  }

  Write-Host "Building MemorySmith.App ($Configuration)..."
  New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

  & dotnet publish $appProject -c $Configuration -o $publishDir -nologo | Out-Host
  if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
  }
  Write-Host "  Published to: $publishDir"
}
else {
  if (-not (Test-Path $publishExe) -and -not (Test-Path $publishDll)) {
    throw "No publish output found at $publishDir. Run without -NoBuild first."
  }
  Write-Host "Skipping build (-NoBuild). Using existing publish: $publishDir"
}

# Use the .dll with "dotnet" — the .exe is a native host that dotnet CLI cannot load as a managed assembly.
$appArtifact = if (Test-Path $publishDll) { $publishDll } else { $publishExe }
Write-Host "  App artifact: $appArtifact"

# ── Prepare data directories ──────────────────────────────────────────────
New-Item -ItemType Directory -Path $serviceDir -Force | Out-Null
New-Item -ItemType Directory -Path $keysPath -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $eventLogPath -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path $modelsPath -Force | Out-Null

if (-not (Test-Path $varsPath)) {
  Set-Content -Path $varsPath -Value "{}" -Encoding utf8
  Write-Host "  Created default vars.json at $varsPath"
}
else {
  Write-Host "  vars.json: $((Get-Item $varsPath).Length) bytes"
}

# ── Select port ────────────────────────────────────────────────────────────
$chosenPort = if (Test-PortAvailable -Port $PreferredPort) {
  $PreferredPort
}
elseif (Test-PortAvailable -Port $FallbackPort) {
  Write-Warning "Preferred port $PreferredPort is in use. Falling back to port $FallbackPort."
  $FallbackPort
}
else {
  throw "Neither preferred port $PreferredPort nor fallback port $FallbackPort is available."
}

# ── Unregister stale service ──────────────────────────────────────────────
Unregister-CodebaseService

# ── Install service ────────────────────────────────────────────────────────
Write-Host "Installing Windows service '$ServiceName' on port $chosenPort..."

& dotnet $appArtifact install `
  --service-name $ServiceName `
  --service-display-name $ServiceDisplayName `
  --service-description "MemorySmith wiki service for MemorySmith.Agent — internal project documentation" `
  --service-start-type auto `
  --memory-directory $memoryDir `
  --port $chosenPort `
  -- `
  --MemorySmith:DataProtectionKeysPath $keysPath `
  --MemorySmith:AllowedFileRoots:0 $repoRoot `
  --MemorySmith:AllowedFileRoots:1 $pagesPath `
  --MemorySmith:AllowedFileRoots:2 (Join-Path $pagesPath "Sources") `
  --MemorySmith:SourceLinks:AllowedFileRoots:0 $repoRoot `
  --MemorySmith:SourceLinks:AllowedFileRoots:1 $pagesPath `
  --MemorySmith:SourceLinks:AllowedFileRoots:2 (Join-Path $pagesPath "Sources") | Out-Host

if ($LASTEXITCODE -ne 0) {
  throw "Service installation failed with exit code $LASTEXITCODE."
}

# ── Start service ──────────────────────────────────────────────────────────
Write-Host "Starting service '$ServiceName'..."
Start-Service -Name $ServiceName
$svc = Get-Service -Name $ServiceName
$svc.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))
Write-Host "  Service started."

# ── Write port file ────────────────────────────────────────────────────────
Set-Content -Path $portFile -Value $chosenPort

# ── Summary ────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "── Deployment Complete ───────────────────────────────────"
Write-Host "ServiceName    : $ServiceName"
Write-Host "URL            : http://127.0.0.1:$chosenPort"
Write-Host "Data directory : $sourceData"
Write-Host "  Memories     : $memoryDir"
Write-Host "  Pages        : $pagesPath"
Write-Host "Publish Dir    : $publishDir"
Write-Host "Out Log        : $logFile"
Write-Host "Err Log        : $errFile"
Write-Host ""
Write-Host "Management:"
Write-Host "  Stop    : .\Scripts\Stop-CodebaseWikiService.ps1"
Write-Host "  Status  : .\Scripts\Get-CodebaseWikiStatus.ps1"
Write-Host "  Uninstall: .\Scripts\Uninstall-CodebaseWikiService.ps1"
Write-Host ""
Write-Host "The agent's RestMemoryGateway should point to: http://127.0.0.1:$chosenPort"
Write-Host ""

