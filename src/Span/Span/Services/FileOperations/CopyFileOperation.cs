using System.Threading;

namespace Span.Services.FileOperations;

/// <summary>
/// Represents a file or directory copy operation with progress reporting and pause support.
/// Implements IPausableOperation so the FileOperationManager can inject a pause event.
/// </summary>
public class CopyFileOperation : IFileOperation, IPausableOperation
{
    private const int BufferSize = 81920; // 80KB buffer for large file copying

    private readonly List<string> _sourcePaths;
    private readonly string _destinationDirectory;
    private readonly List<string> _copiedPaths = new();
    private ConflictResolution _conflictResolution = ConflictResolution.Prompt;
    private bool _applyToAll = false;
    private ManualResetEventSlim? _pauseEvent;

    /// <summary>
    /// Initializes a new instance of the <see cref="CopyFileOperation"/> class.
    /// </summary>
    /// <param name="sourcePaths">The paths of files or directories to copy.</param>
    /// <param name="destinationDirectory">The destination directory.</param>
    public CopyFileOperation(List<string> sourcePaths, string destinationDirectory)
    {
        _sourcePaths = sourcePaths ?? throw new ArgumentNullException(nameof(sourcePaths));
        _destinationDirectory = destinationDirectory ?? throw new ArgumentNullException(nameof(destinationDirectory));
    }

    /// <inheritdoc/>
    public string Description => _sourcePaths.Count == 1
        ? $"Copy \"{Path.GetFileName(_sourcePaths[0])}\" to {Path.GetFileName(_destinationDirectory)}"
        : $"Copy {_sourcePaths.Count} item(s) to {Path.GetFileName(_destinationDirectory)}";

    /// <inheritdoc/>
    public bool CanUndo => true;

    /// <inheritdoc/>
    public void SetPauseEvent(ManualResetEventSlim pauseEvent)
    {
        _pauseEvent = pauseEvent;
    }

