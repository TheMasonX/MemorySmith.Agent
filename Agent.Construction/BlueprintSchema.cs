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
