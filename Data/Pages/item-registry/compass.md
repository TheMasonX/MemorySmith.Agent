# compass

item_id: compass
display_name: Compass
source_blocks: iron_ingot, redstone
requires_smelting: true
min_harvest_level: 2

## Notes

A compass is crafted from 4 iron_ingots + 1 redstone. It points to the
world spawn point. Combined with paper, it creates an empty map.

**Gather strategy:**
1. Smelt iron_ore → iron_ingot; mine redstone_ore → redstone
2. Craft 4 iron_ingots + 1 redstone → 1 compass

**Uses:**
- Navigation (always points to world spawn)
- Map crafting (8 paper + 1 compass → empty_map)
- Lodestone compass (compass + lodestone → points to lodestone location)

**Related items:** `iron_ingot`, `redstone`, `map`, `lodestone`
