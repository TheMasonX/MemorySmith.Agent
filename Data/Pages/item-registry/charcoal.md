# charcoal

item_id: charcoal
display_name: Charcoal
source_blocks: oak_log, spruce_log, birch_log, jungle_log, acacia_log, dark_oak_log, cherry_log, mangrove_log, pale_oak_log
requires_smelting: true
min_harvest_level: 0

## Notes

Charcoal is a renewable alternative to coal, obtained by smelting any type of
log in a furnace. It has the same fuel value as coal (8 items per piece) and
is used in all the same recipes — including torches.

**Gather strategy:**
1. Chop any wood log
2. Smelt 1 log → 1 charcoal in a furnace (requires FurnaceTool, Phase 4b)

**Efficiency:** 1 log smelts into 1 charcoal, which then smelts 8 items.
Using 1 log as fuel to smelt the next log yields a net gain of 7 charcoal.
Starting the chain requires an initial fuel source (planks, coal, etc.).

**Crafting:** 1 charcoal + 1 stick → 4 torches.

**Common uses:**
- Torch crafting when coal is unavailable (torch.md requires coal or charcoal)
- Furnace fuel for early-game smelting operations
- Campfire crafting

**Related items:** `coal` (non-renewable alternative), `torch` (crafted from
charcoal + stick), `coal_block` (no charcoal block variant exists)
