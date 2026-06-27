<#
.SYNOPSIS
    Query MemorySmith.Agent logs — agent, adapter, chat, or LLM context — by time range, app instance, level, or tail.

.DESCRIPTION
    Fetch logs from the rolling daily log files under WebUI.Blazor/logs/. Supports
    time-window filtering, level filtering, app-instance isolation via ConnectionId,
    and finding app start/stop boundaries.

    Log sources and formats:
      - Agent (default):   Serilog text  [yyyy-MM-dd HH:mm:ss.fff LVL] ... {ConnectionId}
      - Adapter:           JSON-NDJSON   {"t":"ISO","l":"level","c":"category",...}
      - Chat:              JSON-NDJSON   {"timestampUtc":"ISO","direction":"in|out",...}
      - LLM context:       JSON-NDJSON   {"timestamp":"ISO","direction":"REQUEST|RESPONSE",...}

.PARAMETER Last
    Show logs from the last N minutes/hours. Examples: "30m", "2h", "1d".

.PARAMETER Since
    ISO datetime to start from. Example: "2026-06-27T10:00:00" or "2026-06-27".

.PARAMETER Until
    ISO datetime to end at. Example: "2026-06-27T11:00:00". Defaults to now when -Since is used alone.

.PARAMETER Date
    Which date's log file to query. Format: YYYYMMDD or YYYY-MM-DD. Default: today.

.PARAMETER Source
    Log source. Allowed: agent, adapter, chat, llm. Default: agent.

.PARAMETER Level
    One or more log levels to filter by. Agent example: DBG, INF, WRN, ERR, FTL.
    Adapter example: debug, info, warn, error. Comma-separated.

.PARAMETER Tail
    Show only the last N lines. Default: 50 when no other filter is specified, 0 = all matching lines.

.PARAMETER AppStart
    Switch. Find all app start markers (=== Agent config: lines) and show a table
    of start time, shutdown time (if found), estimated line range, and ConnectionId.

.PARAMETER Run
    Get all logs for a specific app instance, identified by its ConnectionId
    (found via -AppStart). Example: "0HNMK12O9IO0M".

.PARAMETER NoTruncate
    Switch. By default, log messages are truncated to 300 chars for readability.
    Use this to show full lines.

.PARAMETER Raw
    Switch. Output raw log lines without the formatted table. Useful for piping to
    other tools or counting results with Measure-Object.

.PARAMETER Path
    Override the log directory. Default: WebUI.Blazor/logs relative to repo root.

.PARAMETER Quiet
    Switch. Suppress informational headers and only output matching lines.

.EXAMPLE
    .\Get-MSA-Logs.ps1 -Last 30m
    Latest 30 minutes of agent logs.

    .\Get-MSA-Logs.ps1 -Last 5m -Level WRN,ERR
    Latest 5 minutes, warnings and errors only.

    .\Get-MSA-Logs.ps1 -Source adapter -Last 10m
    Last 10 minutes of JSON adapter logs.

    .\Get-MSA-Logs.ps1 -AppStart
    List all agent restarts today with timestamps and ConnectionId.

    .\Get-MSA-Logs.ps1 -Run "0HNMK12O9IO0M"
    Full agent log for a specific process lifetime.

    .\Get-MSA-Logs.ps1 -Date 2026-06-26 -Tail 200
    Last 200 lines from yesterday's agent log.

    .\Get-MSA-Logs.ps1 -Since 10:00 -Until 10:05 -Level ERR
    5-minute window, errors only.
#>

#Requires -Version 7.0

