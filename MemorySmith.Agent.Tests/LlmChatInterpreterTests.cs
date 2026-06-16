using Agent.Core;
using Agent.Planning;
using Agent.Planning.Llm;

namespace MemorySmith.Agent.Tests;

[TestFixture]
[Description("LlmChatInterpreter: distance gate, rate limiting, LLM + pattern fallback pipeline")]
public sealed class LlmChatInterpreterTests
{
    private static readonly Position BotAtOrigin = new(0, 64, 0);
    private static readonly Position PlayerNear  = new(10, 64, 10);
    private static readonly Position PlayerFar   = new(100, 64, 100);
    private static readonly WorldState EmptyState = new();
    private const string BotName = "AgentBot";

    private static LlmChatInterpreter MakeInterpreter(ILlmProvider? provider) =>
        new(provider ?? new NullLlmProvider(),
            new ChatInterpreter(DefaultOpts),
            new ChatRateLimiter(DefaultOpts),
            DefaultOpts);

    private static readonly ChatOptions DefaultOpts = new()
    {
        LlmEnabled                = false,
        PlayerCooldownSeconds     = 3,
        GlobalPerMinuteMax        = 5,
        MaxMessageLength          = 1024,
        MaxResponseDistanceBlocks = 64.0,
    };

    // ── Distance gate ───────────────────────────────────────────────────────────────────────

    [Test]
    public async Task FarPlayer_MultiPlayer_NotAddressed_IsIgnored()
    {
        var interp = MakeInterpreter(null);
        var result = await interp.InterpretAsync(
            "Player1", "hello everyone", BotName, 3,
            BotAtOrigin, PlayerFar, EmptyState);
        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.NotAddressed));
    }

    [Test]
    public async Task FarPlayer_SoloPlay_IsAddressed()
    {
        var interp = MakeInterpreter(null);
        var result = await interp.InterpretAsync(
            "Player1", "get me wood", BotName, 1,
            BotAtOrigin, PlayerFar, EmptyState);
        Assert.That(result.IntentType, Is.Not.EqualTo(ChatIntentType.NotAddressed));
    }

    // ── Pattern fast-path ───────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ClearGather_SkipsLlm_UsesPatternResult()
    {
        var counting = new CountingLlmProvider();
        var interp   = new LlmChatInterpreter(counting, new ChatInterpreter(DefaultOpts),
            new ChatRateLimiter(DefaultOpts), DefaultOpts);

        await interp.InterpretAsync("P1", "gather 32 cobblestone", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);

        Assert.That(counting.CallCount, Is.Zero,
            "Clear gather command should be handled by pattern matcher; LLM not called");
    }

    [Test]
    public async Task StopCommand_SkipsLlm_ReturnsCancelGoal()
    {
        var counting = new CountingLlmProvider();
        var interp   = new LlmChatInterpreter(counting, new ChatInterpreter(DefaultOpts),
            new ChatRateLimiter(DefaultOpts), DefaultOpts);

        var result = await interp.InterpretAsync("P1", "stop", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);

        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CancelGoal));
        Assert.That(counting.CallCount, Is.Zero);
    }

    // ── LLM integration ────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AmbiguousMessage_UsesLlmWhenAvailable()
    {
        var opts = DefaultOpts with { LlmEnabled = true, LlmProvider = "ollama" };
        var llm  = new FixedLlmProvider("""
            {"addressed":"yes","intent":"gather","item":"diamond","count":5,"response":"Going mining!"}
            """);
        var interp = new LlmChatInterpreter(llm, new ChatInterpreter(opts),
            new ChatRateLimiter(opts), opts);

        var result = await interp.InterpretAsync(
            "P1", "let's go mining for shiny stuff", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);

        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName,   Is.EqualTo("GatherItem:diamond"));
    }

    [Test]
    public async Task LlmReturnsNull_FallsBackToPattern()
    {
        var opts   = DefaultOpts with { LlmEnabled = true, LlmProvider = "ollama" };
        var interp = new LlmChatInterpreter(new NullLlmProvider(), new ChatInterpreter(opts),
            new ChatRateLimiter(opts), opts);

        var result = await interp.InterpretAsync("P1", "gather 10 wood", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);

        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CreateGoal));
        Assert.That(result.GoalName,   Is.EqualTo("GatherItem:oak_log"));
    }

    // ── Rate limiting ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task RateLimited_FallsBackToPatternResult()
    {
        var opts    = DefaultOpts with { LlmEnabled = true, LlmProvider = "ollama", PlayerCooldownSeconds = 100 };
        var limiter = new ChatRateLimiter(opts);
        limiter.TryAcquire("P1", out _); // exhaust per-player slot

        var llm    = new FixedLlmProvider("{\"addressed\":\"yes\",\"intent\":\"help\",\"response\":\"LLM help\"}");
        var interp = new LlmChatInterpreter(llm, new ChatInterpreter(opts), limiter, opts);

        // "stop" is handled by pattern matcher regardless
        var result = await interp.InterpretAsync("P1", "stop", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);

        Assert.That(result.IntentType, Is.EqualTo(ChatIntentType.CancelGoal));
    }

    // ── Max message length ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task MessageTruncation_DoesNotThrow()
    {
        var opts   = DefaultOpts with { MaxMessageLength = 10 };
        var interp = MakeInterpreter(null);
        // 100-char message truncated to 10 before interpretation
        var result = await interp.InterpretAsync("P1", new string('g', 100), BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState with { });
        // Should not throw; result can be any interpretation
        Assert.That(result, Is.Not.Null);
    }

    // ── RecordBotSpoke ────────────────────────────────────────────────────────────────────────

    [Test]
    public void RecordBotSpoke_DoesNotThrow()
        => Assert.DoesNotThrow(() => MakeInterpreter(null).RecordBotSpoke());
}

// ── Mock providers ────────────────────────────────────────────────────────────────────────────

internal sealed class NullLlmProvider : ILlmProvider
{
    public string ProviderName => "null";
    public bool IsAvailable    => false;
    public Task<string?> CompleteAsync(string s, string u, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}

internal sealed class FixedLlmProvider(string json) : ILlmProvider
{
    public string ProviderName => "fixed";
    public bool IsAvailable    => true;
    public Task<string?> CompleteAsync(string s, string u, CancellationToken ct = default)
        => Task.FromResult<string?>(json);
}

internal sealed class CountingLlmProvider : ILlmProvider
{
    public int CallCount { get; private set; }
    public string ProviderName => "counting";
    public bool IsAvailable    => true;
    public Task<string?> CompleteAsync(string s, string u, CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult<string?>(null);
    }
}
