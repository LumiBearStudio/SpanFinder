namespace Span.Services.FileOperations;

/// <summary>
/// Represents a file or directory rename operation.
/// </summary>
public class RenameFileOperation : IFileOperation
{
    private readonly string _sourcePath;
    private readonly string _newName;
    private readonly string _oldName;

    /// <summary>
    /// Initializes a new instance of the <see cref="RenameFileOperation"/> class.
    /// </summary>
    /// <param name="sourcePath">The full path of the file or directory to rename.</param>
    /// <param name="newName">The new name (without path).</param>
    public RenameFileOperation(string sourcePath, string newName)
    {
        _sourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
        _newName = newName ?? throw new ArgumentNullException(nameof(newName));
        _oldName = Path.GetFileName(sourcePath);
    }

    /// <inheritdoc/>
    public string Description => $"Rename '{_oldName}' to '{_newName}'";

    /// <inheritdoc/>
    public bool CanUndo => true;

    /// <inheritdoc/>
    public async Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory = Path.GetDirectoryName(_sourcePath);
            if (string.IsNullOrEmpty(directory))
            {
                return OperationResult.CreateFailure("Invalid source path - no directory component");
            }

            var newPath = Path.Combine(directory, _newName);

            // Check if source exists
            bool isFile = File.Exists(_sourcePath);
            bool isDirectory = Directory.Exists(_sourcePath);

            if (!isFile && !isDirectory)
            {
                return OperationResult.CreateFailure($"Source path does not exist: {_sourcePath}");
            }

            // Check if destination already exists
            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                return OperationResult.CreateFailure($"A file or directory with the name '{_newName}' already exists");
            }

            // Report progress
            progress?.Report(new FileOperationProgress
            {
                CurrentFile = _oldName,
                CurrentFileIndex = 1,
                TotalFileCount = 1,
                Percentage = 0
            });

            // Perform the rename
            if (isFile)
            {
                File.Move(_sourcePath, newPath);
            }
            else
            {
                Directory.Move(_sourcePath, newPath);
            }

            // Report completion
            progress?.Report(new FileOperationProgress
            {
                CurrentFile = _newName,
                CurrentFileIndex = 1,
                TotalFileCount = 1,
                Percentage = 100
            });

            return OperationResult.CreateSuccess(newPath);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.CreateFailure("Rename operation was cancelled");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.CreateFailure($"Access denied: {ex.Message}");
        }
        catch (IOException ex)
        {
            return OperationResult.CreateFailure($"I/O error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailure($"Unexpected error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory = Path.GetDirectoryName(_sourcePath);
            if (string.IsNullOrEmpty(directory))
            {
                return OperationResult.CreateFailure("Invalid source path - no directory component");
            }

            var newPath = Path.Combine(directory, _newName);

            // Check if the renamed file/directory exists
            bool isFile = File.Exists(newPath);
            bool isDirectory = Directory.Exists(newPath);

            if (!isFile && !isDirectory)
            {
                return OperationResult.CreateFailure($"Cannot undo: renamed item not found at {newPath}");
            }

            // Check if original name is available
            if (File.Exists(_sourcePath) || Directory.Exists(_sourcePath))
            {
                return OperationResult.CreateFailure($"Cannot undo: original name '{_oldName}' is already taken");
            }

            // Rename back to original
            if (isFile)
            {
                File.Move(newPath, _sourcePath);
            }
            else
            {
                Directory.Move(newPath, _sourcePath);
            }

            return OperationResult.CreateSuccess(_sourcePath);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.CreateFailure("Undo operation was cancelled");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.CreateFailure($"Access denied during undo: {ex.Message}");
        }
        catch (IOException ex)
        {
            return OperationResult.CreateFailure($"I/O error during undo: {ex.Message}");
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailure($"Unexpected error during undo: {ex.Message}");
        }
    }
}
