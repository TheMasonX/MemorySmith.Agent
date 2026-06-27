---
id: castle
name: Stone Castle Keep
tags: castle, medieval, build, defensive, large
dimensions: 17x8x15
materials: stone_bricks x 274, stone_brick_stairs x 32, cobblestone x 48, oak_planks x 36, oak_door x 2, torch x 10, iron_bars x 12, glass_pane x 8, oak_fence x 16, oak_slab x 24, oak_log x 12, ladder x 4
description: A 17x15 medieval castle keep with 4 corner towers, courtyard, battlements, and a main gate.
---

# Stone Castle Keep

A imposing medieval-style castle keep with four corner towers, a central courtyard,
battlements, and a grand entry gate. Built from stone bricks with cobblestone
foundations. The keep has two main floors plus tower lookout levels.

**Footprint:** 17 × 15 blocks  
**Height:** 8 blocks (1 foundation + 2 floors + 3 tower levels + 2 roof/battlements)  
**Interior courtyard:** 7 × 5 blocks at ground level  
**Corner towers:** 5 × 5 each, rising to Y=7  
**Main gate:** South face (Z = 14), centered at X = 8  
**Wall thickness:** 2 blocks (double-layer stone brick for defense)

---

## Layers

### Y=0 (Foundation — cobblestone)
CCCCCCCCCCCCCCCCC
CCCCCCCCCCCCCCCCC
CCCCC.....CCCCCCC
CCCCC.....CCCCCCC
CCCCC.....CCCCCCC
CCCCC.....CCCCCCC
CCCCC.....CCCCCCC
CCCCC.....CCCCCCC
CCCCC.....CCCCCCC
CCCCC.....CCCCCCC
CCCCC.....CCCCCCC
CCCCC.....CCCCCCC
CCCCC.....CCCCCCC
CCCCCCCCCCCCCCCCC
CCCCCCCCCCCCCCCCC

### Y=1 (Ground floor — lower walls + gate + courtyard floor)
SSSSSSSSSSSSSSSSS
SGGGGSGGGGGSGGGGS
SGGGGSGGGGGSGGGGS
SGGGGSGGGGGSGGGGS
SGGGGS.....SGGGGS
SGGGGS.....SGGGGS
SGGGGS.....SGGGGS
SGGGGS.....SGGGGS
SGGGGS.....SGGGGS
SGGGGS.....SGGGGS
SGGGGS.....SGGGGS
SGGGGS.....SGGGGS
SGGGGS.....SGGGGS
SSSSSSSSSSSSSDSSS
SSSSSSSSSSSSSSSSS

### Y=2 (Ground floor walls — door + arrow slits + interior)
SSSSSSSSSSSSSSSSS
S.....S.....S...S
S.....S.....S...S
S.....S.....S...S
S.....S.....S...S
S..T..S..T..S...S
S.....S.....S...S
S.....S.....S...S
S.....S.....S...S
S.....S.....S...S
S.....S.....S...S
S.....S.....S...S
S.....S.....S...S
SSSSSSSSSSSSSDSSS
SSSSSSSSSSSSSSSSS

### Y=3 (Upper floor — walls + windows + interior)
SSSSSSSSSSSSSSSSS
S.....S.....S...S
S..G..S..G..S..GS
S.....S.....S...S
S..I..S..I..S..IS
S.....S.....S...S
S..G..S..G..S..GS
S.....S.....S...S
S..I..S..I..S..IS
S.....S.....S...S
S..G..S..G..S..GS
S.....S.....S...S
S.....S.....S...S
SSSSSSSSSSSSSSSSS
SSSSSSSSSSSSSSSSS

### Y=4 (Tower lower + wall top + tower interior floor)
SSSSS.SSSSS.SSSSS
S...S.S...S.S...S
S...S.S...S.S...S
S...S.S...S.S...S
SSSSS.......SSSSS
S...S.......S...S
S...S.......S...S
S...S.......S...S
SSSSS.......SSSSS
S...S.......S...S
S...S.......S...S
S...S.......S...S
SSSSS.......SSSSS
SSSSS.SSSSS.SSSSS
SSSSS.SSSSS.SSSSS

### Y=5 (Tower mid + battlements base + walkway)
SBSBSBSBSBSBSBSBS
B.....B.....B...B
S.....S.....S...S
B.....B.....B...B
SBSBS.......SBSBS
B...B.......B...B
S...S.......S...S
B...B.......B...B
SBSBS.......SBSBS
B...B.......B...B
S...S.......S...S
B...B.......B...B
SBSBS.......SBSBS
SBSBSBSBSBSBSBSBS
SBSBSBSBSBSBSBSBS

### Y=6 (Tower upper + crenellations base)
SPSPSPSPSPSPSPSPS
P.....P.....P...P
S.....T.....T...S
P.....P.....P...P
SPSPS.......SPSPS
P...P.......P...P
S...T.......T...S
P...P.......P...P
SPSPS.......SPSPS
P...P.......P...P
S...T.......T...S
P...P.......P...P
SPSPS.......SPSPS
SPSPSPSPSPSPSPSPS
SPSPSPSPSPSPSPSPS

### Y=7 (Tower roofs + crenellations + torches)
SPSPSPSPSPSPSPSPS
P..TP..P..TP..P.T
SPSPSPSPSPSPSPSPS
P..TP..P..TP..P.T
SPSPS.......SPSPS
P..TP.......P..TP
SPSPS.......SPSPS
P..TP.......P..TP
SPSPS.......SPSPS
P..TP.......P..TP
SPSPS.......SPSPS
P..TP.......P..TP
SPSPS.......SPSPS
SPSPSPSPSPSPSPSPS
SPSPSPSPSPSPSPSPS

