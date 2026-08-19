namespace TcWfxPlugin.Wfx;

internal static class VfsDiagnosticLogFormatter
{
    public static string Format(DateTime timestamp, string message)
    {
        return $"{timestamp:yyyy-MM-dd HH:mm:ss,fff} DEBUG tc-wfx: {message}";
    }
}
