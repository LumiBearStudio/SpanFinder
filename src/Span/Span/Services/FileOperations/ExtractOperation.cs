using System.IO.Compression;
using System.IO.Hashing;
using System.Threading;

namespace Span.Services.FileOperations;

/// <summary>
/// 압축 해제 작업.
/// FileOperationManager를 통해 백그라운드 실행, 진행률/일시정지/취소 지원.
///
/// 엔진이 둘이다:
///   .zip  — System.IO.Compression. 스트림 기반으로 1MB마다 진행률 보고와
///           취소·일시정지 확인을 하고, 항목별로 예외를 격리한다(Issue #63 후속).
///   그 외 — OtterZip 네이티브 엔진(.7z / .tar.* / .cab 등, Issue #66).
///
/// ZIP 경로는 의도적으로 교체하지 않는다. 위 세 가지 성질은 네이티브 엔진이
/// 재현하지 못하고(취소는 항목 경계에서만 관측된다), .zip은 이미 잘 동작하고 있어
/// 바꿀 이유가 없다. 이렇게 분리해 두면 새 포맷 지원이 기존 ZIP 사용자에게
/// 구조적으로 회귀를 일으킬 수 없다.
///
/// 엔진 분기를 별도 IFileOperation 클래스가 아니라 이 클래스 안에 둔 이유:
/// MainViewModel.FileOperations.cs의 라우팅이 타입 나열로 되어 있어, 신규 클래스를
/// 거기 등록하지 않으면 컴파일도 되고 추출도 성공하지만 진행률 패널에 바인딩되지
/// 않은 채 취소 버튼 없는 무응답 UI가 된다. Undo 규약도 그대로 상속된다.
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

    /// <summary>
    /// .zip은 아래 System.IO.Compression 경로가, 나머지는 네이티브 엔진이 처리한다.
    /// 확장자로만 판단한다 — 내용 기반 감지는 .zip을 네이티브 경로로 흘려보낼 수 있고
    /// 그건 이 분리의 목적에 반한다.
    /// </summary>
    private bool IsZipFormat =>
        Path.GetExtension(_zipPath).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    public async Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Task.Run에 토큰을 넘기지 않는다. 넘기면 이미 취소된 토큰일 때 델리게이트를
        // 실행조차 하지 않고 TaskCanceledException을 던지는데, 이 클래스의 계약은
        // 취소를 예외가 아니라 OperationResult로 돌려주는 것이다.
        // 취소 확인은 ExtractWithNativeEngine 안에서 한다.
        if (!IsZipFormat)
            return await Task.Run(() => ExtractWithNativeEngine(progress, cancellationToken));

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

    /// <summary>
    /// 비-ZIP 포맷을 OtterZip 네이티브 엔진으로 푼다 (Issue #66).
    ///
    /// ZIP 경로와 두 가지가 다르고, 둘 다 네이티브 API의 한계에서 온다:
    ///   - 진행률과 취소가 항목 경계에서만 관측된다. 큰 항목 하나를 푸는 동안에는
    ///     신호가 없고 취소도 그 항목이 끝나야 반응한다.
    ///   - 일시정지가 없다. 콜백 안에서 블로킹하면 워커 하나만 멈추고 나머지는 계속
    ///     쓰며 취소 통로까지 막히므로, 흉내내지 않고 그냥 지원하지 않는다.
    /// </summary>
    private OperationResult ExtractWithNativeEngine(
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Services.Archive.OtterZipEngine.IsAvailable)
        {
            // x64가 아니거나 DLL이 없는 경우. 예외가 아니라 정상 경로다.
            Span.Helpers.DebugLogger.Log($"[Extract] native engine unavailable for {_zipPath}");
            return OperationResult.CreateFailure(LocalizationService.L("Op_ArchiveUnsupported"));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var handle = Services.Archive.OtterZipEngine.Open(_zipPath);
            if (handle is null)
            {
                // Open 실패 사유는 엔진이 이미 로그에 남겼다. 여기서는 파일 존재 여부로
                // 사용자가 행동할 수 있는 메시지만 고른다.
                return OperationResult.CreateFailure(LocalizationService.L(
                    File.Exists(_zipPath) ? "Op_ArchiveCorrupted" : "Op_ArchiveNotFound"));
            }

            Directory.CreateDirectory(_destinationPath);

            var startTime = DateTime.Now;
            long lastReportTick = Environment.TickCount64;

            var result = Services.Archive.OtterZipEngine.ExtractAll(
                handle,
                _destinationPath,
                onProgress: p =>
                {
                    // 이 콜백은 네이티브 스레드에서 온다. 싸고 논블로킹이어야 하므로
                    // ZIP 경로와 같은 100ms 간격으로 스로틀한다 — 항목이 수만 개인
                    // 아카이브에서 UI 스레드에 그대로 흘리면 안 된다.
                    long now = Environment.TickCount64;
                    if (now - lastReportTick < FileOperationHelpers.ProgressReportIntervalMs) return;
                    lastReportTick = now;

                    FileOperationHelpers.ReportProgress(
                        progress,
                        p.CurrentEntry ?? Path.GetFileName(_zipPath),
                        (int)p.EntriesProcessed,
                        (int)p.EntriesTotal,
                        (long)p.BytesProcessed,
                        (long)p.BytesTotal,
                        startTime);
                },
                cancellationToken: cancellationToken);

            if (result.Canceled)
                return OperationResult.CreateFailure(LocalizationService.L("Toast_OperationCancelled"));

            if (!result.Success)
            {
                // -1/-2/-3/-50은 우리가 API를 잘못 부른 것이다. 사용자는 조치할 수 없으므로
                // 일반 메시지만 보이고, 원인 파악에 필요한 원문은 로그로만 남긴다.
                if (result.IsOurBug)
                {
                    Span.Helpers.DebugLogger.Log(
                        $"[Extract] native engine misuse ({result.Code}) on {_zipPath}: {result.NativeMessage}");
                }
                return OperationResult.CreateFailure(LocalizationService.L(result.MessageKey));
            }

            // 성공했는데 만들어진 파일이 없으면 성공으로 보고하지 않는다
            // (ZIP 경로의 Issue #63 후속 규칙과 동일하게 맞춘다).
            if (result.EntriesExtracted == 0)
            {
                Span.Helpers.DebugLogger.Log(
                    $"[Extract] native engine extracted nothing from {_zipPath} (skipped={result.EntriesSkipped})");
                return OperationResult.CreateFailure(LocalizationService.L("Op_SomeNotExtracted"));
            }

            if (result.EntriesSkipped > 0 || result.WarningsCount > 0)
            {
                Span.Helpers.DebugLogger.Log(
                    $"[Extract] native engine completed with skipped={result.EntriesSkipped} warnings={result.WarningsCount} for {_zipPath}");
            }

            return OperationResult.CreateSuccess(_destinationPath);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.CreateFailure(LocalizationService.L("Toast_OperationCancelled"));
        }
        catch (Exception ex)
        {
            Span.Helpers.DebugLogger.Log($"[Extract] native engine threw for {_zipPath}: {ex.GetType().Name}: {ex.Message}");
            return OperationResult.CreateFailure(LocalizationService.L("Op_ExtractFailed"));
        }
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
