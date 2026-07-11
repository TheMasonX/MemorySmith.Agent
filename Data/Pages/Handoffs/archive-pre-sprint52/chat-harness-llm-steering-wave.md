# Handoff: Sprint Wave — LLM-Driven Chat Harness Steering

**Date:** 2026-06-27
**Branch:** `sprint-35-llm-first`
**Build:** 742 tests passing, 0 warnings

> **Status (2026-06-27):** P0 + P1 waves implemented. Commits through `2c13ea7`.
> Battle-test fixes applied: inventory false-positive, PlaceBlockGoal hijack, /command filtering.

---

## ✅ Implemented (P0 Wave)

| Task | Files |
|---|---|
| **LLM Prompt Enhancement** | `LlmChatInterpreter.cs` — `nextSteps` schema, compound commands, tool auto-crafting note, memory, parser caveat |
| **TSK-0208 Auto-tool crafting** | `ToolRequirements.cs`, `GatherGoalDecomposer.cs` — pickaxe/axe/shovel mapping, pre-gather tool check |
| **TSK-0203 Cross-session memory** | `AgentBackgroundService.cs` — `IMemoryGateway` injection, `LoadSessionFactsAsync`, `RememberFactAsync` |
| **TSK-0205 Multi-step chaining** | `TaskSequenceGoal.cs`, `IntentManager.ParseCommandString`, `AgentBackgroundService.TryAdvanceSequence` |
| **TSK-0210 Contextual status** | `ChatInterpreter.cs` — enriched status/inventory with goal context, suggestions |

## ✅ Implemented (P1 Wave + Battle-Test Fixes)

| Task | Files |
|---|---|
| **Place intent** | `PlaceBlockGoal.cs`, `PlaceBlockGoalDecomposer.cs`, `IntentManager.cs`, `GoalFactory.cs` |
| **Command execution** | `AgentBackgroundService.cs` — gated by `ChatOptions.CommandExecutionEnabled` (default: false) |
| **Move parsing** | `IntentManager.ParseCommandString` — `move/go/navigate/walk to X Y Z` |
| **Inventory false-positive** | `ChatInterpreter.cs` — length gate ≤40 chars |
| **/command filtering** | `AgentBackgroundService.cs` — skipped for parsing, logged to chat history |
| **PlaceBlockGoal hijack** | Removed `IItemSpecGoal`; decomposer ordered before `GatherGoalDecomposer` |
| **Commands wiki** | `Data/Pages/Guides/minecraft-commands.md` |

---

## 🔜 Next Wave: Entity & Block Awareness

**Problem:** Agent has no entity awareness or block sight. "Are there entities nearby?" → "Only you and me."

### Proposed Tasks

| # | Task | Description |
|---|---|---|
| **TSK-0215** | Entity awareness | Expose `bot.entities`/`bot.players` from Mineflayer as world events |
| **TSK-0216** | Block scan | "Scan for iron nearby" — query surrounding blocks |
| **TSK-0217** | LLM entity/block context | Inject entity list + block scan into `BuildSystemPrompt` |
| **TSK-0214** | Command guide | Wiki at `Data/Pages/Guides/minecraft-commands.md`; inject into KB search |

### Remaining P2

- TSK-0201 Chest deposit/withdraw
- TSK-0202 Waypoints
- LLM-driven error recovery (deferred from P1)

---

## 🧠 Architecture: How LLM Steering Works

```
Player Chat → LlmChatInterpreter
                    │
    ┌───────────────┼───────────────┐
    │               │               │
    v               v               v
Deterministic    LLM Parse      Fallback
Fast-Path        (IntentDraft)  (pattern)
(cancel,help,    → GoalRequest  → clarify
 status,nav)     → GoalFactory
                    │
                    v
             AgentBackgroundService
             → PlanAsync → Dispatch → Observe → Replan
```

**Key insight from the logs:** The LLM path (`LlmChatInterpreter`) already works well — it correctly parsed conversation, status, and clarification intents in the session. The deterministic fast-path is the source of bugs. **This wave leans into the LLM path by giving it more tools, more context, and multi-step capability.**

---

## 🔧 Implementation Details Per Task

### 1. TSK-0205: Multi-Step Task Chaining

**Goal:** A single chat like "gather 5 oak_log then craft 20 planks then build a small house" becomes 3 sequential sub-goals.

**Implementation sketch:**

