namespace TcWfxPlugin.Contracts;

public sealed class BridgeHealthResponse
{
    public string Status { get; init; } = string.Empty;
    public string Service { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? Timestamp { get; init; }
}
