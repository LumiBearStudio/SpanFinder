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
        ? string.Format(L("Op_CopySingle"), FileOperationHelpers.GetFileName(_sourcePaths[0]), FileOperationHelpers.GetFileName(_destinationDirectory))
        : string.Format(L("Op_CopyMultiple"), _sourcePaths.Count, FileOperationHelpers.GetFileName(_destinationDirectory));

    /// <inheritdoc/>
    public bool CanUndo => !_hasRemotePaths;

    /// <inheritdoc/>
    public void SetPauseEvent(ManualResetEventSlim pauseEvent)
    {
        _pauseEvent = pauseEvent;
    }

    private void WaitIfPaused(CancellationToken cancellationToken)
        => FileOperationHelpers.WaitIfPaused(_pauseEvent, cancellationToken);

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

                var fileName = FileOperationHelpers.GetFileName(sourcePath);
                var destPath = FileOperationHelpers.CombinePath(_destinationDirectory, fileName, destIsRemote);

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
                                destPath = FileOperationHelpers.GetUniqueFileName(destPath);
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
                                        destPath = FileOperationHelpers.GetUniqueFileName(destPath);
                                        break;
                                }
                            }
                        }

                        // Check if source is a directory (remote)
                        bool srcIsDir = false;
                        if (srcIsRemote)
                        {
                            var srcProvider = FileOperationHelpers.GetRemoteProvider(_router, sourcePath);
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
                                    FileOperationHelpers.ReportProgress(progress, fileName, fileIndex, _sourcePaths.Count, localProcessed + bytes, totalBytes, startTime);
                                }),
                                cancellationToken);
                        }
                        else
                        {
                            await CopyFileViaStreamAsync(
                                sourcePath, destPath, srcIsRemote, destIsRemote,
                                new Progress<long>(bytes =>
                                {
                                    FileOperationHelpers.ReportProgress(progress, fileName, fileIndex, _sourcePaths.Count, localProcessed + bytes, totalBytes, startTime);
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
                                destPath = FileOperationHelpers.GetUniqueFileName(destPath);
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
                                        destPath = FileOperationHelpers.GetUniqueFileName(destPath);
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
                            await FileOperationHelpers.CopyFileWithProgressAsync(
                                sourcePath,
                                destPath,
                                FileOperationHelpers.DefaultBufferSize,
                                _pauseEvent,
                                new Progress<long>(bytes =>
                                {
                                    FileOperationHelpers.ReportProgress(progress, fileName, fileIndex, _sourcePaths.Count, localProcessed + bytes, totalBytes, startTime);
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
                            await FileOperationHelpers.CopyDirectoryWithProgressAsync(
                                sourcePath,
                                destPath,
                                FileOperationHelpers.DefaultBufferSize,
                                _pauseEvent,
                                new Progress<long>(bytes =>
                                {
                                    FileOperationHelpers.ReportProgress(progress, fileName, fileIndex, _sourcePaths.Count, localProcessed + bytes, totalBytes, startTime);
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

            FileOperationHelpers.FinalizeResultWithErrors(result, errors, "Op_SomeNotCopied");
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
                        var provider = FileOperationHelpers.GetRemoteProvider(_router, copiedPath);
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
                    errors.Add(string.Format(L("Op_FailedTo_Delete"), FileOperationHelpers.GetFileName(copiedPath), ex.Message));
                }
            }

            FileOperationHelpers.FinalizeResultWithErrors(result, errors, "Op_SomeNotUndone");
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
            var provider = FileOperationHelpers.GetRemoteProvider(_router, sourcePath)
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
                FileOperationHelpers.DefaultBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        try
        {
            if (destIsRemote)
            {
                var provider = FileOperationHelpers.GetRemoteProvider(_router, destPath)
                    ?? throw new InvalidOperationException($"원격 대상에 대한 연결을 찾을 수 없습니다: {destPath}");
                var remotePath = FileSystemRouter.ExtractRemotePath(destPath);

                // Non-seekable source: buffer into memory first
                Stream uploadStream = sourceStream;
                if (!sourceStream.CanSeek)
                {
                    var memStream = new MemoryStream();
                    var buffer = new byte[FileOperationHelpers.DefaultBufferSize];
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
                    FileOperationHelpers.DefaultBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[FileOperationHelpers.DefaultBufferSize];
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
                        if (now - lastReportTime >= FileOperationHelpers.ProgressReportIntervalMs)
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
            var provider = FileOperationHelpers.GetRemoteProvider(_router, destPath)
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
            var provider = FileOperationHelpers.GetRemoteProvider(_router, sourcePath)
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
                childSrcPath = FileOperationHelpers.CombineRemoteUri(sourcePath, item.Name);
            }
            else
            {
                childSrcPath = item.Path; // already full local path
            }

            var childDestPath = FileOperationHelpers.CombinePath(destPath, item.Name, destIsRemote);

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

    // ── Helper methods ──

    private async Task<long> GetFileOrDirectorySizeAsync(string path, CancellationToken ct)
    {
        if (FileSystemRouter.IsRemotePath(path))
        {
            // Remote: try to get file size via provider (FTP/SFTP 모두 지원)
            try
            {
                var provider = FileOperationHelpers.GetRemoteProvider(_router, path);
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
