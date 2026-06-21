# oak-fence-gate

item_id: oak_fence_gate
display_name: Oak Fence Gate
source_blocks: oak_log, birch_log, spruce_log
requires_smelting: false
min_harvest_level: 0

## Notes

An oak fence gate is crafted from 2 oak planks + 4 sticks → 1 fence gate.
It acts as a door for fence perimeters — opens by right-clicking.

**Gather strategy:**
- Mine any wood log for planks and sticks
- Craft: 2 planks + 4 sticks → 1 fence gate

**Phase 4b limitation:** Crafting is manual; CraftItemTool is Phase 5.

**Placement note:** Place the fence gate in line with the fence row. When closed,
it matches fence height (1.5 blocks). When open, it lies flat. Mineflayer places
it in the closed position by default.

**Common uses in blueprints:**
- Farm entrance/exit (farm blueprint: centered on south or north fence wall)
- Animal pen access point
