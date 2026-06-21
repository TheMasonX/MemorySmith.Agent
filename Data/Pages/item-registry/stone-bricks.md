# stone-bricks

item_id: stone_bricks
display_name: Stone Bricks
source_blocks: stone, cobblestone
requires_smelting: false
min_harvest_level: 1

## Notes

Stone bricks are crafted from 4 stone blocks (2×2 arrangement) → 4 stone bricks.
Stone is obtained by smelting cobblestone in a furnace.

**Gather strategy:**
1. Mine `cobblestone` with a pickaxe
2. Smelt cobblestone → `stone` (requires FurnaceTool, Phase 4b)
3. Craft 4 stone → 4 stone_bricks

**Phase 4b limitation:** Smelting cobblestone into stone requires a furnace.
Crafting stone bricks from stone is manual; CraftItemTool is Phase 5.

**Alternative:** Stone bricks can also be found naturally in strongholds, villages,
and ocean ruins — the agent could loot these structures instead of crafting.

**Common uses in blueprints:**
- Primary wall material for castle blueprint
- Primary wall material for wizard's tower blueprint
- Flooring, pillars, decorative trim
