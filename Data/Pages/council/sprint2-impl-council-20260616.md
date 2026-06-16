# Council Review — Sprint 2: Crafting Chain + TTL Cache + CraftItemTool Pathfinding

**Date:** 2026-06-16
**Sprint:** Phase 6 Sprint 2 — End-to-End Build
**Scope:** HtnTaskLibrary.DecomposeBuild crafting chain (2b), MemorySmithItemRegistry TTL cache (2c),
           MineflayerAdapter/index.js crafting table pathfinding (2a), AGENTS.md, RestMemoryGatewayOptions
**CI commit:** 7517e8e (conclusion: success, 3 pre-existing warnings)

---

## What Was Implemented

### 2a — CraftItemTool Pathfinding (index.js)
- `CRAFT_TABLE_SEARCH_RADIUS = 8` and `CRAFT_TABLE_REACH_DISTANCE = 2` added as named constants
- All other tunable values extracted to named constants: `MINE_SEARCH_RADIUS_NEAR/FAR`,
  `MAX_MINE_PATH_FAILURES`, `FURNACE_SEARCH_RADIUS/REACH_DISTANCE`, `SMELT_TIMEOUT_MS`
- `craft` case now pathfinds to the nearest crafting_table before `bot.craft()` (for `requiresTable` recipes)
- Block re-fetched after navigation to avoid stale position
- `tableSearchRadius` can be overridden per-call via `args`

### 2b — DecomposeBuild Crafting Chain (HtnTaskLibrary.cs)
- Named constant: `TorchesPerCraft = 4` (vanilla Minecraft: 1 stick + 1 coal → 4 torches)
- New phase after raw material gather: emits `CraftItem` actions in dependency order
- `CraftingChainOrder` static list defines the ordered set: planks → table → slab → door → chest
- `EmitCraftIfNeeded` helper: skips step if blueprint doesn't need item or inventory is sufficient
- Torch handling: if blueprint has torch, adds coal gather step + `CraftItem(stick, N)` intermediate
- Existing tests (`HtnPlannerBuildTests`) still pass; 9 new tests added (`HtnTaskLibraryCraftingTests`)

### 2c — IItemRegistry TTL Cache (MemorySmithItemRegistry.cs + RestMemoryGatewayOptions.cs)
- `RestMemoryGatewayOptions.ItemCacheTtlSeconds = 60` — configurable, default 60s
- `ConcurrentDictionary<string, (ItemSpec?, DateTimeOffset Expires)> _cache` in registry
- Cache key = normalised slug (lowercase, hyphens) — same as HTTP lookup key
- Null results cached too (prevents repeated misses for non-existent items)
- `ItemCacheTtlSeconds = 0` disables caching for test isolation
- 4 new cache tests added to `ItemRegistryTests.cs`; existing tests updated for new constructor

### AGENTS.md (new at repo root)
- Documents: no-magic-numbers rule, C# options conventions, JS named constants, sprint workflow, GitHub MCP constraints

### Program.cs
- `MemorySmithItemRegistry` DI now passes `RestMemoryGatewayOptions` for TTL config

---

## 6-Seat Council Review

### Seat 1 — Source-Grounded Archivist

**Confidence: 93%**

Verified against sprint spec:
- 2a: `pathfinder.goto(GoalNear(..., CRAFT_TABLE_REACH_DISTANCE))` before `bot.craft()` ✓
  Search radius expanded from 4 to 8 (CRAFT_TABLE_SEARCH_RADIUS) ✓
  Block re-fetched after navigation ✓
  Args override via `tableSearchRadius` ✓
- 2b: `CraftItem(oak_planks, N)` emitted when blueprint lists planks ✓
  `CraftItem(crafting_table, 1)` emitted when plank-derived items needed ✓
  `CraftItem(oak_slab, N)`, `CraftItem(oak_door, N)`, `CraftItem(chest, N)` ✓
  Sticks intermediate + torch crafting ✓
  Coal gather when torch is in blueprint ✓
  Inventory check (skip if sufficient) ✓
  `TorchesPerCraft = 4` named constant ✓
- 2c: `ItemCacheTtlSeconds` in `RestMemoryGatewayOptions` (configurable) ✓
  `ConcurrentDictionary` TTL cache ✓
  Null results cached ✓
  TTL=0 disables caching ✓
- AGENTS.md: covers no-magic-numbers rule ✓

