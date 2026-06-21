// MemorySmith.Agent — Web UI & Agent Host
// v0.28.0  Sprint 33 — Build verify, DI logger wiring, base64 sweep, Rule E-2

using Agent.Construction;
using Agent.Core;
using Agent.Memory;
using Agent.Planning;
using Agent.Planning.Llm;
using Agent.Tools;
using Agent.World.Minecraft;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using WebUI.Blazor;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfig) =>
{
    var loggingConfig = context.Configuration.GetSection("Agent:Logging");
    var defaultLevelName = loggingConfig.GetValue<string?>("Default") ?? "Information";
    if (Enum.TryParse<LogEventLevel>(defaultLevelName, true, out var configuredDefault))
        loggerConfig.MinimumLevel.Is(configuredDefault);
    else
        loggerConfig.MinimumLevel.Information();

    foreach (var overrideEntry in loggingConfig.GetSection("Overrides").GetChildren())
    {
        if (Enum.TryParse<LogEventLevel>(overrideEntry.Value, true, out var overrideLevel))
            loggerConfig.MinimumLevel.Override(overrideEntry.Key, overrideLevel);
    }

    loggerConfig
        .MinimumLevel.Override("Microsoft",              LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore",   LogEventLevel.Warning)
        .MinimumLevel.Override("System.Net.Http",        LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Extensions.Http", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: "logs/memorysmith-agent-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            shared: true,
            restrictedToMinimumLevel: LogEventLevel.Debug,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}");
});


// ── Options ───────────────────────────────────────────────────────────────────────────

// Map MEMORYSMITH_API_KEY env var → Agent:Memory:ApiKey so the RestMemoryGateway
// sends the correct X-Api-Key header to the MemorySmith API server.
var memorysmithApiKey = Environment.GetEnvironmentVariable("MEMORYSMITH_API_KEY");
if (!string.IsNullOrEmpty(memorysmithApiKey))
{
    builder.Configuration["Agent:Memory:ApiKey"] = memorysmithApiKey;
}

builder.Services.Configure<RestMemoryGatewayOptions>(
    builder.Configuration.GetSection("Agent:Memory"));
builder.Services.Configure<MinecraftAdapterConfig>(
    builder.Configuration.GetSection("Agent:Minecraft"));

var agentEnabled = builder.Configuration.GetValue<bool>("Agent:Enabled");

