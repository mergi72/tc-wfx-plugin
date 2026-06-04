namespace TcWfxPlugin.Wfx;

internal static class WfxBridgeErrorMapper
{
    public static int MapError(int bridgeErrorCode)
    {
        return bridgeErrorCode switch
        {
            // Bridge WfxErrorCode values from dms_provider_bridge.adapters.commander_api.WfxErrorCode
            0 => WfxResultCodes.Success,
            1 => WfxResultCodes.NotSupported,
            2 => WfxResultCodes.FileNotFound,
            3 => WfxResultCodes.AccessDenied,
            4 => WfxResultCodes.FileNotFound,
            5 => WfxResultCodes.UnknownError,

            // Fallback mapping for unexpected HTTP-like error codes
            400 => WfxResultCodes.FileNotFound,
            401 => WfxResultCodes.AccessDenied,
            403 => WfxResultCodes.AccessDenied,
            404 => WfxResultCodes.FileNotFound,
            _ => WfxResultCodes.UnknownError,
        };
    }
}
