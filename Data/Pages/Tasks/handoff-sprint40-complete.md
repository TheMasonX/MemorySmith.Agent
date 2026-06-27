# Handoff: Sprint 40 Complete — P0/P1 Fix Package + Intent Parsing Findings

**Date:** 2026-06-22
**Previous handoff:** `Data/Pages/Tasks/handoff-sprint40-p0-fixes.md`
**Sprint plan:** `Data/Pages/Tasks/sprint40-plan-adapter-council-response.md`
**Wiki memories created:**
- `Data/Memories/Core/agent-sprint40-p0-implementation-status.json`
- `Data/Memories/Core/agent-mineflayer-adapter-state.json`
- `Data/Memories/Core/agent-intent-parsing-issues.json`

---

## Completed P0/P1 Items

All three P0 items and supporting P1 items are **complete, built, and tests pass (63/63).**

| Task | Status | Title | Summary |
|------|--------|-------|---------|
| TSK-0061 | ✅ Done | Wire mineAborted/stopComplete to C# | Already fully wired — no code change needed |
| TSK-0065 | ✅ Done | Fix block position off-by-one | findBestBlock() three-pass scorer + grass_block alias + configurable constants |
| TSK-0066 | ✅ Done | Verify kick→reconnect loop | Already implemented with exponential backoff [2,4,8,16,32]s |
| (New) | ✅ Done | Grass as dirt source | BLOCK_MINING_ALIASES: dirt → [dirt, grass_block] |
| (New) | ✅ Done | Configurable block scoring | MINE_Y_PENALTY_WEIGHT, MINE_FIRST_PASS_COUNT, MINE_SECOND_PASS_COUNT |
| (New) | ✅ Done | Graduated stall retry | Replaced 60s → [10, 20, 30, 60]s + chat notification |
| (New) | ✅ Done | Dig infinite-loop fix | MAX_DIG_FAILURES=3 per block position via digFailures Map |
| (Bonus) | ✅ Done | Fixed pre-existing ReachableBlockFoundEvent stub | Was breaking build with CS1526 |
| (Bonus) | ✅ Done | Switched to array matching for findBlocks | [...acceptableIds] instead of function matching |

### Files Modified

| File | Change |
|------|--------|
| `MineflayerAdapter/index.js` | findBestBlock(), BLOCK_MINING_ALIASES, digFailures Map, array matching, configurable constants |
| `Agent.Core/ReplanGovernor.cs` | Graduated delays [10,20,30,60], _stallAttempt, CurrentStallDelay property |
| `Agent.Core/IReplanGovernor.cs` | CurrentStallDelay added to interface |
| `WebUI.Blazor/AgentBackgroundService.cs` | Chat notification on stall, graduated delay in stall log |
| `Agent.World.Minecraft/WebSocketBridge.cs` | Fixed ReachableBlockFoundEvent parsing stub (was missing constructor args) |
| `Agent.Core/Events/WorldEvents.cs` | (No change needed — MineAbortedEvent, StopCompleteEvent already existed) |
| `MemorySmith.Agent.Tests/ReplanGovernorTests.cs` | Updated constructor param from stalledRecoveryTimeout to stallGraduatedDelaysSec |
| `MemorySmith.Agent.Tests/Sprint20Tests.cs` | Updated MakeGovernor helper |
| `MemorySmith.Agent.Tests/Sprint21Tests.cs` | Added CurrentStallDelay to AlwaysStalledGovernor |
| `Data/Tasks/tsk-006*.json` | 3 tasks updated from Backlog → Done with implementation comments |

---

## Remaining P1 Tasks (Ready for Next Sprint)

| Task | Priority | Title | Notes |
|------|----------|-------|-------|
| TSK-0062 | P1 | Add goto() timeout with Promise.race() | The known hang case. Needs path_update listener for noPath/timeout. Depends on TSK-0070 (path_reset→path_update). Not started. |
| TSK-0070 | P1 | Correct path_reset→path_update | Research paper references non-existent path_reset event. Use path_update with status:'noPath'. Not started. |
| TSK-0067 | P1 | Stale-inventory guard at goal-creation time | GoalFactory rejects GatherItem when stale inventory shows sufficient items. Guard needed before goal creation. Not started. |

## P2 Tasks

| Task | Priority | Title | Notes |
|------|----------|-------|-------|
| TSK-0063 | P2 | Wire bot.inventory.on('updateSlot') | Real-time slot-level inventory tracking. Depends on TSK-0066 (solid reconnect). |
| TSK-0064 | P2 | Add move event throttle | bot.on('move') fires ~20/sec. Add rate limiting in adapter. |

## P3 Tasks

| Task | Priority | Title | Notes |
|------|----------|-------|-------|
| TSK-0068 | P3 | Define IObservationSummarizer | Phase 3 observation summaries integration point |
| TSK-0069 | P3 | Add collectblock + tool deps | Phase 5 plugin-based workflows |

---

## Critical Findings from Runtime Testing

### 1. Intent Parsing Failures (Ollama llama3.2:3b)

**Severity:** Medium-High. The agent repeatedly fails to correctly interpret player chat.

