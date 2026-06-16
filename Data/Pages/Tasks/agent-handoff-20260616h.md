# Agent Handoff — MemorySmith.Agent

**For:** Next agent session
**From:** Session 2026-06-16 (eighth session — CI hotfixes + Sprint 1 reliability)
**Repo:** https://github.com/TheMasonX/MemorySmith.Agent
**CI:** GREEN on `b0924ea` (conclusion: success, 3 pre-existing warnings only)

---

## What Was Done This Session

### Phase 1: CI Hotfixes (5 pre-existing TSK-0014 bugs)

The TSK-0014 refactor from the previous session had introduced 5 compile/test bugs that
were masked by the first compile error. Fixed one at a time as CI revealed each:

| Bug | Fix |
|-----|-----|
| `LlmChatInterpreter.cs` — `$"""` raw string with `{{` JSON braces | `$$"""` + `{{var}}` |
| `LlmChatInterpreter.cs` — `Position?.HasValue` on reference type | `is not null` + direct use |
| `ChatOptions.cs` — `sealed class` blocked `with {}` in tests | `sealed record` |
| `AgentBackgroundServiceTests.cs` — `CreateService(int)` passed int as `GoalFactory?` | named arg |
| `ChatInterpreter.cs` — `GatherRegex`/`BuildRegex` `?` filler groups missed "me" | `*` + "me" added |

Baseline CI green: commit `1840ec0` (ChatInterpreter filler fix).

### Phase 2: Sprint 1 — Reliability

**Sprint 1a: Non-blocking LLM** (`WebUI.Blazor/AgentBackgroundService.cs`)
- Added `Channel<WorldEvent> _chatChannel` (unbounded, SingleReader/Writer)
- `ProcessEventsAsync` `case "chat":` now calls `_chatChannel.Writer.TryWrite(worldEvent)`
- New `ChatConsumerAsync` task reads from channel and calls `HandleChatEventAsync`
- Result: 5–10s LLM call no longer blocks health/death/blockMined processing

**Sprint 1b: Reconnect with exponential backoff** (`WebUI.Blazor/AgentBackgroundService.cs`)
- `ExecuteAsync` has a retry loop: `for (attempt = 0; attempt <= _reconnectDelays.Length; attempt++)`
- Per-connection `CancellationTokenSource` linked to `stoppingToken`
- `MonitorAndCancelOnFaultAsync` wrapper cancels linked CTS if `ProcessEventsAsync` faults
- Default delays: 2s/4s/8s/16s/32s; configurable via `reconnectDelays: TimeSpan[]?` constructor param
- Goals and WorldState survive reconnects (instance fields, not reset on retry)

**New tests:**
- `SlowChatInterpreter_DoesNotBlock_BlockMinedEventProcessing` — verifies Sprint 1a
- `Reconnect_AfterTwoFailures_ResumesCurrentGoal` — verifies Sprint 1b
- `FailingWorldAdapter` helper: throws on first N `ConnectAsync` calls

**Council review:** `Data/Pages/council/sprint1-impl-council-20260616.md`
**CI green:** commit `b0924ea` (conclusion: success)

---

## Current Architecture (quick ref)

```
AgentBackgroundService (WebUI.Blazor/)
  ExecuteAsync:    retry loop (max 6 attempts, 2/4/8/16/32s delays)
  ProcessEventsAsync:  event loop → _chatChannel.Writer.TryWrite for "chat" events
  ChatConsumerAsync:   reads _chatChannel → HandleChatEventAsync (LLM may take 5-10s)
  DispatchActionsAsync: action queue + planner loop
  MonitorAndCancelOnFaultAsync: static helper — cancels connectionCts on ProcessEvents fault

AgentBackgroundService constructor (new in Sprint 1):
  + reconnectDelays: TimeSpan[]? (null = default 2/4/8/16/32s; pass [] for no retries in tests)
```

---

## Next Sprint: Sprint 2 — End-to-End Build

**Sprint 2 backlog (priority order):**

### 2a — CraftItemTool pathfinding (HIGH)
Current: `craft` action fails silently if no crafting table within 4 blocks.
Fix in `MineflayerAdapter/index.js`:
```js
// In the craft handler, before bot.craft():
const craftingTable = bot.findBlock({ matching: mcData.blocksByName.crafting_table.id, maxDistance: 8 });
if (craftingTable) await bot.pathfinder.goto(new goals.GoalGetToBlock(craftingTable.position.x, craftingTable.position.y, craftingTable.position.z));
```
Tests: mock world with crafting table at distance 6; verify craft succeeds.

### 2b — HtnTaskLibrary.DecomposeBuild crafting chain (HIGH)
Current: `DecomposeBuild` only mines directly-mineable blocks. Crafted items must be pre-stocked.
Fix in `Agent.Planning/HtnTaskLibrary.cs`:
After log gathering, emit CraftItem actions: planks → crafting_table → slabs → door → chest → sticks+torches.
Add `coal_ore` to `DirectMineBlocks` for torches.
Check inventory first (skip if already have item).

### 2c — IItemRegistry TTL cache (HIGH)
Current: every `GetAsync("oak_log")` is a fresh HTTP request.
Fix in `Agent.Memory/MemorySmithItemRegistry.cs`:
```csharp
private readonly ConcurrentDictionary<string, (ItemSpec? Spec, DateTimeOffset Expires)> _cache = new();
private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
```
Tests: verify cache hit returns same object; expired entry re-fetches.

---

## Process Reminders

- Each sprint: implement → dotnet build → push → CI green → council review → fix blockers
- Council review: 6 seats, written to `Data/Pages/council/tsk-N-topic-council-YYYYMMDD.md`
- GitHub MCP: `github__create_or_update_file` per-file with blob SHA. Plain text content.
- Test files: `using` directives MUST be BEFORE `namespace` declaration (file-scoped namespace rule)
- No fully-qualified type names inside `MemorySmith.Agent.Tests` namespace — use short names from `using` 
- `reconnectDelays: []` in tests for instant retry (no sleep)

---

## File State (Sprint 1 end)

| File | Notes |
|------|-------|
| `Agent.Planning/LlmChatInterpreter.cs` | Fixed: `$$"""` raw string, `is not null` for Position? |
| `Agent.Planning/Llm/ChatOptions.cs` | Changed to `sealed record` for `with` expression support |
| `Agent.Planning/ChatInterpreter.cs` | Fixed: `GatherRegex`/`BuildRegex` filler words with `*` |
| `WebUI.Blazor/AgentBackgroundService.cs` | Sprint 1: `_chatChannel` + reconnect loop |
| `MemorySmith.Agent.Tests/AgentBackgroundServiceTests.cs` | Sprint 1 tests + named arg fix |

---

## Key Memories to Load

Search knowledge base for "MemorySmith.Agent" for:
- Phase tracking (Phases 0-5b + Sprint 1 complete)
- CI green baseline and test architecture
- GitHub MCP limitations (no push_files, no workflow scope, plain text not base64)
- NuGet proxy fix recipe (authproxy.py)
- LLM chat pipeline (ChatOptions, ILlmProvider, LlmProviderFactory)
- Council review format (6 seats, blocking vs deferred)
- `using` BEFORE namespace in test files (file-scoped namespace rule)
