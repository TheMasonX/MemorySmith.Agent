# Minecraft Command Reference

A quick-reference guide for Minecraft server commands (Java Edition 1.16+).

## Teleportation
- `/tp <target> <x> <y> <z>` — Teleport target to coordinates
- `/tp <target> <destination>` — Teleport target to another player
- `/teleport <x> <y> <z>` — Self-teleport to coordinates

## Item / Inventory
- `/give <target> <item> [count]` — Give items to a player (e.g. `/give @p diamond 64`)
- `/clear <target> [item]` — Clear items from inventory
- `/replaceitem` — Replace item in specific inventory slot

## World / Environment
- `/time set <day|night|noon|midnight|0-24000>` — Set world time
- `/weather <clear|rain|thunder> [duration]` — Set weather
- `/gamerule <rule> [value]` — Change game rules (doDaylightCycle, keepInventory, etc.)
- `/difficulty <peaceful|easy|normal|hard>` — Set difficulty

## Spawning / Summoning
- `/summon <entity> [x y z]` — Summon an entity (e.g. `/summon lightning_bolt`, `/summon creeper ~ ~ ~`)
- `/kill <target>` — Kill entities/players
- `/xp <amount> <target>` — Give experience points

## Blocks / Building
- `/setblock <x> <y> <z> <block>` — Set a single block
- `/fill <x1> <y1> <z1> <x2> <y2> <z2> <block>` — Fill a region with blocks
- `/clone` — Clone a region to another location

## Game Mode
- `/gamemode <creative|survival|adventure|spectator> [target]` — Change game mode

## Effects
- `/effect give <target> <effect> [duration] [amplifier]` — Apply status effect
- `/effect clear <target>` — Clear all effects

## Target Selectors
- `@p` — Nearest player
- `@a` — All players
- `@s` — Self (the executor)
- `@r` — Random player
- `@e` — All entities

## Notes for Agent Use
- Commands require operator (OP) permissions on dedicated servers
- On LAN worlds (Open to LAN), /op does not work — all players have equal rights
- Commands are sent via chat prefixed with `/`
- The agent should verify it has permission before attempting commands
