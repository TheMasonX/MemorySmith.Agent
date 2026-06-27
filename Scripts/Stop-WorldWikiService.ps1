<#
.SYNOPSIS
Stops the MemorySmith World Wiki (TestWorld) Windows service.
#>
[CmdletBinding()]
param(
  [switch]$Quiet,
  [string]$ServiceName = "MemorySmith - World Wiki (TestWorld)"
)

$repoRoot   = Split-Path -Parent $PSScriptRoot
$serviceDir = Join-Path $repoRoot ".service"
$pidFile    = Join-Path $serviceDir "world-wiki.pid"
$portFile   = Join-Path $serviceDir "world-wiki.port"

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $svc -and $svc.Status -ne 'Stopped') {
  Stop-Service -Name $ServiceName -Force
  $svc.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
  if (-not $Quiet) { Write-Host "Stopped Windows service '$ServiceName'." }
}
elseif ($null -ne $svc) {
  if (-not $Quiet) { Write-Host "Service '$ServiceName' is already stopped." }
}
else {
  if (-not $Quiet) { Write-Host "Service '$ServiceName' is not installed." }
}

if (Test-Path $pidFile) { Remove-Item $pidFile -ErrorAction SilentlyContinue }
if (Test-Path $portFile) { Remove-Item $portFile -ErrorAction SilentlyContinue }
