using TcWfxPlugin.Contracts;
using TcWfxPlugin.Wfx;

namespace TcWfxPlugin.Tests;

public sealed class TcDialogAuthProviderTests
{
    [Fact]
    public void GetAuthContext_DefaultMode_UsesCredentialsWithCredentialId()
    {
        var oldMode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE");
        var oldCredentialId = Environment.GetEnvironmentVariable("TC_WFX_CREDENTIAL_ID");

        Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", null);
        Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", "tc-wfx/bridge");

        try
        {
            var store = new FakeCredentialStore
            {
                ShouldReadSucceed = true,
                ReadUserName = "stored-user",
                ReadPassword = "stored-pass",
            };

            var provider = new TcDialogAuthProvider(
                (_, _, _) => throw new InvalidOperationException("Prompt should not be used when stored credential exists."),
                (_, _) => throw new InvalidOperationException("Yes/No prompt should not be used when stored credential exists."),
                store,
                "tc-wfx/bridge");

            var auth = provider.GetAuthContext();

            Assert.Equal("credentials", auth.Mode);
            Assert.Equal("tc-wfx/bridge", auth.CredentialId);
            Assert.Null(auth.Username);
            Assert.Null(auth.Password);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", oldMode);
            Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", oldCredentialId);
        }
    }

    [Fact]
    public void GetAuthContext_CredentialsMode_UsesTcDialogWhenEnvMissing()
    {
        var oldMode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE");
        var oldUser = Environment.GetEnvironmentVariable("TC_WFX_USERNAME");
        var oldPassword = Environment.GetEnvironmentVariable("TC_WFX_PASSWORD");
        var oldCredentialId = Environment.GetEnvironmentVariable("TC_WFX_CREDENTIAL_ID");

        Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", "credentials");
        Environment.SetEnvironmentVariable("TC_WFX_USERNAME", null);
        Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", null);
        Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", null);

        try
        {
            var store = new FakeCredentialStore();
            var provider = new TcDialogAuthProvider((requestType, _, _) =>
            {
                return requestType switch
                {
                    WfxNativeExports.RequestTypeUserName => "tc-user",
                    WfxNativeExports.RequestTypePassword => "tc-pass",
                    _ => null,
                };
            }, (_, _) => false, store, string.Empty);

            var auth = provider.GetAuthContext();

            Assert.Equal("credentials", auth.Mode);
            Assert.Null(auth.CredentialId);
            Assert.Equal("tc-user", auth.Username);
            Assert.Equal("tc-pass", auth.Password);
            Assert.False(store.WasSaved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", oldMode);
            Environment.SetEnvironmentVariable("TC_WFX_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", oldPassword);
            Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", oldCredentialId);
        }
    }

    [Fact]
    public void GetAuthContext_WinUserMode_UsesWinUserWithoutDialog()
    {
        var oldMode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE");
        var oldWinUser = Environment.GetEnvironmentVariable("TC_WFX_WIN_USER");

        Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", "winuser");
        Environment.SetEnvironmentVariable("TC_WFX_WIN_USER", "bridge-user");

        try
        {
            var provider = new TcDialogAuthProvider(
                (_, _, _) => throw new InvalidOperationException("Dialog should not be used in winuser mode."),
                (_, _) => throw new InvalidOperationException("Yes/No dialog should not be used in winuser mode."),
                new FakeCredentialStore(),
                "tc-wfx/bridge");

            var auth = provider.GetAuthContext();

            Assert.Equal("winuser", auth.Mode);
            Assert.Equal("bridge-user", auth.WinUser);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", oldMode);
            Environment.SetEnvironmentVariable("TC_WFX_WIN_USER", oldWinUser);
        }
    }

    [Fact]
    public void GetAuthContext_CredentialsMode_RememberLogin_SavesToCredentialStore()
    {
        var oldMode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE");
        var oldUser = Environment.GetEnvironmentVariable("TC_WFX_USERNAME");
        var oldPassword = Environment.GetEnvironmentVariable("TC_WFX_PASSWORD");
        var oldCredentialId = Environment.GetEnvironmentVariable("TC_WFX_CREDENTIAL_ID");

        Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", "credentials");
        Environment.SetEnvironmentVariable("TC_WFX_USERNAME", null);
        Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", null);
        Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", null);

        try
        {
            var store = new FakeCredentialStore();
            var provider = new TcDialogAuthProvider(
                (requestType, _, _) => requestType == WfxNativeExports.RequestTypeUserName ? "saved-user" : "saved-pass",
                (_, _) => true,
                store,
                "tc-wfx/bridge");

            provider.ResetCachedAuth();

            var auth = provider.GetAuthContext();

            Assert.Equal("credentials", auth.Mode);
            Assert.Equal("tc-wfx/bridge", auth.CredentialId);
            Assert.Null(auth.Username);
            Assert.Null(auth.Password);
            Assert.True(store.WasSaved);
            Assert.Equal("tc-wfx/bridge", store.LastSavedTarget);
            Assert.Equal("saved-user", store.LastSavedUserName);
            Assert.Equal("saved-pass", store.LastSavedPassword);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", oldMode);
            Environment.SetEnvironmentVariable("TC_WFX_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", oldPassword);
            Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", oldCredentialId);
        }
    }

    [Fact]
    public void GetAuthContext_CredentialsMode_UsesStoredCredentialsBeforePrompt()
    {
        var oldMode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE");
        var oldUser = Environment.GetEnvironmentVariable("TC_WFX_USERNAME");
        var oldPassword = Environment.GetEnvironmentVariable("TC_WFX_PASSWORD");
        var oldCredentialId = Environment.GetEnvironmentVariable("TC_WFX_CREDENTIAL_ID");

        Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", "credentials");
        Environment.SetEnvironmentVariable("TC_WFX_USERNAME", null);
        Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", null);
        Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", "tc-wfx/bridge");

        try
        {
            var store = new FakeCredentialStore
            {
                ShouldReadSucceed = true,
                ReadUserName = "stored-user",
                ReadPassword = "stored-pass",
            };

            var provider = new TcDialogAuthProvider(
                (_, _, _) => throw new InvalidOperationException("Prompt should not be used when stored credentials are accepted."),
                (_, _) => throw new InvalidOperationException("Yes/No prompt should not be used when stored credentials exist."),
                store,
                "tc-wfx/bridge");

            var auth = provider.GetAuthContext();

            Assert.Equal("credentials", auth.Mode);
            Assert.Equal("tc-wfx/bridge", auth.CredentialId);
            Assert.Null(auth.Username);
            Assert.Null(auth.Password);
            Assert.False(store.WasDeleted);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", oldMode);
            Environment.SetEnvironmentVariable("TC_WFX_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", oldPassword);
            Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", oldCredentialId);
        }
    }

    [Fact]
    public void ResetCachedAuth_CredentialsMode_IgnoresStoredCredentialsOnceAndPromptsAgain()
    {
        var oldMode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE");
        var oldUser = Environment.GetEnvironmentVariable("TC_WFX_USERNAME");
        var oldPassword = Environment.GetEnvironmentVariable("TC_WFX_PASSWORD");
        var oldCredentialId = Environment.GetEnvironmentVariable("TC_WFX_CREDENTIAL_ID");

        Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", "credentials");
        Environment.SetEnvironmentVariable("TC_WFX_USERNAME", null);
        Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", null);
        Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", "tc-wfx/bridge");

        try
        {
            var store = new FakeCredentialStore
            {
                ShouldReadSucceed = true,
                ReadUserName = "stored-user",
                ReadPassword = "stored-pass",
            };

            var requestCalls = 0;
            var provider = new TcDialogAuthProvider(
                (requestType, _, _) =>
                {
                    requestCalls++;
                    return requestType == WfxNativeExports.RequestTypeUserName ? "prompt-user" : "prompt-pass";
                },
                (_, _) => false,
                store,
                "tc-wfx/bridge");

            var first = provider.GetAuthContext();
            provider.ResetCachedAuth();
            var second = provider.GetAuthContext();

            Assert.Equal("tc-wfx/bridge", first.CredentialId);
            Assert.Null(first.Username);
            Assert.Null(first.Password);
            Assert.Null(second.CredentialId);
            Assert.Equal("prompt-user", second.Username);
            Assert.Equal("prompt-pass", second.Password);
            Assert.Equal(2, requestCalls);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", oldMode);
            Environment.SetEnvironmentVariable("TC_WFX_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", oldPassword);
            Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", oldCredentialId);
        }
    }


    [Fact]
    public void GetAuthContext_CredentialsMode_IncludesConnectionNameInPromptTitle()
    {
        var oldMode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE");
        var oldUser = Environment.GetEnvironmentVariable("TC_WFX_USERNAME");
        var oldPassword = Environment.GetEnvironmentVariable("TC_WFX_PASSWORD");
        var oldCredentialId = Environment.GetEnvironmentVariable("TC_WFX_CREDENTIAL_ID");

        Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", "credentials");
        Environment.SetEnvironmentVariable("TC_WFX_USERNAME", null);
        Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", null);
        Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", null);

        try
        {
            var titles = new List<string>();
            var provider = new TcDialogAuthProvider(
                (requestType, title, _) =>
                {
                    titles.Add(title);
                    return requestType == WfxNativeExports.RequestTypeUserName ? "tc-user" : "tc-pass";
                },
                (_, _) => false,
                new FakeCredentialStore(),
                string.Empty);

            _ = provider.GetAuthContext("webdav1");

            Assert.Equal(new[] { "Provider login - webdav1", "Provider login - webdav1" }, titles);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", oldMode);
            Environment.SetEnvironmentVariable("TC_WFX_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", oldPassword);
            Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", oldCredentialId);
        }
    }

    [Fact]
    public void GetAuthContext_CredentialsMode_UsesCredentialStoreWhenBrokerReturnsNoCredentials()
    {
        var oldMode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE");
        var oldUser = Environment.GetEnvironmentVariable("TC_WFX_USERNAME");
        var oldPassword = Environment.GetEnvironmentVariable("TC_WFX_PASSWORD");
        var oldCredentialId = Environment.GetEnvironmentVariable("TC_WFX_CREDENTIAL_ID");

        Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", "credentials");
        Environment.SetEnvironmentVariable("TC_WFX_USERNAME", null);
        Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", null);
        Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", null);

        try
        {
            var store = new FakeCredentialStore
            {
                ShouldReadSucceed = true,
                ReadUserName = "stored-user",
                ReadPassword = "stored-pass",
            };
            var requestCalls = 0;
            var provider = new TcDialogAuthProvider(
                (_, _, _) =>
                {
                    requestCalls++;
                    return "prompted";
                },
                (_, _) => false,
                store,
                "tc-wfx/bridge",
                new NullCredentialBrokerClient(),
                credentialTargetResolver: connection => connection == "edocat" ? "merhautr@cheminvest/eDoCat_Helper" : null);

            var auth = provider.GetAuthContext("edocat");

            Assert.Equal("credentials", auth.Mode);
            Assert.Null(auth.CredentialId);
            Assert.Equal("stored-user", auth.Username);
            Assert.Equal("stored-pass", auth.Password);
            Assert.Equal(0, requestCalls);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", oldMode);
            Environment.SetEnvironmentVariable("TC_WFX_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", oldPassword);
            Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", oldCredentialId);
        }
    }
    private sealed class FakeCredentialStore : ICredentialStore
    {
        public bool ShouldReadSucceed { get; set; }
        public string ReadUserName { get; set; } = string.Empty;
        public string ReadPassword { get; set; } = string.Empty;

        public bool WasSaved { get; private set; }
        public bool WasDeleted { get; private set; }
        public string LastSavedTarget { get; private set; } = string.Empty;
        public string LastSavedUserName { get; private set; } = string.Empty;
        public string LastSavedPassword { get; private set; } = string.Empty;
        public string LastDeletedTarget { get; private set; } = string.Empty;

        public bool TryRead(string target, out string username, out string password)
        {
            username = ReadUserName;
            password = ReadPassword;
            return ShouldReadSucceed;
        }

        public void Save(string target, string username, string password)
        {
            WasSaved = true;
            LastSavedTarget = target;
            LastSavedUserName = username;
            LastSavedPassword = password;
        }

        public void Delete(string target)
        {
            WasDeleted = true;
            LastDeletedTarget = target;
        }
    }
    private sealed class NullCredentialBrokerClient : ICredentialBrokerClient
    {
        public BridgeAuthContext? Resolve(CredentialBrokerAuthRequirement requirement, string? provider = null)
        {
            return null;
        }
    }

    private sealed class RecordingCredentialBrokerClient : ICredentialBrokerClient
    {
        public int ResolveCalls { get; private set; }
        public string? LastProvider { get; private set; }
        public string? LastTarget { get; private set; }

        public BridgeAuthContext? Resolve(CredentialBrokerAuthRequirement requirement, string? provider = null)
        {
            ResolveCalls++;
            LastProvider = provider;
            LastTarget = requirement.Target;
            return new BridgeAuthContext
            {
                Mode = "credentials",
                Username = "domain-user",
                Password = "domain-pass",
            };
        }
    }

    [Fact]
    public void GetAuthContext_CredentialsMode_UsesConnectionSpecificCredentialTarget()
    {
        var requests = new Queue<string>(new[] { "user-one", "secret-one", "user-two", "secret-two" });
        var store = new FakeCredentialStore();
        var provider = new TcDialogAuthProvider(
            (_, _, _) => requests.Dequeue(),
            (_, _) => true,
            store,
            "tc-wfx/bridge",
            null,
            languageProvider: null,
            credentialTargetResolver: connection => string.Equals(connection, "webdav1", StringComparison.OrdinalIgnoreCase)
                ? "tc-wfx/webdav"
                : null);

        var webdav = provider.GetAuthContext("webdav1");
        var alfresco = provider.GetAuthContext("alfresco");

        Assert.Equal("tc-wfx/webdav", webdav.CredentialId);
        Assert.Equal("tc-wfx/bridge", alfresco.CredentialId);
    }

    [Fact]
    public void GetAuthContext_CredentialsMode_BrokerResolveUsesCredentialTargetNotConnectionName()
    {
        var oldMode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE");
        var oldUser = Environment.GetEnvironmentVariable("TC_WFX_USERNAME");
        var oldPassword = Environment.GetEnvironmentVariable("TC_WFX_PASSWORD");
        var oldCredentialId = Environment.GetEnvironmentVariable("TC_WFX_CREDENTIAL_ID");

        Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", "credentials");
        Environment.SetEnvironmentVariable("TC_WFX_USERNAME", null);
        Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", null);
        Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", null);

        try
        {
            var broker = new RecordingCredentialBrokerClient();
            var provider = new TcDialogAuthProvider(
                (_, _, _) => throw new InvalidOperationException("Prompt should not be used when broker resolves credentials."),
                (_, _) => throw new InvalidOperationException("Yes/No prompt should not be used when broker resolves credentials."),
                new FakeCredentialStore(),
                "tc-wfx/bridge",
                broker,
                credentialTargetResolver: connection =>
                    string.Equals(connection, "alfresco", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(connection, "alfresco1", StringComparison.OrdinalIgnoreCase)
                        ? "tc-wfx/bridge"
                        : null);

            var alfresco = provider.GetAuthContext("alfresco");
            var alfresco1 = provider.GetAuthContext("alfresco1");

            Assert.Equal("domain-user", alfresco.Username);
            Assert.Equal("domain-user", alfresco1.Username);
            Assert.Equal(1, broker.ResolveCalls);
            Assert.Null(broker.LastProvider);
            Assert.Equal("tc-wfx/bridge", broker.LastTarget);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", oldMode);
            Environment.SetEnvironmentVariable("TC_WFX_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", oldPassword);
            Environment.SetEnvironmentVariable("TC_WFX_CREDENTIAL_ID", oldCredentialId);
        }
    }
}
