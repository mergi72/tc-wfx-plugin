using TcWfxPlugin.Core;

namespace TcWfxPlugin.Wfx;

public sealed class WfxEntryPoints
{
    private const int CopyFlagOverwrite = 0x01;
    private const int CopyFlagResume = 0x02;
    private const int CopyFlagMove = 0x04;

    private readonly WfxPluginRuntime _runtime;

    public WfxEntryPoints(WfxPluginRuntime runtime)
    {
        _runtime = runtime;
    }

    public int FsFindFirst(string path, out int findHandle, out WfxFindData? firstItem)
    {
        var result = _runtime.FindFirstAsync(path).GetAwaiter().GetResult();
        findHandle = result.Handle;
        firstItem = result.FirstItem;
        return result.ResultCode;
    }

    public int FsFindNext(int findHandle, out WfxFindData? nextItem)
    {
        return _runtime.FindNext(findHandle, out nextItem);
    }

    public int FsFindClose(int findHandle)
    {
        return _runtime.FindClose(findHandle);
    }

    public int FsMkDir(string path, IProgress<WfxTransferProgress>? progress = null)
    {
        return _runtime.MkDirAsync(path, progress).GetAwaiter().GetResult();
    }

    public int FsDeleteFile(string path, IProgress<WfxTransferProgress>? progress = null)
    {
        var result = _runtime.DeleteAsync(path, progress).GetAwaiter().GetResult();
        return NormalizeDeleteResult(path, result);
    }

    public int FsRenMovFile(string oldName, string newName, bool move, IProgress<WfxTransferProgress>? progress = null)
    {
        // TC may issue same-path callbacks during delete workflows, regardless of move flag.
        if (AreSamePath(oldName, newName))
        {
            return FsDeleteFile(oldName, progress);
        }

        if (move)
        {
            string? moveSourceProviderPath = null;
            string? moveDestinationProviderPath = null;
            var sourceIsProviderPath = !LooksLikeWindowsLocalPath(oldName)
                && TotalCommanderPathMapper.TryToProviderPath(oldName, out moveSourceProviderPath);
            var destinationIsProviderPath = !LooksLikeWindowsLocalPath(newName)
                && TotalCommanderPathMapper.TryToProviderPath(newName, out moveDestinationProviderPath);

            // cross-provider dms -> dms move: route through byte-based download/upload
            // progress because TC does not draw FsRenMovFileW server-side progress.
            if (sourceIsProviderPath && destinationIsProviderPath)
            {
                if (!AreSameProvider(moveSourceProviderPath!, moveDestinationProviderPath!))
                {
                    return CopyProviderToProviderViaLocalPipeline(oldName, newName, move: true, progress);
                }

                return _runtime.RenameAsync(oldName, newName, progress).GetAwaiter().GetResult();
            }

            // dms -> fso move: download to local target then delete source on bridge.
            if (sourceIsProviderPath && !destinationIsProviderPath)
            {
                var localTargetPath = ResolveLocalMoveTargetPath(oldName, newName);
                var downloadResult = _runtime.GetFileAsync(oldName, localTargetPath, progress).GetAwaiter().GetResult();
                if (downloadResult != WfxResultCodes.Success)
                {
                    return downloadResult;
                }

                var deleteResult = _runtime.DeleteAsync(oldName, progress).GetAwaiter().GetResult();
                return NormalizeDeleteResult(oldName, deleteResult);
            }

            // fso -> dms move: upload from local source then delete local source.
            if (!sourceIsProviderPath && destinationIsProviderPath)
            {
                var uploadResult = _runtime.PutFileAsync(oldName, newName, overwrite: true, progress).GetAwaiter().GetResult();
                if (uploadResult != WfxResultCodes.Success)
                {
                    return uploadResult;
                }

                // Best-effort cleanup to avoid reporting move failure after successful upload.
                TryDeleteLocalSourcePath(oldName);

                return WfxResultCodes.Success;
            }

            return WfxResultCodes.FileNotFound;
        }

        string? copySourceProviderPath = null;
        string? copyDestinationProviderPath = null;
        var sourceIsDmsPath = !LooksLikeWindowsLocalPath(oldName)
            && TotalCommanderPathMapper.TryToProviderPath(oldName, out copySourceProviderPath);
        var destinationIsDmsPath = !LooksLikeWindowsLocalPath(newName)
            && TotalCommanderPathMapper.TryToProviderPath(newName, out copyDestinationProviderPath);
        if (sourceIsDmsPath && destinationIsDmsPath && !AreSameProvider(copySourceProviderPath!, copyDestinationProviderPath!))
        {
            return CopyProviderToProviderViaLocalPipeline(oldName, newName, move: false, progress);
        }

        return _runtime.CopyAsync(oldName, newName, progress).GetAwaiter().GetResult();
    }

