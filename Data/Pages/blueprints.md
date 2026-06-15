# Blueprints and Construction

Building tasks use blueprints rather than naive one-by-one LLM block placement. This solves scale, consistency, and performance.

## Blueprint Page Format

Each blueprint is a MemorySmith markdown page with a structured header:

```markdown
# GothicCathedral
Tags: Gothic, Cathedral, Medieval
Materials: StoneBricks x 5000, Glass x 800, Wood x 300
Dimensions: 50x20x100
Variants: survival-ready, decorative

Description:
A large Gothic cathedral with twin towers, pointed arches, and a rose window.
Inspired by Notre Dame.

Plan:
  Floor 1: Lay foundation (50x100).
  Tower A: 6x6 wide, 20 tall.
  Tower B: 6x6 wide, 20 tall.
  Walls: Pointed arch windows every 5 blocks.
  Roof: Sloped with cross at top.
```

Pages are editable via the Blazor UI (markdown editor) and versioned. Users or agents can add new blueprint pages retrievable via `SearchMemory("Blueprint Gothic")`.

## IBlueprintRepository

```csharp
Task<Blueprint?> GetAsync(string blueprintId, ...);
Task<IReadOnlyList<Blueprint>> SearchAsync(string query, ...);
Task<string> SaveAsync(Blueprint blueprint, ...);
```

Backed by MemorySmith pages — no separate database needed.

## IArchitect

Given high-level style requirements, generates or selects a blueprint programmatically:

```csharp
Task<Blueprint> GenerateBlueprintAsync(string description, string[] styleTags, ...);
```

Example: `AbbeyArchitect` produces a cathedral floorplan given tags `["Gothic", "Cathedral"]` — no LLM needed for known styles.

## ConstructBlueprint Tool

1. Reads the blueprint page via `IBlueprintRepository.GetAsync`.
2. Optionally runs through a blueprint compiler that converts the Plan section to a `PlaceBlock` action list.
3. Enqueues `PlaceBlock` actions in `ActionQueue`.
4. Falls back to direct block placement if no compiler output is available.

## Stamps and Fallback

- **Stamps**: if a schematic file is embedded in the blueprint page, the adapter can load it via Mineflayer's build commands in bulk (fast).
- **Fallback**: direct `PlaceBlock` calls one-by-one (slow but universal).

## Implementation Status

| Component | Status |
|---|---|
| `Blueprint` record | ✅ Defined |
| `IArchitect` interface | ✅ Defined |
| `IBlueprintRepository` interface | ✅ Defined |
| `ConstructBlueprint` tool | ⬜ Phase 3 |
| Blueprint compiler | ⬜ Phase 3 |
| `AbbeyArchitect` implementation | ⬜ Phase 3 |
| Litematica schematic import | ⬜ Phase 5 |
