# oak-stairs

item_id: oak_stairs
display_name: Oak Stairs
source_blocks: oak_log, birch_log, spruce_log
requires_smelting: false
min_harvest_level: 0

## Notes

Oak stairs are crafted from 6 oak planks (L-shape arrangement) → 4 stairs.
They allow the player (and mobs) to walk up without jumping.

**Gather strategy:**
- Mine any wood log → craft into planks
- Craft 6 planks → 4 oak stairs

**Phase 4b limitation:** Crafting is manual; CraftItemTool is Phase 5.

**Placement note:** Stairs can face four directions. The BlueprintParser uses
the placement direction from the layer context. For spiral staircases, each
stair faces the next step direction. Mineflayer adapter should handle
orientation via the bot's facing direction at placement time.

**Common uses in blueprints:**
- Spiral staircase in wizard's tower center
- Castle wall access steps
- Decorative roofing
