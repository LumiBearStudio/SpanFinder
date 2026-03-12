using System.Threading;
using Span.Models;
using static Span.Services.LocalizationService;

namespace Span.Services.FileOperations;

/// <summary>
/// Represents a file or directory move (cut/paste) operation with undo support and pause capability.
/// Same-volume moves use the fast File.Move/Directory.Move.
/// Cross-volume and remote moves fall back to stream-based copy+delete with pause support.
/// </summary>
public class MoveFileOperation : IFileOperation, IPausableOperation
{
    private readonly List<string> _sourcePaths;
    private readonly string _destinationDirectory;
    private readonly FileSystemRouter? _router;
    private readonly Dictionary<string, string> _moveMap = new(); // source -> destination
    private bool _hasRemotePaths;
    private ConflictResolution _conflictResolution = ConflictResolution.Prompt;
    private bool _applyToAll = false;
    private ManualResetEventSlim? _pauseEvent;

    public MoveFileOperation(List<string> sourcePaths, string destinationDirectory)
        : this(sourcePaths, destinationDirectory, null)
    {
    }

    public MoveFileOperation(List<string> sourcePaths, string destinationDirectory, FileSystemRouter? router)
    {
        _sourcePaths = sourcePaths ?? throw new ArgumentNullException(nameof(sourcePaths));
        _destinationDirectory = destinationDirectory ?? throw new ArgumentNullException(nameof(destinationDirectory));
        _router = router;
        _hasRemotePaths = _sourcePaths.Any(FileSystemRouter.IsRemotePath)
                          || FileSystemRouter.IsRemotePath(_destinationDirectory);
    }

    /// <summary>Gets the destination directory for this move operation.</summary>
    public string DestinationDirectory => _destinationDirectory;

    /// <summary>Gets the source paths for this move operation (used for post-move source column refresh).</summary>
    public IReadOnlyList<string> SourcePaths => _sourcePaths;

    /// <inheritdoc/>
    public string Description => _sourcePaths.Count == 1
        ? string.Format(L("Op_MoveSingle"), FileOperationHelpers.GetFileName(_sourcePaths[0]), FileOperationHelpers.GetFileName(_destinationDirectory))
        : string.Format(L("Op_MoveMultiple"), _sourcePaths.Count, FileOperationHelpers.GetFileName(_destinationDirectory));

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
        var errors = new List<string>();
        long totalBytes = 0;
        long processedBytes = 0;
        var startTime = DateTime.Now;

