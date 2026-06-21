# furnace

item_id: furnace
display_name: Furnace
source_blocks: cobblestone
requires_smelting: false
min_harvest_level: 1

## Notes

A furnace is crafted from 8 cobblestone (ring pattern, center empty) → 1 furnace.
It is used to smelt ores, cook food, and produce charcoal.

**Gather strategy:**
- Mine cobblestone (8 blocks) → craft into furnace at a crafting table

**Phase 4b limitation:** FurnaceTool covers furnace operations. Crafting the
furnace itself is manual (CraftItemTool is Phase 5).

**Placement note:** Furnaces face toward the player when placed. They require
fuel (coal, charcoal, wood, lava bucket) in the bottom slot and items to smelt
in the top slot.

**Common uses in blueprints:**
- Smelting station (converting cobblestone → stone for stone_bricks)
- Glass production (sand → glass for panes)
- Food cooking and charcoal production
- Not typically placed in blueprints as a fixed structure block, but useful
  as an interim smelting tool
