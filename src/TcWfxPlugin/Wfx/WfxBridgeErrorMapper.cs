namespace TcWfxPlugin.Wfx;

internal static class WfxBridgeErrorMapper
{
    public static int MapError(int bridgeErrorCode)
    {
        return bridgeErrorCode switch
        {
            0 => WfxResultCodes.Success,
            400 => WfxResultCodes.AccessDenied,
            401 => WfxResultCodes.AccessDenied,
            403 => WfxResultCodes.AccessDenied,
            404 => WfxResultCodes.FileNotFound,
            _ => WfxResultCodes.UnknownError,
        };
    }
}
