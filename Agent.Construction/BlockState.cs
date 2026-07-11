namespace Agent.Construction;

using System.Text;

/// <summary>
/// Sprint 60 Wave D (TSK-0245): Structured block state replacing the bare
/// <c>string?</c> on <see cref="PlacementBlock.BlockState"/>. Encapsulates
/// block state properties as a dictionary of key-value pairs.
///
/// Example usage:
/// <code>
/// // From YAML/legend: blockState: half=top,waterlogged=true
/// var state = BlockState.Parse("half=top,waterlogged=true");
/// var wire  = state.ToWireString(); // "half=top,waterlogged=true"
///
/// // Empty state
/// var empty = new BlockState();
/// empty.ToWireString(); // ""
/// </code>
/// </summary>
public sealed record BlockState
{
    /// <summary>
    /// Block state properties. Keys are property names (e.g. "half", "shape", "waterlogged"),
    /// values are property values (e.g. "top", "inner_left", "true").
    /// Empty when no properties are set.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a <see cref="BlockState"/> from a flat wire-format string.
    /// Expected format: comma-separated key=value pairs, e.g. "half=top,waterlogged=true".
    /// Returns <c>null</c> when <paramref name="wire"/> is null or empty.
    /// </summary>
    public static BlockState? Parse(string? wire)
    {
        if (string.IsNullOrWhiteSpace(wire))
            return null;

        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pairs = wire.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var pair in pairs)
        {
            var eqIdx = pair.IndexOf('=');
            if (eqIdx > 0)
            {
                var key = pair[..eqIdx].Trim();
                var val = pair[(eqIdx + 1)..].Trim();
                if (key.Length > 0)
                    props[key] = val;
            }
        }

        return props.Count > 0
            ? new BlockState { Properties = props }
            : null;
    }

    /// <summary>
    /// Serializes this block state to the flat wire format expected by the adapter.
    /// Format: comma-separated key=value pairs, e.g. "half=top,waterlogged=true".
    /// Returns empty string when no properties are set.
    /// </summary>
    public string ToWireString()
    {
        if (Properties.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        var first = true;
        foreach (var kv in Properties)
        {
            if (!first) sb.Append(',');
            sb.Append(kv.Key);
            sb.Append('=');
            sb.Append(kv.Value);
            first = false;
        }
        return sb.ToString();
    }
}
