using System.Threading;

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

/// <summary>
/// Interface for file operations that support pausing via ManualResetEventSlim.
/// The pause event is injected by the FileOperationManager before execution begins.
/// </summary>
public interface IPausableOperation
{
    /// <summary>
    /// Sets the pause event that this operation should check between I/O chunks.
    /// When the event is reset (not signaled), the operation blocks until resumed.
    /// </summary>
    /// <param name="pauseEvent">The ManualResetEventSlim controlling pause/resume.</param>
    void SetPauseEvent(ManualResetEventSlim pauseEvent);
}
