using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TcWfxPlugin.Bridge;
using TcWfxPlugin.Contracts;
using TcWfxPlugin.Wfx;

namespace TcWfxPlugin;

public sealed class WfxBridgeClient : IWfxBridgeClient
{
    private static readonly BridgeJsonSerializerContext SerializerContext = BridgeJsonSerializerContext.Default;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan UploadTimeout = ResolveUploadTimeout();
    private const int UploadBufferSizeBytes = 1024 * 1024;
    private const int RawDownloadUnexpectedJsonErrorCode = 500;
    private const string RawContentHeaderName = "X-Bridge-Raw-Content";
    private const string MinimumBridgeVersionEnvVar = "TC_WFX_MIN_BRIDGE_VERSION";
    private const string UploadTimeoutSecondsEnvVar = "TC_WFX_UPLOAD_TIMEOUT_SECONDS";
    private const string DefaultMinimumBridgeVersion = "0.2.0";

    private readonly HttpClient _httpClient;
    private readonly string _minimumBridgeVersion;
    private readonly SemaphoreSlim _compatibilityGate = new(1, 1);
    private int _compatibilityState;
    private string _compatibilityError = string.Empty;

    public string BaseUrl => _httpClient.BaseAddress?.ToString() ?? string.Empty;

    public WfxBridgeClient(string baseUrl)
        : this(baseUrl, timeout: null)
    {
    }

    public WfxBridgeClient(string baseUrl, TimeSpan? timeout)
        : this(CreateHttpClient(baseUrl, timeout))
    {
    }

