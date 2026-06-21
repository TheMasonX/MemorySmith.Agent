# gold-ingot

item_id: gold_ingot
display_name: Gold Ingot
source_blocks: gold_ore, deepslate_gold_ore, raw_gold
requires_smelting: true
min_harvest_level: 3

## Notes

Gold ingots are obtained by smelting raw gold in a furnace. Gold tools are
fast but have low durability. Gold is primarily used for netherite upgrading,
powered rails, clocks, and golden apples.

**Gather strategy:**
1. Mine `gold_ore` with an iron pickaxe or better → `raw_gold`
2. Smelt 1 raw_gold → 1 gold_ingot (requires FurnaceTool, Phase 4b)

**Crafting uses:**
- 4 gold_ingots + 1 apple → golden_apple
- 4 gold_ingots + 1 netherite_scrap → netherite_ingot (smithing table)
- 6 gold_ingots → 24 gold_nuggets (for powered rails)
- 9 gold_ingots → 1 gold_block
- Clock crafting (4 gold_ingots + 1 redstone)

**Related items:** `raw_gold` (raw material), `gold_ore` (source block),
`gold_nugget`, `golden_apple`, `netherite_ingot`
