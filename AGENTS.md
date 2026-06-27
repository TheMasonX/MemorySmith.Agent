# AGENTS.md — Coding Guidelines for AI Agent Contributors

This project (`MemorySmith.Agent`) is maintained by AI agents (Claude, etc.) and
human developers alike. These conventions keep the codebase consistent.

## Semantic Versioning & Breaking Changes

MemorySmith.Agent follows **Semantic Versioning** for its public API surface (tool schemas,
event contracts, REST API, protocol wire format). See [`BREAKING_CHANGES.md`](BREAKING_CHANGES.md)
for the full deprecation policy, version rules, and migration templates.

- **Announce breaking changes 1 sprint before implementation.**
- **Mark deprecated APIs with `[Obsolete("message")]`** pointing to the replacement.
- **Record every breaking change in `BREAKING_CHANGES.md`** with before/after and migration guidance.

---

## Package Vetting Policy

All NuGet/npm dependencies must comply with [`Data/Pages/policies/package-vetting.md`](Data/Pages/policies/package-vetting.md):

- **P-1:** Documented justification required for every new package.
- **P-1a:** License whitelist — MIT, Apache-2.0, BSD-2/3-Clause only. No GPL/AGPL/copyleft.
- **P-2:** Every dependency must be listed in `WebUI.Blazor/wwwroot/about.html`.
- **P-3:** Vulnerable packages are a P0 blocker — `dotnet list package --vulnerable` must return zero results.
- **P-4:** Deprecated packages are prohibited.
- **P-5:** Direct pinning of transitive deps requires justification and a removal plan.

**Sprint 51 Incident:** `SQLitePCLRaw.lib.e_sqlite3` (deprecated, CVE-2025-6965 CVSS 7.2) was
removed after being added without vetting in Sprint 50. The policy above prevents recurrence.

---

---

## No Magic Numbers

All timeouts, TTLs, delays, retry counts, search radii, and similar tunable values
**must** use named constants or configurable options — never raw literals embedded in logic.

| Pattern | C# | JavaScript |
|---------|-----|-----------|
| Named constant | `private const int MaxRetries = 5;` | `const CRAFT_TABLE_SEARCH_RADIUS = 8;` |
| Named TimeSpan | `private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);` | n/a |
| Configurable option | `public int ItemCacheTtlSeconds { get; init; } = 60;` | pass via `args` with const default |

Exception: single-use, non-tunable values (e.g. an inline regex flag) may be anonymous
*only* if they are private, never likely to change, and documented inline.

```csharp
// ❌  raw literals
bot.findBlock({ maxDistance: 4 });
var cache = new ConcurrentDictionary<string, (ItemSpec?, DateTimeOffset)>();
// 60 second TTL (magic number)

// ✅  named / configurable
const int CRAFT_TABLE_SEARCH_RADIUS = 8; // JS
public int ItemCacheTtlSeconds { get; init; } = 60; // C# option
var ttl = TimeSpan.FromSeconds(_opts.ItemCacheTtlSeconds); // use site
```

---

## Rule E-1: Never Patch C# Verbatim-String Files via Agent Intermediary

C# files containing verbatim strings (`@"..."` or raw string literals `"""..."""`) are
**unsafe to patch** via agent text-manipulation tools (Edit, sed, regex substitution).
The agent's output layer corrupts escape sequences inside verbatim strings — especially
`\"`, `\\`, and curly-brace sequences — producing invalid C# that silently breaks builds.

Sprint 20 required 13 fix commits because of this failure mode.

**Affected files** — any C# file containing:
- `@"..."` verbatim string literals
- `"""..."""` raw string literals
- `Regex` patterns with backslashes or embedded quotes
- JSON strings with curly braces inside string assignments

**Safe patch recipe:**

```
1. Fetch the current blob SHA:
   github__get_file_contents → captures {sha}

2. Read the file, make changes locally in the sandbox (Bash/Edit on local copy)

3. Write the complete replacement as a paramsFile:
   Write({ file_path: "/tmp/params.json", content: JSON.stringify({
     owner, repo, path, message, content: <full file text>, sha, branch
   }) })

4. Submit:
   github__create_or_update_file with paramsFile: "/tmp/params.json"
   (never pass content inline — token truncation will corrupt it)
```

**Verbatim-regex safe patch pattern** (when you must change a string inside a verbatim block):

Use the Bash `sed` with escaped delimiter to avoid misinterpreting backslashes, then commit
the whole file via paramsFile:

```bash
# Read file → modify in sandbox → write paramsFile → commit
cp Agent.Foo/Bar.cs /tmp/Bar.cs
# Edit /tmp/Bar.cs with your change
python3 -c "
import json
content = open('/tmp/Bar.cs').read()
params = {'owner':'TheMasonX','repo':'MemorySmith.Agent',
          'path':'Agent.Foo/Bar.cs','message':'fix: ...','content':content,
          'sha':'<blob_sha>','branch':'main'}
json.dump(params, open('/tmp/params.json','w'))
"
# Then: github__create_or_update_file paramsFile=/tmp/params.json
```

---


## Rule E-2: github__create_or_update_file — Pass Plain Text, Not Base64

When writing C# or JS files via `paramsFile`, the `content` field MUST be plain UTF-8 text.

**Background:** The GitHub REST API requires file content to be base64-encoded in the request body.
The `github__create_or_update_file` MCP action handles this encoding automatically.

**WRONG — causes double-encoding (the BLK-02 pattern):**
```python
import base64
params = {
    "content": base64.b64encode(file_text.encode()).decode()  # ← DO NOT DO THIS
}
```

**CORRECT — pass plain text:**
```python
params = {
    "content": file_text   # ← MCP handles GitHub API base64 transparently
}
```

**Symptoms of double-encoding:**
- File content in GitHub shows as a base64 string (starts with `Ly8g` for `// `, `bmFt` for `namespace`, etc.)
- `dotnet build` fails with "unrecognized escape sequence" or parser errors on every line
- Sprint 28/29/30/32 had this problem; Sprint 33 BLK-S33-01 (truncated Program.cs) was the result

---

## Rule E-3: Never Swallow Exceptions or Drop Events Silently

Every event-handling switch, catch block, and error-handling path **must** log a
warning or error when it discards or ignores an event/exception. Silent dropping
("this can't happen" with no log) is a P0 defect pattern.

**Background:** Sprint 41's `BlockPlacedEvent` had NO handler in
`AgentBackgroundService.ProcessEventsAsync`. It fell through to `default:` and
was silently dropped — no log, no trace. The correlated `PlaceBlock` action
remained in `Dispatched` state until the 30-second sweep timeout fired. The bot
could place only 2 blocks per minute despite the adapter successfully placing
every block and sending the event.

**Rules:**
1. Every `switch` on a discriminated union / event type **must** have a `default`
   branch that logs `LogWarning` with the unhandled type name.
   ```csharp
   // ✅  correct — unhandled events are visible in logs
   default:
       logger?.LogWarning("Unhandled world event type: {Type}", worldEvent.GetType().Name);
       break;
   ```
2. Every `catch` block that does not rethrow **must** log at `LogWarning` or higher.
   ```csharp
   // ✅  correct — silent catch logs the error
   catch (Exception ex) when (IsRecoverable(ex))
   {
       logger?.LogWarning(ex, "Recoverable error in {Context}: {Message}", name, ex.Message);
   }

   // ❌  WRONG — silent catch hides failures
   catch { /* best-effort — never crash */ }
   ```
3. Event-handling code **must** log the event identity (type, position, correlation)
   so post-hoc debugging can trace the path. For `default`-branch drops, log the
   type name and the raw event data at `LogDebug` level.
4. Exception to rule 1: `TryRouteAsError` in `AgentBackgroundService` is exempt
   because it explicitly re-dispatches recognized errors to `_gameErrors` channel.
   Events that reach its fallthrough already pass through the `default` branch
   above and are logged there.

**Safe catch pattern** (when you must swallow to keep the loop alive):
```csharp
try
{
    // fallible operation
}
catch (Exception ex) when (!ct.IsCancellationRequested)
{
    logger?.LogWarning(ex, "Non-fatal error in {Context}: {Message}", contextName, ex.Message);
    // Continue — loop must survive individual failures
}
```

---

## CRITICAL — LLM-First Architecture (Sprint 35+)

These rules encode architectural decisions locked in Sprint 35. Violations require
a council review and an explicit ADR to reverse.

