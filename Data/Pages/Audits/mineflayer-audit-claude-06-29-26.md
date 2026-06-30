
## Critical bugs (confirmed against upstream source)

### 1. The custom `toVec3` shim silently destroys every fractional offset — breaks `place` and `dig` aim

`vec3.js` floors `x/y/z` at construction, and `.floored()` just returns `this`:

```js
self.floored = function () { return self; };
```

The problem is every *other* method also routes through `toVec3(...)` to build its result, so **any arithmetic on a shim instance gets re-floored to integers**, including `.offset()`, `.plus()`, `.minus()`, `.multiply()`, `.unit()`, `.normalize()`.

I traced this through `prismarine-world`'s `getBlock()`:

```js
// prismarine-world/src/world.js
block.position = pos.floored()
```

Since `pos` here is your shim (passed in via `bot.blockAt(toVec3(x,y,z))`), and `.floored()` returns the *same shim instance*, `block.position` literally **is** your broken shim — not a real `Vec3`. That block then flows into Mineflayer's own digging/placing code:

```js
// mineflayer/lib/plugins/digging.js (default branch, exactly what your dispatcher hits)
await bot.lookAt(block.position.offset(0.5, 0.5, 0.5), forceLook)

// mineflayer/lib/plugins/generic_place.js
let dx = 0.5 + faceVector.x * 0.5  // 0, 0.5, or 1
...
await bot.lookAt(referenceBlock.position.offset(dx, dy, dz), options.forceLook)
```

`offset(0.5, 0.5, 0.5)` on your shim computes `toVec3(x+0.5, y+0.5, z+0.5)`, which immediately floors back down to `(x, y, z)` — the block's *corner*, not its *center*. The bot ends up aiming at the wrong point on essentially every dig and every placement.

Blast radius — I grepped every `toVec3(...)` call site:

```
738:  const fresh = bot.blockAt(toVec3(...));        // mine: dig target
890:  const targetPos = toVec3(x, y, z);              // place: target block
998:  bot.blockAt(toVec3(sx, y - 1, sz));              // place: step-aside check
1091: const refPos = toVec3(x + rx, y + ry, z + rz);   // place: 6-face reference search — THE block passed into bot.placeBlock()
1109: await bot.placeBlock(ref, toVec3(fx, fy, fz));
1136-1170: scaffold fallback path (same pattern)
```

So **100% of `place` calls** and the dig-target recheck in **every `mine` call** pass through this shim. The cursor coordinates sent in the network packet (`cursorX/Y/Z`) are computed from raw floats and stay correct, but the bot's actual look direction is wrong — off-center toward the block corner. On dense builds or in tight spaces this is very plausibly the real root cause behind a lot of the band-aid logic already in the file (Sprint 41 facing-aware retries, Sprint 52 scaffold-and-six-face fallback, retry/backoff loops) — those are compensating for aim that's subtly wrong rather than fixing the underlying geometry.

**Fix:** don't re-floor inside the immutable ops. Either use a real `Vec3` from the `vec3` npm package (it already implements the exact prismarine-vector contract you're hand-rolling, and Mineflayer already depends on it transitively) or, if you want to keep the lightweight shim, store floored ints in `self.x/y/z` only for `.floored()`'s own return value, and let `.offset()`/`.plus()`/etc. return objects with the *unfloored* result.

### 2. `bot.bestHarvestTool` doesn't exist — `mine` silently always digs bare-handed

```js
// index.js, the 'mine' action
const harvestTool = bot.bestHarvestTool(fresh);   // <-- this method is NOT on `bot`
```

`bestHarvestTool` is exposed only by `mineflayer-pathfinder`, as `bot.pathfinder.bestHarvestTool` (confirmed in its `index.js` and `index.d.ts`). `bot.bestHarvestTool` is `undefined`, so calling it throws a `TypeError`, which lands in your existing `catch (toolErr)` and gets logged at `debug` level as "bestHarvestTool error, digging bare-handed" — meaning **tool auto-equip in `mine` has never worked, and the failure is invisible unless someone is grepping debug logs.**

There's a second, compounding bug even if you fix the typo: `bot.pathfinder.bestHarvestTool(block)` returns the `Item` directly (`Item | null` per its own `.d.ts` and source), not `{ item, time }`. Your code does:

```js
if (harvestTool) {
  await bot.equip(harvestTool.item, 'hand');  // harvestTool.item is undefined
}
```

So fixing only the `.pathfinder` typo would still pass `undefined` to `bot.equip`, throw again, and fall through to the same bare-handed fallback. Compare with the (correct) usage 100 lines later in the `place` natural-terrain-clearing branch:

