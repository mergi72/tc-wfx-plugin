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
            var provider = new TcDialogAuthProvider((requestType, _, _) =>
            {
                return requestType switch
                {
                    WfxNativeExports.RequestTypeUserName => "tc-user",
                    WfxNativeExports.RequestTypePassword => "tc-pass",
                    _ => null,
                };
            });

            var auth = provider.GetAuthContext();

            Assert.Equal("credentials", auth.Mode);
            Assert.Equal("tc-user", auth.Username);
            Assert.Equal("tc-pass", auth.Password);
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
            var provider = new TcDialogAuthProvider((_, _, _) => throw new InvalidOperationException("Dialog should not be used in winuser mode."));

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
}