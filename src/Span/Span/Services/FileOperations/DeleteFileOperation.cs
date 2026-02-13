using Microsoft.VisualBasic.FileIO;

namespace Span.Services.FileOperations;

/// <summary>
/// Represents a file or directory delete operation with Recycle Bin support.
/// </summary>
public class DeleteFileOperation : IFileOperation
{
    private readonly List<string> _sourcePaths;
    private readonly bool _permanent;
    private readonly Dictionary<string, string> _recycledPaths = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteFileOperation"/> class.
    /// </summary>
    /// <param name="sourcePaths">The paths of files or directories to delete.</param>
    /// <param name="permanent">If true, permanently deletes without sending to Recycle Bin.</param>
    public DeleteFileOperation(List<string> sourcePaths, bool permanent = false)
    {
        _sourcePaths = sourcePaths ?? throw new ArgumentNullException(nameof(sourcePaths));
        _permanent = permanent;
    }

    /// <inheritdoc/>
    public string Description => _permanent
        ? $"Permanently delete {_sourcePaths.Count} item(s)"
        : $"Delete {_sourcePaths.Count} item(s) to Recycle Bin";

    /// <inheritdoc/>
    public bool CanUndo => !_permanent;

    /// <inheritdoc/>
    public async Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new OperationResult { Success = true };
        var errors = new List<string>();

        try
        {
            for (int i = 0; i < _sourcePaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourcePath = _sourcePaths[i];

                // Report progress
                progress?.Report(new FileOperationProgress
                {
                    CurrentFile = Path.GetFileName(sourcePath),
                    CurrentFileIndex = i + 1,
                    TotalFileCount = _sourcePaths.Count,
                    Percentage = (i + 1) * 100 / _sourcePaths.Count
                });

                try
                {
                    if (_permanent)
                    {
                        // Permanent delete
                        if (File.Exists(sourcePath))
                        {
                            File.Delete(sourcePath);
                        }
                        else if (Directory.Exists(sourcePath))
                        {
                            Directory.Delete(sourcePath, recursive: true);
                        }
                        else
                        {
                            errors.Add($"Path not found: {sourcePath}");
                            continue;
                        }
                    }
                    else
                    {
                        // Move to Recycle Bin using Microsoft.VisualBasic
                        if (File.Exists(sourcePath))
                        {
                            FileSystem.DeleteFile(
                                sourcePath,
                                UIOption.OnlyErrorDialogs,
                                RecycleOption.SendToRecycleBin);

                            // Store the original path for potential future restoration
                            _recycledPaths[sourcePath] = sourcePath;
                        }
                        else if (Directory.Exists(sourcePath))
                        {
                            FileSystem.DeleteDirectory(
                                sourcePath,
                                UIOption.OnlyErrorDialogs,
                                RecycleOption.SendToRecycleBin);

                            // Store the original path for potential future restoration
                            _recycledPaths[sourcePath] = sourcePath;
                        }
                        else
                        {
                            errors.Add($"Path not found: {sourcePath}");
                            continue;
                        }
                    }

                    result.AffectedPaths.Add(sourcePath);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to delete {Path.GetFileName(sourcePath)}: {ex.Message}");
                }
            }

            // Set result based on errors
            if (errors.Count > 0)
            {
                if (result.AffectedPaths.Count == 0)
                {
                    // All deletions failed
                    result.Success = false;
                    result.ErrorMessage = string.Join("\n", errors);
                }
                else
                {
                    // Partial success
                    result.Success = true;
                    result.ErrorMessage = $"Some items could not be deleted:\n{string.Join("\n", errors)}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Delete operation was cancelled";
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
        if (_permanent)
        {
            return OperationResult.CreateFailure("Cannot undo permanent deletion");
        }

        // Note: Restoring from Recycle Bin programmatically is very complex
        // and requires Shell API COM interfaces (IFileOperation, etc.)
        // For now, we inform the user to restore manually from Recycle Bin
        return OperationResult.CreateFailure(
            "Cannot restore from Recycle Bin programmatically. " +
            "Please use Windows Recycle Bin to restore the deleted items.");
    }
}
