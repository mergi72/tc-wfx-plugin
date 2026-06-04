using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;

namespace TcWfxPlugin.Wfx;

public static class WfxNativeExports
{
    private const int CopyFlagOverwrite = 0x01;
    private const uint FileAttributeReadOnly = 0x01;
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeArchive = 0x20;
    private const uint FileAttributeNormal = 0x80;
    public const int RequestTypeUserName = 3;
    public const int RequestTypePassword = 4;
    private const int RequestTypeMsgYesNo = 9;
    private const int RequestBufferLength = 512;
    private const int BoolSuccess = 1;
    private const int BoolFailure = 0;

    private static readonly Lazy<WfxEntryPoints> EntryPoints = new(CreateEntryPoints);
    private static readonly object CallbackSyncRoot = new();
    private static readonly object DiagnosticLogSyncRoot = new();
    private const string DiagnosticLogDirectory = "c:\\temp";
    private const string InitLogPath = "c:\\temp\\wfx-init.log";
    private const string DefaultParamsLogPath = "c:\\temp\\wfx-default-params.log";
    private const string ProgressLogPath = "c:\\temp\\wfx-progress.log";
    private const string StatusLogPath = "c:\\temp\\wfx-status.log";
    private const string ProgressSelfTestEnvVar = "TC_WFX_PROGRESS_SELFTEST";
    private static FsDefaultParamStruct? _defaultParams;
    private static int _pluginNumber;
    private static ProgressProcDelegate? _progressProc;
    private static RequestProcDelegate? _requestProc;

