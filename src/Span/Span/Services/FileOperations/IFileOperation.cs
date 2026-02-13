namespace Span.Services.FileOperations;

/// <summary>
/// Defines a file operation that can be executed and undone.
/// </summary>
public interface IFileOperation
{
    /// <summary>
    /// Gets a human-readable description of this operation.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets a value indicating whether this operation can be undone.
    /// </summary>
    bool CanUndo { get; }

    /// <summary>
    /// Executes the file operation.
    /// </summary>
    /// <param name="progress">Optional progress reporter for long-running operations.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result of the operation.</returns>
    Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes the file operation, restoring the previous state.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the undo operation.</param>
    /// <returns>The result of the undo operation.</returns>
    Task<OperationResult> UndoAsync(CancellationToken cancellationToken = default);
}
