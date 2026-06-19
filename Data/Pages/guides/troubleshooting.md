# Troubleshooting Guide

Common problems, symptoms, and fixes for MemorySmith.Agent.

---

## Bot Not Responding to Chat

**Symptom:** You type in Minecraft chat but the bot says nothing.

**Checks:**
1. Is the bot online? Check `/api/agent/status` — `"status"` should not be `"disabled"`.
2. Is `Agent:Enabled = true` in `appsettings.json`?
3. Is the LLM enabled but Ollama not running? Check logs for LLM timeout warnings. Set `Llm:Enabled = false` to use pattern matching only.
4. Is your message being filtered as a system message? Check `SYSTEM_MESSAGE_PATTERNS` in `index.js`. If your chat looks like a server message, it may be filtered.
5. Are you more than 64 blocks away and not named the bot? Distance gate silently drops messages.
6. Check `logs/agent-<date>.log` for `[chat]` lines showing received messages.

**Quick test:** Say "help" in chat — if pattern matching works, you'll get a command list without LLM.

---

## Bot Stuck in a Replan Loop

**Symptom:** Bot repeatedly starts and abandons the same plan, logs show `ReplanTriggered` cycling.

**Checks:**
1. Check `logs/agent-<date>.log` for `"Governor: STALLED"` messages.
2. If STALLED, the governor will auto-recover after 60 seconds.
3. If the governor shows ACTIVE but replanning is fast (< 2s), the target block may be unreachable.
4. Check for `BlockNotFound` events — if the block is truly absent, `Wander` should trigger. Verify `WorldState.StructuredFacts` has a `BlockNotFound` entry.
5. If the inventory is stale (`IsInventoryStale = true`), the goal's `IsComplete` always returns false — ensure `GetStatus` is being called to refresh.

**Force unblock:** `POST /api/agent/command` with `{"command":"GetStatus"}` to refresh world state.

---

## LLM Timeout / "Hmm..." Keeps Appearing

**Symptom:** Bot says "Hmm..." frequently, logs show LLM timeout warnings.

**Checks:**
1. Is Ollama running? `ollama list` or `curl http://localhost:11434/api/tags`.
2. Is the model loaded? First request is slow due to model load time — subsequent calls are faster.
3. Is `LlmTimeoutSeconds` too short? Default is 10s. Increase to 30s for slow hardware: `"LlmTimeoutSeconds": 30`.
4. Check if a larger model is overloading CPU. Switch to `llama3.2` (3B) for fast responses.
5. Craft/forge/smelt commands bypass LLM entirely via CraftRegex — use these if LLM is slow.

**Disable LLM:** Set `"Llm": { "Enabled": false }` to fall back to pattern matching permanently.

---

## Inventory Shows Wrong Values After /clear

**Symptom:** Bot says it has items but inventory was cleared with `/clear`.

**Cause:** `WorldState.IsInventoryStale` is true — last inventory snapshot is outdated.

**Fix:**
1. Send `POST /api/agent/command {"command":"GetStatus"}` to force an inventory refresh.
2. In `appsettings.json`, verify `HealthCriticalThreshold` is set — the agent auto-enqueues `GetStatus` on low health which also refreshes inventory.
3. Check that `SYSTEM_MESSAGE_PATTERNS` includes the `/clear` response format. Pattern: `^Cleared\s+(?:\d+|\S+'s|the\s+inventory)`.

---

## Crafting Goal Returns "Sorry" Instead of Crafting

**Symptom:** You say "craft an iron pickaxe" and the bot responds "Sorry" or does nothing.

**Checks:**
1. Verify `CraftRegex` is active — check logs for `[chat] <Username> -> CreateGoal (CraftItem:iron_pickaxe)`. If the intent log shows `CraftItem`, the interpreter worked.
2. If intent is correct but goal fails, check `FailureReason`:
   - `RecipeMissing` — the agent doesn't have the recipe. Ensure `crafting-recipes.json` or `CommonMinecraftBlocks` includes `iron_pickaxe`.
   - `InventoryFull` — no room for the crafted item.
   - `ToolTimeout` — crafting took > 30 seconds.
3. Is there a crafting table nearby? `CraftItem` requires a crafting table within `CRAFT_TABLE_SEARCH_RADIUS` (default 8 blocks).

---

## Damage Interrupt Firing Too Often

**Symptom:** Bot constantly interrupts goals due to damage from mobs or fall damage.

**Fix:**
1. Increase `DamageInterruptCooldownSeconds` (default 3): add to `appsettings.json`.
2. Check `DamageInterruptThresholdHp` — default is 6 HP (3 hearts). Increase for tankier playstyles.
3. For goals that should never interrupt (future combat goals), override `IGoal.DamageInterruptThresholdHp` to return 0.

---

## MemorySmith / Memory Gateway Connection Issues

See [MemorySmith Setup Guide](memorysmith-setup.md) for full setup instructions.

| Error | Fix |
|---|---|
| `Connection refused on :5001` | Start agent KB MemorySmith |
| `Connection refused on :6869` | Start world KB MemorySmith, or set `WorldKbUrl = null` |
| `401 Unauthorized` | Set matching `ApiKey` in both configs |
| World KB tools return empty results | Check startup log for `WorldKbUrl not configured` warning |

---

## Bot Wanders Aimlessly

**Symptom:** Bot wanders indefinitely instead of mining.

**Cause:** Wander is only supposed to trigger when `WorldState` has a `BlockNotFound` fact for the target block. If wander triggers without this fact, it's a bug.

**Checks:**
1. Check `logs/agent-<date>.log` for `BlockNotFound` events from `WorldStateProjector`.
2. Verify the gather plan sequence: it should be `SearchMemory → MineBlock → GetStatus` with Wander conditional.
3. If `SearchMemory` returned a valid location but `MoveTo` failed repeatedly, the target may be unreachable. Check `FailureReason = TargetUnreachable`.

---

## CI Fails After My Change

**Expected baseline:** All jobs green (`build-and-test`, `build-docs`, `deploy-pages`). Any failure is a new regression.

**Diagnose without admin rights:**
```bash
# Find check runs for a commit
curl https://api.github.com/repos/TheMasonX/MemorySmith.Agent/commits/<sha>/check-runs

# Get annotations for a check run
curl https://api.github.com/repos/TheMasonX/MemorySmith.Agent/check-runs/<run_id>/annotations
```

**Common CI failures:**
- Verbatim string corruption (Rule E-1) — use `github__create_or_update_file` with paramsFile directly
- Missing `using` directive — all `using` must precede file-scoped `namespace`
- `TreatWarningsAsErrors` — zero warnings; check for new CS warnings

---

## Agent Loop Not Starting

**Symptom:** `/api/agent/status` returns `"disabled"`.

**Fix:** Set `"Agent": { "Enabled": true }` in `appsettings.json` and restart.

---

## Log File Not Found

**Symptom:** No log files in `logs/` directory.

**Fix:** Serilog writes to `logs/agent-<date>.log` relative to the working directory. Run `dotnet run` from the `WebUI.Blazor` directory or check the working directory in your run configuration. See [Logging Guide](logging.md).
