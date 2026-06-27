---
id: small-house
name: Small Survival House
tags: house, starter, survival, build
dimensions: 9x5x7
materials: cobblestone x 63, oak_planks x 70, oak_log x 6, glass_pane x 6, oak_door x 1, torch x 2, crafting_table x 1, chest x 2, oak_slab x 63, red_bed x 1
description: A compact 9x5x7 survival starter home with door, windows, torches, crafting bench, double chest, and bed.
---

# Small Survival House

A compact, fully-functional survival starter home built from oak planks and cobblestone.
Features a front door (south face), glass-pane windows, four torches for lighting,
a crafting bench, double chest storage, and a bed for setting your spawn point.

**Footprint:** 9 × 7 blocks  
**Height:** 5 blocks (1 floor + 3 interior + 1 roof slab)  
**Interior space:** 7 × 3 = 21 blocks per floor level  
**Door faces:** South (Z = 6), centered at X = 4

---

## Layers

### Y=0 (Floor — cobblestone)
CCCCCCCCC
CCCCCCCCC
CCCCCCCCC
CCCCCCCCC
CCCCCCCCC
CCCCCCCCC
CCCCCCCCC

### Y=1 (Lower walls + furniture)
LPPPPPPPL
P...H...P
P...F...P
PB......P
PX......P
PX......P
PPPPDPPPP

### Y=2 (Mid walls + windows + torches)
LPGPPPGPL
G.T...T.G
P.......P
P.......P
P.......P
G..T.T..G
PPPP.PPPP

### Y=3 (Upper walls)
LPPPPPPPL
P.......P
P.......P
P.......P
P.......P
P.......P
PPPPPPPPP

### Y=4 (Roof — oak slabs)
SSSSSSSSS
SSSSSSSSS
SSSSSSSSS
SSSSSSSSS
SSSSSSSSS
SSSSSSSSS
SSSSSSSSS

---

## Legend

- `.` = air (skip)
- `C` = cobblestone
- `P` = oak_planks
- `L` = oak_log
- `G` = glass_pane
- `D` = oak_door
- `T` = torch
- `B` = crafting_table
- `X` = chest
- `S` = oak_slab
- `H` = red_bed (head only; Mineflayer auto-places foot block facing south)
- `F` = null (bed foot auto-placed — skip)

---

## Block Placement Notes

**Coordinates** use the blueprint origin (0,0,0) = southwest bottom corner of the structure.
- X increases eastward (0 = west wall, 8 = east wall)
- Y increases upward (0 = floor, 4 = roof)
- Z increases northward from the south door face (0 = north wall, 6 = south wall)

**Interior items (Y=1):**
- Crafting table: (3, 1, 2) — northwest interior
- Double chest: (3, 1, 3) and (4, 1, 3) — place side by side for double storage
- Door: (4, 1, 6) — south-center; Mineflayer places both halves automatically

**Interior items (Y=2):**
- Torch 1: (2, 2, 2) — wall torch on west-interior side
- Bed head: (3, 2, 3) — Mineflayer places foot at (4, 2, 3) automatically
- Torch 2: (6, 2, 4) — wall torch on east-interior side

**Windows (Y=2):**
- North wall: glass_pane at (2, 2, 0) and (6, 2, 0)
- Side walls: glass_pane at (0, 2, 1) and (8, 2, 1) [west/east] and same at Z=5

**Roof:** Oak slabs placed as top-half slabs at Y=4 form the ceiling.

---

## Materials to Gather

### Directly mineable
| Material | Quantity | How to obtain |
|---|---|---|
| Cobblestone | 63 | Mine stone with any pickaxe |
| Oak Log (corner posts) | 6 | Chop oak/birch/spruce trees |

### Crafted items (must be pre-crafted before build)
| Item | Qty needed | Recipe |
|---|---|---|
| Oak Planks | 70 placed + 97 for crafting below | 1 log → 4 planks |
| Oak Slab | 63 | 3 planks → 6 slabs (32 planks → 64 slabs ✓) |
| Crafting Table | 1 | 4 planks |
| Chest | 2 | 8 planks each (16 total) |
| Oak Door | 1 | 6 planks |
| Torch | 4 | 1 stick + 1 coal (or charcoal) |
| Glass Pane | 6 | 3 sand → smelt to 3 glass → 8 glass panes |
| Red Bed | 1 | 3 planks + 3 wool |

**Raw resource requirements:**
- Oak logs: 50+ (for all planks, slabs, door, chests, crafting table, bed frame)
- Cobblestone: 63
- Sand: 3+ (for glass panes)
- Coal or charcoal: 2+ (for torches)
- Wool (any colour): 3 (for bed)

---

## Build Sequence

Execute phases in order to avoid placing blocks in mid-air:

1. **Y=0** — Lay cobblestone floor (63 blocks)
2. **Y=1** — Build lower walls + place door + place furniture (crafting table, chests)
3. **Y=2** — Build mid walls + install glass panes + place bed + place torches
4. **Y=3** — Build upper walls (no door gap at Y=3 — solid planks all around)
5. **Y=4** — Place oak slab roof
6. **Verify** — GetStatus, confirm interior light level ≥ 8

---

## Phase 4b Limitations

- Smelting (sand → glass) requires manual use of a furnace (FurnaceTool is Phase 4b).
- Crafting (logs → planks → slabs, door, chests, table, bed) requires manual crafting or a pre-stocked inventory.
  CraftItemTool is deferred to Phase 5.
- The `F` (bed foot) and door upper half are auto-placed by Mineflayer when the head/lower block is placed.
- Build origin must be set in WorldState facts before planning:
  `build:small-house:origin:x`, `build:small-house:origin:y`, `build:small-house:origin:z`