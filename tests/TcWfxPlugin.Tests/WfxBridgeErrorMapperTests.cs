using TcWfxPlugin.Wfx;

namespace TcWfxPlugin.Tests;

public sealed class WfxBridgeErrorMapperTests
{
    [Theory]
    [InlineData(0, WfxResultCodes.Success)]
    [InlineData(1, WfxResultCodes.NotSupported)]
    [InlineData(2, WfxResultCodes.FileNotFound)]
    [InlineData(3, WfxResultCodes.AccessDenied)]
    [InlineData(4, WfxResultCodes.FileNotFound)]
    [InlineData(5, WfxResultCodes.UnknownError)]
    [InlineData(400, WfxResultCodes.FileNotFound)]
    [InlineData(401, WfxResultCodes.AccessDenied)]
    [InlineData(403, WfxResultCodes.AccessDenied)]
    [InlineData(404, WfxResultCodes.FileNotFound)]
    [InlineData(999, WfxResultCodes.UnknownError)]
    public void MapError_ReturnsExpectedResultCode(int bridgeErrorCode, int expected)
    {
        var result = WfxBridgeErrorMapper.MapError(bridgeErrorCode);

        Assert.Equal(expected, result);
    }
}
