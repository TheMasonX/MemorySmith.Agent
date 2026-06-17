// MemorySmith.Agent — Web UI & Agent Host
// v0.7.0  Phase 5b — LLM chat, provider abstraction, configurable rate limits

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
    loggerConfig
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft",              LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore",   LogEventLevel.Warning)
        .MinimumLevel.Override("System.Net.Http",        LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Extensions.Http", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        // Clean console: no level prefix, no duplicate EventLog fallback
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: "logs/memorysmith-agent-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            shared: true,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
    // EventLog sink removed: requires Windows admin to create event source;
    // its catch-block was adding a second Console sink that duplicated every log line.
});

// ── Options ────────────────────────────────────────────────────────────────────────────
builder.Services.Configure<MinecraftAdapterConfig>(
    builder.Configuration.GetSection("Agent:Minecraft"));
builder.Services.Configure<RestMemoryGatewayOptions>(
    builder.Configuration.GetSection("Agent:Memory"));

// ── Agent services ──────────────────────────────────────────────────────────────────────────
var agentEnabled = builder.Configuration.GetValue<bool>("Agent:Enabled");

if (agentEnabled)
{
    // ── World adapter ───────────────────────────────────────────────────────────────────────
    builder.Services.AddSingleton<IWorldAdapter>(sp =>
    {
        var cfg = sp.GetRequiredService<IOptions<MinecraftAdapterConfig>>().Value;
        return new MinecraftAdapter(cfg);
    });

    // ── Memory gateway ────────────────────────────────────────────────────────────────────
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

    // ── Registries ────────────────────────────────────────────────────────────────────────────
    // Sprint 2c: MemorySmithItemRegistry now receives RestMemoryGatewayOptions for TTL cache config.
    builder.Services.AddSingleton<IItemRegistry>(sp =>
        new MemorySmithItemRegistry(
            sp.GetRequiredService<IMemoryGateway>(),
            sp.GetRequiredService<IOptions<RestMemoryGatewayOptions>>().Value));
    builder.Services.AddSingleton<IBlueprintRepository>(sp =>
        new MemorySmithBlueprintRepository(sp.GetRequiredService<IMemoryGateway>()));

    // ── Goal factory ────────────────────────────────────────────────────────────────────────
    builder.Services.AddSingleton<GoalFactory>(sp => new GoalFactory(
        sp.GetRequiredService<IItemRegistry>(),
        sp.GetRequiredService<IBlueprintRepository>()));
    builder.Services.AddSingleton<IGoalFactory>(sp => sp.GetRequiredService<GoalFactory>());

    // ── Chat / LLM services ───────────────────────────────────────────────────────────────────
    // Sprint 4b: rolling chat history context window (last 5 turns)
    builder.Services.AddSingleton<ChatHistory>();

    var chatOpts = new ChatOptions();
    var chatSection = builder.Configuration.GetSection("Agent:Chat");
    chatSection.Bind(chatOpts);
    // Backward compatibility: support legacy Agent:Chat:Model alongside LlmModel.
    var legacyModel = chatSection["Model"];
    if (!string.IsNullOrWhiteSpace(legacyModel) && string.IsNullOrWhiteSpace(chatSection["LlmModel"]))
        chatOpts = chatOpts with { LlmModel = legacyModel };
    builder.Services.AddSingleton(chatOpts);

    // LLM HttpClient — named "llm", BaseAddress set to the provider's URL
    builder.Services.AddHttpClient("llm", http =>
    {
        http.BaseAddress = new Uri(chatOpts.ResolvedBaseUrl);
        http.Timeout     = TimeSpan.FromSeconds(chatOpts.LlmTimeoutSeconds + 2); // outer > inner
    });
    builder.Services.AddSingleton<ILlmProvider>(sp =>
    {
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("llm");
        return LlmProviderFactory.Create(http, chatOpts);
    });

    builder.Services.AddSingleton<ChatRateLimiter>();
    builder.Services.AddSingleton<ChatInterpreter>();       // pattern-matching fallback
    builder.Services.AddSingleton<IChatInterpreter>(sp =>  // LLM + fallback
        new LlmChatInterpreter(
            sp.GetRequiredService<ILlmProvider>(),
            sp.GetRequiredService<ChatInterpreter>(),
            sp.GetRequiredService<ChatRateLimiter>(),
            chatOpts,
            sp.GetRequiredService<ChatHistory>(),
            sp.GetRequiredService<ILogger<LlmChatInterpreter>>()));

    // ── Tools ─────────────────────────────────────────────────────────────────────────────────
    builder.Services.AddSingleton<IToolCaller>(sp =>
    {
        var world  = sp.GetRequiredService<IWorldAdapter>();
        var memory = sp.GetRequiredService<IMemoryGateway>();
        var d      = new ToolDispatcher();
        d.Register(new MoveToTool(world));
        d.Register(new StatusTool(world));
        d.Register(new GetStatusTool(world));
        d.Register(new MineBlockTool(world));
        d.Register(new WanderTool(world));
        d.Register(new PlaceBlockTool(world));
        d.Register(new SearchMemoryTool(memory));
        d.Register(new GetPageTool(memory));
        d.Register(new CreatePageTool(memory));
        d.Register(new ChatTool(world));
        d.Register(new CraftItemTool(world));
        d.Register(new FurnaceTool(world));
        d.Register(new FindFlatAreaTool(world));
        return d;
    });

    // ── Planner ────────────────────────────────────────────────────────────────────────────────
    builder.Services.AddSingleton<HtnTaskLibrary>();
    builder.Services.AddSingleton<HtnPlanner>(sp =>
        new HtnPlanner(sp.GetRequiredService<HtnTaskLibrary>()));
    builder.Services.AddSingleton<IPlanner>(sp =>
        sp.GetRequiredService<HtnPlanner>());

    // Sprint 6: World Model (belief + prediction + reconciliation)
    builder.Services.AddSingleton<IWorldModel>(new WorldModel());

    // Sprint 6: Decomposer registry + PlannerRouter (extensible planning)
    builder.Services.AddSingleton<DecomposerRegistry>(sp =>
    {
        var lib = sp.GetRequiredService<HtnTaskLibrary>();
        var reg = new DecomposerRegistry();
        reg.Register(new BuildGoalDecomposer(lib));
        reg.Register(new GatherGoalDecomposer(lib));
        reg.Register(new SurviveNightGoalDecomposer(lib));
        return reg;
    });
    builder.Services.AddSingleton<PlannerRouter>();

    // ── Journal (execution trace) ────────────────────────────────────────────────────────────
    builder.Services.AddSingleton<IAgentJournal>(new AgentJournal());

    // ── SignalR dashboard push — Sprint 4a ────────────────────────────────────────────
    builder.Services.AddSignalR();

    // ── Background service ───────────────────────────────────────────────────────────────────
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
            journal:         sp.GetRequiredService<IAgentJournal>());
    });
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentBackgroundService>());
}

