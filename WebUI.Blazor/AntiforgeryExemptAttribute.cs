// MemorySmith.Agent — Web UI & Agent Host
// Sprint 60 — TSK-0350: Global antiforgery filter

namespace WebUI.Blazor;

/// <summary>
/// Marks an endpoint or controller as exempt from antiforgery token validation.
/// Used for WebSocket hubs (SignalR), webhook callbacks, and other endpoints
/// where CSRF is not applicable or tokens cannot be provided.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class IgnoreAntiforgeryTokenAttribute : Attribute;
