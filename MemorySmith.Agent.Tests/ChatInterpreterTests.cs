using Agent.Core;
using Agent.Planning;
using Agent.Planning.Llm;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Sprint 39 P1-C: assertions updated from ChatInterpretation to IntentDraft.
///   - null return  = not addressed  (was IntentType.NotAddressed)
///   - .Intent      = semantic intent string (was .IntentType enum)
///   - .Response    = in-game reply text (unchanged)
///   - .X/.Y/.Z     = navigate coords (was GoalParameters dict)
///   - Gather/Build tests updated: ChatInterpreter no longer handles them
///     (Sprint 35 P1-D) — those messages now return "clarify" from the
///     pattern-only fallback so LlmChatInterpreter routes them to the LLM.
/// </summary>
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

    private static Task<IntentDraft?> Interpret(ChatInterpreter interp,
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
        Assert.That(result, Is.Not.Null, "Solo player should always be addressed.");
    }

    [Test]
    public async Task MultiPlayer_NotAddressedWithoutName()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "let's go mining", onlinePlayers: 3, playerPos: FarPlayerPos);
        Assert.That(result, Is.Null, "Multi-player message not mentioning bot name should not be addressed.");
    }

    [Test]
    public async Task MultiPlayer_AddressedWhenStartsWithBotName()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "AgentBot get me wood", onlinePlayers: 3);
        Assert.That(result, Is.Not.Null, "Message mentioning bot name should be addressed.");
    }

    [Test]
    public async Task MultiPlayer_AddressedAfterBotSpokeRecently()
    {
        var interp = new ChatInterpreter(Opts);
        interp.RecordBotSpoke();
        var result = await Interpret(interp, "thanks", onlinePlayers: 2);
        Assert.That(result, Is.Not.Null, "Continuation message within window should be addressed.");
    }

    // ── Gather (now routes to LLM via clarify fallback) ──────────────────────
    // Sprint 35 P1-D removed gather/build/craft from ChatInterpreter.
    // ChatInterpreter returns "clarify" so LlmChatInterpreter routes them to the LLM.

    [Test]
    public async Task Gather_WoodAlias_ReturnsClarify()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "get me some wood");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Intent, Is.EqualTo("clarify"),
            "Gather commands fall through to 'clarify' so LlmChatInterpreter routes them to the LLM.");
    }

    [Test]
    public async Task Gather_WithCount_ReturnsClarify()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "gather 64 cobblestone");
        Assert.That(result!.Intent, Is.EqualTo("clarify"),
            "Count-qualified gather also falls through to LLM path.");
    }

    [Test]
    public async Task Build_HouseAlias_ReturnsClarify()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "build a house");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Intent, Is.EqualTo("clarify"),
            "Build commands fall through to LLM path since Sprint 35 P1-D.");
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Cancel_StopKeyword()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "stop");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Intent, Is.EqualTo("cancel"));
        Assert.That(result.Response, Is.Not.Empty);
    }

    [Test]
    public async Task Cancel_QuitKeyword()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "quit what you're doing");
        Assert.That(result!.Intent, Is.EqualTo("cancel"));
    }

    // ── Status ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Status_WhatAreYouDoing()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "what are you doing?");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Intent, Is.EqualTo("status"));
        Assert.That(result.Response, Is.Not.Empty);
    }

    // ── Help ──────────────────────────────────────────────────────────────────

    [Test]
    public async Task Help_ListsCommands()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "help");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Intent, Is.EqualTo("help"));
        Assert.That(result.Response, Does.Contain("gather").Or.Contain("get").IgnoreCase);
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    [Test]
    public async Task Navigate_GoToCoords()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "go to 100 64 200");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Intent, Is.EqualTo("navigate"));
        Assert.That(result.X, Is.EqualTo(100));
        Assert.That(result.Y, Is.EqualTo(64));
        Assert.That(result.Z, Is.EqualTo(200));
    }

    [Test]
    public async Task Navigate_ComeHere()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "come here");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Intent, Is.EqualTo("navigate"));
        // null X/Y/Z signals "follow player" to HandleChatEventAsync
        Assert.That(result.X, Is.Null);
        Assert.That(result.Y, Is.Null);
        Assert.That(result.Z, Is.Null);
    }

    // ── Unknown / clarify ─────────────────────────────────────────────────────

    [Test]
    public async Task Unknown_ReturnsUnknownWithResponse()
    {
        var interp = new ChatInterpreter(Opts);
        var result = await Interpret(interp, "asdfghjkl qwerty");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Intent, Is.EqualTo("clarify"));
        Assert.That(result.Response, Is.Not.Empty);
    }

    // ── Max length truncation ─────────────────────────────────────────────────

    [Test]
    public async Task LongMessage_DoesNotThrow()
    {
        var shortOpts = Opts with { MaxMessageLength = 5 };
        var interp    = new ChatInterpreter(shortOpts);
        // 200-char message — solo player so always addressed; result may be clarify
        var result = await interp.InterpretAsync(
            "Player1", new string('x', 200), BotName, 1, BotPos, PlayerPos, Empty);
        Assert.That(result, Is.Not.Null);
    }
}
