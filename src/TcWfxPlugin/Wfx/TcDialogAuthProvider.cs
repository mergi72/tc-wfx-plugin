using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Wfx;

public sealed class TcDialogAuthProvider : IWfxAuthProvider
{
    private readonly Func<int, string, string, string?> _requestValue;
    private readonly object _syncRoot = new();
    private BridgeAuthContext? _cachedAuth;

    public TcDialogAuthProvider(Func<int, string, string, string?> requestValue)
    {
        _requestValue = requestValue;
    }

    public BridgeAuthContext GetAuthContext()
    {
        lock (_syncRoot)
        {
            if (_cachedAuth is not null)
            {
                return _cachedAuth;
            }

            var mode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE") ?? "winuser";
            if (string.Equals(mode, "credentials", StringComparison.OrdinalIgnoreCase))
            {
                var credentialId = Environment.GetEnvironmentVariable("TC_WFX_CREDENTIAL_ID");
                var username = Environment.GetEnvironmentVariable("TC_WFX_USERNAME");
                var password = Environment.GetEnvironmentVariable("TC_WFX_PASSWORD");
                var token = Environment.GetEnvironmentVariable("TC_WFX_TOKEN");

                if (string.IsNullOrWhiteSpace(username))
                {
                    username = _requestValue(
                        WfxNativeExports.RequestTypeUserName,
                        "Provider login",
                        "User name:");
                }

                if (string.IsNullOrWhiteSpace(password))
                {
                    password = _requestValue(
                        WfxNativeExports.RequestTypePassword,
                        "Provider login",
                        "Password:");
                }

                _cachedAuth = new BridgeAuthContext
                {
                    Mode = "credentials",
                    CredentialId = credentialId,
                    Username = username,
                    Password = password,
                    Token = token,
                };

                return _cachedAuth;
            }

            _cachedAuth = new BridgeAuthContext
            {
                Mode = "winuser",
                WinUser = Environment.GetEnvironmentVariable("TC_WFX_WIN_USER") ?? Environment.UserName,
            };

            return _cachedAuth;
        }
    }
}