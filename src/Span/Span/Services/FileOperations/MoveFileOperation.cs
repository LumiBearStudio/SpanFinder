using System.Threading;

namespace Span.Services.FileOperations;

/// <summary>
/// Represents a file or directory move (cut/paste) operation with undo support and pause capability.
/// Same-volume moves use the fast File.Move/Directory.Move.
/// Cross-volume moves fall back to stream-based copy+delete with pause support.
/// </summary>
public class MoveFileOperation : IFileOperation, IPausableOperation
{
    private const int BufferSize = 81920; // 80KB for streaming copy

    private readonly List<string> _sourcePaths;
    private readonly string _destinationDirectory;
    private readonly Dictionary<string, string> _moveMap = new(); // source -> destination
    private ManualResetEventSlim? _pauseEvent;

    /// <summary>
    /// Initializes a new instance of the <see cref="MoveFileOperation"/> class.
    /// </summary>
    /// <param name="sourcePaths">The paths of files or directories to move.</param>
    /// <param name="destinationDirectory">The destination directory.</param>
    public MoveFileOperation(List<string> sourcePaths, string destinationDirectory)
    {
        _sourcePaths = sourcePaths ?? throw new ArgumentNullException(nameof(sourcePaths));
        _destinationDirectory = destinationDirectory ?? throw new ArgumentNullException(nameof(destinationDirectory));
    }

    /// <inheritdoc/>
    public string Description => _sourcePaths.Count == 1
        ? $"Move \"{Path.GetFileName(_sourcePaths[0])}\" to {Path.GetFileName(_destinationDirectory)}"
        : $"Move {_sourcePaths.Count} item(s) to {Path.GetFileName(_destinationDirectory)}";

    /// <inheritdoc/>
    public bool CanUndo => true;

    /// <inheritdoc/>
    public void SetPauseEvent(ManualResetEventSlim pauseEvent)
    {
        _pauseEvent = pauseEvent;
    }

    /// <summary>
    /// Waits if the operation is paused, and checks for cancellation.
    /// </summary>
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
                var fileName = Path.GetFileName(sourcePath);
                var destPath = Path.Combine(_destinationDirectory, fileName);

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

                    // Handle conflict
                    if (File.Exists(destPath) || Directory.Exists(destPath))
                    {
                        destPath = GetUniqueFileName(destPath);
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

                            // Delete source after successful copy
                            File.Delete(sourcePath);
                        }
                        else if (Directory.Exists(sourcePath))
                        {
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

                            // Delete source directory after successful copy
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

                    _moveMap[sourcePath] = destPath;
                    result.AffectedPaths.Add(destPath);
                }
                catch (OperationCanceledException)
                {
                    throw; // propagate cancellation
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to move {fileName}: {ex.Message}");
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
                    errors.Add($"Failed to move back {Path.GetFileName(dest)}: {ex.Message}");
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
    /// Determines whether the move is across different volume roots.
    /// </summary>
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

    private static long GetFileOrDirectorySize(string path)
    {
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
