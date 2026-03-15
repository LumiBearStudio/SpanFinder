using static Span.Services.LocalizationService;

namespace Span.Services.FileOperations;

/// <summary>
/// Represents a file or directory rename operation.
/// Supports remote (FTP/SFTP) paths via FileSystemRouter.
/// </summary>
public class RenameFileOperation : IFileOperation
{
    private readonly string _sourcePath;
    private readonly string _newName;
    private readonly string _oldName;
    private readonly FileSystemRouter? _router;
    private readonly bool _isRemote;

    public RenameFileOperation(string sourcePath, string newName)
        : this(sourcePath, newName, null)
    {
    }

    public RenameFileOperation(string sourcePath, string newName, FileSystemRouter? router)
    {
        _sourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
        _newName = newName ?? throw new ArgumentNullException(nameof(newName));
        _router = router;
        _isRemote = FileSystemRouter.IsRemotePath(sourcePath);
        _oldName = FileOperationHelpers.GetFileName(sourcePath);
    }

    /// <inheritdoc/>
    public string Description => string.Format(L("Op_Rename"), _oldName, _newName);

    /// <inheritdoc/>
    public bool CanUndo => !_isRemote;

    /// <inheritdoc/>
    public async Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new FileOperationProgress
            {
                CurrentFile = _oldName,
                CurrentFileIndex = 1,
                TotalFileCount = 1,
                Percentage = 0
            });

            if (_isRemote)
            {
                // ── 원격 이름 변경 ──
                var provider = _router?.GetConnectionForPath(_sourcePath);
                if (provider == null)
                    return OperationResult.CreateFailure(string.Format(L("Op_NoRemoteRouter"), _sourcePath));

                var remotePath = FileSystemRouter.ExtractRemotePath(_sourcePath);
                var parentDir = remotePath.Contains('/')
                    ? remotePath[..remotePath.TrimEnd('/').LastIndexOf('/')]
                    : "/";
                if (string.IsNullOrEmpty(parentDir)) parentDir = "/";
                var newRemotePath = parentDir.TrimEnd('/') + "/" + _newName;

                await provider.RenameAsync(remotePath, newRemotePath, cancellationToken);

                // 새 전체 URI 경로 생성
                var uriPrefix = _sourcePath[..(_sourcePath.Length - remotePath.Length)];
                var newFullPath = uriPrefix + newRemotePath;

                progress?.Report(new FileOperationProgress
                {
                    CurrentFile = _newName,
                    CurrentFileIndex = 1,
                    TotalFileCount = 1,
                    Percentage = 100
                });

                return OperationResult.CreateSuccess(newFullPath);
            }
            else
            {
                // ── 로컬 이름 변경 ──
                var directory = Path.GetDirectoryName(_sourcePath);
                if (string.IsNullOrEmpty(directory))
                    return OperationResult.CreateFailure(L("Error_InvalidSourcePath"));

                var newPath = Path.Combine(directory, _newName);

                bool isFile = File.Exists(_sourcePath);
                bool isDirectory = Directory.Exists(_sourcePath);

                if (!isFile && !isDirectory)
                    return OperationResult.CreateFailure(string.Format(L("Op_SourceNotExist"), _sourcePath));

                if (File.Exists(newPath) || Directory.Exists(newPath))
                    return OperationResult.CreateFailure(string.Format(L("Op_NameAlreadyExists"), _newName));

                if (isFile)
                    File.Move(_sourcePath, newPath);
                else
                    Directory.Move(_sourcePath, newPath);

                progress?.Report(new FileOperationProgress
                {
                    CurrentFile = _newName,
                    CurrentFileIndex = 1,
                    TotalFileCount = 1,
                    Percentage = 100
                });

                return OperationResult.CreateSuccess(newPath);
            }
        }
        catch (OperationCanceledException)
        {
            return OperationResult.CreateFailure(L("Op_Cancelled_Rename"));
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailure(string.Format(L("Op_FailedTo_Rename"), ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        if (_isRemote)
            return OperationResult.CreateFailure(L("Op_CannotUndoRemoteRename"));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory = Path.GetDirectoryName(_sourcePath);
            if (string.IsNullOrEmpty(directory))
                return OperationResult.CreateFailure(L("Error_InvalidSourcePath"));

            var newPath = Path.Combine(directory, _newName);

            bool isFile = File.Exists(newPath);
            bool isDirectory = Directory.Exists(newPath);

            if (!isFile && !isDirectory)
                return OperationResult.CreateFailure(string.Format(L("Op_UndoItemNotFound"), newPath));

            if (File.Exists(_sourcePath) || Directory.Exists(_sourcePath))
                return OperationResult.CreateFailure(string.Format(L("Op_UndoNameTaken"), _oldName));

            if (isFile) File.Move(newPath, _sourcePath);
            else Directory.Move(newPath, _sourcePath);

            return OperationResult.CreateSuccess(_sourcePath);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.CreateFailure(L("Op_Cancelled_Rename"));
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailure(string.Format(L("Op_UnexpectedErrorUndo"), ex.Message));
        }
    }

}
