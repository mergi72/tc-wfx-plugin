namespace TcWfxPlugin.Core;

public readonly record struct ProviderPath(string Provider, string Path)
{
    public override string ToString() => $"{Provider}:{Path}";

    public static bool TryParse(string value, out ProviderPath providerPath)
    {
        providerPath = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return false;
        }

        var provider = value[..separatorIndex].Trim();
        var path = value[(separatorIndex + 1)..].Trim();

        if (provider.Length == 0 || path.Length == 0 || !path.StartsWith('/'))
        {
            return false;
        }

        providerPath = new ProviderPath(provider, path);
        return true;
    }
}
