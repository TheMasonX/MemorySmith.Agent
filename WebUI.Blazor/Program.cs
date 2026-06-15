// MemorySmith.Agent — Web UI Host
// Exposes REST endpoints for agent control and will host the Blazor dashboard (Phase 1).
// SignalR will push real-time events (BotStatusUpdated, InventoryChanged, NewBlueprintPage)
// to connected browsers.

var builder = WebApplication.CreateBuilder(args);

// TODO Phase 1: builder.Services.AddSignalR();
// TODO Phase 2: register IMemoryGateway, IToolRegistry, IPlanner, IWorldAdapter

var app = builder.Build();

app.MapGet("/", () => "MemorySmith.Agent is running.");

// Agent control endpoints
app.MapPost("/api/agent/connect",  () => Results.Ok(new { Status = "connected" }));
app.MapPost("/api/agent/stop",     () => Results.Ok(new { Status = "stopped" }));
app.MapGet ("/api/agent/status",   () => Results.Ok(new { Status = "idle", Goal = (string?)null }));

app.MapPost("/api/agent/command", (CommandRequest req) =>
    Results.Ok(new { Received = req.Command, Status = "queued" }));

// Blueprint catalog
app.MapGet("/api/blueprints", () => Results.Ok(Array.Empty<object>()));

app.Run();

record CommandRequest(string Command);
