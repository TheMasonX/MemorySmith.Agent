namespace Agent.Vision;

using Agent.Core;

/// <summary>
/// Deterministic spatial analysis over world state.
/// Computes environmental metrics (flatness ratio, tree coverage, water proximity)
/// that inform building site selection and resource planning.
/// No LLM needed — algorithms run directly over Mineflayer block data.
/// </summary>
public interface ISpatialAnalyzer
{
    Task<SpatialAnalysis> AnalyzeAsync(WorldState state, CancellationToken cancellationToken = default);
}

public record SpatialAnalysis(
    double FlatnessRatio,
    double TreeCoverage,
    double WaterProximity,
    string[] Tags
);
