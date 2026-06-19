# AGENTS.md — Coding Guidelines for AI Agent Contributors

This project (`MemorySmith.Agent`) is maintained by AI agents (Claude, etc.) and
human developers alike. These conventions keep the codebase consistent.

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
| `IChatInterpreter` | `Agent.Personality` | InterpretAsync → ChatInterpretation |

See `Data/Pages/architecture.md` for the full interface list and runtime flow.
