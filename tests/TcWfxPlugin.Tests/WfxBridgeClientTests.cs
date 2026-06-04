using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Tests;

public sealed class WfxBridgeClientTests
{
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

        public QueueHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"No queued response for request {request.Method} {request.RequestUri}.");
            }

            var response = _responses.Dequeue();
            response.RequestMessage = request;
            if (response.Content is not null && response.Content.Headers.ContentType is null)
            {
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            return Task.FromResult(response);
        }
    }
}
