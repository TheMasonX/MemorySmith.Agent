# oak-slab

item_id: oak_slab
display_name: Oak Slab
source_blocks: oak_log, birch_log, spruce_log
requires_smelting: false
min_harvest_level: 0

## Notes

Oak slabs are crafted from oak planks: 3 planks → 6 slabs.
They are used as a roof material in the small-house blueprint (63 slabs = 32 planks).

**Gather strategy:**
- Mine oak_log from trees
- Craft: 1 oak_log → 4 oak_planks → craft 3 planks → 6 oak_slab

**Phase 4b limitation:** Crafting is manual; CraftItemTool is Phase 5.

**Placement note:** PlaceBlock emits a standard placement call; Mineflayer places
the slab as a bottom-half slab by default. Top-half placement requires right-clicking
the top face of the block below — handled by the Mineflayer adapter automatically
when the reference block is directly below the target.

**Common uses in blueprints:**
- Roof of small-house (Y=4 layer: 63 slabs over 9×7 footprint)
- Stairs, decorative ledges
