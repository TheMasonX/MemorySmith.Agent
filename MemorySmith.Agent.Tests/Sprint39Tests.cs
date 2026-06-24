namespace MemorySmith.Agent.Tests;

using System.Text.Json;
using global::Agent.Core;
using global::Agent.Planning;
using global::Agent.Planning.Goals;
using global::Agent.Planning.Llm;
using global::Agent.Tools;

/// <summary>
/// Sprint 39 first-half coverage:
///   - Stable goal IDs (every concrete IGoal returns a unique non-empty Guid)
///   - ConcurrentQueue / lifecycle: _cycleOutcomes cleared on SetGoal
///   - ILlmEvaluator.EvaluateAsync WorldState parameter present in signature
///   - IChatInterpreter.InterpretAsync returns IntentDraft? (null = not addressed)
///   - IntentDraft moved to Agent.Core (namespace check)
///   - ChatInterpreter fast-path intents (cancel / status / help / navigate)
///   - TryParseTruncatedJson returns IntentDraft? (Sprint21 regression)
/// </summary>

// ── Stable goal IDs ───────────────────────────────────────────────────────────

[TestFixture]
[Description("Sprint 39: stable goal IDs — every concrete IGoal instance has a unique non-empty Guid.")]
public sealed class Sprint39GoalIdTests
{
    private static readonly ItemSpec OakLogSpec = new()
    {
        ItemId       = "oak_log",
        DisplayName  = "Oak Log",
        SourceBlocks = ["oak_log"],
    };

    [Test]
    public void GenericGatherGoal_Id_IsNotEmpty()
    {
        var g = new GenericGatherGoal(OakLogSpec, 10);
        Assert.That(g.Id, Is.Not.EqualTo(Guid.Empty),
            "GenericGatherGoal must return a real Guid from Id.");
    }

    [Test]
    public void GenericGatherGoal_TwoInstances_HaveDifferentIds()
    {
        var a = new GenericGatherGoal(OakLogSpec, 10);
        var b = new GenericGatherGoal(OakLogSpec, 10);
        Assert.That(a.Id, Is.Not.EqualTo(b.Id),
            "Two distinct GenericGatherGoal instances must have different Ids.");
    }

