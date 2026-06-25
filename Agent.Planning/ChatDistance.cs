namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Shared horizontal-distance calculator for chat addressing.
///
/// AUD-48-003: Centralises the distance formula so deterministic
/// (<see cref="ChatInterpreter"/>) and LLM (<see cref="LlmChatInterpreter"/>)
/// chat paths use exactly the same calculation. Previously the two paths drifted:
/// one used X/Z only, the other used X/Y/Z — producing different results for
/// players at the same horizontal position but different heights (caves, towers,
/// cliffs, underground mining).
///
/// Minecraft chat has unlimited range in vanilla, but for bot-addressing we gate
/// on horizontal distance only: vertical separation does not affect whether a
/// player can call out to the bot.
/// </summary>
public static class ChatDistance
{
    /// <summary>
    /// Euclidean horizontal distance (X/Z only). Y is intentionally excluded.
    /// </summary>
    public static double Horizontal(Position a, Position b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