[CmdletBinding(DefaultParameterSetName = 'Tail')]
param(
    [Parameter(ParameterSetName = 'TimeWindow')]
    [string]$Last,

    [Parameter(ParameterSetName = 'TimeWindow')]
    [datetime]$Since,

    [Parameter(ParameterSetName = 'TimeWindow')]
    [datetime]$Until,

    [ValidatePattern('^\d{4}[-]?\d{2}[-]?\d{2}$')]
    [string]$Date,

    [ValidateSet('agent', 'adapter', 'chat', 'llm')]
    [string]$Source = 'agent',

    [string]$Level,

    [int]$Tail = -1,

    [Parameter(ParameterSetName = 'AppStart')]
    [switch]$AppStart,

    [Parameter(ParameterSetName = 'Run')]
    [string]$Run,

    [switch]$NoTruncate,
    [switch]$Raw,
    [string]$Path,
    [switch]$Quiet
)

# ── Helpers ───────────────────────────────────────────────────────────────────

function Get-RepoRoot {
    $dir = $PSScriptRoot
    while ($dir) {
        if (Test-Path (Join-Path $dir 'MemorySmith.Agent.slnx')) { return $dir }
        if (Test-Path (Join-Path $dir '.git') -PathType Container) {
            $slnx = Get-ChildItem (Join-Path $dir '*.slnx') -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($slnx) { return $dir }
        }
        $dir = Split-Path $dir -Parent
    }
    throw "Cannot find repo root (MemorySmith.Agent.slnx not found)"
}

$repoRoot = Get-RepoRoot
$logDir   = if ($Path) { $Path } else { Join-Path $repoRoot 'WebUI.Blazor' 'logs' }

# Parse date
if (-not $Date) { $dateStr = (Get-Date -Format 'yyyyMMdd') }
else { $dateStr = $Date -replace '-', '' }

# Resolve log file path
$filePath = switch ($Source) {
    'agent'   { Join-Path $logDir "memorysmith-agent-$dateStr.log" }
    'adapter' { Join-Path $logDir "adapter-$($dateStr.Substring(0,4))-$($dateStr.Substring(4,2))-$($dateStr.Substring(6,2)).log" }
    'chat'    { Join-Path $logDir "chat" "chat-$($dateStr.Substring(0,4))-$($dateStr.Substring(4,2))-$($dateStr.Substring(6,2)).log" }
    'llm'     { Join-Path $logDir "llm-context" "llm-context-$($dateStr.Substring(0,4))-$($dateStr.Substring(4,2))-$($dateStr.Substring(6,2)).log" }
}

if (-not (Test-Path $filePath)) {
    Write-Warning "Log file not found: $filePath"
    # Suggest nearby files
    $parent = Split-Path $filePath -Parent
    if (Test-Path $parent) {
        $pattern = switch ($Source) {
            'agent'   { "memorysmith-agent-*.log" }
            'adapter' { "adapter-*.log" }
            'chat'    { "chat-*.log" }
            'llm'     { "llm-context-*.log" }
        }
        $available = Get-ChildItem (Join-Path $parent $pattern) | Select-Object -ExpandProperty Name
        if ($available) {
            Write-Host "Available files for source '$Source':" -ForegroundColor Cyan
            $available | ForEach-Object { Write-Host "  $_" }
        }
    }
    return
}

$fileInfo = Get-Item $filePath

# Read file using shared-read to handle files locked by the running agent process
function Read-FileLines($path) {
    try {
        $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $reader = [System.IO.StreamReader]::new($stream)
        try {
            while (-not $reader.EndOfStream) {
                $reader.ReadLine()
            }
        }
        finally { $reader.Dispose() }
    }
    catch { throw }
}

# ── AppStart mode — scan for agent restarts ───────────────────────────────────

