namespace TcWfxPlugin.Wfx;

public sealed class WfxPluginRuntime
{
    private readonly WfxListingService _listingService;
    private readonly WfxTransferService _transferService;
    private readonly WfxContextManager _contextManager;

    public WfxPluginRuntime(WfxPluginFacade facade, IWfxAuthProvider authProvider, Func<DateTime>? utcNow = null)
    {
        var nowProvider = utcNow ?? (() => DateTime.UtcNow);
        _listingService = new WfxListingService(facade, authProvider, nowProvider);
        _transferService = new WfxTransferService(facade, authProvider);
        _contextManager = new WfxContextManager(nowProvider);
    }

    public void InvalidateRootProvidersCache()
    {
        _listingService.InvalidateRootProvidersCache();
    }

    public async Task<(int ResultCode, int Handle, WfxFindData? FirstItem)> FindFirstAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        var (resultCode, items) = await _listingService.ResolveItemsAsync(totalCommanderPath, cancellationToken);
        if (resultCode != WfxResultCodes.Success && resultCode != WfxResultCodes.NoMoreFiles)
        {
            return (resultCode, 0, null);
        }

        var (handle, first) = _contextManager.Register(items);
        return (first is null ? WfxResultCodes.NoMoreFiles : WfxResultCodes.Success, handle, first);
    }

    public int FindNext(int handle, out WfxFindData? item)
    {
        return _contextManager.FindNext(handle, out item);
    }

    public int FindClose(int handle)
    {
        return _contextManager.FindClose(handle);
    }

    public async Task<int> MkDirAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        return await _transferService.MkDirAsync(totalCommanderPath, cancellationToken);
    }

    public async Task<int> DeleteAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        return await _transferService.DeleteAsync(totalCommanderPath, cancellationToken);
    }

    public async Task<int> RenameAsync(string totalCommanderSourcePath, string totalCommanderDestinationPath, CancellationToken cancellationToken = default)
    {
        return await _transferService.RenameAsync(totalCommanderSourcePath, totalCommanderDestinationPath, cancellationToken);
    }

    public async Task<int> CopyAsync(string totalCommanderSourcePath, string totalCommanderDestinationPath, CancellationToken cancellationToken = default)
    {
        return await _transferService.CopyAsync(totalCommanderSourcePath, totalCommanderDestinationPath, cancellationToken);
    }

    public async Task<int> GetFileAsync(string totalCommanderSourcePath, string localTargetPath, CancellationToken cancellationToken = default)
    {
        return await _transferService.GetFileAsync(totalCommanderSourcePath, localTargetPath, cancellationToken);
    }

    public async Task<int> PutFileAsync(string localSourcePath, string totalCommanderDestinationPath, bool overwrite, CancellationToken cancellationToken = default)
    {
        return await _transferService.PutFileAsync(localSourcePath, totalCommanderDestinationPath, overwrite, cancellationToken);
    }
}
