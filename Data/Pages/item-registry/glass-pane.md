# glass-pane

item_id: glass_pane
display_name: Glass Pane
source_blocks: sand, gravel
requires_smelting: true
min_harvest_level: 0

## Notes

Glass panes require a two-step process: smelt sand into glass blocks, then craft
glass blocks into panes (6 glass → 16 panes).

**Gather strategy:**
1. Mine `sand` (beaches, deserts) — min_harvest_level 0 (bare hands)
2. Smelt sand in a furnace → `glass` (requires FurnaceTool, Phase 4b)
3. Craft 6 glass → 16 glass_pane

**Phase 4b limitation:** FurnaceTool is not yet implemented. Glass panes must be
obtained manually before running a build goal that requires them.

**Common uses in blueprints:**
- Windows (small-house: 6 glass_pane in Y=2 layer)
- Decorative elements

**requires_smelting: true** — GenericGatherGoal.IsComplete checks inventory["glass_pane"],
not inventory["sand"].
