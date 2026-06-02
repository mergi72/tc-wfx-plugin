namespace TcWfxPlugin.Wfx;

public sealed class WfxTransferProgress
{
    public required string Operation { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public long BytesTransferred { get; init; }
    public long? TotalBytes { get; init; }
    public bool IsCompleted { get; init; }
}