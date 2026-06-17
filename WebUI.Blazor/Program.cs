// MemorySmith.Agent — Web UI & Agent Host
// v0.7.0  Phase 5b — LLM chat, provider abstraction, configurable rate limits

using Agent.Construction;
using Agent.Core;
using Agent.Memory;
using Agent.Planning;
using Agent.Planning.Llm;
using Agent.Tools;
using Agent.World.Minecraft;
using Microsoft.Extensions.Options;
using WebUI.Blazor;

var builder = WebApplication.CreateBuilder(args);

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
    builder.Configuration.GetSection("Agent:Chat").Bind(chatOpts);
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
            sp.GetRequiredService<ChatHistory>()));      // Sprint 4b

    // ── Tools ─────────────────────────────────────────────────────────────────────────────────
    builder.Services.AddSingleton<IToolCaller>(sp =>
    {
        var world  = sp.GetRequiredService<IWorldAdapter>();
        var memory = sp.GetRequiredService<IMemoryGateway>();
        var d      = new ToolDispatcher();
        d.Register(new MoveToTool(world));
        d.Register(new StatusTool(world));
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
    builder.Services.AddSingleton<IPlanner, HtnPlanner>();

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

// ── Build ────────────────────────────────────────────────────────────────────────────
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

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

app.Run();

record CommandRequest(string Command);
record PlanRequest(string GoalName, IReadOnlyDictionary<string, object?>? Parameters = null);
record OriginRequest(string BlueprintId, int X, int Y, int Z);
record ChatRequest(string? Message);
