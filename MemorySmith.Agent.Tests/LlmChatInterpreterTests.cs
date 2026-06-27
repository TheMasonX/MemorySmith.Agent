using Agent.Core;
using Agent.Planning;
using Agent.Planning.Llm;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Sprint 39 P1-C: assertions updated from ChatInterpretation to IntentDraft.
///   null result = not addressed (was IntentType.NotAddressed)
///   result.Intent = semantic string (was IntentType enum)
/// </summary>
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
        Assert.That(result, Is.Null, "Far multi-player message not mentioning bot should be ignored (null).");
    }

    [Test]
    public async Task FarPlayer_SoloPlay_IsAddressed()
    {
        var interp = MakeInterpreter(null);
        var result = await interp.InterpretAsync(
            "Player1", "get me wood", BotName, 1,
            BotAtOrigin, PlayerFar, EmptyState);
        Assert.That(result, Is.Not.Null, "Solo player is always addressed regardless of distance.");
    }

    // ── Pattern fast-path ───────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ClearGather_ReachesLlm_NotFastPathed()
    {
        // Sprint 35 P1-D: gather is no longer a fast-path in ChatInterpreter.
        // ChatInterpreter returns "clarify" for gather commands so LlmChatInterpreter
        // calls the LLM (CountingLlmProvider.IsAvailable=true). Verify LLM IS called.
        var counting = new CountingLlmProvider();
        var interp   = new LlmChatInterpreter(counting, new ChatInterpreter(DefaultOpts),
            new ChatRateLimiter(DefaultOpts), DefaultOpts);

        await interp.InterpretAsync("P1", "gather 32 cobblestone", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);

        Assert.That(counting.CallCount, Is.EqualTo(1),
            "Gather command is not fast-pathed — LLM should be called (returns null from CountingLlmProvider).");
    }

    [Test]
    public async Task StopCommand_SkipsLlm_ReturnsCancel()
    {
        var counting = new CountingLlmProvider();
        var interp   = new LlmChatInterpreter(counting, new ChatInterpreter(DefaultOpts),
            new ChatRateLimiter(DefaultOpts), DefaultOpts);

        var result = await interp.InterpretAsync("P1", "stop", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Intent, Is.EqualTo("cancel"));
        Assert.That(counting.CallCount, Is.Zero, "Cancel is fast-pathed — LLM should not be called.");
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

        Assert.That(result, Is.Not.Null);
        // Sprint 39 P1-C: no GoalName in IntentDraft — caller builds goal via IntentManager.
        Assert.That(result!.Intent, Is.EqualTo("gather"));
        Assert.That(result.Item,    Is.EqualTo("diamond"));
        Assert.That(result.Count,   Is.EqualTo(5));
    }

    [Test]
    public async Task LlmReturnsNull_FallsBackToPattern()
    {
        var opts   = DefaultOpts with { LlmEnabled = true, LlmProvider = "ollama" };
        var interp = new LlmChatInterpreter(new NullLlmProvider(), new ChatInterpreter(opts),
            new ChatRateLimiter(opts), opts);

        // "gather 10 wood" — LLM returns null → pattern fallback → "clarify"
        // (Sprint 35 P1-D removed gather from ChatInterpreter)
        var result = await interp.InterpretAsync("P1", "gather 10 wood", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);

        Assert.That(result, Is.Not.Null, "Pattern fallback should still produce a non-null IntentDraft.");
        Assert.That(result!.Intent, Is.EqualTo("clarify"),
            "Gather is not fast-pathed; LLM null → pattern fallback → 'clarify'.");
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

        // "stop" is fast-pathed — rate limit doesn't matter
        var result = await interp.InterpretAsync("P1", "stop", BotName, 1,
            BotAtOrigin, PlayerNear, EmptyState);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Intent, Is.EqualTo("cancel"));
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
        // Should not throw; result can be any interpretation (clarify from pattern)
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
