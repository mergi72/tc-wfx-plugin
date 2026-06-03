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
    private bool _ignoreStoredCredentialsOnce;

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

            var mode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE") ?? "credentials";
            if (string.Equals(mode, "credentials", StringComparison.OrdinalIgnoreCase))
            {
                var credentialId = Environment.GetEnvironmentVariable("TC_WFX_CREDENTIAL_ID");
                if (string.IsNullOrWhiteSpace(credentialId))
                {
                    credentialId = _credentialTarget;
                }

                var username = Environment.GetEnvironmentVariable("TC_WFX_USERNAME");
                var password = Environment.GetEnvironmentVariable("TC_WFX_PASSWORD");
                var token = Environment.GetEnvironmentVariable("TC_WFX_TOKEN");
                var promptedForCredentials = false;

                if (!_ignoreStoredCredentialsOnce && !string.IsNullOrWhiteSpace(credentialId))
                {
                    _cachedAuth = new BridgeAuthContext
                    {
                        Mode = "credentials",
                        CredentialId = credentialId,
                        Username = null,
                        Password = null,
                        Token = null,
                    };

                    return _cachedAuth;
                }

                if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(token))
                {
                    username = _requestValue(
                        WfxNativeExports.RequestTypeUserName,
                        "Provider login",
                        "User name:");
                    promptedForCredentials = true;
                }

                if (string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(token))
                {
                    password = _requestValue(
                        WfxNativeExports.RequestTypePassword,
                        "Provider login",
                        "Password:");
                    promptedForCredentials = true;
                }

                _ignoreStoredCredentialsOnce = false;

                if (promptedForCredentials && !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                {
                    var remember = _requestYesNo(
                        "Remember login",
                        "Remember credentials for provider bridge?");

                    if (remember)
                    {
                        var saveTarget = string.IsNullOrWhiteSpace(credentialId) ? _credentialTarget : credentialId;
                        _credentialStore.Save(saveTarget, username, password);
                        credentialId = saveTarget;
                        _cachedAuth = new BridgeAuthContext
                        {
                            Mode = "credentials",
                            CredentialId = credentialId,
                            Username = null,
                            Password = null,
                            Token = null,
                        };

                        return _cachedAuth;
                    }
                    else
                    {
                        var deleteTarget = string.IsNullOrWhiteSpace(credentialId) ? _credentialTarget : credentialId;
                        _credentialStore.Delete(deleteTarget);
                    }
                }

                _cachedAuth = new BridgeAuthContext
                {
                    Mode = "credentials",
                    CredentialId = null,
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

    public void ResetCachedAuth()
    {
        lock (_syncRoot)
        {
            _cachedAuth = null;
            _ignoreStoredCredentialsOnce = true;
        }
    }
}