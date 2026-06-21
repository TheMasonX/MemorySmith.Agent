# wheat-seeds

item_id: wheat_seeds
display_name: Wheat Seeds
source_blocks: grass_block, tall_grass, wheat
requires_smelting: false
min_harvest_level: 0

## Notes

Wheat seeds are obtained by breaking tall grass or harvesting mature wheat.
They are planted on farmland to grow into wheat.

**Gather strategy:**
- Break tall_grass in any grassy biome → chance to drop 0–1 wheat_seeds
- Harvest mature wheat → drops 1 wheat + 0–3 wheat_seeds (bonus seeds for replanting)

**Planting:** Right-click on tilled farmland with wheat_seeds to plant.
Requires light level ≥ 8 and water within 4 blocks (hydrated farmland).

**Phase 4b limitation:** Breaking tall_grass is Phase 4b (manual or agent action).
Growing wheat requires time (random tick growth stages 0–7). The agent should
plant seeds, wait for maturity, then harvest.

**Common uses in blueprints:**
- Farm field planting (farm blueprint: 8 seeds for 3×3 plots on each side of water)
- Bread crafting ingredient (3 wheat → 1 bread)
- Breeding wheat for animal husbandry
