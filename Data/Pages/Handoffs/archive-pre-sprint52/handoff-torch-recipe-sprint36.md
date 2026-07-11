# Handoff: Torch Recipe Not Found in Mineflayer

**Date:** 2026-06-21
**Observed by:** Agent Smith (Sprint 36)

## Problem

When the user says `"leo craft a torch"`, the C# side correctly resolves `"torch"` to `GoalName: CraftItem:torch` and dispatches a `CraftItem` action with item `torch`. The Mineflayer adapter receives it but fails with:

```
Game error [craft]: No recipe found for: torch
```

The bot has coal (observed in inventory) and can craft sticks, so the prerequisites should be met.

## Root Cause

This is **not** a C# alias problem — `CraftAliases` correctly maps `"torch" → "torch"` and `ResolveCraftId` accepts it as a valid item ID. The failure is on the **Mineflayer/Node.js side** in `MineflayerAdapter/index.js`.

## What to Investigate

### 1. `bot.recipesFor()` behavior
The actual call in `index.js` (line 812):
```js
const recipes = bot.recipesFor(itemEntry.id, null, null, null);
```
Key observations:
- The 4th parameter (`craftingTable`) is `null`, which Mineflayer treats as **no filter** — it searches all recipes regardless of table requirement
- So the crafting-table theory does **not** explain this failure
- The torch recipe is a 1×2 vertical pattern (coal above stick) — it does **not** require a crafting table, it's craftable in the 2×2 inventory grid

**The real question:** Why does `recipesFor(itemEntry.id, null, null, null)` return empty when the item IS found in the registry?

### 2. Possible root causes
- **Mineflayer version quirk**: Some Mineflayer versions may treat `null` differently from `undefined` or treat it as falsy (equivalent to `false` = only non-table recipes). Even if so, torch shouldn't need a table.
- **Prerequisites filtering**: Mineflayer's `recipesFor` may internally filter recipes whose ingredients aren't in inventory — the prerequisite check might use a different item key for coal/charcoal.
- **Item ID mismatch**: `itemsByName['torch']` returns a valid entry, but the numeric item ID might not match what the recipe system expects in this Minecraft version.
- **Recipe book gating**: Newer Minecraft versions may require recipe book discovery before recipes are returned.
- **Missing shaped recipe registration**: The Minecraft version's recipe data might not include the torch recipe in a format Mineflayer can parse.

### 3. Diagnosis approach
Add debug logging before the throw:
```js
console.log('[craft] recipesFor diagnostic', {
  itemName,
  itemId: itemEntry.id,
  itemDisplayName: itemEntry.displayName,
  recipesLength: recipes?.length,
  inventory: bot.inventory.items().map(i => ({ name: i.name, count: i.count })),
});
```
Then test with explicit params:
```js
const r1 = bot.recipesFor(itemEntry.id, null, 1, false); // non-table only
const r2 = bot.recipesFor(itemEntry.id, null, 1, true);  // table only
const r3 = bot.recipesFor(itemEntry.id, null, 1, null);  // no filter
```

### 4. Items with shaped recipes (genuinely need crafting table)
These items genuinely require a crafting table and may fail for a different reason if `recipe.requiresTable` isn't detected:
- chest (8 planks)
- furnace (8 cobblestone)
- door (6 planks)
- bed (3 wool + 3 planks)

Note: **torch does NOT belong on this list** — it's a 1×2 inventory-grid recipe.

## Related Code

- **C# side (works correctly):** `Agent.Tools/Tools/CraftItemTool.cs` — sends `{"action":"craft", "item":"torch", "count":1}`
- **JS side (needs fix):** `MineflayerAdapter/index.js` — `handleCraft` or `craftItem` handler (search for "craft" in index.js)

## Quick Test

In the MineflayerAdapter Node.js process, add debug logging before the throw and test with explicit params:
```js
// Add this before the throw at line 814:
console.log('[craft] recipesFor diagnostic', {
  itemName,
  itemId: itemEntry.id,
  itemDisplayName: itemEntry.displayName,
  recipesLength: recipes?.length,
  inventory: bot.inventory.items().map(i => ({ name: i.name, count: i.count })),
});

// Also test all three craftingTable modes:
const r1 = bot.recipesFor(torchId, null, 1, false); // non-table only
const r2 = bot.recipesFor(torchId, null, 1, true);  // table only
const r3 = bot.recipesFor(torchId, null, 1, null);  // no filter
console.log('Torch recipes — noTable:', r1?.length, 'table:', r2?.length, 'all:', r3?.length);
```

## Tasks Created

| Key | Title | Priority | Link |
|-----|-------|----------|------|
| TSK-0034 | Diagnose & fix torch recipe failure in Mineflayer craft handler | High | Direct fix for this issue |
| TSK-0035 | Add recipe/prerequisite diagnostics to craft error handling | Medium | Structured diagnostics on craft failure |
| TSK-0036 | Deepen Mineflayer adapter world observation payload | Medium | Richer world representation (audit Finding 2) |
| TSK-0037 | Add action progress telemetry to Mineflayer adapter events | Medium | Action lifecycle tracking (audit Finding 6) |
| TSK-0038 | Replace chat regex filter with structured message classification | Medium | Structured chat classification (audit Finding 3) |
| TSK-0039 | Refactor Mineflayer adapter monolith into bounded modules | Low | Split index.js into modules (audit Finding 5) |
