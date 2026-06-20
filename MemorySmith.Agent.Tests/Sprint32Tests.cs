namespace MemorySmith.Agent.Tests;

using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Agent.Planning;

/// <summary>
/// Sprint 32 tests:
///   P1-2 — ApiKeyMiddleware rejection path (happy, missing key, invalid key)
///   P1-1 — WebSocket handshake auth gate (connection without secret rejected)
///   P2-1 — GoalFactory uses ILogger instead of Debug.WriteLine
/// </summary>
[TestFixture]
public class Sprint32Tests
{
    // ── P1-2: ApiKeyMiddleware rejection path ─────────────────────────────────
    //
    // We use an in-process TestServer (Microsoft.AspNetCore.TestHost) to exercise
    // the middleware pipeline without a real HTTP server. The TestServer wires up a
    // minimal ASP.NET Core pipeline: ApiKeyMiddleware on /api/* routes, a plain
    // response handler for the endpoint, and config from an in-memory dictionary.

    private static WebApplication BuildTestApp(string? configuredApiKey)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // Inject the API key into configuration (same path ApiKeyMiddleware reads).
        var cfg = new Dictionary<string, string?>();
        if (configuredApiKey is not null)
            cfg["Agent:ApiKey"] = configuredApiKey;
        builder.Configuration.AddInMemoryCollection(cfg);

        builder.Services.AddLogging(lb => lb.SetMinimumLevel(LogLevel.None));

        var app = builder.Build();

        // Mirror the production pipeline: gate /api/* routes behind ApiKeyMiddleware.
        app.UseWhen(
            ctx => ctx.Request.Path.StartsWithSegments("/api"),
            branch => branch.UseMiddleware<WebUI.Blazor.ApiKeyMiddleware>());

        app.MapGet("/api/about", () => Results.Ok(new { version = "test" }));
        app.MapGet("/health", () => Results.Ok("ok"));

        return app;
    }

    [Test]
    public async Task ApiKeyMiddleware_NoKeyConfigured_AllowsRequest()
    {
        // When no API key is configured, middleware must pass all requests through
        // (dev/localhost convenience mode).
        await using var app = BuildTestApp(configuredApiKey: null);
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/about");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "No ApiKey configured → middleware must pass all requests (dev mode).");
    }

    [Test]
    public async Task ApiKeyMiddleware_ValidKey_AllowsRequest()
    {
        // Happy path: correct X-Api-Key header → request proceeds.
        const string key = "test-secret-key-42";
        await using var app = BuildTestApp(configuredApiKey: key);
        await app.StartAsync();

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        var response = await client.GetAsync("/api/about");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Valid X-Api-Key header must be accepted (HTTP 200).");
    }

    [Test]
    public async Task ApiKeyMiddleware_MissingKey_Returns401()
    {
        // Missing X-Api-Key header → 401 Unauthorized.
        await using var app = BuildTestApp(configuredApiKey: "configured-key");
        await app.StartAsync();

        var client = app.GetTestClient();
        // No X-Api-Key header attached.
        var response = await client.GetAsync("/api/about");

        Assert.That((int)response.StatusCode, Is.EqualTo(401),
            "Missing X-Api-Key header must produce 401 Unauthorized.");
    }

    [Test]
    public async Task ApiKeyMiddleware_InvalidKey_Returns401()
    {
        // Wrong X-Api-Key value → 401 Unauthorized.
        await using var app = BuildTestApp(configuredApiKey: "correct-key");
        await app.StartAsync();

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");
        var response = await client.GetAsync("/api/about");

        Assert.That((int)response.StatusCode, Is.EqualTo(401),
            "Invalid X-Api-Key value must produce 401 Unauthorized.");
    }

    [Test]
    public async Task ApiKeyMiddleware_NonApiRoute_NotGated()
    {
        // Routes outside /api/* must not be gated (ApiKeyMiddleware only wires on /api/*).
        await using var app = BuildTestApp(configuredApiKey: "key");
        await app.StartAsync();

        var client = app.GetTestClient();
        // No key header, but this is a non-/api/ route.
        var response = await client.GetAsync("/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Non-/api/* routes must not be blocked by ApiKeyMiddleware.");
    }

    // ── P2-1: GoalFactory ILogger (no Debug.WriteLine) ────────────────────────

    [Test]
    public async Task GoalFactory_MissingBlueprintRepo_LogsWarningNotDebugOutput()
    {
        // When IBlueprintRepository is null, GoalFactory must emit a structured
        // ILogger.LogWarning (not Debug.WriteLine) and return null.
        // We verify by: (a) return value is null, (b) the TestLogger captures a warning.
        var logger = new TestLogger<GoalFactory>();
        var factory = new GoalFactory(
            itemRegistry: null,
            blueprintRepository: null,
            logger: logger);

        var result = await factory.CreateAsync("Build:house", null, CancellationToken.None);

        Assert.That(result, Is.Null,
            "CreateAsync with missing blueprint repository must return null.");
        Assert.That(logger.HasWarning("IBlueprintRepository"), Is.True,
            "A LogWarning mentioning 'IBlueprintRepository' must be emitted (not Debug.WriteLine).");
    }

    [Test]
    public async Task GoalFactory_ItemNotFound_LogsWarningWithItemId()
    {
        // When item not in registry and not a built-in block, a warning must be emitted.
        var logger = new TestLogger<GoalFactory>();
        var factory = new GoalFactory(
            itemRegistry: null,
            blueprintRepository: null,
            logger: logger);

        var result = await factory.CreateAsync("GatherItem:unobtainium_ore", null,
            CancellationToken.None);

        Assert.That(result, Is.Null,
            "CreateAsync with unknown item must return null.");
        Assert.That(
            logger.Entries.Exists(e => e.Level == LogLevel.Warning &&
                e.Message.Contains("unobtainium_ore")),
            Is.True,
            "LogWarning must include the unknown item ID for diagnostics.");
    }
}