---

## Legend

- `.` = air (skip)
- `S` = stone_bricks
- `C` = cobblestone
- `B` = stone_brick_stairs (facing inward toward courtyard/tower center)
- `P` = oak_planks (tower roofs, walkway floors)
- `D` = oak_door
- `G` = glass_pane
- `I` = iron_bars
- `T` = torch
- `F` = oak_fence
- `L` = oak_slab (roof accent)
- `H` = oak_log

**Dots (`.`)** in grid rows represent air — courtyard interior open to sky in Y=4+ center.

---

## Block Placement Notes

**Coordinates** use the blueprint origin (0,0,0) = southwest bottom corner of the structure.
- X increases eastward (0 = west wall, 16 = east wall)
- Y increases upward (0 = foundation, 7 = tower roof)
- Z increases northward (0 = north wall, 14 = south wall)

**Corner towers (X=0..4, Z=0..4 / X=12..16, Z=0..4 / X=0..4, Z=10..14 / X=12..16, Z=10..14):**
Each tower is 5×5 and extends from Y=0 to Y=7.
- Y=0: Cobblestone foundation
- Y=1–4: Stone brick walls with interior space (air)
- Y=5–6: Tower upper levels with battlements
- Y=7: Tower roof (oak plank platform with torch at center of each tower roof)

**Main gate (Y=1–2, X=8, Z=14):**
- Oak_door at (8, 1, 14) — south-center
- The door at Y=2 is the upper half (auto-placed by Mineflayer)

**Courtyard (X=5..11, Z=5..9):**
- 7×5 open air space at ground level
- Y=0: cobblestone floor
- Y=1: courtyard floor (stone_bricks or cobblestone)
- Y=2–3: open air (roof removed for the courtyard opening)
- Y=4–7: wall sections enclose the courtyard from above

**Arrow slits / windows (Y=2):**
- Small glass_pane windows at regular intervals along the outer walls
- Iron_bars at Y=3 for reinforced window sections

**Battlements (Y=5–6):**
- Stone_brick_stairs placed on walls as crenellations, facing inward
- Alternating stairs and stone_brick blocks create the castle battlement pattern
- Torches on top of battlements at intervals

**Interior ladders (Y=1–4):**
- Four ladders, one per tower, for vertical access (placed against the interior
  tower wall at the tower-center side)

---

## Materials to Gather

### Directly mineable
| Material | Quantity | How to obtain |
|---|---|---|
| Stone/Cobblestone | 322 total | Mine stone with pickaxe → smelt for stone bricks |
| Oak Log | 12 | Chop trees |

### Smelted / crafted items
| Item | Qty needed | Recipe |
|---|---|---|
| Stone Bricks | 274 | 4 stone → 4 bricks (274 bricks = 274 stone smelted) |
| Stone Brick Stairs | 32 | 6 stone bricks → 4 stairs (48 bricks → 32 stairs) |
| Cobblestone | 48 | Mine stone directly |
| Oak Planks | 36 | 9 logs → 36 planks |
| Oak Door | 2 | 6 planks per 3 doors (6 planks for 2 doors) |
| Oak Fence | 16 | 4 planks + 2 sticks → 3 fences |
| Oak Slab | 24 | 3 planks → 6 slabs (12 planks → 24 slabs) |
| Glass Pane | 8 | 3 sand → smelt → craft panes |
| Iron Bars | 12 | 6 iron ingots → 16 bars |
| Torch | 10 | 1 stick + 1 coal → 4 torches (3 coal) |
| Ladder | 4 | 7 sticks → 3 ladders (3 sets of 7 sticks) |

### Raw resource requirements
- Cobblestone / stone: ~548 (for bricks + foundation + stairs)
- Oak logs: ~40+ (for planks, fences, slabs, doors)
- Sand: 3 (for glass panes)
- Iron ore: 6 (for iron bars)
- Coal: 3+ (for torches, smelting)
- Sticks: ~48 (from planks, for fence + ladder + torch)

---

## Build Sequence

Execute phases in order to avoid placing blocks in mid-air:

1. **Y=0** — Lay cobblestone foundation (entire 17×15 footprint)
2. **Y=1** — Build ground floor walls + courtyard floor + place main gate door
3. **Y=2** — Build ground floor walls complete + arrow slit windows + interior details
4. **Y=3** — Build upper floor walls + glass pane windows + iron bar grates
5. **Y=4** — Build tower lower sections + wall top walkway
6. **Y=5** — Build tower mid sections + battlements base
7. **Y=6** — Build tower upper sections + tower roof platforms + place torches
8. **Y=7** — Complete crenellations + tower roof details + place torches
9. **Verify** — GetStatus, confirm all walls & towers complete, door functional,
   light level ≥ 8 interior

---

## Phase 4b Limitations

- Smelting cobblestone → stone for stone_bricks requires a furnace (FurnaceTool, Phase 4b).
- Crafting stone bricks from stone requires CraftItemTool (Phase 5).
- All wood crafting (fence, slabs, doors, ladders) is manual; CraftItemTool is Phase 5.
- Iron smelting and iron_bars crafting are manual.
- Glass pane production requires furnace smelting.
- Ladder placement on interior tower walls is a manual action per block.
- Build origin must be set in WorldState facts before planning:
  `build:castle:origin:x`, `build:castle:origin:y`, `build:castle:origin:z`
