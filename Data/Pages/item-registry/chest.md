# chest

item_id: chest
display_name: Chest
source_blocks: oak_log, birch_log, spruce_log, dark_oak_log, jungle_log
requires_smelting: false
min_harvest_level: 0

## Notes

A chest is crafted from 8 oak planks (ring pattern) → 1 chest.
Two chests placed side by side automatically form a double chest (54 slots).

**Gather strategy:** Mine any wood log (8 planks = 2 logs) per chest.

**Phase 4b limitation:** Crafting is manual; CraftItemTool is Phase 5.

**Common uses in blueprints:**
- Double chest in small-house at (3, 1, 3) and (4, 1, 3) — 54 item slots combined
- Storage hub for gathered resources

**Double chest rule:** Place two single chests adjacent (same Y level, 1 block apart)
to form a double chest automatically. Mineflayer handles this via standard placeBlock calls.
