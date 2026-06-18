# Agent Handoff — Sprint 20 Complete

**For:** Next agent session
**From:** Sprint 20 session (2026-06-18)
**Repo:** https://github.com/TheMasonX/MemorySmith.Agent
**Branch:** sprint-5-tool-safety (open PR #1, extended dev branch)
**CI:** GREEN on e38e2ef97f2bf7ec87d854175e9872f8f810a13d
**Council:** APPROVED WITH CONDITIONS (sprint20-council-20260618.md)

---

## What Was Done This Sprint

### Trigger
User observed two live sessions:
- Leo drowned unresponsive in water (health readout showed 20 while drowning)
- `Build:small-house` looped 18-action plan every 2s forever without building
- `Teleported Leo to TheMasonX23]` and `Removed 13 item(s) from player Leo]` reached LLM
- LLM JSON truncated mid-string (llama3.2:3b hitting token limit)
- `GatherItem:sandstone` and `GatherItem:grass` couldn't be created (goal not found)
- After `/clear`, `leo get 5 dirt` instantly completed (stale inventory)

External audit (Gemini+GPT) provided architectural review confirming governance gaps.

### Delivered

**P0-A: Progress-hash governor (DELIVERED)**
- `ReplanGovernor.RecordProgress()` was called on every "progress signal" tool success (including 0ms Wander), preventing STALL from ever triggering
- Fix: removed per-tool RecordProgress call from DispatchActionsAsync
- Added `_cycleInventorySnapshot` field + cycle-settle inventory-delta check: `sum(_worldState.Inventory)` before vs after each plan cycle; calls RecordProgress() only if sum changed
- This correctly identifies "real" game progress (actual blockMined events arrived during 300ms settle) vs "phantom" progress (ACK-only fire-and-forget tools)
- Sprint 20 tests: 7 governor tests confirming stall/recovery/reset behavior

**P0-C: System message filter expansion (DELIVERED)**
Added 3 new patterns to MineflayerAdapter/index.js SYSTEM_MESSAGE_PATTERNS:
- `/^Removed\s+\d+\s+item/i` — covers `/clear` response: "Removed 13 items from player Leo]"
- `/^Cleared\s+\S+/i` — covers `/clear` alt format
- `/^Gave\s+\S+\s+\d+\s+/i` — covers `/give` alt format

**P0-D: LLM truncation recovery (DELIVERED)**
- `OllamaProvider`: added `OllamaOptions` with `num_predict = 300` (from `ChatOptions.LlmMaxResponseTokens`)
- `LlmChatInterpreter`: added `TryParseTruncatedJson` — extracts `addressed` + `intent` from JSON cut off before closing brace; called when `BraceRegex.Match` fails
- Restored Sprint 19 `history?.Record()` call and logger calls (`[llm] calling`, `returned null`, `failed to parse JSON`) that were lost during a prior encoding-corruption cycle

### Deferred

**P0-B: GetStatus freshness gate (DEFERRED → Sprint 21 P0)**
Injecting `GetStatus` on `SetGoal` to refresh stale inventory before `IsComplete` checks was implemented but REVERTED because it broke existing AgentBackgroundServiceTests (3 tests expect planner to be called after exactly 1 cycle; GetStatus injection adds an extra dispatch cycle).
Root cause of original bug: after admin `/clear`, `GenericGatherGoal.IsComplete` reads stale `_worldState.Inventory` and returns true immediately.
Sprint 21 must: write a test for this behavior FIRST, then implement the fix.

**D-2 from council:** Governor recovery log is at `LogDebug` (file-only). Should be elevated to `LogInformation` so console operators can see when stall clears.

**C-1 from council:** `TryParseTruncatedJson` only handles cancel/status/help/clarify. Truncated gather/build intents fall through to Unknown. Future: extract item/blueprint from partial JSON.

---

## Process Lesson (IMPORTANT — New AGENTS.md rule)

Sprint 20 required 13 commits to fix repeated encoding corruption. Root cause:

**When subagents read a C# file containing verbatim strings and re-push it via `github__create_or_update_file`, the agent's JSON encoding converts C# verbatim escape `""` (double-double-quote) to C-style escape `\"`, breaking verbatim string literals.**

Fix: use `mcp__t__ExecuteIntegration` directly with `paramsFile` containing the raw text (NOT via an agent intermediary). Also: files fetched via GitHub API are already base64-encoded; some agents double-encoded them. The working pattern:

```python
import json
with open('myfile.cs', 'r', encoding='utf-8') as f:
    raw_text = f.read()
params = {"owner": "TheMasonX", "repo": "...", "path": "...", "content": raw_text, ...}
with open('/agent/workspace/params.json', 'w') as f:
    json.dump(params, f, ensure_ascii=False)
```
Then call `mcp__t__ExecuteIntegration(action="github__create_or_update_file", paramsFile="/agent/workspace/params.json")`.

**AGENTS.md rule E-1 must be added this session (blocking council finding).**

---

## Sprint 21 Priorities

### P0: GetStatus freshness gate (from P0-B)
Goal: prevent `GenericGatherGoal.IsComplete` from false-completing when inventory is stale.

Approach options:
1. Inject GetStatus as first planner action (not first queued action) — planner always emits GetStatus as step 1
2. Add freshness timestamp to WorldState.Inventory; IsComplete returns false if > 10s stale
3. On SetGoal, clear only the relevant inventory items to -1 (unknown) for the goal's target item

Must write a failing test FIRST that demonstrates the stale-completion bug.

### P1: Swim/water recovery
- Bot drowned while stationary. No recovery behavior exists.
- Need: detect in-water condition; emit swim-up command; resume goal

### P1: NavigateTo playerPos
- "come here" intent creates MoveTo with `target: player` but player coordinates aren't wired
- `playerPos` is available from the chat event via `ExtractPlayerPosition`

### P2: Additional SYSTEM_MESSAGE_PATTERNS
- `^Cleared\s+\S+` is too broad (could match player messages like "Cleared out the chest")
- D-2: governor recovery log at LogDebug → should be LogInformation

### Sprint 21 test-first requirements
- Test: after admin clear, new gather goal does NOT false-complete
- Test: swim recovery triggers when bot position Y < expected surface Y

---

## Architecture Notes

### Governor behavior as of Sprint 20
```
SetGoal → governor.Reset()
DispatchActionsAsync cycle:
  1. planner.PlanAsync → generate plan
  2. governor.Evaluate(fingerprint) — Stalled → skip; Proceed → enqueue
  3. Execute all actions (0ms ACK-only — game runs async)
  4. 300ms settle — blockMined/craftComplete events arrive
  5. currentSum = Inventory.Values.Sum()
  6. if currentSum != _cycleInventorySnapshot → RecordProgress() → reset stagnation counter
  7. _cycleInventorySnapshot = currentSum
  8. (repeat) — if no progress for N identical plans → STALL → stop planning; wait 60s
```

### Known gaps
- Actions dispatch fire-and-forget (ACK-only). Game operations happen async. PlaceBlock/MineBlock may return 0ms OK without any game interaction.
- BlockNotFound → conditional Wander fires only when `WorldState.Facts["event:BlockNotFound:Block"]` is set. Verify this fact is actually being set (Sprint 19 D-2, still open).

---

## Files Changed This Sprint

| File | Change |
|------|--------|
| `WebUI.Blazor/AgentBackgroundService.cs` | Progress-hash governor; `_cycleInventorySnapshot` field + settle check; STALL log enhancement |
| `Agent.Planning/LlmChatInterpreter.cs` | TryParseTruncatedJson; 6-param constructor restored; logger+history calls |
| `Agent.Planning/Llm/OllamaProvider.cs` | OllamaOptions.NumPredict wired to LlmMaxResponseTokens |
| `Agent.Planning/Llm/ChatOptions.cs` | LlmMaxResponseTokens = 300 (default) |
| `MineflayerAdapter/index.js` | 3 new SYSTEM_MESSAGE_PATTERNS |
| `Data/Pages/Guides/sprint20-audit-20260618.md` | Runtime failure audit + Gemini+GPT review (new) |
| `Data/Pages/council/sprint20-council-20260618.md` | Council review (new) |
| `MemorySmith.Agent.Tests/Sprint20Tests.cs` | 3 test classes, 15 tests (new) |

---

## 7 Non-Negotiable Rules (carry forward)

1. TreatWarningsAsErrors = true — all warnings are errors
2. Never call SendEmergencyStop from SetGoal
3. Using directives BEFORE file-scoped namespace in test files (AGENTS.md Rule 3)
4. All timeouts/TTLs/retry counts must be named constants or configurable options
5. Never push C# verbatim string files via agent intermediary (E-1 — NEW)
6. Each sprint: implement → push → CI green → council review → fix blockers → next sprint
7. GitHub MCP: use mcp__t__ExecuteIntegration with paramsFile for file pushes; agents double-encode content
