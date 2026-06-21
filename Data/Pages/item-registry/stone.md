# stone

item_id: stone
display_name: Stone
source_blocks: stone, cobblestone
requires_smelting: true
min_harvest_level: 1

## Notes

Stone is obtained by smelting cobblestone in a furnace. It is the base material
for stone_bricks (crafted 4 stone → 4 stone_bricks) and stone tools.

**Gather strategy:**
1. Mine `cobblestone` with any pickaxe
2. Smelt cobblestone → stone in a furnace (requires FurnaceTool, Phase 4b)

**Smelting efficiency:** 1 cobblestone + 1 fuel → 1 stone.
Regular stone drops cobblestone when broken without Silk Touch —
always smelt cobblestone to obtain stone blocks.

**Common uses in blueprints:**
- Stone brick crafting (castle, wizard's tower)
- Stone slabs, stone stairs, stone pressure plates
- Stone tools (pickaxe, axe, shovel, hoe — more durable than wood)
- Redstone component base (stone button, stone pressure plate)

**Related items:** `cobblestone` (raw material), `stone_bricks` (crafted product),
`smooth_stone` (smelted variant from stone)
