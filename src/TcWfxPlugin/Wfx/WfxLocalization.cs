using System.Text.Json;

namespace TcWfxPlugin.Wfx;

internal sealed class WfxLocalization
{
    private const string Fallback = "fallback";
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _texts;
    private readonly Func<string?> _languageProvider;

    private WfxLocalization(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> texts,
        Func<string?> languageProvider)
    {
        _texts = texts;
        _languageProvider = languageProvider;
    }

    public string ProviderLoginTitle => Get("providerLoginTitle");

    public string UserNamePrompt => Get("userNamePrompt");

    public string PasswordPrompt => Get("passwordPrompt");

    public string RememberLoginTitle => Get("rememberLoginTitle");

    public string RememberLoginQuestion => Get("rememberLoginQuestion");

    public string OverwriteTitle => Get("overwriteTitle");

    public string OverwriteQuestion(string path)
    {
        return Format("overwriteQuestion", new Dictionary<string, string>
        {
            ["path"] = path,
        });
    }

    public string VersioningTitle => Get("versioningTitle");

    public string VersionConflictText(
        string fileName,
        string versionLines,
        string? modifiedBy,
        string? modifiedAt,
        string nextMajorVersion,
        string nextMinorVersion)
    {
        return Format("versionConflictText", new Dictionary<string, string>
        {
            ["fileName"] = fileName,
            ["versionLines"] = versionLines,
            ["modifiedBy"] = modifiedBy ?? "-",
            ["modifiedAt"] = modifiedAt ?? "-",
            ["nextMajorVersion"] = nextMajorVersion,
            ["nextMinorVersion"] = nextMinorVersion,
        });
    }

    public string CurrentVersionLine(string version, string? versionType)
    {
        return Format("currentVersionLine", new Dictionary<string, string>
        {
            ["version"] = version,
            ["versionTypeSuffix"] = FormatSuffix(versionType),
        });
    }

    public string SourceTargetVersionLines(string? sourceVersion, string? sourceVersionType, string targetVersion, string? targetVersionType)
    {
        return Format("sourceTargetVersionLines", new Dictionary<string, string>
        {
            ["sourceVersion"] = FormatVersion(sourceVersion, sourceVersionType),
            ["targetVersion"] = FormatVersion(targetVersion, targetVersionType),
        });
    }

    public static WfxLocalization Current(Func<string?> languageProvider)
    {
        return new WfxLocalization(LoadTexts(), languageProvider);
    }

    public static WfxLocalization ForLanguageId(string? languageId)
    {
        return new WfxLocalization(LoadTexts(), () => languageId);
    }

    private string FormatVersion(string? version, string? versionType)
    {
        return string.IsNullOrWhiteSpace(version)
            ? Get("unknownVersion")
            : $"{version}{FormatSuffix(versionType)}";
    }

    private static string FormatSuffix(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : $" ({value})";
    }

    private string Format(string key, IReadOnlyDictionary<string, string> values)
    {
        var value = Get(key);
        foreach (var (name, replacement) in values)
        {
            value = value.Replace("{" + name + "}", replacement, StringComparison.Ordinal);
        }

        return value.Replace("\\n", "\n", StringComparison.Ordinal);
    }

    private string Get(string key)
    {
        var language = NormalizeLanguage(_languageProvider());
        if (_texts.TryGetValue(language, out var localized) && localized.TryGetValue(key, out var localizedValue))
        {
            return localizedValue;
        }

        if (_texts.TryGetValue(Fallback, out var fallback) && fallback.TryGetValue(key, out var fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return Fallback;
        }

        return Path.GetFileName(language.Trim().Trim('"')).ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadTexts()
    {
        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                return ParseTexts(File.ReadAllText(path));
            }
            catch
            {
                // Keep dialog text best-effort. If the external JSON is invalid,
                // use the built-in fallback below.
            }
        }

        return ParseTexts(FallbackJson);
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "config", "localize.json");
        yield return Path.Combine(baseDirectory, "localize.json");
        yield return Path.Combine(Environment.CurrentDirectory, "config", "localize.json");
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ParseTexts(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var languageProperty in document.RootElement.EnumerateObject())
        {
            if (languageProperty.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var languageTexts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var textProperty in languageProperty.Value.EnumerateObject())
            {
                if (textProperty.Value.ValueKind == JsonValueKind.String)
                {
                    languageTexts[textProperty.Name] = textProperty.Value.GetString() ?? string.Empty;
                }
            }

            result[NormalizeLanguage(languageProperty.Name)] = languageTexts;
        }

        return result;
    }

    private const string FallbackJson = """
{
  "fallback": {
    "providerLoginTitle": "Provider login",
    "userNamePrompt": "User name:",
    "passwordPrompt": "Password:",
    "rememberLoginTitle": "Remember login",
    "rememberLoginQuestion": "Remember credentials for provider bridge?",
    "overwriteTitle": "Overwrite existing file",
    "overwriteQuestion": "File already exists:\\n{path}\\n\\nOverwrite it?",
    "versioningTitle": "Upload new version",
    "versionConflictText": "Document already exists:\\n{fileName}\\n\\n{versionLines}Modified by: {modifiedBy}\\nModified at: {modifiedAt}\\n\\nUpload source content as a new target version?\\n\\nYes = new MAJOR target version {nextMajorVersion}\\nNo = new MINOR target version {nextMinorVersion}\\nCancel = do nothing",
    "currentVersionLine": "Current version: {version}{versionTypeSuffix}\\n",
    "sourceTargetVersionLines": "Source version: {sourceVersion}\\nTarget version: {targetVersion}\\n",
    "unknownVersion": "unknown"
  }
}
""";
}