if ($AppStart) {
    if ($Source -ne 'agent') {
        Write-Warning "-AppStart only supports source=agent. Switching to agent log."
        # Re-resolve with agent source
        $filePath = Join-Path $logDir "memorysmith-agent-$dateStr.log"
        if (-not (Test-Path $filePath)) { Write-Error "Agent log not found: $filePath"; return }
    }

    # Find start markers (=== Agent config: only — avoids duplicate rows from both markers)
    $startMatches = Select-String -Path $filePath -Pattern '=== Agent config:' -SimpleMatch
    $shutdownMatches = Select-String -Path $filePath -Pattern '\[shutdown\]' -SimpleMatch

    if ($startMatches.Count -eq 0) {
        Write-Host "No app start markers in $($fileInfo.Name). The log starts mid-session or the app was not restarted on this date." -ForegroundColor Yellow
        return
    }

    # Read all lines once for ConnectionId extraction
    $allLines = Get-Content $filePath -ReadCount 0
    $runs = [System.Collections.Generic.List[PSObject]]::new()
    $shutdownLines = @{}; foreach ($sm in $shutdownMatches) { $shutdownLines[$sm.LineNumber] = $true }
    $sortedShutdowns = $shutdownLines.Keys | Sort-Object

    $prevLineNum = -999
    foreach ($m in ($startMatches | Sort-Object LineNumber)) {
        $ln = $m.LineNumber
        # Skip if within 50 lines of previous start (same restart, just twin markers)
        if ($ln - $prevLineNum -lt 50) { continue }
        $prevLineNum = $ln

        # Extract timestamp from the line itself
        # Format: [2026-06-26 20:14:40.561 INF] ...
        $startTs = if ($m.Line -match '^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}\.\d{3})') {
            try { [datetime]::ParseExact($Matches[1], 'yyyy-MM-dd HH:mm:ss.fff', $null) } catch { $null }
        } else { $null }

        # Extract ConnectionId from nearby lines
        $connId = $null
        $s = [Math]::Max(0, $ln - 3)
        $e = [Math]::Min($allLines.Count - 1, $ln + 4)
        for ($j = $s; $j -le $e; $j++) {
            if ($allLines[$j] -match '"ConnectionId":"([^"]+)"') { $connId = $Matches[1]; break }
        }
        # Find closest shutdown after this start
        $endLine = $null; $endTs = $null
        foreach ($sln in $sortedShutdowns) {
            if ($sln -ge $ln) { $endLine = $sln; break }
        }
        if ($endLine) {
            $sl = $allLines[$endLine - 1]
            if ($sl -match '^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}\.\d{3})') {
                try { $endTs = [datetime]::ParseExact($Matches[1], 'yyyy-MM-dd HH:mm:ss.fff', $null) } catch { }
            }
        }
        $runs.Add([PSCustomObject]@{
            StartTime=$startTs; ShutdownTime=$endTs; StartLine=$ln-1
            EndLine=if($endLine){$endLine-1}else{$allLines.Count-1}
            LineCount=if($endLine){$endLine-$ln+1}else{$allLines.Count-$ln+1}
            ConnectionId=$connId; Status=if($endLine){'Shutdown'}else{'Running'}
        })
    }

    # Display as table
    $counter = 0
    $runs | Select-Object @{N='#';E={[int]($script:counter++) + 1}},
        @{N='Started';E={if ($_.StartTime) { $_.StartTime.ToString('HH:mm:ss.fff') } else { '?' }}},
        @{N='Stopped';E={if ($_.ShutdownTime) { $_.ShutdownTime.ToString('HH:mm:ss.fff') } else { '—' }}},
        @{N='Lines';E={$_.LineCount}},
        @{N='Status';E={$_.Status}},
        @{N='ConnectionId';E={if ($_.ConnectionId) { $_.ConnectionId } else { '—' }}} |
        Format-Table -AutoSize

    Write-Host "Tip: Use -Run '<ConnectionId>' to get logs for a specific instance." -ForegroundColor Cyan
    return
}

# ── Run mode — filter by ConnectionId ────────────────────────────────────────

