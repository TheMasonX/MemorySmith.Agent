namespace Agent.Construction;

/// <summary>
/// A blueprint is a named, tagged build plan stored as a MemorySmith wiki page.
/// The page format is markdown with frontmatter-style fields followed by a Plan section.
///
/// Example page:
/// # GothicCathedral
/// Tags: Gothic, Cathedral, Medieval
/// Materials: StoneBricks x 5000, Glass x 800, Wood x 300
/// Dimensions: 50x20x100
/// Description: A large Gothic cathedral with twin towers, pointed arches, and a rose window.
/// Plan:
///   Floor 1: Lay foundation (50x100).
///   Tower A: 6x6 wide, 20 tall.
///   ...
///
/// The ConstructBlueprint tool reads this page and emits PlaceBlock actions.
/// </summary>
public record Blueprint
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string[] Tags { get; init; } = [];
    public MaterialEntry[] Materials { get; init; } = [];
    public Dimensions Dimensions { get; init; } = new();
    public string Description { get; init; } = string.Empty;
    public string Plan { get; init; } = string.Empty;
    public string RawMarkdown { get; init; } = string.Empty;
}

public record MaterialEntry(string Block, int Quantity);
public record Dimensions(int X = 0, int Y = 0, int Z = 0);

/// <summary>
/// A single block placement relative to the blueprint's build origin.
///
/// X = east, Y = up, Z = south (Minecraft right-hand coordinate convention).
/// The build origin is (0,0,0); all offsets are non-negative in a standard blueprint.
///
/// Produced by <see cref="BlueprintParser"/> and consumed by
/// <see cref="BlueprintExecutor"/> to emit PlaceBlock <see cref="Agent.Core.ActionData"/>.
/// </summary>
/// <param name="X">East offset from build origin.</param>
/// <param name="Y">Up offset from build origin.</param>
/// <param name="Z">South offset from build origin.</param>
/// <param name="BlockId">Minecraft block ID (e.g. "oak_door", "oak_slab").</param>
/// <param name="Facing">
/// Desired facing direction for orientation-sensitive blocks.
/// One of: north, south, east, west, up, down. <c>null</c> means no preference
/// (adapter tries all faces). Only meaningful for blocks like doors, beds,
/// stairs, slabs, furnaces.
/// </param>
/// <param name="BlockState">
/// Optional block state properties (e.g. "half=top", "shape=inner_left").
/// Passed through to the adapter for future use. <c>null</c> means default state.
/// </param>
public record PlacementBlock(int X, int Y, int Z, string BlockId,
    string? Facing = null, string? BlockState = null);
