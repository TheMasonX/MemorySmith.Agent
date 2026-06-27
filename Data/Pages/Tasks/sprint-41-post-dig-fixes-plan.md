# Sprint 41 — Post-Dig-Fixes Issues Plan (2026-06-22)
## Status: IMPLEMENTED ✅ (2026-06-22)

All four fixes implemented, built, and tested (608/608 passing).

## Issue 1: Mining Without Tools (Pickaxe for Cobblestone)

### Root Cause

The `case 'mine':` handler in `MineflayerAdapter/index.js` never calls `bot.equip()` before `bot.dig()`. There is zero tool-selection logic anywhere in the codebase. The bot digs every block bare-handed, which means:

- Stone/cobblestone take ~7.5s per block (instead of ~0.4s with a pickaxe)
- No tool means no enchantment benefits
- The bot never auto-crafts or fetches tools

Mineflayer has built-in `bot.bestHarvestTool(block)` and `bot.equip(item, 'hand')` — they're just never called.

### Evidence

**`MineflayerAdapter/index.js`** — The dig call at line ~697:
```js
await bot.dig(fresh);
```
No preceding `bot.equip()` call. Only `bot.equip()` in the entire file is for block placement (line ~794), not mining.

**`Agent.Tools/Tools/MineBlockTool.cs`** — Just forwards block+count. No tool metadata.

**`Agent.Planning/HtnTaskLibrary.cs`** — `GatherItemDecompose`, `DecomposeCraftItem`, `DecomposeBuild` all emit `MineBlock(block, count)` with zero tool context.

### Fix: Add Tool Selection to Adapter ✅

**Target:** `MineflayerAdapter/index.js`, inside the `case 'mine':` handler, before `await bot.dig(fresh)`.

**Implementation:** Before each `bot.dig(fresh)`, calls `bot.bestHarvestTool(fresh)` to detect the best tool. If found, calls `bot.equip(harvestTool.item, 'hand')`. Failures are caught and logged at debug level — the bot falls back to bare-handed digging if equip fails.

**No C# changes needed** — tool selection is purely an adapter concern. The adapter knows what blocks are being mined and what the bot has in its hotbar.

### Planner: Auto-Craft Missing Tools ✅

**Target:** `Agent.Planning/HtnTaskLibrary.cs`

**Implementation:** Added `EnsureToolsForBlocks()` helper called before MineBlock loops in both `DecomposeBuild` and `GatherItemDecompose`. It:
1. Checks if the required blocks need a pickaxe (`RequiresPickaxeBlocks`) or axe (`RequiresAxeBlocks`)
2. Checks inventory for any tier of that tool (`HasAnyTool`)
3. If missing, emits `CraftItem("wooden_pickaxe", 1)` or `CraftItem("wooden_axe", 1)` before mining
4. The existing crafting chain handles all sub-prerequisites (planks, sticks, crafting table) automatically

---

## Issue 2: `\n` Literal in WebUI.Blazor Adapter Logs

### Status: ALREADY FIXED in source — needs restart

The fix from Sprint 41 handoff (`'\\n'` → `'\n'` at `index.js` line 133) is already applied to the source code:

```js
appendFileSync(`${LOG_DIR}/adapter-${dateStr}.log`, entry + '\n');
```

The old log files at `WebUI.Blazor/logs/adapter-*.log` contain entries written with the old code (literal `\n`). Once the adapter process is restarted, new entries will use real newlines.

**No additional code change needed.** Just restart the Node.js adapter process.

---

## Issue 3: Full LLM Request/Response Logging

### Root Cause

Neither `OllamaProvider.CompleteAsync` nor `LlmChatInterpreter.InterpretAsync` logs the full request body or response. Only truncated snippets are logged on error conditions:

| Location | What's Logged | Full? |
|----------|---------------|-------|
| `OllamaProvider.cs:58` | HTTP error body (truncated to 200 chars) | No |
| `OllamaProvider.cs:71` | Timeout message only | No |
| `OllamaProvider.cs:83` | Exception message only | No |
| `LlmChatInterpreter.cs:114` | Message truncated to 60 chars | No |
| `LlmChatInterpreter.cs:132` | Raw content on parse failure (100 chars) | No |

There is **zero logging** of:
- The full system prompt sent to the LLM
- The full user message sent to the LLM
- The full raw response from the LLM (successful or not)

This means for safety monitoring (kids talking to AI) and debugging, there's no record of what the LLM actually received or said.

### Fix: Add Structured Request/Response Logging

**Target file 1:** `Agent.Planning/Llm/OllamaProvider.cs` — Log full request and response at `LogDebug` level.

**Target file 2:** `Agent.Planning/LlmChatInterpreter.cs` — Log the raw LLM response before parsing, plus the parsed IntentDraft result.

