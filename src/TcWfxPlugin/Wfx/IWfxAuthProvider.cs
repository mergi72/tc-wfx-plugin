using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Wfx;

public interface IWfxAuthProvider
{
    BridgeAuthContext GetAuthContext(string? provider = null);
    void ResetCachedAuth();
}
