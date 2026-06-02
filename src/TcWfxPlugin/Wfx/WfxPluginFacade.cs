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

    public Task<WfxResponse<JsonElement>> UploadAsync(
        string destination,
        string fileName,
        BridgeAuthContext auth,
        string contentBase64,
        bool overwrite,
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

        return _bridgeClient.UploadAsync(destination, fileName, auth, contentBase64, overwrite, cancellationToken);
    }
}
