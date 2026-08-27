using System.IO.Compression;
using System.IO.Hashing;
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

                // ZipFile.OpenRead는 인코딩을 지정할 수 없다. 한국 도구들이 만드는 ZIP은
                // CP949 이름을 EFS 비트 없이 기록해 UTF-8로 오디코딩되므로
                // (반디집·윈도우 탐색기 모두 해당, 실측 확인) 항목별 자동 판별을 쓴다.
                using var archiveStream = new FileStream(
                    _zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var archive = new ZipArchive(
                    archiveStream, ZipArchiveMode.Read, leaveOpen: false,
                    entryNameEncoding: Span.Helpers.ZipEntryNameEncoding.Instance);

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
                        // 엔트리 이름의 ':'는 NTFS 대체 데이터 스트림(ADS)으로 해석된다.
                        // "installer.exe:Zone.Identifier"는 경로가 대상 폴더 안이라 아래
                        // traversal 검사를 통과하고, FileStream이 이를 ADS로 기록한다
                        // (실측: 0바이트 본체 + 사용자에게 보이지 않는 스트림).
                        // Windows에서 ':'는 파일명에 쓸 수 없으므로 정상 엔트리는 걸리지 않는다.
                        if (entry.FullName.Contains(':'))
                        {
                            skipped++;
                            Span.Helpers.DebugLogger.Log($"[Extract] skipped (alternate data stream): {entry.FullName}");
                            continue;
                        }

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

                            var crc = new Crc32();

                            // Stream-based extraction with per-byte progress reporting.
                            // CRC 대조를 위해 파일을 닫은 뒤 판정해야 하므로 using 블록으로 감싼다.
                            using (var entryStream = entry.Open())
                            using (var destStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write,
                                FileShare.None, BufferSize, FileOptions.SequentialScan))
                            {
                                int bytesRead;
                                while ((bytesRead = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    FileOperationHelpers.WaitIfPaused(_pauseEvent, cancellationToken);
                                    cancellationToken.ThrowIfCancellationRequested();

                                    destStream.Write(buffer, 0, bytesRead);
                                    crc.Append(buffer.AsSpan(0, bytesRead));
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

                            // .NET ZipArchive의 읽기 경로는 CRC32를 검증하지 않는다 — 비트 썩음이나
                            // 다운로드 손상이 있는 ZIP을 예외 없이 "성공"으로 풀어버린다(실측 확인).
                            // 중앙 디렉터리의 CRC와 대조해 손상을 잡는다.
                            // 중앙 디렉터리 CRC가 0인 비정상 아카이브는 검증을 건너뛴다
                            // (지금까지 정상적으로 풀리던 파일을 오탐으로 막지 않기 위함).
                            bool crcOk = true;
                            if (entry.Crc32 != 0 || entry.Length == 0)
                            {
                                uint actual = crc.GetCurrentHashAsUInt32();
                                if (actual != entry.Crc32)
                                {
                                    crcOk = false;
                                    // 손상된 데이터를 남기지 않는다. 토스트는 오류를 3건까지만 보여주므로
                                    // 파일을 남기면 4번째 이후는 손상된 채 조용히 디스크에 남는다.
                                    try { File.Delete(fullPath); }
                                    catch (Exception delEx)
                                    {
                                        Span.Helpers.DebugLogger.Log(
                                            $"[Extract] failed to remove corrupted file {fullPath}: {delEx.GetType().Name}");
                                    }
                                    errors.Add($"{entry.FullName}: {LocalizationService.L("Op_CrcMismatch")}");
                                    Span.Helpers.DebugLogger.Log(
                                        $"[Extract] CRC mismatch: {entry.FullName} — expected 0x{entry.Crc32:X8}, actual 0x{actual:X8}");
                                }
                            }

                            if (crcOk) extracted++;
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