if (agentEnabled)
{
    builder.Services.AddSingleton<IWorldAdapter>(sp =>
        new MinecraftAdapter(sp.GetRequiredService<IOptions<MinecraftAdapterConfig>>().Value));

    builder.Services.AddHttpClient("memorysmith", (sp, http) =>
    {
        var opts = sp.GetRequiredService<IOptions<RestMemoryGatewayOptions>>().Value;
        http.BaseAddress = new Uri(opts.BaseUrl);
        http.Timeout     = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        if (!string.IsNullOrEmpty(opts.ApiKey))
            http.DefaultRequestHeaders.Add("X-Api-Key", opts.ApiKey);
    });
    builder.Services.AddSingleton<IMemoryGateway>(sp =>
    {
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var opts    = sp.GetRequiredService<IOptions<RestMemoryGatewayOptions>>().Value;
        return new RestMemoryGateway(factory.CreateClient("memorysmith"), opts);
    });

    builder.Services.AddHttpClient("memorysmith-world", (sp, http) =>
    {
        var opts     = sp.GetRequiredService<IOptions<RestMemoryGatewayOptions>>().Value;
        var worldUrl = string.IsNullOrWhiteSpace(opts.WorldKbUrl) ? opts.BaseUrl : opts.WorldKbUrl;
        http.BaseAddress = new Uri(worldUrl);
        http.Timeout     = TimeSpan.FromSeconds(opts.WorldTimeoutSeconds);
        var apiKey   = opts.WorldApiKey ?? opts.ApiKey;
        if (!string.IsNullOrEmpty(apiKey))
            http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    });
    builder.Services.AddKeyedSingleton<IMemoryGateway>("world", (sp, _) =>
    {
        var factory  = sp.GetRequiredService<IHttpClientFactory>();
        var opts     = sp.GetRequiredService<IOptions<RestMemoryGatewayOptions>>().Value;
        var worldUrl = string.IsNullOrWhiteSpace(opts.WorldKbUrl) ? opts.BaseUrl : opts.WorldKbUrl;
        var worldOpts = opts with
        {
            BaseUrl        = worldUrl,
            ApiKey         = opts.WorldApiKey ?? opts.ApiKey,
            TimeoutSeconds = opts.WorldTimeoutSeconds,
        };
        return new RestMemoryGateway(factory.CreateClient("memorysmith-world"), worldOpts);
    });

    builder.Services.AddSingleton<IItemRegistry>(sp =>
        new MemorySmithItemRegistry(
            sp.GetRequiredService<IMemoryGateway>(),
            sp.GetRequiredService<IOptions<RestMemoryGatewayOptions>>().Value));
    builder.Services.AddSingleton<IBlueprintRepository>(sp =>
        new MemorySmithBlueprintRepository(sp.GetRequiredService<IMemoryGateway>()));

    builder.Services.AddSingleton<IKnowledgeResolver>(sp =>
        new LocalKnowledgeResolver(
            sp.GetRequiredService<IItemRegistry>(),
            sp.GetRequiredService<IMemoryGateway>(),
            () => sp.GetService<AgentBackgroundService>()?.WorldState));

    builder.Services.AddSingleton<GoalFactory>(sp => new GoalFactory(
        sp.GetRequiredService<IItemRegistry>(),
        sp.GetRequiredService<IBlueprintRepository>(),
        sp.GetRequiredService<ILogger<GoalFactory>>())); // Sprint 33 DEF-S32-A
    builder.Services.AddSingleton<IGoalFactory>(sp => sp.GetRequiredService<GoalFactory>());

    builder.Services.AddSingleton<ChatHistory>();

    var chatOpts = new ChatOptions();
    var chatSection = builder.Configuration.GetSection("Agent:Chat");
    chatSection.Bind(chatOpts);
    var legacyModel = chatSection["Model"];
    if (!string.IsNullOrWhiteSpace(legacyModel) && string.IsNullOrWhiteSpace(chatSection["LlmModel"]))
        chatOpts = chatOpts with { LlmModel = legacyModel };
    builder.Services.AddSingleton(chatOpts);

    builder.Services.AddHttpClient("llm", http =>
    {
        http.BaseAddress = new Uri(chatOpts.ResolvedBaseUrl);
        http.Timeout     = TimeSpan.FromSeconds(chatOpts.LlmTimeoutSeconds + 2);
    });
    builder.Services.AddSingleton<ILlmProvider>(sp =>
    {
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("llm");
        var ollamaLogger = sp.GetRequiredService<ILogger<OllamaProvider>>();
        return LlmProviderFactory.Create(http, chatOpts, ollamaLogger);
    });

    builder.Services.AddSingleton<ChatRateLimiter>();
    builder.Services.AddSingleton<ChatInterpreter>();
    builder.Services.AddSingleton<IChatInterpreter>(sp =>
        new LlmChatInterpreter(
            sp.GetRequiredService<ILlmProvider>(),
            sp.GetRequiredService<ChatInterpreter>(),
            sp.GetRequiredService<ChatRateLimiter>(),
            chatOpts,
            sp.GetRequiredService<ChatHistory>(),
            sp.GetRequiredService<ILogger<LlmChatInterpreter>>()));

    // ── Tools ──────────────────────────────────────────────────────────────────────────────────
    builder.Services.AddSingleton<IToolCaller>(sp =>
    {
        var world  = sp.GetRequiredService<IWorldAdapter>();
        var memory = sp.GetRequiredService<IMemoryGateway>();
        // Sprint 23 P0-B: SearchMemory + CreatePage route to world KB; GetPage uses agent KB.
        var worldMemory = sp.GetKeyedService<IMemoryGateway>("world") ?? memory;
        var journal = sp.GetRequiredService<IAgentJournal>();
        var d = new ToolDispatcher(journal);
        d.Register(new MoveToTool(world));
        // Sprint 25 P0-B: StatusTool deleted (duplicate of GetStatusTool).
        // GetStatusTool is registered under its canonical name "GetStatus" and aliased as "Status"
        // for backward compatibility with plans and runtime paths that use the old name.
        d.Register(new GetStatusTool(world));
        d.Register("Status", new GetStatusTool(world));
        d.Register(new MineBlockTool(world));
        d.Register(new WanderTool(world));
        d.Register(new PlaceBlockTool(world));
        d.Register(new SearchMemoryTool(worldMemory)); // world KB
        d.Register(new GetPageTool(memory));           // agent KB
        d.Register(new CreatePageTool(worldMemory));   // world KB
        d.Register(new ChatTool(world));
        d.Register(new CraftItemTool(world));
        d.Register(new FurnaceTool(world));
        d.Register(new FindFlatAreaTool(world));
        return d;
    });

    builder.Services.AddSingleton<HtnTaskLibrary>();
    builder.Services.AddSingleton<HtnPlanner>(sp =>
        new HtnPlanner(
            sp.GetRequiredService<HtnTaskLibrary>(),
            sp.GetRequiredService<ILogger<HtnPlanner>>())); // Sprint 33 DEF-S32-G

    builder.Services.AddSingleton<IWorldModel>(new WorldModel());

    builder.Services.AddSingleton<DecomposerRegistry>(sp =>
    {
        var lib = sp.GetRequiredService<HtnTaskLibrary>();
        var lf  = sp.GetRequiredService<ILoggerFactory>(); // Sprint 32 BLK-01: BuildGoalDecomposer requires ILogger
        var reg = new DecomposerRegistry();
        reg.Register(new BuildGoalDecomposer(lib, lf.CreateLogger<BuildGoalDecomposer>()));
        reg.Register(new GatherGoalDecomposer(lib));
        reg.Register(new SurviveNightGoalDecomposer(lib));
        reg.Register(new CraftItemGoalDecomposer(lib)); // Sprint 27 P0-D
        return reg;
    });
    builder.Services.AddSingleton<PlannerRouter>(sp =>
        new PlannerRouter(
            sp.GetRequiredService<DecomposerRegistry>(),
            sp.GetRequiredService<HtnPlanner>()));
    builder.Services.AddSingleton<IPlanner>(sp =>
        sp.GetRequiredService<PlannerRouter>());  // Sprint 27 P0-D: route through decomposer registry first

    builder.Services.AddSingleton<IAgentJournal>(new AgentJournal());

    builder.Services.AddSingleton<IReplanGovernor, ReplanGovernor>();

    // Sprint 27 P0-C: injectable time provider for deterministic testing.
    builder.Services.AddSingleton<ITimeProvider>(SystemTimeProvider.Instance);

    builder.Services.AddSignalR();

    builder.Services.AddSingleton<AgentBackgroundService>(sp =>
    {
        var cfg = sp.GetRequiredService<IOptions<MinecraftAdapterConfig>>().Value;
        return new AgentBackgroundService(
            sp.GetRequiredService<IWorldAdapter>(),
            sp.GetRequiredService<IToolCaller>(),
            sp.GetRequiredService<ILogger<AgentBackgroundService>>(),
            sp.GetRequiredService<IPlanner>(),
            hubContext:      sp.GetRequiredService<IHubContext<AgentHub>>(),
            goalFactory:     sp.GetRequiredService<GoalFactory>(),
            chatInterpreter: sp.GetRequiredService<IChatInterpreter>(),
            botName:         cfg.BotUsername,
            journal:         sp.GetRequiredService<IAgentJournal>(),
            replanGovernor:  sp.GetRequiredService<IReplanGovernor>(),
            timeProvider:    sp.GetRequiredService<ITimeProvider>());  // Sprint 27 P0-C
    });
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentBackgroundService>());
}

