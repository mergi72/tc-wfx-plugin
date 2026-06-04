namespace TcWfxPlugin.Wfx;

public interface IWfxProgressReporter : IDisposable
{
    void SetTotalBytes(long? totalBytes);
    void Report(long bytesTransferred);
    void Finish(bool success, long? bytesTransferred = null);
}
