namespace Agent.Construction;

/// <summary>
/// Parses a MemorySmith wiki blueprint page into a <see cref="Blueprint"/> metadata
/// record and a flat list of <see cref="PlacementBlock"/> records ready for execution.
///
/// Expected wiki page format:
/// <code>
/// ---
/// id: small-house
/// name: Small Survival House
/// tags: house, starter, survival
/// dimensions: 9x5x7
/// materials: cobblestone x 63, oak_planks x 70
/// description: A compact survival starter home.
/// ---
///
/// ## Layers
///
/// ### Y=0 (Floor)
/// CCCCCCCCC
/// ...
///
/// ## Legend
/// - `.` = air (skip)
/// - `C` = cobblestone
/// ...
/// </code>
///
/// Legend is optional — <see cref="DefaultLegend"/> is applied if absent.
/// Grid lines following a ### Y=N header are collected until the next heading or EOF.
/// Cells with null block IDs (air) are skipped; only solid blocks become PlacementBlocks.
/// </summary>
public static class BlueprintParser
{
    /// <summary>
    /// Default block-symbol legend. A null value means "skip this cell" (air).
    /// Pages may override individual entries by including a ## Legend section.
    /// </summary>
    public static readonly IReadOnlyDictionary<char, string?> DefaultLegend =
        new Dictionary<char, string?>
        {
            ['.'] = null,               // air — skip
            ['C'] = "cobblestone",
            ['P'] = "oak_planks",
            ['L'] = "oak_log",
            ['G'] = "glass_pane",
            ['D'] = "oak_door",
            ['T'] = "torch",
            ['B'] = "crafting_table",
            ['X'] = "chest",
            ['S'] = "oak_slab",
            ['H'] = "red_bed",          // bed head; foot auto-placed by Mineflayer
            ['F'] = null,               // bed foot — auto-placed when head is placed, skip
        };

    /// <summary>
    /// Parses a wiki blueprint markdown string into metadata and a flat block list.
    /// Returns empty metadata and an empty block list on null/empty input.
    /// </summary>
    public static (Blueprint Metadata, IReadOnlyList<PlacementBlock> Blocks)
        Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return (new Blueprint(), []);

        var lines = markdown.Split('\n');

        var frontmatter = ExtractFrontmatter(lines, out var contentStart);
        var metadata    = ParseFrontmatter(frontmatter, markdown);
        var legend      = ParseLegend(lines, contentStart);
        var blocks      = ParseLayers(lines, contentStart, legend);

