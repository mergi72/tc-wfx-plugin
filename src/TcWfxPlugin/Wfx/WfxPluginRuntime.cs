namespace TcWfxPlugin.Wfx;

public sealed class WfxPluginRuntime
{
    private readonly IWfxAuthProvider _authProvider;
    private readonly WfxListingService _listingService;
    private readonly WfxTransferService _transferService;
    private readonly WfxContextManager _contextManager;
    private readonly object _transferSyncRoot = new();
    private CancellationTokenSource? _activeTransferCts;

    public event Action<WfxTransferProgress>? TransferProgressChanged;

    public WfxPluginRuntime(WfxPluginFacade facade, IWfxAuthProvider authProvider, Func<DateTime>? utcNow = null)
    {
        _authProvider = authProvider;
        var nowProvider = utcNow ?? (() => DateTime.UtcNow);
        _listingService = new WfxListingService(facade, authProvider, nowProvider);
        _transferService = new WfxTransferService(facade, authProvider);
        _contextManager = new WfxContextManager(nowProvider);
    }

    public void InvalidateRootProvidersCache()
    {
        _listingService.InvalidateRootProvidersCache();
    }

    public void OnReconnect()
    {
        _listingService.InvalidateRootProvidersCache();
        _listingService.InvalidateCapabilitiesCache();
    }

    public async Task<(int ResultCode, int Handle, WfxFindData? FirstItem)> FindFirstAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        var (resultCode, items) = await _listingService.ResolveItemsAsync(totalCommanderPath, cancellationToken);
        if (resultCode == WfxResultCodes.AccessDenied)
        {
            _authProvider.ResetCachedAuth();
            (resultCode, items) = await _listingService.ResolveItemsAsync(totalCommanderPath, cancellationToken);
        }

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
        var result = await RunTransferAsync(
            (_, ct) => _transferService.MkDirAsync(totalCommanderPath, ct),
            cancellationToken,
            retryOnAccessDenied: false);

        if (result == WfxResultCodes.Success)
        {
            _contextManager.ClearAll();
        }

        return result;
    }

    public async Task<int> DeleteAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        return await RunTransferAsync(
            (_, ct) => _transferService.DeleteAsync(totalCommanderPath, ct),
            cancellationToken,
            retryOnAccessDenied: false);
    }

    public async Task<int> RenameAsync(string totalCommanderSourcePath, string totalCommanderDestinationPath, CancellationToken cancellationToken = default)
    {
        return await RunTransferAsync(
            (_, ct) => _transferService.RenameAsync(totalCommanderSourcePath, totalCommanderDestinationPath, ct),
            cancellationToken);
    }

    public async Task<int> CopyAsync(string totalCommanderSourcePath, string totalCommanderDestinationPath, CancellationToken cancellationToken = default)
    {
        return await RunTransferAsync(
            (_, ct) => _transferService.CopyAsync(totalCommanderSourcePath, totalCommanderDestinationPath, ct),
            cancellationToken);
    }

    public async Task<int> GetFileAsync(string totalCommanderSourcePath, string localTargetPath, CancellationToken cancellationToken = default)
    {
        return await RunTransferAsync(
            (progress, ct) => _transferService.GetFileAsync(totalCommanderSourcePath, localTargetPath, progress, ct),
            cancellationToken);
    }

    public async Task<int> PutFileAsync(string localSourcePath, string totalCommanderDestinationPath, bool overwrite, CancellationToken cancellationToken = default)
    {
        return await RunTransferAsync(
            (progress, ct) => _transferService.PutFileAsync(localSourcePath, totalCommanderDestinationPath, overwrite, progress, ct),
            cancellationToken);
    }

    public async Task<bool> PathExistsAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        var exists = await _transferService.PathExistsAsync(totalCommanderPath, cancellationToken);
        if (exists)
        {
            return true;
        }

        return false;
    }

    public void CancelCurrentTransfer()
    {
        lock (_transferSyncRoot)
        {
            _activeTransferCts?.Cancel();
        }
    }

    private async Task<int> RunTransferAsync(
        Func<IProgress<WfxTransferProgress>, CancellationToken, Task<int>> transfer,
        CancellationToken cancellationToken,
        bool retryOnAccessDenied = true)
    {
        CancellationTokenSource transferCts;
        lock (_transferSyncRoot)
        {
            _activeTransferCts?.Cancel();
            _activeTransferCts?.Dispose();
            _activeTransferCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            transferCts = _activeTransferCts;
        }

        try
        {
            var progress = new Progress<WfxTransferProgress>(value => TransferProgressChanged?.Invoke(value));
            var result = await transfer(progress, transferCts.Token);
            if (retryOnAccessDenied && result == WfxResultCodes.AccessDenied)
            {
                _authProvider.ResetCachedAuth();
                result = await transfer(progress, transferCts.Token);
            }

            return result;
        }
        catch (OperationCanceledException) when (transferCts.IsCancellationRequested)
        {
            return WfxResultCodes.UserAbort;
        }
        finally
        {
            lock (_transferSyncRoot)
            {
                if (ReferenceEquals(_activeTransferCts, transferCts))
                {
                    _activeTransferCts = null;
                }
            }

            transferCts.Dispose();
        }
    }
}
