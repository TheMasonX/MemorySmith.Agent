# water-bucket

item_id: water_bucket
display_name: Water Bucket
source_blocks: water
requires_smelting: false
min_harvest_level: 0

## Notes

A water bucket is crafted from 3 iron ingots → 1 bucket, then right-click a
water source block to fill. Water source blocks can be carried and placed elsewhere.

**Gather strategy:**
1. Mine iron_ore → smelt to iron_ingot (requires FurnaceTool, Phase 4b)
2. Craft 3 iron_ingots → 1 bucket (V-shape arrangement)
3. Right-click a water source block with the bucket → water_bucket

**Phase 4b limitation:** Smelting, crafting, and bucket filling are manual.
PlaceWaterSource is a distinct action (not a standard block placement).

**Placement note:** Placing a water_bucket creates a water source block.
Water hydrates farmland within 4 blocks (Manhattan distance). The farm
blueprint uses a 1-wide water channel for crop hydration.

**Common uses in blueprints:**
- Farm irrigation channel (farm blueprint: water source in the center trench)
- Mob farming (water flows push mobs)
- Obsidian creation (water + lava source)
