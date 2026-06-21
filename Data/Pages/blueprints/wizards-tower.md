---
id: wizards-tower
name: Wizard's Tower
tags: tower, magical, build, enchanting, medieval
dimensions: 9x10x9
materials: stone_bricks x 201, spruce_planks x 63, oak_stairs x 7, bookshelf x 22, enchanting_table x 1, oak_door x 1, torch x 6, glass_pane x 8, oak_slab x 8, ladder x 7, spruce_log x 8
description: A 9x9 tall wizard's tower with 8 habitable levels, spiral staircase, enchanting chamber, library, and spire roof.
---

# Wizard's Tower

A tall, Gothic-style wizard's tower built from stone bricks with spruce-wood accents.
Nine levels include a ground-floor entry, storage, living quarters, library,
enchanting chamber, alchemy lab, rooftop observatory, and a pointed spire.
A spiral staircase winds up through the center core from ground floor to observatory.

**Footprint:** 9 × 9 blocks  
**Height:** 10 blocks (ground to spire tip)  
**Interior:** 7 × 7 usable space per level (1-block-thick stone brick walls)  
**Spiral staircase:** Oak stairs at center column (X=4, Z=4)  
**Door faces:** South (Z = 8), centered at X = 4  
**Spire height:** Y=9 peak (1-block stone brick tip)

---

## Layers

### Y=0 (Foundation — stone bricks)
SSSSSSSSS
SSSSSSSSS
SSSSSSSSS
SSSSSSSSS
SSSSSSSSS
SSSSSSSSS
SSSSSSSSS
SSSSSSSSS
SSSSSSSSS

### Y=1 (Ground floor — entry + spiral stair start)
SSSSSSSSS
S.......S
S.......S
S.......S
S...R...S
S.......S
S.......S
S.......S
SSSSSDSSS

### Y=2 (Storage level — chests + crafting + stair)
SSSSSSSSS
S..C....S
S.......S
S.......S
S...R...S
S..X.X..S
S.......S
S.......S
SSSSSSSSS

### Y=3 (Living quarters — bed + torches + stair)
SSSSSSSSS
S..T....S
S.......S
S.......S
S...R...S
S.......S
S.H.....S
S..T....S
SSSSSSSSS

### Y=4 (Library — full bookshelves with stair passage)
SSSSSSSSS
SBBBBBBBS
SBBBBBBBS
SBBBBBBBS
SBBBRBBBS
SBBBBBBBS
SBBBBBBBS
SBBBBBBBS
SSSSSSSSS

### Y=5 (Enchanting chamber — table + bookshelves + stair)
SSSSSSSSS
SBBBBBBBS
SB.....BS
SBE....BS
SB..R..BS
SB.....BS
SBBBBBBBS
SBBBBBBBS
SSSSSSSSS

### Y=6 (Alchemy lab — brewing stand area + stair)
SSSSSSSSS
S..T....S
S.......S
S.......S
S...R...S
S.......S
S.......S
S..T....S
SSSSSSSSS

### Y=7 (Observatory — open walls with glass + stair)
SGSSSSSGS
S..T....S
S.......S
S.......S
S...R...S
S.......S
S.......S
S..T....S
SGSSSSSGS

### Y=8 (Roof — spruce planks with stone brick border)
SSSSSSSSS
SPPPPPPPP
SPPPPPPPP
SPPPPPPPP
SPPPPPPPP
SPPPPPPPP
SPPPPPPPP
SPPPPPPPP
SSSSSSSSS

### Y=9 (Spire tip — single stone brick at center)
.........
.........
.........
.........
....S....
.........
.........
.........
.........

---

## Legend

- `.` = air (skip)
- `S` = stone_bricks
- `P` = spruce_planks
- `R` = oak_stairs (spiral staircase)
- `B` = bookshelf
- `E` = enchanting_table
- `D` = oak_door
- `T` = torch
- `G` = glass_pane
- `X` = chest
- `C` = crafting_table
- `H` = red_bed

**Blank spaces** inside the `S` wall ring are air (interior rooms).

---

## Block Placement Notes

**Coordinates** use the blueprint origin (0,0,0) = southwest bottom corner of the structure.
- X increases eastward (0 = west wall, 8 = east wall)
- Y increases upward (0 = foundation, 9 = spire tip)
- Z increases northward (0 = north wall, 8 = south wall)

**Spiral staircase (center column X=4, Z=4):**
A central oak stair column winds upward from Y=1 to Y=7.
Each floor has one stair at the center (`R` marker), creating vertical access:
- Y=1: (4,1,4) facing south
- Y=2: (4,2,4) facing west
- Y=3: (4,3,4) facing north
- Y=4: (4,4,4) facing east
- Y=5: (4,5,4) facing south
- Y=6: (4,6,4) facing west
- Y=7: (4,7,4) facing north
- The staircase provides access to all floors from ground floor to observatory
- Only the stair block is placed; Mineflayer does not require support blocks

**Interior items per floor:**
- **Y=1 (Entry):** Oak_door at (4, 1, 8) — south-center, auto-places upper half.
  Stair start at (4,1,4). Entry hall is otherwise empty.
- **Y=2 (Storage):** Crafting_table at (1, 2, 1). Double chest at (3, 2, 3) and (4, 2, 3).
  Stair at (4,2,4).
- **Y=3 (Living):** Red_bed head at (2, 3, 6) with foot at (3, 3, 6) — facing east.
  Torches at (1, 3, 1) and (7, 3, 7). Stair at (4,3,4).
- **Y=4 (Library):** Full bookshelf ring — bookshelves line all 4 walls
  (X=1..7, Z=1..7), with the stair passage at (4,4,4). ~24 bookshelves total.
