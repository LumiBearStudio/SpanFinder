namespace Span.Services.FileOperations;

public class NewFolderOperation : IFileOperation
{
    private readonly string _folderPath;
    private readonly FileSystemRouter? _router;
    private readonly bool _isRemote;

    public NewFolderOperation(string folderPath)
        : this(folderPath, null)
    {
    }

    public NewFolderOperation(string folderPath, FileSystemRouter? router)
    {
        _folderPath = folderPath;
        _router = router;
        _isRemote = FileSystemRouter.IsRemotePath(folderPath);
    }

    public string Description => $"Create folder '{GetFileName(_folderPath)}'";
    public bool CanUndo => !_isRemote;

    public async Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_isRemote)
            {
                var provider = _router?.GetConnectionForPath(_folderPath);
                if (provider == null)
                    return OperationResult.CreateFailure($"원격 연결을 찾을 수 없습니다: {_folderPath}");

                var remotePath = FileSystemRouter.ExtractRemotePath(_folderPath);
                await provider.CreateDirectoryAsync(remotePath, cancellationToken);
            }
            else
            {
                Directory.CreateDirectory(_folderPath);
            }
            return OperationResult.CreateSuccess(_folderPath);
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailure(ex.Message);
        }
    }

    public async Task<OperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        if (_isRemote)
            return OperationResult.CreateFailure("원격 폴더 생성은 되돌릴 수 없습니다.");

        try
        {
            if (Directory.Exists(_folderPath) && !Directory.EnumerateFileSystemEntries(_folderPath).Any())
            {
                Directory.Delete(_folderPath);
                return OperationResult.CreateSuccess(_folderPath);
            }
            return OperationResult.CreateFailure("Folder is not empty, cannot undo");
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailure(ex.Message);
        }
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
}
