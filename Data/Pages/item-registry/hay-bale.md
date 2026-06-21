# hay-bale

item_id: hay_bale
display_name: Hay Bale
source_blocks: wheat
requires_smelting: false
min_harvest_level: 0

## Notes

A hay bale is crafted from 9 wheat (3×3 arrangement) → 1 hay bale.
Hay bales can be placed as decorative blocks or used to feed horses.

**Gather strategy:**
- Harvest mature wheat → collect wheat
- Craft 9 wheat → 1 hay_bale

**Phase 4b limitation:** Wheat farming requires planting seeds, waiting for growth,
and harvesting. CraftItemTool is Phase 5.

**Placement note:** Hay bales can be placed in any orientation. They reduce
fall damage when landed upon (like slime blocks but weaker). They also
compost into bonemeal at 85% chance in a composter.

**Common uses in blueprints:**
- Farm shelter decoration (farm blueprint: stacked hay bales at the shed)
- Animal feeding (horses, llamas)
- Composter fuel source
