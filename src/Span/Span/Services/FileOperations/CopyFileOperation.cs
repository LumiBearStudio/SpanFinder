using System.Threading;
using Span.Models;

namespace Span.Services.FileOperations;

/// <summary>
/// Represents a file or directory copy operation with progress reporting and pause support.
/// Supports local ↔ remote (FTP/SFTP) transfers via FileSystemRouter stream-based copying.
/// </summary>
public class CopyFileOperation : IFileOperation, IPausableOperation
{
    private const int BufferSize = 81920; // 80KB buffer for large file copying

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

    /// <inheritdoc/>
    public string Description => _sourcePaths.Count == 1
        ? $"Copy \"{GetFileName(_sourcePaths[0])}\" to {GetFileName(_destinationDirectory)}"
        : $"Copy {_sourcePaths.Count} item(s) to {GetFileName(_destinationDirectory)}";

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
        long totalBytes = 0;
        long processedBytes = 0;
        var startTime = DateTime.Now;
        var errors = new List<string>();

        try
        {
            // Calculate total bytes
            foreach (var path in _sourcePaths)
            {
                totalBytes += await GetFileOrDirectorySizeAsync(path, cancellationToken);
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
                    if (srcIsRemote || destIsRemote)
                    {
                        // ── Remote copy path ──
                        if (_router == null)
                        {
                            errors.Add($"원격 경로를 처리할 수 없습니다 (라우터 없음): {sourcePath}");
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

                        // For remote sources, size may be 0 (indeterminate); just count items
                        processedBytes += totalBytes > 0 ? 0 : 0; // progress already reported via stream bytes
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
                            var dirSize = await GetFileOrDirectorySizeAsync(sourcePath, cancellationToken);
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
                            processedBytes += dirSize;
                        }
                        else
                        {
                            errors.Add($"Path not found: {sourcePath}");
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
                catch (Exception ex)
                {
                    errors.Add($"Failed to copy {fileName}: {ex.Message}");
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
                    result.ErrorMessage = $"Some items could not be copied:\n{string.Join("\n", errors)}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Copy operation was cancelled";
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
                    errors.Add($"Failed to delete {GetFileName(copiedPath)}: {ex.Message}");
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

    // ── Remote stream-based copy ──

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

                // For remote destination, buffer the whole stream and upload
                // (FTP/SFTP providers expect seekable stream)
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
                    // Source is already a MemoryStream (from provider.OpenReadAsync)
                    progress?.Report(sourceStream.Length);
                    await provider.WriteAsync(remotePath, sourceStream, ct);
                }
            }
            else
            {
                // Destination is local
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
                cancellationToken.ThrowIfCancellationRequested();

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
                cancellationToken.ThrowIfCancellationRequested();

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
            // Remote: size unknown, return 0 (indeterminate progress)
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
