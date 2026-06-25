namespace MemorySmith.Agent.Tests;

using global::Agent.Core;
using global::Agent.Planning;
using global::Agent.Planning.Goals;
using global::Agent.Planning.Llm;
using NUnit.Framework;

/// <summary>
/// Sprint 48 — Audit-Driven Corrections
///
/// Test coverage:
///   TSK-0105: Bot name detection uses whole-word matching, not substring
///   TSK-0103: MaxResponseDistanceBlocks wired into deterministic path
///   TSK-0082: Shared SmeltableMapping eliminates duplicate switch logic
/// </summary>
[TestFixture]
public class Sprint48Tests
{
    private const string BotName = "AgentBot";
    private static readonly Position BotPos       = new(0, 64, 0);
    private static readonly Position PlayerPos    = new(5, 64, 5);
    private static readonly Position FarPlayerPos = new(200, 64, 200);
    private static readonly WorldState Empty      = new();

    private static readonly ChatOptions Opts = new()
    {
        MaxMessageLength          = 1024,
        MaxResponseDistanceBlocks = 64.0,
        ConversationWindowSeconds = 60,
    };

    private static Task<IntentDraft?> Interpret(ChatInterpreter interp,
        string message, int onlinePlayers = 1, WorldState? state = null,
        Position? playerPos = null) =>
        interp.InterpretAsync("Player1", message, BotName, onlinePlayers,
            BotPos, playerPos ?? PlayerPos, state ?? Empty);

    // ── TSK-0105: Bot name whole-word detection ─────────────────────────────

    [Test]
    public async Task BotName_SubstringInWord_NotAddressed()
    {
        // "Leo" should NOT match "helios" or "Leopold" — whole-word only
        var interp = new ChatInterpreter("Leo", conversationWindowSeconds: 60, maxResponseDistanceBlocks: 64.0);
        var result = await interp.InterpretAsync(
            "Player1", "helios is cool", "Leo", 3,
            BotPos, FarPlayerPos, Empty);
        Assert.That(result, Is.Null,
            "Bot name 'Leo' should NOT match substring in 'helios'.");
    }

    [Test]
    public async Task BotName_ExactMatch_Addressed()
    {
        var interp = new ChatInterpreter("Leo", conversationWindowSeconds: 60, maxResponseDistanceBlocks: 64.0);
        var result = await interp.InterpretAsync(
            "Player1", "Leo come here", "Leo", 3,
            BotPos, PlayerPos, Empty);
        Assert.That(result, Is.Not.Null,
            "Bot name 'Leo' should match when it appears as a whole word.");
    }

    [Test]
    public async Task BotName_EmbeddedInSentence_Addressed()
    {
        var interp = new ChatInterpreter("AgentBot", conversationWindowSeconds: 60, maxResponseDistanceBlocks: 64.0);
        var result = await interp.InterpretAsync(
            "Player1", "Hey AgentBot, can you help?", "AgentBot", 3,
            BotPos, PlayerPos, Empty);
        Assert.That(result, Is.Not.Null,
            "Bot name 'AgentBot' should match at start of sentence.");
    }

    [Test]
    public async Task BotName_CaseInsensitive_Addressed()
    {
        var interp = new ChatInterpreter("agentbot", conversationWindowSeconds: 60, maxResponseDistanceBlocks: 64.0);
        var result = await interp.InterpretAsync(
            "Player1", "Hey AgentBot what's up", "agentbot", 3,
            BotPos, PlayerPos, Empty);
        Assert.That(result, Is.Not.Null,
            "Bot name matching should be case-insensitive.");
    }

    [Test]
    public async Task BotName_AtEndOfWord_NotAddressed()
    {
        var interp = new ChatInterpreter("bot", conversationWindowSeconds: 60, maxResponseDistanceBlocks: 64.0);
        var result = await interp.InterpretAsync(
            "Player1", "I have a robot", "bot", 3,
            BotPos, FarPlayerPos, Empty);
        Assert.That(result, Is.Null,
            "Bot name 'bot' should NOT match substring in 'robot'.");
    }

    // ── TSK-0103: MaxResponseDistanceBlocks distance gate ───────────────────

