---
id: farm
name: Survival Farm
tags: farm, food, survival, build, agricultural
dimensions: 13x4x11
materials: dirt x 121, oak_fence x 38, oak_fence_gate x 1, water_bucket x 3, wheat_seeds x 18, torch x 4, oak_log x 6, oak_planks x 12, hay_bale x 4, cobblestone x 12, oak_slab x 9
description: A 13x11 fenced farm plot with irrigation, crops, and a small tool shed.
---

# Survival Farm

A fully self-contained survival farm plot with perimeter fencing, a central irrigation
channel, crop rows on both sides, and a small tool shed for storage and decoration.
The 11×9 tillable area accommodates 18 wheat with a 1-wide water channel down the
center, and the shed stores a hay-bale harvest.

**Footprint:** 13 × 11 blocks  
**Height:** 4 blocks (1 ground + 3 structure)  
**Tillable area:** 11 × 9 = 99 blocks (83 farmland + 3 water + 13 fence/shed)  
**Water channel:** 1-wide trench, N–S at X = 6, Z = 1–9 (3 source blocks)  
**Gate faces:** South (Z = 10), centered at X = 6  
**Shed:** 3 × 3 cobblestone-and-plank shelter at NW corner (X = 1, Z = 1)

---

## Layers

### Y=0 (Ground — dirt base with water channel)
DDDDDDDDDDDDD
DDDDDDDDDDDDD
DDDDDDDDDDDDD
DDDDDDDDDDDDD
DDDDDWDDDDDDD
DDDDDWDDDDDDD
DDDDDWDDDDDDD
DDDDDDDDDDDDD
DDDDDDDDDDDDD
DDDDDDDDDDDDD
DDDDDDDDDDDDD

### Y=1 (Farm level — fence, crops, water source, shed base)
FFFFFFFFFFFFF
FCCCDDDDDDDDF
FCPPDDDDDDDDF
FCPPDDDDDDDDF
FDDDDWDDDDDDF
FDDDDWDDDDDDF
FDDDDWDDDDDDF
FDDDDDDDDDDDF
FDDDDDDDDDDDF
FDDDDDDDDDDDF
FFFFFGGFFFFFG

### Y=2 (Mid level — fence upper, shed roof, torches)
F...........F
F.HHHS......F
F.HHHP......F
F.HHHP......F
F....T......F
F...........F
F....T......F
F...........F
F...........F
F...........F
F...........F

### Y=3 (Fence cap — oak_log post tops, shed roof peak)
L...........L
.SSS.........
.SPS.........
.SSS.........
.............
.............
.............
.............
.............
.............
L...........L

---

## Legend

- `.` = air (skip)
- `D` = dirt (tillable base for farmland)
- `W` = water (source block — placed from water_bucket)
- `F` = oak_fence
- `G` = oak_fence_gate
- `C` = cobblestone
- `P` = oak_planks
- `H` = hay_bale
- `T` = torch
- `S` = oak_slab
- `L` = oak_log

**Blank spaces** in Y=3 are air (the shed roof peak uses only the 3×3 shed footprint;
everything else is open at Y=3 except fence-post log caps).

---

## Block Placement Notes

**Coordinates** use the blueprint origin (0,0,0) = southwest bottom corner of the structure.
- X increases eastward (0 = west wall, 12 = east wall)
- Y increases upward (0 = ground/dirt base, 3 = fence cap)
- Z increases northward (0 = north wall, 10 = south wall)

**Water (Y=1, X=6, Z=1–9):**
- Place 3 water source blocks at (6,1,1), (6,1,5), (6,1,9) — water flows between them
- Hydrates all farmland within 4 blocks (Manhattan distance)
- `W` markers only at source positions; expect flow to fill intermediate blocks

**Shed (NW corner, X=1–3, Z=1–3):**
- Y=1: Cobblestone base (X=1..3, Z=1..3)
- Y=2: Hay bale stack (X=1..3, Z=1..3) with oak_planks roof at (X=2, Z=2)
- Y=3: Oak slab roof peak (X=1..3, Z=1..3)
- The shed provides decorative storage and a landmark from a distance

**Fence (Y=1–Y=3):**
- Y=1: Full fence perimeter — all 4 sides at edges and around gate gap
- Y=2: Fence posts only at corners + every 4 blocks in between
- Y=3: Oak_log caps on corner fence posts
- Gate: oak_fence_gate at (6,1,10) — south-center
- Fence gap at (5–7, 1, 10) with gate at center

**Torches (Y=2):**
- Torch 1: (5, 2, 4) — west of water channel, mid-field
- Torch 2: (7, 2, 6) — east of water channel, mid-field
- Provides light level ≥ 8 for crop growth at night

---

## Materials to Gather

### Directly mineable
| Material | Quantity | How to obtain |
|---|---|---|
| Dirt | 143 (121 placed) | Shovel any grass/dirt block |
| Cobblestone | 12 | Mine stone with any pickaxe |
| Oak Log | 6 | Chop trees |

### Crafted / gathered items
| Item | Qty needed | Recipe |
|---|---|---|
| Oak Fence | 38 | 4 planks + 2 sticks → 3 fences (51 planks + 26 sticks ≈ 23 logs) |
| Oak Fence Gate | 1 | 2 planks + 4 sticks |
| Oak Planks | 12 | 1 log → 4 planks (3 logs total for shed) |
| Oak Slab | 9 | 3 planks → 6 slabs (5 planks → 9 slabs usable) |
| Wheat Seeds | 18 | Break tall grass or harvest mature wheat |
| Water Bucket | 3 (placed) | 3 iron ingots → bucket; fill at water source |
| Torch | 4 | 1 stick + 1 coal → 4 torches |
| Hay Bale | 4 | 9 wheat → 1 hay bale (36 wheat total) |

### Raw resource requirements
- Oak logs: ~30+ (for fence, shed, slabs, gate)
- Cobblestone: 12 (shed base)
- Dirt: 143 (tillable area)
- Iron ore: 3 (for bucket)
- Coal: 1 (for torches)
- Wheat: 36 (for 4 hay bales)

---

## Build Sequence

Execute phases in order to avoid placing blocks in mid-air:

1. **Y=0** — Lay dirt base (143 blocks) — entire 13×11 footprint
2. **Y=1** — Build fence perimeter + place gate + lay shed cobblestone base +
   place water source blocks + till dirt into farmland (optional per-phase)
3. **Y=2** — Place fence upper posts + shed hay bale/roof + torches
4. **Y=3** — Place fence-corner oak_log caps + shed slab roof peak
5. **Verify** — GetStatus, confirm water hydrates all farmland blocks,
   light level ≥ 8 at crop positions

---

## Phase 4b Limitations

- Tilling dirt into farmland requires a hoe (Phase 4b tool).
- Planting wheat_seeds on farmland is a right-click action (manual or Phase 4b).
- Water_bucket creation requires smelting iron_ore (FurnaceTool, Phase 4b).
- All crafting (fence, gate, slabs, torches, hay bale) is manual; CraftItemTool is Phase 5.
- Build origin must be set in WorldState facts before planning:
  `build:farm:origin:x`, `build:farm:origin:y`, `build:farm:origin:z`
