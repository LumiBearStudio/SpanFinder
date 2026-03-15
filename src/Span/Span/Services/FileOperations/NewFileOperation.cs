using static Span.Services.LocalizationService;

namespace Span.Services.FileOperations;

/// <summary>
/// 새 파일 생성 (빈 텍스트 파일 등). Undo 시 빈 파일이면 삭제.
/// </summary>
public class NewFileOperation : IFileOperation
{
    private readonly string _filePath;

    public NewFileOperation(string filePath)
    {
        _filePath = filePath;
    }

    public string Description => string.Format(L("FileOp_CreateFile"), Path.GetFileName(_filePath));
    public bool CanUndo => true;

    public Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Create empty file
            File.WriteAllBytes(_filePath, Array.Empty<byte>());
            return Task.FromResult(OperationResult.CreateSuccess(_filePath));
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
            if (File.Exists(_filePath))
            {
                var info = new FileInfo(_filePath);
                if (info.Length == 0)
                {
                    File.Delete(_filePath);
                    return Task.FromResult(OperationResult.CreateSuccess(_filePath));
                }
                return Task.FromResult(OperationResult.CreateFailure(L("FileOp_FileModifiedCannotUndo")));
            }
            return Task.FromResult(OperationResult.CreateFailure(L("FileOp_FileNotExist")));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.CreateFailure(ex.Message));
        }
    }
}
