import os
os.chdir(r'D:\@Repos\MemorySmith.Agent')

with open('Agent.Planning/HtnTaskLibrary.cs', 'r', encoding='utf-8') as f:
    c = f.read()

print(f'File length: {len(c)} chars')
print(f'SearchMemory count: {c.count("SearchMemory")}')

# Step 1: Insert DecomposeSmeltItem after DecomposeCraftItem
# The marker is the end of DecomposeCraftItem before AddCraftingTableIfNeeded
old1 = '        actions.Add(MakeAction("GetStatus"));\n        return actions;\n    }\n\n    /// <summary>\n    /// Emits the minimum actions needed to bootstrap a crafting table'

if old1 not in c:
    print("ERROR: marker not found for insertion")
    # Debug: show what's around that area
    idx = c.find('AddCraftingTableIfNeeded')
    if idx > 0:
        print(f'Found AddCraftingTableIfNeeded at {idx}')
        print(repr(c[idx-200:idx+200]))
else:
    print("Found marker for insertion")

# Step 2: Apply all SearchMemory removals (15 total)
replacements = [
    ('actions.Add(MakeAction("SearchMemory", ("query", "iron ore mine location")));\n                    ', ''),
    ('actions.Add(MakeAction("SearchMemory", ("query", "coal ore location nearby")));\n                    ', ''),
    ('actions.Add(MakeAction("SearchMemory", ("query", "stone cobblestone mine location")));\n                ', ''),
    ('actions.Add(MakeAction("SearchMemory", ("query", $"{logType} nearby source tree")));\n                ', ''),
    ('actions.Add(MakeAction("SearchMemory", ("query", $"flat area build location {blueprint.Name}")));\n            ', ''),
    ('                actions.Add(MakeAction("SearchMemory", ("query", $"{block} nearby source location")));\n', ''),
    ('                        actions.Add(MakeAction("SearchMemory", ("query", "coal ore location nearby")));\n', ''),
    ('                    actions.Add(MakeAction("SearchMemory", ("query", "furnace iron ore location")));\n', ''),
    ('        actions.Add(MakeAction("SearchMemory", ("query", $"flat area build location {blueprint.Name}")));\n\n        // Sprint 43 (P1-2)', '        // Sprint 43 (P1-2)'),
    ('            MakeAction("GetStatus"),\n            MakeAction("SearchMemory", ("query", $"{spec.ItemId} location nearby source")),\n        };', '            MakeAction("GetStatus"),\n        };'),
    ('        MakeAction("SearchMemory", ("query", "nearest oak birch spruce tree coordinates")),\n        MakeAction("GetStatus"),', '        MakeAction("GetStatus"),'),
    ('        MakeAction("SearchMemory", ("query", "shelter cave house location safe night")),\n        MakeAction("GetStatus"),', '        MakeAction("GetStatus"),'),
    ('        MakeAction("SearchMemory", ("query", "shelter cave house night safe")),\n        MakeAction("GetStatus"),', '        MakeAction("GetStatus"),'),
    ('            MakeAction("SearchMemory", ("query", "unexplored areas points of interest biome")),\n            MakeAction("Wander",', '            MakeAction("Wander",'),
    # The 15th is the one on line ~491 which is: actions.Add(MakeAction("SearchMemory", ("query", $"flat area build location {blueprint.Name}")));
    # This should have been caught by the 5th or 9th pattern
]

for old, new in replacements:
    if old in c:
        c = c.replace(old, new, 1)
        print(f'  Replaced: {old[:60]}...')

remaining = c.count('SearchMemory')
print(f'SearchMemory remaining: {remaining}')

# Step 3: Insert DecomposeSmeltItem (after SearchMemory has been removed,
# the file structure is cleaner)
# Now find where to insert
new_methods = '''
    /// <summary>
    /// Sprint 44 (TSK-0079): Decomposes a <see cref="Goals.SmeltGoal"/> into a sequence
    /// of prerequisite and smelting actions.
    ///
    /// Previously, smelt intents were routed through <c>DecomposeCraftItem</c>, which
    /// emitted <c>CraftItem</c> actions — never exercising the adapter's dedicated
    /// <c>case 'smelt':</c> handler. This method emits <c>SmeltItem</c> actions.
    ///
    /// Pre-gathers fuel (coal) and ensures the input item is available before smelting.
    /// The adapter's smelt handler handles furnace interaction and item output.
    /// </summary>
    public IReadOnlyList<ActionData> DecomposeSmeltItem(
        string inputItem, int count, WorldState state)
    {
        var actions = new List<ActionData>();

        // Sprint 44: pre-gather fuel (coal) — 1 coal smelts up to 8 items.
        var coalNeeded = Math.Max(1, (count + 7) / 8);
        var haveCoal   = state.Inventory.GetValueOrDefault("coal");
        if (haveCoal < coalNeeded)
        {
            var coalToMine = coalNeeded - haveCoal;
            actions.Add(MakeAction("MineBlock",
                ("block", "coal_ore"), ("count", (object?)coalToMine)));
        }

        // Sprint 44: pre-gather the input item if it's a mineable block.
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

        // Sprint 44: the actual smelt action.
        actions.Add(MakeAction("SmeltItem",
            ("item", inputBlock), ("count", (object?)count), ("fuel", "coal")));
        actions.Add(MakeAction("GetStatus"));
        return actions;
    }

    /// <summary>
    /// Sprint 44 (TSK-0079): Returns true if the given block ID can be mined directly.
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
    /// Emits the minimum actions needed to bootstrap a crafting table'''

insert_marker = '        return actions;\n    }\n\n    /// <summary>\n    /// Emits the minimum actions needed to bootstrap a crafting table'
if insert_marker in c:
    c = c.replace(insert_marker, new_methods, 1)
    print('Inserted DecomposeSmeltItem!')
else:
    print('ERROR: insert marker not found')
    # Try to find it
    idx = c.find('Emits the minimum actions')
    if idx > 0:
        print(f'Found at {idx}: {repr(c[idx-80:idx+80])}')

with open('Agent.Planning/HtnTaskLibrary.cs', 'w', encoding='utf-8') as f:
    f.write(c)

# Final verification
print(f'Final file length: {len(c)} chars')
print(f'DecomposeSmeltItem: {"DecomposeSmeltItem" in c}')
print(f'IsMineableBlock: {"IsMineableBlock" in c}')
final_sm = c.count('SearchMemory')
print(f'SearchMemory remaining: {final_sm}')