**Findings:**
- D1 (deferred): `CraftingChainOrder` and `RequiresCraftingTable` are separate static structures.
  `RequiresCraftingTable` is defined but currently unused in the code (only `CraftingChainOrder` is
  iterated). The crafting table is emitted via `EmitCraftIfNeeded("crafting_table", ...)` when
  `crafting_table` is in the blueprint Materials. Items that require a table (slabs, doors, chests)
  don't use `RequiresCraftingTable` directly. Should either use it or remove it (dead code).

### Seat 2 — Data Model Architect

**Confidence: 92%**

`ConcurrentDictionary` is the correct choice for the cache:
- Thread-safe for concurrent `GetAsync` calls from `ChatConsumerAsync` + `DispatchActionsAsync`
- `TryGetValue` is lock-free on read — ideal for hot path
- `(ItemSpec? Spec, DateTimeOffset Expires)` value tuple is a simple, allocation-efficient struct

The `DateTimeOffset.UtcNow < entry.Expires` check correctly uses UTC throughout. No clock skew risk.

Null result caching is correct: a blueprint with 330 blocks of `torch` shouldn't hammer the wiki
with repeated misses if the page doesn't exist.

TTL=0 correctly disables caching: `CachingEnabled` property returns `false`, both the lookup and
the store are skipped. Clean test isolation.

`MaterialEntry[]` (from `BlueprintSchema.cs`) is used via LINQ `FirstOrDefault` and `ToDictionary`.
The `ToDictionary` with `StringComparer.OrdinalIgnoreCase` correctly handles case differences.

**Findings:**
- D2 (deferred): If two parallel `GetAsync("oak_log")` calls race on a cache miss, both will call
  `FetchAsync` and both will write the cache entry. The second write clobbers the first — both entries
  have the same TTL, so this is harmless (idempotent). No data corruption risk. But in a high-traffic
  scenario, two HTTP calls are issued instead of one. A `ConcurrentDictionary.GetOrAdd` with
  `Lazy<Task<ItemSpec?>>` pattern would handle this, but is overkill for the current load profile.

### Seat 3 — Retrieval Specialist

**Confidence: 89%**

The cache key (normalised slug) is consistent with the HTTP lookup key. `"oak_log"` → `"oak-log"` for
both the cache key and the HTTP request. No mismatch risk.

Null caching means a missing `oak_planks` wiki page won't cause a surge of HTTP misses during a
330-block build. After the first miss, subsequent `GetAsync("oak_planks")` calls return null from
cache for 60 seconds. Correct and desirable.

The crafting chain in `DecomposeBuild` uses `blueprint.Materials.ToDictionary(...)` — this does NOT
call `IItemRegistry`. The crafting chain is purely based on the blueprint's declared Materials,
not wiki page lookups. The cache is only needed when the planner resolves `GenericGatherGoal.IsComplete`
and the `HtnPlanner` calls `registry.GetAsync(...)`. The cache still benefits that path substantially.

**Findings:**
- D3 (deferred): The `ToDictionary` in `BuildCraftingChain` will throw if the blueprint has
  duplicate material entries for the same block (same block listed twice). This should use
  `GroupBy` + sum or a safe overload. Unlikely in practice (blueprints are wiki pages), but
  defensible.

### Seat 4 — Human Learning Advocate

**Confidence: 91%**

AGENTS.md is a genuine improvement for future agent sessions:
- The "no magic numbers" rule is stated clearly with examples
- The C# and JS patterns are kept symmetric (named const + optional arg override)
- Sprint workflow is concise and correct
- GitHub MCP constraints are documented to prevent the `workflow scope 403` footgun

The `HtnTaskLibraryCraftingTests.cs` suite (9 tests) is well-structured:
- Each test has a descriptive name and a single assertion goal
- `EmitCraftIfNeeded` skip tests (inventory sufficient) verify the most important correctness property
- The intermediate stick step and coal gather are separately tested

The `CountingGateway` file-local wrapper (in `ItemRegistryTests.cs`) is a clean test-isolation
pattern: delegates all 4 interface methods, tracks only what needs counting.

**Findings:**
- None blocking.

### Seat 5 — Skeptical Reviewer

**Confidence: 87%**

**Concerns:**

D4 (deferred): The crafting chain assumes vanilla Minecraft recipes. `TorchesPerCraft = 4` is
correct for vanilla, but a modded server with Forge/Fabric could have different yields. Since
the project targets vanilla for Phase 6 (per ADR decisions), this is acceptable. A future
`IRecipeRegistry` abstraction (item-registry wiki pages could include `yield_per_craft` field)
would generalise this.

