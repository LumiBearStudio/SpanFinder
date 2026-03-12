using System.IO.Compression;
using System.Threading;

namespace Span.Services.FileOperations;

/// <summary>
/// ZIP 압축 작업. 파일 또는 폴더를 ZIP으로 압축.
/// FileOperationManager를 통해 백그라운드 실행, 진행률/일시정지/취소 지원.
/// 스트림 기반 압축으로 바이트 단위 실시간 progress 보고.
/// </summary>
public class CompressOperation : IFileOperation, IPausableOperation
{
    private readonly string[] _sourcePaths;
    private readonly string _zipPath;
    private ManualResetEventSlim? _pauseEvent;

    // 1MB buffer for high throughput (matches CopyFileOperation)
    private const int BufferSize = 1048576;

    public CompressOperation(string[] sourcePaths, string zipPath)
    {
        _sourcePaths = sourcePaths;
        _zipPath = zipPath;
    }

    public string Description => LocalizationService.L("Op_CompressTo") is string s && s != "Op_CompressTo"
        ? string.Format(s, Path.GetFileName(_zipPath))
        : $"Compress to '{Path.GetFileName(_zipPath)}'";

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
                // Pre-calculate total bytes for accurate progress
                var allFiles = new List<(string FullPath, string RelativePath, long Size)>();
                foreach (var sourcePath in _sourcePaths)
                {
                    if (File.Exists(sourcePath))
                    {
                        var fi = new FileInfo(sourcePath);
                        allFiles.Add((sourcePath, Path.GetFileName(sourcePath), fi.Length));
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        var parentDir = Path.GetDirectoryName(sourcePath)!;
                        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                var fi = new FileInfo(file);
                                var relativePath = Path.GetRelativePath(parentDir, file);
                                allFiles.Add((file, relativePath, fi.Length));
                            }
                            catch (PathTooLongException) { }
                        }
                    }
                }

                long totalBytes = 0;
                foreach (var f in allFiles) totalBytes += f.Size;
                long processedBytes = 0;
                var startTime = DateTime.Now;
                long lastReportTick = Environment.TickCount64;

                using var zipStream = new FileStream(_zipPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, BufferSize, FileOptions.SequentialScan);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

                var buffer = new byte[BufferSize];

                for (int i = 0; i < allFiles.Count; i++)
                {
                    FileOperationHelpers.WaitIfPaused(_pauseEvent, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    var (fullPath, relativePath, size) = allFiles[i];

                    try
                    {
                        var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);

                        // Stream-based compression with per-byte progress reporting
                        using var sourceStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                            FileShare.Read, BufferSize, FileOptions.SequentialScan);
                        using var entryStream = entry.Open();

                        int bytesRead;
                        while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            FileOperationHelpers.WaitIfPaused(_pauseEvent, cancellationToken);
                            cancellationToken.ThrowIfCancellationRequested();

                            entryStream.Write(buffer, 0, bytesRead);
                            processedBytes += bytesRead;

                            // Throttled progress reporting (every 100ms) to avoid UI thread saturation
                            long now = Environment.TickCount64;
                            if (now - lastReportTick >= FileOperationHelpers.ProgressReportIntervalMs)
                            {
                                FileOperationHelpers.ReportProgress(
                                    progress, Path.GetFileName(fullPath),
                                    i, allFiles.Count,
                                    processedBytes, totalBytes, startTime);
                                lastReportTick = now;
                            }
                        }
                    }
                    catch (PathTooLongException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) when (!cancellationToken.IsCancellationRequested) { }

                    // Report after each file completion (ensures progress updates for small files)
                    FileOperationHelpers.ReportProgress(
                        progress, Path.GetFileName(fullPath),
                        i, allFiles.Count,
                        processedBytes, totalBytes, startTime);
                }

                return OperationResult.CreateSuccess(_zipPath);
            }
            catch (OperationCanceledException)
            {
                try { if (File.Exists(_zipPath)) File.Delete(_zipPath); } catch { }
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
            if (File.Exists(_zipPath))
            {
                File.Delete(_zipPath);
                return Task.FromResult(OperationResult.CreateSuccess(_zipPath));
            }
            return Task.FromResult(OperationResult.CreateFailure("ZIP file does not exist"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.CreateFailure(ex.Message));
        }
    }
}