```js
const bestTool = bot.pathfinder.bestHarvestTool(targetBlock);
if (bestTool) await bot.equip(bestTool, 'hand');   // correct
```

**Impact:** this is the single most-used action (`mine`), and it never equips a pickaxe/axe/shovel. Beyond being slow, this is functionally broken for any block requiring a specific tool to drop an item (stone, ores, etc. — bare-handed mining of those breaks the block but drops nothing). Your `blockMined`/`mineComplete` events would still report success and increment the C# side's inventory model, while the real Minecraft inventory silently gets nothing — a state-divergence bug that `sendBotStatus()` reconciliation would eventually catch, but only after the damage is done.

**Fix:**
```js
const harvestTool = bot.pathfinder.bestHarvestTool(fresh);
if (harvestTool) { try { await bot.equip(harvestTool, 'hand'); } catch (...) { ... } }
```

### 3. `craft` can never use a crafting table — the table-pathfinding logic is dead code

```js
const recipes = bot.recipesFor(itemEntry.id, null, null, null);
//                                                        ^^^^ craftingTable param
...
const recipe = recipes[0];
if (recipe.requiresTable) { /* find table, pathfind, etc. */ }
```

I checked `mineflayer`'s `craft.js`:

```js
function recipesFor (itemType, metadata, minResultCount, craftingTable) {
  ...
  if (requirementsMetForRecipe(recipe, minResultCount, craftingTable)) results.push(recipe)
}
function requirementsMetForRecipe (recipe, minResultCount, craftingTable) {
  if (recipe.requiresTable && !craftingTable) return false   // <-- filters BEFORE you ever see it
  ...
}
```

Passing `craftingTable = null` means `recipesFor` **excludes every recipe that requires a table before it returns**. So `recipes[0]` can never be a table recipe — `recipe.requiresTable` is always `false`, and your "pathfind to nearest crafting table" branch is unreachable dead code. Worse: for any item whose *only* recipe requires a table (tools, furnace, chest, most mid/late-game items), `recipesFor` returns an empty array and you throw `No recipe found for: ${itemName}` even when a crafting table is sitting right next to the bot and the bot has all the ingredients.

This is a significant, easily-reproducible capability gap for an agent meant to build things — it can only ever craft the small set of table-free recipes (sticks, torches, etc.), and a Sprint-2a feature ("craft case now pathfinds to the nearest crafting table") that the file's own header comment claims exists has never actually run.

**Fix:** call `bot.recipesFor(itemEntry.id, null, null, true)` (or do two calls — once with `false`, once with `true` — and merge) so table recipes are included; your existing manual table-search/pathfind/recheck logic then becomes reachable and does what it was clearly designed to do.

## Other real bugs / silent-failure modes

**WS broadcast isn't gated by the handshake, and there's no connection exclusivity.** `agentSocket = ws` is set the instant a TCP connection lands, before any handshake message arrives. `sendEvent()` only checks `agentSocket?.readyState === 1` — it has no idea whether that socket authenticated. Two consequences: (1) an unauthenticated client receives every broadcast event (position, inventory, health, chat) the moment it connects, even though `WS_TOKEN` is supposed to gate it; (2) a second connection — malicious or just a stray reconnect — silently replaces `agentSocket`, and the legitimate C# host stops receiving any events with no error surfaced anywhere. Given Sprint 32 explicitly hardened command auth, this is a gap worth closing in the same pass: don't assign `agentSocket = ws` (or don't call `sendEvent`) until `isAuthenticated` is true, and probably reject/close new connections while one is already active.

**No reconnect logic at all.** `bot.on('kicked', ...)` and `bot.on('error', ...)` just log and forward an event — there's no retry/backoff to reconnect to the MC server. If the bot gets kicked (server restart, "You have died" / `idle` AFK kick, brief network hiccup), the process is permanently stuck pointing at a dead `bot` object. Every subsequent dispatched action throws a raw `TypeError` from accessing properties on a disconnected client, and `classifyError()`'s `not spawned`/`not connected` string matchers won't catch these because the real error text (`"Cannot read properties of undefined"`, etc.) doesn't contain those substrings — so the C# side gets `reasonCode: 'unknown_error'` for what's actually a totally recoverable "we got disconnected" condition.

**Dig-failure memory doesn't survive a single dispatch call.** `digFailures` (and the `isDigExhausted` closure built on it) is declared fresh inside each `case 'mine':` block. If the C# planner issues several small "mine 1 X" actions back-to-back rather than one "mine N", each call starts the failure counter at zero, so an unbreakable/unreachable block gets the full 3-retry treatment every single time instead of being remembered as exhausted. Worth hoisting to a process-level `Map` if mining is ever dispatched in small increments.

