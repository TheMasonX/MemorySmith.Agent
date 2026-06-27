namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Goals;

/// <summary>
/// Sprint 44 (TSK-0079): Decomposes a <see cref="SmeltGoal"/> into smelting actions
/// via <see cref="HtnTaskLibrary.DecomposeSmeltItem"/>.
///
/// Previously, all smelt intents were routed through <c>CraftItemGoal</c> →
/// <c>CraftItemGoalDecomposer</c> → <c>CraftItemTool</c>, which never exercised the
/// adapter's dedicated <c>case 'smelt':</c> handler. This decomposer ensures that
/// smelt intents produce <c>SmeltItem</c> actions instead.
/// </summary>
public sealed class SmeltGoalDecomposer(HtnTaskLibrary taskLibrary) : IGoalDecomposer
{
    /// <inheritdoc />
    public bool CanHandle(IGoal goal) => goal is SmeltGoal;

    /// <inheritdoc />
    public ActionPlan Decompose(IGoal goal, WorldState state)
    {
        var sg = (SmeltGoal)goal;
        var actions = taskLibrary.DecomposeSmeltItem(sg.InputItem, sg.Count, state);
        return new ActionPlan(goal.Name, goal.Phases.ToArray(), actions);
    }
}