if (!agentEnabled)
{
    builder.Services.AddSingleton<IAgentJournal>(NullAgentJournal.Instance);
    builder.Services.AddSingleton<IWorldModel>(new WorldModel());
}

var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseDefaultFiles();
app.UseStaticFiles();

// Sprint 30 P1-C (SEC-01): gate all /api/* routes behind ApiKeyMiddleware.
// Configure Agent:ApiKey in appsettings.json or Agent__ApiKey env var.
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseMiddleware<ApiKeyMiddleware>()
);

if (agentEnabled)
{
    var opts = app.Services.GetRequiredService<ChatOptions>();
    app.Logger.LogInformation(
        "Chat LLM config: enabled={Enabled}, provider={Provider}, model={Model}, baseUrl={BaseUrl}",
        opts.LlmEnabled, opts.LlmProvider, opts.LlmModel, opts.ResolvedBaseUrl);

    var mcCfg  = app.Services.GetRequiredService<IOptions<MinecraftAdapterConfig>>().Value;
    var memCfg = app.Services.GetRequiredService<IOptions<RestMemoryGatewayOptions>>().Value;
    app.Logger.LogInformation(
        "=== Agent config: bot={Bot} mc={Host}:{McPort} | " +
        "llmTimeout={LlmTimeout}s rateCooldown={Cooldown}s maxPerMin={Max} | " +
        "memory={MemUrl} actionTimeout=30s replanInterval=2s ===",
        mcCfg.BotUsername, mcCfg.ServerHost, mcCfg.ServerPort,
        opts.LlmTimeoutSeconds, opts.PlayerCooldownSeconds, opts.GlobalPerMinuteMax,
        memCfg.BaseUrl);

    // Sprint 23 B-1: warn when WorldKbUrl is not configured.
    if (string.IsNullOrWhiteSpace(memCfg.WorldKbUrl))
    {
        app.Logger.LogWarning(
            "World KB URL is not configured (WorldKbUrl is null). World observations will be stored " +
            "in agent KB. Set WorldKbUrl in Agent:Memory:WorldKbUrl to enable world KB separation. " +
            "See Data/Pages/Guides/world-kb-deployment.md");
    }

    // Sprint 33 DEF-S32-H: log when AdapterSecret is null so misconfiguration is visible.
    // When null, no handshake is sent; if WS_TOKEN is set externally the connection will fail.
    if (string.IsNullOrEmpty(mcCfg.AdapterSecret))
    {
        app.Logger.LogDebug(
            "MinecraftAdapter: AdapterSecret is not configured (null/empty). No WebSocket handshake " +
            "will be sent on connect. If WS_TOKEN is set in the Node.js environment the connection " +
            "will be rejected. Set Agent:Minecraft:AdapterSecret to enable auth.");
    }
}