**`scanNearbyEntities` detects threats but the adapter has no way to act on them.** The commit you linked (`06e7798`) turns on `bot.on('physicsTick', scanNearbyEntities)`, wiring up `entityObserved` for the C# replan loop — but there's no `attack`, `flee`, `equipArmor`, or `shield` action anywhere in the dispatcher. The LLM evaluator can now *learn* a creeper is 4 blocks away and has nothing it can tell the adapter to do about it besides `move` away manually (with no obstacle/explosion-radius awareness in pathfinding). That's a glaring loop-completion gap given this is the newest feature in the codebase.

**`place`'s "scaffold, step-aside, mine-natural-terrain" fallback paths can leave the bot in a worse position than it started.** E.g. the no-walkable-ground fallback:
```js
await bot.pathfinder.goto(new pfGoals.GoalNear(x + 2, y, z, 1));
```
blindly walks to `x+2` with zero ground/void check — if that's a cliff edge or a hole, the bot just walks off it. Every other branch in that function checks `groundBelow` first; this one fallback doesn't.

## Smaller things worth tightening

- `movements.js` imports `{ Movements }` from `'mineflayer-pathfinder'` (named import) while `index.js` destructures it off the default export (`const { pathfinder, Movements, goals } = mflPathfinder`). Both happen to resolve to the same class today, but it's an inconsistent import style in the same module graph — pick one.
- `createMovements()` is a hard singleton, constructed once and never updated. Comment claims this is safe because "entityIntersections are refreshed per-path," which is true for collision state, but it also means you can never give different actions different movement profiles (e.g., `canDig: false` while placing blocks so the bot doesn't path-break blueprint walls, vs. `canDig: true` while mining) — right now every action shares identical movement permissions.
- `findBestBlock()`'s alias-ID resolution (`aliasNames`/`acceptableIds`) is duplicated verbatim three times inside the `mine` case (inside `findBestBlock`, then again right after pathfinding succeeds to validate `fresh`). Pull it into a small helper to avoid the two copies drifting.
- `classifyError()`'s `missing_item` branch has an operator-precedence trap that happens to work but reads like a bug: `m.includes('not in inventory') || m.includes('missing') || m.includes('not found') && m.includes('item')` — without parens it's easy to misread as `(A || B || C) && D`. It actually evaluates as `A || B || (C && D)` (correct), but I'd parenthesize it explicitly so the next person doesn't "fix" it incorrectly.

## Mineflayer capability gaps (what's missing for a more capable agent)

I diffed your action set (`move/mine/place/wander/findFlatArea/status/chat/craft/smelt/findReachableBlock/queryBlocks/queryEntities`) against what core Mineflayer actually exposes (`bot.attack`, `bot.useOn`, `bot.activateBlock`, `bot.activateEntity`, `bot.consume`, `bot.openBlock`/chest windows, `bot.equip` for armor slots, bed/sleep, anvil, enchanting table, fishing, mounting). Given threat detection just shipped, the highest-leverage additions are:

1. **Combat/flee actions** (`bot.attack(entity)`, plus a "retreat" goal using `pfGoals.GoalInvert`/`GoalAvoidEntity` or just a vector away from the nearest hostile) — closes the loop on the new `entityObserved` event with zero new infrastructure since you already track hostile positions.
2. **`activateBlock`** for doors/levers/buttons/trapdoors and pressure-plate-free entry, and for opening chests — right now the bot can `place` and `mine` but can't *use* anything, including its own crafting table without going through `bot.craft`'s internal `activateBlock` call.
3. **`consume`/eating** — you track `food` in status but there's no action to eat when hungry, which matters a lot for any long-running building/mining session in survival.
4. **Armor equip** (`bot.equip(item, 'torso'|'legs'|'head'|'feet')`) — trivial addition, meaningful survivability gain once combat exists.
5. **Bucket interactions** (`bot.activateItem` while holding a water/lava bucket) — useful for clearing lava hazards while mining and for quick scaffolding-via-water-bucket tricks.
6. **Container deposit/withdraw** (`bot.openBlock(chest)` + `clickWindow`) for actual base-building/storage workflows, since your agent's whole purpose seems to be constructing structures.
7. A pre-dig hazard check (liquid/gravity-block above the target, `LIQUID_BLOCK_NAMES` is already defined and used in `findFlatArea` but never checked in `mine` before digging) would prevent classic "mined straight into lava/gravel" deaths.