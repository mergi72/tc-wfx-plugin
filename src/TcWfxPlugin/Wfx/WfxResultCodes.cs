namespace TcWfxPlugin.Wfx;

public static class WfxResultCodes
{
    public const int Success = 0;
    public const int FileNotFound = 2;
    public const int AccessDenied = 5;
    public const int NoMoreFiles = 18;
    public const int ReadError = 30;
    public const int WriteError = 31;
    public const int NotSupported = 50;
    public const int UserAbort = 90;
    public const int UnknownError = 99;
}
