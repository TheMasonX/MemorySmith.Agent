import os
os.chdir(r'D:\@Repos\MemorySmith.Agent')

with open('Agent.Planning/HtnTaskLibrary.cs', 'r', encoding='utf-8') as f:
    c = f.read()

# Find the area around DecomposeCraftItem's end - it needs a return and closing brace
# The pattern is: actions.Add(MakeAction("GetStatus"));\n\n    /// <summary>\n    /// Sprint 44
# Should be: actions.Add(MakeAction("GetStatus"));\n        return actions;\n    }\n\n    /// <summary>\n    /// Sprint 44

old = '        actions.Add(MakeAction("GetStatus"));\n\n    /// <summary>\n    /// Sprint 44 (TSK-0079): Decomposes a <see cref="Goals.SmeltGoal"/>'
new = '        actions.Add(MakeAction("GetStatus"));\n        return actions;\n    }\n\n    /// <summary>\n    /// Sprint 44 (TSK-0079): Decomposes a <see cref="Goals.SmeltGoal"/>'

if old in c:
    c = c.replace(old, new, 1)
    print('Fixed missing return/brace!')
else:
    print('ERROR: pattern not found')
    idx = c.find('Sprint 44 (TSK-0079)')
    if idx >= 0:
        print(f'Found Sprint 44 at {idx}: {repr(c[idx-80:idx+80])}')
    else:
        print('Sprint 44 not found - file may be corrupted')

with open('Agent.Planning/HtnTaskLibrary.cs', 'w', encoding='utf-8') as f:
    f.write(c)

print(f'Total SearchMemory: {c.count("SearchMemory")}')
print(f'DecomposeSmeltItem: {"DecomposeSmeltItem" in c}')