        return (metadata, blocks);
    }

    private static Dictionary<string, string> ExtractFrontmatter(
        string[] lines, out int contentStart)
    {
        contentStart = 0;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (lines.Length == 0 || lines[0].Trim() != "---")
            return fields;

        int closeIdx = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---") { closeIdx = i; break; }

            var colonIdx = lines[i].IndexOf(':');
            if (colonIdx > 0)
            {
                var key   = lines[i][..colonIdx].Trim();
                var value = lines[i][(colonIdx + 1)..].Trim();
                if (key.Length > 0 && value.Length > 0)
                    fields[key] = value;
            }
        }

        contentStart = closeIdx >= 0 ? closeIdx + 1 : 0;
        return fields;
    }

    private static Blueprint ParseFrontmatter(
        Dictionary<string, string> fields, string rawMarkdown)
    {
        var id   = fields.GetValueOrDefault("id", string.Empty);
        var name = fields.GetValueOrDefault("name", string.Empty);

        var tags = Array.Empty<string>();
        if (fields.TryGetValue("tags", out var tagsRaw))
            tags = tagsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var dimensions = new Dimensions();
        if (fields.TryGetValue("dimensions", out var dimRaw))
        {
            var parts = dimRaw.Split('x', StringSplitOptions.TrimEntries);
            if (parts.Length >= 3
                && int.TryParse(parts[0], out var dx)
                && int.TryParse(parts[1], out var dy)
                && int.TryParse(parts[2], out var dz))
                dimensions = new Dimensions(dx, dy, dz);
        }

        var materials = Array.Empty<MaterialEntry>();
        if (fields.TryGetValue("materials", out var matRaw))
            materials = ParseMaterials(matRaw);

        var description = fields.GetValueOrDefault("description", string.Empty);

        return new Blueprint
        {
            Id          = id,
            Name        = name,
            Tags        = tags,
            Dimensions  = dimensions,
            Materials   = materials,
            Description = description,
            RawMarkdown = rawMarkdown,
        };
    }

    private static MaterialEntry[] ParseMaterials(string raw)
    {
        // Format: "cobblestone x 63, oak_planks x 70"  OR  "cobblestone: 63, oak_planks: 70"
        var entries = new List<MaterialEntry>();
        foreach (var entry in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            // Try "block x qty" first
            var xIdx = entry.IndexOf(" x ", StringComparison.OrdinalIgnoreCase);
            if (xIdx > 0)
            {
                var block  = entry[..xIdx].Trim();
                var qtyStr = entry[(xIdx + 3)..].Trim();
                if (block.Length > 0 && int.TryParse(qtyStr, out var qty))
                    entries.Add(new MaterialEntry(block, qty));
                continue;
            }

            // Fall back to "block: qty"
            var colonIdx = entry.IndexOf(':');
            if (colonIdx > 0)
            {
                var block  = entry[..colonIdx].Trim();
                var qtyStr = entry[(colonIdx + 1)..].Trim();
                if (block.Length > 0 && int.TryParse(qtyStr, out var qty))
                    entries.Add(new MaterialEntry(block, qty));
            }
        }
        return [.. entries];
    }

    // ── Legend ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Internal parsed legend entry: block ID plus optional facing/blockState
    /// extracted from per-symbol annotations.
    /// </summary>
    private sealed record LegendEntry(
        string? BlockId,
        string? Facing = null,
        string? BlockState = null);

    private static Dictionary<char, LegendEntry> ParseLegend(string[] lines, int contentStart)
    {
        // Start from the defaults; individual entries may be overridden.
        var legend = new Dictionary<char, LegendEntry>();
        foreach (var kv in DefaultLegend)
            legend[kv.Key] = new LegendEntry(kv.Value);

        bool inLegend = false;
        for (int i = contentStart; i < lines.Length; i++)
        {
            var line    = lines[i];
            var trimmed = line.Trim();

            if (trimmed.StartsWith("## Legend", StringComparison.OrdinalIgnoreCase))
            {
                inLegend = true;
                continue;
            }

            if (!inLegend) continue;

            // Stop at next section header (## or #, but not ###)
            if (trimmed.StartsWith("## ") || (trimmed.StartsWith("# ") && !trimmed.StartsWith("###")))
                break;

            // Acceptable legend line formats:
            //   - `.` = air (skip)
            //   - `C` = cobblestone
            //   * 'D' = oak_door
            //   C = cobblestone
            //   - `D` = oak_door | facing: north
            //   - `S` = oak_slab | facing: up | blockState: half=top
            var stripped = trimmed.TrimStart('-', '*', ' ');
            if (stripped.Length < 3) continue;

            char sym;
            int  eqIdx;

            if (stripped[0] == '`' || stripped[0] == '\'')
            {
                // Backtick or quote-delimited symbol: `C` = cobblestone
                if (stripped.Length < 4) continue;
                sym   = stripped[1];
                eqIdx = stripped.IndexOf('=', 2);
            }
            else
            {
                // Bare symbol: C = cobblestone
                sym   = stripped[0];
                eqIdx = stripped.IndexOf('=', 1);
            }

            if (eqIdx < 0) continue;

            var rawValue = stripped[(eqIdx + 1)..].Trim();

            // Split on '|' to separate block ID from annotations
            var parts = rawValue.Split('|', StringSplitOptions.TrimEntries);
            var blockId = parts[0];

            // Strip inline comments like "(skip - auto-placed)" or "(head only)"
            var parenIdx = blockId.IndexOf('(');
            if (parenIdx > 0)
                blockId = blockId[..parenIdx].Trim();

            // Parse annotations: facing: X, blockState: X
            string? facing = null;
            string? blockState = null;
            for (int a = 1; a < parts.Length; a++)
            {
                var annotation = parts[a];
                var colonIdx = annotation.IndexOf(':');
                if (colonIdx <= 0) continue;

                var key = annotation[..colonIdx].Trim();
                var val = annotation[(colonIdx + 1)..].Trim();

                if (key.Equals("facing", StringComparison.OrdinalIgnoreCase) && val.Length > 0)
                    facing = val;
                else if (key.Equals("blockState", StringComparison.OrdinalIgnoreCase) && val.Length > 0)
                    blockState = val;
            }

            string? resolvedBlockId;
            if (blockId.Equals("air",  StringComparison.OrdinalIgnoreCase)
             || blockId.Equals("skip", StringComparison.OrdinalIgnoreCase)
             || blockId.Equals("null", StringComparison.OrdinalIgnoreCase)
             || blockId.Length == 0)
                resolvedBlockId = null;
            else
                resolvedBlockId = blockId;

            legend[sym] = new LegendEntry(resolvedBlockId, facing, blockState);
        }

        return legend;
    }

    // ── Layers ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<PlacementBlock> ParseLayers(
        string[] lines, int contentStart, Dictionary<char, LegendEntry> legend)
    {
        var blocks   = new List<PlacementBlock>();
        int currentY = -1;
        int currentZ = 0;
        bool inLayer = false;

        for (int i = contentStart; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            // Detect "### Y=N" layer headers (any trailing description is fine)
            if (trimmed.StartsWith("###"))
            {
                var yVal = ExtractYLevel(trimmed);
                if (yVal >= 0)
                {
                    currentY = yVal;
                    currentZ = 0;
                    inLayer  = true;
                }
                else
                {
                    inLayer = false;
                }
                continue;
            }

            // Any ## or # heading ends the current layer
            if (trimmed.StartsWith("##") || trimmed.StartsWith("# "))
            {
                inLayer = false;
                continue;
            }

            if (!inLayer || currentY < 0) continue;

            // Skip blank lines, code fences, and HTML comments
            if (trimmed.Length == 0
             || trimmed.StartsWith("```")
             || trimmed.StartsWith("<!--"))
                continue;

            // A valid grid row must consist entirely of characters present in the legend.
            // This guards against parsing prose descriptions as grid data.
            if (!IsValidGridRow(trimmed, legend)) continue;

            for (int x = 0; x < trimmed.Length; x++)
            {
                var ch = trimmed[x];
                if (!legend.TryGetValue(ch, out var entry) || entry.BlockId is null)
                    continue; // air or auto-placed — skip

                blocks.Add(new PlacementBlock(
                    x, currentY, currentZ, entry.BlockId,
                    entry.Facing, entry.BlockState));
            }

            currentZ++;
        }

        return blocks;
    }

    private static int ExtractYLevel(string headerLine)
    {
        // Find "Y=" followed by digits anywhere in the line, e.g. "### Y=2 (Mid walls)"
        var idx = headerLine.IndexOf("Y=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return -1;

        int start = idx + 2;
        int end   = start;
        while (end < headerLine.Length && char.IsDigit(headerLine[end]))
            end++;

        return (end > start && int.TryParse(headerLine[start..end], out var y)) ? y : -1;
    }

    private static bool IsValidGridRow(string line, Dictionary<char, LegendEntry> legend)
    {
        // Every character in the line must be a known legend symbol.
        // Prose lines will contain characters like spaces, letters not in the legend,
        // punctuation, etc. — those are rejected here.
        foreach (var ch in line)
            if (!legend.ContainsKey(ch)) return false;
        return line.Length > 0;
    }
}
