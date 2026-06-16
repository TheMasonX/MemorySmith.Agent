namespace Agent.Core;

/// <summary>
/// Looks up <see cref="ItemSpec"/> records by item ID.
///
/// Backed by MemorySmith wiki pages at path "item-registry/{itemId}".
/// Returns null for unknown items — callers are responsible for falling back
/// to the LLM if needed (per D-003: deterministic-first planning).
///
/// Implementations MUST NOT call the LLM themselves.
/// </summary>
public interface IItemRegistry
{
    /// <summary>
    /// Returns the <see cref="ItemSpec"/> for the given item ID, or null if not found.
    /// <paramref name="itemId"/> is the short form without namespace prefix,
    /// e.g. "oak_log", "iron_ore", "diamond".
    /// </summary>
    Task<ItemSpec?> GetAsync(string itemId, CancellationToken ct = default);
}