```csharp
// New model in Agent.Core/Models/
public sealed record TaskSequenceGoal(
    string Name,
    IReadOnlyList<IGoal> Steps,
    int CurrentStep = 0) : IGoal
{
    public bool IsComplete(WorldState state) =>
        CurrentStep >= Steps.Count || Steps[CurrentStep..].All(s => s.IsComplete(state));
}

// In AgentBackgroundService, after PlanAsync completes:
if (_currentGoal is TaskSequenceGoal seq)
{
    if (seq.Steps[seq.CurrentStep].IsComplete(_worldState))
    {
        seq = seq with { CurrentStep = seq.CurrentStep + 1 };
        _currentGoal = seq;
        if (seq.CurrentStep < seq.Steps.Count)
        {
            // Start next step
            _queue.Clear();
            await PlanAsync(seq.Steps[seq.CurrentStep], ct);
        }
    }
}
```

**Key files:**
- `Agent.Core/Models/` — new `TaskSequenceGoal` record
- `WebUI.Blazor/AgentBackgroundService.cs` — sequence progression in main loop
- `Agent.Planning/LlmChatInterpreter.cs` — update `BuildSystemPrompt` to describe compound commands
- `Agent.Planning/IntentManager.cs` — detect compound intent and return sequence

**Edge cases:**
- Failure in step N → stop sequence, report which step failed, offer to skip/retry
- Player sends new command mid-sequence → cancel remaining steps
- Very long sequences → use `ChatMaxResponseLength` (TSK-0199) for progress updates

---

### 2. TSK-0208: Auto-Tool Crafting

**Goal:** When asked to gather a resource, the agent automatically crafts the required tool if missing.

**Tool requirement map (in config or code):**
```
stone, cobblestone, iron_ore, coal_ore, diamond_ore → pickaxe
oak_log, birch_log, spruce_log → axe
dirt, grass_block, sand, gravel → shovel
```

**Implementation sketch:**
```csharp
// In GatherGoalDecomposer.cs Decompose():
var requiredTool = GetRequiredTool(goal.ItemId);
if (requiredTool != null && !HasTool(state.Inventory, requiredTool))
{
    // Insert CraftItem action before MineBlock actions
    actions.Insert(0, new ActionData
    {
        Tool = "CraftItem",
        Arguments = { ["item"] = requiredTool, ["count"] = 1 }
    });
}
```

**Key files:**
- `Agent.Planning/Decomposition/GatherGoalDecomposer.cs` — pre-gather tool check
- `Agent.Core/Models/` — new `ToolRequirement` map (or config section)
- `Agent.Planning/LlmChatInterpreter.cs` — update prompt so LLM knows tool dependencies

**Edge cases:**
- Tool already in inventory → skip crafting
- Insufficient materials for tool → report "need 2 sticks and 3 planks for a wooden pickaxe"
- Tool breaks mid-use → future enhancement (requires durability tracking)

---

### 3. TSK-0203: Cross-Session Memory Recall

**Goal:** The agent persists and recalls facts across sessions via the MemorySmith KB.

**Implementation sketch:**
```csharp
// Agent.Tools/RememberTool.cs
public class RememberTool : ITool
{
    public string Name => "Remember";
    public async Task<ActionOutcome> ExecuteAsync(IMemoryGateway gateway, string key, string value, Position? pos)
    {
        await gateway.CreatePageAsync($"agent-facts/{key}", $"# Agent Fact: {key}\n\n{value}\n\n---\n*Position: {pos}*\n*Timestamp: {DateTime.UtcNow:O}*");
        return ActionOutcome.Succeeded(guid, "Remember", $"Stored '{key}'");
    }
}
```

**Integration with startup:**
```csharp
// In AgentBackgroundService.StartAsync or similar:
var facts = await _memoryGateway.SearchAsync("agent-facts/*", limit: 20);
foreach (var fact in facts)
    _chatHistory?.Record("System", $"Recall: {fact.Title}: {fact.ContentPreview}");
```

**Key files:**
- `Agent.Tools/` — new `RememberTool`, `RecallTool`
- `Agent.Core/Interfaces/` — maybe extend `ITool` or add new interface
- `WebUI.Blazor/AgentBackgroundService.cs` — startup fact loading
- `WebUI.Blazor/Program.cs` — DI registration

**Edge cases:**
- KB unavailable → "I can't remember things right now, the memory system is down"
- Key collision → overwrite with confirmation
- Very old facts → eviction by timestamp (keep last 50)

---

### 4. LLM System Prompt Enhancement

**Goal:** Give the LLM more context so it makes better decisions about intents, tools, and responses.

**Current prompt** (in `LlmChatInterpreter.BuildSystemPrompt`) tells the LLM:
- Bot identity, position, status, inventory
- JSON schema for IntentDraft
- Intent rules (gather, build, craft, smelt, navigate, etc.)

