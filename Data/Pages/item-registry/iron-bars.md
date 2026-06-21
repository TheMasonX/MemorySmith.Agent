# iron-bars

item_id: iron_bars
display_name: Iron Bars
source_blocks: iron_ore, deepslate_iron_ore
requires_smelting: true
min_harvest_level: 1

## Notes

Iron bars are crafted from 6 iron nuggets (2×3 arrangement) → 16 iron bars.
Each iron ingot smelts into 9 iron nuggets. Alternatively, 6 iron ingots → 16 bars
(the standard recipe before 1.17+ nugget recipes).

**Gather strategy:**
1. Mine iron_ore or deepslate_iron_ore with a stone pickaxe or better
2. Smelt iron_ore → iron_ingot in a furnace (requires FurnaceTool, Phase 4b)
3. Craft 6 iron ingots → 16 iron_bars

**Phase 4b limitation:** Smelting and crafting are manual.
FurnaceTool is Phase 4b; CraftItemTool is Phase 5.

**Placement note:** Iron bars function like glass panes — they connect to adjacent
solid blocks and iron bars. They have a 1-pixel-thick appearance but provide
solid collision. Mobs can see through them but cannot pass through.

**Common uses in blueprints:**
- Castle window grates (castle blueprint)
- Prison cell bars
- Decorative fencing (more durable than wood)
