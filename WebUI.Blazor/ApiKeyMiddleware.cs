namespace WebUI.Blazor;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Middleware that validates an API key on all /api/* routes.
/// Sprint 30 P1-C (SEC-01): unauthenticated REST endpoints allowed any LAN user
/// to enqueue arbitrary bot actions. This middleware adds a shared-secret gate.
///
/// Configuration: set <c>Agent:ApiKey</c> in appsettings.json or the environment
/// variable <c>Agent__ApiKey</c>. Clients must include the key in <c>X-Api-Key</c>.
/// When no key is configured the middleware allows all requests (dev/localhost mode).
/// See Data/Pages/guides/getting-started.md for setup instructions.
/// </summary>
public sealed class ApiKeyMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    ILogger<ApiKeyMiddleware> logger)
{
    private readonly string? _apiKey = configuration["Agent:ApiKey"];

    public async Task InvokeAsync(HttpContext context)
    {
        // If no key is configured, skip enforcement (localhost dev convenience).
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) ||
            !FixedTimeEquals(_apiKey, provided.ToString()))
        {
            logger.LogWarning(
                "API key validation failed for {Method} {Path}",
                context.Request.Method, context.Request.Path);
            context.Response.StatusCode  = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"error\":\"Unauthorized: missing or invalid X-Api-Key header.\"}");
            return;
        }

        await next(context);
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var e = Encoding.UTF8.GetBytes(expected);
        var a = Encoding.UTF8.GetBytes(actual);
        return CryptographicOperations.FixedTimeEquals(e, a);
    }
}
