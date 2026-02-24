using System.IO.Compression;

namespace Span.Services.FileOperations;

/// <summary>
/// ZIP 압축 해제 작업.
/// </summary>
public class ExtractOperation : IFileOperation
{
    private readonly string _zipPath;
    private readonly string _destinationPath;

    public ExtractOperation(string zipPath, string destinationPath)
    {
        _zipPath = zipPath;
        _destinationPath = destinationPath;
    }

    public string Description => $"Extract '{Path.GetFileName(_zipPath)}'";
    public bool CanUndo => true;

    public Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_destinationPath);

            using var archive = ZipFile.OpenRead(_zipPath);
            int total = archive.Entries.Count;
            int current = 0;

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(Path.Combine(_destinationPath, entry.FullName));
                }
                catch (PathTooLongException)
                {
                    continue; // Skip entries with paths exceeding MAX_PATH
                }

                // Security: prevent path traversal
                if (!fullPath.StartsWith(Path.GetFullPath(_destinationPath), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrEmpty(entry.Name))
                {
                    // Directory entry
                    Directory.CreateDirectory(fullPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    entry.ExtractToFile(fullPath, overwrite: true);
                }

                current++;
                progress?.Report(new FileOperationProgress
                {
                    CurrentFile = entry.FullName,
                    CurrentFileIndex = current,
                    TotalFileCount = total
                });
            }

            return Task.FromResult(OperationResult.CreateSuccess(_destinationPath));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult.CreateFailure("Operation cancelled"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.CreateFailure(ex.Message));
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
            return Task.FromResult(OperationResult.CreateFailure("Extracted folder does not exist"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.CreateFailure(ex.Message));
        }
    }
}
