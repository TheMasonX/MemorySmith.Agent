<#
.SYNOPSIS
Displays the status of the MemorySmith World Wiki (TestWorld) service.
#>
[CmdletBinding()]
param(
  [string]$ServiceName = "MemorySmith - World Wiki (TestWorld)"
)

$repoRoot   = Split-Path -Parent $PSScriptRoot
$serviceDir = Join-Path $repoRoot ".service"
$portFile   = Join-Path $serviceDir "world-wiki.port"
$logFile    = Join-Path $serviceDir "world-wiki.log"
$errFile    = Join-Path $serviceDir "world-wiki.err.log"

$svc  = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$port = if (Test-Path $portFile) { (Get-Content $portFile -Raw).Trim() } else { "unknown" }

if ($null -ne $svc) {
  Write-Host "Mode          : windows-service"
  Write-Host "ServiceName   : $ServiceName"
  Write-Host "ServiceStatus : $($svc.Status)"
  Write-Host "Port          : $port"
  Write-Host "URL           : http://127.0.0.1:$port"
  if (Test-Path $logFile) { Write-Host "OutLog        : $logFile" }
  if (Test-Path $errFile) { Write-Host "ErrLog        : $errFile" }
  return
}

Write-Host "Status: stopped or not installed"
