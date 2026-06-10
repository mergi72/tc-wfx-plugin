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

    public WfxPluginRuntime(
        WfxPluginFacade facade,
        IWfxAuthProvider authProvider,
        Func<DateTime>? utcNow = null,
        IWfxProgressReporterFactory? progressReporterFactory = null,
        IWfxVersioningDecisionProvider? versioningDecisionProvider = null)
    {
        _authProvider = authProvider;
        var nowProvider = utcNow ?? (() => DateTime.UtcNow);
        _listingService = new WfxListingService(facade, authProvider, nowProvider);
        _transferService = new WfxTransferService(facade, authProvider, progressReporterFactory, versioningDecisionProvider);
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

    public async Task<int> MkDirAsync(string totalCommanderPath, IProgress<WfxTransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return await RunTransferAsync(
            (p, ct) => _transferService.MkDirAsync(totalCommanderPath, p, ct),
            cancellationToken,
            progress,
            retryOnAccessDenied: false,
            invalidateRootProvidersCacheOnSuccess: true);
    }

    public async Task<int> DeleteAsync(string totalCommanderPath, IProgress<WfxTransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return await RunTransferAsync(
            (p, ct) => _transferService.DeleteAsync(totalCommanderPath, p, ct),
            cancellationToken,
            progress,
            invalidateRootProvidersCacheOnSuccess: true);
    }

    public async Task<int> RenameAsync(string totalCommanderSourcePath, string totalCommanderDestinationPath, IProgress<WfxTransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return await RunTransferAsync(
            (p, ct) => _transferService.RenameAsync(totalCommanderSourcePath, totalCommanderDestinationPath, p, ct),
            cancellationToken,
            progress,
            invalidateRootProvidersCacheOnSuccess: true);
    }

    public async Task<int> CopyAsync(string totalCommanderSourcePath, string totalCommanderDestinationPath, IProgress<WfxTransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return await RunTransferAsync(
            (p, ct) => _transferService.CopyAsync(totalCommanderSourcePath, totalCommanderDestinationPath, p, ct),
            cancellationToken,
            progress,
            invalidateRootProvidersCacheOnSuccess: true);
    }

    public async Task<int> GetFileAsync(string totalCommanderSourcePath, string localTargetPath, CancellationToken cancellationToken = default)
    {
        return await GetFileAsync(totalCommanderSourcePath, localTargetPath, progress: null, cancellationToken);
    }

    public async Task<int> GetFileAsync(
        string totalCommanderSourcePath,
        string localTargetPath,
        IProgress<WfxTransferProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        return await RunTransferAsync(
            (transferProgress, ct) => _transferService.GetFileAsync(totalCommanderSourcePath, localTargetPath, transferProgress, ct),
            cancellationToken,
            progress);
    }

    public async Task<int> PutFileAsync(string localSourcePath, string totalCommanderDestinationPath, bool overwrite, CancellationToken cancellationToken = default)
    {
        return await PutFileAsync(localSourcePath, totalCommanderDestinationPath, overwrite, progress: null, cancellationToken);
    }

    public async Task<int> PutFileAsync(
        string localSourcePath,
        string totalCommanderDestinationPath,
        bool overwrite,
        IProgress<WfxTransferProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        return await RunTransferAsync(
            (transferProgress, ct) => _transferService.PutFileAsync(localSourcePath, totalCommanderDestinationPath, overwrite, transferProgress, ct),
            cancellationToken,
            progress,
            invalidateRootProvidersCacheOnSuccess: true);
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
        IProgress<WfxTransferProgress>? progress = null,
        bool retryOnAccessDenied = true,
        bool invalidateRootProvidersCacheOnSuccess = false)
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
            var effectiveProgress = progress ?? new InlineProgress<WfxTransferProgress>(value => TransferProgressChanged?.Invoke(value));
            var result = await transfer(effectiveProgress, transferCts.Token);
            if (retryOnAccessDenied && result == WfxResultCodes.AccessDenied)
            {
                _authProvider.ResetCachedAuth();
                result = await transfer(effectiveProgress, transferCts.Token);
            }

            if (invalidateRootProvidersCacheOnSuccess && result == WfxResultCodes.Success)
            {
                _listingService.InvalidateRootProvidersCache();
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

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value)
        {
            _handler(value);
        }
    }
}
