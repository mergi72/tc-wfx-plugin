using TcWfxPlugin.Core;

namespace TcWfxPlugin.Tests;

public sealed class ProviderPathTests
{
    [Theory]
    [InlineData("edocat:/")]
    [InlineData("edocat:/folder")]
    [InlineData("alfresco:/sites/demo/documentLibrary")]
    public void TryParse_ValidPath_ReturnsTrue(string value)
    {
        var result = ProviderPath.TryParse(value, out var providerPath);

        Assert.True(result);
        Assert.Equal(value, providerPath.ToString());
    }

    [Fact]
    public void TryParse_MixedCaseProvider_NormalizesToLowercase()
    {
        var result = ProviderPath.TryParse("Alfresco:/folder", out var providerPath);

        Assert.True(result);
        Assert.Equal("alfresco:/folder", providerPath.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("edocat")]
    [InlineData(":/folder")]
    [InlineData("edocat:folder")]
    public void TryParse_InvalidPath_ReturnsFalse(string value)
    {
        var result = ProviderPath.TryParse(value, out _);

        Assert.False(result);
    }
}
