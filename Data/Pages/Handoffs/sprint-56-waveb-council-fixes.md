# Sprint 56 Wave B — Council-Driven Immediate Fixes

**Date:** 2026-06-30 | **Branch:** `dev/round-3` | **Baseline:** After Sprint 56 Wave A (TSK-0260–0273)

## Summary

This handoff captures the Sprint 56+ immediate fixes from the **10-seat council review of 4 external audit documents** (filed at `Data/Pages/council/2026-06-30-external-audits-council.md`). The council found ~40 audit findings clustering into 7 themes. This wave covers the **7 bounded, high-confidence, implementation-ready fixes** — gated by TaskSequenceGoal.IsComplete verification.

**Wave B goal:** Stabilize the runtime by fixing security gaps, concrete bugs, and test debt identified by the council. No architectural extraction — all fixes are local and independently scoped.

---

## Prerequisite: Verify TaskSequenceGoal.IsComplete

| Attribute | Value |
|-----------|-------|
| **Task** | TSK-0274 (Critical, InProgress) |
| **Claim** | `TaskSequenceGoal.IsComplete()` may never delegate to the current step's `IsComplete`. Completion logic is split across `TryAdvance()` (which increments step index) and `IsComplete()` (which only checks `_currentStep >= _steps.Count`). If advancement only happens via external callers and `IsComplete` never checks the actual step, no multi-step sequence can ever be recognized as complete by the dispatch loop. |
| **Evidence location** | `Agent.Core/Models/TaskSequenceGoal.cs` lines 64-103 |
| **Action** | 1. Read `TaskSequenceGoal.cs` — read full file<br>2. Read the dispatch loop in `AgentBackgroundService.cs` around lines 1600-1610 (`if (_currentGoal.IsComplete(_worldState))`)<br>3. Trace the state machine: does `TryAdvance()` ever cause `IsComplete()` to return `true`?<br>4. If confirmed P0: fix `IsComplete()` to delegate to `_steps[_currentStep].IsComplete(state)` OR ensure the dispatch loop has explicit sequence advancement path<br>5. Add NUnit tests: 1-step seq, 3-step seq, max-step seq, `TryAdvance` at last step, `HasFailed` propagation |
| **Gates** | This blocks the refactoring roadmap. Verification document must be posted to `Data/Pages/council/`. If confirmed P0, fix must ship with tests before any Sprint 59+ extraction begins. |

---

## Fix 1: /give Command Injection — Sanitize Block Names

| Attribute | Value |
|-----------|-------|
| **Task** | TSK-0275 (Critical, Ready) |
| **Severity** | P0 — Security |
| **Discovery** | Seat 7 (Security & Safety Auditor) — only seat to flag this. No prior audit or review considered injection risk. |

### Problem
`ProvisionGoalIfCreativeAsync` in `AgentBackgroundService.cs` (line ~422) builds:
```csharp
var giveCmd = $"/give @p {block} {need}";
```
`block` comes from `Blueprint.Materials[].Block` — a string parsed from wiki page frontmatter (`BlueprintParser.cs` lines 85-92) with **NO validation**. Blueprint pages can be created/modified via `CreatePageTool` (MemorySmith wiki), which accepts user/LLM-provided content.

The adapter's `creativeProvider.js` also falls back to `/give` via `bot.chat()` (strategy 2, line ~109) — same vector.

### Files to modify
- `WebUI.Blazor/AgentBackgroundService.cs` — `ProvisionGoalIfCreativeAsync` method (~lines 401-450)
- `MineflayerAdapter/creativeProvider.js` — `/give` fallback path
- `Agent.Core/` — optionally create a shared command validation utility

### Fix requirements
1. **Add an allowlist of valid Minecraft item IDs** before `/give` dispatch. Reject unknown block names. Use the known set from `CommonMinecraftBlocks.cs` or a curated subset.
2. **Sanitize `block` to alphanumeric + underscores only.** Reject spaces, semicolons, shell metacharacters, or anything matching `[^a-zA-Z0-9_]`.
3. **Move creative provisioning to adapter's `creative.setInventorySlot()` exclusively** where possible — never fall back to `/give` chat on servers without OP.
4. **Add a `CommandExecutionEnabled` check** in the `"command"` intent handler of `HandleChatEventAsync` (ABS.cs ~line 1156-1171). Currently passes through with only `StartsWith('/')`.
5. **Unit test:** Verify sanitization rejects `"cobblestone;give @p command_block"`, `"../etc"`, etc.

### Validation
- Cross-site/LLM-injection test: craft a blueprint with material `block="diamond_block;op SteveBot"` — verify `/give @p diamond_block;op SteveBot 64` is rejected before dispatch
- Normal creative build in creative mode still works

---

## Fix 2: Chat Command Deny List

