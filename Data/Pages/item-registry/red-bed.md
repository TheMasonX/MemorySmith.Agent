# red-bed

item_id: red_bed
display_name: Red Bed
source_blocks: oak_log, birch_log, spruce_log
requires_smelting: false
min_harvest_level: 0

## Notes

A red bed is crafted from 3 oak planks + 3 red/white wool → 1 bed.
Any colour wool works; the item name reflects the wool colour used.

**Gather strategy:**
- Mine wood logs for planks (3 planks needed)
- Collect wool from sheep (3 wool needed)
- Craft the bed at a crafting table

**Phase 4b limitation:** Crafting and wool collection are manual.

**Placement note:** A bed occupies 2 blocks: head and foot. Mineflayer places both
when the head block (`H` in the blueprint legend) is placed. The foot position
(`F` in the legend) maps to null and is skipped by BlueprintParser — Mineflayer
places it automatically.

**Bed direction:** The head block faces the direction the bot looks when placing.
For the small-house blueprint, the bed head is at (3, 2, 3) with the foot at
(4, 2, 3) — the bot should face east (toward X+) when placing.

**Common uses in blueprints:**
- Spawn point anchor (sleeping skips night, sets respawn)
- Required comfort item in small-house blueprint at Y=2
