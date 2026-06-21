# copper-ingot

item_id: copper_ingot
display_name: Copper Ingot
source_blocks: copper_ore, deepslate_copper_ore, raw_copper
requires_smelting: true
min_harvest_level: 2

## Notes

Copper ingots are obtained by smelting raw copper in a furnace. They are used
for decorative oxidized building blocks, lightning rods, and spyglasses.

**Gather strategy:**
1. Mine `copper_ore` with a stone pickaxe or better → `raw_copper`
2. Smelt 1 raw_copper → 1 copper_ingot (requires FurnaceTool, Phase 4b)

**Crafting uses:**
- 3 copper_ingots → 1 lightning_rod
- 2 copper_ingots + 1 amethyst_shard → 1 spyglass
- 9 copper_ingots → 1 copper_block (oxidizes over time)
- Waxing prevents oxidation

**Related items:** `raw_copper` (raw material), `copper_ore` (source block),
`copper_block`, `lightning_rod`, `spyglass`
