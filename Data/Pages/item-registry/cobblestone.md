# cobblestone

item_id: cobblestone
display_name: Cobblestone
source_blocks: stone, cobblestone, deepslate, cobbled_deepslate
requires_smelting: false
min_harvest_level: 1

## Notes

Stone drops cobblestone when mined without Silk Touch. Cobblestone can also be
found naturally or mined directly. The agent should target `stone` or `cobblestone`
blocks; deepslate variants yield `cobbled_deepslate` in 1.18+.

**Tool requirement:** Wooden pickaxe or better (min_harvest_level: 1).
Bare-hand mining is possible but very slow; always use a pickaxe.

**Common uses in MemorySmith.Agent blueprints:**
- Floor blocks (small-house, shelter blueprints)
- Wall reinforcement
- Crafting: furnace, stone tools, stone slabs
