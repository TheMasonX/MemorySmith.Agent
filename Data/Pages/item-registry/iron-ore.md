# iron-ore

item_id: iron_ore
display_name: Iron Ore
source_blocks: iron_ore, deepslate_iron_ore
requires_smelting: false
min_harvest_level: 2

Iron ore can be found between layers -64 and 320, most commonly around layer 16.
Breaking it with a stone pickaxe or better drops raw iron (since Java 1.17).
Use this entry when gathering raw iron — for iron ingots see item-registry/iron-ingot.

Note: requires_smelting is false here because the inventory key after mining is
"raw_iron" (since Java 1.17) or "iron_ore" (pre-1.17). For ingots, create a
separate item-registry/iron-ingot page with requires_smelting: true.
