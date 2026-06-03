using TcWfxPlugin.Core;

namespace TcWfxPlugin.Wfx;

public sealed class WfxEntryPoints
{
    private const int CopyFlagOverwrite = 0x02;
    private const int CopyFlagResume = 0x04;

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

    public int FsMkDir(string path)
    {
        return _runtime.MkDirAsync(path).GetAwaiter().GetResult();
    }

    public int FsDeleteFile(string path)
    {
        var result = _runtime.DeleteAsync(path).GetAwaiter().GetResult();
        return NormalizeDeleteResult(path, result);
    }

    public int FsRenMovFile(string oldName, string newName, bool move)
    {
        // TC may issue same-path callbacks during delete workflows, regardless of move flag.
        if (AreSamePath(oldName, newName))
        {
            return FsDeleteFile(oldName);
        }

        if (move)
        {
            var sourceIsProviderPath = !LooksLikeWindowsLocalPath(oldName)
                && TotalCommanderPathMapper.TryToProviderPath(oldName, out _);
            var destinationIsProviderPath = !LooksLikeWindowsLocalPath(newName)
                && TotalCommanderPathMapper.TryToProviderPath(newName, out _);

            // dms -> dms move: delegate to bridge move endpoint.
            if (sourceIsProviderPath && destinationIsProviderPath)
            {
                return _runtime.RenameAsync(oldName, newName).GetAwaiter().GetResult();
            }

            // dms -> fso move: download to local target then delete source on bridge.
            if (sourceIsProviderPath && !destinationIsProviderPath)
            {
                var localTargetPath = ResolveLocalMoveTargetPath(oldName, newName);
                var downloadResult = _runtime.GetFileAsync(oldName, localTargetPath).GetAwaiter().GetResult();
                if (downloadResult != WfxResultCodes.Success)
                {
                    return downloadResult;
                }

                var deleteResult = _runtime.DeleteAsync(oldName).GetAwaiter().GetResult();
                return NormalizeDeleteResult(oldName, deleteResult);
            }

            // fso -> dms move: upload from local source then delete local source.
            if (!sourceIsProviderPath && destinationIsProviderPath)
            {
                var uploadResult = _runtime.PutFileAsync(oldName, newName, overwrite: true).GetAwaiter().GetResult();
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

        return _runtime.CopyAsync(oldName, newName).GetAwaiter().GetResult();
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

    public int FsGetFile(string remoteName, string localName, int copyFlags = 0)
    {
        var options = ParseCopyFlags(copyFlags);
        if (options.Resume)
        {
            return WfxResultCodes.NotSupported;
        }

        // TC may call FsGetFile for plugin->plugin copy. Besides classic "\\provider\\..."
        // paths, TC can also pass a provider path without a leading slash.
        if (TotalCommanderPathMapper.TryToProviderPath(remoteName, out var sourceProviderPath)
            && TotalCommanderPathMapper.TryToProviderPath(localName, out var destinationProviderPath)
            && sourceProviderPath.Split(':', 2)[0].Equals(destinationProviderPath.Split(':', 2)[0], StringComparison.OrdinalIgnoreCase))
        {
            return _runtime.CopyAsync(remoteName, localName).GetAwaiter().GetResult();
        }

        if (!options.Overwrite && File.Exists(localName))
        {
            return WfxResultCodes.WriteError;
        }

        return _runtime.GetFileAsync(remoteName, localName).GetAwaiter().GetResult();
    }

    public int FsPutFile(string localName, string remoteName, int copyFlags)
    {
        var options = ParseCopyFlags(copyFlags);
        if (options.Resume)
        {
            return WfxResultCodes.NotSupported;
        }

        return FsPutFile(localName, remoteName, options.Overwrite);
    }

    public int FsPutFile(string localName, string remoteName, bool overwrite)
    {
        return _runtime.PutFileAsync(localName, remoteName, overwrite).GetAwaiter().GetResult();
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

    private static (bool Overwrite, bool Resume) ParseCopyFlags(int copyFlags)
    {
        var overwrite = (copyFlags & CopyFlagOverwrite) != 0;
        var resume = (copyFlags & CopyFlagResume) != 0;
        return (overwrite, resume);
    }
}
