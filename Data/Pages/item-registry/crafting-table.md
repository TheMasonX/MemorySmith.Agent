# crafting-table

item_id: crafting_table
display_name: Crafting Table
source_blocks: oak_log, birch_log, spruce_log, dark_oak_log
requires_smelting: false
min_harvest_level: 0

## Notes

A crafting table is crafted from 4 oak planks (2×2 arrangement) → 1 crafting table.

**Gather strategy:** Mine any wood log, craft into 4+ planks, then craft the crafting table.

**Phase 4b limitation:** Crafting is manual; CraftItemTool is Phase 5. The agent can
mine source_blocks (oak_log etc.) and count them toward the crafting_table requirement.

**Common uses in blueprints:**
- Placed inside the small-house at (3, 1, 2) as a permanent fixture
- Enables all crafting recipes in survival mode

**Note:** Only 1 is needed in any shelter — once placed, it can be used for all
subsequent crafting without being picked up.