| Attribute | Value |
|-----------|-------|
| **Task** | TSK-0277 (Critical, Ready) |
| **Severity** | P0 — Security |
| **Discovery** | Seat 7 — no command restriction exists beyond `StartsWith('/')` |

### Problem
The `"command"` intent path dispatches LLM-generated commands with only a `StartsWith('/')` check. The LLM is explicitly told (ABS.cs line ~370): *"You CAN execute Minecraft server commands (/give, /tp, /setblock, /time, etc.)"* and *"use intent=\"command\" with item=\"/command args\"".*

A hallucinated or adversarial LLM response with `intent="command"` and `item="/op <player>"` or `item="/kill @e"` would be dispatched immediately with no human-in-the-loop check. On servers with OP, `/op` could escalate privileges permanently.

### Files to modify
- `WebUI.Blazor/AgentBackgroundService.cs` — `HandleChatEventAsync` command intent case (~lines 1156-1171)
- `Agent.Core/` — optionally add a `CommandSafety` utility class
- `MineflayerAdapter/index.js` — optionally add adapter-side guard

### Fix requirements
1. **Define a deny list constant** of prohibited server commands:
   ```
   /op, /deop, /kill, /ban, /pardon, /stop, /save-off, /save-on,
   /reload, /publish, /whitelist, /debug, /difficulty, /gamerule,
   /setworldspawn, /setblock, /fill, /clone, /summon, /give (if not creative),
   /gamemode (for other players)
   ```
2. **Block denied commands even when `CommandExecutionEnabled` is true** — this is a safety layer, not a feature toggle.
3. **Log every dispatched command at Warning level** with full context: goal name, intent, raw command, timestamp, `_currentGoal?.Name`.
4. **Add a separate `AllowDestructiveCommands` toggle** (default: false) for commands that modify server state beyond item provisioning. When false, the deny list above is always enforced.
5. **Unit test:** Verify each denied command is rejected. Verify allowed commands (`/time set day`, `/tp @p 0 64 0`) pass through.

### Validation
- Say "kill all entities" or "op me" — verify the bot logs a warning and does not dispatch the command
- Say "set time to day" — verify the command is dispatched normally

---

## Fix 3: BlockNotFound Retry Counter Type Mismatch

| Attribute | Value |
|-----------|-------|
| **Task** | TSK-0276 (High, Ready) |
| **Severity** | P1 — Bug (highest-ROI fix in entire audit corpus) |
| **ROI** | ~5 lines changed, prevents infinite retry loops on missing blocks |

### Problem
`TryRouteAsError` in `AgentBackgroundService.cs` writes the BlockNotFound retry count as a **string** (`(prevCount + 1).ToString()`) but `HtnTaskLibrary` reads it as an **integer**. The counter never accumulates past `1`, so the bot never widens its search radius from `40→80→120` blocks as intended.

### Files to modify
- `WebUI.Blazor/AgentBackgroundService.cs` — `TryRouteAsError` method, locate the Facts write that stores the retry count

### Fix
Change the write path to store as integer. Search for `TryRouteAsError` and find the line that writes to Facts — likely something like:
```csharp
// Current (wrong):
SetFact($"retry:{blockName}", (prevCount + 1).ToString());

// Fixed:
SetFact($"retry:{blockName}", prevCount + 1);
```

### Validation
- Unit test: verify that after `MaxRetries` BlockNotFound errors, the action produces `ActionOutcome.Failed` with reason `BlockNotFound` (not infinite loop)
- The retry counter should be able to reach at least 3 before termination

---

## Fix 4: LLM Parse Failure — Treat as Signal, Not Silence

| Attribute | Value |
|-----------|-------|
| **Task** | TSK-0278 (High, Ready) |
| **Severity** | P1 — Bug / Reliability |

### Problem
Three silent-failure layers conspire to suppress replanning when the LLM returns malformed output:

1. **`ParseEvaluationResult`** (`LlmEvaluatorImpl.cs` lines 274-293): `try/catch` all → returns `EvaluationResult(false, "unparseable response")` — i.e., "no replan." No structured logging for the specific error mode.
2. **`ExtractJson`** (`LlmEvaluatorImpl.cs` lines 296-300): When no JSON brackets found, returns `"{}"` → parsed as `shouldReplan = false`. No log at all.
3. **Outer try/catch** in `EvaluateAsync` (~line 105): Catches all exceptions, logs generic `LogWarning` — no distinction between invalid JSON, timeout, low confidence, or truncation recovery.

### Files to modify
- `Agent.Planning/LlmEvaluatorImpl.cs` — `ParseEvaluationResult`, `ExtractJson`
- `WebUI.Blazor/AgentBackgroundService.cs` — caller at ~lines 1935-1948

