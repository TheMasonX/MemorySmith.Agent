namespace WebUI.Blazor.Dashboard.Contracts;

/// <summary>
/// Reserved for future spatial/video UI support.
/// Source identifies the camera/adapter, Type is the media format (e.g. "image/jpeg"),
/// and Url is the frame endpoint.
/// </summary>
public sealed record ViewportSnapshot(
    string Source,
    string Type,
    string Url);
