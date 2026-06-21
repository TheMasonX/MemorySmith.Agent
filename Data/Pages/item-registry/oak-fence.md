# oak-fence

item_id: oak_fence
display_name: Oak Fence
source_blocks: oak_log, birch_log, spruce_log
requires_smelting: false
min_harvest_level: 0

## Notes

An oak fence is crafted from 4 oak planks + 2 sticks → 3 fences.
They connect to adjacent fences, fence gates, and solid blocks automatically.

**Gather strategy:**
- Mine any wood log for planks and sticks
- Craft: 4 planks + 2 sticks → 3 fences

**Phase 4b limitation:** Crafting is manual; CraftItemTool is Phase 5.

**Placement note:** Fences automatically connect to neighbouring fences, fence gates,
and solid blocks. They are 1.5 blocks tall, preventing mobs (and the player) from
jumping over without a gate.

**Common uses in blueprints:**
- Farm perimeter (farm blueprint: ~24 fences around 9×7 plot)
- Animal pen boundaries
- Decorative railings on castle battlements