### Fix requirements
1. **Add structured logging** distinguishing failure modes: `ParseFailure`, `Timeout`, `LowConfidence`, `TruncationRecovery`
2. **Add response length caps** — reject responses over a threshold (e.g., 2000 chars for evaluation)
3. **Log parse failures at Warning level** — currently silent `catch` blocks with no structured event
4. **Return structured `EvaluationResult`** with `IsSuccess=false` and specific `FailureReason` — callers should use this signal rather than treating "no replan" as the default
5. **Update AC-5 in council report** will verify this

### Validation
- Unit test: `ParseEvaluationResult("sure, sounds good")` → returns `IsSuccess=false, FailureReason=ParseFailure`
- Unit test: `ParseEvaluationResult("")` → returns same
- Unit test: `ParseEvaluationResult('{"replan": true}')` → returns `IsSuccess=true, ShouldReplan=true`
- Integration test: mock LLM returns garbage, verify agent does not silently continue a broken plan

---

## Fix 5: Make ParseEvaluationResult + ExtractJson Internal + Test

| Attribute | Value |
|-----------|-------|
| **Task** | Part of TSK-0278 |
| **Severity** | P1 — Testing |

### Problem
`ParseEvaluationResult` and `ExtractJson` in `LlmEvaluatorImpl.cs` are `private static` methods. They are pure functions (string → `EvaluationResult`) with zero test coverage. These are critical parsing utilities used by the replanning path.

### Files to modify
- `Agent.Planning/LlmEvaluatorImpl.cs` — change accessibility
- `Agent.Planning/` — potentially add `InternalsVisibleTo` to `.csproj` or use `AssemblyInfo.cs`
- `MemorySmith.Agent.Tests/` — add `LlmEvaluatorImplTests.cs` or extend existing test file

### Fix requirements
1. **Make `ParseEvaluationResult` and `ExtractJson` `internal`** (and `static` remains). Add `[assembly: InternalsVisibleTo("MemorySmith.Agent.Tests")]` if not already present.
2. **Add NUnit tests covering:**
   - Valid JSON with `replan: true` 
   - Valid JSON with `replan: false`
   - Truncated JSON (`{ "replan": true` — no closing brace)
   - Prose-only ("sure, I'll replan")
   - Empty string
   - Malformed braces (`{replan: true` — no quotes, no closing)
   - Extra properties (`{"replan": true, "extra": "data", "nested": {"a": 1}}`)
   - Text before/after JSON ("Here is my response: {\"replan\": true}")
3. **Verify each test produces the expected `EvaluationResult`** — especially that parse failures return `IsSuccess=false, FailureReason=ParseFailure` (not silent "no replan")

---

## Fix 6: Delete Dead chatFilter.js

| Attribute | Value |
|-----------|-------|
| **Task** | TSK-0279 (High, Ready) |
| **Severity** | Cleanup |

### Problem
`MineflaterAdapter/chatFilter.js` was extracted from `index.js` during Sprint 52 modularization (TSK-0166) but was **NEVER wired** — `index.js` does not import it. Confirmed by grep: zero references to `registerChatFilter` or any export from `chatFilter.js` in `index.js`.

The file exists as dead code with a duplicated copy of `SYSTEM_MESSAGE_PATTERNS` (also defined inline in `index.js` lines 559-573). If someone later modifies `chatFilter.js` thinking it's active, patterns can diverge.

