using System.Text.Json;

namespace TcWfxPlugin.Wfx;

public interface IWfxOverwriteDecisionProvider
{
    bool ConfirmOverwrite(WfxOverwriteRequest request);
}

public sealed class WfxOverwriteRequest
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required string FileName { get; init; }
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}

internal sealed class TcDialogOverwriteDecisionProvider : IWfxOverwriteDecisionProvider
{
    private readonly Func<string, string, bool> _confirm;
    private readonly WfxLocalization _localization;

    public TcDialogOverwriteDecisionProvider(Func<string, string, bool> confirm, Func<string?> languageProvider)
    {
        _confirm = confirm;
        _localization = WfxLocalization.Current(languageProvider);
    }

    public bool ConfirmOverwrite(WfxOverwriteRequest request)
    {
        return _confirm(
            _localization.OverwriteTitle,
            _localization.OverwriteQuestion(request.DestinationPath));
    }
}
