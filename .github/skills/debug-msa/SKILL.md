---
name: debug-msa
description: 'Debug MemorySmith.Agent by querying rolling log files — agent, adapter, chat, or LLM context — with time windows, level filters, app-instance isolation, and tail mode.'
user-invocable: true
---

# Debug MemorySmith.Agent (debug-msa)

Query the rolling daily log files under `WebUI.Blazor/logs/` to diagnose agent behavior, find errors, isolate app restarts, and inspect LLM interactions.

## When to Use

- Agent is misbehaving, stalling, or crashing and you need the last N log lines
- Need to isolate logs from a **specific app instance** (one process lifetime)
- Searching for errors/warnings across a time window
- Investigating LLM chat interpretation or adapter-level (Mineflayer) issues
- Need to know **how many times** the app restarted and when

## Log Sources

| Source | File Pattern | Format |
|--------|-------------|--------|
| `agent` (default) | `memorysmith-agent-YYYYMMDD.log` | Serilog text: `[ts LVL] Source: [tag] Message {json}` |
| `adapter` | `adapter-YYYY-MM-DD.log` | JSON-NDJSON: `{"t":"ISO","l":"level","c":"category","m":"message",...}` |
| `chat` | `chat/chat-YYYY-MM-DD.log` | JSON-NDJSON chat transcript |
| `llm` | `llm-context/llm-context-YYYY-MM-DD.log` | JSON-NDJSON LLM request/response pairs |

**Log directory:** `WebUI.Blazor/logs/` (relative to repo root)

## Using the Script

The skill includes a PowerShell script: [`scripts/Get-MSA-Logs.ps1`](./scripts/Get-MSA-Logs.ps1)

### Basic Usage

```powershell
# Last 50 lines of agent log (default)
.\scripts\Get-MSA-Logs.ps1

# Last 30 minutes
.\scripts\Get-MSA-Logs.ps1 -Last 30m

# Last 2 hours, warnings and errors only
.\scripts\Get-MSA-Logs.ps1 -Last 2h -Level WRN,ERR

# Specific time window
.\scripts\Get-MSA-Logs.ps1 -Since "2026-06-27T10:00" -Until "2026-06-27T11:00"

# Yesterday's log
.\scripts\Get-MSA-Logs.ps1 -Date 20260626
```

### Finding App Restarts

```powershell
# List all app restarts with timestamps and ConnectionIds
.\scripts\Get-MSA-Logs.ps1 -AppStart

# From a different date
.\scripts\Get-MSA-Logs.ps1 -AppStart -Date 20260626
```

### Isolating a Single App Instance

```powershell
# Find ConnectionIds first
.\scripts\Get-MSA-Logs.ps1 -AppStart

# Then get all logs for one instance (agent log only)
.\scripts\Get-MSA-Logs.ps1 -Run "0HNMKBD67IU7N"

# Limit output
.\scripts\Get-MSA-Logs.ps1 -Run "0HNMKBD67IU7N" -Tail 100
```

### Other Log Sources

```powershell
# Adapter (Mineflayer) logs — last minute, info and above
.\scripts\Get-MSA-Logs.ps1 -Source adapter -Last 1m -Level info,warn,error

# Chat transcript
.\scripts\Get-MSA-Logs.ps1 -Source chat -Last 1h

# LLM request/response pairs
.\scripts\Get-MSA-Logs.ps1 -Source llm -Since "2026-06-27T02:28:00" -Until "2026-06-27T02:29:00"
```

### Output Control

```powershell
# Show full lines (no truncation)
.\scripts\Get-MSA-Logs.ps1 -Tail 10 -NoTruncate

# Raw output (no formatting, pipe-safe)
.\scripts\Get-MSA-Logs.ps1 -Last 1h -Raw | Select-String "error"

# Suppress headers
.\scripts\Get-MSA-Logs.ps1 -Tail 20 -Quiet
```

## Procedure

1. **Start with tail** — `Get-MSA-Logs.ps1 -Tail 50` to see the latest activity
2. **Check errors** — `Get-MSA-Logs.ps1 -Last 5m -Level WRN,ERR`
3. **If app was restarted** — `Get-MSA-Logs.ps1 -AppStart` to find past sessions
4. **Isolate the relevant run** — `Get-MSA-Logs.ps1 -Run "<ConnectionId>"`
5. **Check adapter & LLM** — repeat with `-Source adapter` or `-Source llm`

## Key Facts for Agents

- The **agent log** (`memorysmith-agent-*.log`) can be **7+ MB** per day. Always use `-Last`, `-Tail`, or `-Run` to avoid flooding context.
- A **ConnectionId** (like `0HNMKBD67IU7N`) identifies **one process lifetime** — use `-Run` to isolate it.
- App starts are marked by `=== Agent config:` lines; shutdowns by `[shutdown]` lines.
- The adapter log uses JSON-NDJSON format; agent log uses Serilog text format.
- Agent logs are **locked by the running process** — the script uses shared-read access.