**Add to the prompt:**
```
CRITICAL: The deterministic command parser has bugs. It may misinterpret your
intent. If you see unexpected behavior (e.g., emergency stop when you didn't
ask for one), use intent="conversation" to chat with the player and explain
what happened.

When you receive a compound command like "gather wood then build a house":
- Parse it as the FIRST actionable intent (e.g., "gather")
- The system handles step sequencing internally
- Your job is to identify the first step correctly

Available tools beyond chat: {toolNames}
Use these tools when the player asks about capabilities.
```

**Key files:**
- `Agent.Planning/LlmChatInterpreter.cs` — `BuildSystemPrompt` method

---

### 5. TSK-0210: Contextual Status Reports

**Goal:** Replace raw inventory dumps with intelligent status summaries when the LLM is available.

**Implementation sketch:**
- On "status" intent, if LLM is available, call LLM with enriched context:
  ```
  Current goal: Build:small-house (72% complete — walls done, roof in progress)
  Inventory: oak_planks:70, cobblestone:63, oak_slab:63, ...
  Position: (-236, 66, 173) — at build site
  Health: 20/20, Food: 20/20
  Nearby entities: none
  Recent errors: 0 in last 5 min
  
  Summarize this in 2-3 sentences as the bot's status report.
  ```
- When LLM is unavailable, fall back to current deterministic format

**Key files:**
- `Agent.Planning/ChatInterpreter.cs` — update status/inventory patterns to route through LLM when available
- `Agent.Planning/LlmChatInterpreter.cs` — add status-summary prompt variant

---

## 🧪 Validation Plan

### Per-Task Tests
| Task | Test | Evidence |
|---|---|---|
| TSK-0205 | `TaskSequenceGoal` completes steps in order | Unit test with mock goals |
| TSK-0205 | Failure in step 2 stops sequence at step 2 | Unit test with failing mock goal |
| TSK-0208 | Gather stone with no pickaxe → auto-craft pickaxe first | Integration test with mock inventory |
| TSK-0208 | Gather stone with pickaxe already → skip crafting | Integration test |
| TSK-0203 | Remember fact → restart → recall fact | E2E via MemoryGateway mock |
| TSK-0210 | Status with LLM → contextual response | Unit test with mock LLM |
| TSK-0210 | Status without LLM → deterministic fallback | Unit test |

### Regression Gates
- `dotnet test MemorySmith.Agent.slnx --nologo -v q` → **742 tests pass** (baseline)
- `pwsh Scripts/Test-TaskRecords.ps1 -Quiet` → **pass** (task JSON integrity)
- `dotnet build --nologo -v q` → **0 warnings** (`TreatWarningsAsErrors = true`)

---

## 🚨 Known Risks & Mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| LLM timeout blocks chat for 15s | Medium | TSK-0199 response splitting keeps partial responses flowing |
| MemorySmith KB unavailable on startup | Low | Graceful fallback to empty memory; log warning |
| Task sequence too long for reasonable play | Low | Cap at 5 steps; player can always cancel with "stop" |
| Tool crafting loops if materials insufficient | Low | Check material availability before enqueueing; report gap |
| LLM hallucinates tool/feature that doesn't exist | Medium | `registeredToolNames` in prompt constrains tool awareness |

---

## 📁 Files Likely to Change

| Area | Files |
|---|---|
| New models | `Agent.Core/Models/TaskSequenceGoal.cs`, `Agent.Core/Models/ToolRequirementMap.cs` |
| New tools | `Agent.Tools/RememberTool.cs`, `Agent.Tools/RecallTool.cs` |
| Decomposition | `Agent.Planning/Decomposition/GatherGoalDecomposer.cs` |
| Chat interpreter | `Agent.Planning/LlmChatInterpreter.cs` (prompt), `Agent.Planning/ChatInterpreter.cs` (status path) |
| Agent loop | `WebUI.Blazor/AgentBackgroundService.cs` (sequence progression, startup recall) |
| DI wiring | `WebUI.Blazor/Program.cs` |

---

## 📎 References

- Session log 2026-06-27 (full chat transcript with agent's feature requests)
- `Data/Pages/chat-system.md` — current chat architecture docs
- `Data/Pages/planner.md` — planner architecture
- `Data/Pages/architecture.md` — canonical runtime flow
- `Data/Tasks/tsk-0199.json` — response splitting (already done)
- `Data/Tasks/tsk-0200.json` — regex tightening (already done)
- `Data/Tasks/tsk-0205.json` — multi-step chaining
- `Data/Tasks/tsk-0208.json` — auto-tool crafting
- `Data/Tasks/tsk-0203.json` — cross-session memory
- `Data/Tasks/tsk-0210.json` — contextual status