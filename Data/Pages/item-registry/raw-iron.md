# raw-iron

item_id: raw_iron
display_name: Raw Iron
source_blocks: iron_ore, deepslate_iron_ore
requires_smelting: true
min_harvest_level: 2

## Notes

Raw iron is obtained by mining iron ore with a stone pickaxe or better
(min_harvest_level: 2). Since Java 1.17, iron ore drops raw iron instead of
the ore block itself. Smelt raw iron in a furnace to obtain iron ingots.

**Gather strategy:**
1. Mine `iron_ore` with a stone pickaxe or better → drops raw_iron
2. Smelt raw_iron → iron_ingot (FurnaceTool, Phase 4b)

**Fortune effect:** Fortune enchantment increases the number of raw_iron
dropped per ore block (up to 4× at Fortune III).

**Best mining levels:** Iron generates most commonly around Y=16 and Y=232.
Deepslate iron ore generates below Y=0.

**Related items:** `iron_ore` (source block), `iron_ingot` (smelted product),
`raw_iron_block` (compact storage, 9 raw_iron)
