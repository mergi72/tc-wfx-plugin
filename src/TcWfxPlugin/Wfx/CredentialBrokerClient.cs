using System.Text;
using System.Text.Json;
using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Wfx;

public interface ICredentialBrokerClient
{
    BridgeAuthContext? Resolve(CredentialBrokerAuthRequirement requirement, string? provider = null);
}

public sealed class HttpCredentialBrokerClient : ICredentialBrokerClient
{
    private const string BrokerUrlEnvVar = "TC_WFX_CREDENTIAL_BROKER_URL";
    private const string BrokerTimeoutMsEnvVar = "TC_WFX_CREDENTIAL_BROKER_TIMEOUT_MS";
    private const string DefaultBrokerUrl = "http://127.0.0.1:8776/";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private static readonly BridgeJsonSerializerContext SerializerContext = BridgeJsonSerializerContext.Default;

    private readonly HttpClient _httpClient;

    public HttpCredentialBrokerClient()
        : this(CreateHttpClient())
    {
    }

    public HttpCredentialBrokerClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-VFS-Component", "tc-wfx");
    }

    public BridgeAuthContext? Resolve(CredentialBrokerAuthRequirement requirement, string? provider = null)
    {
        try
        {
            var request = new CredentialBrokerRequest
            {
                Provider = provider,
                Auth = requirement,
            };

            var json = JsonSerializer.Serialize(request, SerializerContext.CredentialBrokerRequest);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = _httpClient.PostAsync("credentials/resolve", content).GetAwaiter().GetResult();
            var rawResponse = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return null;
            }

            var parsed = JsonSerializer.Deserialize(rawResponse, SerializerContext.CredentialBrokerResponse);
            if (parsed?.Ok == true && parsed.Auth is not null)
            {
                return parsed.Auth;
            }
        }
        catch
        {
            // Broker is an optional user-context resolver. Callers fall back to dialog auth when unavailable.
        }

        return null;
    }

    private static HttpClient CreateHttpClient()
    {
        var baseUrl = Environment.GetEnvironmentVariable(BrokerUrlEnvVar);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = DefaultBrokerUrl;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            uri = new Uri(DefaultBrokerUrl, UriKind.Absolute);
        }

        var timeout = DefaultTimeout;
        var rawTimeout = Environment.GetEnvironmentVariable(BrokerTimeoutMsEnvVar);
        if (int.TryParse(rawTimeout, out var timeoutMs) && timeoutMs > 0)
        {
            timeout = TimeSpan.FromMilliseconds(timeoutMs);
        }

        return new HttpClient
        {
            BaseAddress = uri,
            Timeout = timeout,
        };
    }
}
