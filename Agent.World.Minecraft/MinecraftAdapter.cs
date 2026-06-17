namespace Agent.World.Minecraft;

using Agent.Core;
using System.Diagnostics;
using System.Net.Sockets;

/// <summary>
/// IWorldAdapter implementation for Minecraft via Node.js/Mineflayer.
///
/// Connection flow:
///   1. If AutoStartNode is true, spawn MineflayerAdapter/index.js as a subprocess.
///   2. Poll until the WebSocket server port is open (Node.js startup).
///   3. Connect WebSocketBridge to the WS server.
///   4. On bot.once('spawn'), Node sends {"event":"spawn",...} — agent is live.
///
/// C# → Node command:  {"action":"move","x":10,"y":64,"z":20}
/// Node → C# event:    {"event":"blockMined","block":"stone","count":3}
/// </summary>
public sealed class MinecraftAdapter(MinecraftAdapterConfig config) : IWorldAdapter, IDisposable
{
    private WebSocketBridge? _bridge;
    private Process? _nodeProcess;

    public bool IsConnected => _bridge?.IsOpen ?? false;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (config.AutoStartNode && !string.IsNullOrWhiteSpace(config.NodeScriptPath))
            await StartNodeProcessAsync(cancellationToken);

        _bridge = new WebSocketBridge(config.WebSocketUrl);
        await _bridge.ConnectAsync(cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_bridge is not null)
            await _bridge.CloseAsync(cancellationToken);

        if (_nodeProcess is not { HasExited: false })
            return;

        // 1. Send SIGTERM for graceful shutdown.
        //    .NET's Process.Kill() sends SIGKILL on Linux, so we use kill -TERM
        //    to give the Node process a chance to flush state and exit cleanly.
        try
        {
            var pid = _nodeProcess.Id;
            using var killProc = new Process
            {
                StartInfo = new ProcessStartInfo("kill", $"-TERM {pid}")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            killProc.Start();
            killProc.WaitForExit(1000);
        }
        catch
        {
            // Swallow — process may have already exited or kill may not be available.
        }

        // 2. Wait up to 5 seconds for the process to exit gracefully.
        try
        {
            _nodeProcess.WaitForExit(5000);
        }
        catch (SystemException)
        {
            // Guard against WaitForExit throwing on an invalid handle.
        }

        // 3. If still alive after the grace period, force-kill the entire tree.
        if (!_nodeProcess.HasExited)
        {
            try
            {
                _nodeProcess.Kill(entireProcessTree: true);
                await _nodeProcess.WaitForExitAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Process already exited between the check and the kill.
            }
        }
    }

    public Task SendActionAsync(ActionData action, CancellationToken cancellationToken = default)
    {
        if (_bridge is null || !_bridge.IsOpen)
            throw new InvalidOperationException("Minecraft adapter is not connected.");

        return _bridge.SendAsync(action, cancellationToken);
    }

    public IAsyncEnumerable<WorldEvent> ReceiveEventsAsync(CancellationToken cancellationToken = default)
    {
        if (_bridge is null)
            throw new InvalidOperationException("Minecraft adapter is not connected.");

        return _bridge.ReceiveAsync(cancellationToken);
    }

    // ── Private helpers ──────────────────────────────

    private async Task StartNodeProcessAsync(CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("node", config.NodeScriptPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.EnvironmentVariables["WS_PORT"]      = config.WebSocketPort.ToString();
        psi.EnvironmentVariables["MC_HOST"]       = config.ServerHost;
        psi.EnvironmentVariables["MC_PORT"]       = config.ServerPort.ToString();
        psi.EnvironmentVariables["MC_USERNAME"]   = config.BotUsername;

        _nodeProcess = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start Node process: {config.NodeScriptPath}");

        await WaitForPortAsync(config.WebSocketPort, config.NodeStartTimeoutMs, cancellationToken);
    }

    private static async Task WaitForPortAsync(int port, int timeoutMs, CancellationToken ct)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeoutMs / 1000.0 * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", port, ct);
                return; // port is open
            }
            catch
            {
                await Task.Delay(200, ct);
            }
        }
        throw new TimeoutException($"Node.js WS server did not open port {port} within {timeoutMs}ms.");
    }

    public void Dispose()
    {
        _bridge?.Dispose();
        _nodeProcess?.Dispose();
    }
}
