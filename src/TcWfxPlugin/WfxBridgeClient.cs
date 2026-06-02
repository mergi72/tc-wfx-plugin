namespace TcWfxPlugin;

public sealed class WfxBridgeClient
{
    public string BaseUrl { get; }

    public WfxBridgeClient(string baseUrl)
    {
        BaseUrl = baseUrl;
    }
}
