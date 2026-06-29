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

    // Sprint 51: fixed to use distinct named groups (x/y/z) instead of single group "x".
    // The axis label is captured as literal text in the axis group; the numeric value
    // is in the val group. This makes the regex self-documenting and safe against
    // accidental group-name reuse.0+
    private static readonly Regex CoordLabelsPattern = new(
        @"\b(?<axis>[xyz])\s*[:=]\s*(?<val>-?\d+)\b",
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
        // Sprint 53 (TSK-0193): wrap search in try/catch so transient failures
        // return a graceful ToolResult rather than crashing the tool loop.
        try
        {
            return await ExecuteSearchAsync(arguments, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // cooperative cancellation — propagate
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Search failed: {ex.Message}");
        }
    }

    private async Task<ToolResult> ExecuteSearchAsync(JsonElement arguments, CancellationToken ct)
    {
        var query = arguments.TryGetProperty("query", out var q) ? q.GetString()
                    : throw new ArgumentException("SearchMemory requires a 'query' parameter.");
        var limit = arguments.TryGetProperty("limit", out var l) && l.TryGetInt32(out var parsedLimit)
            ? Math.Max(1, parsedLimit)
            : 10;

        var results = await _memory.SearchAsync(query!, ct).ConfigureAwait(false);
        var limitedResults = results.Take(limit).ToList();
        // bestPageId always points to the top-ranked result for backward compatibility
        // (consumers that don't need coordinates still want the best page reference).
        var bestPageId = limitedResults.FirstOrDefault()?.PageId;

        // Sprint 51: scan ALL results for the first hit with valid coordinates,
        // not just the top-ranked result. This ensures the bot finds known resource
        // locations even when they don't appear in the #1 search result.
        // Priority: parenthesized "at (x, y, z)" pattern first, then labeled "x: n" pattern.
        int? coordX = null, coordY = null, coordZ = null;
        foreach (var result in limitedResults)
        {
            if (result.Snippet is not { } snippet) continue;

            var coordMatch = CoordPattern.Match(snippet);
            if (coordMatch.Success
                && int.TryParse(coordMatch.Groups["x"].Value, out var cx)
                && int.TryParse(coordMatch.Groups["y"].Value, out var cy)
                && int.TryParse(coordMatch.Groups["z"].Value, out var cz))
            {
                coordX = cx; coordY = cy; coordZ = cz;
                break;
            }

            // Fallback: look for labeled x:/y:/z: patterns in the snippet
            var labelMatches = CoordLabelsPattern.Matches(snippet);
            int? lx = null, ly = null, lz = null;
            foreach (Match m in labelMatches)
            {
                if (!int.TryParse(m.Groups["val"].Value, out var val)) continue;
                var axis = m.Groups["axis"].Value;
                if (axis is "x" or "X") lx = val;
                else if (axis is "y" or "Y") ly = val;
                else if (axis is "z" or "Z") lz = val;
            }
            if (lx.HasValue && ly.HasValue && lz.HasValue)
            {
                coordX = lx; coordY = ly; coordZ = lz;
                break;
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
