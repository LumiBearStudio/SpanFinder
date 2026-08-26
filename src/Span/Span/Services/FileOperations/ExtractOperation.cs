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

    public string Description => string.Format(LocalizationService.L("FileOp_Extract"), Path.GetFileName(_zipPath));

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

                // Issue #63 후속: 목적지 루트를 구분자까지 포함해 계산한다.
                // 구분자 없이 StartsWith로 비교하면 "C:\dest"와 "C:\dest2\x.txt"가 매칭되어
                // 대상 폴더 밖에 파일을 쓰는 path traversal이 통과한다(실측 확인).
                var destRoot = Path.GetFullPath(_destinationPath)
                                   .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                int skipped = 0;
                int extracted = 0;
                var errors = new List<string>();

                foreach (var entry in fileEntries)
                {
                    FileOperationHelpers.WaitIfPaused(_pauseEvent, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    // Issue #63 후속: 항목별로 예외를 격리한다. 기존에는 항목 하나가 실패하면
                    // (미지원 압축 방식, 금지 문자 파일명 등) 루프 전체가 중단되어 나머지가
                    // 통째로 누락됐다.
                    try
                    {
                        string fullPath;
                        try
                        {
                            fullPath = Path.GetFullPath(Path.Combine(_destinationPath, entry.FullName));
                        }
                        catch (PathTooLongException)
                        {
                            skipped++;
                            Span.Helpers.DebugLogger.Log($"[Extract] skipped (path too long): {entry.FullName}");
                            continue;
                        }

                        // Security: prevent path traversal
                        if (!fullPath.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            skipped++;
                            Span.Helpers.DebugLogger.Log($"[Extract] skipped (outside destination): {entry.FullName}");
                            continue;
                        }

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
                            extracted++;
                        }
                    }
                    catch (OperationCanceledException) { throw; } // 취소는 전체 중단이 맞다
                    catch (Exception ex)
                    {
                        errors.Add($"{entry.FullName}: {ex.Message}");
                        Span.Helpers.DebugLogger.Log($"[Extract] entry failed: {entry.FullName} — {ex.GetType().Name}: {ex.Message}");
                    }

                    current++;
                    FileOperationHelpers.ReportProgress(
                        progress, entry.FullName,
                        current - 1, fileEntries.Count,
                        processedBytes, totalBytes, startTime);
                }

                // Issue #63 후속: 전부 건너뛰거나 실패해 실제로 만든 파일이 없으면
                // 성공으로 보고하지 않는다 (기존에는 빈 폴더 + 성공 토스트가 떴다).
                if (extracted == 0 && (skipped > 0 || errors.Count > 0))
                {
                    return OperationResult.CreateFailure(
                        errors.Count > 0
                            ? string.Join("\n", errors)
                            : LocalizationService.L("Op_SomeNotExtracted"));
                }

                var result = OperationResult.CreateSuccess(_destinationPath);
                FileOperationHelpers.FinalizeResultWithErrors(result, errors, "Op_SomeNotExtracted");
                if (skipped > 0)
                    Span.Helpers.DebugLogger.Log($"[Extract] completed with {skipped} skipped entry(ies)");
                return result;
            }
            catch (OperationCanceledException)
            {
                return OperationResult.CreateFailure(LocalizationService.L("Toast_OperationCancelled"));
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
            return Task.FromResult(OperationResult.CreateFailure(LocalizationService.L("FileOp_ExtractedFolderNotExist")));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.CreateFailure(ex.Message));
        }
    }
}
