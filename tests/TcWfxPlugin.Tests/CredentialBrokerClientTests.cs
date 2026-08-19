using System.Net;
using System.Text;
using TcWfxPlugin.Contracts;
using TcWfxPlugin.Wfx;

namespace TcWfxPlugin.Tests;

public sealed class CredentialBrokerClientTests
{
    [Fact]
    public void Resolve_IdentifiesTcWfxToBroker()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8776/"),
        };
        var client = new HttpCredentialBrokerClient(httpClient);

        _ = client.Resolve(new CredentialBrokerAuthRequirement
        {
            Mode = "windows",
            Target = "tc-wfx/bridge",
            Required = true,
        });

        Assert.NotNull(handler.Request);
        Assert.Equal("tc-wfx", Assert.Single(handler.Request!.Headers.GetValues("X-VFS-Component")));
        Assert.True(Guid.TryParse(Assert.Single(handler.Request.Headers.GetValues("X-VFS-Correlation-ID")), out _));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":false}", Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }
}
