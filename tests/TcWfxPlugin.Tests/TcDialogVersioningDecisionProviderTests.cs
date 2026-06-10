using System.Text.Json;
using TcWfxPlugin.Wfx;

namespace TcWfxPlugin.Tests;

public sealed class TcDialogVersioningDecisionProviderTests
{
    [Fact]
    public void ChooseVersioning_ShowsCalculatedMajorAndMinorTargetVersions()
    {
        string? capturedText = null;
        var provider = new TcDialogVersioningDecisionProvider((_, text) =>
        {
            capturedText = text;
            return false;
        });

        var result = provider.ChooseVersioning(new WfxVersioningRequest
        {
            SourcePath = @"C:\Temp\sample.pdf",
            DestinationPath = @"\alfresco\sample.pdf",
            FileName = "sample.pdf",
            Metadata = new Dictionary<string, JsonElement>
            {
                ["current_version"] = JsonDocument.Parse("\"1.4\"").RootElement.Clone(),
                ["current_version_type"] = JsonDocument.Parse("\"MINOR\"").RootElement.Clone(),
            },
        });

        Assert.NotNull(result);
        Assert.False(result!.MajorVersion);
        Assert.Contains("Yes = major version 2.0", capturedText, StringComparison.Ordinal);
        Assert.Contains("No = minor version 1.5", capturedText, StringComparison.Ordinal);
    }
}
