#!/usr/bin/env pwsh
# Triage script: classify 32 Critical/High backlog items into groups
# Group A: Entity/Observation/Scene chain (TSK-0146-0155) — planned feature epic
# Group B: Security hardening (TSK-0180, 0181) — still relevant
# Group C: Testing infrastructure (TSK-0189, 0191) — still relevant
# Group D: Adapter telemetry (TSK-0158-0163) — planned
# Group E: Bug fixes/low-priority (misc)
# Group F: Infrastructure (TSK-0012, 0040)
# Group G: Already superseded or absorbed

$taskDir = "D:\@Repos\MemorySmith.Agent\Data\Tasks"

# Read all tasks
$tasks = @{}
Get-ChildItem "$taskDir\*.json" | ForEach-Object {
    try {
        $t = Get-Content $_ -Raw | ConvertFrom-Json
        $tasks[$t.key] = @{ path = $_.FullName; task = $t }
    } catch {}
}

# Entity/Observation/Scene epic — add epicId to link them
$entityEpic = @('TSK-0146','TSK-0147','TSK-0148','TSK-0149','TSK-0150','TSK-0151','TSK-0152','TSK-0153','TSK-0154','TSK-0155')
foreach ($key in $entityEpic) {
    if ($tasks.ContainsKey($key)) {
        $t = $tasks[$key].task
        $t | Add-Member -NotePropertyName 'epicId' -NotePropertyValue 'tsk-0146-entity-observation-scene' -Force
        if (-not $t.labels -contains 'domain:observation') {
            $labels = [System.Collections.Generic.List[object]]::new($t.labels ?? @())
            [void]$labels.Add('domain:observation')
            $t.labels = $labels.ToArray()
        }
        $t | ConvertTo-Json -Depth 10 | Set-Content $tasks[$key].path -NoNewline
        Write-Host "$key : epic tagged — $($t.title)"
    }
}

# Also create an epic parent task TSK-0146 as the epic container
if ($tasks.ContainsKey('TSK-0146')) {
    $t = $tasks['TSK-0146'].task
    $t | Add-Member -NotePropertyName 'epicId' -NotePropertyValue 'tsk-0146-entity-observation-scene' -Force
    $t | ConvertTo-Json -Depth 10 | Set-Content $tasks['TSK-0146'].path -NoNewline
}

# Security tasks — add domain:security label, confirm priority
$securityTasks = @('TSK-0180','TSK-0181')
foreach ($key in $securityTasks) {
    if ($tasks.ContainsKey($key)) {
        $t = $tasks[$key].task
        if (-not $t.labels -contains 'domain:security') {
            $labels = [System.Collections.Generic.List[object]]::new($t.labels ?? @())
            [void]$labels.Add('domain:security')
            $t.labels = $labels.ToArray()
            $t | ConvertTo-Json -Depth 10 | Set-Content $tasks[$key].path -NoNewline
        }
        Write-Host "$key : security tagged — $($t.title)"
    }
}

# Testing tasks
$testTasks = @('TSK-0189','TSK-0191')
foreach ($key in $testTasks) {
    if ($tasks.ContainsKey($key)) {
        $t = $tasks[$key].task
        if (-not $t.labels -contains 'domain:testing') {
            $labels = [System.Collections.Generic.List[object]]::new($t.labels ?? @())
            [void]$labels.Add('domain:testing')
            $t.labels = $labels.ToArray()
            $t | ConvertTo-Json -Depth 10 | Set-Content $tasks[$key].path -NoNewline
        }
        Write-Host "$key : testing tagged — $($t.title)"
    }
}

# Adapter telemetry
$adapterTasks = @('TSK-0158','TSK-0159','TSK-0160','TSK-0161','TSK-0162','TSK-0163')
foreach ($key in $adapterTasks) {
    if ($tasks.ContainsKey($key)) {
        $t = $tasks[$key].task
        if (-not $t.labels -contains 'domain:adapter') {
            $labels = [System.Collections.Generic.List[object]]::new($t.labels ?? @())
            [void]$labels.Add('domain:adapter')
            $t.labels = $labels.ToArray()
            $t | ConvertTo-Json -Depth 10 | Set-Content $tasks[$key].path -NoNewline
        }
        Write-Host "$key : adapter tagged — $($t.title)"
    }
}

# Bug fix tasks
$bugTasks = @('TSK-0034','TSK-0067','TSK-0070','TSK-0077','TSK-0188','TSK-0192','TSK-0195')
foreach ($key in $bugTasks) {
    if ($tasks.ContainsKey($key)) {
        $t = $tasks[$key].task
        if (-not $t.labels -contains 'type:bug') {
            $labels = [System.Collections.Generic.List[object]]::new($t.labels ?? @())
            [void]$labels.Add('type:bug')
            $t.labels = $labels.ToArray()
            $t | ConvertTo-Json -Depth 10 | Set-Content $tasks[$key].path -NoNewline
        }
        Write-Host "$key : bug tagged — $($t.title)"
    }
}

# Infrastructure
$infraTasks = @('TSK-0012','TSK-0040')
foreach ($key in $infraTasks) {
    if ($tasks.ContainsKey($key)) {
        $t = $tasks[$key].task
        if (-not $t.labels -contains 'domain:infra') {
            $labels = [System.Collections.Generic.List[object]]::new($t.labels ?? @())
            [void]$labels.Add('domain:infra')
            $t.labels = $labels.ToArray()
            $t | ConvertTo-Json -Depth 10 | Set-Content $tasks[$key].path -NoNewline
        }
        Write-Host "$key : infra tagged — $($t.title)"
    }
}

Write-Host "`nTriage complete — 32 tasks classified into domain groups"
