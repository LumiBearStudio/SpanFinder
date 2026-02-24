using Microsoft.VisualBasic.FileIO;

namespace Span.Services.FileOperations;

/// <summary>
/// Represents a file or directory delete operation with Recycle Bin support.
/// Supports remote (FTP/SFTP) paths via FileSystemRouter.
/// </summary>
public class DeleteFileOperation : IFileOperation
{
    private readonly List<string> _sourcePaths;
    private readonly bool _permanent;
    private readonly FileSystemRouter? _router;
    private readonly Dictionary<string, string> _recycledPaths = new();

    public DeleteFileOperation(List<string> sourcePaths, bool permanent = false)
        : this(sourcePaths, permanent, null)
    {
    }

    public DeleteFileOperation(List<string> sourcePaths, bool permanent, FileSystemRouter? router)
    {
        _sourcePaths = sourcePaths ?? throw new ArgumentNullException(nameof(sourcePaths));
        _permanent = permanent;
        _router = router;
    }

    /// <inheritdoc/>
    public string Description => _permanent
        ? $"Permanently delete {_sourcePaths.Count} item(s)"
        : $"Delete {_sourcePaths.Count} item(s)";

    /// <inheritdoc/>
    public bool CanUndo => !_permanent && !_sourcePaths.Any(FileSystemRouter.IsRemotePath);

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
                var fileName = GetFileName(sourcePath);

                progress?.Report(new FileOperationProgress
                {
                    CurrentFile = fileName,
                    CurrentFileIndex = i + 1,
                    TotalFileCount = _sourcePaths.Count,
                    Percentage = (i + 1) * 100 / _sourcePaths.Count
                });

                try
                {
                    if (FileSystemRouter.IsRemotePath(sourcePath))
                    {
                        // ── 원격 삭제 ──
                        var provider = _router?.GetConnectionForPath(sourcePath);
                        if (provider == null)
                        {
                            errors.Add($"원격 연결을 찾을 수 없습니다: {sourcePath}");
                            continue;
                        }

                        var remotePath = FileSystemRouter.ExtractRemotePath(sourcePath);
                        await provider.DeleteAsync(remotePath, recursive: true, cancellationToken);
                    }
                    else if (_permanent)
                    {
                        // ── 로컬 영구 삭제 ──
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
                        // ── 로컬 휴지통 삭제 ──
                        if (File.Exists(sourcePath))
                        {
                            FileSystem.DeleteFile(
                                sourcePath,
                                UIOption.OnlyErrorDialogs,
                                RecycleOption.SendToRecycleBin);
                            _recycledPaths[sourcePath] = sourcePath;
                        }
                        else if (Directory.Exists(sourcePath))
                        {
                            FileSystem.DeleteDirectory(
                                sourcePath,
                                UIOption.OnlyErrorDialogs,
                                RecycleOption.SendToRecycleBin);
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
                catch (PathTooLongException)
                {
                    errors.Add($"경로가 너무 깁니다: {fileName}");
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to delete {fileName}: {ex.Message}");
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

        return OperationResult.CreateFailure(
            "Cannot restore from Recycle Bin programmatically. " +
            "Please use Windows Recycle Bin to restore the deleted items.");
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