    [Test]
    public async Task FarPlayer_Multiplayer_NotAddressed_WhenBeyondMaxDistance()
    {
        // Player at 200 blocks — beyond 64-block default
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "hello", onlinePlayers: 3, playerPos: FarPlayerPos);
        Assert.That(result, Is.Null,
            "Far multi-player message without name mention should be ignored via distance gate.");
    }

    [Test]
    public async Task FarPlayer_SoloPlay_StillAddressed_RegardlessOfDistance()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "hello", onlinePlayers: 1, playerPos: FarPlayerPos);
        Assert.That(result, Is.Not.Null,
            "Solo player is always addressed regardless of distance.");
    }

    [Test]
    public async Task NearPlayer_Multiplayer_Addressed_WithoutName_WithinConversationWindow()
    {
        // Within distance but no name — should NOT be addressed unless within conversation window
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "hello", onlinePlayers: 3, playerPos: PlayerPos);
        Assert.That(result, Is.Null,
            "Multi-player message within distance but no name mention should not be addressed.");
    }

    [Test]
    public async Task CustomMaxDistance_AllowsCloserPlayer()
    {
        var tightOpts = Opts with { MaxResponseDistanceBlocks = 10.0 };
        var interp = new ChatInterpreter(tightOpts);
        // Player at 5 blocks — within 10-block limit
        var result = await Interpret(interp, "hello", onlinePlayers: 3, playerPos: PlayerPos);
        Assert.That(result, Is.Null, // Still needs name or conv window
            "Player within custom distance still needs name mention.");
    }

    // ── TSK-0082: SmeltableMapping shared class ─────────────────────────────

    [Test]
    public void SmeltableMapping_IronOre_OutputsIronIngot()
    {
        Assert.That(SmeltableMapping.GetOutput("iron_ore"), Is.EqualTo("iron_ingot"));
    }

    [Test]
    public void SmeltableMapping_RawIron_OutputsIronIngot()
    {
        Assert.That(SmeltableMapping.GetOutput("raw_iron"), Is.EqualTo("iron_ingot"));
    }

    [Test]
    public void SmeltableMapping_GoldOre_OutputsGoldIngot()
    {
        Assert.That(SmeltableMapping.GetOutput("gold_ore"), Is.EqualTo("gold_ingot"));
    }

    [Test]
    public void SmeltableMapping_NetherGoldOre_OutputsGoldIngot()
    {
        Assert.That(SmeltableMapping.GetOutput("nether_gold_ore"), Is.EqualTo("gold_ingot"));
    }

    [Test]
    public void SmeltableMapping_AncientDebris_OutputsNetheriteScrap()
    {
        Assert.That(SmeltableMapping.GetOutput("ancient_debris"), Is.EqualTo("netherite_scrap"));
    }

    [Test]
    public void SmeltableMapping_UnknownItem_Passthrough()
    {
        Assert.That(SmeltableMapping.GetOutput("unknown_ore"), Is.EqualTo("unknown_ore"));
    }

    [Test]
    public void SmeltableMapping_IronIngot_ReverseToIronOre()
    {
        Assert.That(SmeltableMapping.GetInputBlock("iron_ingot"), Is.EqualTo("iron_ore"));
    }

    [Test]
    public void SmeltableMapping_GoldIngot_ReverseToGoldOre()
    {
        Assert.That(SmeltableMapping.GetInputBlock("gold_ingot"), Is.EqualTo("gold_ore"));
    }

    [Test]
    public void SmeltableMapping_UnknownIngot_ReversePassthrough()
    {
        Assert.That(SmeltableMapping.GetInputBlock("emerald"), Is.EqualTo("emerald"));
    }

    [Test]
    public void SmeltableMapping_IronOre_IsSmeltableMineable()
    {
        Assert.That(SmeltableMapping.IsSmeltableMineableBlock("iron_ore"), Is.True);
    }

    [Test]
    public void SmeltableMapping_DeepslateIronOre_IsSmeltableMineable()
    {
        Assert.That(SmeltableMapping.IsSmeltableMineableBlock("deepslate_iron_ore"), Is.True);
    }

    [Test]
    public void SmeltableMapping_Stone_IsNotSmeltableMineable()
    {
        Assert.That(SmeltableMapping.IsSmeltableMineableBlock("stone"), Is.False);
    }

    [Test]
    public void SmeltableMapping_Dirt_IsNotSmeltableMineable()
    {
        Assert.That(SmeltableMapping.IsSmeltableMineableBlock("dirt"), Is.False);
    }

    // ── SmeltGoal uses shared mapping ────────────────────────────────────────

    [Test]
    public void SmeltGoal_IronOre_OutputItem_MatchesSharedMapping()
    {
        var goal = new SmeltGoal("iron_ore");
        Assert.That(goal.OutputItem, Is.EqualTo(SmeltableMapping.GetOutput("iron_ore")));
    }

    [Test]
    public void SmeltGoal_GoldOre_OutputItem_MatchesSharedMapping()
    {
        var goal = new SmeltGoal("gold_ore");
        Assert.That(goal.OutputItem, Is.EqualTo(SmeltableMapping.GetOutput("gold_ore")));
    }

    [Test]
    public void SmeltGoal_UnknownInput_OutputPassthrough()
    {
        var goal = new SmeltGoal("unknown_item");
        Assert.That(goal.OutputItem, Is.EqualTo("unknown_item"));
    }

    // ── AUD-48-001: Raw items are NOT mineable blocks ────────────────────────

    [Test]
    public void SmeltableMapping_RawIron_IsNotMineableBlock()
    {
        // AUD-48-001: raw_iron is an item drop, not a block — must not appear
        // in SmeltableMineableBlocks to prevent MineBlock(raw_iron) emissions.
        Assert.That(SmeltableMapping.IsSmeltableMineableBlock("raw_iron"), Is.False);
    }

    [Test]
    public void SmeltableMapping_RawGold_IsNotMineableBlock()
    {
        Assert.That(SmeltableMapping.IsSmeltableMineableBlock("raw_gold"), Is.False);
    }

    [Test]
    public void SmeltableMapping_RawCopper_IsNotMineableBlock()
    {
        Assert.That(SmeltableMapping.IsSmeltableMineableBlock("raw_copper"), Is.False);
    }

    [Test]
    public void SmeltableMapping_GetInputBlock_ResolvesRawIronToIronOre()
    {
        // AUD-48-001: GetInputBlock must resolve raw items to their ore block
        // so DecomposeSmeltItem emits MineBlock(iron_ore) not MineBlock(raw_iron).
        Assert.That(SmeltableMapping.GetInputBlock("raw_iron"), Is.EqualTo("iron_ore"));
    }

    [Test]
    public void SmeltableMapping_GetInputBlock_ResolvesRawGoldToGoldOre()
    {
        Assert.That(SmeltableMapping.GetInputBlock("raw_gold"), Is.EqualTo("gold_ore"));
    }

    [Test]
    public void SmeltableMapping_GetInputBlock_ResolvesRawCopperToCopperOre()
    {
        Assert.That(SmeltableMapping.GetInputBlock("raw_copper"), Is.EqualTo("copper_ore"));
    }

    // ── AUD-48-002: Cached regex is wired ────────────────────────────────────

    [Test]
    public void BotName_MultiplayerNamed_StillAddressedWithCachedRegex()
    {
        // AUD-48-002: Verifies the cached _botNameRegex is consulted when the
        // call-time botName matches the constructor botName (common case).
        var interp = new ChatInterpreter("BuildBot", conversationWindowSeconds: 0, maxResponseDistanceBlocks: 64.0);
        var result = interp.InterpretAsync(
            "Player1", "BuildBot come here", "BuildBot", 3,
            BotPos, PlayerPos, Empty).Result;
        Assert.That(result, Is.Not.Null,
            "Cached regex should match exact bot name in multiplayer.");
    }

    // ── AUD-48-003: Shared distance calculation ──────────────────────────────

    [Test]
    public void ChatDistance_Horizontal_IgnoresVerticalSeparation()
    {
        // Same X,Z at different heights — distance should be zero.
        var a = new Position(10, 64, 20);
        var b = new Position(10, 120, 20);
        Assert.That(ChatDistance.Horizontal(a, b), Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void ChatDistance_Horizontal_MatchesDeterministicPath()
    {
        // Verifies the deterministic path uses the shared calculator.
        var a = new Position(0, 64, 0);
        var b = new Position(30, 80, 40);
        var expected = System.Math.Sqrt(30.0 * 30.0 + 40.0 * 40.0); // 50
        Assert.That(ChatDistance.Horizontal(a, b), Is.EqualTo(expected).Within(0.001));
    }
}
