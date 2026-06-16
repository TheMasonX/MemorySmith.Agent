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
  ```
- Optional `args` override constants at call-time:
  ```js
  const { tableSearchRadius = CRAFT_TABLE_SEARCH_RADIUS } = args;
  bot.findBlock({ maxDistance: tableSearchRadius });
  ```
- Always use `pathfinder.goto()` **before** proximity-dependent actions
  (craft, place, smelt, interact). Never assume the bot is already in range.

---

## Sprint Workflow

```
implement → push → CI green (conclusion: success) →
6-seat council review (Data/Pages/council/) → fix blockers → next sprint
```

- No sprint ships with a failing CI or a **blocking** council finding.
- Council seats: Source-Grounded Archivist · Data Model Architect · Retrieval Specialist ·
  Human Learning Advocate · Skeptical Reviewer · Synthesizer.
  Each seat: confidence %, explicit dissent, blocking vs deferred.

---

## GitHub MCP

- Use `github__create_or_update_file` per file. Pass content as plain text (never base64).
- Existing files require their current blob SHA — fetch via `github__get_file_contents` first.
- `.github/workflows/*.yml` requires the `workflow` OAuth scope (403 otherwise).
  Surface workflow YAML to the user for manual apply via the GitHub web UI.

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
