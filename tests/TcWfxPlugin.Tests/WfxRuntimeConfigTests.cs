using System.Text.Json;
using TcWfxPlugin.Wfx;

namespace TcWfxPlugin.Tests;

public sealed class WfxRuntimeConfigTests
{
    [Fact]
    public void Load_WhenConfigJsonPresent_UsesConfiguredValuesIncludingLoggingDisabled()
    {
        using var workspace = new TempDirectory();
        var configPath = Path.Combine(workspace.Path, "config.json");

        var json = """
            {
              "bridge": {
                "url": "http://127.0.0.1:8765",
                "timeoutSeconds": 321
              },
              "progress": {
                "steps": 17
              },
              "logging": {
                "enabled": false,
                "path": "diag-logs"
              }
            }
            """;

        File.WriteAllText(configPath, json);

        using var env = new ScopedEnvironmentVariables(new Dictionary<string, string?>
        {
            ["TC_WFX_BRIDGE_URL"] = "http://should-not-win:9999/",
            ["TC_WFX_BRIDGE_TIMEOUT_SECONDS"] = "30",
            ["TC_WFX_PROGRESS_STEPS"] = "3",
            ["TC_WFX_LOGGING_ENABLED"] = "true",
            ["TC_WFX_LOG_DIR"] = "other-logs",
        });

        var runtimeConfig = WfxRuntimeConfig.Load(workspace.Path);

        Assert.Equal("http://127.0.0.1:8765/", runtimeConfig.BridgeUrl);
        Assert.Equal(TimeSpan.FromSeconds(321), runtimeConfig.BridgeTimeout);
        Assert.Equal(17, runtimeConfig.ProgressSteps);
        Assert.False(runtimeConfig.LoggingEnabled);
        Assert.Equal(Path.Combine(workspace.Path, "diag-logs"), runtimeConfig.LogDirectoryPath);
    }

    [Fact]
    public void Load_WhenConfigMissing_FallsBackToEnvironmentValues()
    {
        using var workspace = new TempDirectory();

        using var env = new ScopedEnvironmentVariables(new Dictionary<string, string?>
        {
            ["TC_WFX_BRIDGE_URL"] = "http://127.0.0.1:8877",
            ["TC_WFX_BRIDGE_TIMEOUT_SECONDS"] = "120",
            ["TC_WFX_PROGRESS_STEPS"] = "12",
            ["TC_WFX_LOGGING_ENABLED"] = "false",
            ["TC_WFX_LOG_DIR"] = "runtime-logs",
        });

        var runtimeConfig = WfxRuntimeConfig.Load(workspace.Path);

        Assert.Equal("http://127.0.0.1:8877/", runtimeConfig.BridgeUrl);
        Assert.Equal(TimeSpan.FromSeconds(120), runtimeConfig.BridgeTimeout);
        Assert.Equal(12, runtimeConfig.ProgressSteps);
        Assert.False(runtimeConfig.LoggingEnabled);
        Assert.Equal(Path.Combine(workspace.Path, "runtime-logs"), runtimeConfig.LogDirectoryPath);
    }

    [Fact]
    public void Load_WhenNestedConfigExists_UsesNestedConfigLocation()
    {
        using var workspace = new TempDirectory();
        var nestedDir = Directory.CreateDirectory(Path.Combine(workspace.Path, "config"));
        var nestedConfigPath = Path.Combine(nestedDir.FullName, "config.json");

        var json = JsonSerializer.Serialize(new
        {
            progress = new
            {
                steps = 22,
            },
            logging = new
            {
                enabled = false,
            },
        });

        File.WriteAllText(nestedConfigPath, json);

        var runtimeConfig = WfxRuntimeConfig.Load(workspace.Path);

        Assert.Equal(22, runtimeConfig.ProgressSteps);
        Assert.False(runtimeConfig.LoggingEnabled);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tc-wfx-config-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class ScopedEnvironmentVariables : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues;

        public ScopedEnvironmentVariables(Dictionary<string, string?> values)
        {
            _previousValues = new Dictionary<string, string?>(StringComparer.Ordinal);

            foreach (var (key, value) in values)
            {
                _previousValues[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, value) in _previousValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
