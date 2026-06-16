namespace MemorySmith.Agent.Tests;

using Agent.Core;
using Agent.Planning;

/// <summary>
/// Unit tests for <see cref="ChatInterpreter"/>: directed-at-bot heuristics,
/// intent parsing, item/blueprint alias resolution, and response generation.
/// </summary>
[TestFixture]
[Description("ChatInterpreter: directed-at heuristics, gather/build/cancel/status/help parsing")]
public sealed class ChatInterpreterTests
{
    private const string BotName = "AgentBot";
    private static readonly WorldState EmptyState = new();

    // ── IsDirectedAtBot ───────────────────────────────────────────────────────

    [Test]
    public void SoloPlayer_AlwaysAddressed()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "hello there", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.Not.EqualTo(ChatIntentType.NotAddressed));
    }

    [Test]
    public void MultiPlayer_NotAddressedWithoutName()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "let's go mining", BotName, onlinePlayers: 3, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.NotAddressed));
    }

    [Test]
    public void MultiPlayer_AddressedWhenStartsWithBotName()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "AgentBot get me wood", BotName, onlinePlayers: 3, EmptyState);
        Assert.That(result.IntentType, Is.Not.EqualTo(ChatIntentType.NotAddressed));
    }

    [Test]
    public void MultiPlayer_AddressedWhenStartsWithBotNameCaseInsensitive()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "agentbot stop", BotName, onlinePlayers: 2, EmptyState);
        Assert.That(result.IntentType, Is.Not.EqualTo(ChatIntentType.NotAddressed));
    }

    [Test]
    public void MultiPlayer_AddressedAfterBotSpokeRecently()
    {
        var interp = new ChatInterpreter();
        interp.RecordBotSpoke(); // simulate bot just spoke
        var result = interp.Interpret("Player1", "thanks", BotName, onlinePlayers: 2, EmptyState);
        Assert.That(result.IntentType, Is.Not.EqualTo(ChatIntentType.NotAddressed));
    }

    // ── Gather intent ─────────────────────────────────────────────────────────

    [Test]
    public void Gather_WoodAlias_ReturnsGatherItem_OakLog()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "get me some wood", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName,   Is.EqualTo("GatherItem:oak_log"));
    }

    [Test]
    public void Gather_WithCount_ParsesCount()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "gather 64 cobblestone", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName,   Is.EqualTo("GatherItem:cobblestone"));
        Assert.That(result.GoalParameters?["count"], Is.EqualTo(64));
    }

    [Test]
    public void Gather_DefaultCount_Is10()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "mine some iron", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName,   Is.EqualTo("GatherItem:iron_ore"));
        Assert.That(result.GoalParameters?["count"], Is.EqualTo(10));
    }

    [Test]
    public void Gather_CobbleAlias_ResolvesCobblestone()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "collect 32 cobble", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.GoalName, Is.EqualTo("GatherItem:cobblestone"));
    }

    [Test]
    public void Gather_ReturnsResponse()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "get wood", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.Response, Is.Not.Empty);
    }

    // ── Build intent ──────────────────────────────────────────────────────────

    [Test]
    public void Build_HouseAlias_ReturnsBuildSmallHouse()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "build a house", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName,   Is.EqualTo("Build:small-house"));
    }

    [Test]
    public void Build_ShelterAlias_ReturnsBuildSmallHouse()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "build me a shelter", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName,   Is.EqualTo("Build:small-house"));
    }

    // ── Cancel intent ─────────────────────────────────────────────────────────

    [Test]
    public void Cancel_StopKeyword_ReturnsCancelGoal()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "stop", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CancelGoal));
    }

    [Test]
    public void Cancel_QuitKeyword_ReturnsCancelGoal()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "quit what you're doing", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CancelGoal));
    }

    [Test]
    public void Cancel_ReturnsResponse()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "stop", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.Response, Is.Not.Empty);
    }

    // ── Status intent ─────────────────────────────────────────────────────────

    [Test]
    public void Status_WhatAreYouDoingKeyword_ReturnsQueryStatus()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "what are you doing?", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.QueryStatus));
    }

    [Test]
    public void Status_ReturnsResponse()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "status", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.QueryStatus));
        Assert.That(result.Response,   Is.Not.Empty);
    }

    // ── Help intent ───────────────────────────────────────────────────────────

    [Test]
    public void Help_HelpKeyword_ReturnsQueryHelp()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "help", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.QueryHelp));
    }

    [Test]
    public void Help_ResponseListsCommands()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "help", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.Response, Does.Contain("gather").Or.Contain("get").IgnoreCase);
    }

    // ── Navigation intent ─────────────────────────────────────────────────────

    [Test]
    public void NavigateTo_GoToCoords_ReturnsNavigateTo()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "go to 100 64 200", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.NavigateTo));
        Assert.That(result.GoalParameters?["x"], Is.EqualTo(100));
        Assert.That(result.GoalParameters?["y"], Is.EqualTo(64));
        Assert.That(result.GoalParameters?["z"], Is.EqualTo(200));
    }

    [Test]
    public void NavigateTo_ComeHere_ReturnsNavigateTo()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "come here", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.NavigateTo));
    }

    // ── Unknown intent ────────────────────────────────────────────────────────

    [Test]
    public void Unknown_Gibberish_ReturnsUnknownNotNull()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "asdfghjkl qwerty", BotName, onlinePlayers: 1, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.Unknown));
        Assert.That(result.Response,   Is.Not.Empty); // friendly fallback message
    }

    // ── Bot name stripping ────────────────────────────────────────────────────

    [Test]
    public void BotNamePrefix_IsStrippedBeforeParsing()
    {
        var interp = new ChatInterpreter();
        var result = interp.Interpret("Player1", "AgentBot, get me 32 cobble", BotName, onlinePlayers: 2, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName,   Is.EqualTo("GatherItem:cobblestone"));
        Assert.That(result.GoalParameters?["count"], Is.EqualTo(32));
    }

    // ── RecordBotSpoke ────────────────────────────────────────────────────────

    [Test]
    public void RecordBotSpoke_IsNotNull()
    {
        var interp = new ChatInterpreter();
        // Should not throw
        Assert.DoesNotThrow(() => interp.RecordBotSpoke());
    }
}
