using System.Net.Http.Json;
using System.Text.Json;
using TcWfxPlugin.Bridge;
using TcWfxPlugin.Contracts;

namespace TcWfxPlugin;

public sealed class WfxBridgeClient : IWfxBridgeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

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
        return GetAsync<WfxProvidersData>("bridge/wfx/providers", cancellationToken);
    }

    public Task<WfxResponse<WfxListingData>> ListAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync<WfxPathRequest, WfxListingData>(
            "bridge/wfx/list",
            new WfxPathRequest { Path = providerPath, Auth = auth },
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> StatAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync<WfxPathRequest, JsonElement>(
            "bridge/wfx/stat",
            new WfxPathRequest { Path = providerPath, Auth = auth },
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> MkdirAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync<WfxPathRequest, JsonElement>(
            "bridge/wfx/mkdir",
            new WfxPathRequest { Path = providerPath, Auth = auth },
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> DeleteAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync<WfxPathRequest, JsonElement>(
            "bridge/wfx/delete",
            new WfxPathRequest { Path = providerPath, Auth = auth },
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> RenameAsync(string source, string destination, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync<WfxMoveRequest, JsonElement>(
            "bridge/wfx/rename",
            new WfxMoveRequest
            {
                Source = source,
                Destination = destination,
                Auth = auth,
            },
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> CopyAsync(string source, string destination, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync<WfxMoveRequest, JsonElement>(
            "bridge/wfx/copy",
            new WfxMoveRequest
            {
                Source = source,
                Destination = destination,
                Auth = auth,
            },
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> DownloadAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        return PostAsync<WfxPathRequest, JsonElement>(
            "bridge/wfx/download",
            new WfxPathRequest { Path = providerPath, Auth = auth },
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
        return PostAsync<WfxUploadRequest, JsonElement>(
            "bridge/wfx/upload",
            new WfxUploadRequest
            {
                Destination = destination,
                FileName = fileName,
                Auth = auth,
                ContentBase64 = contentBase64,
                Overwrite = overwrite,
            },
            cancellationToken);
    }

    private async Task<WfxResponse<TData>> PostAsync<TRequest, TData>(
        string route,
        TRequest payload,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(route, payload, JsonOptions, cancellationToken);
        return await ParseResponseAsync<TData>(response, cancellationToken);
    }

    private async Task<WfxResponse<TData>> GetAsync<TData>(
        string route,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(route, cancellationToken);
        return await ParseResponseAsync<TData>(response, cancellationToken);
    }

    private static async Task<WfxResponse<TData>> ParseResponseAsync<TData>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return WfxResponse<TData>.Failed("Bridge response body is empty.", (int)response.StatusCode);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<WfxResponse<TData>>(rawBody, JsonOptions);
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
        };
    }
}
