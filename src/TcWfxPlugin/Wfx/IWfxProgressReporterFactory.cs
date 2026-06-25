namespace TcWfxPlugin.Wfx;

public interface IWfxProgressReporterFactory
{
    IWfxProgressReporter Create(
        IProgress<WfxTransferProgress>? progress,
        string operation,
        string sourcePath,
        string destinationPath,
        long? totalBytes);

    IWfxProgressReporter CreateUnit(
        IProgress<WfxTransferProgress>? progress,
        string operation,
        string sourcePath,
        string destinationPath);

    IWfxProgressReporter CreateSynthetic(
        IProgress<WfxTransferProgress>? progress,
        string operation,
        string sourcePath,
        string destinationPath);
}
