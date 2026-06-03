namespace TcWfxPlugin.Wfx;

public sealed class WfxFindData
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public string? MimeType { get; init; }
    public DateTimeOffset? LastWriteTimeUtc { get; init; }
    public bool IsReadOnly { get; init; }
}