if (agentEnabled)
    app.MapHub<AgentHub>("/agent-hub");

app.MapGet("/", () => "MemorySmith.Agent is running.");

app.MapGet("/api/about", (IGoalFactory? factory) => Results.Ok(new
{
    Name    = "MemorySmith.Agent",
    Version = "0.28.0",
    Phase   = "Sprint 33 — Build verify, DI logger wiring, base64 sweep, Rule E-2",
    License = "MIT",
    Repository  = "https://github.com/TheMasonX/MemorySmith.Agent",
    Dashboard   = "/",
    RegisteredGoals = factory?.RegisteredGoals ?? [],
}));

app.MapGet("/api/agent/status", (AgentBackgroundService? agent, IWorldModel? worldModel) =>
    Results.Ok(new
    {
        Status              = agentEnabled ? (agent?.CurrentGoal != null ? "active" : "idle") : "disabled",
        Goal                = agent?.CurrentGoal?.Name,
        GoalDescription     = agent?.CurrentGoal?.Description,
        GoalPhases          = agent?.CurrentGoal?.Phases,
        Health              = agent?.WorldState.Health  ?? 0,
        Food                = agent?.WorldState.Food    ?? 0,
        Position            = agent?.WorldState.Position,
        Inventory           = agent?.WorldState.Inventory,
        QueuedActions       = agent?.GetPendingActions().Count ?? 0,
        ConsecutiveFailures = agent?.ConsecutiveFailures ?? 0,
        Uncertainty         = worldModel?.Uncertainty ?? 0.0,
    }));

app.MapGet("/api/goals", (IGoalFactory? factory) =>
    Results.Ok(factory?.RegisteredGoals ?? []));

app.MapPost("/api/agent/plan", async (PlanRequest req, AgentBackgroundService? agent,
    IGoalFactory? factory, IPlanner? planner) =>
{
    if (agent is null || planner is null || factory is null)
        return Results.Problem("Agent not enabled. Set Agent:Enabled=true.");

    var goal = await factory.CreateAsync(req.GoalName, req.Parameters);
    if (goal is null)
        return Results.BadRequest(new
        {
            Error     = $"Unknown goal '{req.GoalName}'.",
            Available = factory.RegisteredGoals,
        });

    var plan = await planner.PlanAsync(goal, agent.WorldState);
    agent.SetGoal(goal);
    return Results.Ok(new
    {
        Goal        = goal.Name,
        Description = goal.Description,
        Phases      = plan.Phases,
        ActionCount = plan.Actions.Count,
        Actions     = plan.Actions.Take(5).Select(a => new { a.Tool, a.Arguments }),
    });
});

app.MapDelete("/api/agent/goal", (AgentBackgroundService? agent) =>
{
    if (agent is null) return Results.BadRequest("Agent not enabled.");
    agent.CancelGoal();
    return Results.Ok(new { Status = "cancelled" });
});

app.MapGet("/api/agent/queue", (AgentBackgroundService? agent) =>
{
    var actions = agent?.GetPendingActions() ?? [];
    return Results.Ok(new { Count = actions.Count, Actions = actions.Select(a => new { a.Tool, Args = a.Arguments }) });
});

app.MapPost("/api/agent/origin", (OriginRequest req, AgentBackgroundService? agent) =>
{
    if (agent is null) return Results.BadRequest("Agent not enabled.");
    agent.SetBuildOrigin(req.BlueprintId, req.X, req.Y, req.Z);
    return Results.Ok(new { Status = "origin set", req.BlueprintId, req.X, req.Y, req.Z });
});

