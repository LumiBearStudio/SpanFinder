namespace Span.Services.FileOperations;

/// <summary>
/// Wraps a completed IFileOperation so it can be added to the FileOperationHistory
/// for undo support without re-executing the operation.
/// ExecuteAsync returns the already-known result immediately.
/// </summary>
internal class CompletedOperationWrapper : IFileOperation
{
    private readonly IFileOperation _innerOperation;
    private readonly OperationResult _completedResult;
    private bool _consumedInitialResult;

    public CompletedOperationWrapper(IFileOperation innerOperation, OperationResult completedResult)
    {
        _innerOperation = innerOperation;
        _completedResult = completedResult;
    }

    public string Description => _innerOperation.Description;
    public bool CanUndo => _innerOperation.CanUndo;

    public Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 히스토리 등록 시의 첫 호출: 이미 실행됐으므로 캐시된 결과를 반환한다.
        if (!_consumedInitialResult)
        {
            _consumedInitialResult = true;
            return Task.FromResult(_completedResult);
        }

        // Issue #61: 이후 호출(= Undo 후 Redo)은 실제로 다시 실행해야 한다.
        // 캐시 결과를 계속 반환하면 파일시스템은 그대로인데 "다시 실행됨"으로 보고되어
        // 히스토리와 실제 상태가 영구히 어긋난다.
        return _innerOperation.ExecuteAsync(progress, cancellationToken);
    }

    public Task<OperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        // Delegate undo to the original operation
        return _innerOperation.UndoAsync(cancellationToken);
    }
}