    /// <summary>
    /// Waits if the operation is paused, and checks for cancellation.
    /// Called between I/O operations to allow responsive pause/cancel.
    /// </summary>
    private void WaitIfPaused(CancellationToken cancellationToken)
    {
        if (_pauseEvent != null)
        {
            // Wait until the event is signaled (resumed) or cancellation
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
                totalBytes += GetFileOrDirectorySize(path);
            }

            for (int i = 0; i < _sourcePaths.Count; i++)
            {
                WaitIfPaused(cancellationToken);

                var sourcePath = _sourcePaths[i];
                var fileName = Path.GetFileName(sourcePath);
                var destPath = Path.Combine(_destinationDirectory, fileName);

                try
                {
                    // Handle conflict
                    if (File.Exists(destPath) || Directory.Exists(destPath))
                    {
                        if (!_applyToAll)
                        {
                            // Auto-rename for now (conflict dialog handled by ViewModel)
                            destPath = GetUniqueFileName(destPath);
                        }
                        else
                        {
                            // Apply stored resolution
                            switch (_conflictResolution)
                            {
                                case ConflictResolution.Skip:
                                    continue;
                                case ConflictResolution.Replace:
                                    // Delete existing before copy
                                    if (File.Exists(destPath))
                                        File.Delete(destPath);
                                    else if (Directory.Exists(destPath))
                                        Directory.Delete(destPath, recursive: true);
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
                        long localProcessed = processedBytes; // capture for closure
                        int fileIndex = i; // capture for closure
                        await CopyFileWithProgressAsync(
                            sourcePath,
                            destPath,
                            new Progress<long>(bytes =>
                            {
                                var elapsed = DateTime.Now - startTime;
                                var currentTotal = localProcessed + bytes;
                                var speed = elapsed.TotalSeconds > 0 ? currentTotal / elapsed.TotalSeconds : 0;
                                var remaining = speed > 0 ? TimeSpan.FromSeconds((totalBytes - currentTotal) / speed) : TimeSpan.Zero;

                                progress?.Report(new FileOperationProgress
                                {
                                    CurrentFile = fileName,
                                    CurrentFileIndex = fileIndex + 1,
                                    TotalFileCount = _sourcePaths.Count,
                                    TotalBytes = totalBytes,
                                    ProcessedBytes = currentTotal,
                                    SpeedBytesPerSecond = speed,
                                    EstimatedTimeRemaining = remaining
                                });
                            }),
                            cancellationToken);

                        processedBytes += fileSize;
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        var dirSize = GetFileOrDirectorySize(sourcePath);
                        long localProcessed = processedBytes;
                        int fileIndex = i;
                        await CopyDirectoryWithProgressAsync(
                            sourcePath,
                            destPath,
                            new Progress<long>(bytes =>
                            {
                                var elapsed = DateTime.Now - startTime;
                                var currentTotal = localProcessed + bytes;
                                var speed = elapsed.TotalSeconds > 0 ? currentTotal / elapsed.TotalSeconds : 0;
                                var remaining = speed > 0 ? TimeSpan.FromSeconds((totalBytes - currentTotal) / speed) : TimeSpan.Zero;

                                progress?.Report(new FileOperationProgress
                                {
                                    CurrentFile = Path.GetFileName(sourcePath),
                                    CurrentFileIndex = fileIndex + 1,
                                    TotalFileCount = _sourcePaths.Count,
                                    TotalBytes = totalBytes,
                                    ProcessedBytes = currentTotal,
                                    SpeedBytesPerSecond = speed,
                                    EstimatedTimeRemaining = remaining
                                });
                            }),
                            cancellationToken);
                        processedBytes += dirSize;
                    }
                    else
                    {
                        errors.Add($"Path not found: {sourcePath}");
                        continue;
                    }

                    _copiedPaths.Add(destPath);
                    result.AffectedPaths.Add(destPath);
                }
                catch (OperationCanceledException)
                {
                    throw; // propagate cancellation
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to copy {fileName}: {ex.Message}");
                }
            }

            // Set result based on errors
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
                    if (File.Exists(copiedPath))
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
                    errors.Add($"Failed to delete {Path.GetFileName(copiedPath)}: {ex.Message}");
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

    /// <summary>
    /// Sets the conflict resolution strategy for this copy operation.
    /// </summary>
    /// <param name="resolution">The conflict resolution strategy.</param>
    /// <param name="applyToAll">Whether to apply this resolution to all conflicts.</param>
    public void SetConflictResolution(ConflictResolution resolution, bool applyToAll)
    {
        _conflictResolution = resolution;
        _applyToAll = applyToAll;
    }

    private async Task CopyFileWithProgressAsync(
        string source,
        string destination,
        IProgress<long> progress,
        CancellationToken cancellationToken)
    {
        using var sourceStream = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true);

        using var destStream = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true);

        var buffer = new byte[BufferSize];
        long copiedBytes = 0;
        int bytesRead;

        while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            // Check pause state between buffer writes - this is the key pause point
            WaitIfPaused(cancellationToken);

            await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            copiedBytes += bytesRead;
            progress?.Report(copiedBytes);
        }
    }

    /// <summary>
    /// Copies a directory recursively with progress reporting and pause support.
    /// Unlike the old CopyDirectoryAsync, this streams each file through CopyFileWithProgressAsync.
    /// </summary>
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
                    file,
                    destFile,
                    new Progress<long>(_ =>
                    {
                        // report cumulative bytes for the whole directory
                    }),
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

    private long GetFileOrDirectorySize(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path).Length;
        }

        if (Directory.Exists(path))
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);
                return dirInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
            catch
            {
                // If we can't enumerate, return 0
                return 0;
            }
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

/// <summary>
/// Defines conflict resolution strategies when copying files.
/// </summary>
public enum ConflictResolution
{
    /// <summary>
    /// Prompt the user for each conflict.
    /// </summary>
    Prompt,

    /// <summary>
    /// Replace the existing file.
    /// </summary>
    Replace,

    /// <summary>
    /// Skip the conflicting file.
    /// </summary>
    Skip,

    /// <summary>
    /// Keep both files by auto-renaming.
    /// </summary>
    KeepBoth
}
