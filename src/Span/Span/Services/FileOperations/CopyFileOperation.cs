using System.Threading;
using Span.Models;
using static Span.Services.LocalizationService;

namespace Span.Services.FileOperations;

/// <summary>
/// Represents a file or directory copy operation with progress reporting and pause support.
/// Supports local ↔ remote (FTP/SFTP) transfers via FileSystemRouter stream-based copying.
/// </summary>
public class CopyFileOperation : IFileOperation, IPausableOperation
{
    /// <summary>
    /// I/O 버퍼 크기 (1MB). 대용량 파일에서 시스템 콜 횟수를 줄여 throughput 극대화.
    /// 1MB 이하 파일은 CopyDirRecursive에서 File.Copy()로 커널 최적화 경로 사용.
    /// </summary>
    private const int BufferSize = 1048576;
    private const int ProgressReportIntervalMs = 100; // Progress 보고 최소 간격 (ms)

    private readonly List<string> _sourcePaths;
    private readonly string _destinationDirectory;
    private readonly FileSystemRouter? _router;
    private readonly List<string> _copiedPaths = new();
    private bool _hasRemotePaths;
    private ConflictResolution _conflictResolution = ConflictResolution.Prompt;
    private bool _applyToAll = false;
    private ManualResetEventSlim? _pauseEvent;

    public CopyFileOperation(List<string> sourcePaths, string destinationDirectory)
        : this(sourcePaths, destinationDirectory, null)
    {
    }

    public CopyFileOperation(List<string> sourcePaths, string destinationDirectory, FileSystemRouter? router)
    {
        _sourcePaths = sourcePaths ?? throw new ArgumentNullException(nameof(sourcePaths));
        _destinationDirectory = destinationDirectory ?? throw new ArgumentNullException(nameof(destinationDirectory));
        _router = router;
        _hasRemotePaths = _sourcePaths.Any(FileSystemRouter.IsRemotePath)
                          || FileSystemRouter.IsRemotePath(_destinationDirectory);
    }

    /// <summary>Gets the destination directory for this copy operation.</summary>
    public string DestinationDirectory => _destinationDirectory;

    /// <inheritdoc/>
    public string Description => _sourcePaths.Count == 1
        ? string.Format(L("Op_CopySingle"), GetFileName(_sourcePaths[0]), GetFileName(_destinationDirectory))
        : string.Format(L("Op_CopyMultiple"), _sourcePaths.Count, GetFileName(_destinationDirectory));

    /// <inheritdoc/>
    public bool CanUndo => !_hasRemotePaths;

    /// <inheritdoc/>
    public void SetPauseEvent(ManualResetEventSlim pauseEvent)
    {
        _pauseEvent = pauseEvent;
    }

