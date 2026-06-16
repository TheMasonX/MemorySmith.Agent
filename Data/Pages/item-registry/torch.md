# torch

item_id: torch
display_name: Torch
source_blocks: coal_ore, deepslate_coal_ore, oak_log, birch_log, spruce_log
requires_smelting: false
min_harvest_level: 0

## Notes

Torches are crafted from 1 stick + 1 coal/charcoal → 4 torches. Sticks come from
planks (2 planks → 4 sticks). Charcoal comes from smelting any log.

**Gather strategy:**
- Mine `coal_ore` for coal (preferred), OR
- Smelt a log → charcoal (requires FurnaceTool, Phase 4b)
- Craft: 1 stick + 1 coal → 4 torches

**Phase 4b limitation:** Crafting is manual; CraftItemTool is Phase 5.

**Light level:** Torches emit light level 14, preventing mob spawning in a 7-block radius.

**Common uses in blueprints:**
- Interior lighting (small-house: 2 torches at Y=2)
- Mine corridors, cave exploration markers
