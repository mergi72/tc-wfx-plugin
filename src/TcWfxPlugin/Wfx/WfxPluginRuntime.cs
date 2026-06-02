using System.Text.Json;
using TcWfxPlugin.Contracts;
using TcWfxPlugin.Core;

namespace TcWfxPlugin.Wfx;

public sealed class WfxPluginRuntime
{
    private readonly WfxPluginFacade _facade;
    private readonly IWfxAuthProvider _authProvider;
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<int, FindContext> _findContexts = new();
    private readonly object _syncRoot = new();
    private readonly object _rootProvidersCacheLock = new();
    private readonly TimeSpan _rootProvidersCacheTtl;
    private readonly TimeSpan _findContextTtl;
    private readonly int _maxFindContexts;
    private int _nextFindHandle = 1;
    private IReadOnlyList<string>? _cachedRootProviders;
    private DateTime _cachedRootProvidersAtUtc;

    public WfxPluginRuntime(WfxPluginFacade facade, IWfxAuthProvider authProvider, Func<DateTime>? utcNow = null)
    {
        _facade = facade;
        _authProvider = authProvider;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _rootProvidersCacheTtl = ResolveRootProvidersCacheTtl();
        _findContextTtl = ResolveFindContextTtl();
        _maxFindContexts = ResolveMaxFindContexts();
    }

    public void InvalidateRootProvidersCache()
    {
        lock (_rootProvidersCacheLock)
        {
            _cachedRootProviders = null;
            _cachedRootProvidersAtUtc = default;
        }
    }

    public async Task<(int ResultCode, int Handle, WfxFindData? FirstItem)> FindFirstAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        WfxFindData[] items;