D5 (deferred): In `DecomposeBuild`, the crafting table is emitted by `EmitCraftIfNeeded("crafting_table", ...)`
only when `crafting_table` appears in the blueprint's Materials. But slabs, doors, chests require a
crafting table. If the blueprint does NOT list `crafting_table` explicitly, the chain emits slab/door/
chest CraftItem actions but does NOT emit `CraftItem(crafting_table)` first. In Mineflayer,
`recipe.requiresTable = true` means the bot needs a placed crafting table in range. If none exists
in the world AND the blueprint doesn't list it, the craft will fail.

**This is a functional limitation** — not a code bug. The council recommends: if any item in
`RequiresCraftingTable` is in the blueprint Materials, always emit `CraftItem(crafting_table)` even if
`crafting_table` is not in Materials, as a preparatory step. Currently this is only done when the
blueprint explicitly lists `crafting_table`.

**BLOCKING (severity: medium — degrades reliability but doesn't crash):**
The above limitation means builds with slabs/doors/chests that don't list `crafting_table` as a
Material will fail silently at the craft step. The fix is 2 lines. Council recommends fixing before
Sprint 3.

**Fix:** In `BuildCraftingChain`, before the `foreach (var item in CraftingChainOrder)` loop:
```csharp
// If any table-requiring item is in the blueprint, ensure we craft a table first.
bool anyTableRequired = blueprint.Materials.Any(m => RequiresCraftingTable.Contains(m.Block));
if (anyTableRequired)
{
    var haveTable = state.Inventory.GetValueOrDefault("crafting_table");
    if (haveTable == 0)
        actions.Add(MakeAction("CraftItem", ("item", "crafting_table"), ("count", (object?)1)));
}
```

D6 (deferred): `RequiresCraftingTable` is defined as a static `HashSet<string>` but is only used in
the fix above. Currently it exists as dead code. After the fix it will be used.

### Seat 6 — Synthesizer

**Confidence: 91%**

Sprint 2 delivers the three HIGH-priority items from the roadmap:
- **2a**: CraftItemTool no longer silently fails when the crafting table is out of range. All JS
  constants are now named and consistent. This is a reliability improvement that also satisfies the
  "no magic numbers" directive.
- **2b**: DecomposeBuild can now drive a build from bare logs + coal to a fully-furnished house frame.
  The crafting chain is correctly dependency-ordered. The `TorchesPerCraft` constant is appropriately
  named.
- **2c**: The TTL cache eliminates the 330+ redundant HTTP requests identified in the architecture
  review. The 60s default is sensible; the `TTL=0` escape hatch for tests is correct.

**One blocking finding (D5 from Skeptical Reviewer):** Build blueprints that require table-recipe
items (slab, door, chest) but don't explicitly list `crafting_table` as a Material will fail at
the craft step. This is a 2-line fix and must be applied before Sprint 3.

**Recommendation: FIX BLOCKER then proceed to Sprint 3.**

---

## Acceptance Criteria

| Criterion | Status |
|-----------|--------|
| CraftItemTool pathfinds to table within 8 blocks | ✅ Sprint 2a |
| DecomposeBuild emits CraftItem for planks/table/slabs/door/chest/torch | ✅ Sprint 2b |
| Coal gathered when torch is in blueprint | ✅ Sprint 2b |
| Inventory check skips CraftItem when sufficient | ✅ Sprint 2b |
| IItemRegistry TTL cache — hit returns without HTTP | ✅ Sprint 2c (test: CountingGateway) |
| TTL=0 disables cache for test isolation | ✅ Sprint 2c |
| CI green | ✅ Commit 7517e8e |

---

## Blocking Findings

| ID | Description | Severity | Fix |
|----|-------------|----------|-----|
| B1 (=D5) | `CraftItem(crafting_table)` not auto-emitted when blueprint needs slab/door/chest but doesn't list table in Materials | Medium | Add `RequiresCraftingTable` check before CraftingChainOrder loop in `BuildCraftingChain` |

**Blocker B1 must be fixed before Sprint 3.**

---

## Deferred Items

| ID | Finding | Seat | Sprint |
|----|---------|------|--------|
| D1 | `RequiresCraftingTable` set is dead code (will be used by B1 fix) | Archivist | fix with B1 |
| D2 | Parallel miss race — two HTTP calls instead of one | Data Model | Sprint 3 |
| D3 | `ToDictionary` throws on duplicate blueprint materials | Retrieval | Sprint 3 |
| D4 | `TorchesPerCraft = 4` hardcoded vanilla recipe | Skeptical | future IRecipeRegistry |
| D6 | `RequiresCraftingTable` unused until B1 fix | Skeptical | fix with B1 |