    [Test]
    public void CraftItemGoal_Id_IsNotEmpty()
    {
        var g = new CraftItemGoal("iron_pickaxe", 1);
        Assert.That(g.Id, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void GatherWoodGoal_Id_IsNotEmpty()
    {
        var g = new GatherWoodGoal(10);
        Assert.That(g.Id, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void SurviveNightGoal_Id_IsNotEmpty()
    {
        var g = new SurviveNightGoal();
        Assert.That(g.Id, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void SimpleGoal_Id_IsNotEmpty()
    {
        var g = new SimpleGoal("Test", "desc", [], _ => false);
        Assert.That(g.Id, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void AllConcreteGoals_HaveUniqueIds()
    {
        // Sampling 4 goal instances — all must be unique
        var ids = new[]
        {
            new GenericGatherGoal(OakLogSpec, 10).Id,
            new CraftItemGoal("iron_pickaxe").Id,
            new GatherWoodGoal().Id,
            new SurviveNightGoal().Id,
        };
        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length),
            "All sampled goal IDs must be unique.");
    }
}

// ── ILlmEvaluator WorldState parameter ───────────────────────────────────────

[TestFixture]
[Description("Sprint 39 D-S38-02: ILlmEvaluator.EvaluateAsync has a WorldState parameter.")]
public sealed class Sprint39LlmEvaluatorSignatureTests
{
    [Test]
    public void ILlmEvaluator_EvaluateAsync_HasWorldStateParameter()
    {
        var method = typeof(ILlmEvaluator).GetMethod("EvaluateAsync");
        Assert.That(method, Is.Not.Null, "ILlmEvaluator.EvaluateAsync must exist.");

        var paramNames = method!.GetParameters().Select(p => p.Name).ToArray();
        Assert.That(paramNames, Does.Contain("worldState"),
            "EvaluateAsync must have a 'worldState' parameter (D-S38-02).");
    }
}

// ── IntentDraft namespace (moved to Agent.Core) ───────────────────────────────

[TestFixture]
[Description("Sprint 39 P1-C: IntentDraft lives in Agent.Core, not Agent.Planning.")]
public sealed class Sprint39IntentDraftNamespaceTests
{
    [Test]
    public void IntentDraft_IsInAgentCoreNamespace()
    {
        Assert.That(typeof(IntentDraft).Namespace, Is.EqualTo("Agent.Core"),
            "IntentDraft must be in Agent.Core namespace (moved from Agent.Planning in Sprint 39 P1-C).");
    }

    [Test]
    public void IntentDraft_CanBeConstructed()
    {
        var draft = new IntentDraft("yes", "gather", "oak_log", null, 10,
            null, null, null, 0.95, null, "I'll get some wood!");
        Assert.That(draft.Intent,     Is.EqualTo("gather"));
        Assert.That(draft.Item,       Is.EqualTo("oak_log"));
        Assert.That(draft.Count,      Is.EqualTo(10));
        Assert.That(draft.Confidence, Is.EqualTo(0.95).Within(0.001));
    }
}

// ── IChatInterpreter returns IntentDraft? ────────────────────────────────────

[TestFixture]
[Description("Sprint 39 P1-C: IChatInterpreter.InterpretAsync return type is Task<IntentDraft?>.")]
public sealed class Sprint39IChatInterpreterContractTests
{
    [Test]
    public void InterpretAsync_ReturnType_IsNullableIntentDraft()
    {
        var method = typeof(IChatInterpreter).GetMethod("InterpretAsync");
        Assert.That(method, Is.Not.Null, "IChatInterpreter.InterpretAsync must exist.");

        // Task<IntentDraft?> has a generic argument of IntentDraft (nullable is a C# annotation, not runtime type)
        var returnType = method!.ReturnType;
        Assert.That(returnType.IsGenericType, Is.True, "Return type must be generic (Task<>).");
        var innerType = returnType.GetGenericArguments()[0];
        Assert.That(innerType, Is.EqualTo(typeof(IntentDraft)),
            "Return type must be Task<IntentDraft?> (inner type is IntentDraft).");
    }
}

// ── ChatInterpreter fast-path intents ────────────────────────────────────────

[TestFixture]
[Description("Sprint 39 P1-C: ChatInterpreter fast-path returns IntentDraft? with correct Intent strings.")]
public sealed class Sprint39ChatInterpreterFastPathTests
{
    private static readonly ChatOptions Opts = new()
    {
        ConversationWindowSeconds = 60,
        MaxMessageLength = 1024,
    };
    private static readonly Position BotPos    = new(0, 64, 0);
    private static readonly Position PlayerPos  = new(0, 64, 5);
    private static readonly WorldState Empty    = new();
    private const string BotName = "AgentBot";

    private Task<IntentDraft?> Interpret(string msg, int players = 1) =>
        new ChatInterpreter(Opts).InterpretAsync(
            "Player1", msg, BotName, players, BotPos, PlayerPos, Empty);

    [Test]
    public async Task NotAddressed_ReturnsNull()
    {
        var result = await Interpret("hello everyone", players: 3);
        // Far player not mentioning bot → null
        var result2 = await new ChatInterpreter(Opts).InterpretAsync(
            "P1", "let's do something", BotName, 3,
            BotPos, new Position(200, 64, 200), Empty);
        Assert.That(result2, Is.Null, "Not-addressed message should return null IntentDraft.");
    }

    [Test]
    public async Task CancelIntent_ReturnsCancel()
    {
        var result = await Interpret("stop");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Intent, Is.EqualTo("cancel"));
        Assert.That(result.Response, Is.Not.Empty);
        Assert.That(result.Addressed, Is.EqualTo("yes"));
    }

    [Test]
    public async Task StatusIntent_ReturnsStatus()
    {
        var result = await Interpret("what are you doing?");
        Assert.That(result!.Intent, Is.EqualTo("status"));
        Assert.That(result.Response, Is.Not.Empty);
    }

    [Test]
    public async Task HelpIntent_ReturnsHelp()
    {
        var result = await Interpret("help");
        Assert.That(result!.Intent, Is.EqualTo("help"));
        Assert.That(result.Response, Does.Contain("gather").Or.Contain("get").IgnoreCase);
    }

    [Test]
    public async Task NavigateGoTo_ReturnsNavigateWithCoords()
    {
        var result = await Interpret("go to 100 64 200");
        Assert.That(result!.Intent, Is.EqualTo("navigate"));
        Assert.That(result.X, Is.EqualTo(100));
        Assert.That(result.Y, Is.EqualTo(64));
        Assert.That(result.Z, Is.EqualTo(200));
    }

    [Test]
    public async Task NavigateComeHere_ReturnsNavigateWithNullCoords()
    {
        var result = await Interpret("come here");
        Assert.That(result!.Intent, Is.EqualTo("navigate"));
        // null X/Y/Z = follow player — handled by ABS HandleChatEventAsync
        Assert.That(result.X, Is.Null);
        Assert.That(result.Y, Is.Null);
        Assert.That(result.Z, Is.Null);
    }

    [Test]
    public async Task GatherCommand_ReturnsClarify_RoutedToLlm()
    {
        // Sprint 35 P1-D: gather not in ChatInterpreter. Returns "clarify" so
        // LlmChatInterpreter always routes gather to the LLM.
        var result = await Interpret("get me some wood");
        Assert.That(result!.Intent, Is.EqualTo("clarify"),
            "Gather removed from ChatInterpreter — should return 'clarify'.");
    }

    [Test]
    public async Task Confidence_FastPathIntents_IsOne()
    {
        var result = await Interpret("stop");
        Assert.That(result!.Confidence, Is.EqualTo(1.0),
            "Deterministic fast-path intents have confidence 1.0.");
    }
}

// ── TryParseTruncatedJson returns IntentDraft? (Sprint21 regression) ──────────

[TestFixture]
[Description("Sprint 39 P1-C regression: TryParseTruncatedJson still extracts intent fields correctly.")]
public sealed class Sprint39TruncatedJsonTests
{
    private static IntentDraft? Parse(string json)
    {
        var method = typeof(LlmChatInterpreter)
            .GetMethod("TryParseTruncatedJson",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method is null) { Assert.Ignore("TryParseTruncatedJson not found."); return null; }
        // Sprint 46 P0 (TSK-0101): Added optional ILogger parameter — pass null for test.
        return (IntentDraft?)method.Invoke(null, new object?[] { json, null });
    }

    [Test]
    public void TruncatedGather_ExtractsIntentAndItem()
    {
        var json = @"{ ""addressed"": ""yes"", ""intent"": ""gather"", ""item"": ""diamond"", ""count"": 3, ""response"": ""Mining""}";
        var d = Parse(json);
        Assert.That(d, Is.Not.Null);
        Assert.That(d!.Intent, Is.EqualTo("gather"));
        Assert.That(d.Item,    Is.EqualTo("diamond"));
        Assert.That(d.Count,   Is.EqualTo(3));
    }

    [Test]
    public void TruncatedBuild_ExtractsBlueprintField()
    {
        var json = @"{ ""addressed"": ""yes"", ""intent"": ""build"", ""blueprint"": ""small-house"", ""response"": ""OK""}";
        var d = Parse(json);
        Assert.That(d, Is.Not.Null);
        Assert.That(d!.Intent,     Is.EqualTo("build"));
        Assert.That(d.Blueprint,   Is.EqualTo("small-house"));
    }

    [Test]
    public void TruncatedNotAddressed_ReturnsNull()
    {
        var json = @"{ ""addressed"": ""no"", ""intent"": ""gather""";
        var d = Parse(json);
        Assert.That(d, Is.Null, "Addressed='no' in truncated JSON should return null.");
    }
}

[TestFixture]
[Description("Sprint 39 P1: LlmEvaluatorImpl concrete behaviour")]
public sealed class Sprint39LlmEvaluatorImplTests
{
    // ── Test double ──────────────────────────────────────────────────────────

    private sealed class StubLlmProvider(string? response, bool available = true)
        : ILlmProvider
    {
        public string  ProviderName => "stub";
        public bool    IsAvailable  => available;
        public int     CallCount    { get; private set; }

        public Task<string?> CompleteAsync(string system, string user, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(response);
        }
    }

    private static IGoal MakeGoal(string name = "GatherItem:oak_log")
        => new SimpleGoal(name, name, [], _ => false);

    private static ActionOutcome Ok(string tool = "MineBlock")
        => ActionOutcome.Succeeded(Guid.NewGuid(), tool, "mined 1x oak_log");

    private static ActionOutcome Fail(string tool = "MineBlock")
        => ActionOutcome.Failed(Guid.NewGuid(), tool, "block not found");

    private static WorldState AnyState()
        => new WorldState();

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task LlmEvaluator_SkipsEval_WhenTooFewOutcomes()
    {
        var provider  = new StubLlmProvider(@"{""replan"":true}");
        var evaluator = new LlmEvaluatorImpl(provider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LlmEvaluatorImpl>.Instance);

        // Only 2 outcomes — below MinOutcomesBeforeEval=3
        var outcomes = new[] { Fail(), Fail() };
        var result = await evaluator.EvaluateAsync(MakeGoal(), outcomes, AnyState());

        Assert.That(result, Is.False, "Should skip LLM when fewer than 3 outcomes.");
        Assert.That(provider.CallCount, Is.Zero, "Should not call LLM at all.");
    }

    [Test]
    public async Task LlmEvaluator_SkipsEval_WhenAllSucceeded()
    {
        var provider  = new StubLlmProvider(@"{""replan"":true}");
        var evaluator = new LlmEvaluatorImpl(provider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LlmEvaluatorImpl>.Instance);

        var outcomes = new[] { Ok(), Ok(), Ok() }; // 3 successes, 0 failures
        var result = await evaluator.EvaluateAsync(MakeGoal(), outcomes, AnyState());

        Assert.That(result, Is.False, "Should skip LLM when all outcomes succeeded.");
        Assert.That(provider.CallCount, Is.Zero, "Should not call LLM when no failures.");
    }

    [Test]
    public async Task LlmEvaluator_SkipsEval_WhenProviderUnavailable()
    {
        var provider  = new StubLlmProvider(@"{""replan"":true}", available: false);
        var evaluator = new LlmEvaluatorImpl(provider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LlmEvaluatorImpl>.Instance);

        var outcomes = new[] { Ok(), Fail(), Fail() };
        var result = await evaluator.EvaluateAsync(MakeGoal(), outcomes, AnyState());

        Assert.That(result, Is.False, "Should return false when provider is unavailable.");
        Assert.That(provider.CallCount, Is.Zero, "Should not call unavailable provider.");
    }

    [Test]
    public async Task LlmEvaluator_ReturnsTrue_OnReplanJson()
    {
        var provider  = new StubLlmProvider(@"{""replan"":true}");
        var evaluator = new LlmEvaluatorImpl(provider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LlmEvaluatorImpl>.Instance);

        var outcomes = new[] { Ok(), Fail(), Fail() };
        var result = await evaluator.EvaluateAsync(MakeGoal(), outcomes, AnyState());

        Assert.That(result, Is.True,  "Should return true when LLM says replan.");
        Assert.That(provider.CallCount, Is.EqualTo(1), "Should call LLM exactly once.");
    }

    [Test]
    public async Task LlmEvaluator_ReturnsFalse_OnContinueJson()
    {
        var provider  = new StubLlmProvider(@"{""replan"":false,""reason"":""trying harder""}");
        var evaluator = new LlmEvaluatorImpl(provider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LlmEvaluatorImpl>.Instance);

        var outcomes = new[] { Ok(), Fail(), Ok() };
        var result = await evaluator.EvaluateAsync(MakeGoal(), outcomes, AnyState());

        Assert.That(result, Is.False, "Should return false when LLM says continue.");
    }

    [Test]
    public async Task LlmEvaluator_ReturnsFalse_WhenLlmReturnsNull()
    {
        var provider  = new StubLlmProvider(response: null); // provider returns null
        var evaluator = new LlmEvaluatorImpl(provider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LlmEvaluatorImpl>.Instance);

        var outcomes = new[] { Fail(), Fail(), Fail() };
        var result = await evaluator.EvaluateAsync(MakeGoal(), outcomes, AnyState());

        Assert.That(result, Is.False, "Null LLM response should default to no-replan.");
    }

    [Test]
    public async Task LlmEvaluator_ReturnsFalse_OnUnparseableResponse()
    {
        var provider  = new StubLlmProvider("I think you should replan! This is not JSON.");
        var evaluator = new LlmEvaluatorImpl(provider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LlmEvaluatorImpl>.Instance);

        var outcomes = new[] { Fail(), Fail(), Fail() };
        var result = await evaluator.EvaluateAsync(MakeGoal(), outcomes, AnyState());

        Assert.That(result, Is.False, "Unparseable response should default to no-replan.");
    }

    [Test]
    public async Task LlmEvaluator_ExtractsJson_FromProseWrappedResponse()
    {
        // LLM sometimes wraps JSON in prose — the extractor should strip it.
        var provider  = new StubLlmProvider(@"Sure! Here is my answer: {""replan"":true} Done.");
        var evaluator = new LlmEvaluatorImpl(provider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LlmEvaluatorImpl>.Instance);

        var outcomes = new[] { Fail(), Fail(), Fail() };
        var result = await evaluator.EvaluateAsync(MakeGoal(), outcomes, AnyState());

        Assert.That(result, Is.True, "Should extract JSON embedded in prose.");
    }

    [Test]
    public async Task LlmEvaluator_PassesWorldState_ToProvider()
    {
        string? capturedUser = null;
        var provider = new CapturingLlmProvider(user => { capturedUser = user; return @"{""replan"":false}"; });
        var evaluator = new LlmEvaluatorImpl(provider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LlmEvaluatorImpl>.Instance);

        var state = new WorldState.Builder(new WorldState())
            .SetHealth(15)
            .SetFood(10)
            .Build();
        var outcomes = new[] { Ok(), Fail(), Fail() };
        await evaluator.EvaluateAsync(MakeGoal(), outcomes, state);

        Assert.That(capturedUser, Does.Contain("HP=15"), "World state HP should be in user message.");
        Assert.That(capturedUser, Does.Contain("Food=10"), "World state food should be in user message.");
    }

    private sealed class CapturingLlmProvider(Func<string, string?> respond) : ILlmProvider
    {
        public string ProviderName => "capturing";
        public bool   IsAvailable  => true;
        public Task<string?> CompleteAsync(string system, string user, CancellationToken ct = default)
            => Task.FromResult(respond(user));
    }
}

// ── Typed GoalRequest ─────────────────────────────────────────────────────────

[TestFixture]
[Description("Sprint 39 P3: typed GoalRequest hierarchy via IntentManager")]
public sealed class Sprint39TypedGoalRequestTests
{
    private readonly IntentManager _mgr = new();

    [Test]
    public void GatherGoalRequest_HasCorrectGoalName()
    {
        var req = new GatherGoalRequest("oak_log", 32);
        Assert.That(req.GoalName, Is.EqualTo("GatherItem:oak_log"));
    }

    [Test]
    public void GatherGoalRequest_Parameters_ContainsCount()
    {
        var req = new GatherGoalRequest("diamond", 5);
        Assert.That(req.Parameters, Is.Not.Null);
        Assert.That(req.Parameters!["count"], Is.EqualTo(5));
    }

    [Test]
    public void CraftGoalRequest_HasCorrectGoalName()
    {
        var req = new CraftGoalRequest("iron_pickaxe", 1);
        Assert.That(req.GoalName, Is.EqualTo("CraftItem:iron_pickaxe"));
    }

    [Test]
    public void BuildGoalRequest_HasCorrectGoalName()
    {
        var req = new BuildGoalRequest("small-house");
        Assert.That(req.GoalName, Is.EqualTo("Build:small-house"));
    }

    [Test]
    public void BuildGoalRequest_WithOrigin_ExposesCoordinates()
    {
        var origin = new BuildOrigin(10, 64, 20, BuildOriginSource.Explicit);
        var req = new BuildGoalRequest("cabin", origin);
        Assert.That(req.Origin, Is.Not.Null);
        Assert.That(req.Origin!.X, Is.EqualTo(10));
        Assert.That(req.Origin.Y, Is.EqualTo(64));
        Assert.That(req.Origin.Z, Is.EqualTo(20));
        Assert.That(req.Parameters!["originX"], Is.EqualTo(10));
        Assert.That(req.Parameters!["originY"], Is.EqualTo(64));
        Assert.That(req.Parameters!["originZ"], Is.EqualTo(20));
    }

    [Test]
    public void BuildGoalRequest_WithoutOrigin_ParametersIsNull()
    {
        var req = new BuildGoalRequest("cabin"); // no origin
        Assert.That(req.Origin, Is.Null);
        Assert.That(req.Parameters, Is.Null, "Null origin should produce null Parameters.");
    }

    [Test]
    public void NavigateGoalRequest_HasCorrectGoalName()
    {
        var req = new NavigateGoalRequest(100, 64, 200);
        Assert.That(req.GoalName, Is.EqualTo("MoveTo"));
        Assert.That(req.Parameters!["x"], Is.EqualTo(100));
        Assert.That(req.Parameters!["y"], Is.EqualTo(64));
        Assert.That(req.Parameters!["z"], Is.EqualTo(200));
    }

    [Test]
    public void IntentManager_BuildGoalRequest_ReturnsGatherGoalRequest()
    {
        var draft = new IntentDraft("yes", "gather", "oak_log", null, 20, null, null, null, 0.9, null, "");
        var req   = _mgr.BuildGoalRequest(draft);

        Assert.That(req, Is.InstanceOf<GatherGoalRequest>());
        var g = (GatherGoalRequest)req!;
        Assert.That(g.Item,  Is.EqualTo("oak_log"));
        Assert.That(g.Count, Is.EqualTo(20));
    }

    [Test]
    public void IntentManager_BuildGoalRequest_ReturnsCraftGoalRequest()
    {
        var draft = new IntentDraft("yes", "craft", "iron_pickaxe", null, 1, null, null, null, 0.9, null, "");
        var req   = _mgr.BuildGoalRequest(draft);

        Assert.That(req, Is.InstanceOf<CraftGoalRequest>());
        var c = (CraftGoalRequest)req!;
        Assert.That(c.Item, Is.EqualTo("iron_pickaxe"));
    }

    [Test]
    public void IntentManager_BuildGoalRequest_ReturnsBuildGoalRequest_WithOrigin()
    {
        var draft = new IntentDraft("yes", "build", null, "small-house", null, 50, 64, 100, 0.9, null, "");
        var req   = _mgr.BuildGoalRequest(draft);

        Assert.That(req, Is.InstanceOf<BuildGoalRequest>());
        var b = (BuildGoalRequest)req!;
        Assert.That(b.Blueprint, Is.EqualTo("small-house"));
        Assert.That(b.Origin, Is.Not.Null);
        Assert.That(b.Origin!.X, Is.EqualTo(50));
        Assert.That(b.Origin.Y, Is.EqualTo(64));
        Assert.That(b.Origin.Z, Is.EqualTo(100));
    }

    [Test]
    public void IntentManager_BuildGoalRequest_ReturnsNavigateGoalRequest()
    {
        var draft = new IntentDraft("yes", "navigate", null, null, null, 10, 64, 20, 0.9, null, "");
        var req   = _mgr.BuildGoalRequest(draft);

        Assert.That(req, Is.InstanceOf<NavigateGoalRequest>());
        var n = (NavigateGoalRequest)req!;
        Assert.That(n.X, Is.EqualTo(10));
        Assert.That(n.Y, Is.EqualTo(64));
        Assert.That(n.Z, Is.EqualTo(20));
    }

    [Test]
    public void IntentManager_BuildGoalRequest_ReturnsNull_ForUnknownIntent()
    {
        var draft = new IntentDraft("yes", "cancel", null, null, null, null, null, null, 0.9, null, "");
        Assert.That(_mgr.BuildGoalRequest(draft), Is.Null);
    }
}

// ── Schema validation extensions ──────────────────────────────────────────────

[TestFixture]
[Description("Sprint 39 P3: deeper ToolDispatcher schema validation (min/max/enum/length)")]
public sealed class Sprint39SchemaValidationExtensionTests
{
    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement;
    private static JsonElement Args(string json)   => JsonDocument.Parse(json).RootElement;

    private static string? InvokeValidateAgainstSchema(JsonElement args, JsonElement schema)
    {
        var method = typeof(ToolDispatcher).GetMethod("ValidateAgainstSchema",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        return (string?)method!.Invoke(null, new object[] { args, schema });
    }

    [Test]
    public void ValidateSchema_RejectsValue_BelowMinimum()
    {
        var schema = Schema(@"{""type"":""object"",""properties"":{""count"":{""type"":""integer"",""minimum"":1}}}");
        var error  = InvokeValidateAgainstSchema(Args(@"{""count"":0}"), schema);
        Assert.That(error, Is.Not.Null);
        Assert.That(error, Does.Contain(">= 1"));
    }

    [Test]
    public void ValidateSchema_AcceptsValue_AtMinimum()
    {
        var schema = Schema(@"{""type"":""object"",""properties"":{""count"":{""type"":""integer"",""minimum"":1}}}");
        var error  = InvokeValidateAgainstSchema(Args(@"{""count"":1}"), schema);
        Assert.That(error, Is.Null, "Value at minimum boundary should pass.");
    }

    [Test]
    public void ValidateSchema_RejectsValue_AboveMaximum()
    {
        var schema = Schema(@"{""type"":""object"",""properties"":{""radius"":{""type"":""integer"",""maximum"":100}}}");
        var error  = InvokeValidateAgainstSchema(Args(@"{""radius"":101}"), schema);
        Assert.That(error, Is.Not.Null);
        Assert.That(error, Does.Contain("<= 100"));
    }

    [Test]
    public void ValidateSchema_AcceptsValue_AtMaximum()
    {
        var schema = Schema(@"{""type"":""object"",""properties"":{""radius"":{""type"":""integer"",""maximum"":100}}}");
        var error  = InvokeValidateAgainstSchema(Args(@"{""radius"":100}"), schema);
        Assert.That(error, Is.Null, "Value at maximum boundary should pass.");
    }

    [Test]
    public void ValidateSchema_RejectsValue_NotInEnum()
    {
        var schema = Schema(@"{""type"":""object"",""properties"":{""mode"":{""type"":""string"",""enum"":[""fast"",""slow""]}}}");
        var error  = InvokeValidateAgainstSchema(Args(@"{""mode"":""medium""}"), schema);
        Assert.That(error, Is.Not.Null);
        Assert.That(error, Does.Contain("mode"));
        Assert.That(error, Does.Contain("fast"));
    }

    [Test]
    public void ValidateSchema_AcceptsValue_InEnum()
    {
        var schema = Schema(@"{""type"":""object"",""properties"":{""mode"":{""type"":""string"",""enum"":[""fast"",""slow""]}}}");
        var error  = InvokeValidateAgainstSchema(Args(@"{""mode"":""fast""}"), schema);
        Assert.That(error, Is.Null, "Valid enum value should pass.");
    }

    [Test]
    public void ValidateSchema_RejectsString_BelowMinLength()
    {
        var schema = Schema(@"{""type"":""object"",""properties"":{""name"":{""type"":""string"",""minLength"":3}}}");
        var error  = InvokeValidateAgainstSchema(Args(@"{""name"":""ab""}"), schema);
        Assert.That(error, Is.Not.Null);
        Assert.That(error, Does.Contain("minLength"));
    }

    [Test]
    public void ValidateSchema_RejectsString_AboveMaxLength()
    {
        var schema = Schema(@"{""type"":""object"",""properties"":{""name"":{""type"":""string"",""maxLength"":5}}}");
        var error  = InvokeValidateAgainstSchema(Args(@"{""name"":""toolong""}"), schema);
        Assert.That(error, Is.Not.Null);
        Assert.That(error, Does.Contain("maxLength"));
    }

    [Test]
    public void ValidateSchema_AcceptsString_WithinLengthBounds()
    {
        var schema = Schema(@"{""type"":""object"",""properties"":{""name"":{""type"":""string"",""minLength"":2,""maxLength"":10}}}");
        var error  = InvokeValidateAgainstSchema(Args(@"{""name"":""hello""}"), schema);
        Assert.That(error, Is.Null, "String within length bounds should pass.");
    }

    [Test]
    public void ValidateSchema_EnumWorks_ForIntegers()
    {
        var schema = Schema(@"{""type"":""object"",""properties"":{""level"":{""type"":""integer"",""enum"":[1,2,3]}}}");

        var goodError = InvokeValidateAgainstSchema(Args(@"{""level"":2}"), schema);
        Assert.That(goodError, Is.Null, "Valid integer enum value should pass.");

        var badError = InvokeValidateAgainstSchema(Args(@"{""level"":4}"), schema);
        Assert.That(badError, Is.Not.Null, "Out-of-enum integer should fail.");
    }
}