    [UnmanagedCallersOnly(EntryPoint = "FsInitW")]
    public static int FsInitW(int pluginNr, nint progressProc, nint logProc, nint requestProc)
    {
        lock (CallbackSyncRoot)
        {
            _pluginNumber = pluginNr;
            _progressProc = progressProc == nint.Zero
                ? null
                : Marshal.GetDelegateForFunctionPointer<ProgressProcDelegate>(progressProc);
            _requestProc = requestProc == nint.Zero
                ? null
                : Marshal.GetDelegateForFunctionPointer<RequestProcDelegate>(requestProc);
        }

        AppendDiagnosticLog(
            InitLogPath,
            $"{DateTime.Now:HH:mm:ss.fff} FsInitW pluginNr={pluginNr}, progressProc=0x{progressProc.ToInt64():X}, requestProc=0x{requestProc.ToInt64():X}");

        _ = logProc;
        EntryPoints.Value.OnReconnect();
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "FsSetDefaultParams")]
    public static void FsSetDefaultParams(nint defaultParamsPtr)
    {
        if (defaultParamsPtr == nint.Zero)
        {
            return;
        }

        var defaultParams = Marshal.PtrToStructure<FsDefaultParamStruct>(defaultParamsPtr);
        lock (CallbackSyncRoot)
        {
            _defaultParams = defaultParams;
        }

        AppendDiagnosticLog(
            DefaultParamsLogPath,
            $"{DateTime.Now:HH:mm:ss.fff} FsSetDefaultParams size={defaultParams.Size}, interfaceLow={defaultParams.PluginInterfaceVersionLow}, interfaceHi={defaultParams.PluginInterfaceVersionHi}, defaultIniName={defaultParams.DefaultIniName}");
    }

    [UnmanagedCallersOnly(EntryPoint = "FsStatusInfoW")]
    public static void FsStatusInfoW(nint remoteDirPtr, int infoStartEnd, int infoOperation)
    {
        var remoteDir = Marshal.PtrToStringUni(remoteDirPtr) ?? string.Empty;
        AppendDiagnosticLog(
            StatusLogPath,
            $"{DateTime.Now:HH:mm:ss.fff} FsStatusInfoW startEnd={infoStartEnd} operation={infoOperation} remoteDir={remoteDir}");
    }

    [UnmanagedCallersOnly(EntryPoint = "FsStatusInfo")]
    public static void FsStatusInfo(nint remoteDirPtr, int infoStartEnd, int infoOperation)
    {
        var remoteDir = Marshal.PtrToStringAnsi(remoteDirPtr) ?? string.Empty;
        AppendDiagnosticLog(
            StatusLogPath,
            $"{DateTime.Now:HH:mm:ss.fff} FsStatusInfo startEnd={infoStartEnd} operation={infoOperation} remoteDir={remoteDir}");
    }

    [UnmanagedCallersOnly(EntryPoint = "FsGetBackgroundFlags")]
    public static int FsGetBackgroundFlags()
    {
        // Explicitly report foreground-only behavior to TC.
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "FsFindFirstW")]
    public static nint FsFindFirstW(nint pathPtr, nint findDataPtr)
    {
        var path = Marshal.PtrToStringUni(pathPtr) ?? string.Empty;
        var result = EntryPoints.Value.FsFindFirst(path, out var handle, out var firstItem);
        if (result != WfxResultCodes.Success || firstItem is null)
        {
            WriteEmptyFindData(findDataPtr);
            return nint.Zero;
        }

        WriteFindData(findDataPtr, firstItem);
        return (nint)handle;
    }

    [UnmanagedCallersOnly(EntryPoint = "FsFindNextW")]
    public static int FsFindNextW(nint handle, nint findDataPtr)
    {
        var result = EntryPoints.Value.FsFindNext((int)handle, out var item);
        if (result != WfxResultCodes.Success || item is null)
        {
            WriteEmptyFindData(findDataPtr);
            return 0;
        }

        WriteFindData(findDataPtr, item);
        return 1;
    }

    [UnmanagedCallersOnly(EntryPoint = "FsFindClose")]
    public static int FsFindClose(nint handle)
    {
        return EntryPoints.Value.FsFindClose((int)handle) == WfxResultCodes.Success ? 0 : -1;
    }

    [UnmanagedCallersOnly(EntryPoint = "FsMkDirW")]
    public static int FsMkDirW(nint pathPtr)
    {
        var path = Marshal.PtrToStringUni(pathPtr) ?? string.Empty;
        var progress = CreateDirectProgressReporter("mkdir", path, path);
        var result = EntryPoints.Value.FsMkDir(path, progress);
        if (result == WfxResultCodes.Success)
        {
            NotifyTotalCommanderPathChanged(path);
            return BoolSuccess;
        }

        return BoolFailure;
    }

    [UnmanagedCallersOnly(EntryPoint = "FsDeleteFileW")]
    public static int FsDeleteFileW(nint pathPtr)
    {
        var path = Marshal.PtrToStringUni(pathPtr) ?? string.Empty;
        var progress = CreateDirectProgressReporter("delete", path, path);
        var result = EntryPoints.Value.FsDeleteFile(path, progress);
        if (result == WfxResultCodes.Success)
        {
            NotifyTotalCommanderPathChanged(path);
            return BoolSuccess;
        }

        return BoolFailure;
    }

    [UnmanagedCallersOnly(EntryPoint = "FsRemoveDirW")]
    public static int FsRemoveDirW(nint pathPtr)
    {
        var path = Marshal.PtrToStringUni(pathPtr) ?? string.Empty;
        var progress = CreateDirectProgressReporter("delete", path, path);
        var result = EntryPoints.Value.FsDeleteFile(path, progress);
        if (result == WfxResultCodes.Success)
        {
            NotifyTotalCommanderPathChanged(path);
            return BoolSuccess;
        }

        return BoolFailure;
    }

    [UnmanagedCallersOnly(EntryPoint = "FsRenMovFileW")]
    public static int FsRenMovFileW(nint oldNamePtr, nint newNamePtr, int move, int overwrite, nint remoteInfo)
    {
        _ = overwrite;
        _ = remoteInfo;

        var oldName = Marshal.PtrToStringUni(oldNamePtr) ?? string.Empty;
        var newName = Marshal.PtrToStringUni(newNamePtr) ?? string.Empty;
        var progress = CreateDirectProgressReporter(move != 0 ? "move" : "copy", oldName, newName);
        var result = EntryPoints.Value.FsRenMovFile(oldName, newName, move != 0, progress);
        return result;
    }

    [UnmanagedCallersOnly(EntryPoint = "FsGetFileW")]
    public static int FsGetFileW(nint remoteNamePtr, nint localNamePtr, int copyFlags, nint remoteInfo)
    {
        _ = remoteInfo;

        var remoteName = Marshal.PtrToStringUni(remoteNamePtr) ?? string.Empty;
        var localName = Marshal.PtrToStringUni(localNamePtr) ?? string.Empty;

        var effectiveCopyFlags = copyFlags;
        if ((effectiveCopyFlags & CopyFlagOverwrite) == 0 && File.Exists(localName))
        {
            if (TryConfirmOverwrite(localName))
            {
                effectiveCopyFlags |= CopyFlagOverwrite;
            }
            else if (_requestProc is null)
            {
                // TC may call download/copy without request callback; prefer overwrite over hard failure.
                effectiveCopyFlags |= CopyFlagOverwrite;
            }
            else
            {
                return WfxResultCodes.WriteError;
            }
        }

        var directProgress = CreateDirectProgressReporter("download", remoteName, localName);
        directProgress?.Report(new WfxTransferProgress { Operation = "download", SourcePath = remoteName, DestinationPath = localName, BytesTransferred = 0, TotalBytes = null });
        var result = EntryPoints.Value.FsGetFile(remoteName, localName, effectiveCopyFlags, directProgress);
        return result;
    }

    [UnmanagedCallersOnly(EntryPoint = "FsPutFileW")]
    public static int FsPutFileW(nint localNamePtr, nint remoteNamePtr, int copyFlags)
    {
        var localName = Marshal.PtrToStringUni(localNamePtr) ?? string.Empty;
        var remoteName = Marshal.PtrToStringUni(remoteNamePtr) ?? string.Empty;

        var effectiveCopyFlags = copyFlags;
        if ((effectiveCopyFlags & CopyFlagOverwrite) == 0)
        {
            if (EntryPoints.Value.FsPathExists(remoteName))
            {
                if (TryConfirmOverwrite(remoteName))
                {
                    effectiveCopyFlags |= CopyFlagOverwrite;
                }
                else if (_requestProc is null)
                {
                    // TC sometimes calls save/upload without a request callback; in that case
                    // we prefer to proceed with overwrite rather than fail the editor save.
                    effectiveCopyFlags |= CopyFlagOverwrite;
                }
                else
                {
                    return WfxResultCodes.WriteError;
                }
            }

            var fileName = Path.GetFileName(localName);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                // If remoteName already points to the target file, avoid appending the same
                // file name again (which would produce "...file.ext/file.ext").
                if (!PathEndsWithLeafName(remoteName, fileName))
                {
                    var remoteFilePath = CombinePath(remoteName, fileName);
                    if ((effectiveCopyFlags & CopyFlagOverwrite) == 0 && EntryPoints.Value.FsPathExists(remoteFilePath))
                    {
                        if (TryConfirmOverwrite(remoteFilePath))
                        {
                            effectiveCopyFlags |= CopyFlagOverwrite;
                        }
                        else if (_requestProc is null)
                        {
                            effectiveCopyFlags |= CopyFlagOverwrite;
                        }
                        else
                        {
                            return WfxResultCodes.WriteError;
                        }
                    }
                }
            }
        }

        var selfTestResult = TryRunProgressSelfTest(localName, remoteName);
        if (selfTestResult.HasValue)
        {
            return selfTestResult.Value;
        }

        var directProgress = CreateDirectProgressReporter("upload", localName, remoteName);
        directProgress?.Report(new WfxTransferProgress { Operation = "upload", SourcePath = localName, DestinationPath = remoteName, BytesTransferred = 0, TotalBytes = null });
        var result = EntryPoints.Value.FsPutFile(localName, remoteName, effectiveCopyFlags, directProgress);
        return result;
    }

    private static void WriteFindData(nint destination, WfxFindData item)
    {
        var attributes = item.IsDirectory ? FileAttributeDirectory : FileAttributeArchive;
        if (item.IsReadOnly)
        {
            attributes |= FileAttributeReadOnly;
        }

        var findData = new Win32FindDataW
        {
            DwFileAttributes = attributes,
            FtCreationTime = default,
            FtLastAccessTime = default,
            FtLastWriteTime = item.LastWriteTimeUtc.HasValue
                ? DateTimeToFileTime(item.LastWriteTimeUtc.Value.UtcDateTime)
                : default,
            NFileSizeHigh = (uint)(item.Size >> 32),
            NFileSizeLow = (uint)(item.Size & 0xFFFFFFFF),
            DwReserved0 = 0,
            DwReserved1 = 0,
            CFileName = item.FileName,
            CAlternateFileName = string.Empty,
        };

        Marshal.StructureToPtr(findData, destination, fDeleteOld: false);
    }

    private static FILETIME DateTimeToFileTime(DateTime utcDateTime)
    {
        var fileTime = utcDateTime.ToFileTimeUtc();
        return new FILETIME
        {
            dwLowDateTime = (int)(fileTime & 0xFFFFFFFF),
            dwHighDateTime = (int)(fileTime >> 32),
        };
    }

    private static void WriteEmptyFindData(nint destination)
    {
        if (destination == nint.Zero)
        {
            return;
        }

        var findData = new Win32FindDataW
        {
            DwFileAttributes = 0,
            FtCreationTime = default,
            FtLastAccessTime = default,
            FtLastWriteTime = default,
            NFileSizeHigh = 0,
            NFileSizeLow = 0,
            DwReserved0 = 0,
            DwReserved1 = 0,
            CFileName = string.Empty,
            CAlternateFileName = string.Empty,
        };

        Marshal.StructureToPtr(findData, destination, fDeleteOld: false);
    }

    private static WfxEntryPoints CreateEntryPoints()
    {
        var baseUrl = Environment.GetEnvironmentVariable("TC_WFX_BRIDGE_URL") ?? "http://127.0.0.1:8765/";
        var authProvider = new TcDialogAuthProvider(
            TryRequestValue,
            TryConfirmYesNo,
            new WindowsCredentialStore(),
            "tc-wfx/bridge");
        var client = new WfxBridgeClient(baseUrl);
        var facade = new WfxPluginFacade(client);
        var runtime = new WfxPluginRuntime(facade, authProvider);
        return new WfxEntryPoints(runtime);
    }

    private static IProgress<WfxTransferProgress>? CreateDirectProgressReporter(string operation, string sourcePath, string destinationPath)
    {
        ProgressProcDelegate? progressProc;
        int pluginNumber;
        lock (CallbackSyncRoot)
        {
            progressProc = _progressProc;
            pluginNumber = _pluginNumber;
        }

        if (progressProc is null)
        {
            AppendDiagnosticLog(
                ProgressLogPath,
                $"{DateTime.Now:HH:mm:ss.fff} {operation} progressProc=null source={sourcePath} target={destinationPath}");
            return null;
        }

        return new DirectTcProgressReporter(operation, sourcePath, destinationPath, pluginNumber, progressProc);
    }

    private static void NotifyTotalCommanderPathChanged(string path)
    {
        ProgressProcDelegate? progressProc;
        int pluginNumber;

        lock (CallbackSyncRoot)
        {
            progressProc = _progressProc;
            pluginNumber = _pluginNumber;
        }

        if (progressProc is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            _ = progressProc(pluginNumber, path, path, 100);
        }
        catch
        {
            // Best-effort refresh hint for TC UI; operation result is already resolved.
        }
    }

    private static int? TryRunProgressSelfTest(string sourcePath, string destinationPath)
    {
        var selfTestEnabled = string.Equals(
            Environment.GetEnvironmentVariable(ProgressSelfTestEnvVar),
            "1",
            StringComparison.OrdinalIgnoreCase);
        if (!selfTestEnabled)
        {
            return null;
        }

        ProgressProcDelegate? progressProc;
        int pluginNumber;
        lock (CallbackSyncRoot)
        {
            progressProc = _progressProc;
            pluginNumber = _pluginNumber;
        }

        if (progressProc is null)
        {
            return null;
        }

        // Use hardcoded non-empty names so TC dialog shows something identifiable.
        const string selfTestSrc = "selftest-src.bin";
        const string selfTestDst = "selftest-dst.bin";

        var steps = new[] { (0, 1000), (25, 1000), (50, 5000), (75, 1000), (100, 1000) };
        foreach (var (percent, delayMs) in steps)
        {
            var callbackResult = progressProc(pluginNumber, selfTestSrc, selfTestDst, percent);
            AppendDiagnosticLog(
                ProgressLogPath,
                $"{DateTime.Now:HH:mm:ss.fff} selftest percent={percent} callback=result={callbackResult}");

            if (callbackResult != 0)
            {
                return WfxResultCodes.UserAbort;
            }

            Thread.Sleep(delayMs);
        }

        return null;
    }

    // Removed TraceCallback method and its usage

    private static bool TryConfirmOverwrite(string localPath)
    {
        RequestProcDelegate? requestProc;
        int pluginNumber;

        lock (CallbackSyncRoot)
        {
            requestProc = _requestProc;
            pluginNumber = _pluginNumber;
        }

        if (requestProc is null)
        {
            return false;
        }

        nint titlePtr = nint.Zero;
        nint textPtr = nint.Zero;
        try
        {
            titlePtr = Marshal.StringToHGlobalUni("Overwrite existing file");
            textPtr = Marshal.StringToHGlobalUni($"File already exists:\n{localPath}\n\nOverwrite it?");
            var result = requestProc(pluginNumber, RequestTypeMsgYesNo, titlePtr, textPtr, nint.Zero, 0);
            return result != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (titlePtr != nint.Zero)
            {
                Marshal.FreeHGlobal(titlePtr);
            }

            if (textPtr != nint.Zero)
            {
                Marshal.FreeHGlobal(textPtr);
            }
        }
    }

    private static bool TryConfirmYesNo(string title, string text)
    {
        RequestProcDelegate? requestProc;
        int pluginNumber;

        lock (CallbackSyncRoot)
        {
            requestProc = _requestProc;
            pluginNumber = _pluginNumber;
        }

        if (requestProc is null)
        {
            return false;
        }

        nint titlePtr = nint.Zero;
        nint textPtr = nint.Zero;
        try
        {
            titlePtr = Marshal.StringToHGlobalUni(title);
            textPtr = Marshal.StringToHGlobalUni(text);
            var result = requestProc(pluginNumber, RequestTypeMsgYesNo, titlePtr, textPtr, nint.Zero, 0);
            return result != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (titlePtr != nint.Zero)
            {
                Marshal.FreeHGlobal(titlePtr);
            }

            if (textPtr != nint.Zero)
            {
                Marshal.FreeHGlobal(textPtr);
            }
        }
    }

    private static string? TryRequestValue(int requestType, string title, string text)
    {
        RequestProcDelegate? requestProc;
        int pluginNumber;

        lock (CallbackSyncRoot)
        {
            requestProc = _requestProc;
            pluginNumber = _pluginNumber;
        }

        if (requestProc is null)
        {
            return null;
        }

        nint titlePtr = nint.Zero;
        nint textPtr = nint.Zero;
        nint resultBufferPtr = nint.Zero;
        try
        {
            titlePtr = Marshal.StringToHGlobalUni(title);
            textPtr = Marshal.StringToHGlobalUni(text);
            resultBufferPtr = Marshal.AllocHGlobal(sizeof(char) * RequestBufferLength);

            for (var i = 0; i < RequestBufferLength; i++)
            {
                Marshal.WriteInt16(resultBufferPtr, i * sizeof(char), 0);
            }

            var result = requestProc(pluginNumber, requestType, titlePtr, textPtr, resultBufferPtr, RequestBufferLength);
            if (result == 0)
            {
                return null;
            }

            var value = Marshal.PtrToStringUni(resultBufferPtr);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (titlePtr != nint.Zero)
            {
                Marshal.FreeHGlobal(titlePtr);
            }

            if (textPtr != nint.Zero)
            {
                Marshal.FreeHGlobal(textPtr);
            }

            if (resultBufferPtr != nint.Zero)
            {
                Marshal.FreeHGlobal(resultBufferPtr);
            }
        }
    }

    private static string CombinePath(string directoryPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return fileName;
        }

        var trimmed = directoryPath.TrimEnd('\\', '/');
        return $"{trimmed}\\{fileName}";
    }

    private static bool PathEndsWithLeafName(string path, string leafName)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(leafName))
        {
            return false;
        }

        var normalized = path.Trim().TrimEnd('\\', '/');
        if (normalized.Length == 0)
        {
            return false;
        }

        var slashIndex = Math.Max(normalized.LastIndexOf('\\'), normalized.LastIndexOf('/'));
        var currentLeaf = slashIndex >= 0 ? normalized[(slashIndex + 1)..] : normalized;
        return string.Equals(currentLeaf, leafName, StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendDiagnosticLog(string filePath, string line)
    {
        try
        {
            lock (DiagnosticLogSyncRoot)
            {
                Directory.CreateDirectory(DiagnosticLogDirectory);
                File.AppendAllText(filePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must not affect plugin behavior.
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct FsDefaultParamStruct
    {
        public int Size;
        public int PluginInterfaceVersionLow;
        public int PluginInterfaceVersionHi;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DefaultIniName;
    }

    private sealed class DirectTcProgressReporter : IProgress<WfxTransferProgress>
    {
        private readonly string _operation;
        private readonly string _sourcePath;
        private readonly string _destinationPath;
        private readonly int _pluginNumber;
        private readonly ProgressProcDelegate _progressProc;
        private int _lastPercent = -1;

        public DirectTcProgressReporter(
            string operation,
            string sourcePath,
            string destinationPath,
            int pluginNumber,
            ProgressProcDelegate progressProc)
        {
            _operation = operation;
            _sourcePath = sourcePath;
            _destinationPath = destinationPath;
            _pluginNumber = pluginNumber;
            _progressProc = progressProc;
        }

        public void Report(WfxTransferProgress value)
        {
            var totalBytes = value.TotalBytes.GetValueOrDefault();
            var percent = totalBytes > 0
                ? (int)Math.Clamp((value.BytesTransferred * 100L) / totalBytes, 0, 100)
                : 0;

            if (_lastPercent < 0)
            {
                SendPercent(0, value.BytesTransferred, value.TotalBytes);
                _lastPercent = 0;
            }

            if (percent < _lastPercent)
            {
                percent = _lastPercent;
            }

            if (percent == _lastPercent)
            {
                AppendDiagnosticLog(
                    ProgressLogPath,
                    $"{DateTime.Now:HH:mm:ss.fff} {_operation} {value.BytesTransferred}/{value.TotalBytes} percent={percent} callback=skipped-same-percent");
                return;
            }

            SendPercent(percent, value.BytesTransferred, value.TotalBytes);
            _lastPercent = percent;
        }

        private void SendPercent(int percent, long bytesTransferred, long? totalBytes)
        {
            AppendDiagnosticLog(
                ProgressLogPath,
                $"{DateTime.Now:HH:mm:ss.fff} {_operation} {bytesTransferred}/{totalBytes} percent={percent} callback=before");

            var callbackResult = _progressProc(_pluginNumber, _sourcePath, _destinationPath, percent);

            AppendDiagnosticLog(
                ProgressLogPath,
                $"{DateTime.Now:HH:mm:ss.fff} {_operation} {bytesTransferred}/{totalBytes} percent={percent} callback=result={callbackResult}");

            if (callbackResult != 0)
            {
                EntryPoints.Value.CancelCurrentTransfer();
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int ProgressProcDelegate(
        int pluginNr,
        [MarshalAs(UnmanagedType.LPWStr)] string sourceName,
        [MarshalAs(UnmanagedType.LPWStr)] string targetName,
        int percentDone);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int RequestProcDelegate(int pluginNr, int requestType, nint customTitle, nint customText, nint returnedText, int maxLen);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindDataW
    {
        public uint DwFileAttributes;
        public FILETIME FtCreationTime;
        public FILETIME FtLastAccessTime;
        public FILETIME FtLastWriteTime;
        public uint NFileSizeHigh;
        public uint NFileSizeLow;
        public uint DwReserved0;
        public uint DwReserved1;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string CFileName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string CAlternateFileName;
    }
}