if ($Run) {
    if ($Source -ne 'agent') {
        Write-Warning "-Run only supports source=agent. Switching to agent log."
        $filePath = Join-Path $logDir "memorysmith-agent-$dateStr.log"
        if (-not (Test-Path $filePath)) { Write-Error "Agent log not found: $filePath"; return }
    }

    # Find start line for this ConnectionId
    $matchCount = 0
    $connFilter = '"ConnectionId":"' + $Run + '"'

    if (-not $Quiet) {
        Write-Host "Filtering for ConnectionId: $Run in $($fileInfo.Name)" -ForegroundColor Cyan
    }

    Read-FileLines $filePath | Where-Object { $_ -match $connFilter } | ForEach-Object {
        $matchCount++
        $line = $_
        if (-not $Raw) {
            # Truncate long messages
            if (-not $NoTruncate -and $line.Length -gt 350) { $line = $line.Substring(0, 347) + '...' }
        }
        $line
    }

    if ($matchCount -eq 0) {
        Write-Host "No lines found with ConnectionId '$Run'." -ForegroundColor Yellow
        Write-Host "Run with -AppStart to see available ConnectionIds." -ForegroundColor Gray
    }
    elseif (-not $Quiet) {
        Write-Host "`n($matchCount lines)" -ForegroundColor Gray
    }
    return
}

# ── Time window helpers ───────────────────────────────────────────────────────

function Parse-Last($lastStr) {
    # "30m", "2h", "1d", "45s"
    $m = [regex]::Match($lastStr, '^(\d+)\s*(s|m|h|d)$')
    if (-not $m.Success) { throw "Invalid -Last format. Use e.g. '30m', '2h', '1d'" }
    $val = [int]$m.Groups[1].Value
    $unit = $m.Groups[2].Value
    switch ($unit) {
        's' { return [TimeSpan]::FromSeconds($val) }
        'm' { return [TimeSpan]::FromMinutes($val) }
        'h' { return [TimeSpan]::FromHours($val) }
        'd' { return [TimeSpan]::FromDays($val) }
    }
}

function Format-SerilogTimestamp($line) {
    # Format: [YYYY-MM-DD HH:mm:ss.fff LVL] ...
    if ($line -match '^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}\.\d{3})\s') {
        return [datetime]::ParseExact($Matches[1], 'yyyy-MM-dd HH:mm:ss.fff', $null)
    }
    return $null
}

function Format-AdapterTimestamp($line) {
    if ($line -match '"t":"([^"]+)"') {
        $dt = try { [datetime]::ParseExact($Matches[1], 'yyyy-MM-ddTHH:mm:ss.fffZ', $null) } catch { $null }
        if (-not $dt) { try { [datetime]::ParseExact($Matches[1], 'yyyy-MM-ddTHH:mm:ss.fffK', $null) } catch { $null } }
        return $dt
    }
    return $null
}

function Format-ChatTimestamp($line) {
    if ($line -match '"timestampUtc":"([^"]+)"') {
        return try { [datetime]::ParseExact($Matches[1], 'yyyy-MM-ddTHH:mm:ss.fffffffK', $null) } catch { $null }
    }
    return $null
}

function Format-LlmTimestamp($line) {
    if ($line -match '"timestamp":"([^"]+)"') {
        return try { [datetime]::ParseExact($Matches[1], 'yyyy-MM-ddTHH:mm:ss.fffffffK', $null) } catch { $null }
    }
    return $null
}

# ── Parse -Last / -Since / -Until ─────────────────────────────────────────────

$now = Get-Date
$applyTimeFilter = $false
$startTime = $null
$endTime = $null

if ($Last) {
    $dur = Parse-Last $Last
    $startTime = $now - $dur
    $endTime = $now
    $applyTimeFilter = $true
}
elseif ($Since) {
    $startTime = $Since
    $endTime = if ($Until) { $Until } else { $now }
    $applyTimeFilter = $true
}
elseif ($Until) {
    $startTime = (Get-Date '1970-01-01')
    $endTime = $Until
    $applyTimeFilter = $true
}

# ── Parse -Level ──────────────────────────────────────────────────────────────

$levelFilters = if ($Level) { $Level -split ',' | ForEach-Object { $_.Trim().ToUpperInvariant() } } else { @() }

# ── Default tail ──────────────────────────────────────────────────────────────