        if (IsRootListingPath(totalCommanderPath))
        {
            var providers = await ResolveRootProvidersAsync(cancellationToken);
            items = BuildRootItems(providers);
        }
        else
        {
            if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderPath, out var providerPath))
            {
                return (WfxResultCodes.FileNotFound, 0, null);
            }

            var response = await _facade.ListDirectoryAsync(providerPath, _authProvider.GetAuthContext(), cancellationToken);
            if (!response.Ok || response.Data is null)
            {
                return (MapError(response.ErrorCode), 0, null);
            }

            items = response.Data.Items
                .Select(item => new WfxFindData
                {
                    FileName = item.Name,
                    FullPath = item.Path,
                    IsDirectory = item.IsFolder,
                    Size = item.Size ?? 0,
                    MimeType = item.MimeType,
                })
                .ToArray();
        }

        var context = new FindContext(items, _utcNow());
        var first = context.MoveNext();

        var handle = RegisterContext(context);
        return (first is null ? WfxResultCodes.NoMoreFiles : WfxResultCodes.Success, handle, first);
    }

    public int FindNext(int handle, out WfxFindData? item)
    {
        item = null;

        if (!TryGetContext(handle, out var context))
        {
            return WfxResultCodes.FileNotFound;
        }

        var next = context.MoveNext();
        if (next is null)
        {
            return WfxResultCodes.NoMoreFiles;
        }

        item = next;
        return WfxResultCodes.Success;
    }

    public int FindClose(int handle)
    {
        lock (_syncRoot)
        {
            CleanupExpiredFindContextsNoLock();
            if (_findContexts.Remove(handle))
            {
                return WfxResultCodes.Success;
            }
        }

        return WfxResultCodes.FileNotFound;
    }

    public async Task<int> MkDirAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderPath, out var providerPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.CreateDirectoryAsync(providerPath, _authProvider.GetAuthContext(), cancellationToken);
        return response.Ok ? WfxResultCodes.Success : MapError(response.ErrorCode);
    }

    public async Task<int> DeleteAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderPath, out var providerPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.DeleteAsync(providerPath, _authProvider.GetAuthContext(), cancellationToken);
        return response.Ok ? WfxResultCodes.Success : MapError(response.ErrorCode);
    }

    public async Task<int> RenameAsync(string totalCommanderSourcePath, string totalCommanderDestinationPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderSourcePath, out var sourceProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderDestinationPath, out var destinationProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.RenameAsync(sourceProviderPath, destinationProviderPath, _authProvider.GetAuthContext(), cancellationToken);
        return response.Ok ? WfxResultCodes.Success : MapError(response.ErrorCode);
    }

    public async Task<int> CopyAsync(string totalCommanderSourcePath, string totalCommanderDestinationPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderSourcePath, out var sourceProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderDestinationPath, out var destinationProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.CopyAsync(sourceProviderPath, destinationProviderPath, _authProvider.GetAuthContext(), cancellationToken);
        return response.Ok ? WfxResultCodes.Success : MapError(response.ErrorCode);
    }

    public async Task<int> GetFileAsync(string totalCommanderSourcePath, string localTargetPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderSourcePath, out var sourceProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.DownloadAsync(sourceProviderPath, _authProvider.GetAuthContext(), cancellationToken);
        if (!response.Ok || response.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return MapError(response.ErrorCode);
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

        await File.WriteAllBytesAsync(localTargetPath, rawContent, cancellationToken);
        return WfxResultCodes.Success;
    }

    public async Task<int> PutFileAsync(string localSourcePath, string totalCommanderDestinationPath, bool overwrite, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localSourcePath))
        {
            return WfxResultCodes.FileNotFound;
        }

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderDestinationPath, out var destinationProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var fileName = Path.GetFileName(localSourcePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return WfxResultCodes.WriteError;
        }

        var content = await File.ReadAllBytesAsync(localSourcePath, cancellationToken);
        var contentBase64 = Convert.ToBase64String(content);

        var response = await _facade.UploadAsync(
            destinationProviderPath,
            fileName,
            _authProvider.GetAuthContext(),
            contentBase64,
            overwrite,
            cancellationToken);

        return response.Ok ? WfxResultCodes.Success : MapError(response.ErrorCode);
    }

    private int RegisterContext(FindContext context)
    {
        lock (_syncRoot)
        {
            CleanupExpiredFindContextsNoLock();
            EnforceFindContextCapacityNoLock();

            var handle = _nextFindHandle;
            _nextFindHandle++;
            _findContexts[handle] = context;
            return handle;
        }
    }

    private bool TryGetContext(int handle, out FindContext context)
    {
        lock (_syncRoot)
        {
            CleanupExpiredFindContextsNoLock();
            var found = _findContexts.TryGetValue(handle, out context!);
            if (found)
            {
                context.Touch(_utcNow());
            }

            return found;
        }
    }

    private void CleanupExpiredFindContextsNoLock()
    {
        if (_findContextTtl <= TimeSpan.Zero || _findContexts.Count == 0)
        {
            return;
        }

        var now = _utcNow();
        var expiredHandles = _findContexts
            .Where(entry => now - entry.Value.LastAccessAtUtc > _findContextTtl)
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var handle in expiredHandles)
        {
            _findContexts.Remove(handle);
        }
    }

    private void EnforceFindContextCapacityNoLock()
    {
        if (_maxFindContexts <= 0)
        {
            return;
        }

        while (_findContexts.Count >= _maxFindContexts)
        {
            var oldest = _findContexts
                .OrderBy(entry => entry.Value.LastAccessAtUtc)
                .ThenBy(entry => entry.Value.CreatedAtUtc)
                .FirstOrDefault();

            if (oldest.Key == 0 && oldest.Value is null)
            {
                return;
            }

            _findContexts.Remove(oldest.Key);
        }
    }

    private static int MapError(int bridgeErrorCode)
    {
        return bridgeErrorCode switch
        {
            0 => WfxResultCodes.Success,
            400 => WfxResultCodes.AccessDenied,
            401 => WfxResultCodes.AccessDenied,
            403 => WfxResultCodes.AccessDenied,
            404 => WfxResultCodes.FileNotFound,
            _ => WfxResultCodes.UnknownError,
        };
    }

    private static bool IsRootListingPath(string totalCommanderPath)
    {
        if (string.IsNullOrWhiteSpace(totalCommanderPath))
        {
            return true;
        }

        var normalized = totalCommanderPath.Trim().Replace('\\', '/').Trim();
        return normalized is "/" or "/*.*";
    }

    private async Task<IReadOnlyList<string>> ResolveRootProvidersAsync(CancellationToken cancellationToken)
    {
        var configured = TryGetConfiguredProvidersFromEnvironment();
        if (configured.Count > 0)
        {
            return configured;
        }

        var cached = TryGetCachedRootProviders(allowStale: false);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var response = await _facade.GetProvidersAsync(cancellationToken);
            var providers = response.Data?.Providers;
            if (response.Ok && providers is not null && providers.Count > 0)
            {
                CacheRootProviders(providers);
                return providers;
            }
        }
        catch
        {
            // Fallback below keeps root listing available even when bridge is temporarily unavailable.
        }

        var staleCached = TryGetCachedRootProviders(allowStale: true);
        if (staleCached is not null)
        {
            return staleCached;
        }

        return DefaultProviders;
    }

    private static WfxFindData[] BuildRootItems(IReadOnlyList<string> providers)
    {
        return providers
            .Select(provider => new WfxFindData
            {
                FileName = provider,
                FullPath = $"{provider}:/",
                IsDirectory = true,
                Size = 0,
                MimeType = null,
            })
            .ToArray();
    }

    private static IReadOnlyList<string> TryGetConfiguredProvidersFromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable("TC_WFX_PROVIDERS");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return [];
        }

        var providers = configured
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return providers;
    }

    private IReadOnlyList<string>? TryGetCachedRootProviders(bool allowStale)
    {
        if (!allowStale && _rootProvidersCacheTtl <= TimeSpan.Zero)
        {
            return null;
        }

        lock (_rootProvidersCacheLock)
        {
            if (_cachedRootProviders is null)
            {
                return null;
            }

            if (!allowStale)
            {
                var age = _utcNow() - _cachedRootProvidersAtUtc;
                if (age > _rootProvidersCacheTtl)
                {
                    return null;
                }
            }

            return _cachedRootProviders;
        }
    }

    private void CacheRootProviders(IReadOnlyList<string> providers)
    {
        if (_rootProvidersCacheTtl <= TimeSpan.Zero)
        {
            return;
        }

        lock (_rootProvidersCacheLock)
        {
            _cachedRootProviders = providers.ToArray();
            _cachedRootProvidersAtUtc = _utcNow();
        }
    }

    private static TimeSpan ResolveRootProvidersCacheTtl()
    {
        var raw = Environment.GetEnvironmentVariable("TC_WFX_PROVIDERS_CACHE_SECONDS");
        if (!int.TryParse(raw, out var seconds))
        {
            seconds = 30;
        }

        if (seconds < 0)
        {
            seconds = 0;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan ResolveFindContextTtl()
    {
        var raw = Environment.GetEnvironmentVariable("TC_WFX_FIND_CONTEXT_TTL_SECONDS");
        if (!int.TryParse(raw, out var seconds))
        {
            seconds = 600;
        }

        if (seconds < 0)
        {
            seconds = 0;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static int ResolveMaxFindContexts()
    {
        var raw = Environment.GetEnvironmentVariable("TC_WFX_MAX_FIND_CONTEXTS");
        if (!int.TryParse(raw, out var maxContexts))
        {
            maxContexts = 512;
        }

        return maxContexts < 1 ? 1 : maxContexts;
    }

    private static readonly string[] DefaultProviders = ["edocat", "alfresco", "fso"];

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

    private sealed class FindContext
    {
        private readonly IReadOnlyList<WfxFindData> _items;
        private int _index;
        public DateTime CreatedAtUtc { get; }
        public DateTime LastAccessAtUtc { get; private set; }

        public FindContext(IReadOnlyList<WfxFindData> items, DateTime createdAtUtc)
        {
            _items = items;
            _index = -1;
            CreatedAtUtc = createdAtUtc;
            LastAccessAtUtc = createdAtUtc;
        }

        public void Touch(DateTime utcNow)
        {
            LastAccessAtUtc = utcNow;
        }

        public WfxFindData? MoveNext()
        {
            _index++;
            if (_index < 0 || _index >= _items.Count)
            {
                return null;
            }

            return _items[_index];
        }
    }
}
