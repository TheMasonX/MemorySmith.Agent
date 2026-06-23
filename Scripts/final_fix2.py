import os
os.chdir(r'D:\@Repos\MemorySmith.Agent')

with open('Agent.Planning/HtnTaskLibrary.cs', 'r', encoding='utf-8') as f:
    c = f.read()

print(f'Before: SearchMemory={c.count("SearchMemory")}')

# Remove the flat area SearchMemory in non-creative build path
old = '        actions.Add(MakeAction("SearchMemory", ("query", $"flat area build location {blueprint.Name}")));\n        actions.Add(MakeAction("MoveTo",'
new = '        actions.Add(MakeAction("MoveTo",'
if old in c:
    c = c.replace(old, new, 1)
    print('Removed flat area SearchMemory')
else:
    print('ERROR: flat area SearchMemory not found')
    # Debug
    idx = c.find('flat area build location')
    if idx > 0:
        print(f'Found at {idx}: {repr(c[idx:idx+150])}')
    else:
        print('"flat area build location" not found in file')

# Also remove the GatherItemDecompose SearchMemory (if still there)
old2 = '            MakeAction("SearchMemory", ("query", $"{spec.ItemId} location nearby source")),\n            MakeAction("MineBlock",'
if old2 in c:
    c = c.replace(old2, '            MakeAction("MineBlock",', 1)
    print('Removed GatherItemDecompose SearchMemory')

with open('Agent.Planning/HtnTaskLibrary.cs', 'w', encoding='utf-8') as f:
    f.write(c)

print(f'After: SearchMemory={c.count("SearchMemory")}')
print(f'DecomposeSmeltItem: {"DecomposeSmeltItem" in c}')
print(f'IsMineableBlock: {"IsMineableBlock" in c}')
