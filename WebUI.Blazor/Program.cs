// MemorySmith.Agent — Web UI & Agent Host
// Phase 1: REST API endpoints, WebSocket bridge to Minecraft.
// Phase 2: RestMemoryGateway (IMemoryGateway via MemorySmith REST).
// Phase 3: HTN/GOAP planner, goal factory, POST /api/agent/plan endpoint.
// Phase 4: IItemRegistry, IBlueprintRepository, GenericGatherGoal, BuildGoal.
// Phase 5: ChatInterpreter, CraftItemTool, FurnaceTool, async goal creation.

using Agent.Construction;
using Agent.Core;
using Agent.Memory;
using Agent.Planning;
using Agent.Tools;
using Agent.World.Minecraft;
using Microsoft.Extensions.Options;
using WebUI.Blazor;

var builder = WebApplication.CreateBuilder(args);

// ── Options ──────────────────────────────────────────────────────────────────
builder.Services.Configure<MinecraftAdapterConfig>(
    builder.Configuration.GetSection("Agent:Minecraft"));
builder.Services.Configure<RestMemoryGatewayOptions>(
    builder.Configuration.GetSection("Agent:Memory"));

// ── Agent services (registered when Agent:Enabled = true) ───────────────────
var agentEnabled = builder.Configuration.GetValue<bool>("Agent:Enabled");

