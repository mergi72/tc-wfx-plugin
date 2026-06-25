using System.Text.Json;
using TcWfxPlugin.Contracts;
using TcWfxPlugin.Core;

namespace TcWfxPlugin.Wfx;

internal sealed class WfxTransferService
{
    private const int IoChunkSize = 64 * 1024;
    private const long SyntheticProgressUnits = 100;

    private readonly WfxPluginFacade _facade;
    private readonly IWfxAuthProvider _authProvider;
    private readonly IWfxProgressReporterFactory _progressReporterFactory;
    private readonly IWfxVersioningDecisionProvider? _versioningDecisionProvider;
    private readonly IWfxOverwriteDecisionProvider? _overwriteDecisionProvider;

    public WfxTransferService(
        WfxPluginFacade facade,
        IWfxAuthProvider authProvider,
        IWfxProgressReporterFactory? progressReporterFactory = null,
        IWfxVersioningDecisionProvider? versioningDecisionProvider = null,
        IWfxOverwriteDecisionProvider? overwriteDecisionProvider = null)
    {
        _facade = facade;
        _authProvider = authProvider;
        _progressReporterFactory = progressReporterFactory ?? new WfxProgressReporterFactory();
        _versioningDecisionProvider = versioningDecisionProvider;
        _overwriteDecisionProvider = overwriteDecisionProvider;
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

        var response = await _facade.CreateDirectoryAsync(providerPath, AuthForProviderPath(providerPath), cancellationToken);
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

        var response = await _facade.DeleteAsync(providerPath, AuthForProviderPath(providerPath), cancellationToken);
        operation.Finish(response.Ok);
        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    public async Task<int> RenameAsync(
        string totalCommanderSourcePath,
        string totalCommanderDestinationPath,
        IProgress<WfxTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var operation = _progressReporterFactory.CreateSynthetic(
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

        var sourceAuth = AuthForProviderPath(sourceProviderPath);
        var destinationAuth = AuthForProviderPath(destinationProviderPath);
        var heartbeat = CreateRemoteTransferHeartbeat(operation);
        var response = await heartbeat.AwaitAsync(_facade.RenameAsync(sourceProviderPath, destinationProviderPath, destinationAuth, sourceAuth, destinationAuth, overwrite: false, versioning: null, cancellationToken: cancellationToken), cancellationToken);
        var overwriteRetryResult = await RetryMoveWhenOverwriteRequiredAsync(
            response,
            operation,
            moveOperation: "move",
            sourcePath: totalCommanderSourcePath,
            destinationPath: totalCommanderDestinationPath,
            fileName: Path.GetFileName(destinationProviderPath.Replace('\\', '/')),
            retry: ct => heartbeat.AwaitAsync(_facade.RenameAsync(sourceProviderPath, destinationProviderPath, destinationAuth, sourceAuth, destinationAuth, overwrite: true, versioning: null, cancellationToken: ct), ct),
            cancellationToken);
        if (overwriteRetryResult.Canceled)
        {
            operation.Finish(false);
            return WfxResultCodes.UserAbort;
        }

        response = overwriteRetryResult.Response;
        var retryResult = await RetryMoveWhenVersionRequiredAsync(
            response,
            operation,
            moveOperation: "move",
            sourcePath: totalCommanderSourcePath,
            destinationPath: totalCommanderDestinationPath,
            fileName: Path.GetFileName(destinationProviderPath.Replace('\\', '/')),
            retry: (versioning, ct) => heartbeat.AwaitAsync(_facade.RenameAsync(sourceProviderPath, destinationProviderPath, destinationAuth, sourceAuth, destinationAuth, overwrite: false, versioning: versioning, cancellationToken: ct), ct),
            cancellationToken);
        if (retryResult.Canceled)
        {
            operation.Finish(false);
            return WfxResultCodes.UserAbort;
        }

        response = retryResult.Response;
        operation.Finish(response.Ok);
        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    public async Task<int> CopyAsync(
        string totalCommanderSourcePath,
        string totalCommanderDestinationPath,
        IProgress<WfxTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var operation = _progressReporterFactory.CreateSynthetic(
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

        var sourceAuth = AuthForProviderPath(sourceProviderPath);
        var destinationAuth = AuthForProviderPath(destinationProviderPath);
        var heartbeat = CreateRemoteTransferHeartbeat(operation);
        var response = await heartbeat.AwaitAsync(_facade.CopyAsync(sourceProviderPath, destinationProviderPath, destinationAuth, sourceAuth, destinationAuth, overwrite: false, versioning: null, cancellationToken: cancellationToken), cancellationToken);
        var overwriteRetryResult = await RetryMoveWhenOverwriteRequiredAsync(
            response,
            operation,
            moveOperation: "copy",
            sourcePath: totalCommanderSourcePath,
            destinationPath: totalCommanderDestinationPath,
            fileName: Path.GetFileName(destinationProviderPath.Replace('\\', '/')),
            retry: ct => heartbeat.AwaitAsync(_facade.CopyAsync(sourceProviderPath, destinationProviderPath, destinationAuth, sourceAuth, destinationAuth, overwrite: true, versioning: null, cancellationToken: ct), ct),
            cancellationToken);
        if (overwriteRetryResult.Canceled)
        {
            operation.Finish(false);
            return WfxResultCodes.UserAbort;
        }

        response = overwriteRetryResult.Response;
        var retryResult = await RetryMoveWhenVersionRequiredAsync(
            response,
            operation,
            moveOperation: "copy",
            sourcePath: totalCommanderSourcePath,
            destinationPath: totalCommanderDestinationPath,
            fileName: Path.GetFileName(destinationProviderPath.Replace('\\', '/')),
            retry: (versioning, ct) => heartbeat.AwaitAsync(_facade.CopyAsync(sourceProviderPath, destinationProviderPath, destinationAuth, sourceAuth, destinationAuth, overwrite: false, versioning: versioning, cancellationToken: ct), ct),
            cancellationToken);
        if (retryResult.Canceled)
        {
            operation.Finish(false);
            return WfxResultCodes.UserAbort;
        }

        response = retryResult.Response;
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
        ReportProgressStage(operation, expectedSize, 1);
        var rawDownloadHeartbeat = new TransferProgressHeartbeat(operation, expectedSize, startPercent: 2, endPercent: 90, intervalMs: 1000);

        var rawDownloadTask = _facade.DownloadRawAsync(sourceProviderPath, AuthForProviderPath(sourceProviderPath), cancellationToken);
        var rawDownload = await rawDownloadHeartbeat.AwaitAsync(rawDownloadTask, cancellationToken);
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

                var contentLength = rawDownload.Session.ContentLength;
                var totalBytes = contentLength ?? expectedSize;
                operation.ReportDiagnostic(
                    $"download_size contentLength={contentLength?.ToString() ?? "null"} expectedSize={expectedSize?.ToString() ?? "null"} totalBytes={totalBytes?.ToString() ?? "null"} source={(contentLength.HasValue ? "content-length" : expectedSize.HasValue ? "stat" : "unknown")}");
                var rawBytesTransferred = 0L;
                operation.SetTotalBytes(totalBytes);

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

        var response = await _facade.DownloadAsync(sourceProviderPath, AuthForProviderPath(sourceProviderPath), cancellationToken);
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
        ReportProgressStage(operation, rawContent.LongLength, 5);

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

                operation.Report(MapDownloadReadToDisplayBytes(bytesTransferred, rawContent.LongLength));
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

        var auth = AuthForProviderPath(destinationProviderPath);
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
        var uploadHeartbeat = new TransferProgressHeartbeat(operation, totalBytes, startPercent: 1, endPercent: 99);

        IProgress<long>? uploadProgress = null;
        if (progress is not null)
        {
            uploadProgress = new UploadByteProgress(bytesTransferred =>
            {
                var normalizedBytes = Math.Clamp(bytesTransferred, 0, totalBytes);
                Interlocked.Exchange(ref reportedUploadBytes, normalizedBytes);
                var displayBytes = MapUploadReadToDisplayBytes(normalizedBytes, totalBytes);
                var displayPercent = CalculateProgressPercent(displayBytes, totalBytes);
                if (displayPercent >= uploadHeartbeat.LastReportedPercent)
                {
                    operation.Report(displayBytes);
                }

                uploadHeartbeat.AdvanceToAtLeast(displayPercent + 1);
            });
        }

        var uploadTask = _facade.UploadRawAsync(
            uploadDestinationProviderPath,
            fileName,
            auth,
            localSourcePath,
            overwrite,
            versioning: null,
            uploadProgress,
            cancellationToken);
        var response = await uploadHeartbeat.AwaitAsync(uploadTask, cancellationToken);
        operation.ReportDiagnostic(
            $"upload_response ok={response.Ok} error_code={response.ErrorCode} message={SanitizeDiagnosticMessage(response.Message)} metadata={FormatMetadataKeys(response.Metadata)}");
        if (IsVersionRequiredResponse(response))
        {
            operation.ReportDiagnostic($"upload_version_required file={fileName} destination={uploadDestinationProviderPath}");
            var versioning = _versioningDecisionProvider?.ChooseVersioning(new WfxVersioningRequest
            {
                SourcePath = localSourcePath,
                DestinationPath = totalCommanderDestinationPath,
                FileName = fileName,
                Metadata = response.Metadata,
            });

            if (versioning is not null)
            {
                var retryTask = _facade.UploadRawAsync(
                    uploadDestinationProviderPath,
                    fileName,
                    auth,
                    localSourcePath,
                    overwrite,
                    versioning,
                    uploadProgress,
                    cancellationToken);
                response = await uploadHeartbeat.AwaitAsync(retryTask, cancellationToken);
                operation.ReportDiagnostic(
                    $"upload_version_retry_response ok={response.Ok} error_code={response.ErrorCode} message={SanitizeDiagnosticMessage(response.Message)} metadata={FormatMetadataKeys(response.Metadata)}");
            }
            else
            {
                operation.ReportDiagnostic($"upload_version_canceled file={fileName} destination={uploadDestinationProviderPath}");
                operation.Finish(false, Interlocked.Read(ref reportedUploadBytes));
                return WfxResultCodes.UserAbort;
            }
        }

        var finalBytesTransferred = response.Ok
            ? totalBytes
            : Interlocked.Read(ref reportedUploadBytes);

        operation.Finish(response.Ok, finalBytesTransferred);

        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    private BridgeAuthContext AuthForProviderPath(string providerPath)
    {
        return _authProvider.GetAuthContext(ConnectionNameFromProviderPath(providerPath));
    }

    private static string? ConnectionNameFromProviderPath(string providerPath)
    {
        return ProviderPath.TryParse(providerPath, out var parsed) ? parsed.Provider : null;
    }
    private static bool IsVersionRequiredResponse(WfxResponse<JsonElement> response)
    {
        if (response.Ok)
        {
            return false;
        }

        if (response.Metadata is null)
        {
            return IsVersionRequiredMessage(response.Message);
        }

        if (!response.Metadata.TryGetValue("action", out var action) || action.ValueKind != JsonValueKind.String)
        {
            return IsVersionRequiredMessage(response.Message);
        }

        return string.Equals(action.GetString(), "version_required", StringComparison.OrdinalIgnoreCase)
            || IsVersionRequiredMessage(response.Message);
    }

    private static bool IsOverwriteRequiredResponse(WfxResponse<JsonElement> response)
    {
        if (response.Ok)
        {
            return false;
        }

        if (response.Metadata is null)
        {
            return IsOverwriteRequiredMessage(response.Message);
        }

        if (!response.Metadata.TryGetValue("action", out var action) || action.ValueKind != JsonValueKind.String)
        {
            return IsOverwriteRequiredMessage(response.Message);
        }

        return string.Equals(action.GetString(), "overwrite_required", StringComparison.OrdinalIgnoreCase)
            || IsOverwriteRequiredMessage(response.Message);
    }

    private async Task<OverwriteRetryResult> RetryMoveWhenOverwriteRequiredAsync(
        WfxResponse<JsonElement> response,
        IWfxProgressReporter operation,
        string moveOperation,
        string sourcePath,
        string destinationPath,
        string fileName,
        Func<CancellationToken, Task<WfxResponse<JsonElement>>> retry,
        CancellationToken cancellationToken)
    {
        if (!IsOverwriteRequiredResponse(response))
        {
            return new OverwriteRetryResult(false, response);
        }

        operation.ReportDiagnostic($"{moveOperation}_overwrite_required file={fileName} destination={destinationPath}");
        var overwrite = _overwriteDecisionProvider?.ConfirmOverwrite(new WfxOverwriteRequest
        {
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            FileName = fileName,
            Metadata = response.Metadata,
        }) ?? false;

        if (!overwrite)
        {
            operation.ReportDiagnostic($"{moveOperation}_overwrite_canceled file={fileName} destination={destinationPath}");
            return new OverwriteRetryResult(true, response);
        }

        operation.ReportDiagnostic($"{moveOperation}_overwrite_confirmed file={fileName} destination={destinationPath}");
        var retryResponse = await retry(cancellationToken);
        operation.ReportDiagnostic(
            $"{moveOperation}_overwrite_retry_response ok={retryResponse.Ok} error_code={retryResponse.ErrorCode} message={SanitizeDiagnosticMessage(retryResponse.Message)} metadata={FormatMetadataKeys(retryResponse.Metadata)}");
        return new OverwriteRetryResult(false, retryResponse);
    }

    private readonly record struct OverwriteRetryResult(bool Canceled, WfxResponse<JsonElement> Response);

    private async Task<VersionRetryResult> RetryMoveWhenVersionRequiredAsync(
        WfxResponse<JsonElement> response,
        IWfxProgressReporter operation,
        string moveOperation,
        string sourcePath,
        string destinationPath,
        string fileName,
        Func<WfxUploadVersioning, CancellationToken, Task<WfxResponse<JsonElement>>> retry,
        CancellationToken cancellationToken)
    {
        if (!IsVersionRequiredResponse(response))
        {
            return new VersionRetryResult(false, response);
        }

        operation.ReportDiagnostic($"{moveOperation}_version_required file={fileName} destination={destinationPath}");
        operation.ReportDiagnostic($"{moveOperation}_version_dialog_open file={fileName} destination={destinationPath}");
        var versioning = _versioningDecisionProvider?.ChooseVersioning(new WfxVersioningRequest
        {
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            FileName = fileName,
            Metadata = response.Metadata,
        });

        if (versioning is null)
        {
            operation.ReportDiagnostic($"{moveOperation}_version_canceled file={fileName} destination={destinationPath}");
            return new VersionRetryResult(true, response);
        }

        operation.ReportDiagnostic($"{moveOperation}_version_dialog_choice file={fileName} destination={destinationPath} major={versioning.MajorVersion}");
        var retryResponse = await retry(versioning, cancellationToken);
        operation.ReportDiagnostic(
            $"{moveOperation}_version_retry_response ok={retryResponse.Ok} error_code={retryResponse.ErrorCode} message={SanitizeDiagnosticMessage(retryResponse.Message)} metadata={FormatMetadataKeys(retryResponse.Metadata)}");
        return new VersionRetryResult(false, retryResponse);
    }

    private readonly record struct VersionRetryResult(bool Canceled, WfxResponse<JsonElement> Response);

    private static bool IsVersionRequiredMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("version choice", StringComparison.OrdinalIgnoreCase)
            || message.Contains("version_required", StringComparison.OrdinalIgnoreCase)
            || message.Contains("document already exists", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOverwriteRequiredMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("overwrite_required", StringComparison.OrdinalIgnoreCase)
            || message.Contains("overwrite choice", StringComparison.OrdinalIgnoreCase)
            || message.Contains("requires overwrite", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatMetadataKeys(IReadOnlyDictionary<string, JsonElement>? metadata)
    {
        return metadata is null || metadata.Count == 0
            ? "-"
            : string.Join(",", metadata.Keys.OrderBy(static key => key, StringComparer.Ordinal));
    }

    private static string SanitizeDiagnosticMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "-";
        }

        return message.Replace('\r', ' ').Replace('\n', ' ');
    }

    private static TransferProgressHeartbeat CreateRemoteTransferHeartbeat(IWfxProgressReporter operation)
    {
        return new TransferProgressHeartbeat(
            operation,
            SyntheticProgressUnits,
            startPercent: 1,
            endPercent: 95,
            intervalMs: 1000);
    }

    private sealed class TransferProgressHeartbeat
    {
        private readonly IWfxProgressReporter _operation;
        private readonly long? _totalBytes;
        private readonly int _maxPercent;
        private readonly int _intervalMs;
        private int _nextPercent;
        private int _lastReportedPercent;

        public TransferProgressHeartbeat(IWfxProgressReporter operation, long? totalBytes, int startPercent, int endPercent, int intervalMs = 250)
        {
            _operation = operation;
            _totalBytes = totalBytes;
            _nextPercent = Math.Clamp(startPercent, 0, 100);
            _maxPercent = Math.Clamp(endPercent, _nextPercent, 100);
            _intervalMs = Math.Max(100, intervalMs);
        }

        public int NextPercent => _nextPercent;

        public int LastReportedPercent => _lastReportedPercent;

        public async Task<T> AwaitAsync<T>(Task<T> task, CancellationToken cancellationToken)
        {
            while (!task.IsCompleted)
            {
                ReportNext();

                var completed = await Task.WhenAny(task, Task.Delay(_intervalMs, cancellationToken));
                if (ReferenceEquals(completed, task))
                {
                    break;
                }
            }

            return await task;
        }

        public void AdvanceToAtLeast(int percent)
        {
            _nextPercent = Math.Clamp(Math.Max(_nextPercent, percent), 0, _maxPercent);
        }

        private void ReportNext()
        {
            _lastReportedPercent = _nextPercent;
            ReportProgressStage(_operation, _totalBytes, _nextPercent);
            if (_nextPercent < _maxPercent)
            {
                _nextPercent++;
            }
        }
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
            var response = await _facade.GetItemInfoAsync(providerPath, AuthForProviderPath(providerPath), cancellationToken);
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

    private static void ReportProgressStage(IWfxProgressReporter operation, long? totalBytes, int percent)
    {
        if (percent <= 0)
        {
            operation.Report(0);
            return;
        }

        if (totalBytes is > 0)
        {
            operation.Report(CalculateProgressBytesForPercent(totalBytes.Value, percent));
            return;
        }

        // Unknown total size: reporter in native layer maps positive bytes to visible progress.
        operation.Report(percent);
    }

    private static long MapDownloadReadToDisplayBytes(long bytesTransferred, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return bytesTransferred;
        }

        var clampedBytes = Math.Clamp(bytesTransferred, 0L, totalBytes);
        var startBytes = CalculateProgressBytesForPercent(totalBytes, 5);
        var endBytes = CalculateProgressBytesForPercent(totalBytes, 99);
        var spanBytes = Math.Max(0L, endBytes - startBytes);

        if (spanBytes == 0)
        {
            return startBytes;
        }

        return startBytes + ((clampedBytes * spanBytes) / totalBytes);
    }

    private static long MapUploadReadToDisplayBytes(long bytesTransferred, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return bytesTransferred;
        }

        var clampedBytes = Math.Clamp(bytesTransferred, 0L, totalBytes);
        var endBytes = CalculateProgressBytesForPercent(totalBytes, 90);
        if (endBytes <= 0)
        {
            return 0;
        }

        return (clampedBytes * endBytes) / totalBytes;
    }


    private static int CalculateProgressPercent(long bytesTransferred, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return 0;
        }

        var clampedBytes = Math.Clamp(bytesTransferred, 0L, totalBytes);
        return (int)Math.Clamp((clampedBytes * 100L) / totalBytes, 0L, 100L);
    }

    private static long CalculateProgressBytesForPercent(long totalBytes, int percent)
    {
        if (totalBytes <= 0)
        {
            return 0;
        }

        var clampedPercent = Math.Clamp(percent, 0, 100);
        if (clampedPercent == 0)
        {
            return 0;
        }

        var scaled = (long)Math.Ceiling(totalBytes * (clampedPercent / 100d));
        return Math.Clamp(scaled, 1L, totalBytes);
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

        var response = await _facade.GetItemInfoAsync(providerPath, AuthForProviderPath(providerPath), cancellationToken);
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