#### `OllamaProvider.CompleteAsync` changes:

```csharp
// Log full request at DEBUG level (system prompt can be large)
logger?.LogDebug("[ollama] request to {Model}: system={SystemPromptLen} chars, user={UserMsgLen} chars",
    options.LlmModel, systemPrompt.Length, userMessage.Length);

// After successful response:
logger?.LogDebug("[ollama] response from {Model}: {ResponseLen} chars\n{Response}",
    options.LlmModel, result.Message.Content.Length, result.Message.Content);
logger?.LogInformation("[ollama] {Model} responded ({ResponseLen} chars) in {ElapsedMs}ms",
    options.LlmModel, result.Message.Content.Length, elapsedMs);
```

#### `LlmChatInterpreter.InterpretAsync` changes:

```csharp
// After getting raw response from LLM
logger?.LogDebug("[llm] raw response from {Provider} ({Model}): {RawContent}",
    provider.ProviderName, options.LlmModel, raw);

// After parsing
if (llmResult != null)
{
    logger?.LogDebug("[llm] parsed intent: {Intent}, item={Item}, blueprint={Blueprint}, "
        + "count={Count}, confidence={Confidence}, response={Response}",
        llmResult.Intent, llmResult.Item, llmResult.Blueprint,
        llmResult.Count, llmResult.Confidence, llmResult.Response);
}
```

#### Rate limiting consideration

The full request/response logging should be at `LogDebug` level only — this means it only shows up when `"Logging:LogLevel:Agent.Planning": "Debug"` is set in `appsettings.json`. The production `LogInformation` lines remain as-is (summary only), so the logs don't flood.

In addition, Serilog in `Program.cs` already supports structured logging — the LLM payloads will be captured as structured properties in the JSON log output, making them searchable.

---

## Issue 4: Build Stall Loop in Creative Mode

### Root Cause

The bot is in creative mode. `AgentBackgroundService.SetGoal` has creative provisioning:

```csharp
if (_worldState.IsCreativeMode && goal is IItemSpecGoal itemGoal)
{
    // Enqueue /give @p <item> <count>
}
```

But **`BuildGoal` is NOT an `IItemSpecGoal`** — it implements plain `IGoal`. The provisioning check at line 240 skips it entirely.

Meanwhile, `HtnTaskLibrary.DecomposeBuild` has a creative path:

```csharp
if (isCreative)
{
    // Creative mode grants the agent the requested materials up front, so skip
    // mining, smelting, and crafting pre-gather actions entirely.
    actions.Add(MakeAction("SearchMemory", ...));
    actions.Add(MakeAction("MoveTo", ...));
    // Directly emits PlaceBlock actions for ALL blocks in blueprint
    // But bot has NONE of these blocks in inventory!
}
```

**Result:** The plan has 218 actions: `[SearchMemory → MoveTo → PlaceBlock x215 → GetStatus]`. No mining, no crafting, no `/give`. Every PlaceBlock fails with "X not in inventory". The governor detects stall (no inventory change), repeats the same plan, and the cycle continues until the user says "stop."

### Fix ✅

**Target file:** `WebUI.Blazor/AgentBackgroundService.cs` — `SetGoal` method (line 237)

**Implementation:** Extended the creative provisioning `if` to include a `BuildGoal` branch. When `goal is BuildGoal buildGoal`, it:
1. Groups all blueprint materials by block type
2. For each material, checks inventory and enqueues `/give @p {block} {need}` if short
3. Enqueues a `GetStatus` to refresh inventory before the planner runs
4. Logs and journals each `/give` via structured logging

This ensures ALL required blueprint materials are granted before the creative PlaceBlock path runs.

---

## Issue 5 (Bonus): Adapter Log Path Consistency

The `LOG_DIR` in `MineflayerAdapter/index.js` defaults to `'./logs'`. When running via `Start-Mineflayer.ps1`, the working directory is `MineflayerAdapter/`, so logs go to `MineflayerAdapter/logs/`. When `LOG_DIR` env var is set to point to `WebUI.Blazor/logs`, they go there.

Check the launch scripts to confirm the `LOG_DIR` env var is consistently set. If not, ensure `Start-Mineflayer.ps1` or the C# process launcher sets it so all logs land in the same directory.

---

## Summary

| # | Issue | Files Changed | Priority |
|---|-------|---------------|----------|
| 1 | Mining without tools | `MineflayerAdapter/index.js` | P1 High |
| 2 | `\n` in logs | None (already fixed, restart needed) | P2 Low |
| 3 | Full LLM logging | `OllamaProvider.cs`, `LlmChatInterpreter.cs` | P1 High (safety) |
| 4 | Build stall in creative | `AgentBackgroundService.cs` | P0 Critical |