**Canonical pipeline diagram:** [`Data/Pages/architecture.md`](Data/Pages/architecture.md#runtime-flow-canonical--sprint-50) —
this is the single authoritative description of the agent runtime flow.
When `architecture.md`, `AGENTS.md`, or any other doc disagree, `architecture.md` wins.

---

### CRITICAL Rule A-1: Parsers Never Create Goals

**Chat → IntentDraft → Planner → Goal** is the only valid pipeline. The reverse
direction (`Chat → Interpreter → Goal`) is forbidden.

- `IChatInterpreter.InterpretAsync` returns `ChatInterpretation`. It expresses semantic
  intent (what, item, blueprint, count, coords, confidence). It does **not** call
  `GoalFactory` and does **not** return a `GoalName` string.
- `IntentDraft` has no `GoalName` field. The mapping `intent → GoalName` is done
  exclusively in `AgentBackgroundService.IntentDraftToGoal` (Sprint 35 transition
  layer; moves to `IIntentManager` in Sprint 36+).
- No regex or fast-path in `ChatInterpreter` may produce a `ChatIntentType.CreateGoal`
  result for gather/build/craft intents. Only `CancelGoal`, `QueryStatus`,
  `QueryInventory`, and `QueryHelp` may be fast-pathed (zero-risk, deterministic).

**Why this matters:** Fast-path goal creation bypasses the LLM and skips confidence
scoring, clarification questions, and context enrichment. Sprint 35 P1-B explicitly
removed these fast-paths after they caused BUG-4 (two-minute stall on "craft an
iron pickaxe" when Ollama timed out).

---

### PRINCIPLE-1 Pipeline Status (Sprint 38)

As of Sprint 38 the pipeline is:
  Chat → IChatInterpreter → ChatInterpretation → AgentBackgroundService
       → IntentManager.BuildGoalRequest → GoalRequest → GoalFactory → IGoal

- `ParseDecision` (LlmChatInterpreter): legacy goal-name switch removed (Sprint 38 P1-A).
  When `IntentManager` is injected, goal mapping is delegated to it exclusively.
- `TryParseTruncatedJson`: accepts optional `IntentManager?` (Sprint 38 P1-B).
  When provided, maps partial intent to GoalRequest via IntentManager.
- `ChatInterpretation.GoalName` field still exists for Sprint 21 backward compatibility;
  removal deferred to Sprint 39 (requires ChatInterpreterTests + Sprint21Tests updates).

**Correlation model** (Sprint 25+):
- Every dispatched action gets a `correlationId` (Guid) stored in `ActionData.Context`.
- `AgentBackgroundService._correlatedActions` tracks lifecycle: Dispatched → Completed/Failed/TimedOut.
- `CompleteCorrelatedActionByTool(toolName)` transitions the first matching Dispatched action.
- `_currentGoal?.Id` is now used as the GoalId in ActionOutcome (Sprint 38 P2; was Guid.Empty).

**Observation-driven replanning** (Sprint 38 P3 stub):
- `ActionOutcome[]` is accumulated per dispatch cycle in `_cycleOutcomes`.
- `ILlmEvaluator.EvaluateAsync(goal, outcomes)` interface defined; concrete impl in Sprint 39.

---

### IntentDraft Schema (Sprint 35 P1-A)

The LLM returns a JSON object. `LlmChatInterpreter.ParseDecision` deserialises it
into this shape:

```json
{
  "addressed":             "yes" | "maybe" | "no",
  "intent":                "gather" | "build" | "craft" | "navigate" | "cancel"
                           | "status" | "help" | "conversation" | "clarify" | "ignore",
  "item":                  "<minecraft_id or null>",
  "blueprint":             "<blueprint_id or null>",
  "count":                 <integer or null>,
  "x": <integer or null>,  "y": <integer or null>,  "z": <integer or null>,
  "confidence":            <0.0–1.0>,
  "clarificationQuestion": "<question to ask if confidence is low, or null>",
  "response":              "<in-game reply, max 50 words, empty if intent is ignore>"
}
```

- **`confidence`** — must be present; default 1.0 if absent. `LlmConfidenceThreshold`
  (default 0.6, `ChatOptions`) gates clarification: if `confidence < threshold` AND
  `clarificationQuestion` is non-empty, the interpretation becomes
  `ChatIntentType.Unknown` and the bot asks the clarifying question.
- **No `goalName` field** — goal mapping is the planner's responsibility.
- Truncated JSON (no closing brace) is recovered via `TryParseTruncatedJson`.

---

### ActionOutcome — Universal Tool Result (Sprint 35 P1-E)

Every `ToolDispatcher.CallAsync` (and its future `CallWithOutcomeAsync` variant)
will produce an `ActionOutcome` record. This is the single result type flowing
into recovery, replanning, journaling, and world-state updates.

```csharp
// Agent.Core/Models/ActionOutcome.cs
public sealed record ActionOutcome(
    Guid GoalId, string ToolName, bool Success,
    string ObservationSummary,
    IReadOnlyList<StructuredEffect> Effects,
    DateTimeOffset Timestamp);

public sealed record StructuredEffect(
    string Type, string Item, int Count);   // e.g. "ItemCollected", "oak_log", 3
```

Factory helpers: `ActionOutcome.Collected(goalId, tool, item, count)`,
`ActionOutcome.Succeeded(goalId, tool, summary)`, `ActionOutcome.Failed(goalId, tool, reason)`.

Sprint 35 added the records and stubs; Sprint 36 wires `CallWithOutcomeAsync` in
`ToolDispatcher` and `LogOutcome` in `IAgentJournal`.

---

## C# Conventions

- `*Options` classes must be `sealed record` so tests can use `with {}` expressions.
- Bind from `appsettings.json` via `builder.Services.Configure<TOptions>(section)`.
- Timeouts stored as `int *Seconds`; convert to `TimeSpan` at the use site.
- Test-injectable delays/counts: add optional constructor params with `null = use defaults`
  (e.g. `TimeSpan[]? reconnectDelays = null`) so tests can inject zero-delay values.
- All `using` directives **must appear before** the file-scoped `namespace` declaration.
  File-scoped namespaces cause relative resolution; placing `using` after `namespace` breaks
  unqualified type lookups in `MemorySmith.Agent.Tests`.
- Never use fully-qualified names like `Agent.Core.Position` inside `MemorySmith.Agent.Tests`.
  The `Agent` prefix resolves to the parent namespace `MemorySmith.Agent`, not the root.
- Use the `file` modifier on test-only helper classes to avoid name collisions across test files.
- `TreatWarningsAsErrors = true` (set in `Directory.Build.props`) — the build rejects any new
  compiler warning. Fix warnings immediately; never suppress with `#pragma warning disable`.

---

## JavaScript (MineflayerAdapter/index.js)

- Declare all tunable constants at the **top of the file**, grouped by category:
  ```js
  // ── Tunable constants ─────────────────────────────────
  const MINE_SEARCH_RADIUS_NEAR    = 64;   // blocks — first findBlock pass
  const MINE_SEARCH_RADIUS_FAR     = 128;  // blocks — second findBlock pass
  const CRAFT_TABLE_SEARCH_RADIUS  = 8;    // blocks — findBlock for crafting_table
  const CRAFT_TABLE_REACH_DISTANCE = 2;    // blocks — pathfinder GoalNear tolerance
  const MAX_MINE_PATH_FAILURES     = 3;    // consecutive pathfinder failures before abort
  const FLAT_AREA_DEFAULT_RADIUS   = 32;   // blocks — findFlatArea default search radius
  const FLAT_AREA_RETRY_RADIUS     = 48;   // blocks — findFlatArea retry after zero-area result
  ```
- Optional `args` override constants at call-time:
  ```js
  const { tableSearchRadius = CRAFT_TABLE_SEARCH_RADIUS } = args;
  bot.findBlock({ maxDistance: tableSearchRadius });
  ```
- Always use `pathfinder.goto()` **before** proximity-dependent actions
  (craft, place, smelt, interact). Never assume the bot is already in range.
- System message filtering: `SYSTEM_MESSAGE_PATTERNS` in `index.js` must block all
  server/admin messages from reaching the LLM pipeline. Add new patterns for any
  server output that leaks through (e.g. `/clear` responses, `/give` echoes, teleport
  announcements). Never remove existing patterns without verifying they don't filter
  real player chat.

### `/op` and `/give` on LAN worlds (Sprint 52)

The `/op` command does NOT work in "Open to LAN" singleplayer worlds.
There is no permissions system in LAN mode — all players have equal rights.
This means `/give @p` also does not function via chat on LAN worlds
(the server ignores commands from non-OP players, and `/op` can't grant OP).

**Creative inventory fallback:** Use `bot.creative.setInventorySlot()` (Mineflayer
creative API) which works on all versions and requires no permissions.
The `creativeProvider.js` module handles this with a `/give` fallback for
dedicated servers where OP is available.

Do NOT rely on `/op` or `/give` as the primary creative provisioning mechanism
for LAN testing — they will silently fail.

### playerCollect guard (Sprint 35 P0-A)

The `bot.on('playerCollect', ...)` listener must guard against items collected by
other players sharing the entity. The safe pattern:

```js
bot.on('playerCollect', (collector, itemDrop) => {
    if (collector.username !== bot.username) return; // not our pickup
    // entity.metadata is Mineflayer-version-sensitive; use fallback chain:
    const itemName = entity?.metadata?.find(m => m?.value?.name)?.value?.name
                     ?? entity?.name ?? 'unknown';
    sendEvent('itemCollected', { item: itemName, count: 1, correlationId });
});
```

Without the guard, items picked up by other players increment the bot's inventory
in WorldState — a bug that is very hard to reproduce in single-player tests.

`entity?.metadata?.name` is Mineflayer-version-specific. The fallback chain
(`entity?.metadata?.find(m => m?.value?.name)?.value?.name ?? entity?.name ?? 'unknown'`)
is version-safe. Do not simplify to `entity.name` alone.

### mineComplete event contract (Sprint 35 P0-B)

At the end of every mine-block loop, emit `mineComplete` with the final counts:

```js
sendEvent('mineComplete', {
    block: targetBlock,
    mined: actualMinedCount,
    targetCount: requestedCount,
    correlationId: args.correlationId
});
```

C# `MineCompleteEvent(string Block, int Mined, int TargetCount, DateTimeOffset Timestamp)`
is the typed counterpart. `AgentBackgroundService.ProcessEventsAsync` uses this event
to transition the correlated `PendingAction` to `Completed` state — critical for the
`SweepTimedOutActions` path not to orphan successful mine completions.

---

## Sprint Workflow

```
implement → push → CI green (conclusion: success) →
6-seat council review (Data/Pages/council/) → fix blockers → next sprint
```

- No sprint ships with a failing CI or a **blocking** council finding.
- Council review written to `Data/Pages/council/<topic>-council-<date>.md`.
- Pre-council doc written to `Data/Pages/council/<topic>-pre-council-<date>.md` for context.
- Council seats: Source-Grounded Archivist · Data Model Architect · Retrieval Specialist ·
  Human Learning Advocate · Skeptical Reviewer · Synthesizer.
  Each seat: confidence %, explicit dissent, blocking vs deferred triage,
  testable acceptance criteria.

---

## GitHub MCP

- Use `github__create_or_update_file` per file. Pass content as plain text (never base64).
- Existing files require their current blob SHA — fetch via `github__get_file_contents` first.
- For any file > ~5 KB or containing verbatim strings: use `paramsFile` (see Rule E-1).
- `github__push_files` (trees API) is blocked on this token — use `create_or_update_file` per file.
- `.github/workflows/*.yml` requires the `workflow` OAuth scope (403 otherwise).
  Surface workflow YAML to the user for manual apply via the GitHub web UI.
- `github__pull_request_read` uses `method` param: `get`, `get_diff`, `get_status`,
  `get_files`, `get_reviews`, `get_check_runs`.
- CI annotations (no admin required): `GET .../commits/<sha>/check-runs` then
  `GET .../check-runs/<id>/annotations`.

---

## Key ADRs

| ID | Decision |
|----|----------|
| D-002 | MemorySmith wiki as long-term memory |
| D-003 | Deterministic-first; LLM is optional enhancement |
| D-006 | Blueprints are wiki pages |
| D-007 | .slnx solution format |
| D-008 | Node.js for Mineflayer subprocess |
| D-010 | ActionProtocol wire names (all lowercase) |
| D-011 | Parsers never create goals (Sprint 35, CRITICAL) |
| D-012 | ActionOutcome is the universal tool result (Sprint 35) |
| D-013 | Inventory is event-sourced via ItemCollectedEvent (Sprint 35) |

Full decisions in `Data/Pages/decisions.md`.

---

## Key Interfaces (quick reference)

| Interface | Location | Purpose |
|-----------|----------|---------| 
| `IGoal` | `Agent.Core` | Goal evaluation; `DamageInterruptThresholdHp` per-goal override |
| `IPlanner` | `Agent.Planning` | `PlanAsync` + `ReplanAsync` |
| `IGoalDecomposer` | `Agent.Planning` | Pluggable goal decomposition (CanHandle + Decompose) |
| `IReplanGovernor` | `Agent.Planning` | Stall detection (IsStalled, RecordPlan, RecordProgress) |
| `IAgentJournal` | `Agent.Core` | Append-only event log (Append, GetRecent, Clear) |
| `IWorldModel` | `Agent.Core` | Observe / Predict / Reconcile / Uncertainty |
| `IMemoryGateway` | `Agent.Memory` | Search / GetPage / CreatePage / UpdatePage |
| `ITool` | `Agent.Tools` | Name, Description, InputSchema, ExecuteAsync |
| `IWorldAdapter` | `Agent.World.Minecraft` | Connect, SendAction, ReceiveEvents |
| `IChatInterpreter` | `Agent.Personality` | InterpretAsync → ChatInterpretation (no goal creation) |
| `ILlmEvaluator` | `Agent.Core` | Evaluate ActionOutcome[] → should replan? (Sprint 39 impl) |

Sprint 36 will add: `IIntentManager`, `IPlanningManager`, `IExecutionManager`,
`IRecoveryManager`, `IStateManager`, `IDashboardPublisher` (AgentRuntime decomposition).

See `Data/Pages/architecture.md` for the full interface list and runtime flow.
