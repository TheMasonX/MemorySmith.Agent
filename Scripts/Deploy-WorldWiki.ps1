<#
.SYNOPSIS
Builds, deploys, and starts a MemorySmith wiki service for the Minecraft World KB.

.DESCRIPTION
Publishes MemorySmith.App from the MemorySmith engine repo and registers a Windows
service that serves the Minecraft World KB data at D:\Minecraft\MemorySmith\TestWorld
as a standalone wiki — block references, biome guides, crafting recipes, exploration
observations, and world facts.

This is the world-knowledge service that MemorySmith.Agent's "world" keyed
RestMemoryGateway connects to at runtime for SearchMemory and CreatePage tools.

.PARAMETER MemorySmithRepoPath
Path to the MemorySmith engine repository (where MemorySmith.App.csproj lives).
Default: "D:\@Repos\MemorySmith"

.PARAMETER WorldDataRoot
Root directory of the world KB data (Memories, Pages, Events subdirectories).
Default: "D:\Minecraft\MemorySmith\TestWorld"

.PARAMETER PreferredPort
Primary HTTP port for the wiki service.
Default: 6869

.PARAMETER FallbackPort
Fallback HTTP port if the primary is in use.
Default: 6969

.PARAMETER Configuration
dotnet build/publish configuration (Release or Debug).
Default: "Release"

.PARAMETER ServiceName
Windows Service name for the world wiki.
Default: "MemorySmith - World Wiki (TestWorld)"

.PARAMETER ServiceDisplayName
Display name shown in Windows Service Manager.
Default: "MemorySmith - World Wiki (TestWorld)"

.PARAMETER NoBuild
Skip the dotnet publish step. Uses the existing publish output from
artifacts/MemorySmith.App. Useful when only reinstalling or restarting.

.PARAMETER Force
Stop any existing service before deploying, even if running.

.EXAMPLE
# Full deploy with defaults
.\Scripts\Deploy-WorldWiki.ps1

.EXAMPLE
# Custom world data directory
.\Scripts\Deploy-WorldWiki.ps1 -WorldDataRoot "D:\Minecraft\MemorySmith\SurvivalWorld"

.EXAMPLE
# Custom port
.\Scripts\Deploy-WorldWiki.ps1 -PreferredPort 6870 -FallbackPort 7870

.EXAMPLE
# Refresh service without rebuilding
.\Scripts\Deploy-WorldWiki.ps1 -NoBuild

.EXAMPLE
# Specify the MemorySmith engine repo location
.\Scripts\Deploy-WorldWiki.ps1 -MemorySmithRepoPath "C:\Projects\MemorySmith"
#>
[CmdletBinding()]
param(
  [string]$MemorySmithRepoPath = "D:\@Repos\MemorySmith",
  [string]$WorldDataRoot = "D:\Minecraft\MemorySmith\TestWorld",
  [int]$PreferredPort = 6869,
  [int]$FallbackPort = 6969,
  [string]$Configuration = "Release",
  [string]$ServiceName = "MemorySmith - World Wiki (TestWorld)",
  [string]$ServiceDisplayName = "MemorySmith - World Wiki (TestWorld)",
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

# World KB data paths (all under $WorldDataRoot)
$memoryDir     = Join-Path $WorldDataRoot "Memories"
$pagesPath     = Join-Path $WorldDataRoot "Pages"
$eventLogPath  = Join-Path $WorldDataRoot "Events/audit.log"
$keysPath      = Join-Path $WorldDataRoot "Keys"
$modelsPath    = Join-Path $WorldDataRoot "Models"
$dbPath        = Join-Path $WorldDataRoot "memorysmith.db"
$varsPath      = Join-Path $WorldDataRoot "vars.json"

$logFile       = Join-Path $serviceDir "world-wiki.log"
$errFile       = Join-Path $serviceDir "world-wiki.err.log"
$portFile      = Join-Path $serviceDir "world-wiki.port"

# ── Prerequisites ──────────────────────────────────────────────────────────
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet SDK is required but was not found on PATH."
}