- **Y=5 (Enchanting):** Enchanting_table at (3, 5, 3) — offset from center to
  leave room for the spiral stair at (4,5,4). Bookshelves on north (Z=1),
  south (Z=6, Z=7), and side walls provide ~22 bookshelves for max level-30
  enchanting. The 1-block gap around the table allows access from any side.
- **Y=6 (Alchemy):** Open room. Torches at (1,6,1) and (7,6,7).
  Brewing stand can be placed at (3,6,3) in future phases.
  Stair at (4,6,4).
- **Y=7 (Observatory):** Glass_pane at (0,7,1), (0,7,7), (8,7,1), (8,7,7).
  Torches at (1,7,1) and (7,7,7). Open to the sky. Stair at (4,7,4).
- **Y=8 (Roof):** Spruce_planks form the roof base (63 planks across the full 9×9).
  Stone brick border at north and south edges (Z=0, Z=8).
- **Y=9 (Spire):** Single stone_brick at (4, 9, 4) as the spire tip.

**Ladders (Y=1–7, along north interior wall):**
Ladder column at (4, 1, 0) through (4, 7, 0) — placed against the north
stone_brick wall. Provides backup vertical access alongside the spiral staircase.
Each Y level gets one ladder block. Install from bottom to top.

**Windows:**
- Y=2, Y=3, Y=6: One glass_pane centered on each face at those Y levels
  (X=4, Z=0), (X=4, Z=8), (X=0, Z=4), (X=8, Z=4)
- Y=7 (Observatory): Glass panes at corner window positions (0,7,1), (0,7,7),
  (8,7,1), (8,7,7) for panoramic views

---

## Materials to Gather

### Directly mineable
| Material | Quantity | How to obtain |
|---|---|---|
| Stone/Cobblestone | 201+ | Mine stone with pickaxe → smelt for bricks |
| Spruce Log | 20+ | Chop spruce trees (taiga biomes) |

### Smelted / crafted items
| Item | Qty needed | Recipe |
|---|---|---|
| Stone Bricks | 201 | 4 stone → 4 bricks (201 bricks = 201 stone smelted) |
| Spruce Planks | 63 | 1 spruce log → 4 planks (16 logs → 64 planks) |
| Oak Stairs | 7 | 6 planks → 4 stairs (11 planks → 7 stairs) |
| Bookshelf | 22 | 6 planks + 3 books each (132 planks + 66 books) |
| Enchanting Table | 1 | 4 obsidian + 2 diamonds + 1 book |
| Oak Slab | 8 | 3 planks → 6 slabs (4 planks → 8 slabs) |
| Glass Pane | 8 | 6 glass → 16 panes (3 sand smelted) |
| Oak Door | 1 | 6 planks → 3 doors |
| Torch | 6 | 1 stick + 1 coal → 4 torches (2 coal) |
| Ladder | 7 | 7 sticks → 3 ladders (21 sticks → 9 ladders, 7 used) |
| Chest | 2 | 8 planks each |
| Crafting Table | 1 | 4 planks |
| Red Bed | 1 | 3 planks + 3 wool |
| Furnace | 1 | 8 cobblestone (for smelting) |

### Raw resource requirements
- Cobblestone / stone: ~201+ (for stone bricks)
- Spruce logs: ~16 (for roof planks)
- Oak logs: ~60+ (for stairs, bookshelves, door, slabs, chests, ladders, tools)
- Diamonds: 2 (for enchanting table)
- Obsidian: 4 (for enchanting table — requires diamond pickaxe)
- Sand: 3 (for glass panes)
- Sugar cane: ~198 (for 66 books = 198 paper)
- Leather: 22 (for 66 books — requires ~22 cows/rabbits, or village library looting)
- Coal: 2+ (for torches + smelting fuel)
- Wool: 3 (for bed)
- Iron ore: 3+ (for bucket if brewing later)

---

## Build Sequence

Execute phases in order to avoid placing blocks in mid-air:

1. **Y=0** — Lay stone brick foundation (9×9 = 81 blocks)
2. **Y=1** — Build ground floor walls + place door + spiral stair at (4,1,4)
3. **Y=2** — Build storage level walls + place chests, crafting table, ladder, stair
4. **Y=3** — Build living level walls + place bed, torches, ladder, stair
5. **Y=4** — Build library level walls + place bookshelves (ring), ladder, stair
6. **Y=5** — Build enchanting chamber walls + place enchanting table at (3,5,3),
   bookshelves on walls, ladder, stair
7. **Y=6** — Build alchemy level walls + place torches, ladder, stair
8. **Y=7** — Build observatory walls + place glass panes, torches, ladder, stair
9. **Y=8** — Place spruce plank roof (63 planks across 9×9)
10. **Y=9** — Place single stone brick spire tip at (4,9,4)
11. **Verify** — GetStatus, confirm all levels built, enchanting table at (3,5,3)
    surrounded by bookshelves, door functional, light level ≥ 8 interior

---

## Phase 4b Limitations

- Smelting cobblestone → stone requires FurnaceTool (Phase 4b).
- Crafting stone_bricks, bookshelves, enchanting table is manual (CraftItemTool Phase 5).
- Books require leather (animal farming/trading) and paper (sugar cane) — complex supply chain.
- Enchanting table requires mid-game resources (diamond + obsidian) — not available early-game.
- Spiral staircase orientation must be manually set per stair placement.
- Ladder placement on walls is manual per block.
- Glass pane production requires furnace smelting.
- Build origin must be set in WorldState facts before planning:
  `build:wizards-tower:origin:x`, `build:wizards-tower:origin:y`, `build:wizards-tower:origin:z`
