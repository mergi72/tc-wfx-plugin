namespace TcWfxPlugin.Wfx;

internal sealed class WfxContextManager
{
    private readonly Func<DateTime> _utcNow;
    private readonly object _syncRoot = new();
    private readonly Dictionary<int, FindContext> _findContexts = new();
    private readonly TimeSpan _findContextTtl;
    private readonly int _maxFindContexts;
    private int _nextFindHandle = 1;

    public WfxContextManager(Func<DateTime> utcNow)
    {
        _utcNow = utcNow;
        _findContextTtl = ResolveFindContextTtl();
        _maxFindContexts = ResolveMaxFindContexts();
    }

    public (int Handle, WfxFindData? FirstItem) Register(IReadOnlyList<WfxFindData> items)
    {
        var context = new FindContext(items, _utcNow());
        var first = context.MoveNext();

        lock (_syncRoot)
        {
            CleanupExpiredNoLock();
            EnforceCapacityNoLock();

            var handle = _nextFindHandle;
            _nextFindHandle++;
            _findContexts[handle] = context;
            return (handle, first);
        }
    }

    public int FindNext(int handle, out WfxFindData? item)
    {
        item = null;

        lock (_syncRoot)
        {
            CleanupExpiredNoLock();
            if (!_findContexts.TryGetValue(handle, out var context))
            {
                return WfxResultCodes.FileNotFound;
            }

            context.Touch(_utcNow());
            var next = context.MoveNext();
            if (next is null)
            {
                return WfxResultCodes.NoMoreFiles;
            }

            item = next;
            return WfxResultCodes.Success;
        }
    }

    public int FindClose(int handle)
    {
        lock (_syncRoot)
        {
            CleanupExpiredNoLock();
            if (_findContexts.Remove(handle))
            {
                return WfxResultCodes.Success;
            }
        }

        return WfxResultCodes.FileNotFound;
    }

    public void ClearAll()
    {
        lock (_syncRoot)
        {
            _findContexts.Clear();
        }
    }

    private void CleanupExpiredNoLock()
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

    private void EnforceCapacityNoLock()
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
