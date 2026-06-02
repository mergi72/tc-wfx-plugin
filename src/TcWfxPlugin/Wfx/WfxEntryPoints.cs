namespace TcWfxPlugin.Wfx;

public sealed class WfxEntryPoints
{
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
        return _runtime.DeleteAsync(path).GetAwaiter().GetResult();
    }

    public int FsRenMovFile(string oldName, string newName, bool move)
    {
        if (move)
        {
            return _runtime.RenameAsync(oldName, newName).GetAwaiter().GetResult();
        }

        return _runtime.CopyAsync(oldName, newName).GetAwaiter().GetResult();
    }

    public int FsGetFile(string remoteName, string localName)
    {
        return _runtime.GetFileAsync(remoteName, localName).GetAwaiter().GetResult();
    }

    public int FsPutFile(string localName, string remoteName, bool overwrite)
    {
        return _runtime.PutFileAsync(localName, remoteName, overwrite).GetAwaiter().GetResult();
    }
}
