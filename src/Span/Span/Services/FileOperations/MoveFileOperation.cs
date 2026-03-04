using System.Threading;
using Span.Models;

namespace Span.Services.FileOperations;

/// <summary>
/// Represents a file or directory move (cut/paste) operation with undo support and pause capability.
/// Same-volume moves use the fast File.Move/Directory.Move.
/// Cross-volume and remote moves fall back to stream-based copy+delete with pause support.
/// </summary>
public class MoveFileOperation : IFileOperation, IPausableOperation
{
    private const int BufferSize = 1048576; // 1MB buffer for streaming copy (up from 80KB)

    private static int GetBufferSize(long fileSize) => fileSize < 1048576 ? 81920 : BufferSize;

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

    /// <inheritdoc/>
    public string Description => _sourcePaths.Count == 1
        ? $"Move \"{GetFileName(_sourcePaths[0])}\" to {GetFileName(_destinationDirectory)}"
        : $"Move {_sourcePaths.Count} item(s) to {GetFileName(_destinationDirectory)}";

    /// <inheritdoc/>
    public bool CanUndo => !_hasRemotePaths;

    /// <inheritdoc/>
    public void SetPauseEvent(ManualResetEventSlim pauseEvent)
    {
        _pauseEvent = pauseEvent;
    }

    private void WaitIfPaused(CancellationToken cancellationToken)
    {
        if (_pauseEvent != null)
        {
            _pauseEvent.Wait(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

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

                var sourcePath = _sourcePaths[i];
                bool srcIsRemote = FileSystemRouter.IsRemotePath(sourcePath);
                bool destIsRemote = FileSystemRouter.IsRemotePath(_destinationDirectory);

                var fileName = GetFileName(sourcePath);
                var destPath = CombinePath(_destinationDirectory, fileName, destIsRemote);

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
                            errors.Add($"원격 경로를 처리할 수 없습니다 (라우터 없음): {sourcePath}");
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
                                    destPath = GetUniqueFileName(destPath);
                                    break;
                            }
                        }

                        // Check if source is a directory
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

                        // Copy via stream
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

                        // Delete source after successful copy
                        if (srcIsRemote)
                        {
                            var provider = GetRemoteProvider(sourcePath);
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
                                    destPath = GetUniqueFileName(destPath);
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
                                await CopyFileWithProgressAsync(
                                    sourcePath,
                                    destPath,
                                    new Progress<long>(bytes =>
                                    {
                                        ReportProgress(progress, fileName, fileIndex, localProcessed + bytes, totalBytes, startTime);
                                    }),
                                    cancellationToken);

                                File.Delete(sourcePath);
                            }
                            else if (Directory.Exists(sourcePath))
                            {
                                await CopyDirectoryWithProgressAsync(
                                    sourcePath,
                                    destPath,
                                    new Progress<long>(bytes =>
                                    {
                                        ReportProgress(progress, fileName, fileIndex, localProcessed + bytes, totalBytes, startTime);
                                    }),
                                    cancellationToken);

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
                                errors.Add($"Path not found: {sourcePath}");
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
                    errors.Add($"경로가 너무 깁니다: {fileName}");
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to move {fileName}: {ex.Message}");
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
                    result.ErrorMessage = $"Some items could not be moved:\n{string.Join("\n", errors)}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Move operation was cancelled";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Unexpected error: {ex.Message}";
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
                    errors.Add($"Failed to move back {GetFileName(dest)}: {ex.Message}");
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
                    result.ErrorMessage = $"Some items could not be undone:\n{string.Join("\n", errors)}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Undo operation was cancelled";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Unexpected error during undo: {ex.Message}";
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
            var provider = GetRemoteProvider(sourcePath)
                ?? throw new InvalidOperationException($"원격 소스에 대한 연결을 찾을 수 없습니다: {sourcePath}");
            var remotePath = FileSystemRouter.ExtractRemotePath(sourcePath);
            sourceStream = await provider.OpenReadAsync(remotePath, ct);
        }
        else
        {
            sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        }

        try
        {
            if (destIsRemote)
            {
                var provider = GetRemoteProvider(destPath)
                    ?? throw new InvalidOperationException($"원격 대상에 대한 연결을 찾을 수 없습니다: {destPath}");
                var remotePath = FileSystemRouter.ExtractRemotePath(destPath);

                if (!sourceStream.CanSeek)
                {
                    var memStream = new MemoryStream();
                    var buffer = new byte[BufferSize];
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

                using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                var buffer = new byte[BufferSize];
                long copiedBytes = 0;
                int bytesRead;
                while ((bytesRead = await sourceStream.ReadAsync(buffer, ct)) > 0)
                {
                    WaitIfPaused(ct);
                    await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    copiedBytes += bytesRead;
                    progress?.Report(copiedBytes);
                }
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
            var provider = GetRemoteProvider(destPath)
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
            var provider = GetRemoteProvider(sourcePath)
                ?? throw new InvalidOperationException($"원격 소스에 대한 연결을 찾을 수 없습니다: {sourcePath}");
            var remotePath = FileSystemRouter.ExtractRemotePath(sourcePath);
            items = await provider.GetItemsAsync(remotePath, ct);
        }
        else
        {
            var localItems = new List<IFileSystemItem>();
            foreach (var file in Directory.GetFiles(sourcePath))
            {
                localItems.Add(new FileItem
                {
                    Name = Path.GetFileName(file),
                    Path = file,
                    Size = new FileInfo(file).Length
                });
            }
            foreach (var dir in Directory.GetDirectories(sourcePath))
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
            ct.ThrowIfCancellationRequested();

            string childSrcPath;
            if (srcIsRemote)
            {
                childSrcPath = CombineRemoteUri(sourcePath, item.Name);
            }
            else
            {
                childSrcPath = item.Path;
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

    private static bool IsCrossVolumeMove(string source, string destination)
    {
        var sourceRoot = Path.GetPathRoot(source);
        var destRoot = Path.GetPathRoot(destination);
        return !string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);
    }

    private async Task CopyFileWithProgressAsync(
        string source,
        string destination,
        IProgress<long> progress,
        CancellationToken cancellationToken)
    {
        using var sourceStream = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);

        using var destStream = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        var buffer = new byte[BufferSize];
        long copiedBytes = 0;
        int bytesRead;

        while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            WaitIfPaused(cancellationToken);
            await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            copiedBytes += bytesRead;
            progress?.Report(copiedBytes);
        }
    }

    private async Task CopyDirectoryWithProgressAsync(
        string source,
        string destination,
        IProgress<long> overallProgress,
        CancellationToken cancellationToken)
    {
        long bytesCopied = 0;

        async Task CopyDirRecursive(string src, string dest)
        {
            Directory.CreateDirectory(dest);

            foreach (var file in Directory.GetFiles(src))
            {
                WaitIfPaused(cancellationToken);

                var destFile = Path.Combine(dest, Path.GetFileName(file));
                var fileSize = new FileInfo(file).Length;

                await CopyFileWithProgressAsync(
                    file, destFile,
                    new Progress<long>(_ => { }),
                    cancellationToken);

                bytesCopied += fileSize;
                overallProgress?.Report(bytesCopied);
            }

            foreach (var dir in Directory.GetDirectories(src))
            {
                WaitIfPaused(cancellationToken);
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
        return parentUri.TrimEnd('/') + "/" + childName;
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
            TotalBytes = totalBytes > 0 ? totalBytes : currentTotal,
            ProcessedBytes = currentTotal,
            SpeedBytesPerSecond = speed,
            EstimatedTimeRemaining = remaining
        });
    }

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
