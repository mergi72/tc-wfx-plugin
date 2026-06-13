using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Wfx;

public sealed class TcDialogAuthProvider : IWfxAuthProvider
{
    private readonly Func<int, string, string, string?> _requestValue;
    private readonly Func<string, string, bool> _requestYesNo;
    private readonly ICredentialStore _credentialStore;
    private readonly ICredentialBrokerClient? _credentialBrokerClient;
    private readonly string _credentialTarget;
    private readonly WfxLocalization _text;
    private readonly object _syncRoot = new();
    private BridgeAuthContext? _cachedAuth;
    private bool _ignoreStoredCredentialsOnce;

    public TcDialogAuthProvider(
        Func<int, string, string, string?> requestValue,
        Func<string, string, bool> requestYesNo,
        ICredentialStore credentialStore,
        string credentialTarget,
        ICredentialBrokerClient? credentialBrokerClient = null,
        Func<string?>? languageProvider = null,
        string? languageId = null)
    {
        _requestValue = requestValue;
        _requestYesNo = requestYesNo;
        _credentialStore = credentialStore;
        _credentialTarget = credentialTarget;
        _credentialBrokerClient = credentialBrokerClient;
        _text = languageId is not null
            ? WfxLocalization.ForLanguageId(languageId)
            : WfxLocalization.Current(languageProvider ?? (() => null));
    }

    public BridgeAuthContext GetAuthContext(string? provider = null)
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
                var hasDirectSecret = !string.IsNullOrWhiteSpace(username)
                    || !string.IsNullOrWhiteSpace(password)
                    || !string.IsNullOrWhiteSpace(token);

                if (!_ignoreStoredCredentialsOnce && !hasDirectSecret && !string.IsNullOrWhiteSpace(credentialId))
                {
                    var brokerAuth = TryResolveViaBroker(credentialId, provider);
                    if (brokerAuth is not null)
                    {
                        _cachedAuth = brokerAuth;
                        return _cachedAuth;
                    }

                    if (_credentialBrokerClient is null)
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
                }

                if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(token))
                {
                    username = _requestValue(
                        WfxNativeExports.RequestTypeUserName,
                        _text.ProviderLoginTitle,
                        _text.UserNamePrompt);
                    promptedForCredentials = true;
                }

                if (string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(token))
                {
                    password = _requestValue(
                        WfxNativeExports.RequestTypePassword,
                        _text.ProviderLoginTitle,
                        _text.PasswordPrompt);
                    promptedForCredentials = true;
                }

                _ignoreStoredCredentialsOnce = false;

                if (promptedForCredentials && !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                {
                    var remember = _requestYesNo(
                        _text.RememberLoginTitle,
                        _text.RememberLoginQuestion);

                    if (remember)
                    {
                        var saveTarget = string.IsNullOrWhiteSpace(credentialId) ? _credentialTarget : credentialId;
                        _credentialStore.Save(saveTarget, username, password);
                        credentialId = saveTarget;

                        if (_credentialBrokerClient is null)
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

    private BridgeAuthContext? TryResolveViaBroker(string credentialId, string? provider)
    {
        if (_credentialBrokerClient is null)
        {
            return null;
        }

        var resolved = _credentialBrokerClient.Resolve(
            new CredentialBrokerAuthRequirement
            {
                Mode = "windows",
                Target = credentialId,
                Required = true,
            },
            provider);

        if (resolved is null || !string.Equals(resolved.Mode, "credentials", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(resolved.Username) && string.IsNullOrWhiteSpace(resolved.Password) && string.IsNullOrWhiteSpace(resolved.Token))
        {
            return null;
        }

        return new BridgeAuthContext
        {
            Mode = "credentials",
            CredentialId = null,
            Username = resolved.Username,
            Password = resolved.Password,
            Token = resolved.Token,
        };
    }
}


