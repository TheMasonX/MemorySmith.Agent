import os
os.chdir(r'D:\@Repos\MemorySmith.Agent')

with open('Agent.Planning/HtnTaskLibrary.cs', 'r', encoding='utf-8') as f:
    c = f.read()

print(f'Before: SearchMemory={c.count("SearchMemory")} DecomposeSmeltItem={"DecomposeSmeltItem" in c}')

# Remove flat area SearchMemory in non-creative path
c = c.replace('        actions.Add(MakeAction("SearchMemory", ("query", $"flat area build location {blueprint.Name}")));\n\n        // Sprint 43 (P1-2)', '        // Sprint 43 (P1-2)', 1)

# Remove GatherItemDecompose SearchMemory
c = c.replace('            MakeAction("SearchMemory", ("query", $"{spec.ItemId} location nearby source")),\n', '', 1)

with open('Agent.Planning/HtnTaskLibrary.cs', 'w', encoding='utf-8') as f:
    f.write(c)

print(f'After: SearchMemory={c.count("SearchMemory")} DecomposeSmeltItem={"DecomposeSmeltItem" in c}')
