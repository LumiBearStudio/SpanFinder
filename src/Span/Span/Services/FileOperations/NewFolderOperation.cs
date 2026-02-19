namespace Span.Services.FileOperations;

public class NewFolderOperation : IFileOperation
{
    private readonly string _folderPath;

    public NewFolderOperation(string folderPath)
    {
        _folderPath = folderPath;
    }

    public string Description => $"Create folder '{Path.GetFileName(_folderPath)}'";
    public bool CanUndo => true;

    public Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_folderPath);
            return Task.FromResult(OperationResult.CreateSuccess(_folderPath));
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
            if (Directory.Exists(_folderPath) && !Directory.EnumerateFileSystemEntries(_folderPath).Any())
            {
                Directory.Delete(_folderPath);
                return Task.FromResult(OperationResult.CreateSuccess(_folderPath));
            }
            return Task.FromResult(OperationResult.CreateFailure("Folder is not empty, cannot undo"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.CreateFailure(ex.Message));
        }
    }
}
