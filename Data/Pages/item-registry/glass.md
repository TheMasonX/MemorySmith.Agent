# glass

item_id: glass
display_name: Glass
source_blocks: sand, red_sand
requires_smelting: true
min_harvest_level: 0

## Notes

Glass is obtained by smelting sand in a furnace. It is a transparent block that
does not drop when broken (unless using Silk Touch).

**Gather strategy:**
1. Mine `sand` from beaches, deserts, or rivers
2. Smelt 1 sand → 1 glass in a furnace (requires FurnaceTool, Phase 4b)

**Crafting:** 6 glass → 16 glass_pane (at a crafting table).

**Phase 4b limitations:** Smelting and crafting are manual.
FurnaceTool is Phase 4b; CraftItemTool is Phase 5.

**Placement note:** Glass does not block line of sight. Mobs cannot suffocate
through glass. It does not reduce light passing through.

**Related items:** `sand` (raw material), `glass_pane` (crafted from glass),
`tinted_glass` (light-blocking variant)
