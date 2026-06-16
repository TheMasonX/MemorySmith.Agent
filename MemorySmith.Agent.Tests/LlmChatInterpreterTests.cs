using Agent.Core;
using Agent.Planning;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Unit tests for <see cref="LlmChatInterpreter"/>: distance gate, rate limit fallback,
/// pattern-match fast-path, LLM integration, and IChatInterpreter contract.
/// </summary>
[TestFixture]
[Description("LlmChatInterpreter: distance gate, rate limiting, LLM + fallback pipeline")]
public sealed class LlmChatInterpreterTests
{
    private static readonly Position BotAtOrigin = new(0, 64, 0);
    private static readonly Position PlayerNear   = new(10, 64, 10);  // ~14 blocks
    private static readonly Position PlayerFar    = new(100, 64, 100); // ~141 blocks
    private static readonly WorldState EmptyState  = new();
    private const string BotName = "AgentBot";

    // ── Distance gate ────────────────────────────────────────────────

    [Test]
    public async Task FarPlayer_MultiPlayer_NotAddressed_IsIgnored()
    {
        var interp = MakeInterpreter(llm: null);
        // Multiple players, player is far away, message has no bot name → not addressed
        var result = await interp.InterpretAsync(
            "Player1", "hello everyone", BotName, 3,
            BotAtOrigin, PlayerFar, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.NotAddressed));
    }

    [Test]
    public async Task FarPlayer_SinglePlayer_IsStillAddressed()
    {
        var interp = MakeInterpreter(llm: null);
        // Single player — always addressed regardless of distance
        var result = await interp.InterpretAsync(
            "Player1", "get me some wood", BotName, 1,
            BotAtOrigin, PlayerFar, EmptyState);
        Assert.That(result.IntentType, Is.Not.EqualTo(ChatIntentType.NotAddressed));
    }

    [Test]
    public async Task NearPlayer_UnambiguousGather_UsesPatternFast()
    {
        var interp = MakeInterpreter(llm: null); // no LLM needed for clear patterns
        var result = await interp.InterpretAsync(
            "Player1", "gather 32 cobblestone", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName, Is.EqualTo("GatherItem:cobblestone"));
    }

    // ── Rate limiting ────────────────────────────────────────────────

    [Test]
    public async Task RateLimited_FallsBackToPatternMatch()
    {
        var limiter = new ChatRateLimiter();
        // Exhaust the rate limit for Player1
        limiter.TryAcquire("Player1", out _);
        limiter.TryAcquire("Player1", out _); // second call within cooldown → blocked

        var llm    = new MockLlmClient(new ChatInterpretation(ChatIntentType.QueryHelp, Response: "LLM response"));
        var interp = new LlmChatInterpreter(llm, new ChatInterpreter(), limiter);

        // "stop" matches pattern matching regardless of LLM
        var result = await interp.InterpretAsync(
            "Player1", "stop", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);

        // Even without LLM, pattern matching handles "stop"
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CancelGoal));
    }

    // ── LLM integration ───────────────────────────────────────────────

    [Test]
    public async Task LlmAvailable_AmbiguousMessage_UsesLlmResult()
    {
        var llmResult = new ChatInterpretation(
            ChatIntentType.CreateGoal, "GatherItem:diamond",
            new Dictionary<string, object?> { ["count"] = 5 },
            "Sure, going mining for diamonds!");

        var llm    = new MockLlmClient(llmResult);
        var interp = MakeInterpreter(llm);

        // "let's go mining" is ambiguous for pattern matching but LLM can parse it
        var result = await interp.InterpretAsync(
            "Player1", "let's go mining", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);

        // The pattern matcher would return QueryStatus/Unknown; LLM returns CreateGoal
        // LLM should be used here since pattern matching returns a non-confident result
        Assert.That(result, Is.EqualTo(llmResult));
    }

    [Test]
    public async Task LlmUnavailable_FallsBackToPatternMatch()
    {
        var llm    = new FailingLlmClient(); // always returns null
        var interp = MakeInterpreter(llm);

        var result = await interp.InterpretAsync(
            "Player1", "gather 10 wood", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);

        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName,   Is.EqualTo("GatherItem:oak_log"));
    }

    [Test]
    public async Task LlmNotAddressed_NotOverridesPatternConfident()
    {
        // If pattern matching already returned CreateGoal (confident), don't call LLM
        var llm    = new MockLlmClient(new ChatInterpretation(ChatIntentType.NotAddressed));
        var callCount = 0;
        var countingLlm = new CountingLlmClient(llm, () => callCount++);
        var interp = MakeInterpreter(countingLlm);

        // "gather 64 cobblestone" is a confident pattern match → LLM should NOT be called
        var result = await interp.InterpretAsync(
            "Player1", "gather 64 cobblestone", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);

        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(callCount, Is.EqualTo(0), "LLM should not be called for confident pattern matches");
    }

    // ── RecordBotSpoke ───────────────────────────────────────────────

    [Test]
    public void RecordBotSpoke_DoesNotThrow()
    {
        var interp = MakeInterpreter(llm: null);
        Assert.DoesNotThrow(() => interp.RecordBotSpoke());
    }

    // ── IChatInterpreter contract ───────────────────────────────────────────

    [Test]
    public async Task NullLlm_StillWorks_ViaPatternMatch()
    {
        var interp = MakeInterpreter(llm: null); // no LLM at all
        var result = await interp.InterpretAsync(
            "Player1", "help", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.QueryHelp));
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static LlmChatInterpreter MakeInterpreter(IChatLlmClient? llm) =>
        new(llm, new ChatInterpreter(), new ChatRateLimiter());
}

// ── Mock LLM clients ──────────────────────────────────────────────────

internal sealed class MockLlmClient(ChatInterpretation result) : IChatLlmClient
{
    public Task<ChatInterpretation?> EvaluateAsync(
        string botName, Position botPosition, string username, string message,
        int onlinePlayers, Position? playerPosition, string? currentGoal,
        CancellationToken ct = default)
        => Task.FromResult<ChatInterpretation?>(result);
}

internal sealed class FailingLlmClient : IChatLlmClient
{
    public Task<ChatInterpretation?> EvaluateAsync(
        string botName, Position botPosition, string username, string message,
        int onlinePlayers, Position? playerPosition, string? currentGoal,
        CancellationToken ct = default)
        => Task.FromResult<ChatInterpretation?>(null);
}

internal sealed class CountingLlmClient(IChatLlmClient inner, Action onCall) : IChatLlmClient
{
    public Task<ChatInterpretation?> EvaluateAsync(
        string botName, Position botPosition, string username, string message,
        int onlinePlayers, Position? playerPosition, string? currentGoal,
        CancellationToken ct = default)
    {
        onCall();
        return inner.EvaluateAsync(botName, botPosition, username, message,
            onlinePlayers, playerPosition, currentGoal, ct);
    }
}
