using TcWfxPlugin.Wfx;

namespace TcWfxPlugin.Tests;

public sealed class VfsDiagnosticLogFormatterTests
{
    [Fact]
    public void Format_UsesVfsPlatformLogShape()
    {
        var timestamp = new DateTime(2026, 8, 19, 22, 30, 0, 772, DateTimeKind.Local);

        var result = VfsDiagnosticLogFormatter.Format(timestamp, "FsStatusInfoW status=ok");

        Assert.Equal(
            "2026-08-19 22:30:00,772 DEBUG tc-wfx: FsStatusInfoW status=ok",
            result);
    }
}
