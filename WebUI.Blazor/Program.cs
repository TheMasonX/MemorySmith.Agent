// MemorySmith.Agent — Web UI & Agent Host
// Exposes REST endpoints for agent control and hosts the agent background service.
// SignalR (Phase 1): will push BotStatusUpdated, InventoryChanged, NewBlueprintPage.
// LLM integration (Phase 2): Microsoft.Extensions.AI with Ollama/OpenAI.

using Agent.Core;
using Agent.Memory;
using Agent.Tools;
using Agent.World.Minecraft;
using Microsoft.Extensions.Options;
using WebUI.Blazor;

var builder = WebApplication.CreateBuilder(args);

// ── Agent options ────────────────────────────────────────────────────────────
builder.Services.Configure<MinecraftAdapterConfig>(
    builder.Configuration.GetSection("Agent:Minecraft"));
builder.Services.Configure<RestMemoryGatewayOptions>(
    builder.Configuration.GetSection("Agent:Memory"));

// ── Agent services (registered when Agent:Enabled = true) ───────────────────
var agentEnabled = builder.Configuration.GetValue<bool>("Agent:Enabled");

if (agentEnabled)
{
    // IWorldAdapter → MinecraftAdapter (auto-starts Node.js when AutoStartNode = true)
    builder.Services.AddSingleton<IWorldAdapter>(sp =>
    {
        var cfg = sp.GetRequiredService<IOptions<MinecraftAdapterConfig>>().Value;
        return new MinecraftAdapter(cfg);
    });

    // IMemoryGateway → RestMemoryGateway via IHttpClientFactory (pooled, resilient)
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

    // IToolCaller → ToolEngine with all registered tools
    builder.Services.AddSingleton<ToolRegistry>();
    builder.Services.AddSingleton<IToolCaller>(sp =>
    {
        var registry = sp.GetRequiredService<ToolRegistry>();
        var world   = sp.GetRequiredService<IWorldAdapter>();
        var memory  = sp.GetRequiredService<IMemoryGateway>();

        registry.Register(new MoveToTool(world));
        registry.Register(new StatusTool(world));
        registry.Register(new SearchMemoryTool(memory));
        registry.Register(new GetPageTool(memory));
        registry.Register(new CreatePageTool(memory));

        return new ToolEngine(registry);
    });

    // Hosted agent loop
    builder.Services.AddHostedService<AgentBackgroundService>();
}

// ── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.MapGet("/", () => "MemorySmith.Agent is running.");

// Agent control endpoints
app.MapPost("/api/agent/connect",  () => Results.Ok(new { Status = "connected" }));
app.MapPost("/api/agent/stop",     () => Results.Ok(new { Status = "stopped" }));
app.MapGet ("/api/agent/status",   () => Results.Ok(new { Status = agentEnabled ? "idle" : "disabled", Goal = (string?)null }));

app.MapPost("/api/agent/command", (CommandRequest req) =>
{
    // TODO Phase 1: route to AgentBackgroundService.Enqueue when agent is enabled
    return Results.Ok(new { Received = req.Command, Status = agentEnabled ? "queued" : "agent disabled" });
});

// Blueprint catalog
app.MapGet("/api/blueprints", () => Results.Ok(Array.Empty<object>()));

app.Run();

record CommandRequest(string Command);
