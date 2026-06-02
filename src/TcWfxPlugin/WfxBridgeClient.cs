using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TcWfxPlugin.Bridge;
using TcWfxPlugin.Contracts;

namespace TcWfxPlugin;

public sealed class WfxBridgeClient : IWfxBridgeClient
{
    private static readonly BridgeJsonSerializerContext SerializerContext = BridgeJsonSerializerContext.Default;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;

    public string BaseUrl => _httpClient.BaseAddress?.ToString() ?? string.Empty;

    public WfxBridgeClient(string baseUrl)
        : this(CreateHttpClient(baseUrl))
    {
    }

    public WfxBridgeClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<WfxResponse<WfxProvidersData>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(
            "bridge/wfx/providers",
            SerializerContext.WfxResponseWfxProvidersData,
            cancellationToken);
    }

    public Task<WfxResponse<WfxListingData>> ListAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync(
            "bridge/wfx/list",
            new WfxPathRequest { Path = providerPath, Auth = auth },
            SerializerContext.WfxPathRequest,
            SerializerContext.WfxResponseWfxListingData,
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> StatAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync(
            "bridge/wfx/stat",
            new WfxPathRequest { Path = providerPath, Auth = auth },
            SerializerContext.WfxPathRequest,
            SerializerContext.WfxResponseJsonElement,
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> MkdirAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync(
            "bridge/wfx/mkdir",
            new WfxPathRequest { Path = providerPath, Auth = auth },
            SerializerContext.WfxPathRequest,
            SerializerContext.WfxResponseJsonElement,
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> DeleteAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync(
            "bridge/wfx/delete",
            new WfxPathRequest { Path = providerPath, Auth = auth },
            SerializerContext.WfxPathRequest,
            SerializerContext.WfxResponseJsonElement,
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> RenameAsync(string source, string destination, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync(
            "bridge/wfx/rename",
            new WfxMoveRequest
            {
                Source = source,
                Destination = destination,
                Auth = auth,
            },
            SerializerContext.WfxMoveRequest,
            SerializerContext.WfxResponseJsonElement,
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> CopyAsync(string source, string destination, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync(
            "bridge/wfx/copy",
            new WfxMoveRequest
            {
                Source = source,
                Destination = destination,
                Auth = auth,
            },
            SerializerContext.WfxMoveRequest,
            SerializerContext.WfxResponseJsonElement,
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> DownloadAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync(
            "bridge/wfx/download",
            new WfxPathRequest { Path = providerPath, Auth = auth },
            SerializerContext.WfxPathRequest,
            SerializerContext.WfxResponseJsonElement,
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> UploadAsync(
        string destination,
        string fileName,
        BridgeAuthContext auth,
        string? contentBase64,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        return PostAsync(
            "bridge/wfx/upload",
            new WfxUploadRequest
            {
                Destination = destination,
                FileName = fileName,
                Auth = auth,
                ContentBase64 = contentBase64,
                Overwrite = overwrite,
            },
            SerializerContext.WfxUploadRequest,
            SerializerContext.WfxResponseJsonElement,
            cancellationToken);
    }

    private async Task<WfxResponse<TData>> PostAsync<TRequest, TData>(
        string route,
        TRequest payload,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<WfxResponse<TData>> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(payload, requestTypeInfo);
        using var response = await _httpClient.PostAsync(route, content, cancellationToken);
        return await ParseResponseAsync(response, responseTypeInfo, cancellationToken);
    }

    private async Task<WfxResponse<TData>> GetAsync<TData>(
        string route,
        JsonTypeInfo<WfxResponse<TData>> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(route, cancellationToken);
        return await ParseResponseAsync(response, responseTypeInfo, cancellationToken);
    }

    private static async Task<WfxResponse<TData>> ParseResponseAsync<TData>(
        HttpResponseMessage response,
        JsonTypeInfo<WfxResponse<TData>> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return WfxResponse<TData>.Failed("Bridge response body is empty.", (int)response.StatusCode);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(rawBody, responseTypeInfo);
            if (parsed is not null)
            {
                return parsed;
            }
        }
        catch (JsonException)
        {
            // Fallback error below keeps the original payload for easier diagnosis.
        }

        return WfxResponse<TData>.Failed(
            $"Unexpected bridge response ({(int)response.StatusCode}): {rawBody}",
            (int)response.StatusCode);
    }

    private static HttpClient CreateHttpClient(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Base URL must be an absolute URI.", nameof(baseUrl));
        }

        return new HttpClient
        {
            BaseAddress = uri,
            Timeout = DefaultTimeout,
        };
    }
}
