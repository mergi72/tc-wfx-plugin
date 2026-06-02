using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Wfx;

public sealed class TcDialogAuthProvider : IWfxAuthProvider
{
    private readonly Func<int, string, string, string?> _requestValue;
    private readonly Func<string, string, bool> _requestYesNo;
    private readonly ICredentialStore _credentialStore;
    private readonly string _credentialTarget;
    private readonly object _syncRoot = new();
    private BridgeAuthContext? _cachedAuth;

    public TcDialogAuthProvider(
        Func<int, string, string, string?> requestValue,
        Func<string, string, bool> requestYesNo,
        ICredentialStore credentialStore,
        string credentialTarget)
    {
        _requestValue = requestValue;
        _requestYesNo = requestYesNo;
        _credentialStore = credentialStore;
        _credentialTarget = credentialTarget;
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

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    if (_credentialStore.TryRead(_credentialTarget, out var storedUsername, out var storedPassword))
                    {
                        username = string.IsNullOrWhiteSpace(username) ? storedUsername : username;
                        password = string.IsNullOrWhiteSpace(password) ? storedPassword : password;
                    }
                }

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

                if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                {
                    var remember = _requestYesNo(
                        "Remember login",
                        "Remember credentials for provider bridge?");

                    if (remember)
                    {
                        _credentialStore.Save(_credentialTarget, username, password);
                    }
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