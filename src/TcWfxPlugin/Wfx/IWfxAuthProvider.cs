using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Wfx;

public interface IWfxAuthProvider
{
    BridgeAuthContext GetAuthContext();
    void ResetCachedAuth();
}
