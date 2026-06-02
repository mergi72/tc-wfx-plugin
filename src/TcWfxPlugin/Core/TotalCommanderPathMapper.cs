namespace TcWfxPlugin.Core;

public static class TotalCommanderPathMapper
{
    public static bool TryToProviderPath(string totalCommanderPath, out string providerPath)
    {
        providerPath = string.Empty;

        if (string.IsNullOrWhiteSpace(totalCommanderPath))
        {
            return false;
        }

        var normalized = totalCommanderPath.Trim().Replace('\\', '/');
        normalized = StripWildcardTail(normalized);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (ProviderPath.TryParse(normalized, out var alreadyProviderPath))
        {
            providerPath = alreadyProviderPath.ToString();
            return true;
        }

        if (!normalized.Contains('/'))
        {
            return false;
        }

        normalized = normalized.Trim('/');
        if (normalized.Length == 0)
        {
            return false;
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var provider = parts[0];
        var providerRelativePath = parts.Length == 1
            ? "/"
            : $"/{string.Join('/', parts.Skip(1))}";

        providerPath = $"{provider}:{providerRelativePath}";
        return ProviderPath.TryParse(providerPath, out _);
    }

    private static string StripWildcardTail(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash == path.Length - 1)
        {
            return path;
        }

        var tail = path[(lastSlash + 1)..];
        if (!tail.Contains('*') && !tail.Contains('?'))
        {
            return path;
        }

        return path[..lastSlash];
    }
}
