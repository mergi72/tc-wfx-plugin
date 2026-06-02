namespace TcWfxPlugin.Contracts;

public sealed class BridgeAuthContext
{
    public required string Mode { get; init; }
    public string? CredentialId { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Token { get; init; }
    public string? WinUser { get; init; }
}
