# raw-copper

item_id: raw_copper
display_name: Raw Copper
source_blocks: copper_ore, deepslate_copper_ore
requires_smelting: true
min_harvest_level: 2

## Notes

Raw copper is obtained by mining copper ore with a stone pickaxe or better
(min_harvest_level: 2). Since Java 1.17, copper ore drops raw copper. Smelt
raw copper in a furnace to obtain copper ingots.

**Gather strategy:**
1. Mine `copper_ore` with a stone pickaxe or better → drops raw_copper
2. Smelt raw_copper → copper_ingot (FurnaceTool, Phase 4b)

**Fortune effect:** Fortune increases raw_copper drop count. Copper generates
in large veins, making it very abundant.

**Best mining levels:** Between Y=0 and Y=96, most common around Y=48.

**Related items:** `copper_ore` (source block), `copper_ingot` (smelted product),
`raw_copper_block` (compact storage), `lightning_rod`, `spyglass`
