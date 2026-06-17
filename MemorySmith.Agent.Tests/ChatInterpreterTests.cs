using Agent.Core;
using Agent.Planning;
using Agent.Planning.Llm;

namespace MemorySmith.Agent.Tests;

[TestFixture]
[Description("ChatInterpreter: directed-at-bot heuristics, intent parsing, aliases, response generation")]
public sealed class ChatInterpreterTests
{
    private const string BotName = "AgentBot";
    private static readonly Position BotPos    = new(0, 64, 0);
    private static readonly Position PlayerPos = new(5, 64, 5);
    private static readonly Position FarPlayerPos = new(200, 64, 200);
    private static readonly WorldState Empty   = new();

    private static readonly ChatOptions Opts = new()
    {
        MaxMessageLength          = 1024,
        MaxResponseDistanceBlocks = 64.0,
        ConversationWindowSeconds = 60,
    };

    private static Task<ChatInterpretation> Interpret(ChatInterpreter interp,
        string message, int onlinePlayers = 1, WorldState? state = null,
        Position? playerPos = null) =>
        interp.InterpretAsync("Player1", message, BotName, onlinePlayers,
            BotPos, playerPos ?? PlayerPos, state ?? Empty);

    // ── IsDirectedAtBot ───────────────────────────────────────────────────────

    [Test]
    public async Task SoloPlayer_AlwaysAddressed()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "hello", onlinePlayers: 1);
        Assert.That(result.IntentType, Is.Not.EqualTo(ChatIntentType.NotAddressed));
    }

    [Test]
    public async Task MultiPlayer_NotAddressedWithoutName()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "let's go mining", onlinePlayers: 3, playerPos: FarPlayerPos);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.NotAddressed));
    }

    [Test]
    public async Task MultiPlayer_AddressedWhenStartsWithBotName()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "AgentBot get me wood", onlinePlayers: 3);
        Assert.That(result.IntentType, Is.Not.EqualTo(ChatIntentType.NotAddressed));
    }

    [Test]
    public async Task MultiPlayer_AddressedAfterBotSpokeRecently()
    {
        var interp = new ChatInterpreter(Opts);
        interp.RecordBotSpoke();
        var result = await Interpret(interp, "thanks", onlinePlayers: 2);
        Assert.That(result.IntentType, Is.Not.EqualTo(ChatIntentType.NotAddressed));
    }

    // ── Gather ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Gather_WoodAlias_ReturnsOakLog()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "get me some wood");
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName,   Is.EqualTo("GatherItem:oak_log"));
    }

    [Test]
    public async Task Gather_WithCount_ParsesCount()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "gather 64 cobblestone");
        Assert.That(result.IntentType,                        Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName,                          Is.EqualTo("GatherItem:cobblestone"));
        Assert.That(result.GoalParameters?["count"], Is.EqualTo(64));
    }

    [Test]
    public async Task Gather_DefaultCount_Is10()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "mine some iron");
        Assert.That(result.GoalName,                          Is.EqualTo("GatherItem:iron_ore"));
        Assert.That(result.GoalParameters?["count"], Is.EqualTo(10));
    }

    [Test]
    public async Task Gather_CobbleAlias()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "collect 32 cobble");
        Assert.That(result.GoalName, Is.EqualTo("GatherItem:cobblestone"));
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task Build_HouseAlias_ReturnsSmallHouse()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "build a house");
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName,   Is.EqualTo("Build:small-house"));
    }

    [Test]
    public async Task Build_ShelterAlias_ReturnsSmallHouse()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "build me a shelter");
        Assert.That(result.GoalName, Is.EqualTo("Build:small-house"));
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Cancel_StopKeyword()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "stop");
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CancelGoal));
        Assert.That(result.Response,   Is.Not.Empty);
    }

    [Test]
    public async Task Cancel_QuitKeyword()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "quit what you're doing");
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CancelGoal));
    }

    // ── Status ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Status_WhatAreYouDoing()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "what are you doing?");
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.QueryStatus));
        Assert.That(result.Response,   Is.Not.Empty);
    }

    // ── Help ──────────────────────────────────────────────────────────────────

    [Test]
    public async Task Help_ListsCommands()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "help");
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.QueryHelp));
        Assert.That(result.Response,   Does.Contain("gather").Or.Contain("get").IgnoreCase);
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    [Test]
    public async Task Navigate_GoToCoords()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "go to 100 64 200");
        Assert.That(result.IntentType,                  Is.EqualTo(ChatIntentType.NavigateTo));
        Assert.That(result.GoalParameters?["x"], Is.EqualTo(100));
        Assert.That(result.GoalParameters?["y"], Is.EqualTo(64));
        Assert.That(result.GoalParameters?["z"], Is.EqualTo(200));
    }

    [Test]
    public async Task Navigate_ComeHere()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "come here");
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.NavigateTo));
    }

    // ── Unknown ───────────────────────────────────────────────────────────────

    [Test]
    public async Task Unknown_ReturnsUnknownWithResponse()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "asdfghjkl qwerty");
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.Unknown));
        Assert.That(result.Response,   Is.Not.Empty);
    }

    // ── Bot name stripping ────────────────────────────────────────────────────

    [Test]
    public async Task BotName_StrippedBeforeParsing()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await interp.InterpretAsync(
            "Player1", "AgentBot, get me 32 cobble", BotName, 2,
            BotPos, PlayerPos, Empty);
        Assert.That(result.IntentType,                        Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName,                          Is.EqualTo("GatherItem:cobblestone"));
        Assert.That(result.GoalParameters?["count"], Is.EqualTo(32));
    }

    // ── Max length truncation ─────────────────────────────────────────────────

    [Test]
    public async Task LongMessage_TruncatedToMaxLength()
    {
        var shortOpts = Opts with { MaxMessageLength = 5 };
        var interp    = new ChatInterpreter(shortOpts);
        // 200-char message — should not throw; result may be Unknown
        var result = await interp.InterpretAsync(
            "Player1", new string('x', 200), BotName, 1, BotPos, PlayerPos, Empty);
        Assert.That(result, Is.Not.Null);
    }
}
