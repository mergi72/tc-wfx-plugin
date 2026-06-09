using System.Diagnostics;
using System.Text.Json;

namespace TcWfxPlugin.Wfx;

internal sealed class WfxRuntimeConfig
{
    private const string DefaultBridgeUrl = "http://127.0.0.1:8765/";
    private const int DefaultBridgeTimeoutSeconds = 900;
    private const int DefaultProgressSteps = 10;
    private const string DefaultLogDirectoryName = "logs";

    private const string BridgeUrlEnvVar = "TC_WFX_BRIDGE_URL";
    private const string BridgeTimeoutEnvVar = "TC_WFX_BRIDGE_TIMEOUT_SECONDS";
    private const string ProgressStepsEnvVar = "TC_WFX_PROGRESS_STEPS";
    private const string LoggingEnabledEnvVar = "TC_WFX_LOGGING_ENABLED";
    private const string LoggingDirEnvVar = "TC_WFX_LOG_DIR";
    private const string ConfigDirEnvVar = "TC_WFX_CONFIG_DIR";

    private const string RootConfigFileName = "config.json";
    private const string NestedConfigFileName = "config\\config.json";

    private WfxRuntimeConfig(
        string bridgeUrl,
        TimeSpan bridgeTimeout,
        int progressSteps,
        bool loggingEnabled,
        string logDirectoryPath)
    {
        BridgeUrl = bridgeUrl;
        BridgeTimeout = bridgeTimeout;
        ProgressSteps = progressSteps;
        LoggingEnabled = loggingEnabled;
        LogDirectoryPath = logDirectoryPath;
    }

    public string BridgeUrl { get; }
    public TimeSpan BridgeTimeout { get; }
    public int ProgressSteps { get; }
    public bool LoggingEnabled { get; }
    public string LogDirectoryPath { get; }

    public static WfxRuntimeConfig Load()
    {
        return Load(AppContext.BaseDirectory, includeInstalledFallback: true);
    }

    internal static WfxRuntimeConfig Load(string baseDir)
    {
        return Load(baseDir, includeInstalledFallback: false);
    }

    private static WfxRuntimeConfig Load(string baseDir, bool includeInstalledFallback)
    {
        var configPath = ResolveConfigPath(baseDir, includeInstalledFallback, out var configBaseDir);

        JsonElement configRoot = default;
        var hasConfig = false;
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            try
            {
                using var stream = File.OpenRead(configPath);
                using var document = JsonDocument.Parse(stream);
                configRoot = document.RootElement.Clone();
                hasConfig = true;
            }
            catch
            {
                hasConfig = false;
                configBaseDir = baseDir;
            }
        }

        var bridgeUrl = ResolveBridgeUrl(configRoot, hasConfig);
        var bridgeTimeout = ResolveBridgeTimeout(configRoot, hasConfig);
        var progressSteps = ResolveProgressSteps(configRoot, hasConfig);
        var loggingEnabled = ResolveLoggingEnabled(configRoot, hasConfig);
        var logDirectoryPath = ResolveLogDirectory(configRoot, hasConfig, configBaseDir);

