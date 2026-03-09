using System.IO.Compression;
using System.Threading;

namespace Span.Services.FileOperations;

/// <summary>
/// ZIP 압축 작업. 파일 또는 폴더를 ZIP으로 압축.
/// FileOperationManager를 통해 백그라운드 실행, 진행률/일시정지/취소 지원.
/// </summary>
public class CompressOperation : IFileOperation, IPausableOperation
{
    private readonly string[] _sourcePaths;
    private readonly string _zipPath;
    private ManualResetEventSlim? _pauseEvent;

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

                using var archive = ZipFile.Open(_zipPath, ZipArchiveMode.Create);

                for (int i = 0; i < allFiles.Count; i++)
                {
                    FileOperationHelpers.WaitIfPaused(_pauseEvent, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    var (fullPath, relativePath, size) = allFiles[i];

                    try
                    {
                        archive.CreateEntryFromFile(fullPath, relativePath, CompressionLevel.Optimal);
                    }
                    catch (PathTooLongException) { }

                    processedBytes += size;
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
