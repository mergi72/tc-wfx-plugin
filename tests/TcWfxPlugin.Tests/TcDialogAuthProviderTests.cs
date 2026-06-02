using TcWfxPlugin.Wfx;

namespace TcWfxPlugin.Tests;

public sealed class TcDialogAuthProviderTests
{
    [Fact]
    public void GetAuthContext_CredentialsMode_UsesTcDialogWhenEnvMissing()
    {
        var oldMode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE");
        var oldUser = Environment.GetEnvironmentVariable("TC_WFX_USERNAME");
        var oldPassword = Environment.GetEnvironmentVariable("TC_WFX_PASSWORD");

        Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", "credentials");
        Environment.SetEnvironmentVariable("TC_WFX_USERNAME", null);
        Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", null);

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
            }, (_, _) => false, store, "tc-wfx/bridge");

            var auth = provider.GetAuthContext();

            Assert.Equal("credentials", auth.Mode);
            Assert.Equal("tc-user", auth.Username);
            Assert.Equal("tc-pass", auth.Password);
            Assert.False(store.WasSaved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", oldMode);
            Environment.SetEnvironmentVariable("TC_WFX_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", oldPassword);
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

        Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", "credentials");
        Environment.SetEnvironmentVariable("TC_WFX_USERNAME", null);
        Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", null);

        try
        {
            var store = new FakeCredentialStore();
            var provider = new TcDialogAuthProvider(
                (requestType, _, _) => requestType == WfxNativeExports.RequestTypeUserName ? "saved-user" : "saved-pass",
                (_, _) => true,
                store,
                "tc-wfx/bridge");

            var auth = provider.GetAuthContext();

            Assert.Equal("credentials", auth.Mode);
            Assert.Equal("saved-user", auth.Username);
            Assert.Equal("saved-pass", auth.Password);
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
        }
    }

    [Fact]
    public void GetAuthContext_CredentialsMode_UsesStoredCredentialsBeforePrompt()
    {
        var oldMode = Environment.GetEnvironmentVariable("TC_WFX_AUTH_MODE");
        var oldUser = Environment.GetEnvironmentVariable("TC_WFX_USERNAME");
        var oldPassword = Environment.GetEnvironmentVariable("TC_WFX_PASSWORD");

        Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", "credentials");
        Environment.SetEnvironmentVariable("TC_WFX_USERNAME", null);
        Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", null);

        try
        {
            var store = new FakeCredentialStore
            {
                ShouldReadSucceed = true,
                ReadUserName = "stored-user",
                ReadPassword = "stored-pass",
            };

            var provider = new TcDialogAuthProvider(
                (_, _, _) => throw new InvalidOperationException("Prompt should not be used when credential store has values."),
                (_, _) => false,
                store,
                "tc-wfx/bridge");

            var auth = provider.GetAuthContext();

            Assert.Equal("credentials", auth.Mode);
            Assert.Equal("stored-user", auth.Username);
            Assert.Equal("stored-pass", auth.Password);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_AUTH_MODE", oldMode);
            Environment.SetEnvironmentVariable("TC_WFX_USERNAME", oldUser);
            Environment.SetEnvironmentVariable("TC_WFX_PASSWORD", oldPassword);
        }
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        public bool ShouldReadSucceed { get; set; }
        public string ReadUserName { get; set; } = string.Empty;
        public string ReadPassword { get; set; } = string.Empty;

        public bool WasSaved { get; private set; }
        public string LastSavedTarget { get; private set; } = string.Empty;
        public string LastSavedUserName { get; private set; } = string.Empty;
        public string LastSavedPassword { get; private set; } = string.Empty;

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
    }
}