// ── Fallback singletons for when agent is disabled ────────────────────────────────────
// These ensure endpoints that depend on IAgentJournal / IWorldModel resolve cleanly
// even when Agent:Enabled=false, instead of returning 500.
if (!agentEnabled)
{
    builder.Services.AddSingleton<IAgentJournal>(NullAgentJournal.Instance);
    builder.Services.AddSingleton<IWorldModel>(new WorldModel());
}

// ── Build ────────────────────────────────────────────────────────────────────────────
var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseDefaultFiles();
app.UseStaticFiles();

if (agentEnabled)
{
    var opts = app.Services.GetRequiredService<ChatOptions>();
    app.Logger.LogInformation(
        "Chat LLM config: enabled={Enabled}, provider={Provider}, model={Model}, baseUrl={BaseUrl}",
        opts.LlmEnabled, opts.LlmProvider, opts.LlmModel, opts.ResolvedBaseUrl);
}

// Sprint 4a: map SignalR hub for real-time dashboard push
if (agentEnabled)
    app.MapHub<AgentHub>("/agent-hub");

app.MapGet("/", () => "MemorySmith.Agent is running.");

app.MapGet("/api/about", (IGoalFactory? factory) => Results.Ok(new
{
    Name    = "MemorySmith.Agent",
    Version = "0.7.0",
    Phase   = "Phase 5b — LLM chat, multi-provider, configurable rate limits",
    License = "MIT",
    Repository  = "https://github.com/TheMasonX/MemorySmith.Agent",
    Dashboard   = "/",
    RegisteredGoals = factory?.RegisteredGoals ?? [],
}));

app.MapGet("/api/agent/status", (AgentBackgroundService? agent) =>
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
// Sprint 5: locked down — validates against registered tools instead of accepting anything
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

// ── Agent observability endpoints (Sprint 7 / D5) ─────────────────────────────────────

app.MapGet("/api/agent/journal", (
    IAgentJournal? journal,
    int limit = 50,
    string? type = null) =>
{
    if (journal is NullAgentJournal or null)
        return Results.Ok(new { count = 0, entries = Array.Empty<object>() });

    JournalEntryType? typeFilter = null;
    if (type is not null && Enum.TryParse<JournalEntryType>(type, ignoreCase: true, out var t))
        typeFilter = t;

    var entries = journal.Query(typeFilter).Take(limit).ToList();
    return Results.Ok(new { count = journal.Count, returned = entries.Count, entries });
});

app.MapGet("/api/agent/worldmodel", (IWorldModel? model) =>
{
    if (model is null)
        return Results.Ok(new { available = false });

    return Results.Ok(new
    {
        available   = true,
        uncertainty = model.Uncertainty,
        belief      = model.Belief,
        observed    = model.Observed,
    });
});

app.Run();

record CommandRequest(string Command);
record PlanRequest(string GoalName, IReadOnlyDictionary<string, object?>? Parameters = null);
record OriginRequest(string BlueprintId, int X, int Y, int Z);
record ChatRequest(string? Message);

