# TSK-0126: Verification Pass + Band-Aid Reversion (DEFERRED)

**Status:** Backlog
**Priority:** Medium
**Sprint:** 54 (deferred from Sprint 53)
**Depends on:** TSK-0125 (per-block tracking stable and runtime-validated)
**Council:** [build-checkpoint-per-block-council-2026-06-27.md](../council/build-checkpoint-per-block-council-2026-06-27.md)

## Summary

After per-block tracking (TSK-0125) is proven correct at runtime, remove the TSK-0124 safety nets:
1. Revert JS NATURAL_TERRAIN whitelist → simple "mine if different"
2. Revert C# unconditional checkpoint-advance-on-skip → per-block skip isolation
3. Implement verification pass to find and fill remaining gaps

## Background

The NATURAL_TERRAIN whitelist and unconditional skip-advance were added as band-aids for the linear checkpoint's inability to distinguish "this block was already placed" from "this position has natural terrain." Per-block tracking eliminates this ambiguity by tracking each position independently.

## Preconditions (Must Be True Before Starting)

- [ ] TSK-0125 merged and deployed
- [ ] At least one successful `small-house` build with per-block tracking
- [ ] Zero instances of "already placed" checks firing for non-"placed" blocks
- [ ] Spatial ordering (TSK-0179) evaluated — may eliminate need for verification pass

## Subtasks

### TSK-0126.1 — Evaluate Spatial Ordering
Evaluate whether TSK-0179 (Y→Z→X with neighbor-precedence) eliminates the need for verification pass. If roof slabs can be placed edge-first using wall references, interior gaps are impossible. If TSK-0179 solves this, skip TSK-0126.3.

### TSK-0126.2 — Revert NATURAL_TERRAIN Whitelist
Replace NATURAL_TERRAIN whitelist with simple "mine if different" check in `index.js` place handler. Keep UNBREAKABLE blacklist (bedrock, barrier) as safety net.

### TSK-0126.3 — Implement Verification Pass
After primary build pass completes (all blocks placed or skipped):
1. Scan build area using `bot.blockAt()` for each blueprint position
2. Compare against expected material
3. Generate PlaceBlock actions for mismatched positions
4. Run up to 3 passes until no progress

### TSK-0126.4 — Revert C# Skip-Handler Band-Aid
Restore per-reason skip handling:
- Bot-position skip: mark "skipped", retry on next replan (bot moved)
- Terrain-occupied: mark "skipped", retry in verification pass
- No-reference: mark "skipped", retry when neighbors placed

## References
- [Council Report](../council/build-checkpoint-per-block-council-2026-06-27.md)
- `MineflayerAdapter/index.js:769-804` — NATURAL_TERRAIN whitelist (to revert)
- `WebUI.Blazor/AgentBackgroundService.cs:699-713` — skip handler (to revert)
- TSK-0179 — Spatial Ordering
