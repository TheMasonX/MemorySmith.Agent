# grass-block

item_id: grass_block
display_name: Grass Block
source_blocks: grass_block
requires_smelting: false
min_harvest_level: 0

## Notes

Grass blocks form the surface layer of most overworld biomes. They spread to
adjacent dirt blocks in light level ≥ 9. Breaking a grass block without Silk
Touch drops dirt instead.

**Gather strategy:**
- Use Silk Touch tool → obtain grass_block directly
- Without Silk Touch → obtain dirt (see item-registry/dirt)
- Grass spreads naturally from existing grass blocks to adjacent dirt

**Common uses in blueprints:**
- Natural landscaping and terrain blending
- Passive mob spawning surface
- Bone meal target for tall grass/flowers

**Related items:** `dirt`, `tall_grass`, `wheat_seeds` (breaking tall grass drops seeds)