    private int CopyProviderToProviderViaLocalPipeline(
        string sourcePath,
        string destinationPath,
        bool move,
        IProgress<WfxTransferProgress>? progress)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "tc-wfx-plugin", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        var sourceLeafName = TryGetSourceLeafName(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceLeafName))
        {
            sourceLeafName = "transfer.bin";
        }

        var tempPath = Path.Combine(tempDirectory, sourceLeafName);
        try
        {
            var downloadResult = _runtime.GetFileAsync(sourcePath, tempPath, progress).GetAwaiter().GetResult();
            if (downloadResult != WfxResultCodes.Success)
            {
                return downloadResult;
            }

            var uploadResult = _runtime.PutFileAsync(tempPath, destinationPath, overwrite: true, progress).GetAwaiter().GetResult();
            if (uploadResult != WfxResultCodes.Success)
            {
                return uploadResult;
            }

            if (move)
            {
                var deleteResult = _runtime.DeleteAsync(sourcePath, progress).GetAwaiter().GetResult();
                return NormalizeDeleteResult(sourcePath, deleteResult);
            }

            return WfxResultCodes.Success;
        }
        finally
        {
            TryDeleteLocalSourcePath(tempPath);
            TryDeleteEmptyDirectory(tempDirectory);
        }
    }

    private static bool AreSameProvider(string sourceProviderPath, string destinationProviderPath)
    {
        var sourceProvider = sourceProviderPath.Split(':', 2)[0];
        var destinationProvider = destinationProviderPath.Split(':', 2)[0];
        return sourceProvider.Equals(destinationProvider, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool LooksLikeWindowsLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/'))
        {
            return true;
        }

        return path.StartsWith("\\\\", StringComparison.Ordinal);
    }

    private static string ResolveLocalMoveTargetPath(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return destinationPath;
        }

        var destinationLooksLikeDirectory = destinationPath.EndsWith("\\", StringComparison.Ordinal)
            || destinationPath.EndsWith("/", StringComparison.Ordinal)
            || Directory.Exists(destinationPath);

        if (!destinationLooksLikeDirectory)
        {
            return destinationPath;
        }

        var sourceLeafName = TryGetSourceLeafName(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceLeafName))
        {
            return destinationPath;
        }

        return Path.Combine(destinationPath, sourceLeafName);
    }

    private static string TryGetSourceLeafName(string sourcePath)
    {
        if (TotalCommanderPathMapper.TryToProviderPath(sourcePath, out var providerPath)
            && ProviderPath.TryParse(providerPath, out var parsedProviderPath))
        {
            var normalized = parsedProviderPath.Path.TrimEnd('/');
            var slashIndex = normalized.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex < normalized.Length - 1)
            {
                return normalized[(slashIndex + 1)..];
            }

            if (!string.IsNullOrWhiteSpace(normalized) && normalized != "/")
            {
                return normalized.TrimStart('/');
            }
        }

        return Path.GetFileName(sourcePath.TrimEnd('\\', '/'));
    }

    private static void TryDeleteLocalSourcePath(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return;
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private int NormalizeDeleteResult(string path, int result)
    {
        if (result == WfxResultCodes.Success || result == WfxResultCodes.FileNotFound)
        {
            return WfxResultCodes.Success;
        }

        var stillExists = _runtime.PathExistsAsync(path).GetAwaiter().GetResult();
        if (!stillExists)
        {
            return WfxResultCodes.Success;
        }

        // Some providers may report stale existence right after successful delete.
        // Re-check once before surfacing an error to Total Commander.
        Task.Delay(200).GetAwaiter().GetResult();
        stillExists = _runtime.PathExistsAsync(path).GetAwaiter().GetResult();
        return stillExists ? result : WfxResultCodes.Success;
    }

    private static bool AreSamePath(string left, string right)
    {
        var normalizedLeft = NormalizeTcPath(left);
        var normalizedRight = NormalizeTcPath(right);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTcPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Trim()
            .Replace('/', '\\')
            .TrimEnd('\\');

        if (normalized.StartsWith('\\') && !LooksLikeWindowsLocalPath(normalized))
        {
            normalized = normalized.TrimStart('\\');
        }

        return normalized;
    }

    public int FsGetFile(string remoteName, string localName, int copyFlags = 0, IProgress<WfxTransferProgress>? progress = null)
    {
        var options = ParseCopyFlags(copyFlags);
        var effectiveLocalPath = ResolveLocalMoveTargetPath(remoteName, localName);

        // TC may call FsGetFile for plugin->plugin copy. Besides classic "\\provider\\..."
        // paths, TC can also pass a provider path without a leading slash.
        if (TotalCommanderPathMapper.TryToProviderPath(remoteName, out var sourceProviderPath)
            && TotalCommanderPathMapper.TryToProviderPath(localName, out var destinationProviderPath)
            && sourceProviderPath.Split(':', 2)[0].Equals(destinationProviderPath.Split(':', 2)[0], StringComparison.OrdinalIgnoreCase))
        {
            var copyResult = _runtime.CopyAsync(remoteName, localName).GetAwaiter().GetResult();
            if (copyResult != WfxResultCodes.Success)
            {
                return copyResult;
            }

            if (options.Move)
            {
                return FsDeleteFile(remoteName);
            }

            return WfxResultCodes.Success;
        }

        if (!options.Overwrite && File.Exists(effectiveLocalPath))
        {
            return WfxResultCodes.WriteError;
        }

        var downloadResult = _runtime.GetFileAsync(remoteName, effectiveLocalPath, progress).GetAwaiter().GetResult();
        if (downloadResult != WfxResultCodes.Success)
        {
            return downloadResult;
        }

        if (options.Move)
        {
            return FsDeleteFile(remoteName);
        }

        return WfxResultCodes.Success;
    }

    public int FsPutFile(string localName, string remoteName, int copyFlags, IProgress<WfxTransferProgress>? progress = null)
    {
        var options = ParseCopyFlags(copyFlags);
        var uploadResult = FsPutFile(localName, remoteName, options.Overwrite, progress);
        if (uploadResult != WfxResultCodes.Success)
        {
            return uploadResult;
        }

        if (options.Move)
        {
            TryDeleteLocalSourcePath(localName);
        }

        return WfxResultCodes.Success;
    }

    public int FsPutFile(string localName, string remoteName, bool overwrite, IProgress<WfxTransferProgress>? progress = null)
    {
        return _runtime.PutFileAsync(localName, remoteName, overwrite, progress).GetAwaiter().GetResult();
    }

    public void InvalidateProvidersCache()
    {
        _runtime.InvalidateRootProvidersCache();
    }

    public void OnReconnect()
    {
        _runtime.OnReconnect();
    }

    public void CancelCurrentTransfer()
    {
        _runtime.CancelCurrentTransfer();
    }

    public bool FsPathExists(string path)
    {
        return _runtime.PathExistsAsync(path).GetAwaiter().GetResult();
    }

    private static (bool Move, bool Overwrite, bool Resume) ParseCopyFlags(int copyFlags)
    {
        var move = (copyFlags & CopyFlagMove) != 0;
        var overwrite = (copyFlags & CopyFlagOverwrite) != 0;
        var resume = (copyFlags & CopyFlagResume) != 0;
        return (move, overwrite, resume);
    }
}
