// MemorySmith.Agent — Web UI & Agent Host
// Phase 1: REST API endpoints, WebSocket bridge to Minecraft.
// Phase 2: RestMemoryGateway (IMemoryGateway via MemorySmith REST).
// Phase 3: HTN/GOAP planner, goal factory, POST /api/agent/plan endpoint.
// Phase 4: IItemRegistry, IBlueprintRepository, GenericGatherGoal, BuildGoal.
// Phase 5: ChatInterpreter, CraftItemTool, FurnaceTool, async goal creation.
// Phase 5b: LLM-powered chat (OllamaLlmClient), LlmChatInterpreter, rate limiting,
//           distance-based routing, player position in chat events.

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

// ── Agent services ────────────────────────────────────────────────────────────
var agentEnabled = builder.Configuration.GetValue<bool>("Agent:Enabled");

if (agentEnabled)
{
    // IWorldAdapter → MinecraftAdapter
    builder.Services.AddSingleton<IWorldAdapter>(sp =>
    {
        var cfg = sp.GetRequiredService<IOptions<MinecraftAdapterConfig>>().Value;
        return new MinecraftAdapter(cfg);
    });

    // IMemoryGateway → RestMemoryGateway
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

    // IItemRegistry + IBlueprintRepository
    builder.Services.AddSingleton<IItemRegistry>(sp =>
        new MemorySmithItemRegistry(sp.GetRequiredService<IMemoryGateway>()));
    builder.Services.AddSingleton<IBlueprintRepository>(sp =>
        new MemorySmithBlueprintRepository(sp.GetRequiredService<IMemoryGateway>()));

    // GoalFactory (concrete + interface)
    builder.Services.AddSingleton<GoalFactory>(sp => new GoalFactory(
        sp.GetRequiredService<IItemRegistry>(),
        sp.GetRequiredService<IBlueprintRepository>()));
    builder.Services.AddSingleton<IGoalFactory>(sp => sp.GetRequiredService<GoalFactory>());

    // ── LLM chat services (Phase 5b) ──────────────────────────────────────────
    var llmOpts = new LlmOptions();
    builder.Configuration.GetSection("Agent:Llm").Bind(llmOpts);

    // Ollama HTTP client — registered even if disabled (client just returns null)
    builder.Services.AddHttpClient("ollama", http =>
    {
        http.BaseAddress = new Uri(llmOpts.OllamaUrl);
        http.Timeout     = TimeSpan.FromSeconds(8); // outer timeout; inner is 5s per call
    });
    builder.Services.AddSingleton<IChatLlmClient>(sp =>
    {
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama");
        return new OllamaLlmClient(http, llmOpts);
    });
    builder.Services.AddSingleton<ChatRateLimiter>();
    builder.Services.AddSingleton<ChatInterpreter>();      // pattern-matching fallback
    builder.Services.AddSingleton<IChatInterpreter>(sp =>  // LLM + fallback
        new LlmChatInterpreter(
            sp.GetRequiredService<IChatLlmClient>(),
            sp.GetRequiredService<ChatInterpreter>(),
            sp.GetRequiredService<ChatRateLimiter>()));

    // ── Tool dispatcher ────────────────────────────────────────────────────────
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
        dispatcher.Register(new ChatTool(world));
        dispatcher.Register(new CraftItemTool(world));
        dispatcher.Register(new FurnaceTool(world));

        return dispatcher;
    });

    // IPlanner + HtnTaskLibrary
    builder.Services.AddSingleton<HtnTaskLibrary>();
    builder.Services.AddSingleton<IPlanner, HtnPlanner>();

    // AgentBackgroundService
    builder.Services.AddSingleton<AgentBackgroundService>(sp =>
    {
        var cfg = sp.GetRequiredService<IOptions<MinecraftAdapterConfig>>().Value;
        return new AgentBackgroundService(
            sp.GetRequiredService<IWorldAdapter>(),
            sp.GetRequiredService<IToolCaller>(),
            sp.GetRequiredService<ILogger<AgentBackgroundService>>(),
            sp.GetRequiredService<IPlanner>(),
            goalFactory:     sp.GetService<GoalFactory>(),
            chatInterpreter: sp.GetService<IChatInterpreter>(),
            botName:         cfg.BotUsername,
            maxConsecutiveFailures: 3);
    });
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentBackgroundService>());
}

// ── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/", () => "MemorySmith.Agent is running.");

// ── About ─────────────────────────────────────────────────────────────────────
app.MapGet("/api/about", (IGoalFactory? factory) => Results.Ok(new
{
    Name        = "MemorySmith.Agent",
    Description = "A modular autonomous agent framework backed by the MemorySmith knowledge system.",
    Version     = "0.6.0",
    Phase       = "Phase 5b — LLM Chat Interpretation, Rate Limiting, Distance Routing (complete)",
    License     = "MIT",
    Repository  = "https://github.com/TheMasonX/MemorySmith.Agent",
    Dashboard   = "/",
    AboutPage   = "/about.html",
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

app.MapPost("/api/agent/plan", async (PlanRequest req, AgentBackgroundService? agent,
    IGoalFactory? factory, IPlanner? planner) =>
{
    if (agent is null || planner is null || factory is null)
        return Results.Problem("Agent services not enabled. Set Agent:Enabled=true.");

    var goal = await factory.CreateAsync(req.GoalName, req.Parameters);
    if (goal is null)
        return Results.BadRequest(new
        {
            Error = $"Unknown goal '{req.GoalName}' or missing registry dependency.",
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

// ── Queue, origin, chat ───────────────────────────────────────────────────────
app.MapGet("/api/agent/queue", (AgentBackgroundService? agent) =>
{
    var actions = agent?.GetPendingActions() ?? [];
    return Results.Ok(new
    {
        Count   = actions.Count,
        Actions = actions.Select(a => new { a.Tool, Args = a.Arguments }),
    });
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
    new { Id = "small-house", Name = "Small Survival House", Tags = new[] { "house", "starter" } }
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

record CommandRequest(string Command);
record PlanRequest(string GoalName, IReadOnlyDictionary<string, object?>? Parameters = null);
record OriginRequest(string BlueprintId, int X, int Y, int Z);
record ChatRequest(string? Message);