using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Wfx;

public sealed class StaticAuthProvider : IWfxAuthProvider
{
    private readonly BridgeAuthContext _authContext;

    public StaticAuthProvider(BridgeAuthContext authContext)
    {
        _authContext = authContext;
    }

    public BridgeAuthContext GetAuthContext() => _authContext;

    public void ResetCachedAuth()
    {
    }
}