if ($Tail -eq -1) {
    $Tail = if ($applyTimeFilter) { 0 } else { 50 }
}

# ── Read and filter ───────────────────────────────────────────────────────────

$totalLines = 0
$matchedLines = @()

# Determine timestamp parser by source
$tsParser = switch ($Source) {
    'agent'   { ${function:Format-SerilogTimestamp} }
    'adapter' { ${function:Format-AdapterTimestamp} }
    'chat'    { ${function:Format-ChatTimestamp} }
    'llm'     { ${function:Format-LlmTimestamp} }
}

# Level field extractor by source
function Test-LevelMatch {
    param($Line, $Levels, $Source)
    if ($Levels.Count -eq 0) { return $true }
    # For agent: level is after timestamp like [2026-06-27 00:00:00.318 DBG]
    if ($Source -eq 'agent') {
        if ($Line -match '^\[\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}\.\d{3}\s+([A-Z]{3,4})\]') {
            return $Matches[1] -in $Levels
        }
        return $false # No level match on this line
    }
    # For JSON sources: "l":"level"
    if ($Line -match '"l":"(debug|info|warn|error)"') {
        return ($Matches[1].ToUpperInvariant() -in $Levels)
    }
    return $true
}

if ($Tail -gt 0 -and -not $applyTimeFilter) {
    # Tail mode: efficient read from end with circular buffer
    $buffer = [System.Collections.Generic.List[string]]::new()
    foreach ($line in (Read-FileLines $filePath)) {
        if ($levelFilters.Count -eq 0 -or (Test-LevelMatch $line $levelFilters $Source)) {
            $buffer.Add($line)
            if ($buffer.Count -gt $Tail) { $buffer.RemoveAt(0) }
        }
    }
    $matchedLines = $buffer
}
else {
    # Full scan with optional time+level filter
    if ($applyTimeFilter -and -not $Quiet) {
        $sinceStr = if ($startTime) { $startTime.ToString('yyyy-MM-dd HH:mm:ss') } else { 'beginning' }
        $untilStr = if ($endTime) { $endTime.ToString('yyyy-MM-dd HH:mm:ss') } else { 'end' }
        Write-Host "Window: $sinceStr → $untilStr | Source: $Source | File: $($fileInfo.Name)" -ForegroundColor Cyan
    }

    foreach ($line in (Read-FileLines $filePath)) {
        $totalLines++

        # Check level filter first (cheapest)
        if ($levelFilters.Count -gt 0 -and -not (Test-LevelMatch $line $levelFilters $Source)) {
            continue
        }

        # Check time filter
        if ($applyTimeFilter) {
            $ts = & $tsParser $line
            if (-not $ts) { continue } # Skip lines without timestamps
            if ($ts -lt $startTime -or $ts -gt $endTime) { continue }
        }

        $matchedLines += $line

        # If in tail+time mode, keep only last N
        if ($Tail -gt 0 -and $matchedLines.Count -gt $Tail) {
            $matchedLines = $matchedLines[($matchedLines.Count - $Tail)..$matchedLines.Count]
        }
    }
}

# ── Output ────────────────────────────────────────────────────────────────────

if ($matchedLines.Count -eq 0) {
    if (-not $Quiet) { Write-Host "No matching log lines found." -ForegroundColor Yellow }
    return
}

if ($Raw) {
    $matchedLines -join "`n"
}
else {
    $truncated = 0
    foreach ($_line in $matchedLines) {
        $line = $_line
        if (-not $NoTruncate -and $line.Length -gt 350) {
            $line = $line.Substring(0, 347) + '...'
            $truncated++
        }
        $line
    }

    if (-not $Quiet) {
        Write-Host "`n($($matchedLines.Count) lines"
        if ($totalLines -gt 0) { Write-Host " from $totalLines total" }
        if ($truncated -gt 0) { Write-Host ", $truncated truncated" }
        Write-Host ")" -ForegroundColor Gray
    }
}
