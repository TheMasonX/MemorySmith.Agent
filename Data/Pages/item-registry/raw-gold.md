# raw-gold

item_id: raw_gold
display_name: Raw Gold
source_blocks: gold_ore, deepslate_gold_ore
requires_smelting: true
min_harvest_level: 3

## Notes

Raw gold is obtained by mining gold ore with an iron pickaxe or better
(min_harvest_level: 3). Since Java 1.17, gold ore drops raw gold instead of
the ore block. Smelt raw gold in a furnace to obtain gold ingots.

**Gather strategy:**
1. Mine `gold_ore` with an iron pickaxe or better → drops raw_gold
2. Smelt raw_gold → gold_ingot (FurnaceTool, Phase 4b)

**Fortune effect:** Fortune increases raw_gold drop count.

**Best mining levels:** Gold generates most commonly below Y=-16 in the
badlands biome (where it's also more abundant at higher levels).

**Related items:** `gold_ore` (source block), `gold_ingot` (smelted product),
`raw_gold_block` (compact storage)