        return new WfxRuntimeConfig(bridgeUrl, bridgeTimeout, progressSteps, loggingEnabled, logDirectoryPath);
    }

    private static string? ResolveConfigPath(string baseDir, bool includeInstalledFallback, out string configBaseDir)
    {
        foreach (var directory in ResolveConfigSearchDirectories(baseDir, includeInstalledFallback))
        {
            var rootPath = Path.Combine(directory, RootConfigFileName);
            if (File.Exists(rootPath))
            {
                configBaseDir = directory;
                return rootPath;
            }

            var nestedPath = Path.Combine(directory, NestedConfigFileName);
            if (File.Exists(nestedPath))
            {
                configBaseDir = directory;
                return nestedPath;
            }
        }

        configBaseDir = baseDir;
        return null;
    }

    private static IReadOnlyList<string> ResolveConfigSearchDirectories(string baseDir, bool includeInstalledFallback)
    {
        var directories = new List<string>();
        AddDirectory(directories, Environment.GetEnvironmentVariable(ConfigDirEnvVar));
        AddDirectory(directories, baseDir);
        AddDirectory(directories, TryGetLoadedPluginDirectory());
        if (includeInstalledFallback)
        {
            AddDirectory(directories, TryGetGhislerPluginDirectory());
        }
        return directories;
    }
    private static void AddDirectory(List<string> directories, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!directories.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                directories.Add(fullPath);
            }
        }
        catch
        {
            // Invalid config path candidates are ignored.
        }
    }

    private static string? TryGetLoadedPluginDirectory()
    {
        try
        {
            foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
            {
                var fileName = Path.GetFileName(module.FileName);
                if (string.Equals(fileName, "TcWfxPlugin.wfx64", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, "TcWfxPlugin.dll", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetDirectoryName(module.FileName);
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? TryGetGhislerPluginDirectory()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return string.IsNullOrWhiteSpace(appData)
                ? null
                : Path.Combine(appData, "GHISLER", "Plugins", "wfx", "TcWfxPlugin");
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveBridgeUrl(JsonElement root, bool hasConfig)
    {
        if (hasConfig
            && TryGetNestedString(root, out var value, "bridge", "url")
            && Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return EnsureTrailingSlash(value);
        }

        var env = Environment.GetEnvironmentVariable(BridgeUrlEnvVar);
        if (!string.IsNullOrWhiteSpace(env) && Uri.TryCreate(env, UriKind.Absolute, out _))
        {
            return EnsureTrailingSlash(env);
        }

        return DefaultBridgeUrl;
    }

    private static TimeSpan ResolveBridgeTimeout(JsonElement root, bool hasConfig)
    {
        if (hasConfig
            && TryGetNestedInt(root, out var configSeconds, "bridge", "timeoutSeconds")
            && configSeconds > 0)
        {
            return TimeSpan.FromSeconds(configSeconds);
        }

        var env = Environment.GetEnvironmentVariable(BridgeTimeoutEnvVar);
        if (int.TryParse(env, out var envSeconds) && envSeconds > 0)
        {
            return TimeSpan.FromSeconds(envSeconds);
        }

        return TimeSpan.FromSeconds(DefaultBridgeTimeoutSeconds);
    }

    private static int ResolveProgressSteps(JsonElement root, bool hasConfig)
    {
        if (hasConfig
            && TryGetNestedInt(root, out var configSteps, "progress", "steps")
            && configSteps > 0)
        {
            return Math.Clamp(configSteps, 1, 100);
        }

        var env = Environment.GetEnvironmentVariable(ProgressStepsEnvVar);
        if (int.TryParse(env, out var envSteps) && envSteps > 0)
        {
            return Math.Clamp(envSteps, 1, 100);
        }

        return DefaultProgressSteps;
    }

    private static bool ResolveLoggingEnabled(JsonElement root, bool hasConfig)
    {
        if (hasConfig && TryGetNestedBool(root, out var configEnabled, "logging", "enabled"))
        {
            return configEnabled;
        }

        var env = Environment.GetEnvironmentVariable(LoggingEnabledEnvVar);
        if (bool.TryParse(env, out var envEnabled))
        {
            return envEnabled;
        }

        return true;
    }

    private static string ResolveLogDirectory(JsonElement root, bool hasConfig, string baseDir)
    {
        string? configuredPath = null;
        if (hasConfig && TryGetNestedString(root, out var configPath, "logging", "path"))
        {
            configuredPath = configPath;
        }

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = Environment.GetEnvironmentVariable(LoggingDirEnvVar);
        }

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = DefaultLogDirectoryName;
        }

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(baseDir, configuredPath));
    }

    private static bool TryGetNestedString(JsonElement root, out string value, string sectionName, string propertyName)
    {
        value = string.Empty;
        if (!TryGetNestedProperty(root, out var property, sectionName, propertyName)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetNestedInt(JsonElement root, out int value, string sectionName, string propertyName)
    {
        value = 0;
        if (!TryGetNestedProperty(root, out var property, sectionName, propertyName)
            || property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        return property.TryGetInt32(out value);
    }

    private static bool TryGetNestedBool(JsonElement root, out bool value, string sectionName, string propertyName)
    {
        value = false;
        if (!TryGetNestedProperty(root, out var property, sectionName, propertyName)
            || (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetNestedProperty(JsonElement root, out JsonElement property, string sectionName, string propertyName)
    {
        property = default;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!root.TryGetProperty(sectionName, out var section) || section.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return section.TryGetProperty(propertyName, out property);
    }

    private static string EnsureTrailingSlash(string url)
    {
        return url.EndsWith("/", StringComparison.Ordinal) ? url : $"{url}/";
    }
}
