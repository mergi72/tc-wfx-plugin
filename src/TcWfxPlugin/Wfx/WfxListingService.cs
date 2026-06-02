using TcWfxPlugin.Core;

namespace TcWfxPlugin.Wfx;

internal sealed class WfxListingService
{
    private static readonly string[] DefaultProviders = ["edocat", "alfresco", "fso"];

    private readonly WfxPluginFacade _facade;
    private readonly IWfxAuthProvider _authProvider;
    private readonly Func<DateTime> _utcNow;
    private readonly object _rootProvidersCacheLock = new();
    private readonly TimeSpan _rootProvidersCacheTtl;
    private IReadOnlyList<string>? _cachedRootProviders;
    private DateTime _cachedRootProvidersAtUtc;

    public WfxListingService(WfxPluginFacade facade, IWfxAuthProvider authProvider, Func<DateTime> utcNow)
    {
        _facade = facade;
        _authProvider = authProvider;
        _utcNow = utcNow;
        _rootProvidersCacheTtl = ResolveRootProvidersCacheTtl();
    }

    public void InvalidateRootProvidersCache()
    {
        lock (_rootProvidersCacheLock)
        {
            _cachedRootProviders = null;
            _cachedRootProvidersAtUtc = default;
        }
    }

    public async Task<(int ResultCode, WfxFindData[] Items)> ResolveItemsAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        if (IsRootListingPath(totalCommanderPath))
        {
            var providers = await ResolveRootProvidersAsync(cancellationToken);
            return (WfxResultCodes.Success, BuildRootItems(providers));
        }

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderPath, out var providerPath))
        {
            return (WfxResultCodes.FileNotFound, []);
        }

        var response = await _facade.ListDirectoryAsync(providerPath, _authProvider.GetAuthContext(), cancellationToken);
        if (!response.Ok || response.Data is null)
        {
            return (WfxBridgeErrorMapper.MapError(response.ErrorCode), []);
        }

        var items = response.Data.Items
            .Select(item => new WfxFindData
            {
                FileName = item.Name,
                FullPath = item.Path,
                IsDirectory = item.IsFolder,
                Size = item.Size ?? 0,
                MimeType = item.MimeType,
            })
            .ToArray();

        return (items.Length > 0 ? WfxResultCodes.Success : WfxResultCodes.NoMoreFiles, items);
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

    private static bool IsRootListingPath(string totalCommanderPath)
    {
        if (string.IsNullOrWhiteSpace(totalCommanderPath))
        {
            return true;
        }

        var normalized = totalCommanderPath.Trim().Replace('\\', '/').Trim();
        return normalized is "/" or "/*.*";
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
}
