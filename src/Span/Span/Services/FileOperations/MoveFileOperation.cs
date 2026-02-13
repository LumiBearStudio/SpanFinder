namespace Span.Services.FileOperations;

/// <summary>
/// Represents a file or directory move (cut/paste) operation with undo support.
/// </summary>
public class MoveFileOperation : IFileOperation
{
    private readonly List<string> _sourcePaths;
    private readonly string _destinationDirectory;
    private readonly Dictionary<string, string> _moveMap = new(); // source -> destination

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
    public string Description => $"Move {_sourcePaths.Count} item(s) to {Path.GetFileName(_destinationDirectory)}";

    /// <inheritdoc/>
    public bool CanUndo => true;

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
                var fileName = Path.GetFileName(sourcePath);
                var destPath = Path.Combine(_destinationDirectory, fileName);

                try
                {
                    progress?.Report(new FileOperationProgress
                    {
                        CurrentFile = fileName,
                        CurrentFileIndex = i + 1,
                        TotalFileCount = _sourcePaths.Count,
                        Percentage = (i + 1) * 100 / _sourcePaths.Count
                    });

                    // Handle conflict
                    if (File.Exists(destPath) || Directory.Exists(destPath))
                    {
                        destPath = GetUniqueFileName(destPath);
                    }

                    // Move file or directory
                    if (File.Exists(sourcePath))
                    {
                        File.Move(sourcePath, destPath);
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        Directory.Move(sourcePath, destPath);
                    }
                    else
                    {
                        errors.Add($"Path not found: {sourcePath}");
                        continue;
                    }

                    _moveMap[sourcePath] = destPath;
                    result.AffectedPaths.Add(destPath);
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
