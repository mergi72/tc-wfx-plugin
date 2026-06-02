using System.Text.Json;

namespace TcWfxPlugin.Contracts;

public sealed class WfxResponse<TData>
{
    public bool Ok { get; init; }
    public int ErrorCode { get; init; }
    public string? Message { get; init; }
    public TData? Data { get; init; }
    public Dictionary<string, JsonElement>? Metadata { get; init; }

    public static WfxResponse<TData> Failed(string message, int errorCode = -1)
    {
        return new WfxResponse<TData>
        {
            Ok = false,
            ErrorCode = errorCode,
            Message = message,
            Data = default,
            Metadata = null,
        };
    }
}

public sealed class WfxListingData
{
    public required string Provider { get; init; }
    public required string Path { get; init; }
    public int Total { get; init; }
    public required IReadOnlyList<WfxItemDto> Items { get; init; }
}

public sealed class WfxItemDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public bool IsFolder { get; init; }
    public long? Size { get; init; }
    public string? MimeType { get; init; }
}

public sealed class WfxProvidersData
{
    public required IReadOnlyList<string> Providers { get; init; }
    public string? DefaultProvider { get; init; }
    public Dictionary<string, WfxProviderCapabilities>? Capabilities { get; init; }
}

public sealed class WfxProviderCapabilities
{
    public bool List { get; init; } = true;
    public bool Stat { get; init; } = true;
    public bool Mkdir { get; init; } = true;
    public bool Delete { get; init; } = true;
    public bool Rename { get; init; } = true;
    public bool Copy { get; init; } = true;
    public bool Download { get; init; } = true;
    public bool Upload { get; init; } = true;
}
