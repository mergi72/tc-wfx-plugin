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
            return WfxVersioningDialogChoice.Minor;
        }, WfxDialogLanguage.English);

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
        Assert.Contains("Yes = new MAJOR target version 2.0", capturedText, StringComparison.Ordinal);
        Assert.Contains("No = new MINOR target version 1.5", capturedText, StringComparison.Ordinal);
        Assert.Contains("Cancel = do nothing", capturedText, StringComparison.Ordinal);
    }

    [Fact]
    public void ChooseVersioning_ShowsSourceAndTargetVersionsForProviderToProviderConflict()
    {
        string? capturedText = null;
        var provider = new TcDialogVersioningDecisionProvider((_, text) =>
        {
            capturedText = text;
            return WfxVersioningDialogChoice.Minor;
        }, WfxDialogLanguage.English);

        var result = provider.ChooseVersioning(new WfxVersioningRequest
        {
            SourcePath = @"\edocat\source.pdf",
            DestinationPath = @"\alfresco\target.pdf",
            FileName = "target.pdf",
            Metadata = new Dictionary<string, JsonElement>
            {
                ["source_provider"] = JsonDocument.Parse("\"edocat\"").RootElement.Clone(),
                ["source_version"] = JsonDocument.Parse("null").RootElement.Clone(),
                ["target_provider"] = JsonDocument.Parse("\"alfresco\"").RootElement.Clone(),
                ["target_version"] = JsonDocument.Parse("\"5.1\"").RootElement.Clone(),
                ["target_version_type"] = JsonDocument.Parse("\"MINOR\"").RootElement.Clone(),
                ["current_version"] = JsonDocument.Parse("\"5.1\"").RootElement.Clone(),
                ["current_version_type"] = JsonDocument.Parse("\"MINOR\"").RootElement.Clone(),
            },
        });

        Assert.NotNull(result);
        Assert.Contains("Source version: unknown", capturedText, StringComparison.Ordinal);
        Assert.Contains("Target version: 5.1 (MINOR)", capturedText, StringComparison.Ordinal);
        Assert.Contains("Yes = new MAJOR target version 6.0", capturedText, StringComparison.Ordinal);
        Assert.Contains("No = new MINOR target version 5.2", capturedText, StringComparison.Ordinal);
        Assert.Contains("Cancel = do nothing", capturedText, StringComparison.Ordinal);
    }

    [Fact]
    public void ChooseVersioning_WhenDialogIsCanceled_ReturnsNull()
    {
        var provider = new TcDialogVersioningDecisionProvider((_, _) => WfxVersioningDialogChoice.Cancel, WfxDialogLanguage.English);

        var result = provider.ChooseVersioning(new WfxVersioningRequest
        {
            SourcePath = @"C:\Temp\sample.pdf",
            DestinationPath = @"\alfresco\sample.pdf",
            FileName = "sample.pdf",
            Metadata = new Dictionary<string, JsonElement>
            {
                ["current_version"] = JsonDocument.Parse("\"1.4\"").RootElement.Clone(),
            },
        });

        Assert.Null(result);
    }

    [Fact]
    public void ChooseVersioning_WhenCzechLanguageIsSelected_ShowsCzechText()
    {
        string? capturedTitle = null;
        string? capturedText = null;
        var provider = new TcDialogVersioningDecisionProvider((title, text) =>
        {
            capturedTitle = title;
            capturedText = text;
            return WfxVersioningDialogChoice.Major;
        }, WfxDialogLanguage.Czech);

        var result = provider.ChooseVersioning(new WfxVersioningRequest
        {
            SourcePath = @"\edocat\source.pdf",
            DestinationPath = @"\alfresco\target.pdf",
            FileName = "target.pdf",
            Metadata = new Dictionary<string, JsonElement>
            {
                ["source_provider"] = JsonDocument.Parse("\"edocat\"").RootElement.Clone(),
                ["source_version"] = JsonDocument.Parse("\"1.0\"").RootElement.Clone(),
                ["target_provider"] = JsonDocument.Parse("\"alfresco\"").RootElement.Clone(),
                ["target_version"] = JsonDocument.Parse("\"5.1\"").RootElement.Clone(),
                ["target_version_type"] = JsonDocument.Parse("\"MINOR\"").RootElement.Clone(),
                ["current_version"] = JsonDocument.Parse("\"5.1\"").RootElement.Clone(),
            },
        });

        Assert.NotNull(result);
        Assert.True(result!.MajorVersion);
        Assert.Equal("Nahrát novou verzi", capturedTitle);
        Assert.Contains("Dokument již existuje:", capturedText, StringComparison.Ordinal);
        Assert.Contains("Zdrojová verze: 1.0", capturedText, StringComparison.Ordinal);
        Assert.Contains("Cílová verze: 5.1 (MINOR)", capturedText, StringComparison.Ordinal);
        Assert.Contains("Ano = nová HLAVNÍ verze cíle 6.0", capturedText, StringComparison.Ordinal);
        Assert.Contains("Ne = nová VEDLEJŠÍ verze cíle 5.2", capturedText, StringComparison.Ordinal);
        Assert.Contains("Storno = neprovádět nic", capturedText, StringComparison.Ordinal);
    }

    [Fact]
    public void ChooseVersioning_WhenTotalCommanderLanguageIniIsCzech_ShowsCzechText()
    {
        string? capturedText = null;
        var provider = new TcDialogVersioningDecisionProvider((_, text) =>
        {
            capturedText = text;
            return WfxVersioningDialogChoice.Minor;
        }, () => "WCMD_CZ.LNG");

        var result = provider.ChooseVersioning(new WfxVersioningRequest
        {
            SourcePath = @"C:\Temp\sample.pdf",
            DestinationPath = @"\alfresco\sample.pdf",
            FileName = "sample.pdf",
            Metadata = new Dictionary<string, JsonElement>
            {
                ["current_version"] = JsonDocument.Parse("\"1.4\"").RootElement.Clone(),
            },
        });

        Assert.NotNull(result);
        Assert.Contains("Dokument již existuje:", capturedText, StringComparison.Ordinal);
        Assert.Contains("Aktuální verze: 1.4", capturedText, StringComparison.Ordinal);
    }
}
