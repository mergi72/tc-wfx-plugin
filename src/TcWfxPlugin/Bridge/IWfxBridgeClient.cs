using System.Text.Json;
using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Bridge;

public interface IWfxBridgeClient
{
    Task<WfxResponse<WfxProvidersData>> GetProvidersAsync(CancellationToken cancellationToken = default);
    Task<WfxResponse<WfxListingData>> ListAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default);
    Task<WfxResponse<JsonElement>> StatAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default);
    Task<WfxResponse<JsonElement>> MkdirAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default);
    Task<WfxResponse<JsonElement>> DeleteAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default);
    Task<WfxResponse<JsonElement>> RenameAsync(string source, string destination, BridgeAuthContext auth, BridgeAuthContext? sourceAuth = null, BridgeAuthContext? destinationAuth = null, bool overwrite = false, WfxUploadVersioning? versioning = null, CancellationToken cancellationToken = default);
    Task<WfxResponse<JsonElement>> CopyAsync(string source, string destination, BridgeAuthContext auth, BridgeAuthContext? sourceAuth = null, BridgeAuthContext? destinationAuth = null, bool overwrite = false, WfxUploadVersioning? versioning = null, CancellationToken cancellationToken = default);
    Task<WfxResponse<JsonElement>> DownloadAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default);
    Task<WfxResponse<JsonElement>> UploadAsync(string destination, string fileName, BridgeAuthContext auth, string? contentBase64, bool overwrite, WfxUploadVersioning? versioning = null, CancellationToken cancellationToken = default);
    Task<WfxResponse<JsonElement>> UploadFromSourceAsync(string destination, string fileName, BridgeAuthContext auth, string sourcePath, bool overwrite, WfxUploadVersioning? versioning = null, CancellationToken cancellationToken = default);
    Task<WfxResponse<JsonElement>> UploadRawAsync(string destination, string fileName, BridgeAuthContext auth, string sourcePath, bool overwrite, WfxUploadVersioning? versioning = null, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
}
