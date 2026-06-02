using System.Text.Json;
using TcWfxPlugin.Core;

namespace TcWfxPlugin.Wfx;

internal sealed class WfxTransferService
{
    private readonly WfxPluginFacade _facade;
    private readonly IWfxAuthProvider _authProvider;

    public WfxTransferService(WfxPluginFacade facade, IWfxAuthProvider authProvider)
    {
        _facade = facade;
        _authProvider = authProvider;
    }

    public async Task<int> MkDirAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderPath, out var providerPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.CreateDirectoryAsync(providerPath, _authProvider.GetAuthContext(), cancellationToken);
        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    public async Task<int> DeleteAsync(string totalCommanderPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderPath, out var providerPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.DeleteAsync(providerPath, _authProvider.GetAuthContext(), cancellationToken);
        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    public async Task<int> RenameAsync(string totalCommanderSourcePath, string totalCommanderDestinationPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderSourcePath, out var sourceProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderDestinationPath, out var destinationProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.RenameAsync(sourceProviderPath, destinationProviderPath, _authProvider.GetAuthContext(), cancellationToken);
        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    public async Task<int> CopyAsync(string totalCommanderSourcePath, string totalCommanderDestinationPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderSourcePath, out var sourceProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderDestinationPath, out var destinationProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.CopyAsync(sourceProviderPath, destinationProviderPath, _authProvider.GetAuthContext(), cancellationToken);
        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    public async Task<int> GetFileAsync(string totalCommanderSourcePath, string localTargetPath, CancellationToken cancellationToken = default)
    {
        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderSourcePath, out var sourceProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var response = await _facade.DownloadAsync(sourceProviderPath, _authProvider.GetAuthContext(), cancellationToken);
        if (!response.Ok || response.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return WfxBridgeErrorMapper.MapError(response.ErrorCode);
        }

        if (!TryGetContentBase64(response.Data, out var contentBase64))
        {
            return WfxResultCodes.ReadError;
        }

        byte[] rawContent;
        try
        {
            rawContent = Convert.FromBase64String(contentBase64);
        }
        catch (FormatException)
        {
            return WfxResultCodes.ReadError;
        }

        var targetDirectory = Path.GetDirectoryName(localTargetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        await File.WriteAllBytesAsync(localTargetPath, rawContent, cancellationToken);
        return WfxResultCodes.Success;
    }

    public async Task<int> PutFileAsync(string localSourcePath, string totalCommanderDestinationPath, bool overwrite, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localSourcePath))
        {
            return WfxResultCodes.FileNotFound;
        }

        if (!TotalCommanderPathMapper.TryToProviderPath(totalCommanderDestinationPath, out var destinationProviderPath))
        {
            return WfxResultCodes.FileNotFound;
        }

        var fileName = Path.GetFileName(localSourcePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return WfxResultCodes.WriteError;
        }

        var content = await File.ReadAllBytesAsync(localSourcePath, cancellationToken);
        var contentBase64 = Convert.ToBase64String(content);

        var response = await _facade.UploadAsync(
            destinationProviderPath,
            fileName,
            _authProvider.GetAuthContext(),
            contentBase64,
            overwrite,
            cancellationToken);

        return response.Ok ? WfxResultCodes.Success : WfxBridgeErrorMapper.MapError(response.ErrorCode);
    }

    private static bool TryGetContentBase64(JsonElement data, out string contentBase64)
    {
        contentBase64 = string.Empty;

        if (data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!data.TryGetProperty("content_base64", out var contentProperty) || contentProperty.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = contentProperty.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        contentBase64 = value;
        return true;
    }
}