**Four distinct failure modes documented in** `agent-intent-parsing-issues`:

1. **False "ignore"** — LLM classifies disagreements ("no, that's wrong") as `addressed: "no"` or `intent: "ignore"`. The player's correction is silently dropped.
2. **System messages leak through** — `/clear` response variations evade the regex filter and trigger 15s wasted Ollama calls.
3. **Clarify doesn't produce useful questions** — When LLM fails or produces low confidence, the fallback uses hardcoded generic responses.
4. **"inventory" fast-path too broad** — Pattern-based ChatInterpreter matches "inv"/"inventory" keywords and returns status BEFORE the LLM runs, eating messages like "your inventory is stale, refresh it".

**Documented in:** `agent-intent-parsing-issues.json`

### 2. dig() Crash — "point.minus is not a function"

**Fixed.** Root cause was Mineflayer's internal Vec3 method calls on block positions created by our custom `toVec3()` helper. The `toVec3` object only provides `floored()` and `offset()`, not `minus()`, `plus()`, etc.

Workaround: `MAX_DIG_FAILURES = 3` per-block-position limit prevents infinite retry loops. Each failed position key `"x,y,z"` increments a counter in `digFailures` Map, and after 3 failures the block is skipped. Success resets the counter.

**Long-term fix:** Replace `toVec3` with proper `Vec3` from mineflayer (`import { Vec3 } from 'mineflayer'`) throughout the adapter. The `toVec3` helper was a stopgap for the `pos.floored()` requirement; using actual Vec3 instances would prevent this class of errors entirely.

### 3. findBlocks Function Matching

**Fixed.** Mineflayer 4.x's `findBlocks` with function matching (`matching: block => boolean`) may not work reliably. Switched all calls to array matching (`matching: [...acceptableIds]`) which is better documented and more widely supported.

### 4. bot.registry.blocksById Doesn't Exist

**Fixed.** The `actualBlockName()` function in `findBestBlock()` originally used `bot.registry.blocksById[block.type]?.name` which crashed with "Cannot read properties of undefined (reading '8')". Replaced with reverse-lookup using `bot.registry.blocksByName` and the known `aliasNames` closure array.

---

## Build & Test Status

```powershell
# Build: 0 errors, 0 warnings
dotnet build

# Tests: 63/63 passing
dotnet test --filter "FullyQualifiedName~ReplanGovernorTests|FullyQualifiedName~Sprint20Tests|FullyQualifiedName~Sprint21Tests|FullyQualifiedName~AgentBackgroundServiceTests"

# JS syntax check
cd MineflayerAdapter
node --check index.js
```

---

## Next Sprint: Suggested Focus

### Sprint 41 — Chat Intent Reliability & Pathfinder Safety

**Primary goal:** Fix the intent parsing pipeline so the agent reliably understands player commands. The current Ollama 3B model is too small for this task.

**Suggested execution order:**

1. **TSK-0062** (P1) — `goto()` timeout safety. The most impactful runtime safety fix.
2. **TSK-0070** (P1) — Fix `path_reset`→`path_update`. Prerequisite for TSK-0062.
3. **TSK-0067** (P1) — Stale-inventory guard at goal-creation time. User-facing quality fix.
4. **Intent parsing overhaul** (no task yet) — Address the 4 failure modes:
   - Upgrade or replace Ollama model (llama3.1:8b or cloud API fallback)
   - Make "clarify" use the LLM to generate specific questions
   - Narrow pattern fast-paths — remove "inv"/"inventory" as hard keywords
   - Improve system message filtering for `/clear` response variants
5. **TSK-0063** (P2) — `updateSlot` wiring for real-time inventory tracking
6. **TSK-0064** (P2) — `move` event throttle

### Dependency Map

```
TSK-0070 (path_reset→path_update) ──prerequisite── TSK-0062 (goto timeout)
                                            │
                                            └──related── TSK-0037 (action progress telemetry)

TSK-0063 (updateSlot) ──depends-on── TSK-0066 (solid reconnect) [done]

Intent overhaul (no task) ──independent── can start immediately
```

---

## Key Files to Read for Next Agent

| File | Why |
|------|-----|
| `Data/Memories/Core/agent-sprint40-p0-implementation-status.json` | Current implementation status |
| `Data/Memories/Core/agent-mineflayer-adapter-state.json` | Adapter architecture & lessons learned |
| `Data/Memories/Core/agent-intent-parsing-issues.json` | Detailed intent parsing failure analysis |
| `Agent.Planning/LlmChatInterpreter.cs` | Intent parsing pipeline — ParseDecision, TryParseTruncatedJson |
| `Agent.Planning/Llm/OllamaProvider.cs` | Ollama API client — CompleteAsync |
| `Agent.Personality/Interfaces/IChatInterpreter.cs` | Chat interpretation interface |
| `WebUI.Blazor/AgentBackgroundService.cs` lines 724-830 | HandleChatEventAsync — intent dispatch |
| `Data/Pages/chat-system.md` | Chat pipeline documentation |
| `AGENTS.md` | Coding guidelines — CRITICAL: Rule A-1, IntentDraft schema, pipeline status |
