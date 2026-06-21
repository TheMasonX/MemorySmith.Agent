# wheat

item_id: wheat
display_name: Wheat
source_blocks: wheat, wheat_seeds
requires_smelting: false
min_harvest_level: 0

## Notes

Wheat is the crop harvested from mature wheat plants (grown from wheat_seeds
on farmland). It is used for crafting bread, hay bales, cakes, and breeding
cows and sheep.

**Gather strategy:**
1. Till farmland with a hoe
2. Plant wheat_seeds on farmland
3. Wait for 8 growth stages (bone meal can speed this)
4. Harvest mature wheat → 1 wheat + 0–3 wheat_seeds (for replanting)

**Crafting uses:**
- 3 wheat → 1 bread
- 9 wheat → 1 hay_bale
- 2 wheat + 1 milk_bucket + 1 sugar + 1 egg → 1 cake
- 3 wheat + 3 hay_bale → (used for horse feeding, not a recipe)

**Common uses in blueprints:**
- Bread production (sustainable food source)
- Hay bale crafting (farm blueprint: 4 hay bales = 36 wheat)

**Related items:** `wheat_seeds`, `bread`, `hay_bale`, `bone_meal`
