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
        // Already executed - return the cached result
        return Task.FromResult(_completedResult);
    }

    public Task<OperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        // Delegate undo to the original operation
        return _innerOperation.UndoAsync(cancellationToken);
    }
}
