# ladder

item_id: ladder
display_name: Ladder
source_blocks: oak_log, birch_log, spruce_log
requires_smelting: false
min_harvest_level: 0

## Notes

A ladder is crafted from 7 sticks (H-shape arrangement) → 3 ladders.
Ladders are climbable by holding the forward key against them.

**Gather strategy:**
- Mine any wood log → craft into planks → craft sticks (2 planks → 4 sticks)
- Craft 7 sticks → 3 ladders

**Phase 4b limitation:** Crafting is manual; CraftItemTool is Phase 5.

**Placement note:** Ladders must be placed against a solid block face.
They automatically stack vertically when placed against the same wall.
The agent should place ladders from bottom to top, one per Y level.

**Common uses in blueprints:**
- Vertical access in wizard's tower (ladder up the center or along the wall)
- Mine shaft access
- Decorative wall detailing
