using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace TcWfxPlugin.Wfx;

public static class WfxNativeExports
{
    private const int CopyFlagOverwrite = 0x02;
    public const int RequestTypeUserName = 3;
    public const int RequestTypePassword = 4;
    private const int RequestTypeMsgYesNo = 9;
    private const int RequestBufferLength = 512;

    private static readonly Lazy<WfxEntryPoints> EntryPoints = new(CreateEntryPoints);
    private static readonly object CallbackSyncRoot = new();
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
        _ = logProc;
        EntryPoints.Value.OnReconnect();
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
        return EntryPoints.Value.FsMkDir(path);
    }

    [UnmanagedCallersOnly(EntryPoint = "FsDeleteFileW")]
    public static int FsDeleteFileW(nint pathPtr)
    {
        var path = Marshal.PtrToStringUni(pathPtr) ?? string.Empty;
        return EntryPoints.Value.FsDeleteFile(path);
    }

    [UnmanagedCallersOnly(EntryPoint = "FsRenMovFileW")]
    public static int FsRenMovFileW(nint oldNamePtr, nint newNamePtr, int move, int overwrite, nint remoteInfo)
    {
        _ = overwrite;
        _ = remoteInfo;

        var oldName = Marshal.PtrToStringUni(oldNamePtr) ?? string.Empty;
        var newName = Marshal.PtrToStringUni(newNamePtr) ?? string.Empty;
        return EntryPoints.Value.FsRenMovFile(oldName, newName, move != 0);
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
            else
            {
                return WfxResultCodes.WriteError;
            }
        }

        return EntryPoints.Value.FsGetFile(remoteName, localName, effectiveCopyFlags);
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
                else
                {
                    return WfxResultCodes.WriteError;
                }
            }

            var fileName = Path.GetFileName(localName);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var remoteFilePath = CombinePath(remoteName, fileName);
                if ((effectiveCopyFlags & CopyFlagOverwrite) == 0 && EntryPoints.Value.FsPathExists(remoteFilePath))
                {
                    if (TryConfirmOverwrite(remoteFilePath))
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

        return EntryPoints.Value.FsPutFile(localName, remoteName, effectiveCopyFlags);
    }

    private static void WriteFindData(nint destination, WfxFindData item)
    {
        var findData = new Win32FindDataW
        {
            DwFileAttributes = item.IsDirectory ? 0x10u : 0x80u,
            FtCreationTime = default,
            FtLastAccessTime = default,
            FtLastWriteTime = default,
            NFileSizeHigh = (uint)(item.Size >> 32),
            NFileSizeLow = (uint)(item.Size & 0xFFFFFFFF),
            DwReserved0 = 0,
            DwReserved1 = 0,
            CFileName = item.FileName,
            CAlternateFileName = string.Empty,
        };

        Marshal.StructureToPtr(findData, destination, fDeleteOld: false);
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
        runtime.TransferProgressChanged += OnTransferProgressChanged;
        return new WfxEntryPoints(runtime);
    }

    private static void OnTransferProgressChanged(WfxTransferProgress progress)
    {
        ProgressProcDelegate? progressProc;
        lock (CallbackSyncRoot)
        {
            progressProc = _progressProc;
        }

        if (progressProc is null)
        {
            return;
        }

        var percentDone = 0;
        if (progress.TotalBytes is long total && total > 0)
        {
            var rawPercent = (progress.BytesTransferred * 100L) / total;
            percentDone = (int)Math.Clamp(rawPercent, 0, 100);
        }

        nint sourcePtr = nint.Zero;
        nint destinationPtr = nint.Zero;
        try
        {
            sourcePtr = Marshal.StringToHGlobalUni(progress.SourcePath);
            destinationPtr = Marshal.StringToHGlobalUni(progress.DestinationPath);

            var callbackResult = progressProc(_pluginNumber, sourcePtr, destinationPtr, percentDone);
            if (callbackResult != 0)
            {
                EntryPoints.Value.CancelCurrentTransfer();
            }
        }
        finally
        {
            if (sourcePtr != nint.Zero)
            {
                Marshal.FreeHGlobal(sourcePtr);
            }

            if (destinationPtr != nint.Zero)
            {
                Marshal.FreeHGlobal(destinationPtr);
            }
        }
    }

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

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int ProgressProcDelegate(int pluginNr, nint sourceName, nint targetName, int percentDone);

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
