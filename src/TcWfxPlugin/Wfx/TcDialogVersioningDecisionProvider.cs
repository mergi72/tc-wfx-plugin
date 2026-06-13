using System.Text.Json;
using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Wfx;

public sealed class TcDialogVersioningDecisionProvider : IWfxVersioningDecisionProvider
{
    private readonly Func<string, string, WfxVersioningDialogChoice> _requestVersioningChoice;
    private readonly WfxLocalization _text;

    public TcDialogVersioningDecisionProvider(
        Func<string, string, WfxVersioningDialogChoice> requestVersioningChoice,
        string languageId)
        : this(requestVersioningChoice, languageProvider: null, languageId)
    {
    }

    public TcDialogVersioningDecisionProvider(
        Func<string, string, WfxVersioningDialogChoice> requestVersioningChoice,
        Func<string?>? languageProvider = null,
        string? languageId = null)
    {
        _requestVersioningChoice = requestVersioningChoice;
        _text = languageId is not null
            ? WfxLocalization.ForLanguageId(languageId)
            : WfxLocalization.Current(languageProvider ?? (() => null));
    }

    public WfxUploadVersioning? ChooseVersioning(WfxVersioningRequest request)
    {
        var currentVersion = TryGetString(request.Metadata, "current_version") ?? "?";
        var currentVersionType = TryGetString(request.Metadata, "current_version_type");
        var sourceVersion = TryGetString(request.Metadata, "source_version");
        var sourceVersionType = TryGetString(request.Metadata, "source_version_type");
        var targetVersion = TryGetString(request.Metadata, "target_version") ?? currentVersion;
        var targetVersionType = TryGetString(request.Metadata, "target_version_type") ?? currentVersionType;
        var hasProviderSource = HasMetadataValue(request.Metadata, "source_provider")
            || HasMetadataValue(request.Metadata, "source_path")
            || HasMetadataValue(request.Metadata, "target_provider")
            || HasMetadataValue(request.Metadata, "target_path");
        var modifiedBy = TryGetString(request.Metadata, "current_modified_by");
        var modifiedAt = TryGetString(request.Metadata, "current_modified_at");
        var nextMajorVersion = TryCalculateNextVersion(currentVersion, major: true) ?? "next major";
        var nextMinorVersion = TryCalculateNextVersion(currentVersion, major: false) ?? "next minor";

        var text = _text.VersionConflictText(
            request.FileName,
            VersionLines(hasProviderSource, sourceVersion, sourceVersionType, targetVersion, targetVersionType, currentVersion, currentVersionType),
            modifiedBy,
            modifiedAt,
            nextMajorVersion,
            nextMinorVersion);

        var choice = _requestVersioningChoice(_text.VersioningTitle, text);
        if (choice == WfxVersioningDialogChoice.Cancel)
        {
            return null;
        }

        return new WfxUploadVersioning
        {
            Mode = "version",
            MajorVersion = choice == WfxVersioningDialogChoice.Major,
        };
    }

    private string VersionLines(
        bool hasProviderSource,
        string? sourceVersion,
        string? sourceVersionType,
        string targetVersion,
        string? targetVersionType,
        string currentVersion,
        string? currentVersionType)
    {
        if (!hasProviderSource)
        {
            return _text.CurrentVersionLine(currentVersion, currentVersionType);
        }

        return _text.SourceTargetVersionLines(sourceVersion ?? string.Empty, sourceVersionType, targetVersion, targetVersionType);
    }

    private static bool HasMetadataValue(Dictionary<string, JsonElement>? metadata, string key)
    {
        return metadata is not null
            && metadata.TryGetValue(key, out var value)
            && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
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

public enum WfxVersioningDialogChoice
{
    Cancel,
    Minor,
    Major,
}
