# brick-block

item_id: brick_block
display_name: Bricks
source_blocks: clay
requires_smelting: true
min_harvest_level: 1

## Notes

Brick blocks are crafted from 4 bricks (2×2 arrangement) → 1 brick block.
Each brick is obtained by smelting a clay ball in a furnace.

**Gather strategy:**
1. Mine clay → 4 clay balls per block
2. Smelt clay balls → bricks (FurnaceTool, Phase 4b)
3. Craft 4 bricks → 1 brick_block (CraftItemTool, Phase 5)

**Phase 4b limitations:** Full supply chain is multi-step and manual.

**Common uses in blueprints:**
- Decorative building material (chimneys, accent walls)
- Village church/buildings (brick roof accents)
- Brick slabs, brick stairs, brick walls

**Related items:** `clay` (raw), `brick` (smelted intermediate), `brick_stairs`,
`brick_slab`, `brick_wall`
