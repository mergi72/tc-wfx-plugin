using System.Text.Json.Serialization;

namespace TcWfxPlugin.Contracts;

public sealed class WfxPathRequest
{
    public required string Path { get; init; }
    public required BridgeAuthContext Auth { get; init; }
}

public sealed class WfxMoveRequest
{
    public required string Source { get; init; }
    public required string Destination { get; init; }
    public required BridgeAuthContext Auth { get; init; }
    public BridgeAuthContext? SourceAuth { get; init; }
    public BridgeAuthContext? DestinationAuth { get; init; }
    public bool Overwrite { get; init; }
    public WfxUploadVersioning? Versioning { get; init; }
}

public sealed class WfxUploadRequest
{
    public required string Destination { get; init; }
    public required BridgeAuthContext Auth { get; init; }
    public required string FileName { get; init; }
    public string? ContentBase64 { get; init; }
    public string? SourcePath { get; init; }
    public bool Overwrite { get; init; }
    public WfxUploadVersioning? Versioning { get; init; }
}

public sealed class WfxUploadVersioning
{
    public string Mode { get; init; } = "version";

    [JsonPropertyName("majorVersion")]
    public bool MajorVersion { get; init; }

    public string? Comment { get; init; }
}

public sealed class WfxShareUrlRequest
{
    public required string ShareUrl { get; init; }
    public string Provider { get; init; } = "alfresco";
}

public sealed class WfxShareUrlBrowseRequest
{
    public required string ShareUrl { get; init; }
    public required BridgeAuthContext Auth { get; init; }
    public string Provider { get; init; } = "alfresco";
    public string Operation { get; init; } = "list";
    public bool Execute { get; init; } = true;
    public string? ProviderPathOverride { get; init; }
    public string? DestinationShareUrl { get; init; }
    public string? DestinationPathOverride { get; init; }
    public string? FileName { get; init; }
    public string? ContentBase64 { get; init; }
    public bool Overwrite { get; init; }
    public WfxUploadVersioning? Versioning { get; init; }
}
