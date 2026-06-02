using System.Text.Json;
using TcWfxPlugin.Core;

namespace TcWfxPlugin.Wfx;

internal sealed class WfxTransferService
{
    private const int IoChunkSize = 64 * 1024;

    private readonly WfxPluginFacade _facade;
    private readonly IWfxAuthProvider _authProvider;

    public WfxTransferService(WfxPluginFacade facade, IWfxAuthProvider authProvider)
    {
        _facade = facade;
        _authProvider = authProvider;
    }

    public async Task<int> MkDirAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderPath, out var providerPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.CreateDirectoryAsync(providerPath, _authProvider.GetAuthContext(), cancellationToken);
        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    public async Task<int> DeleteAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderPath, out var providerPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.DeleteAsync(providerPath, _authProvider.GetAuthContext(), cancellationToken);
        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    public async Task<int> RenameAsync(string totalCommanderSourcePath, string totalCommanderDestinationPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderSourcePath, out var sourceProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderDestinationPath, out var destinationProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.RenameAsync(sourceProviderPath, destinationProviderPath, _authProvider.GetAuthContext(), cancellationToken);
        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    public async Task<int> CopyAsync(string totalCommanderSourcePath, string totalCommanderDestinationPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderSourcePath, out var sourceProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderDestinationPath, out var destinationProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.CopyAsync(sourceProviderPath, destinationProviderPath, _authProvider.GetAuthContext(), cancellationToken);
        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    public async Task<int> GetFileAsync(
        string totalCommanderSourcePath,
        string localTargetPath,
        IProgress<WfxTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderSourcePath, out var sourceProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.DownloadAsync(sourceProviderPath, _authProvider.GetAuthContext(), cancellationToken);
        if (!response.Ok || response.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return WfxBridgeErrorMapper.MapError(response.ErrorCode);
        }

        if (!TryGetContentBase64(response.Data, out var contentBase64))
        {
            return WfxResultCodes.ReadError;
        }

        byte[] rawContent;
        try
        {
            rawContent = Convert.FromBase64String(contentBase64);
        }
        catch (FormatException)
        {
            return WfxResultCodes.ReadError;
        }

        var targetDirectory = Path.GetDirectoryName(localTargetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var bytesTransferred = 0L;
        progress?.Report(new WfxTransferProgress
        {
            Operation = "download",
            SourcePath = totalCommanderSourcePath,
            DestinationPath = localTargetPath,
            BytesTransferred = bytesTransferred,
            TotalBytes = rawContent.LongLength,
            IsCompleted = false,
        });

        await using (var output = new FileStream(localTargetPath, FileMode.Create, FileAccess.Write, FileShare.None, IoChunkSize, useAsync: true))
        {
            var offset = 0;
            while (offset < rawContent.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var count = Math.Min(IoChunkSize, rawContent.Length - offset);
                await output.WriteAsync(rawContent.AsMemory(offset, count), cancellationToken);
                offset += count;
                bytesTransferred += count;

                progress?.Report(new WfxTransferProgress
                {
                    Operation = "download",
                    SourcePath = totalCommanderSourcePath,
                    DestinationPath = localTargetPath,
                    BytesTransferred = bytesTransferred,
                    TotalBytes = rawContent.LongLength,
                    IsCompleted = bytesTransferred >= rawContent.LongLength,
                });
            }
        }

        return WfxResultCodes.Success;
    }

    public async Task<int> PutFileAsync(
        string localSourcePath,
        string totalCommanderDestinationPath,
        bool overwrite,
        IProgress<WfxTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localSourcePath))
        {
            return WfxResultCodes.FileNotFound;
        }

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderDestinationPath, out var destinationProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var fileName = Path.GetFileName(localSourcePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return WfxResultCodes.WriteError;
        }

        byte[] content;
        await using (var source = new FileStream(localSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, IoChunkSize, useAsync: true))
        {
            var totalBytes = source.Length;
            var buffer = new byte[IoChunkSize];
            var transferred = 0L;

            progress?.Report(new WfxTransferProgress
            {
                Operation = "upload",
                SourcePath = localSourcePath,
                DestinationPath = totalCommanderDestinationPath,
                BytesTransferred = transferred,
                TotalBytes = totalBytes,
                IsCompleted = false,
            });

            using var memory = new MemoryStream();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                transferred += read;

                progress?.Report(new WfxTransferProgress
                {
                    Operation = "upload",
                    SourcePath = localSourcePath,
                    DestinationPath = totalCommanderDestinationPath,
                    BytesTransferred = transferred,
                    TotalBytes = totalBytes,
                    IsCompleted = transferred >= totalBytes,
                });
            }

            content = memory.ToArray();
        }

        var contentBase64 = Convert.ToBase64String(content);

        var response = await _facade.UploadAsync(
            destinationProviderPath,
            fileName,
            _authProvider.GetAuthContext(),
            contentBase64,
            overwrite,
            cancellationToken);

        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    private static bool TryGetContentBase64(JsonElement data, out string contentBase64)
    {
        contentBase64 = string.Empty;

        if (data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!data.TryGetProperty("content_base64", out var contentProperty) || contentProperty.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = contentProperty.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        contentBase64 = value;
        return true;
    }
}
