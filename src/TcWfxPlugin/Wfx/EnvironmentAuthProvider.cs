using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Wfx;

public sealed class EnvironmentAuthProvider : IWfxAuthProvider
{
    public BridgeAuthContext GetAuthContext(string? provider = null)
    {
        var mode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE") ?? "winuser";

        if (string.Equals(mode, "credentials", StringComparison.OrdinalIgnoreCase))
        {
            return new BridgeAuthContext
            {
                Mode = "credentials",
                CredentialId = Environment.GetEnvironmentVariable("TC_WFX_CREDENTIAL_ID"),
                Username = Environment.GetEnvironmentVariable("TC_WFX_USERNAME"),
                Password = Environment.GetEnvironmentVariable("TC_WFX_PASSWORD"),
                Token = Environment.GetEnvironmentVariable("TC_WFX_TOKEN"),
            };
        }

        return new BridgeAuthContext
        {
            Mode = "winuser",
            WinUser = Environment.GetEnvironmentVariable("TC_WFX_WIN_USER") ?? Environment.UserName,
        };
    }

    public void ResetCachedAuth()
    {
    }
}