app.MapPost("/api/agent/chat", (ChatRequest req, AgentBackgroundService? agent) =>
{
    if (agent is null) return Results.BadRequest("Agent not enabled.");
    agent.Enqueue(new ActionData { Tool = "Chat", Arguments = { ["message"] = req.Message ?? string.Empty } });
    return Results.Ok(new { Status = "queued" });
});

app.MapGet("/api/blueprints", () => Results.Ok(new[]
    { new { Id = "small-house", Name = "Small Survival House", Tags = new[] { "house", "starter" } } }));

app.MapPost("/api/agent/connect", () => Results.Ok(new { Status = "connected" }));
app.MapPost("/api/agent/stop",    () => Results.Ok(new { Status = "stopped" }));
app.MapPost("/api/agent/command", (CommandRequest req, AgentBackgroundService? agent,
    IToolCaller? tools) =>
{
    if (agent is null) return Results.BadRequest("Agent not enabled.");
    if (tools is not ToolDispatcher dispatcher)
        return Results.Problem("Tool dispatcher not available.");
    if (dispatcher.Get(req.Command) is null)
        return Results.BadRequest(new { Error = $"Unknown tool '{req.Command}'.", Registered = dispatcher.All.Select(t => t.Name) });
    agent.Enqueue(new ActionData { Tool = req.Command });
    return Results.Ok(new { Received = req.Command, Status = "queued" });
});

app.MapGet("/api/agent/journal", (
    IAgentJournal? journal,
    int limit = 50,
    string? type = null) =>
{
    if (journal is NullAgentJournal or null)
        return Results.Ok(new { count = 0, returned = 0, entries = Array.Empty<JournalEntryDto>() });

    JournalEntryType? typeFilter = null;
    if (type is not null && Enum.TryParse<JournalEntryType>(type, ignoreCase: true, out var t))
        typeFilter = t;

    var entries = journal.Query(typeFilter).Take(limit).ToList();
    var dtos = entries.Select(e => new JournalEntryDto(
        e.Timestamp.ToString("O"),
        e.Type.ToString(),
        e.Summary,
        (e.Details ?? new Dictionary<string, object?>())
            .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString())
    )).ToList();

    return Results.Ok(new { count = journal.Count, returned = dtos.Count, entries = dtos });
});

app.MapGet("/api/agent/worldmodel", (IWorldModel? model, bool detail = true) =>
{
    if (model is null)
        return Results.Ok(new { available = false });

    if (!detail)
    {
        return Results.Ok(new
        {
            available      = true,
            uncertainty    = model.Uncertainty,
            position       = model.Belief.Position,
            health         = model.Belief.Health,
            food           = model.Belief.Food,
            inventoryCount = model.Belief.Inventory.Count,
        });
    }

    return Results.Ok(new
    {
        available   = true,
        uncertainty = model.Uncertainty,
        belief      = model.Belief,
        observed    = model.Observed,
    });
});

app.MapGet("/api/agent/resolve", async (
    IKnowledgeResolver? resolver,
    string? q,
    string? types = null,
    float confidenceThreshold = 0.0f,
    int topN = 5) =>
{
    if (resolver is null)
        return Results.Problem("Knowledge resolver not available. Set Agent:Enabled=true.");

    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { Error = "q parameter is required." });

    CandidateType[]? typeFilter = null;
    if (!string.IsNullOrWhiteSpace(types))
    {
        typeFilter = types
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => Enum.TryParse<CandidateType>(t, ignoreCase: true, out _))
            .Select(t => Enum.Parse<CandidateType>(t, ignoreCase: true))
            .ToArray();
    }

    var query  = new KnowledgeQuery(q, typeFilter, confidenceThreshold, Math.Max(1, topN));
    var result = await resolver.ResolveAsync(query);

    return Results.Ok(new
    {
        query          = q,
        candidateCount = result.Candidates.Count,
        wasAmbiguous   = result.WasAmbiguous,
        best           = result.Best is { } b
            ? new { b.Id, b.DisplayName, type = b.Type.ToString(), b.Confidence, b.Detail }
            : (object?)null,
        candidates = result.Candidates.Select(c => new
        {
            c.Id,
            c.DisplayName,
            type       = c.Type.ToString(),
            c.Confidence,
            c.Detail,
        }),
    });
});

app.Run();

record CommandRequest(string Command);
record PlanRequest(string GoalName, IReadOnlyDictionary<string, object?>? Parameters = null);
record OriginRequest(string BlueprintId, int X, int Y, int Z);
record ChatRequest(string? Message);
