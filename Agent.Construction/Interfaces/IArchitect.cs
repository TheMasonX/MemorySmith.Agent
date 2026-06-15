namespace Agent.Construction;

/// <summary>
/// Generates or selects blueprints from high-level style requirements.
/// For example, AbbeyArchitect produces a cathedral floorplan given tags
/// ["Gothic", "Cathedral", "Medieval"].
///
/// The generated blueprint is stored as a MemorySmith page so it can be
/// retrieved and refined in future sessions.
/// </summary>
public interface IArchitect
{
    Task<Blueprint> GenerateBlueprintAsync(
        string description,
        string[] styleTags,
        CancellationToken cancellationToken = default);
}
