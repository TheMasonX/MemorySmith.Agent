namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Searches the world knowledge base for observations, notes, and other in-world context.
/// When the best result's snippet contains coordinate patterns (e.g. "at (10, 64, -20)"
/// or "x:10 y:64 z:-20"), the tool emits nearestX/nearestY/nearestZ in its output data
/// so downstream tools (e.g. MoveToTool) can navigate there via context carry.
/// </summary>
public sealed class SearchMemoryTool : ITool
{
    private static readonly Regex CoordPattern = new(
        @"(?:at|coordinates?|pos(?:ition)?)\s*[=:≈~]?\s*\(?\s*(?<x>-?\d+)\s*[,;]\s*(?<y>-?\d+)\s*[,;]\s*(?<z>-?\d+)\s*\)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CoordLabelsPattern = new(
        @"\b[xyz]\s*[:=]\s*(?<x>-?\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IMemoryGateway _memory;

    public SearchMemoryTool(IMemoryGateway memory) => _memory = memory;

    public string Name => "SearchMemory";

    public string Description => "Searches the world knowledge base for spatial observations, block data, biome notes, and in-world exploration history. Routes to the world KB instance (see WorldKbUrl in appsettings).";

    public JsonElement InputSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search query for the world knowledge base" },
            "limit": { "type": "integer", "description": "Maximum number of results to return (default 10)" }
          },
          "required": ["query"]
        }
        """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var query = arguments.TryGetProperty("query", out var q) ? q.GetString()
                    : throw new ArgumentException("SearchMemory requires a 'query' parameter.");
        var limit = arguments.TryGetProperty("limit", out var l) && l.TryGetInt32(out var parsedLimit)
            ? Math.Max(1, parsedLimit)
            : 10;

        var results = await _memory.SearchAsync(query!, ct).ConfigureAwait(false);
        var limitedResults = results.Take(limit).ToList();
        var bestPageId = limitedResults.FirstOrDefault()?.PageId;
        var bestSnippet = limitedResults.FirstOrDefault()?.Snippet;

        // Attempt to extract coordinates from the best result's snippet.
        // Priority: parenthesized "at (x, y, z)" pattern first, then labeled "x: n" pattern.
        int? coordX = null, coordY = null, coordZ = null;
        if (bestSnippet is not null)
        {
            var coordMatch = CoordPattern.Match(bestSnippet);
            if (coordMatch.Success)
            {
                coordX = int.Parse(coordMatch.Groups["x"].Value);
                coordY = int.Parse(coordMatch.Groups["y"].Value);
                coordZ = int.Parse(coordMatch.Groups["z"].Value);
            }
            else
            {
                // Fallback: look for labeled x:/y:/z: patterns in the snippet
                var labelMatches = CoordLabelsPattern.Matches(bestSnippet);
                foreach (Match m in labelMatches)
                {
                    var val = int.Parse(m.Groups["x"].Value);
                    _ = m.Value[0] is 'x' or 'X' ? coordX = val
                        : m.Value[0] is 'y' or 'Y' ? coordY = val
                        : coordZ = val;
                }
            }
        }

        var data = new Dictionary<string, object?>
        {
            ["query"] = query,
            ["results"] = limitedResults,
            ["bestPageId"] = bestPageId,
            ["count"] = limitedResults.Count,
        };

        // Emit structured coordinate hints for context carry to downstream tools
        if (coordX.HasValue && coordY.HasValue && coordZ.HasValue)
        {
            data["nearestX"] = coordX.Value;
            data["nearestY"] = coordY.Value;
            data["nearestZ"] = coordZ.Value;
        }

        return new ToolResult(true, $"Found {limitedResults.Count} result(s).", data);
    }
}
