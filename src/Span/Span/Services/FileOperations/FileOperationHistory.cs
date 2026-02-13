namespace Span.Services.FileOperations;

/// <summary>
/// Manages the history of file operations and provides undo/redo functionality.
/// </summary>
public class FileOperationHistory
{
    private const int MaxHistorySize = 50;
    private readonly Stack<IFileOperation> _undoStack = new();
    private readonly Stack<IFileOperation> _redoStack = new();

    /// <summary>
    /// Occurs when the history state changes (undo/redo availability or descriptions).
    /// </summary>
    public event EventHandler<HistoryChangedEventArgs>? HistoryChanged;

    /// <summary>
    /// Gets a value indicating whether an undo operation is available.
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Gets a value indicating whether a redo operation is available.
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Gets the description of the operation that would be undone.
    /// </summary>
    public string? UndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;

    /// <summary>
    /// Gets the description of the operation that would be redone.
    /// </summary>
    public string? RedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

    /// <summary>
    /// Executes a file operation and adds it to the history if successful and undoable.
    /// </summary>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<OperationResult> ExecuteAsync(
        IFileOperation operation,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await operation.ExecuteAsync(progress, cancellationToken);

        if (result.Success && operation.CanUndo)
        {
            _undoStack.Push(operation);
            _redoStack.Clear();

            // Limit stack size to prevent memory issues
            if (_undoStack.Count > MaxHistorySize)
            {
                var temp = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = 0; i < MaxHistorySize; i++)
                {
                    _undoStack.Push(temp[i]);
                }
            }

            OnHistoryChanged();
        }

        return result;
    }

    /// <summary>
    /// Undoes the most recent operation.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the undo operation.</param>
    /// <returns>The result of the undo operation.</returns>
    public async Task<OperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUndo)
        {
            return OperationResult.CreateFailure("Nothing to undo");
        }

        var operation = _undoStack.Pop();
        var result = await operation.UndoAsync(cancellationToken);

        if (result.Success)
        {
            _redoStack.Push(operation);
            OnHistoryChanged();
        }
        else
        {
            // Restore to undo stack if undo failed
            _undoStack.Push(operation);
        }

        return result;
    }

    /// <summary>
    /// Redoes the most recently undone operation.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the redo operation.</param>
    /// <returns>The result of the redo operation.</returns>
    public async Task<OperationResult> RedoAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRedo)
        {
            return OperationResult.CreateFailure("Nothing to redo");
        }

        var operation = _redoStack.Pop();
        var result = await operation.ExecuteAsync(null, cancellationToken);

        if (result.Success)
        {
            _undoStack.Push(operation);
            OnHistoryChanged();
        }
        else
        {
            // Restore to redo stack if redo failed
            _redoStack.Push(operation);
        }

        return result;
    }

    /// <summary>
    /// Clears all operation history.
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        OnHistoryChanged();
    }

    /// <summary>
    /// Raises the HistoryChanged event.
    /// </summary>
    private void OnHistoryChanged()
    {
        HistoryChanged?.Invoke(this, new HistoryChangedEventArgs
        {
            CanUndo = CanUndo,
            CanRedo = CanRedo,
            UndoDescription = UndoDescription,
            RedoDescription = RedoDescription
        });
    }
}

/// <summary>
/// Provides data for the HistoryChanged event.
/// </summary>
public class HistoryChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets a value indicating whether undo is available.
    /// </summary>
    public bool CanUndo { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether redo is available.
    /// </summary>
    public bool CanRedo { get; set; }

    /// <summary>
    /// Gets or sets the description of the operation that can be undone.
    /// </summary>
    public string? UndoDescription { get; set; }

    /// <summary>
    /// Gets or sets the description of the operation that can be redone.
    /// </summary>
    public string? RedoDescription { get; set; }
}
