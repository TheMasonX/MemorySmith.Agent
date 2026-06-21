# bucket

item_id: bucket
display_name: Bucket
source_blocks: iron_ore, deepslate_iron_ore
requires_smelting: true
min_harvest_level: 2

## Notes

A bucket is crafted from 3 iron ingots (V-shape arrangement). Buckets can hold
water, lava, or milk, and are essential for farm irrigation and early-game
resource collection.

**Gather strategy:**
1. Mine iron_ore → smelt to iron_ingot (FurnaceTool, Phase 4b)
2. Craft 3 iron_ingots → 1 bucket (CraftItemTool, Phase 5)

**Usage:**
- Right-click water source → water_bucket
- Right-click lava source → lava_bucket
- Right-click cow/mooshroom → milk_bucket
- Right-click to place the liquid back as a source block

**Common uses in blueprints:**
- Farm irrigation (farm blueprint: 3 water_buckets needed)
- Creating obsidian (place water over lava source)
- Mob farming (water flows push mobs)

**Related items:** `water_bucket` (filled variant), `lava_bucket`,
`milk_bucket`, `iron_ingot`
