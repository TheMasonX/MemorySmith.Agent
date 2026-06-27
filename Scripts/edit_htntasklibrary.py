import re

with open('Agent.Planning/HtnTaskLibrary.cs', 'r', encoding='utf-8') as f:
    c = f.read()

# Pattern 1: Insert DecomposeSmeltItem after DecomposeCraftItem's closing brace
old1 = '        return actions;\n    }\n\n    /// <summary>\n    /// Emits the minimum actions needed to bootstrap a crafting table'

new_methods = '''        return actions;
    }

    /// <summary>
    /// Sprint 44 (TSK-0079): Decomposes a <see cref="Goals.SmeltGoal"/> into a sequence
    /// of prerequisite and smelting actions.
    ///
    /// Previously, smelt intents were routed through <c>DecomposeCraftItem</c>, which
    /// emitted <c>CraftItem</c> actions — never exercising the adapter\'s dedicated
    /// <c>case \'smelt\':</c> handler. This method emits <c>SmeltItem</c> actions.
    ///
    /// Pre-gathers fuel (coal) and ensures the input item is available before smelting.
    /// The adapter\'s smelt handler handles furnace interaction and item output.
    /// </summary>
    public IReadOnlyList<ActionData> DecomposeSmeltItem(
        string inputItem, int count, WorldState state)
    {
        var actions = new List<ActionData>();

        // ── Pre-gather fuel (coal) ────────────────────────────────────────────
        // 1 coal smelts up to 8 items; use ceiling(count/8) coal.
        var coalNeeded = Math.Max(1, (count + 7) / 8);
        var haveCoal   = state.Inventory.GetValueOrDefault("coal");
        if (haveCoal < coalNeeded)
        {
            var coalToMine = coalNeeded - haveCoal;
            actions.Add(MakeAction("MineBlock",
                ("block", "coal_ore"), ("count", (object?)coalToMine)));
        }

        // ── Pre-gather the input item ─────────────────────────────────────────
        // If the input is an ore that needs mining, mine enough.
        // Common smeltable ores: iron_ore, gold_ore, copper_ore, ancient_debris, etc.
        var inputBlock = inputItem switch
        {
            "iron_ingot"    => "iron_ore",
            "gold_ingot"    => "gold_ore",
            "copper_ingot"  => "copper_ore",
            "netherite_scrap" => "ancient_debris",
            _               => inputItem,
        };

        var haveInput = state.Inventory.GetValueOrDefault(inputBlock);
        var needInput = count - haveInput;
        if (needInput > 0 && IsMineableBlock(inputBlock))
        {
            actions.Add(MakeAction("MineBlock",
                ("block", inputBlock), ("count", (object?)needInput)));
        }

        // ── The smelt ─────────────────────────────────────────────────────────
        actions.Add(MakeAction("SmeltItem",
            ("item", inputBlock), ("count", (object?)count), ("fuel", "coal")));
        actions.Add(MakeAction("GetStatus"));
        return actions;
    }

    /// <summary>
    /// Returns true if the given block ID can be mined directly (is in the direct-mine set
    /// or is a known smeltable ore). Used by <see cref="DecomposeSmeltItem"/> to decide
    /// whether emission of a MineBlock action is appropriate.
    /// </summary>
    private static bool IsMineableBlock(string block)
    {
        if (DirectMineBlocks.Contains(block)) return true;
        return block switch
        {
            "iron_ore" or "deepslate_iron_ore" or
            "gold_ore" or "deepslate_gold_ore" or
            "copper_ore" or "deepslate_copper_ore" or
            "ancient_debris" or "nether_gold_ore" or
            "coal_ore" or "deepslate_coal_ore" => true,
            _ => false,
        };
    }

    /// <summary>
    /// Emits the minimum actions needed to bootstrap a crafting table into the bot\'s'''

c = c.replace(old1, new_methods, 1)
print('Replace 1 applied:', 'DecomposeSmeltItem' in c)

# Pattern 2: Remove SearchMemory calls
# The file still has Original search calls. Let me count before
before = c.count('SearchMemory')
print(f'SearchMemory before removal: {before}')

# a)
c = c.replace('actions.Add(MakeAction("SearchMemory", ("query", "iron ore mine location")));\n                    ', '', 1)
# b)
c = c.replace('actions.Add(MakeAction("SearchMemory", ("query", "coal ore location nearby")));\n                    ', '', 1)
# c)
c = c.replace('actions.Add(MakeAction("SearchMemory", ("query", "stone cobblestone mine location")));\n                ', '', 1)
# d)
c = c.replace('actions.Add(MakeAction("SearchMemory", ("query", $"{logType} nearby source tree")));\n                ', '', 1)
# e)
c = c.replace('            actions.Add(MakeAction("SearchMemory", ("query", $"flat area build location {blueprint.Name}")));\n', '', 1)
# f)
c = c.replace('                actions.Add(MakeAction("SearchMemory", ("query", $"{block} nearby source location")));\n', '', 1)
# g)
c = c.replace('                        actions.Add(MakeAction("SearchMemory", ("query", "coal ore location nearby")));\n', '', 1)
# h)
c = c.replace('                    actions.Add(MakeAction("SearchMemory", ("query", "furnace iron ore location")));\n', '', 1)
# i)
c = c.replace('        actions.Add(MakeAction("SearchMemory", ("query", $"flat area build location {blueprint.Name}")));\n\n', '', 1)
# j)
c = c.replace('            MakeAction("GetStatus"),\n            MakeAction("SearchMemory", ("query", $"{spec.ItemId} location nearby source")),\n        };', '            MakeAction("GetStatus"),\n        };', 1)
# k)
c = c.replace('        MakeAction("SearchMemory", ("query", "nearest oak birch spruce tree coordinates")),\n        MakeAction("GetStatus"),', '        MakeAction("GetStatus"),', 1)
# l)
c = c.replace('        MakeAction("SearchMemory", ("query", "shelter cave house location safe night")),\n        MakeAction("GetStatus"),', '        MakeAction("GetStatus"),', 1)
# m)
c = c.replace('        MakeAction("SearchMemory", ("query", "shelter cave house night safe")),\n        MakeAction("GetStatus"),', '        MakeAction("GetStatus"),', 1)
# n)
c = c.replace('            MakeAction("SearchMemory", ("query", "unexplored areas points of interest biome")),\n            MakeAction("Wander",', '            MakeAction("Wander",', 1)

after = c.count('SearchMemory')
print(f'SearchMemory after removal: {after}')
print(f'Removed {before - after} SearchMemory calls')

with open('Agent.Planning/HtnTaskLibrary.cs', 'w', encoding='utf-8') as f:
    f.write(c)

# Verify
print('DecomposeSmeltItem present:', 'DecomposeSmeltItem' in c)
print('IsMineableBlock present:', 'IsMineableBlock' in c)
print('Done')
