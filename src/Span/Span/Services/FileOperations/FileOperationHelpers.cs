using System.Threading;
using static Span.Services.LocalizationService;

namespace Span.Services.FileOperations;

/// <summary>
/// Common helper methods shared across Copy, Move, Delete, Rename, and NewFolder operations.
/// Extracted to eliminate duplication — all methods are internal static with no instance state.
/// </summary>
internal static class FileOperationHelpers
{
    /// <summary>I/O buffer size (1MB). Reduces system call count for large file throughput.</summary>
    internal const int DefaultBufferSize = 1048576;

    /// <summary>Minimum interval (ms) between progress reports to reduce UI thread load.</summary>
    internal const int ProgressReportIntervalMs = 100;

    /// <summary>
    /// Extracts the file/folder name from a local or remote path.
    /// For remote URIs, parses the last segment and unescapes it.
    /// Used by Copy, Move, Delete, Rename operations.
    /// </summary>
    internal static string GetFileName(string path)
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

    /// <summary>
    /// Generates a unique file name by appending " (1)", " (2)", etc. until no conflict exists.
    /// Used by Copy and Move for KeepBoth conflict resolution.
    /// </summary>
    internal static string GetUniqueFileName(string path)
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

    /// <summary>
    /// Combines a directory and name into a full path. Uses URI-style for remote paths, Path.Combine for local.
    /// </summary>
    internal static string CombinePath(string directory, string name, bool isRemote)
    {
        if (isRemote)
            return directory.TrimEnd('/') + "/" + name;
        return Path.Combine(directory, name);
    }

    /// <summary>
    /// Combines a remote parent URI with a child name using forward-slash separator.
    /// Example: ftp://user@host:21/path/to/dir + file.txt = ftp://user@host:21/path/to/dir/file.txt
    /// </summary>
    internal static string CombineRemoteUri(string parentUri, string childName)
    {
        return parentUri.TrimEnd('/') + "/" + childName;
    }

    /// <summary>
    /// Resolves the remote file system provider for a given full path via FileSystemRouter.
    /// Returns null if no router or no matching connection.
    /// </summary>
    internal static IFileSystemProvider? GetRemoteProvider(FileSystemRouter? router, string fullPath)
    {
        return router?.GetConnectionForPath(fullPath);
    }

    /// <summary>
    /// Blocks the current thread if the pause event is not set. Cancellation causes immediate return
    /// without throwing — the caller checks IsCancellationRequested to handle graceful shutdown.
    /// </summary>
    internal static void WaitIfPaused(ManualResetEventSlim? pauseEvent, CancellationToken cancellationToken)
    {
        if (pauseEvent != null && !cancellationToken.IsCancellationRequested)
        {
            try { pauseEvent.Wait(cancellationToken); }
            catch (OperationCanceledException) { /* caller checks IsCancellationRequested */ }
        }
    }

    /// <summary>
    /// Reports file operation progress with speed and ETA calculation.
    /// </summary>
    internal static void ReportProgress(
        IProgress<FileOperationProgress>? progress,
        string fileName, int fileIndex, int totalFileCount,
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
            TotalFileCount = totalFileCount,
            TotalBytes = totalBytes > 0 ? totalBytes : currentTotal, // indeterminate: use current as total
            ProcessedBytes = currentTotal,
            SpeedBytesPerSecond = speed,
            EstimatedTimeRemaining = remaining
        });
    }

    /// <summary>
    /// Copies a single local file with progress reporting and pause/cancel support.
    /// Uses SequentialScan for OS-level read-ahead optimization and time-throttled progress reports.
    /// </summary>
    internal static async Task CopyFileWithProgressAsync(
        string source,
        string destination,
        int bufferSize,
        ManualResetEventSlim? pauseEvent,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        using var sourceStream = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize, FileOptions.SequentialScan);

        using var destStream = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize, FileOptions.SequentialScan);

        var buffer = new byte[bufferSize];
        long copiedBytes = 0;
        int bytesRead;
        long lastReportTime = Environment.TickCount64;

        while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            WaitIfPaused(pauseEvent, cancellationToken);
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

    /// <summary>
    /// Recursively copies a local directory with progress reporting and pause/cancel support.
    /// Uses File.Copy for small files (at or below buffer size) for kernel optimization,
    /// and stream copy for larger files for progress reporting.
    /// Skips self-copy (destination inside source) to prevent infinite recursion.
    /// </summary>
    internal static async Task CopyDirectoryWithProgressAsync(
        string source,
        string destination,
        int bufferSize,
        ManualResetEventSlim? pauseEvent,
        IProgress<long> overallProgress,
        CancellationToken cancellationToken)
    {
        string? selfCopySkipDir = null;
        var srcNorm = source.TrimEnd('\\', '/') + "\\";
        var destNorm = destination.TrimEnd('\\', '/') + "\\";
        if (destNorm.StartsWith(srcNorm, StringComparison.OrdinalIgnoreCase))
            selfCopySkipDir = destination;

        long bytesCopied = 0;

        async Task CopyDirRecursive(string src, string dest)
        {
            if (cancellationToken.IsCancellationRequested) return;
            Directory.CreateDirectory(dest);

            foreach (var file in Directory.EnumerateFiles(src))
            {
                WaitIfPaused(pauseEvent, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                var destFile = Path.Combine(dest, Path.GetFileName(file));
                var fileSize = new FileInfo(file).Length;

                if (fileSize <= bufferSize)
                {
                    File.Copy(file, destFile, overwrite: true);
                }
                else
                {
                    await CopyFileWithProgressAsync(
                        file, destFile, bufferSize, pauseEvent,
                        null,
                        cancellationToken);
                }

                bytesCopied += fileSize;
                overallProgress?.Report(bytesCopied);
            }

            foreach (var dir in Directory.EnumerateDirectories(src))
            {
                WaitIfPaused(pauseEvent, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                if (selfCopySkipDir != null && string.Equals(dir, selfCopySkipDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                var destDir = Path.Combine(dest, Path.GetFileName(dir));
                await CopyDirRecursive(dir, destDir);
            }
        }

        await CopyDirRecursive(source, destination);
    }

    /// <summary>
    /// Finalizes an OperationResult when errors occurred during batch operations.
    /// If no items succeeded, marks result as failure with all errors.
    /// If some items succeeded, marks as partial success with the given localization key.
    /// </summary>
    internal static void FinalizeResultWithErrors(OperationResult result, List<string> errors, string partialSuccessKey)
    {
        if (errors.Count == 0) return;

        if (result.AffectedPaths.Count == 0)
        {
            result.Success = false;
            result.ErrorMessage = string.Join("\n", errors);
        }
        else
        {
            result.Success = true;
            result.ErrorMessage = $"{L(partialSuccessKey)}:\n{string.Join("\n", errors)}";
        }
    }
}
