# Create Debug Tasks in MemorySmith
# Run this after logging in to MemorySmith (http://localhost:6868/login)

$baseUrl = "http://localhost:6868"

# Create Task 1: GatherItem premature completion
$task1 = @{
    title = "GatherItem goal completes prematurely with 0 inventory"
    description = @"
When saying "gather 100 dirt", the goal completes in the same timestamp with 0 dirt in inventory.
Likely cause: IsCreativeMode returning true due to game mode detection failure in Mineflayer.
Diagnostic logging added: game mode, stale flag, and inventory are now logged at completion time.

See Data/Pages/sprint-37-debug-tasks.md for full details.
"@
    type = "Bug"
    status = "Ready"
    priority = "High"
    assigneeMode = "Custom"
    assigneeCustomText = "Copilot"
    reporter = "Copilot"
    labels = @("gather", "goal-completion", "debugging")
}

# Create Task 2: findFlatArea tower issue
$task2 = @{
    title = "findFlatArea builds on tower instead of nearby ground"
    description = @"
Bot runs to a distant tower structure and builds on its roof instead of finding nearby flat ground.
Fix: proximity weighting, reduced minArea on retry, scanOrigin parameter support.
Requires Mineflayer adapter restart to pick up JS changes.

See Data/Pages/sprint-37-debug-tasks.md for full details.
"@
    type = "Bug"
    status = "Ready"
    priority = "High"
    assigneeMode = "Custom"
    assigneeCustomText = "Copilot"
    reporter = "Copilot"
    labels = @("build", "findFlatArea", "scanner")
}

# Create Task 3: Chat response logging
$task3 = @{
    title = "Chat responses not visible in console logs"
    description = @"
Bot chat responses (e.g. "Gathering 100x dirt.") are sent in-game but not logged to the C# console.
Fix: added logging for CreateGoal and NavigateTo intents.

See Data/Pages/sprint-37-debug-tasks.md for full details.
"@
    type = "Bug"
    status = "Ready"
    priority = "Low"
    assigneeMode = "Custom"
    assigneeCustomText = "Copilot"
    reporter = "Copilot"
    labels = @("chat", "logging")
}

$tasks = @($task1, $task2, $task3)

foreach ($task in $tasks) {
    $json = $task | ConvertTo-Json -Depth 3
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/tasks" `
            -Method Post `
            -ContentType "application/json" `
            -Body $json `
            -ErrorAction Stop
        Write-Host "Created: $($response.key) - $($response.title)" -ForegroundColor Green
    } catch {
        if ($_.Exception.Response.StatusCode -eq 401) {
            Write-Host "Not authenticated. Please log in at $baseUrl/login first." -ForegroundColor Yellow
            Write-Host "Task JSON was:" -ForegroundColor Cyan
            Write-Host $json -ForegroundColor Gray
            break
        } else {
            Write-Host "Failed to create task: $_" -ForegroundColor Red
        }
    }
}