    /// <summary>
    /// 일시정지 상태이면 재개될 때까지 대기. 취소 시 예외 없이 즉시 반환.
    /// ThrowIfCancellationRequested 대신 IsCancellationRequested 체크를 사용하는 이유:
    /// ManualResetEventSlim.Wait(ct)가 취소 시 OperationCanceledException을 던지므로,
    /// 여기서 catch하고 호출자(루프)가 IsCancellationRequested로 정상 종료 처리.
    /// 예외를 전파하면 각 파일 루프의 try-catch에서 개별 파일 에러로 오인될 수 있음.
    /// </summary>
    private void WaitIfPaused(CancellationToken cancellationToken)
    {
        if (_pauseEvent != null && !cancellationToken.IsCancellationRequested)
        {
            try { _pauseEvent.Wait(cancellationToken); }
            catch (OperationCanceledException) { /* 취소 시 즉시 반환 — 호출자가 IsCancellationRequested 확인 */ }
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new OperationResult { Success = true };
        long totalBytes = 0;
        long processedBytes = 0;
        var startTime = DateTime.Now;
        var errors = new List<string>();

        try
        {
            // 전체 바이트 계산 (각 항목 크기를 캐싱하여 이중 계산 방지)
            var itemSizes = new long[_sourcePaths.Count];
            for (int idx = 0; idx < _sourcePaths.Count; idx++)
            {
                itemSizes[idx] = await GetFileOrDirectorySizeAsync(_sourcePaths[idx], cancellationToken);
                totalBytes += itemSizes[idx];
            }

            for (int i = 0; i < _sourcePaths.Count; i++)
            {
                WaitIfPaused(cancellationToken);
                if (cancellationToken.IsCancellationRequested) break;

                var sourcePath = _sourcePaths[i];
                bool srcIsRemote = FileSystemRouter.IsRemotePath(sourcePath);
                bool destIsRemote = FileSystemRouter.IsRemotePath(_destinationDirectory);

                var fileName = GetFileName(sourcePath);
                var destPath = CombinePath(_destinationDirectory, fileName, destIsRemote);

                try
                {
                    if (srcIsRemote || destIsRemote)
                    {
                        // ── Remote copy path ──
                        if (_router == null)
                        {
                            errors.Add(string.Format(L("Op_NoRemoteRouter"), sourcePath));
                            continue;
                        }

                        // Conflict check: only for local destinations
                        if (!destIsRemote && (File.Exists(destPath) || Directory.Exists(destPath)))
                        {
                            if (!_applyToAll)
                            {
                                destPath = GetUniqueFileName(destPath);
                            }
                            else
                            {
                                switch (_conflictResolution)
                                {
                                    case ConflictResolution.Skip:
                                        continue;
                                    case ConflictResolution.Replace:
                                        if (File.Exists(destPath)) File.Delete(destPath);
                                        else if (Directory.Exists(destPath)) Directory.Delete(destPath, recursive: true);
                                        break;
                                    case ConflictResolution.KeepBoth:
                                        destPath = GetUniqueFileName(destPath);
                                        break;
                                }
                            }
                        }

                        // Check if source is a directory (remote)
                        bool srcIsDir = false;
                        if (srcIsRemote)
                        {
                            var srcProvider = GetRemoteProvider(sourcePath);
                            if (srcProvider != null)
                            {
                                var remotePath = FileSystemRouter.ExtractRemotePath(sourcePath);
                                srcIsDir = await srcProvider.IsDirectoryAsync(remotePath, cancellationToken);
                            }
                        }
                        else
                        {
                            srcIsDir = Directory.Exists(sourcePath);
                        }

                        long localProcessed = processedBytes;
                        int fileIndex = i;

                        if (srcIsDir)
                        {
                            await CopyDirectoryViaStreamAsync(
                                sourcePath, destPath, srcIsRemote, destIsRemote,
                                new Progress<long>(bytes =>
                                {
                                    ReportProgress(progress, fileName, fileIndex, localProcessed + bytes, totalBytes, startTime);
                                }),
                                cancellationToken);
                        }
                        else
                        {
                            await CopyFileViaStreamAsync(
                                sourcePath, destPath, srcIsRemote, destIsRemote,
                                new Progress<long>(bytes =>
                                {
                                    ReportProgress(progress, fileName, fileIndex, localProcessed + bytes, totalBytes, startTime);
                                }),
                                cancellationToken);
                        }

                        // 캐싱된 크기 사용 (이중 계산 방지)
                        processedBytes += itemSizes[i];
                    }
                    else
                    {
                        // ── Local copy path (unchanged) ──

                        // Handle conflict
                        if (File.Exists(destPath) || Directory.Exists(destPath))
                        {
                            if (!_applyToAll)
                            {
                                destPath = GetUniqueFileName(destPath);
                            }
                            else
                            {
                                switch (_conflictResolution)
                                {
                                    case ConflictResolution.Skip:
                                        continue;
                                    case ConflictResolution.Replace:
                                        if (File.Exists(destPath)) File.Delete(destPath);
                                        else if (Directory.Exists(destPath)) Directory.Delete(destPath, recursive: true);
                                        break;
                                    case ConflictResolution.KeepBoth:
                                        destPath = GetUniqueFileName(destPath);
                                        break;
                                }
                            }
                        }

                        // Copy file or directory
                        if (File.Exists(sourcePath))
                        {
                            var fileSize = new FileInfo(sourcePath).Length;
                            long localProcessed = processedBytes;
                            int fileIndex = i;
                            await CopyFileWithProgressAsync(
                                sourcePath,
                                destPath,
                                new Progress<long>(bytes =>
                                {
                                    ReportProgress(progress, fileName, fileIndex, localProcessed + bytes, totalBytes, startTime);
                                }),
                                cancellationToken);

                            processedBytes += fileSize;
                        }
                        else if (Directory.Exists(sourcePath))
                        {
                            // 캐싱된 크기 사용 (이중 계산 방지)
                            var dirSize = itemSizes[i];
                            long localProcessed = processedBytes;
                            int fileIndex = i;
                            await CopyDirectoryWithProgressAsync(
                                sourcePath,
                                destPath,
                                new Progress<long>(bytes =>
                                {
                                    ReportProgress(progress, fileName, fileIndex, localProcessed + bytes, totalBytes, startTime);
                                }),
                                cancellationToken);

                            // 취소 시 부분 복사 디렉토리 정리 — _copiedPaths에 미추가로 Undo 일관성 보장
                            if (cancellationToken.IsCancellationRequested)
                            {
                                try { if (Directory.Exists(destPath)) Directory.Delete(destPath, recursive: true); } catch { }
                                break;
                            }

                            processedBytes += dirSize;
                        }
                        else
                        {
                            errors.Add(string.Format(L("Op_PathNotFound"), sourcePath));
                            continue;
                        }
                    }

                    _copiedPaths.Add(destPath);
                    result.AffectedPaths.Add(destPath);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (PathTooLongException)
                {
                    errors.Add(string.Format(L("Op_PathTooLong"), fileName));
                }
                catch (Exception ex)
                {
                    errors.Add(string.Format(L("Op_FailedTo_Copy"), fileName, ex.Message));
                }
            }

            // 취소 시 예외 없이 즉시 종료
            if (cancellationToken.IsCancellationRequested)
            {
                result.Success = false;
                result.ErrorMessage = L("Op_Cancelled_Copy");
                return result;
            }

            if (errors.Count > 0)
            {
                if (result.AffectedPaths.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = string.Join("\n", errors);
                }
                else
                {
                    result.Success = true;
                    result.ErrorMessage = $"{L("Op_SomeNotCopied")}:\n{string.Join("\n", errors)}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = L("Op_Cancelled_Copy");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = string.Format(L("Op_UnexpectedError"), ex.Message);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<OperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        var result = new OperationResult { Success = true };
        var errors = new List<string>();

        try
        {
            foreach (var copiedPath in _copiedPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (FileSystemRouter.IsRemotePath(copiedPath))
                    {
                        // Remote undo: delete via provider
                        var provider = GetRemoteProvider(copiedPath);
                        if (provider != null)
                        {
                            var remotePath = FileSystemRouter.ExtractRemotePath(copiedPath);
                            await provider.DeleteAsync(remotePath, recursive: true, cancellationToken);
                        }
                    }
                    else if (File.Exists(copiedPath))
                    {
                        File.Delete(copiedPath);
                    }
                    else if (Directory.Exists(copiedPath))
                    {
                        Directory.Delete(copiedPath, recursive: true);
                    }

                    result.AffectedPaths.Add(copiedPath);
                }
                catch (Exception ex)
                {
                    errors.Add(string.Format(L("Op_FailedTo_Delete"), GetFileName(copiedPath), ex.Message));
                }
            }

            if (errors.Count > 0)
            {
                if (result.AffectedPaths.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = string.Join("\n", errors);
                }
                else
                {
                    result.Success = true;
                    result.ErrorMessage = $"{L("Op_SomeNotUndone")}:\n{string.Join("\n", errors)}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = L("Op_Cancelled_Copy");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = string.Format(L("Op_UnexpectedErrorUndo"), ex.Message);
        }

        return result;
    }

    public void SetConflictResolution(ConflictResolution resolution, bool applyToAll)
    {
        _conflictResolution = resolution;
        _applyToAll = applyToAll;
    }

    // ── Remote stream-based copy ──

    private async Task CopyFileViaStreamAsync(
        string sourcePath, string destPath,
        bool srcIsRemote, bool destIsRemote,
        IProgress<long> progress,
        CancellationToken ct)
    {
        Stream sourceStream;
        bool sourceUsedProgress = false; // 다운로드에서 이미 진행률을 보고했는지 추적

        if (srcIsRemote)
        {
            var provider = GetRemoteProvider(sourcePath)
                ?? throw new InvalidOperationException($"원격 소스에 대한 연결을 찾을 수 없습니다: {sourcePath}");
            var remotePath = FileSystemRouter.ExtractRemotePath(sourcePath);

            if (destIsRemote)
            {
                // Remote→Remote: 진행률은 업로드 단계에서만 보고
                sourceStream = new MemoryStream();
                if (provider is FtpProvider ftpDl)
                    await ftpDl.DownloadWithProgressAsync(remotePath, sourceStream, null, ct);
                else if (provider is SftpProvider sftpDl)
                    await sftpDl.DownloadWithProgressAsync(remotePath, sourceStream, null, ct);
                else
                    sourceStream = await provider.OpenReadAsync(remotePath, ct);
                if (sourceStream is MemoryStream ms) ms.Position = 0;
            }
            else
            {
                // Remote→Local: 다운로드에서 진행률 보고
                sourceStream = new MemoryStream();
                if (provider is FtpProvider ftpDl)
                {
                    await ftpDl.DownloadWithProgressAsync(remotePath, sourceStream,
                        new Progress<long>(bytes => progress?.Report(bytes)), ct);
                    sourceUsedProgress = true;
                }
                else if (provider is SftpProvider sftpDl)
                {
                    await sftpDl.DownloadWithProgressAsync(remotePath, sourceStream,
                        new Progress<long>(bytes => progress?.Report(bytes)), ct);
                    sourceUsedProgress = true;
                }
                else
                {
                    sourceStream = await provider.OpenReadAsync(remotePath, ct);
                }
                if (sourceStream is MemoryStream ms2) ms2.Position = 0;
            }
        }
        else
        {
            sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        try
        {
            if (destIsRemote)
            {
                var provider = GetRemoteProvider(destPath)
                    ?? throw new InvalidOperationException($"원격 대상에 대한 연결을 찾을 수 없습니다: {destPath}");
                var remotePath = FileSystemRouter.ExtractRemotePath(destPath);

                // Non-seekable source: buffer into memory first
                Stream uploadStream = sourceStream;
                if (!sourceStream.CanSeek)
                {
                    var memStream = new MemoryStream();
                    var buffer = new byte[BufferSize];
                    int bytesRead;
                    while ((bytesRead = await sourceStream.ReadAsync(buffer, ct)) > 0)
                    {
                        WaitIfPaused(ct);
                        await memStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    }
                    memStream.Position = 0;
                    uploadStream = memStream;
                }

                // 업로드 (진행률은 한 번만 보고)
                if (provider is FtpProvider ftpUp)
                {
                    await ftpUp.UploadWithProgressAsync(remotePath, uploadStream,
                        new Progress<long>(bytes => progress?.Report(bytes)), ct);
                }
                else if (provider is SftpProvider sftpUp)
                {
                    await sftpUp.UploadWithProgressAsync(remotePath, uploadStream,
                        new Progress<long>(bytes => progress?.Report(bytes)), ct);
                }
                else
                {
                    await provider.WriteAsync(remotePath, uploadStream, ct);
                    if (uploadStream.CanSeek)
                        progress?.Report(uploadStream.Length);
                }

                if (uploadStream != sourceStream)
                    uploadStream.Dispose();
            }
            else
            {
                // Destination is local
                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[BufferSize];
                long copiedBytes = 0;
                int bytesRead;
                long lastReportTime = Environment.TickCount64;
                while ((bytesRead = await sourceStream.ReadAsync(buffer, ct)) > 0)
                {
                    WaitIfPaused(ct);
                    await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    copiedBytes += bytesRead;
                    // 다운로드에서 이미 진행률을 보고했으면 여기서 중복 보고 안 함
                    // 시간 간격 throttle로 UI 스레드 부하 감소
                    if (!sourceUsedProgress && progress != null)
                    {
                        long now = Environment.TickCount64;
                        if (now - lastReportTime >= ProgressReportIntervalMs)
                        {
                            progress.Report(copiedBytes);
                            lastReportTime = now;
                        }
                    }
                }
                // 최종 진행률 보고
                if (sourceUsedProgress && sourceStream.CanSeek)
                    progress?.Report(sourceStream.Length);
                else if (!sourceUsedProgress)
                    progress?.Report(copiedBytes);
            }
        }
        finally
        {
            sourceStream.Dispose();
        }
    }

    private async Task CopyDirectoryViaStreamAsync(
        string sourcePath, string destPath,
        bool srcIsRemote, bool destIsRemote,
        IProgress<long> overallProgress,
        CancellationToken ct)
    {
        long bytesCopied = 0;

        // Create destination directory
        if (destIsRemote)
        {
            var provider = GetRemoteProvider(destPath)
                ?? throw new InvalidOperationException($"원격 대상에 대한 연결을 찾을 수 없습니다: {destPath}");
            await provider.CreateDirectoryAsync(FileSystemRouter.ExtractRemotePath(destPath), ct);
        }
        else
        {
            Directory.CreateDirectory(destPath);
        }

        // Get items from source
        IReadOnlyList<IFileSystemItem> items;
        if (srcIsRemote)
        {
            var provider = GetRemoteProvider(sourcePath)
                ?? throw new InvalidOperationException($"원격 소스에 대한 연결을 찾을 수 없습니다: {sourcePath}");
            var remotePath = FileSystemRouter.ExtractRemotePath(sourcePath);
            items = await provider.GetItemsAsync(remotePath, ct);
        }
        else
        {
            var localItems = new List<IFileSystemItem>();
            // EnumerateFiles/EnumerateDirectories: lazy enumeration으로 취소 즉시 반응
            foreach (var file in Directory.EnumerateFiles(sourcePath))
            {
                localItems.Add(new FileItem
                {
                    Name = Path.GetFileName(file),
                    Path = file,
                    Size = new FileInfo(file).Length
                });
            }
            foreach (var dir in Directory.EnumerateDirectories(sourcePath))
            {
                localItems.Add(new FolderItem
                {
                    Name = Path.GetFileName(dir),
                    Path = dir
                });
            }
            items = localItems;
        }

        foreach (var item in items)
        {
            WaitIfPaused(ct);
            if (ct.IsCancellationRequested) return;

            string childSrcPath;
            if (srcIsRemote)
            {
                // item.Path from provider is a remote-relative path; reconstruct full URI
                childSrcPath = CombineRemoteUri(sourcePath, item.Name);
            }
            else
            {
                childSrcPath = item.Path; // already full local path
            }

            var childDestPath = CombinePath(destPath, item.Name, destIsRemote);

            if (item is FolderItem)
            {
                await CopyDirectoryViaStreamAsync(childSrcPath, childDestPath, srcIsRemote, destIsRemote,
                    new Progress<long>(bytes =>
                    {
                        bytesCopied += bytes;
                        overallProgress?.Report(bytesCopied);
                    }),
                    ct);
            }
            else
            {
                await CopyFileViaStreamAsync(childSrcPath, childDestPath, srcIsRemote, destIsRemote,
                    new Progress<long>(bytes =>
                    {
                        overallProgress?.Report(bytesCopied + bytes);
                    }),
                    ct);
                if (item is FileItem fi)
                    bytesCopied += fi.Size;
            }
        }
    }

    // ── Local copy (unchanged) ──

    private async Task CopyFileWithProgressAsync(
        string source,
        string destination,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        // SequentialScan으로 OS-level read-ahead 최적화
        // ReadAsync/WriteAsync로 CancellationToken을 통한 즉시 취소 지원
        using var sourceStream = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.SequentialScan);

        using var destStream = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None,
            BufferSize, FileOptions.SequentialScan);

        var buffer = new byte[BufferSize];
        long copiedBytes = 0;
        int bytesRead;
        long lastReportTime = Environment.TickCount64;

        // async I/O로 취소 토큰이 Read/Write 중에도 즉시 반응
        while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            WaitIfPaused(cancellationToken);
            await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            copiedBytes += bytesRead;

            if (progress != null)
            {
                long now = Environment.TickCount64;
                if (now - lastReportTime >= ProgressReportIntervalMs)
                {
                    progress.Report(copiedBytes);
                    lastReportTime = now;
                }
            }
        }

        progress?.Report(copiedBytes);
    }

    private async Task CopyDirectoryWithProgressAsync(
        string source,
        string destination,
        IProgress<long> overallProgress,
        CancellationToken cancellationToken)
    {
        // 자기 폴더 복사 방지: destination이 source 안에 있으면 해당 디렉토리를 skip
        string? selfCopySkipDir = null;
        var srcNorm = source.TrimEnd('\\', '/') + "\\";
        var destNorm = destination.TrimEnd('\\', '/') + "\\";
        if (destNorm.StartsWith(srcNorm, StringComparison.OrdinalIgnoreCase))
            selfCopySkipDir = destination;

        long bytesCopied = 0;

        // 취소 시 return 패턴: ThrowIfCancellationRequested 대신 IsCancellationRequested + return 사용.
        // 이유: 재귀 호출 스택 전체를 예외로 unwind하는 비용을 피하고,
        // 부분 복사된 파일들은 ExecuteAsync의 cancellation 분기에서 일관되게 처리.
        async Task CopyDirRecursive(string src, string dest)
        {
            if (cancellationToken.IsCancellationRequested) return;
            Directory.CreateDirectory(dest);

            // EnumerateFiles: lazy enumeration으로 대규모 디렉토리에서 즉시 처리 시작 + 취소 반응
            foreach (var file in Directory.EnumerateFiles(src))
            {
                WaitIfPaused(cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                var destFile = Path.Combine(dest, Path.GetFileName(file));
                var fileSize = new FileInfo(file).Length;

                if (fileSize <= BufferSize) // 1MB 이하: File.Copy 사용 (커널 최적화, 작은 파일 대량 복사 시 성능 향상)
                {
                    File.Copy(file, destFile, overwrite: true);
                }
                else // 1MB 초과: 스트림 복사 (대용량 파일 progress 리포팅 가능)
                {
                    await CopyFileWithProgressAsync(
                        file, destFile,
                        null,
                        cancellationToken);
                }

                bytesCopied += fileSize;
                overallProgress?.Report(bytesCopied);
            }

            // EnumerateDirectories: lazy enumeration으로 취소 즉시 반응
            foreach (var dir in Directory.EnumerateDirectories(src))
            {
                WaitIfPaused(cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                // 자기 복사 방지: destination 디렉토리 자체를 재귀 대상에서 제외
                if (selfCopySkipDir != null && string.Equals(dir, selfCopySkipDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                var destDir = Path.Combine(dest, Path.GetFileName(dir));
                await CopyDirRecursive(dir, destDir);
            }
        }

        await CopyDirRecursive(source, destination);
    }

    // ── Helper methods ──

    private IFileSystemProvider? GetRemoteProvider(string fullPath)
    {
        return _router?.GetConnectionForPath(fullPath);
    }

    private static string GetFileName(string path)
    {
        if (FileSystemRouter.IsRemotePath(path))
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
            {
                var segments = uri.AbsolutePath.TrimEnd('/').Split('/');
                return segments.Length > 0 ? Uri.UnescapeDataString(segments[^1]) : path;
            }
            return path.TrimEnd('/').Split('/')[^1];
        }
        return Path.GetFileName(path);
    }

    private static string CombinePath(string directory, string name, bool isRemote)
    {
        if (isRemote)
            return directory.TrimEnd('/') + "/" + name;
        return Path.Combine(directory, name);
    }

    private static string CombineRemoteUri(string parentUri, string childName)
    {
        // parentUri: ftp://user@host:21/path/to/dir
        // childName: file.txt
        // result: ftp://user@host:21/path/to/dir/file.txt
        return parentUri.TrimEnd('/') + "/" + childName;
    }

    private async Task<long> GetFileOrDirectorySizeAsync(string path, CancellationToken ct)
    {
        if (FileSystemRouter.IsRemotePath(path))
        {
            // Remote: try to get file size via provider (FTP/SFTP 모두 지원)
            try
            {
                var provider = GetRemoteProvider(path);
                var remotePath = FileSystemRouter.ExtractRemotePath(path);

                if (provider is FtpProvider ftpProvider)
                {
                    bool isDir = await ftpProvider.IsDirectoryAsync(remotePath, ct);
                    if (!isDir)
                    {
                        var size = await ftpProvider.GetFileSizeAsync(remotePath, ct);
                        return size > 0 ? size : 0;
                    }
                }
                else if (provider is SftpProvider sftpProvider)
                {
                    bool isDir = await sftpProvider.IsDirectoryAsync(remotePath, ct);
                    if (!isDir)
                    {
                        var size = await sftpProvider.GetFileSizeAsync(remotePath, ct);
                        return size > 0 ? size : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Span.Helpers.DebugLogger.Log($"[CopyFileOperation] 원격 파일 크기 조회 실패 ({path}): {ex.Message}");
            }
            return 0;
        }

        if (File.Exists(path))
            return new FileInfo(path).Length;

        if (Directory.Exists(path))
        {
            try
            {
                return await Task.Run(() =>
                    new DirectoryInfo(path)
                        .EnumerateFiles("*", SearchOption.AllDirectories)
                        .Sum(f => f.Length), ct);
            }
            catch { return 0; }
        }

        return 0;
    }

    private void ReportProgress(
        IProgress<FileOperationProgress>? progress,
        string fileName, int fileIndex,
        long currentTotal, long totalBytes,
        DateTime startTime)
    {
        var elapsed = DateTime.Now - startTime;
        var speed = elapsed.TotalSeconds > 0 ? currentTotal / elapsed.TotalSeconds : 0;
        var remaining = speed > 0 && totalBytes > 0
            ? TimeSpan.FromSeconds((totalBytes - currentTotal) / speed)
            : TimeSpan.Zero;

        progress?.Report(new FileOperationProgress
        {
            CurrentFile = fileName,
            CurrentFileIndex = fileIndex + 1,
            TotalFileCount = _sourcePaths.Count,
            TotalBytes = totalBytes > 0 ? totalBytes : currentTotal, // indeterminate: use current as total
            ProcessedBytes = currentTotal,
            SpeedBytesPerSecond = speed,
            EstimatedTimeRemaining = remaining
        });
    }

    private string GetUniqueFileName(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        int counter = 1;

        string newPath;
        do
        {
            newPath = Path.Combine(directory, $"{fileName} ({counter}){extension}");
            counter++;
        } while (File.Exists(newPath) || Directory.Exists(newPath));

        return newPath;
    }
}

/// <summary>
/// Defines conflict resolution strategies when copying files.
/// </summary>
public enum ConflictResolution
{
    Prompt,
    Replace,
    Skip,
    KeepBoth
}
