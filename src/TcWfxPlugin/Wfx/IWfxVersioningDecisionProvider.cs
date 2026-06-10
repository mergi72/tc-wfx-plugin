using System.Text.Json;
using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Wfx;

public interface IWfxVersioningDecisionProvider
{
    WfxUploadVersioning? ChooseVersioning(WfxVersioningRequest request);
}

public sealed class WfxVersioningRequest
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required string FileName { get; init; }
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}
