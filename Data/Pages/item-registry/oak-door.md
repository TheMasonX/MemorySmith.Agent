# oak-door

item_id: oak_door
display_name: Oak Door
source_blocks: oak_log, birch_log, spruce_log
requires_smelting: false
min_harvest_level: 0

## Notes

An oak door is crafted from 6 oak planks (2 wide × 3 tall) → 3 doors.

**Gather strategy:** Mine any wood log; craft into planks; craft the door.

**Phase 4b limitation:** Crafting is manual; CraftItemTool is Phase 5.

**Placement note:** Placing an oak door via Mineflayer places both halves (lower + upper)
in a single call. The blueprint grid marks only the lower half (`D` at Y=1); the upper
half at Y=2 is auto-placed. The BlueprintParser marks Y=2 door position as air to avoid
double-placement.

**Door facing:** Mineflayer determines door facing based on the bot's current facing
direction at placement time. The agent should face south (toward Z+) before placing
the door for the small-house blueprint (south face, Z=6).

**Common uses in blueprints:**
- Entry point of small-house at (4, 1, 6) — south-center of building
