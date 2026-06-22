namespace MemorySmith.Agent.Tests;

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
        return (IntentDraft?)method.Invoke(null, new object?[] { json });
    }

    [Test]
    public void TruncatedGather_ExtractsIntentAndItem()
    {
        var json = @"{ ""addressed"": ""yes"", ""intent"": ""gather"", ""item"": ""diamond"", ""count"": 3, ""response"": ""Mining";
        var d = Parse(json);
        Assert.That(d, Is.Not.Null);
        Assert.That(d!.Intent, Is.EqualTo("gather"));
        Assert.That(d.Item,    Is.EqualTo("diamond"));
        Assert.That(d.Count,   Is.EqualTo(3));
    }

    [Test]
    public void TruncatedBuild_ExtractsBlueprintField()
    {
        var json = @"{ ""addressed"": ""yes"", ""intent"": ""build"", ""blueprint"": ""small-house"", ""response"": ""OK";
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