    public WfxBridgeClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-VFS-Component", "tc-wfx");
        if (_httpClient.Timeout != Timeout.InfiniteTimeSpan && _httpClient.Timeout < UploadTimeout)
        {
            _httpClient.Timeout = UploadTimeout;
        }
        _minimumBridgeVersion = ResolveMinimumBridgeVersion();
    }

    public Task<WfxResponse<WfxProvidersData>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync(
            "bridge/wfx/connections",
            SerializerContext.WfxResponseWfxProvidersData,
            cancellationToken);
    }
    public string? ResolveCredentialTarget(string? connectionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            return null;
        }

        try
        {
            var endpoint = $"bridge/wfx/connections/{Uri.EscapeDataString(connectionName.Trim())}";
            var response = GetAsync(endpoint, SerializerContext.WfxResponseJsonElement, cancellationToken)
                .GetAwaiter()
                .GetResult();
            if (!response.Ok || response.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                return null;
            }

            if (!response.Data.TryGetProperty("auth", out var auth) || auth.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var key in new[] { "credential_id", "credentialId", "target", "targetBase", "target_base" })
            {
                if (auth.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }
        }
        catch
        {
            // Keep the existing global credential target behavior if bridge detail lookup is unavailable.
        }

        return null;
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

    public Task<WfxResponse<JsonElement>> RenameAsync(string source, string destination, BridgeAuthContext auth, BridgeAuthContext? sourceAuth = null, BridgeAuthContext? destinationAuth = null, bool overwrite = false, WfxUploadVersioning? versioning = null, CancellationToken cancellationToken = default)
    {
        return PostAsync(
            "bridge/wfx/move",
            new WfxMoveRequest
            {
                Source = source,
                Destination = destination,
                Auth = auth,
                SourceAuth = sourceAuth,
                DestinationAuth = destinationAuth,
                Overwrite = overwrite,
                Versioning = versioning,
            },
            SerializerContext.WfxMoveRequest,
            SerializerContext.WfxResponseJsonElement,
            cancellationToken);
    }

    public Task<WfxResponse<JsonElement>> CopyAsync(string source, string destination, BridgeAuthContext auth, BridgeAuthContext? sourceAuth = null, BridgeAuthContext? destinationAuth = null, bool overwrite = false, WfxUploadVersioning? versioning = null, CancellationToken cancellationToken = default)
    {
        return PostAsync(
            "bridge/wfx/copy",
            new WfxMoveRequest
            {
                Source = source,
                Destination = destination,
                Auth = auth,
                SourceAuth = sourceAuth,
                DestinationAuth = destinationAuth,
                Overwrite = overwrite,
                Versioning = versioning,
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
        WfxCorrelationContext.Apply(request);

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
        WfxUploadVersioning? versioning = null,
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
                Versioning = versioning,
            },
            SerializerContext.WfxUploadRequest,
            SerializerContext.WfxResponseJsonElement,
            cancellationToken);
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
        return PostAsync(
            "bridge/wfx/upload",
            new WfxUploadRequest
            {
                Destination = destination,
                FileName = fileName,
                Auth = auth,
                SourcePath = sourcePath,
                Overwrite = overwrite,
                Versioning = versioning,
            },
            SerializerContext.WfxUploadRequest,
            SerializerContext.WfxResponseJsonElement,
            cancellationToken);
    }

    public async Task<WfxResponse<JsonElement>> UploadRawAsync(
        string destination,
        string fileName,
        BridgeAuthContext auth,
        string sourcePath,
        bool overwrite,
        WfxUploadVersioning? versioning = null,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var compatibilityError = await EnsureBridgeCompatibilityAsync(cancellationToken);
        if (compatibilityError is not null)
        {
            return WfxResponse<JsonElement>.Failed(compatibilityError, 426);
        }

        await using var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            UploadBufferSizeBytes,
            useAsync: true);
        Stream uploadStream = progress is null ? stream : new ProgressReadStream(stream, progress);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(destination), "destination");
        form.Add(new StringContent(fileName), "file_name");
        form.Add(new StringContent(overwrite ? "true" : "false"), "overwrite");
        form.Add(new StringContent(JsonSerializer.Serialize(auth, SerializerContext.BridgeAuthContext)), "auth_json");
        if (versioning is not null)
        {
            form.Add(new StringContent(JsonSerializer.Serialize(versioning, SerializerContext.WfxUploadVersioning)), "versioning_json");
        }

        using var fileContent = new StreamContent(uploadStream, UploadBufferSizeBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", fileName);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(UploadTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Post, "bridge/wfx/upload-raw") { Content = form };
        WfxCorrelationContext.Apply(request);
        using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
        return await ParseResponseAsync(response, SerializerContext.WfxResponseJsonElement, timeoutCts.Token);
    }

    private sealed class ProgressReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly IProgress<long> _progress;
        private long _bytesRead;

        public ProgressReadStream(Stream inner, IProgress<long> progress)
        {
            _inner = inner;
            _progress = progress;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            ReportProgress(read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = _inner.Read(buffer);
            ReportProgress(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            ReportProgress(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            ReportProgress(read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();

        public override ValueTask DisposeAsync()
        {
            return _inner.DisposeAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void ReportProgress(int bytesRead)
        {
            if (bytesRead <= 0)
            {
                return;
            }

            var totalBytesRead = Interlocked.Add(ref _bytesRead, bytesRead);
            _progress.Report(totalBytesRead);
        }
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

        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(payload, requestTypeInfo),
        };
        WfxCorrelationContext.Apply(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
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

        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        WfxCorrelationContext.Apply(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
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
        using var request = new HttpRequestMessage(HttpMethod.Get, "health");
        WfxCorrelationContext.Apply(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
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

    private static TimeSpan ResolveUploadTimeout()
    {
        var configured = Environment.GetEnvironmentVariable(UploadTimeoutSecondsEnvVar);
        if (int.TryParse(configured, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.FromMinutes(90);
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

    private static HttpClient CreateHttpClient(string baseUrl, TimeSpan? timeout)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Base URL must be an absolute URI.", nameof(baseUrl));
        }

        var resolvedTimeout = timeout.HasValue && timeout.Value > TimeSpan.Zero
            ? timeout.Value
            : DefaultTimeout;

        return new HttpClient
        {
            BaseAddress = uri,
            Timeout = resolvedTimeout,
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
