using System.Text.Json;
using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Wfx;

public sealed class TcDialogVersioningDecisionProvider : IWfxVersioningDecisionProvider
{
    private readonly Func<string, string, bool> _requestYesNo;

    public TcDialogVersioningDecisionProvider(Func<string, string, bool> requestYesNo)
    {
        _requestYesNo = requestYesNo;
    }

    public WfxUploadVersioning? ChooseVersioning(WfxVersioningRequest request)
    {
        var currentVersion = TryGetString(request.Metadata, "current_version") ?? "?";
        var currentVersionType = TryGetString(request.Metadata, "current_version_type");
        var modifiedBy = TryGetString(request.Metadata, "current_modified_by");
        var modifiedAt = TryGetString(request.Metadata, "current_modified_at");
        var nextMajorVersion = TryCalculateNextVersion(currentVersion, major: true) ?? "next major";
        var nextMinorVersion = TryCalculateNextVersion(currentVersion, major: false) ?? "next minor";

        var text =
            $"Document already exists:\n{request.FileName}\n\n" +
            $"Current version: {currentVersion}{FormatSuffix(currentVersionType)}\n" +
            $"Modified by: {modifiedBy ?? "-"}\n" +
            $"Modified at: {modifiedAt ?? "-"}\n\n" +
            "Create a MAJOR version?\n\n" +
            $"Yes = major version {nextMajorVersion}\n" +
            $"No = minor version {nextMinorVersion}";

        var majorVersion = _requestYesNo("Upload new version", text);
        return new WfxUploadVersioning
        {
            Mode = "version",
            MajorVersion = majorVersion,
        };
    }

    private static string FormatSuffix(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : $" ({value})";
    }

    private static string? TryGetString(Dictionary<string, JsonElement>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static string? TryCalculateNextVersion(string currentVersion, bool major)
    {
        var parts = currentVersion.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out var majorPart))
        {
            return null;
        }

        var minorPart = 0;
        if (parts.Length > 1 && !int.TryParse(parts[1], out minorPart))
        {
            return null;
        }

        if (major)
        {
            return $"{majorPart + 1}.0";
        }

        return $"{majorPart}.{minorPart + 1}";
    }
}