        try
        {
            // Calculate total bytes for progress
            foreach (var path in _sourcePaths)
            {
                totalBytes += GetFileOrDirectorySize(path);
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
                    progress?.Report(new FileOperationProgress
                    {
                        CurrentFile = fileName,
                        CurrentFileIndex = i + 1,
                        TotalFileCount = _sourcePaths.Count,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                        Percentage = totalBytes > 0 ? (int)(processedBytes * 100 / totalBytes) : (i + 1) * 100 / _sourcePaths.Count
                    });

                    if (srcIsRemote || destIsRemote)
                    {
                        // ── Remote move: stream copy + delete source ──
                        if (_router == null)
                        {
                            errors.Add(string.Format(L("Op_NoRemoteRouter"), sourcePath));
                            continue;
                        }

                        // Conflict check: only for local destinations
                        if (!destIsRemote && (File.Exists(destPath) || Directory.Exists(destPath)))
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
                                default:
                                    destPath = FileOperationHelpers.GetUniqueFileName(destPath);
                                    break;
                            }
                        }

                        // Check if source is a directory
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

                        // Copy via stream
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

                        // Delete source after successful copy
                        if (srcIsRemote)
                        {
                            var provider = FileOperationHelpers.GetRemoteProvider(_router, sourcePath);
                            if (provider != null)
                            {
                                var remotePath = FileSystemRouter.ExtractRemotePath(sourcePath);
                                await provider.DeleteAsync(remotePath, recursive: true, cancellationToken);
                            }
                        }
                        else
                        {
                            if (File.Exists(sourcePath)) File.Delete(sourcePath);
                            else if (Directory.Exists(sourcePath)) Directory.Delete(sourcePath, recursive: true);
                        }
                    }
                    else
                    {
                        // ── Local move ──

                        // Handle conflict
                        if (File.Exists(destPath) || Directory.Exists(destPath))
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
                                default:
                                    destPath = FileOperationHelpers.GetUniqueFileName(destPath);
                                    break;
                            }
                        }

                        bool isCrossVolume = IsCrossVolumeMove(sourcePath, destPath);

                        if (isCrossVolume)
                        {
                            // Cross-volume: stream copy + delete (pausable)
                            var fileSize = GetFileOrDirectorySize(sourcePath);
                            long localProcessed = processedBytes;
                            int fileIndex = i;

                            if (File.Exists(sourcePath))
                            {
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

                                // 취소 시 소스 삭제 방지 + 불완전 대상 파일 정리
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
                                    break;
                                }

                                File.Delete(sourcePath);
                            }
                            else if (Directory.Exists(sourcePath))
                            {
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

                                // 취소 시 소스 삭제 방지 + 부분 복사 디렉토리 정리 (데이터 손실 방지)
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    try { if (Directory.Exists(destPath)) Directory.Delete(destPath, recursive: true); } catch { }
                                    break;
                                }

                                Directory.Delete(sourcePath, recursive: true);
                            }

                            processedBytes += fileSize;
                        }
                        else
                        {
                            // Same-volume: fast move (nearly instant, not pausable)
                            if (File.Exists(sourcePath))
                            {
                                File.Move(sourcePath, destPath);
                                processedBytes += new FileInfo(destPath).Length;
                            }
                            else if (Directory.Exists(sourcePath))
                            {
                                Directory.Move(sourcePath, destPath);
                                processedBytes += GetFileOrDirectorySize(destPath);
                            }
                            else
                            {
                                errors.Add(string.Format(L("Op_PathNotFound"), sourcePath));
                                continue;
                            }
                        }
                    }

                    _moveMap[sourcePath] = destPath;
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
                    errors.Add(string.Format(L("Op_FailedTo_Move"), fileName, ex.Message));
                }
            }

            // 취소 시 예외 없이 즉시 종료
            if (cancellationToken.IsCancellationRequested)
            {
                result.Success = false;
                result.ErrorMessage = L("Op_Cancelled_Move");
                return result;
            }

            FileOperationHelpers.FinalizeResultWithErrors(result, errors, "Op_SomeNotMoved");
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = L("Op_Cancelled_Move");
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
            // Move back in reverse order
            foreach (var (source, dest) in _moveMap.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(dest))
                    {
                        File.Move(dest, source);
                    }
                    else if (Directory.Exists(dest))
                    {
                        Directory.Move(dest, source);
                    }

                    result.AffectedPaths.Add(source);
                }
                catch (Exception ex)
                {
                    errors.Add(string.Format(L("Op_FailedTo_MoveBack"), FileOperationHelpers.GetFileName(dest), ex.Message));
                }
            }

            FileOperationHelpers.FinalizeResultWithErrors(result, errors, "Op_SomeNotUndone");
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = L("Op_Cancelled_Move");
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

    // ── Remote stream-based copy (reused from CopyFileOperation pattern) ──

    private async Task CopyFileViaStreamAsync(
        string sourcePath, string destPath,
        bool srcIsRemote, bool destIsRemote,
        IProgress<long> progress,
        CancellationToken ct)
    {
        Stream sourceStream;
        if (srcIsRemote)
        {
            var provider = FileOperationHelpers.GetRemoteProvider(_router, sourcePath)
                ?? throw new InvalidOperationException($"원격 소스에 대한 연결을 찾을 수 없습니다: {sourcePath}");
            var remotePath = FileSystemRouter.ExtractRemotePath(sourcePath);
            sourceStream = await provider.OpenReadAsync(remotePath, ct);
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

                if (!sourceStream.CanSeek)
                {
                    var memStream = new MemoryStream();
                    var buffer = new byte[FileOperationHelpers.DefaultBufferSize];
                    long copiedBytes = 0;
                    int bytesRead;
                    while ((bytesRead = await sourceStream.ReadAsync(buffer, ct)) > 0)
                    {
                        WaitIfPaused(ct);
                        await memStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        copiedBytes += bytesRead;
                        progress?.Report(copiedBytes);
                    }
                    memStream.Position = 0;
                    await provider.WriteAsync(remotePath, memStream, ct);
                    memStream.Dispose();
                }
                else
                {
                    progress?.Report(sourceStream.Length);
                    await provider.WriteAsync(remotePath, sourceStream, ct);
                }
            }
            else
            {
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
                    // 시간 간격 throttle로 UI 스레드 부하 감소
                    if (progress != null)
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
                childSrcPath = FileOperationHelpers.CombineRemoteUri(sourcePath, item.Name);
            }
            else
            {
                childSrcPath = item.Path;
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

    // ── Local copy (unchanged) ──

    private static bool IsCrossVolumeMove(string source, string destination)
    {
        var sourceRoot = Path.GetPathRoot(source);
        var destRoot = Path.GetPathRoot(destination);
        return !string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helper methods ──

    private static long GetFileOrDirectorySize(string path)
    {
        if (FileSystemRouter.IsRemotePath(path))
            return 0; // Remote: size unknown

        if (File.Exists(path))
            return new FileInfo(path).Length;

        if (Directory.Exists(path))
        {
            try
            {
                return new DirectoryInfo(path)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
            catch { return 0; }
        }
        return 0;
    }

}
