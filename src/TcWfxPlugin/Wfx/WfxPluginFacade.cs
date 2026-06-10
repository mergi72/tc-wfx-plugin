using System.Text.Json;
using TcWfxPlugin.Bridge;
using TcWfxPlugin.Contracts;
using TcWfxPlugin.Core;

namespace TcWfxPlugin.Wfx;

public sealed class WfxPluginFacade
{
    private readonly IWfxBridgeClient _bridgeClient;

    public WfxPluginFacade(IWfxBridgeClient bridgeClient)
    {
        _bridgeClient = bridgeClient;
    }

    public Task<WfxResponse<WfxProvidersData>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        return _bridgeClient.GetProvidersAsync(cancellationToken);
    }

    public Task<WfxResponse<WfxListingData>> ListDirectoryAsync(
        string providerPath,
        BridgeAuthContext auth,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderPath.TryParse(providerPath, out _))
        {
            return Task.FromResult(WfxResponse<WfxListingData>.Failed($"Invalid provider path '{providerPath}'. Expected format provider:/path."));
        }

        return _bridgeClient.ListAsync(providerPath, auth, cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> GetItemInfoAsync(
        string providerPath,
        BridgeAuthContext auth,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderPath.TryParse(providerPath, out _))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed($"Invalid provider path '{providerPath}'. Expected format provider:/path."));
        }

        return _bridgeClient.StatAsync(providerPath, auth, cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> CreateDirectoryAsync(
        string providerPath,
        BridgeAuthContext auth,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderPath.TryParse(providerPath, out _))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed($"Invalid provider path '{providerPath}'. Expected format provider:/path."));
        }

        return _bridgeClient.MkdirAsync(providerPath, auth, cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> DeleteAsync(
        string providerPath,
        BridgeAuthContext auth,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderPath.TryParse(providerPath, out _))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed($"Invalid provider path '{providerPath}'. Expected format provider:/path."));
        }

        return _bridgeClient.DeleteAsync(providerPath, auth, cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> RenameAsync(
        string source,
        string destination,
        BridgeAuthContext auth,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderPath.TryParse(source, out _) || !ProviderPath.TryParse(destination, out _))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed("Invalid source/destination provider path. Expected format provider:/path."));
        }

        return _bridgeClient.RenameAsync(source, destination, auth, cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> CopyAsync(
        string source,
        string destination,
        BridgeAuthContext auth,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderPath.TryParse(source, out _) || !ProviderPath.TryParse(destination, out _))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed("Invalid source/destination provider path. Expected format provider:/path."));
        }

        return _bridgeClient.CopyAsync(source, destination, auth, cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> DownloadAsync(
        string providerPath,
        BridgeAuthContext auth,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderPath.TryParse(providerPath, out _))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed($"Invalid provider path '{providerPath}'. Expected format provider:/path."));
        }

        return _bridgeClient.DownloadAsync(providerPath, auth, cancellationToken);
    }

    public async Task<WfxRawDownloadResult?> DownloadRawAsync(
        string providerPath,
        BridgeAuthContext auth,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderPath.TryParse(providerPath, out _))
        {
            return WfxRawDownloadResult.Failed(404, $"Invalid provider path '{providerPath}'. Expected format provider:/path.");
        }

        if (_bridgeClient is WfxBridgeClient directClient)
        {
            return await directClient.DownloadRawAsync(providerPath, auth, cancellationToken);
        }

        return null;
    }

    public Task<WfxResponse<JsonElement>> UploadAsync(
        string destination,
        string fileName,
        BridgeAuthContext auth,
        string contentBase64,
        bool overwrite,
        WfxUploadVersioning? versioning = null,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderPath.TryParse(destination, out _))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed($"Invalid provider path '{destination}'. Expected format provider:/path."));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed("File name is required for upload."));
        }

        return _bridgeClient.UploadAsync(destination, fileName, auth, contentBase64, overwrite, versioning, cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> UploadFromSourceAsync(
        string destination,
        string fileName,
        BridgeAuthContext auth,
        string sourcePath,
        bool overwrite,
        WfxUploadVersioning? versioning = null,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderPath.TryParse(destination, out _))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed($"Invalid provider path '{destination}'. Expected format provider:/path."));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed("File name is required for upload."));
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed("Source path is required for upload."));
        }

        return _bridgeClient.UploadFromSourceAsync(destination, fileName, auth, sourcePath, overwrite, versioning, cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> UploadRawAsync(
        string destination,
        string fileName,
        BridgeAuthContext auth,
        string sourcePath,
        bool overwrite,
        WfxUploadVersioning? versioning = null,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderPath.TryParse(destination, out _))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed($"Invalid provider path '{destination}'. Expected format provider:/path."));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed("File name is required for upload."));
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return Task.FromResult(WfxResponse<JsonElement>.Failed("Source path is required for upload."));
        }

        return _bridgeClient.UploadRawAsync(destination, fileName, auth, sourcePath, overwrite, versioning, progress, cancellationToken);
    }
}
