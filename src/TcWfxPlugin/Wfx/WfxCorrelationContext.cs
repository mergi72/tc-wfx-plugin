namespace TcWfxPlugin.Wfx;

internal static class WfxCorrelationContext
{
    internal const string HeaderName = "X-VFS-Correlation-ID";

    private static readonly object SyncRoot = new();
    private static string? _current;

    internal static string Begin()
    {
        lock (SyncRoot)
        {
            _current = Guid.NewGuid().ToString("D");
            return _current;
        }
    }

    internal static string CurrentOrCreate()
    {
        lock (SyncRoot)
        {
            _current ??= Guid.NewGuid().ToString("D");
            return _current;
        }
    }

    internal static void End()
    {
        lock (SyncRoot)
        {
            _current = null;
        }
    }

    internal static void Apply(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation(HeaderName, CurrentOrCreate());
    }
}
