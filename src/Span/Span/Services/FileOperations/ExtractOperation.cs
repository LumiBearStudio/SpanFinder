using System.IO.Compression;
using System.Threading;

namespace Span.Services.FileOperations;

/// <summary>
/// ZIP 압축 해제 작업.
/// FileOperationManager를 통해 백그라운드 실행, 진행률/일시정지/취소 지원.
/// 스트림 기반 해제로 바이트 단위 실시간 progress 보고.
/// </summary>
public class ExtractOperation : IFileOperation, IPausableOperation
{
    private readonly string _zipPath;
    private readonly string _destinationPath;
    private ManualResetEventSlim? _pauseEvent;

    private const int BufferSize = 1048576; // 1MB

    public ExtractOperation(string zipPath, string destinationPath)
    {
        _zipPath = zipPath;
        _destinationPath = destinationPath;
    }

    public string Description => LocalizationService.L("Op_ExtractFrom") is string s && s != "Op_ExtractFrom"
        ? string.Format(s, Path.GetFileName(_zipPath))
        : $"Extract '{Path.GetFileName(_zipPath)}'";

    public bool CanUndo => true;

    public void SetPauseEvent(ManualResetEventSlim pauseEvent) => _pauseEvent = pauseEvent;

    public async Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(_destinationPath);

                using var archive = ZipFile.OpenRead(_zipPath);

                // Calculate total bytes from entries
                long totalBytes = 0;
                var fileEntries = new List<ZipArchiveEntry>();
                foreach (var entry in archive.Entries)
                {
                    totalBytes += entry.Length;
                    fileEntries.Add(entry);
                }

                long processedBytes = 0;
                int current = 0;
                var startTime = DateTime.Now;
                long lastReportTick = Environment.TickCount64;
                var buffer = new byte[BufferSize];

                foreach (var entry in fileEntries)
                {
                    FileOperationHelpers.WaitIfPaused(_pauseEvent, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    string fullPath;
                    try
                    {
                        fullPath = Path.GetFullPath(Path.Combine(_destinationPath, entry.FullName));
                    }
                    catch (PathTooLongException)
                    {
                        continue;
                    }

                    // Security: prevent path traversal
                    if (!fullPath.StartsWith(Path.GetFullPath(_destinationPath), StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(fullPath);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

                        // Stream-based extraction with per-byte progress reporting
                        using var entryStream = entry.Open();
                        using var destStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write,
                            FileShare.None, BufferSize, FileOptions.SequentialScan);

                        int bytesRead;
                        while ((bytesRead = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            FileOperationHelpers.WaitIfPaused(_pauseEvent, cancellationToken);
                            cancellationToken.ThrowIfCancellationRequested();

                            destStream.Write(buffer, 0, bytesRead);
                            processedBytes += bytesRead;

                            long now = Environment.TickCount64;
                            if (now - lastReportTick >= FileOperationHelpers.ProgressReportIntervalMs)
                            {
                                FileOperationHelpers.ReportProgress(
                                    progress, entry.FullName,
                                    current, fileEntries.Count,
                                    processedBytes, totalBytes, startTime);
                                lastReportTick = now;
                            }
                        }
                    }

                    current++;
                    FileOperationHelpers.ReportProgress(
                        progress, entry.FullName,
                        current - 1, fileEntries.Count,
                        processedBytes, totalBytes, startTime);
                }

                return OperationResult.CreateSuccess(_destinationPath);
            }
            catch (OperationCanceledException)
            {
                return OperationResult.CreateFailure("Operation cancelled");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure(ex.Message);
            }
        }, cancellationToken);
    }

    public Task<OperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (Directory.Exists(_destinationPath))
            {
                Directory.Delete(_destinationPath, recursive: true);
                return Task.FromResult(OperationResult.CreateSuccess(_destinationPath));
            }
            return Task.FromResult(OperationResult.CreateFailure("Extracted folder does not exist"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.CreateFailure(ex.Message));
        }
    }
}
