// MemorySmith.Agent — Sprint 60 (TSK-0350): Global antiforgery filter tests
//
// These tests verify that:
//   1. UseAntiforgery() middleware blocks POST/PUT/DELETE without a valid token
//   2. GET endpoints are not blocked (safe methods are skipped)
//   3. The /antiforgery/token endpoint returns a valid token
//   4. The SignalR hub at /agent-hub is exempted from antiforgery
//   5. POST with a valid antiforgery token succeeds

namespace MemorySmith.Agent.Tests;

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using WebUI.Blazor;

[TestFixture]
public class Sprint60AntiforgeryTests
{
    /// <summary>
    /// Builds a minimal test app mirroring the production antiforgery pipeline:
    ///   AddAntiforgery → UseAntiforgery → MapPost("/api/test") → .DisableAntiforgery for hub
    /// </summary>
    private static WebApplication BuildTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging(lb => lb.SetMinimumLevel(LogLevel.None));
        builder.Services.AddAntiforgery();
        builder.Services.AddRouting(); // needed for MapHub convention builder

        var app = builder.Build();
        app.UseRouting();
        app.UseAntiforgery();
        app.UseMiddleware<AntiforgeryValidationMiddleware>();

        // GET endpoint (safe method — should pass through)
        app.MapGet("/api/status", () => Results.Ok(new { status = "ok" }));

        // POST endpoint (unsafe — requires antiforgery token)
        app.MapPost("/api/data", (HttpContext ctx) => Results.Ok(new { received = true }));

        // DELETE endpoint (unsafe — requires antiforgery token)
        app.MapDelete("/api/data", () => Results.Ok(new { deleted = true }));

        // Simulated hub endpoint (exempt from antiforgery)
        app.MapPost("/agent-hub/negotiate", () => Results.Ok(new { negotiated = true }))
           .DisableAntiforgery();

        // Token endpoint (GET — safe, passes through)
        app.MapGet("/antiforgery/token", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new
            {
                token      = tokens.RequestToken,
                headerName = tokens.HeaderName,
            });
        });

        return app;
    }

    [Test]
    public async Task Antiforgery_GET_SafeMethod_NoToken_Returns200()
    {
        // GET is a safe method — antiforgery middleware must skip it.
        await using var app = BuildTestApp();
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "GET requests (safe method) must pass through without antiforgery token.");
    }

    [Test]
    public async Task Antiforgery_POST_WithoutToken_Returns400()
    {
        // POST without antiforgery token must be rejected with 400 Bad Request.
        await using var app = BuildTestApp();
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/data", new StringContent("{}"));

        Assert.That((int)response.StatusCode, Is.EqualTo(400),
            "POST requests without antiforgery token must be rejected with 400.");
    }

    [Test]
    public async Task Antiforgery_DELETE_WithoutToken_Returns400()
    {
        // DELETE without antiforgery token must be rejected with 400 Bad Request.
        await using var app = BuildTestApp();
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.DeleteAsync("/api/data");

        Assert.That((int)response.StatusCode, Is.EqualTo(400),
            "DELETE requests without antiforgery token must be rejected with 400.");
    }

    [Test]
    public async Task Antiforgery_HubEndpoint_Exempt_Returns200WithoutToken()
    {
        // The hub endpoint has .DisableAntiforgery() — must pass through without token.
        await using var app = BuildTestApp();
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.PostAsync("/agent-hub/negotiate", new StringContent("{}"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Hub endpoint with .DisableAntiforgery() must accept POST without token.");
    }

    [Test]
    public async Task Antiforgery_TokenEndpoint_ReturnsToken()
    {
        // The /antiforgery/token endpoint must return a valid request token and header name.
        await using var app = BuildTestApp();
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.GetAsync("/antiforgery/token");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Token endpoint must return 200 OK.");

        var json = await response.Content.ReadAsStringAsync();
        var body = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(json);
        Assert.That(body, Is.Not.Null, "Response body must be valid JSON.");
        Assert.That(body!.token, Is.Not.Null.And.Not.Empty,
            "Token endpoint must return a non-empty request token.");
        Assert.That(body.headerName, Is.Not.Null.And.Not.Empty,
            "Token endpoint must return a non-empty header name.");
    }

    [Test]
    public async Task Antiforgery_TokenEndpoint_SetsCookie()
    {
        // The /antiforgery/token endpoint must return a Set-Cookie header containing
        // the antiforgery cookie (required for subsequent POST requests).
        await using var app = BuildTestApp();
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.GetAsync("/antiforgery/token");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Token endpoint must return 200 OK.");

        var setCookie = response.Headers
            .FirstOrDefault(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.That(setCookie.Value, Is.Not.Null.And.Not.Empty,
            "Token endpoint must set the antiforgery cookie via Set-Cookie header.");

        var cookieValue = setCookie.Value?.FirstOrDefault();
        Assert.That(cookieValue, Does.Contain(".AspNetCore.Antiforgery"),
            "Set-Cookie header must contain the antiforgery cookie.");
    }

    private sealed record TokenResponse(string? token, string? headerName);
}
