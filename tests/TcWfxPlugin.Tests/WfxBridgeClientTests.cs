using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Tests;

public sealed class WfxBridgeClientTests
{
    [Fact]
    public async Task GetProvidersAsync_UsesBridgeConnectionsEndpoint()
    {
        var handler = new QueueHttpMessageHandler(
            HttpResponseMessageForJson(HttpStatusCode.OK, "{\"status\":\"ok\",\"service\":\"dms-provider-bridge\",\"version\":\"0.7.0-beta\"}"),
            HttpResponseMessageForJson(HttpStatusCode.OK, "{\"ok\":true,\"error_code\":0,\"message\":null,\"data\":{\"connection_names\":[\"alfresco\",\"edocat\"],\"default_connection\":\"alfresco\",\"connections\":[],\"available_drivers\":[\"alfresco\",\"edocat\"]}}"));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8765/"),
        };

        var client = new WfxBridgeClient(httpClient);
        var result = await client.GetProvidersAsync();

        Assert.True(result.Ok);
        Assert.NotNull(result.Data);
        Assert.Equal(["alfresco", "edocat"], result.Data!.Providers);
        Assert.Equal("alfresco", result.Data.DefaultProvider);
        Assert.EndsWith("/bridge/wfx/connections", handler.Requests[^1].RequestUri?.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("tc-wfx", Assert.Single(handler.Requests[^1].Headers.GetValues("X-VFS-Component")));
        Assert.True(Guid.TryParse(Assert.Single(handler.Requests[^1].Headers.GetValues("X-VFS-Correlation-ID")), out _));
    }

    [Fact]
    public async Task DownloadRawAsync_WhenBridgeReturnsJsonEnvelope_ReturnsFailureWithNonZeroErrorCode()
    {
        var handler = new QueueHttpMessageHandler(
            HttpResponseMessageForJson(HttpStatusCode.OK, "{\"status\":\"ok\",\"service\":\"dms-provider-bridge\",\"version\":\"0.2.0\"}"),
            HttpResponseMessageForJson(HttpStatusCode.OK, "{\"ok\":true,\"error_code\":0,\"message\":null,\"data\":{\"content_base64\":\"aGVsbG8=\"}}"));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8765/"),
        };

        var client = new WfxBridgeClient(httpClient);
        var result = await client.DownloadRawAsync(
            "edocat:/hello.json",
            new BridgeAuthContext { Mode = "credentials", CredentialId = "tc-wfx/bridge" });

        Assert.False(result.Ok);
        Assert.NotEqual(0, result.ErrorCode);
        Assert.Contains("Unexpected success envelope for raw download", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadRawAsync_WhenJsonContentTypeContainsFilePayload_ReturnsSuccess()
    {
        var jsonFilePayload = "{\"name\":\"default_11\",\"version\":1}";
        var handler = new QueueHttpMessageHandler(
            HttpResponseMessageForJson(HttpStatusCode.OK, "{\"status\":\"ok\",\"service\":\"dms-provider-bridge\",\"version\":\"0.2.0\"}"),
            HttpResponseMessageForJson(HttpStatusCode.OK, jsonFilePayload));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8765/"),
        };

        var client = new WfxBridgeClient(httpClient);
        var result = await client.DownloadRawAsync(
            "alfresco:/path/default_11.json",
            new BridgeAuthContext { Mode = "credentials", CredentialId = "tc-wfx/bridge" });

        Assert.True(result.Ok);
        Assert.NotNull(result.Session);
        using var reader = new StreamReader(result.Session!.ContentStream, Encoding.UTF8, leaveOpen: false);
        var text = await reader.ReadToEndAsync();
        Assert.Equal(jsonFilePayload, text);
    }

