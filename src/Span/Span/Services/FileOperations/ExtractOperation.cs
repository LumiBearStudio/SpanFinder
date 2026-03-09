using System.IO.Compression;
using System.Threading;

namespace Span.Services.FileOperations;

/// <summary>
/// ZIP 압축 해제 작업.
/// FileOperationManager를 통해 백그라운드 실행, 진행률/일시정지/취소 지원.
/// </summary>
public class ExtractOperation : IFileOperation, IPausableOperation
{
    private readonly string _zipPath;
    private readonly string _destinationPath;
    private ManualResetEventSlim? _pauseEvent;

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
                        entry.ExtractToFile(fullPath, overwrite: true);
                    }

                    processedBytes += entry.Length;
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
