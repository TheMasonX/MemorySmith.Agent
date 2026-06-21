# stone-slab

item_id: stone_slab
display_name: Stone Slab
source_blocks: stone, cobblestone
requires_smelting: true
min_harvest_level: 1

## Notes

Stone slabs are crafted from 3 stone blocks → 6 stone slabs. They are smooth,
grey slabs useful for flooring, paths, and roofing.

**Gather strategy:**
1. Mine cobblestone → smelt to stone (FurnaceTool, Phase 4b)
2. Craft 3 stone → 6 stone_slab (CraftItemTool, Phase 5)

**Placement note:** Like oak_slabs, stone slabs can be placed in the top or
bottom half of a block. Mineflayer places bottom-half by default.

**Common uses in blueprints:**
- Smooth stone flooring
- Garden paths and courtyard paving
- Roofing material for stone builds

**Related items:** `stone` (raw material), `stone_stairs`, `cobblestone_slab`,
`smooth_stone_slab`
