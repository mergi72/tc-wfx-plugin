namespace TcWfxPlugin.Wfx;

public sealed class WfxProgressReporterFactory : IWfxProgressReporterFactory
{
    public IWfxProgressReporter Create(
        IProgress<WfxTransferProgress>? progress,
        string operation,
        string sourcePath,
        string destinationPath,
        long? totalBytes)
    {
        return new WfxProgressReporter(progress, operation, sourcePath, destinationPath, totalBytes);
    }

    public IWfxProgressReporter CreateUnit(
        IProgress<WfxTransferProgress>? progress,
        string operation,
        string sourcePath,
        string destinationPath)
    {
        var reporter = new WfxProgressReporter(progress, operation, sourcePath, destinationPath, totalBytes: 1);
        reporter.Report(0);
        return reporter;
    }

    private sealed class WfxProgressReporter : IWfxProgressReporter
    {
        private readonly IProgress<WfxTransferProgress>? _progress;
        private readonly string _operation;
        private readonly string _sourcePath;
        private readonly string _destinationPath;
        private long? _totalBytes;
        private long _lastBytes;
        private bool _completed;

        public WfxProgressReporter(
            IProgress<WfxTransferProgress>? progress,
            string operation,
            string sourcePath,
            string destinationPath,
            long? totalBytes)
        {
            _progress = progress;
            _operation = operation;
            _sourcePath = sourcePath;
            _destinationPath = destinationPath;
            _totalBytes = totalBytes;
        }

        public void SetTotalBytes(long? totalBytes)
        {
            _totalBytes = totalBytes;
        }

        public void Report(long bytesTransferred)
        {
            if (_completed)
            {
                return;
            }

            var normalizedBytes = bytesTransferred < 0 ? 0 : bytesTransferred;
            if (_totalBytes is long total)
            {
                normalizedBytes = Math.Min(normalizedBytes, total);
            }

            _lastBytes = normalizedBytes;
            Emit(isCompleted: false);
        }

        public void Finish(bool success, long? bytesTransferred = null)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;

            if (bytesTransferred is long providedBytes)
            {
                _lastBytes = providedBytes;
            }

            if (success && _totalBytes is long total)
            {
                _lastBytes = total;
            }

            if (_lastBytes < 0)
            {
                _lastBytes = 0;
            }

            Emit(isCompleted: success);
        }

        public void Dispose()
        {
            if (!_completed)
            {
                Finish(success: false);
            }
        }

        private void Emit(bool isCompleted)
        {
            _progress?.Report(new WfxTransferProgress
            {
                Operation = _operation,
                SourcePath = _sourcePath,
                DestinationPath = _destinationPath,
                BytesTransferred = _lastBytes,
                TotalBytes = _totalBytes,
                IsCompleted = isCompleted,
            });
        }
    }
}
