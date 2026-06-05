using System.Text.Json;
using TcWfxPlugin.Core;

namespace TcWfxPlugin.Wfx;

internal sealed class WfxTransferService
{
    private const int IoChunkSize = 64 * 1024;

    private readonly WfxPluginFacade _facade;
    private readonly IWfxAuthProvider _authProvider;
    private readonly IWfxProgressReporterFactory _progressReporterFactory;

    public WfxTransferService(
        WfxPluginFacade facade,
        IWfxAuthProvider authProvider,
        IWfxProgressReporterFactory? progressReporterFactory = null)
    {
        _facade = facade;
        _authProvider = authProvider;
        _progressReporterFactory = progressReporterFactory ?? new WfxProgressReporterFactory();
    }

    public async Task<int> MkDirAsync(
        string totalCommanderPath,
        IProgress<WfxTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var operation = _progressReporterFactory.CreateUnit(
            progress,
            operation: "mkdir",
            sourcePath: totalCommanderPath,
            destinationPath: totalCommanderPath);

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderPath, out var providerPath))
        {
            operation.Finish(false);
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.CreateDirectoryAsync(providerPath, _authProvider.GetAuthContext(), cancellationToken);
        operation.Finish(response.Ok);
        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    public async Task<int> DeleteAsync(
        string totalCommanderPath,
        IProgress<WfxTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var operation = _progressReporterFactory.CreateUnit(
            progress,
            operation: "delete",
            sourcePath: totalCommanderPath,
            destinationPath: totalCommanderPath);

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderPath, out var providerPath))
        {
            operation.Finish(false);
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.DeleteAsync(providerPath, _authProvider.GetAuthContext(), cancellationToken);
        operation.Finish(response.Ok);
        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    public async Task<int> RenameAsync(
        string totalCommanderSourcePath,
        string totalCommanderDestinationPath,
        IProgress<WfxTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var operation = _progressReporterFactory.CreateUnit(
            progress,
            operation: "move",
            sourcePath: totalCommanderSourcePath,
            destinationPath: totalCommanderDestinationPath);

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderSourcePath, out var sourceProviderPath))
        {
            operation.Finish(false);
            return WfxResultCodes.FileNotFound;
        }

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderDestinationPath, out var destinationProviderPath))
        {
            operation.Finish(false);
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.RenameAsync(sourceProviderPath, destinationProviderPath, _authProvider.GetAuthContext(), cancellationToken);
        operation.Finish(response.Ok);
        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    public async Task<int> CopyAsync(
        string totalCommanderSourcePath,
        string totalCommanderDestinationPath,
        IProgress<WfxTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var operation = _progressReporterFactory.CreateUnit(
            progress,
            operation: "copy",
            sourcePath: totalCommanderSourcePath,
            destinationPath: totalCommanderDestinationPath);

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderSourcePath, out var sourceProviderPath))
        {
            operation.Finish(false);
            return WfxResultCodes.FileNotFound;
        }

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderDestinationPath, out var destinationProviderPath))
        {
            operation.Finish(false);
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.CopyAsync(sourceProviderPath, destinationProviderPath, _authProvider.GetAuthContext(), cancellationToken);
        operation.Finish(response.Ok);
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

        // Pre-fetch the remote file size so TC shows progress from the very start,
        // before the potentially slow DownloadRawAsync response headers arrive.
        var expectedSize = await TryGetRemoteSizeAsync(sourceProviderPath, cancellationToken);

        using var operation = _progressReporterFactory.Create(
            progress,
            operation: "download",
            sourcePath: totalCommanderSourcePath,
            destinationPath: localTargetPath,
            totalBytes: expectedSize);

        operation.Report(0);

        var rawDownload = await _facade.DownloadRawAsync(sourceProviderPath, _authProvider.GetAuthContext(), cancellationToken);
        if (rawDownload is not null)
        {
            if (!rawDownload.Ok || rawDownload.Session is null)
            {
                return WfxBridgeErrorMapper.MapError(rawDownload.ErrorCode);
            }

            using (rawDownload.Session)
            {
                var rawTargetDirectory = Path.GetDirectoryName(localTargetPath);
                if (!string.IsNullOrWhiteSpace(rawTargetDirectory))
                {
                    Directory.CreateDirectory(rawTargetDirectory);
                }

                var totalBytes = rawDownload.Session.ContentLength ?? expectedSize;
                var rawBytesTransferred = 0L;
                operation.SetTotalBytes(totalBytes);
                operation.Report(0);

                var buffer = new byte[IoChunkSize];
                await using (var output = new FileStream(localTargetPath, FileMode.Create, FileAccess.Write, FileShare.None, IoChunkSize, useAsync: true))
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var read = await rawDownload.Session.ContentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                        if (read == 0)
                        {
                            break;
                        }

                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        rawBytesTransferred += read;

                        operation.Report(rawBytesTransferred);
                    }
                }

                operation.Finish(true, totalBytes ?? rawBytesTransferred);

                return WfxResultCodes.Success;
            }
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
        operation.SetTotalBytes(rawContent.LongLength);
        operation.Report(bytesTransferred);

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

                operation.Report(bytesTransferred);
            }
        }

        operation.Finish(true, bytesTransferred);

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

        var auth = _authProvider.GetAuthContext();
        ResolveUploadTarget(destinationProviderPath, out var uploadDestinationProviderPath, out var uploadFileNameFromPath, out var destinationLooksLikeFile);

        var fileName = uploadFileNameFromPath;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = Path.GetFileName(localSourcePath);
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return WfxResultCodes.WriteError;
        }

        if (!destinationLooksLikeFile)
        {
            var statResponse = await _facade.GetItemInfoAsync(destinationProviderPath, auth, cancellationToken);
            if (statResponse.Ok && TryGetIsFolder(statResponse.Data, out var isFolder) && !isFolder)
            {
                ResolveUploadTarget(destinationProviderPath, out uploadDestinationProviderPath, out uploadFileNameFromPath, out _, destinationLooksLikeFileHint: true);
                if (!string.IsNullOrWhiteSpace(uploadFileNameFromPath))
                {
                    fileName = uploadFileNameFromPath;
                }
            }
        }

        var totalBytes = new FileInfo(localSourcePath).Length;
        long reportedUploadBytes = 0;
        using var operation = _progressReporterFactory.Create(
            progress,
            operation: "upload",
            sourcePath: localSourcePath,
            destinationPath: totalCommanderDestinationPath,
            totalBytes: totalBytes);
        operation.Report(0);

        IProgress<long>? uploadProgress = null;
        if (progress is not null)
        {
            uploadProgress = new UploadByteProgress(bytesTransferred =>
            {
                var normalizedBytes = Math.Clamp(bytesTransferred, 0, totalBytes);
                Interlocked.Exchange(ref reportedUploadBytes, normalizedBytes);
                operation.Report(normalizedBytes);
            });
        }

        var response = await _facade.UploadRawAsync(
            uploadDestinationProviderPath,
            fileName,
            auth,
            localSourcePath,
            overwrite,
            uploadProgress,
            cancellationToken);

        var finalBytesTransferred = response.Ok
            ? totalBytes
            : Interlocked.Read(ref reportedUploadBytes);

        operation.Finish(response.Ok, finalBytesTransferred);

        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    private static void ResolveUploadTarget(
        string destinationProviderPath,
        out string uploadDestinationProviderPath,
        out string uploadFileName,
        out bool destinationLooksLikeFile,
        bool destinationLooksLikeFileHint = false)
    {
        uploadDestinationProviderPath = destinationProviderPath;
        uploadFileName = string.Empty;
        destinationLooksLikeFile = false;

        if (!ProviderPath.TryParse(destinationProviderPath, out var parsed))
        {
            return;
        }

        var normalizedPath = parsed.Path;
        if (normalizedPath.Length <= 1 || normalizedPath.EndsWith('/'))
        {
            return;
        }

        var slashIndex = normalizedPath.LastIndexOf('/');
        var leaf = slashIndex >= 0 ? normalizedPath[(slashIndex + 1)..] : normalizedPath.TrimStart('/');
        if (string.IsNullOrWhiteSpace(leaf))
        {
            return;
        }

        destinationLooksLikeFile = destinationLooksLikeFileHint || leaf.Contains('.', StringComparison.Ordinal);
        if (!destinationLooksLikeFile)
        {
            return;
        }

        uploadFileName = leaf;
        var parentPath = slashIndex <= 0 ? "/" : normalizedPath[..slashIndex];
        uploadDestinationProviderPath = $"{parsed.Provider}:{parentPath}";
    }

    private async Task<long?> TryGetRemoteSizeAsync(string providerPath, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _facade.GetItemInfoAsync(providerPath, _authProvider.GetAuthContext(), cancellationToken);
            if (!response.Ok || response.Data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (response.Data.TryGetProperty("size", out var sizeProperty)
                && sizeProperty.ValueKind == JsonValueKind.Number
                && sizeProperty.TryGetInt64(out var size)
                && size > 0)
            {
                return size;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Best-effort: if stat fails, continue download without known size.
        }

        return null;
    }

    private static bool TryGetIsFolder(JsonElement data, out bool isFolder)
    {
        isFolder = false;
        if (data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!data.TryGetProperty("is_folder", out var isFolderProperty) || isFolderProperty.ValueKind != JsonValueKind.True && isFolderProperty.ValueKind != JsonValueKind.False)
        {
            return false;
        }

        isFolder = isFolderProperty.GetBoolean();
        return true;
    }

    public async Task<bool> PathExistsAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderPath, out var providerPath))
        {
            return false;
        }

        var response = await _facade.GetItemInfoAsync(providerPath, _authProvider.GetAuthContext(), cancellationToken);
        if (response.Ok)
        {
            return true;
        }

        // Bridge not-found code from commander_api.WfxErrorCode.
        if (response.ErrorCode == 2 || response.ErrorCode == 404)
        {
            return false;
        }

        return false;
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

    private sealed class UploadByteProgress : IProgress<long>
    {
        private readonly Action<long> _onReport;

        public UploadByteProgress(Action<long> onReport)
        {
            _onReport = onReport;
        }

        public void Report(long value)
        {
            _onReport(value);
        }
    }
}
