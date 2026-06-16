// MemorySmith.Agent — Web UI & Agent Host
// Phase 1: REST API endpoints, WebSocket bridge to Minecraft.
// Phase 2: RestMemoryGateway (IMemoryGateway via MemorySmith REST).
// Phase 3: HTN/GOAP planner, goal factory, POST /api/agent/plan endpoint.

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
        var opts = sp.GetRequiredService<IOptions<RestMemoryGatewayOptions>>().Value;
        return new RestMemoryGateway(factory.CreateClient("memorysmith"), opts);
    });

    // IToolCaller → ToolEngine with all tools
    builder.Services.AddSingleton<ToolRegistry>();
    builder.Services.AddSingleton<IToolCaller>(sp =>
    {
        var registry = sp.GetRequiredService<ToolRegistry>();
        var world    = sp.GetRequiredService<IWorldAdapter>();
        var memory   = sp.GetRequiredService<IMemoryGateway>();

        registry.Register(new MoveToTool(world));
        registry.Register(new StatusTool(world));
        registry.Register(new MineBlockTool(world));
        registry.Register(new SearchMemoryTool(memory));
        registry.Register(new GetPageTool(memory));
        registry.Register(new CreatePageTool(memory));

        return new ToolEngine(registry);
    });

    // IPlanner → HtnPlanner with default task library
    builder.Services.AddSingleton<HtnTaskLibrary>();
    builder.Services.AddSingleton<IPlanner, HtnPlanner>();

    // IGoalFactory → GoalFactory
    builder.Services.AddSingleton<IGoalFactory, GoalFactory>();

    // Hosted agent loop — registered as concrete type AND as IHostedService
    // so both /api/agent/plan (typed resolution) and DI work.
    builder.Services.AddSingleton<AgentBackgroundService>();
    builder.Services.AddHostedService(sp =>
        sp.GetRequiredService<AgentBackgroundService>());
}

// ── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Static files (wwwroot/about.html → /about)
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/", () => "MemorySmith.Agent is running.");

// About endpoint
app.MapGet("/api/about", () => Results.Ok(new
{
    Name        = "MemorySmith.Agent",
    Description = "A modular autonomous agent framework backed by the MemorySmith knowledge system.",
    Version     = "0.3.0",
    Phase       = "Phase 3 — HTN/GOAP Planner (complete)",
    License     = "MIT",
    Repository  = "https://github.com/TheMasonX/MemorySmith.Agent",
    ProjectSource = "https://github.com/TheMasonX/MemorySmith",
    Dashboard   = "/about",
    WikiHome    = "/Data/Pages/home.md",
    RegisteredGoals = agentEnabled
        ? new[] { "GatherWood", "SurviveNight" }
        : Array.Empty<string>(),
}));

// Agent control
app.MapPost("/api/agent/connect",  () => Results.Ok(new { Status = "connected" }));
app.MapPost("/api/agent/stop",     () => Results.Ok(new { Status = "stopped" }));
app.MapGet ("/api/agent/status", (AgentBackgroundService? agent) =>
    Results.Ok(new
    {
        Status = agentEnabled ? "idle" : "disabled",
        Goal   = agent?.CurrentGoal?.Name,
        Health = agent?.WorldState.Health,
    }));

// Queue a raw tool call
app.MapPost("/api/agent/command", (CommandRequest req, AgentBackgroundService? agent) =>
{
    if (agent is null)
        return Results.BadRequest("Agent is not enabled.");
    agent.Enqueue(new ActionData { Tool = req.Command });
    return Results.Ok(new { Received = req.Command, Status = "queued" });
});

// Create a plan for a named goal and enqueue it
app.MapPost("/api/agent/plan", async (PlanRequest req, AgentBackgroundService? agent,
    IPlanner? planner, IGoalFactory? factory) =>
{
    if (agent is null || planner is null || factory is null)
        return Results.Problem("Agent services are not enabled. Set Agent:Enabled=true.");

    var goal = factory.Create(req.GoalName, req.Parameters);
    if (goal is null)
        return Results.BadRequest(
            new { Error = $"Unknown goal '{req.GoalName}'.",
                  Available = factory.RegisteredGoals });

    var plan = await planner.PlanAsync(goal, agent.WorldState);
    agent.SetGoal(goal); // also clears queue and resets failures
    return Results.Ok(new
    {
        Goal        = goal.Name,
        Description = goal.Description,
        ActionCount = plan.Actions.Count,
        Phases      = plan.Phases,
    });
});

// List available goals
app.MapGet("/api/goals", (IGoalFactory? factory) =>
    Results.Ok(factory?.RegisteredGoals ?? []));

// Blueprint catalog
app.MapGet("/api/blueprints", () => Results.Ok(Array.Empty<object>()));

app.Run();

record CommandRequest(string Command);
record PlanRequest(string GoalName, IReadOnlyDictionary<string, object?>? Parameters = null);
