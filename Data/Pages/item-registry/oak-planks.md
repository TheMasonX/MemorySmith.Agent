# oak-planks

item_id: oak_planks
display_name: Oak Planks
source_blocks: oak_log, birch_log, spruce_log, dark_oak_log, jungle_log, acacia_log, cherry_log
requires_smelting: false
min_harvest_level: 0

## Notes

Oak planks are crafted from any type of log: 1 log yields 4 planks.
The agent should mine oak (or any wood) logs and then craft them into planks.

**Gather strategy:** Mine oak_log from oak trees, then craft 4 planks per log.
Phase 4b only mines the source blocks. CraftItemTool (Phase 5) will handle crafting.

**Common uses:**
- Wall material (small-house blueprint: 70 planks)
- Crafting base for slabs, doors, stairs, chests, crafting table, bed frame
- 1 log → 4 planks → 2 slabs (or 6 slabs per 3 planks)

**Inventory key:** `oak_planks` (the crafted product checked by IsComplete).
