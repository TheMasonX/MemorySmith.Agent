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
/// When no key is configured the middleware blocks all requests UNLESS
/// <c>Agent:AllowUnauthenticatedApi</c> is explicitly set to <c>true</c>
/// (dev/localhost convenience).
/// See Data/Pages/guides/getting-started.md for setup instructions.
/// </summary>
public sealed class ApiKeyMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    ILogger<ApiKeyMiddleware> logger)
{
    private readonly string? _apiKey = configuration["Agent:ApiKey"];
    private readonly bool _allowUnauthenticated = 
        bool.TryParse(configuration["Agent:AllowUnauthenticatedApi"], out var val) && val;

    public async Task InvokeAsync(HttpContext context)
    {
        // If no key is configured, enforce unless explicitly opted out.
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            if (!_allowUnauthenticated)
            {
                logger.LogWarning(
                    "API key enforcement: Agent:ApiKey is not configured and " +
                    "Agent:AllowUnauthenticatedApi is false. Blocking {Method} {Path}.",
                    context.Request.Method, context.Request.Path);
                context.Response.StatusCode  = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    "{\"error\":\"Unauthorized: Agent:ApiKey not configured. " +
                    "Set Agent:AllowUnauthenticatedApi=true for local-only bypass.\"}");
                return;
            }

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
