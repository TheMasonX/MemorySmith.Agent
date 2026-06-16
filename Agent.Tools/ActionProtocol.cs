namespace Agent.Tools;

/// <summary>
/// Wire-protocol action names sent to the Node.js/Mineflayer adapter over WebSocket.
///
/// Each tool sets <see cref="Agent.Core.ActionData.Tool"/> to one of these constants
/// in its <c>ExecuteAsync</c> implementation. The WebSocket bridge forwards the value
/// as-is — it no longer lowercases the name (see ADR-010 in Data/Pages/decisions.md).
///
/// This makes the tool the single source of truth for its wire name and eliminates
/// the implicit lowercase-convention dependency.
/// </summary>
public static class ActionProtocol
{
    /// <summary>Navigate the bot to (x, y, z). Node.js action: <c>move</c>.</summary>
    public const string Move = "move";

    /// <summary>Mine blocks of a given type near the bot. Node.js action: <c>mine</c>.</summary>
    public const string Mine = "mine";

    /// <summary>Place a block at (x, y, z). Node.js action: <c>place</c>.</summary>
    public const string Place = "place";

    /// <summary>Request current bot status (position, health, inventory). Node.js action: <c>status</c>.</summary>
    public const string Status = "status";

    /// <summary>Walk in a random nearby direction. Node.js action: <c>wander</c>.</summary>
    public const string Wander = "wander";

    /// <summary>Send a chat message in-game. Node.js action: <c>chat</c>.</summary>
    public const string Chat = "chat";

    /// <summary>Craft an item from inventory materials. Node.js action: <c>craft</c>.</summary>
    public const string Craft = "craft";

    /// <summary>Smelt an item in a nearby furnace. Node.js action: <c>smelt</c>.</summary>
    public const string Smelt = "smelt";

    /// <summary>Scan for a flat, buildable area near the bot. Node.js action: <c>findFlatArea</c>.</summary>
    public const string FindFlatArea = "findFlatArea";
}