if (-not (Test-Path $MemorySmithRepoPath)) {
  throw "MemorySmith engine repo not found: $MemorySmithRepoPath"
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

function Stop-WorldWikiService {
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

function Unregister-WorldWikiService {
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
Write-Host "── Deploy-WorldWiki ───────────────────────────────────────"
Write-Host "MemorySmith engine repo : $MemorySmithRepoPath"
Write-Host "World data root         : $WorldDataRoot"
Write-Host "Service name            : $ServiceName"
Write-Host ""

# ── Stop any existing service ──────────────────────────────────────────────
Stop-WorldWikiService

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

# ── Prepare world data directories & files ─────────────────────────────────
New-Item -ItemType Directory -Path $serviceDir -Force | Out-Null
New-Item -ItemType Directory -Path $memoryDir -Force | Out-Null
New-Item -ItemType Directory -Path $pagesPath -Force | Out-Null
New-Item -ItemType Directory -Path $keysPath -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $eventLogPath -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path $modelsPath -Force | Out-Null

if (-not (Test-Path $varsPath)) {
  $varsContent = @{
    WorldDataRoot   = $WorldDataRoot
    MemorySmithRepo = $MemorySmithRepoPath
    MinecraftData   = $WorldDataRoot
  } | ConvertTo-Json
  Set-Content -Path $varsPath -Value $varsContent -Encoding utf8
  Write-Host "  Created default vars.json at $varsPath"
}
else {
  Write-Host "  vars.json: $((Get-Item $varsPath).Length) bytes"
}

# Ensure Core/Memories subdirectories exist
@("Core", "Working", "Unconsolidated", "Deprecated") | ForEach-Object {
  $sub = Join-Path $memoryDir $_
  if (-not (Test-Path $sub)) { New-Item -ItemType Directory -Path $sub -Force | Out-Null }
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
Unregister-WorldWikiService

# ── Install service ────────────────────────────────────────────────────────
Write-Host "Installing Windows service '$ServiceName' on port $chosenPort..."

& dotnet $appArtifact install `
  --service-name $ServiceName `
  --service-display-name $ServiceDisplayName `
  --service-description "MemorySmith wiki service for Minecraft World KB (TestWorld) — block locations, exploration, world facts" `
  --service-start-type auto `
  --memory-directory $memoryDir `
  --port $chosenPort `
  -- `
  --MemorySmith:DataPath $memoryDir `
  --MemorySmith:PagesPath $pagesPath `
  --MemorySmith:EventLogPath $eventLogPath `
  --MemorySmith:DataProtectionKeysPath $keysPath `
  --MemorySmith:VarsPath $varsPath `
  --MemorySmith:Database:ConnectionString "Data Source=$dbPath" `
  --MemorySmith:Audit:JsonlPath (Join-Path (Split-Path $eventLogPath -Parent) "audit-{yyyy}-W{week}.jsonl") `
  --MemorySmith:History:RootPath (Join-Path $WorldDataRoot ".history") `
  --MemorySmith:Auth:AnonymousAccess Editor `
  --MemorySmith:Auth:OpenLocalEditorCompatibility true `
  --MemorySmith:Auth:Setup:AllowLoopbackBootstrap true `
  --MemorySmith:SecurityProfile local-dev `
  --MemorySmith:SourceLinks:AllowedFileRoots:0 $WorldDataRoot `
  --MemorySmith:SourceLinks:AllowedFileRoots:1 $pagesPath | Out-Host

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
Write-Host "World data     : $WorldDataRoot"
Write-Host "  Memories     : $memoryDir"
Write-Host "  Pages        : $pagesPath"
Write-Host "  Database     : $dbPath"
Write-Host "Publish Dir    : $publishDir"
Write-Host "Out Log        : $logFile"
Write-Host "Err Log        : $errFile"
Write-Host ""
Write-Host "Management:"
Write-Host "  Stop    : .\Scripts\Stop-WorldWikiService.ps1"
Write-Host "  Status  : .\Scripts\Get-WorldWikiStatus.ps1"
Write-Host "  Uninstall: .\Scripts\Uninstall-WorldWikiService.ps1"
Write-Host ""
Write-Host "Agent's RestMemoryGateway WorldKbUrl should point to: http://127.0.0.1:$chosenPort"
Write-Host ""