if (agentEnabled)
{
    // IWorldAdapter → MinecraftAdapter
    builder.Services.AddSingleton<IWorldAdapter>(sp =>
    {
        var cfg = sp.GetRequiredService<IOptions<MinecraftAdapterConfig>>().Value;
        return new MinecraftAdapter(cfg);
    });

    // IMemoryGateway → RestMemoryGateway (IHttpClientFactory for pooling)
    builder.Services.AddHttpClient("memorysmith", (sp, http) =>
    {
        var opts = sp.GetRequiredService<IOptions<RestMemoryGatewayOptions>>().Value;
        http.BaseAddress = new Uri(opts.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        if (!string.IsNullOrEmpty(opts.ApiKey))
            http.DefaultRequestHeaders.Add("X-Api-Key", opts.ApiKey);
    });
    builder.Services.AddSingleton<IMemoryGateway>(sp =>
    {
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var opts    = sp.GetRequiredService<IOptions<RestMemoryGatewayOptions>>().Value;
        return new RestMemoryGateway(factory.CreateClient("memorysmith"), opts);
    });

    // IItemRegistry → MemorySmithItemRegistry (Phase 4a)
    builder.Services.AddSingleton<IItemRegistry>(sp =>
        new MemorySmithItemRegistry(sp.GetRequiredService<IMemoryGateway>()));

    // IBlueprintRepository → MemorySmithBlueprintRepository (Phase 4b)
    builder.Services.AddSingleton<IBlueprintRepository>(sp =>
        new MemorySmithBlueprintRepository(sp.GetRequiredService<IMemoryGateway>()));

    // GoalFactory — concrete type, wired with both registries (Phase 4b+5)
    builder.Services.AddSingleton<GoalFactory>(sp => new GoalFactory(
        sp.GetRequiredService<IItemRegistry>(),
        sp.GetRequiredService<IBlueprintRepository>()));
    builder.Services.AddSingleton<IGoalFactory>(sp => sp.GetRequiredService<GoalFactory>());

    // ChatInterpreter — stateful singleton (tracks conversation window)
    builder.Services.AddSingleton<ChatInterpreter>();

    // IToolCaller → ToolDispatcher (all tools registered here)
    builder.Services.AddSingleton<IToolCaller>(sp =>
    {
        var world      = sp.GetRequiredService<IWorldAdapter>();
        var memory     = sp.GetRequiredService<IMemoryGateway>();
        var dispatcher = new ToolDispatcher();

        dispatcher.Register(new MoveToTool(world));
        dispatcher.Register(new StatusTool(world));
        dispatcher.Register(new MineBlockTool(world));
        dispatcher.Register(new WanderTool(world));
        dispatcher.Register(new PlaceBlockTool(world));
        dispatcher.Register(new SearchMemoryTool(memory));
        dispatcher.Register(new GetPageTool(memory));
        dispatcher.Register(new CreatePageTool(memory));
        // Phase 5: chat + crafting tools
        dispatcher.Register(new ChatTool(world));
        dispatcher.Register(new CraftItemTool(world));
        dispatcher.Register(new FurnaceTool(world));

        return dispatcher;
    });

    // IPlanner → HtnPlanner with default task library
    builder.Services.AddSingleton<HtnTaskLibrary>();
    builder.Services.AddSingleton<IPlanner, HtnPlanner>();

    // AgentBackgroundService — inject all optional services explicitly
    builder.Services.AddSingleton<AgentBackgroundService>(sp =>
    {
        var cfg = sp.GetRequiredService<IOptions<MinecraftAdapterConfig>>().Value;
        return new AgentBackgroundService(
            sp.GetRequiredService<IWorldAdapter>(),
            sp.GetRequiredService<IToolCaller>(),
            sp.GetRequiredService<ILogger<AgentBackgroundService>>(),
            sp.GetRequiredService<IPlanner>(),
            goalFactory:      sp.GetService<GoalFactory>(),
            chatInterpreter:  sp.GetService<ChatInterpreter>(),
            botName:          cfg.BotUsername,
            maxConsecutiveFailures: 3);
    });
    builder.Services.AddHostedService(sp =>
        sp.GetRequiredService<AgentBackgroundService>());
}

// ── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseDefaultFiles();   // serves index.html as default
app.UseStaticFiles();

app.MapGet("/", () => "MemorySmith.Agent is running.");

// ── Info ─────────────────────────────────────────────────────────────────────

app.MapGet("/api/about", (IGoalFactory? factory) => Results.Ok(new
{
    Name        = "MemorySmith.Agent",
    Description = "A modular autonomous agent framework backed by the MemorySmith knowledge system.",
    Version     = "0.5.0",
    Phase       = "Phase 5 — Crafting, Chat Interpretation, Interactive Dashboard (complete)",
    License     = "MIT",
    Repository  = "https://github.com/TheMasonX/MemorySmith.Agent",
    ProjectSource = "https://github.com/TheMasonX/MemorySmith",
    Dashboard   = "/",
    AboutPage   = "/about.html",
    WikiHome    = "/Data/Pages/home.md",
    RegisteredGoals = factory?.RegisteredGoals ?? [],
}));

// ── Agent status ──────────────────────────────────────────────────────────────

app.MapGet("/api/agent/status", (AgentBackgroundService? agent) =>
    Results.Ok(new
    {
        Status              = agentEnabled ? (agent?.CurrentGoal != null ? "active" : "idle") : "disabled",
        Goal                = agent?.CurrentGoal?.Name,
        GoalDescription     = agent?.CurrentGoal?.Description,
        GoalPhases          = agent?.CurrentGoal?.Phases,
        Health              = agent?.WorldState.Health ?? 0,
        Food                = agent?.WorldState.Food ?? 0,
        Position            = agent?.WorldState.Position,
        Inventory           = agent?.WorldState.Inventory,
        QueuedActions       = agent?.GetPendingActions().Count ?? 0,
        ConsecutiveFailures = agent?.ConsecutiveFailures ?? 0,
    }));

// ── Goal management ───────────────────────────────────────────────────────────

app.MapGet("/api/goals", (IGoalFactory? factory) =>
    Results.Ok(factory?.RegisteredGoals ?? []));

/// <summary>Create and start pursuing a goal. Supports GatherItem: and Build: async prefixes.</summary>
app.MapPost("/api/agent/plan", async (PlanRequest req, AgentBackgroundService? agent,
    IGoalFactory? factory, IPlanner? planner) =>
{
    if (agent is null || planner is null || factory is null)
        return Results.Problem("Agent services are not enabled. Set Agent:Enabled=true in configuration.");

    // Use CreateAsync so GatherItem: and Build: goals resolve via registries
    var goal = await factory.CreateAsync(req.GoalName, req.Parameters);
    if (goal is null)
        return Results.BadRequest(new
        {
            Error     = $"Unknown goal '{req.GoalName}' or missing registry dependency.",
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

/// <summary>Cancel the current goal immediately.</summary>
app.MapDelete("/api/agent/goal", (AgentBackgroundService? agent) =>
{
    if (agent is null) return Results.BadRequest("Agent not enabled.");
    agent.CancelGoal();
    return Results.Ok(new { Status = "cancelled" });
});

// ── Queue inspection ──────────────────────────────────────────────────────────

app.MapGet("/api/agent/queue", (AgentBackgroundService? agent) =>
{
    var actions = agent?.GetPendingActions() ?? [];
    return Results.Ok(new
    {
        Count   = actions.Count,
        Actions = actions.Select(a => new { a.Tool, Args = a.Arguments }),
    });
});

// ── Build origin ──────────────────────────────────────────────────────────────

app.MapPost("/api/agent/origin", (OriginRequest req, AgentBackgroundService? agent) =>
{
    if (agent is null) return Results.BadRequest("Agent not enabled.");
    agent.SetBuildOrigin(req.BlueprintId, req.X, req.Y, req.Z);
    return Results.Ok(new
    {
        Status      = "origin set",
        BlueprintId = req.BlueprintId,
        X = req.X, Y = req.Y, Z = req.Z,
    });
});

// ── Chat ──────────────────────────────────────────────────────────────────────

app.MapPost("/api/agent/chat", (ChatRequest req, AgentBackgroundService? agent) =>
{
    if (agent is null) return Results.BadRequest("Agent not enabled.");
    agent.Enqueue(new ActionData
    {
        Tool      = "Chat",
        Arguments = { ["message"] = req.Message ?? string.Empty }
    });
    return Results.Ok(new { Status = "queued", Message = req.Message });
});

// ── Blueprints ────────────────────────────────────────────────────────────────

app.MapGet("/api/blueprints", () => Results.Ok(new[]
{
    new { Id = "small-house", Name = "Small Survival House", Tags = new[] { "house", "starter", "survival" } }
}));

// ── Legacy ────────────────────────────────────────────────────────────────────

app.MapPost("/api/agent/connect", () => Results.Ok(new { Status = "connected" }));
app.MapPost("/api/agent/stop",    () => Results.Ok(new { Status = "stopped" }));
app.MapPost("/api/agent/command", (CommandRequest req, AgentBackgroundService? agent) =>
{
    if (agent is null) return Results.BadRequest("Agent not enabled.");
    agent.Enqueue(new ActionData { Tool = req.Command });
    return Results.Ok(new { Received = req.Command, Status = "queued" });
});

app.Run();

// ── Request records ───────────────────────────────────────────────────────────

record CommandRequest(string Command);
record PlanRequest(string GoalName, IReadOnlyDictionary<string, object?>? Parameters = null);
record OriginRequest(string BlueprintId, int X, int Y, int Z);
record ChatRequest(string? Message);
