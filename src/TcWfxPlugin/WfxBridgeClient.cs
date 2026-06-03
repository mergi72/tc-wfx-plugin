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
    private const int RawDownloadUnexpectedJsonErrorCode = 500;
    private const string RawContentHeaderName = "X-Bridge-Raw-Content";
    private const string MinimumBridgeVersionEnvVar = "TC_WFX_MIN_BRIDGE_VERSION";
    private const string DefaultMinimumBridgeVersion = "0.2.0";

    private readonly HttpClient _httpClient;
    private readonly string _minimumBridgeVersion;
    private readonly SemaphoreSlim _compatibilityGate = new(1, 1);
    private int _compatibilityState;
    private string _compatibilityError = string.Empty;

    public string BaseUrl => _httpClient.BaseAddress?.ToString() ?? string.Empty;

    public WfxBridgeClient(string baseUrl)
        : this(CreateHttpClient(baseUrl))
    {
    }

    public WfxBridgeClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _minimumBridgeVersion = ResolveMinimumBridgeVersion();
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

    public async Task<WfxRawDownloadResult> DownloadRawAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
    {
        var compatibilityError = await EnsureBridgeCompatibilityAsync(cancellationToken);
        if (compatibilityError is not null)
        {
            return WfxRawDownloadResult.Failed(426, compatibilityError);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "bridge/wfx/download-raw")
        {
            Content = JsonContent.Create(new WfxPathRequest { Path = providerPath, Auth = auth }, SerializerContext.WfxPathRequest),
        };

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var rawHeader = response.Headers.TryGetValues(RawContentHeaderName, out var rawHeaderValues)
            ? rawHeaderValues.FirstOrDefault()
            : null;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            return WfxRawDownloadResult.Failed((int)response.StatusCode, $"Raw download failed with HTTP {(int)response.StatusCode}: {body}");
        }

        if (string.Equals(rawHeader, "1", StringComparison.Ordinal))
        {
            var rawStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return WfxRawDownloadResult.Succeeded(new WfxRawDownloadSession(response, rawStream, response.Content.Headers.ContentLength));
        }

        if (string.Equals(rawHeader, "0", StringComparison.Ordinal))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();

            if (TryParseBridgeEnvelope(body, out var parsedError))
            {
                return WfxRawDownloadResult.Failed(parsedError.ErrorCode, parsedError.Message ?? string.Empty);
            }

            return WfxRawDownloadResult.Failed(RawDownloadUnexpectedJsonErrorCode, $"Unexpected error payload for raw download: {body}");
        }

        if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var bodyBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (TryParseBridgeEnvelope(bodyBytes, out var parsedResponse))
            {
                response.Dispose();
                if (!parsedResponse.Ok)
                {
                    return WfxRawDownloadResult.Failed(parsedResponse.ErrorCode, parsedResponse.Message ?? string.Empty);
                }

                return WfxRawDownloadResult.Failed(RawDownloadUnexpectedJsonErrorCode, "Unexpected success envelope for raw download.");
            }

            // JSON file payloads are valid download content; keep them as binary data.
            var jsonStream = new MemoryStream(bodyBytes, writable: false);
            return WfxRawDownloadResult.Succeeded(new WfxRawDownloadSession(response, jsonStream, bodyBytes.LongLength));
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return WfxRawDownloadResult.Succeeded(new WfxRawDownloadSession(response, stream, response.Content.Headers.ContentLength));
    }

    private static bool TryParseBridgeEnvelope(string body, out WfxResponse<JsonElement> response)
    {
        response = default!;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(body, SerializerContext.WfxResponseJsonElement);
            if (parsed is null)
            {
                return false;
            }

            response = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseBridgeEnvelope(byte[] bodyBytes, out WfxResponse<JsonElement> response)
    {
        response = default!;
        if (bodyBytes.Length == 0)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(bodyBytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("ok", out var okProperty) || (okProperty.ValueKind != JsonValueKind.True && okProperty.ValueKind != JsonValueKind.False))
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("error_code", out var codeProperty) || codeProperty.ValueKind != JsonValueKind.Number)
            {
                return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(bodyBytes, SerializerContext.WfxResponseJsonElement);
            if (parsed is null)
            {
                return false;
            }

            response = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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
        var compatibilityError = await EnsureBridgeCompatibilityAsync(cancellationToken);
        if (compatibilityError is not null)
        {
            return WfxResponse<TData>.Failed(compatibilityError, 426);
        }

        using var content = JsonContent.Create(payload, requestTypeInfo);
        using var response = await _httpClient.PostAsync(route, content, cancellationToken);
        return await ParseResponseAsync(response, responseTypeInfo, cancellationToken);
    }

    private async Task<WfxResponse<TData>> GetAsync<TData>(
        string route,
        JsonTypeInfo<WfxResponse<TData>> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        var compatibilityError = await EnsureBridgeCompatibilityAsync(cancellationToken);
        if (compatibilityError is not null)
        {
            return WfxResponse<TData>.Failed(compatibilityError, 426);
        }

        using var response = await _httpClient.GetAsync(route, cancellationToken);
        return await ParseResponseAsync(response, responseTypeInfo, cancellationToken);
    }

    private async Task<string?> EnsureBridgeCompatibilityAsync(CancellationToken cancellationToken)
    {
        if (_compatibilityState == 1)
        {
            return null;
        }

        if (_compatibilityState == -1)
        {
            return _compatibilityError;
        }

        await _compatibilityGate.WaitAsync(cancellationToken);
        try
        {
            if (_compatibilityState == 1)
            {
                return null;
            }

            if (_compatibilityState == -1)
            {
                return _compatibilityError;
            }

            BridgeHealthResponse health;
            try
            {
                health = await GetBridgeHealthAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _compatibilityState = -1;
                _compatibilityError = $"Bridge compatibility check failed: unable to read health endpoint ({ex.Message}).";
                return _compatibilityError;
            }

            if (!BridgeVersionCompatibility.IsSupported(health.Version, _minimumBridgeVersion, out var reason))
            {
                _compatibilityState = -1;
                _compatibilityError =
                    $"Unsupported bridge version '{health.Version}'. Minimum required version is '{_minimumBridgeVersion}'. {reason}";
                return _compatibilityError;
            }

            _compatibilityState = 1;
            return null;
        }
        finally
        {
            _compatibilityGate.Release();
        }
    }

    private async Task<BridgeHealthResponse> GetBridgeHealthAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("health", cancellationToken);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {rawBody}");
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            throw new InvalidOperationException("Bridge health response body is empty.");
        }

        var parsed = JsonSerializer.Deserialize(rawBody, SerializerContext.BridgeHealthResponse);
        if (parsed is null)
        {
            throw new InvalidOperationException("Bridge health response could not be parsed.");
        }

        return parsed;
    }

    private static string ResolveMinimumBridgeVersion()
    {
        var configured = Environment.GetEnvironmentVariable(MinimumBridgeVersionEnvVar);
        return string.IsNullOrWhiteSpace(configured) ? DefaultMinimumBridgeVersion : configured.Trim();
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

public sealed class WfxRawDownloadSession : IDisposable
{
    private readonly HttpResponseMessage _response;

    public WfxRawDownloadSession(HttpResponseMessage response, Stream contentStream, long? contentLength)
    {
        _response = response;
        ContentStream = contentStream;
        ContentLength = contentLength;
    }

    public Stream ContentStream { get; }
    public long? ContentLength { get; }

    public void Dispose()
    {
        _response.Dispose();
    }
}

public sealed class WfxRawDownloadResult
{
    private WfxRawDownloadResult(bool ok, int errorCode, string message, WfxRawDownloadSession? session)
    {
        Ok = ok;
        ErrorCode = errorCode;
        Message = message;
        Session = session;
    }

    public bool Ok { get; }
    public int ErrorCode { get; }
    public string Message { get; }
    public WfxRawDownloadSession? Session { get; }

    public static WfxRawDownloadResult Succeeded(WfxRawDownloadSession session)
    {
        return new WfxRawDownloadResult(true, 0, string.Empty, session);
    }

    public static WfxRawDownloadResult Failed(int errorCode, string message)
    {
        return new WfxRawDownloadResult(false, errorCode, message, null);
    }
}