    [Fact]
    public async Task DownloadRawAsync_WhenHeaderForcesRawPayload_TreatsJsonAsFile()
    {
        var envelopeLikeJson = "{\"ok\":true,\"error_code\":0,\"message\":null}";
        var rawResponse = HttpResponseMessageForJson(HttpStatusCode.OK, envelopeLikeJson);
        rawResponse.Headers.Add("X-Bridge-Raw-Content", "1");

        var handler = new QueueHttpMessageHandler(
            HttpResponseMessageForJson(HttpStatusCode.OK, "{\"status\":\"ok\",\"service\":\"dms-provider-bridge\",\"version\":\"0.2.0\"}"),
            rawResponse);

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8765/"),
        };

        var client = new WfxBridgeClient(httpClient);
        var result = await client.DownloadRawAsync(
            "alfresco:/path/default_11.json",
            new BridgeAuthContext { Mode = "credentials", CredentialId = "tc-wfx/bridge" });

        Assert.True(result.Ok);
        Assert.NotNull(result.Session);
        using var reader = new StreamReader(result.Session!.ContentStream, Encoding.UTF8, leaveOpen: false);
        var text = await reader.ReadToEndAsync();
        Assert.Equal(envelopeLikeJson, text);
    }

    [Fact]
    public async Task DownloadRawAsync_WhenHeaderMarksErrorEnvelope_UsesEnvelopeErrorCode()
    {
        var errorEnvelope = "{\"ok\":false,\"error_code\":403,\"message\":\"Access denied\",\"data\":null}";
        var errorResponse = HttpResponseMessageForJson(HttpStatusCode.OK, errorEnvelope);
        errorResponse.Headers.Add("X-Bridge-Raw-Content", "0");

        var handler = new QueueHttpMessageHandler(
            HttpResponseMessageForJson(HttpStatusCode.OK, "{\"status\":\"ok\",\"service\":\"dms-provider-bridge\",\"version\":\"0.2.0\"}"),
            errorResponse);

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8765/"),
        };

        var client = new WfxBridgeClient(httpClient);
        var result = await client.DownloadRawAsync(
            "alfresco:/path/forbidden.json",
            new BridgeAuthContext { Mode = "credentials", CredentialId = "tc-wfx/bridge" });

        Assert.False(result.Ok);
        Assert.Equal(403, result.ErrorCode);
        Assert.Contains("Access denied", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadRawAsync_WhenHttpClientTimeoutIsShorterThanUploadTimeout_UsesUploadTimeout()
    {
        var handler = new DelayedQueueHttpMessageHandler(
            HttpResponseMessageForJson(HttpStatusCode.OK, "{\"status\":\"ok\",\"service\":\"dms-provider-bridge\",\"version\":\"0.2.0\"}"),
            HttpResponseMessageForJson(HttpStatusCode.OK, "{\"ok\":true,\"error_code\":0,\"message\":\"\",\"data\":null}"));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8765/"),
            Timeout = TimeSpan.FromMilliseconds(100),
        };

        var client = new WfxBridgeClient(httpClient);
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "payload");

        try
        {
            var result = await client.UploadRawAsync(
                "alfresco:/contracts",
                "upload.txt",
                new BridgeAuthContext { Mode = "credentials", CredentialId = "tc-wfx/bridge" },
                tempFile,
                overwrite: false);

            Assert.True(result.Ok);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadRawAsync_ReportsStreamingProgress()
    {
        var handler = new UploadReadingQueueHttpMessageHandler(
            HttpResponseMessageForJson(HttpStatusCode.OK, "{\"status\":\"ok\",\"service\":\"dms-provider-bridge\",\"version\":\"0.2.0\"}"),
            HttpResponseMessageForJson(HttpStatusCode.OK, "{\"ok\":true,\"error_code\":0,\"message\":\"\",\"data\":null}"));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8765/"),
        };

        var client = new WfxBridgeClient(httpClient);
        var tempFile = Path.GetTempFileName();
        var payload = new byte[(1024 * 1024 * 2) + 123];
        new Random(42).NextBytes(payload);
        await File.WriteAllBytesAsync(tempFile, payload);
        var progressEvents = new List<long>();
        var progress = new CallbackProgress<long>(value => progressEvents.Add(value));

        try
        {
            var result = await client.UploadRawAsync(
                "alfresco:/contracts",
                "upload.bin",
                new BridgeAuthContext { Mode = "credentials", CredentialId = "tc-wfx/bridge" },
                tempFile,
                overwrite: false,
                progress: progress);

            Assert.True(result.Ok);
            Assert.NotEmpty(progressEvents);
            Assert.Equal(payload.LongLength, progressEvents[^1]);
            Assert.Contains(progressEvents, value => value > 0 && value < payload.LongLength);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadRawAsync_WhenVersioningProvided_SendsVersioningJsonWithMajorVersion()
    {
        var handler = new CapturingUploadHttpMessageHandler(
            HttpResponseMessageForJson(HttpStatusCode.OK, "{\"status\":\"ok\",\"service\":\"dms-provider-bridge\",\"version\":\"0.4.7\"}"),
            HttpResponseMessageForJson(HttpStatusCode.OK, "{\"ok\":true,\"error_code\":0,\"message\":\"\",\"data\":null}"));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8765/"),
        };

        var client = new WfxBridgeClient(httpClient);
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "payload");

        try
        {
            var result = await client.UploadRawAsync(
                "alfresco:/contracts",
                "upload.txt",
                new BridgeAuthContext { Mode = "credentials", Username = "user", Password = "pass" },
                tempFile,
                overwrite: false,
                versioning: new WfxUploadVersioning
                {
                    Mode = "version",
                    MajorVersion = true,
                    Comment = "TC upload",
                });

            Assert.True(result.Ok);
            Assert.NotNull(handler.LastVersioningJson);
            Assert.Contains("\"majorVersion\":true", handler.LastVersioningJson, StringComparison.Ordinal);
            Assert.DoesNotContain("major_version", handler.LastVersioningJson, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static HttpResponseMessage HttpResponseMessageForJson(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        private readonly List<HttpRequestMessage> _requests = [];

        public QueueHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public IReadOnlyList<HttpRequestMessage> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"No queued response for request {request.Method} {request.RequestUri}.");
            }

            _requests.Add(request);
            var response = _responses.Dequeue();
            response.RequestMessage = request;
            if (response.Content is not null && response.Content.Headers.ContentType is null)
            {
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            return Task.FromResult(response);
        }
    }

    private sealed class DelayedQueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public DelayedQueueHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"No queued response for request {request.Method} {request.RequestUri}.");
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/bridge/wfx/upload-raw", StringComparison.Ordinal) == true)
            {
                await Task.Delay(300, cancellationToken);
            }

            var response = _responses.Dequeue();
            response.RequestMessage = request;
            if (response.Content is not null && response.Content.Headers.ContentType is null)
            {
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            return response;
        }
    }

    private sealed class UploadReadingQueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public UploadReadingQueueHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"No queued response for request {request.Method} {request.RequestUri}.");
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/bridge/wfx/upload-raw", StringComparison.Ordinal) == true && request.Content is not null)
            {
                _ = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            }

            var response = _responses.Dequeue();
            response.RequestMessage = request;
            if (response.Content is not null && response.Content.Headers.ContentType is null)
            {
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            return response;
        }
    }

    private sealed class CapturingUploadHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public CapturingUploadHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public string? LastVersioningJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"No queued response for request {request.Method} {request.RequestUri}.");
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/bridge/wfx/upload-raw", StringComparison.Ordinal) == true
                && request.Content is MultipartFormDataContent form)
            {
                foreach (var part in form)
                {
                    var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                    if (string.Equals(name, "versioning_json", StringComparison.Ordinal))
                    {
                        LastVersioningJson = await part.ReadAsStringAsync(cancellationToken);
                    }
                }
            }

            var response = _responses.Dequeue();
            response.RequestMessage = request;
            if (response.Content is not null && response.Content.Headers.ContentType is null)
            {
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            return response;
        }
    }

    private sealed class CallbackProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        public CallbackProgress(Action<T> callback)
        {
            _callback = callback;
        }

        public void Report(T value)
        {
            _callback(value);
        }
    }
}
