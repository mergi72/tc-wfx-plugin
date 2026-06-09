namespace TcWfxPlugin.Contracts;

public sealed class CredentialBrokerAuthRequirement
{
    public required string Mode { get; init; }
    public string? Target { get; init; }
    public string? TargetBase { get; init; }
    public bool Required { get; init; } = true;
}

public sealed class CredentialBrokerRequest
{
    public string? Provider { get; init; }
    public required CredentialBrokerAuthRequirement Auth { get; init; }
}

public sealed class CredentialBrokerResponse
{
    public bool Ok { get; init; }
    public BridgeAuthContext? Auth { get; init; }
    public string? Message { get; init; }
}