### Files to modify
- `MineflayerAdapter/chatFilter.js` — DELETE the file
- `MineflayerAdapter/index.js` — verify imports (lines 30-39) do NOT reference chatFilter.js (they don't currently)
- Optional: `MineflayerAdapter/` — check for any other references

### Fix
1. Delete `MineflayerAdapter/chatFilter.js`
2. Run `git grep -i "chatFilter"` to confirm zero remaining references
3. Verify `index.js` `SYSTEM_MESSAGE_PATTERNS` and `isSystemMessage()` cover the required patterns (cross-check against `chatFilter.js` before deletion)
4. Optionally add JS unit tests for `isSystemMessage()` covering all 12+ patterns

### Validation
- `git grep chatFilter` returns zero results
- `node index.js` module loads without errors (no missing import)
- Chat filtering still works: server messages (`[Server]`, teleport confirmations) are filtered, player chat is forwarded

---

## Supplementary: Task Record Correction (TSK-0190)

While not a separate code fix, note that **TSK-0190 claims `/give` provisioning was removed from `SetGoal`** but the live code at `ProvisionGoalIfCreativeAsync` still has it (with comment "Sprint 52: Re-enabled /give as a secondary provisioning path"). After the Sprint 56 security fixes change the `/give` behavior, update TSK-0190 to reflect the actual runtime state.

This is tracked in the Wave B documentation scope — can be done as a comment in the existing task file.

---

## Implementation Order

```
1. [VERIFY] TSK-0274 — TaskSequenceGoal.IsComplete (gates roadmap)
2. [FIX]    TSK-0275 — /give command injection (P0 security)
3. [FIX]    TSK-0277 — Chat command deny list (P0 security)
4. [FIX]    TSK-0276 — BlockNotFound retry counter (5 min, highest ROI)
5. [FIX]    TSK-0278 — LLM parse failure signal + tests
6. [FIX]    TSK-0279 — Delete dead chatFilter.js + JS tests
7. [DOC]    Update TSK-0190 to match live code
```

Each fix is independent — parallel execution is safe.

---

## File Index for the Next Agent

| File | Purpose | Fixes |
|------|---------|-------|
| `Data/Pages/council/2026-06-30-external-audits-council.md` | Full council report with all 10 seats, synthesis, dissent | Context for all fixes |
| `Data/Pages/Audits/2026-06-30_memorysmith_additional_code_audit_legacy_debt_and_architecture_delta.md` | Audit A | Background |
| `Data/Pages/Audits/memorysmith_followup_debt_audit.md` | Audit B | Background |
| `Data/Pages/Audits/memorysmith_agent_addendum_audit.md` | Audit C | Background |
| `Data/Pages/Audits/memorysmith_agent_audit_report(1).md` | Audit D | Background |
| `WebUI.Blazor/AgentBackgroundService.cs` | ~3500-line god class | TSK-0274 (dispatch loop), TSK-0275 (/give), TSK-0277 (command deny), TSK-0276 (retry counter), TSK-0278 (caller) |
| `Agent.Planning/LlmEvaluatorImpl.cs` | LLM evaluation result parser | TSK-0278 (ParseEvaluationResult, ExtractJson) |
| `MineflayerAdapter/creativeProvider.js` | Adapter-side creative provisioning | TSK-0275 (/give fallback) |
| `MineflayerAdapter/index.js` | Adapter main module | TSK-0279 (verify no import), TSK-0277 (adapter-side guard optional) |
| `MineflayerAdapter/chatFilter.js` | Dead code — DELETE | TSK-0279 |
| `Agent.Core/Models/TaskSequenceGoal.cs` | Sequence goal state machine | TSK-0274 (verification) |
| `Data/Tasks/tsk-0190-creative-mode-recovery-guards.json` | Task with drift | Post-fix correction |
| `MemorySmith.Agent.Tests/` | Test project | TSK-0274 tests, TSK-0276 tests, TSK-0278 tests, TSK-0279 JS tests |

---

## Acceptance Criteria (from Council Report — Sprint 56 Gates)

- [ ] **AC-1:** TaskSequenceGoal.IsComplete verified. If P0 confirmed, fix deployed with tests.
- [ ] **AC-2:** /give command injection fixed. Block names sanitized. Allowlist enforced.
- [ ] **AC-3:** Chat command deny list deployed. Unit tests verify each denied command.
- [ ] **AC-4:** BlockNotFound retry counter type mismatch fixed. Test verifies termination.
- [ ] **AC-5:** LLM parse failure treated as signal, not silence. Structured logging added.
- [ ] **AC-6:** ParseEvaluationResult + ExtractJson internal + tested.
- [ ] **AC-7:** Dead chatFilter.js deleted. Zero references remain.

---

## Known State (Sprint 56 Wave A Continuation)

The Sprint 56 Wave A work (TSK-0260-0273) addressed adapter bugs from an external Mineflayer audit. Key unresolved items in Wave A that Wave B should be aware of:

- **TSK-0270 (P0):** PlaceBlock timeout — root cause found (5s timeout too short, increased to 15s; stale `_stopRequested` flag fixed; `ActionCompletedEvent` correlation added). Needs live validation.
- **TSK-0271 (P1):** 218 simultaneous place actions — diagnosed as by-design (MaxConcurrentPlaceBlock=8 limits concurrent dispatch), not a bug. Reclassify if needed.
- **TSK-0272 (P1):** Creative mode provisioning — root cause found (`bot.game?.gameMode` string vs number comparison). Fixed via `normalizeGameMode()`. Needs live validation.
- **TSK-0273 (P2):** GetStatus timeout — shares root cause with TSK-0270 (actionCompleted correlation). Fix applied in TSK-0270. Needs live validation.

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|------|:----------:|:------:|------------|
| TaskSequenceGoal.IsComplete confirmed P0 blocks roadmap | Medium | Critical | Verification is first action. If P0, fix immediately with tests. |
| Security fixes break creative provisioning | Low | High | All TSK-0275 changes are in the `/give` path only. `creative.setInventorySlot()` path is untouched. |
| LLM parse signal change causes false replans | Low | Medium | Conservative default: parse failure → `confidence=0` → `shouldReplan=false` still, but with structured signal so callers can escalate. |
| Command deny list blocks legitimate commands | Low | Medium | Deny list is curated. `/time set day`, `/tp @p`, `/summon` for mobs are NOT in deny list. |
