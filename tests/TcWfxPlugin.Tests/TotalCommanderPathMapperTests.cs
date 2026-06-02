using TcWfxPlugin.Core;

namespace TcWfxPlugin.Tests;

public sealed class TotalCommanderPathMapperTests
{
    [Theory]
    [InlineData("edocat:/", "edocat:/")]
    [InlineData("\\edocat", "edocat:/")]
    [InlineData("\\edocat\\folder\\a", "edocat:/folder/a")]
    [InlineData("alfresco/sites/demo", "alfresco:/sites/demo")]
    public void TryToProviderPath_ValidInput_ReturnsExpectedPath(string input, string expected)
    {
        var result = TotalCommanderPathMapper.TryToProviderPath(input, out var providerPath);

        Assert.True(result);
        Assert.Equal(expected, providerPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("\\")]
    public void TryToProviderPath_InvalidInput_ReturnsFalse(string input)
    {
        var result = TotalCommanderPathMapper.TryToProviderPath(input, out _);

        Assert.False(result);
    }
}
