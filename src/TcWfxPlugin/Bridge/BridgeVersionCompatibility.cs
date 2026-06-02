namespace TcWfxPlugin.Bridge;

internal static class BridgeVersionCompatibility
{
    public static bool IsSupported(string bridgeVersion, string minimumSupportedVersion, out string reason)
    {
        if (!TryParseVersion(bridgeVersion, out var parsedBridgeVersion))
        {
            reason = $"Unable to parse bridge version '{bridgeVersion}'.";
            return false;
        }

        if (!TryParseVersion(minimumSupportedVersion, out var parsedMinimumVersion))
        {
            reason = $"Unable to parse minimum supported bridge version '{minimumSupportedVersion}'.";
            return false;
        }

        if (parsedBridgeVersion.CompareTo(parsedMinimumVersion) < 0)
        {
            reason = $"Bridge version {parsedBridgeVersion} is lower than required {parsedMinimumVersion}.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        var separatorIndex = candidate.IndexOfAny(['-', '+']);
        if (separatorIndex >= 0)
        {
            candidate = candidate[..separatorIndex];
        }

        if (!Version.TryParse(candidate, out var parsed) || parsed is null)
        {
            return false;
        }

        version = parsed;
        return true;
    }
}
