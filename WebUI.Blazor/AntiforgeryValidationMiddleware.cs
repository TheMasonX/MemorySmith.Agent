// MemorySmith.Agent — Web UI & Agent Host
// Sprint 60 (TSK-0350): Global antiforgery validation middleware

using Microsoft.AspNetCore.Antiforgery;

namespace WebUI.Blazor;

/// <summary>
/// Middleware that enforces antiforgery token validation on unsafe HTTP methods
/// (POST, PUT, PATCH, DELETE) by calling <see cref="IAntiforgery.ValidateRequestAsync"/>
/// directly.
///
/// This middleware must run AFTER <c>app.UseAntiforgery()</c> so the antiforgery
/// cookie and metadata infrastructure is in place. It explicitly validates all
/// unsafe methods because the <c>UseAntiforgery()</c> middleware in .NET 9+ only
/// validates endpoints that have <c>IAntiforgeryMetadata.RequiresValidation = true</c>
/// — which by default only applies to form-data minimal APIs, not JSON endpoints.
///
/// Endpoints with <c>.DisableAntiforgery()</c> are skipped by checking for
/// <see cref="IAntiforgeryMetadata"/> where <c>RequiresValidation == false</c>.
///
/// Safe HTTP methods (GET, HEAD, OPTIONS, TRACE) are skipped.
/// </summary>
public sealed class AntiforgeryValidationMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> UnsafeMethods =
        ["POST", "PUT", "PATCH", "DELETE"];

    public async Task InvokeAsync(HttpContext context)
    {
        var httpMethod = context.Request.Method;

        // Skip safe methods — they don't need antiforgery
        if (!UnsafeMethods.Contains(httpMethod))
        {
            await next(context);
            return;
        }

        // Check if the endpoint has .DisableAntiforgery()
        var endpoint = context.GetEndpoint();
        if (endpoint is not null)
        {
            var antiforgeryMetadata = endpoint.Metadata
                .GetMetadata<IAntiforgeryMetadata>();
            if (antiforgeryMetadata is not null && !antiforgeryMetadata.RequiresValidation)
            {
                await next(context);
                return;
            }
        }

        // First, check if the UseAntiforgery() middleware already validated
        // (it may have for form-data endpoints or endpoints with RequireAntiforgery())
        var feature = context.Features.Get<IAntiforgeryValidationFeature>();
        if (feature is not null)
        {
            if (feature.IsValid)
            {
                await next(context);
                return;
            }

            // Feature exists but invalid — the UseAntiforgery() middleware validated
            // and found the token was missing/invalid.
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"error\":\"Antiforgery token validation failed. " +
                "Include the request token in the 'RequestVerificationToken' header " +
                "(or the configured header name) obtained from GET /antiforgery/token.\"}");
            return;
        }

        // Feature is null — UseAntiforgery() didn't validate (JSON endpoint).
        // Validate explicitly using IAntiforgery.
        try
        {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            await antiforgery.ValidateRequestAsync(context);
            await next(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"error\":\"Antiforgery token validation failed. " +
                "Include the request token in the 'RequestVerificationToken' header " +
                "(or the configured header name) obtained from GET /antiforgery/token.\"}");
        }
    }
}
