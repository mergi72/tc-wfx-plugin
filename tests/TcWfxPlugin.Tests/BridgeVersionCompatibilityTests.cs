using TcWfxPlugin.Bridge;

namespace TcWfxPlugin.Tests;

public sealed class BridgeVersionCompatibilityTests
{
    [Theory]
    [InlineData("0.2.0", "0.2.0")]
    [InlineData("0.2.1", "0.2.0")]
    [InlineData("1.0.0", "0.2.0")]
    [InlineData("0.2.0-beta.1", "0.2.0")]
    public void IsSupported_ReturnsTrue_ForCompatibleVersions(string bridgeVersion, string minimumVersion)
    {
        var supported = BridgeVersionCompatibility.IsSupported(bridgeVersion, minimumVersion, out var reason);

        Assert.True(supported);
        Assert.Equal(string.Empty, reason);
    }

    [Theory]
    [InlineData("0.1.9", "0.2.0")]
    [InlineData("", "0.2.0")]
    [InlineData("not-a-version", "0.2.0")]
    [InlineData("0.2.0", "bad-minimum")]
    public void IsSupported_ReturnsFalse_ForIncompatibleOrInvalidVersions(string bridgeVersion, string minimumVersion)
    {
        var supported = BridgeVersionCompatibility.IsSupported(bridgeVersion, minimumVersion, out var reason);

        Assert.False(supported);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }
}
