namespace WebUI.Blazor;

using Agent.Core;
using Agent.Tools;
using System.Text.Json;

/// <summary>
/// Hosted service that owns the agent loop.
/// On start: connects the world adapter, then runs two concurrent tasks:
///   1. ProcessEventsAsync — reads WorldEvents from the adapter, updates state.
///   2. DispatchActionsAsync — drains the ActionQueue and dispatches via ToolEngine.
///
/// Phase 1: basic loop, no planner, no memory. Actions are enqueued externally
///          via the /api/agent/command endpoint.
/// Phase 2: wire IMemoryGateway for context injection.
/// Phase 3: wire IPlanner to generate actions from goals.
/// </summary>
public sealed class AgentBackgroundService(
    IWorldAdapter worldAdapter,
    IToolCaller toolCaller,
    ILogger<AgentBackgroundService> logger) : BackgroundService
{
    private readonly ActionQueue _queue = new();
    private WorldState _worldState = new();

    public WorldState WorldState => _worldState;

    /// <summary>Externally enqueue an action (called from REST endpoint).</summary>
    public void Enqueue(ActionData action) => _queue.Enqueue(action);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AgentBackgroundService starting...");

        try
        {
            await worldAdapter.ConnectAsync(stoppingToken);
            logger.LogInformation("World adapter connected.");

            await Task.WhenAll(
                ProcessEventsAsync(stoppingToken),
                DispatchActionsAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("AgentBackgroundService stopping.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AgentBackgroundService encountered a fatal error.");
        }
        finally
        {
            try { await worldAdapter.DisconnectAsync(stoppingToken); }
            catch { /* best-effort disconnect */ }
        }
    }

    private async Task ProcessEventsAsync(CancellationToken ct)
    {
        await foreach (var worldEvent in worldAdapter.ReceiveEventsAsync(ct))
        {
            logger.LogDebug("World event: {Type}", worldEvent.EventType);

            // Phase 1: update basic world state facts from events
            _worldState = _worldState.With(b =>
            {
                foreach (var kv in worldEvent.Payload)
                    b.SetFact($"event:{worldEvent.EventType}:{kv.Key}", kv.Value);
            });

            // TODO Phase 3: trigger planner if goal is incomplete
        }
    }

    private async Task DispatchActionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var action = _queue.Dequeue();
            if (action is not null)
            {
                try
                {
                    var argsJson = JsonSerializer.Serialize(action.Arguments);
                    using var doc = JsonDocument.Parse(argsJson);
                    var result = await toolCaller.CallAsync(action.Tool, doc.RootElement, ct);

                    if (result.Success)
                        logger.LogInformation("Tool {Tool}: {Message}", action.Tool, result.Message);
                    else
                        logger.LogWarning("Tool {Tool} failed: {Message}", action.Tool, result.Message);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Exception dispatching tool {Tool}", action.Tool);
                }
            }
            else
            {
                await Task.Delay(50, ct); // idle poll
            }
        }
    }
}
