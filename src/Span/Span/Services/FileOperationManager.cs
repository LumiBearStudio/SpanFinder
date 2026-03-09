using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Span.Services.FileOperations;

namespace Span.Services;

/// <summary>
/// Manages concurrent file copy/move operations with pause, resume, and cancel support.
/// Each operation runs independently on a background thread.
/// </summary>
public class FileOperationManager
{
    private int _nextOperationId = 0;
    private readonly object _lock = new();

    /// <summary>
    /// Observable collection of all active (in-progress or paused) operations.
    /// Bind this to the UI to display the operation list.
    /// </summary>
    public ObservableCollection<FileOperationEntry> ActiveOperations { get; } = new();

    /// <summary>
    /// Raised when all active operations have completed (collection becomes empty).
    /// </summary>
    public event EventHandler? AllOperationsCompleted;

    /// <summary>
    /// Raised when any single operation completes (success or failure).
    /// </summary>
    public event EventHandler<OperationCompletedEventArgs>? OperationCompleted;

    /// <summary>
    /// Starts a new file operation (copy or move) in the background.
    /// Returns immediately with the operation entry for tracking.
    /// </summary>
    /// <param name="operation">The file operation to execute.</param>
    /// <param name="dispatcherQueue">The UI dispatcher queue for thread-safe collection updates.</param>
    /// <returns>The operation entry that can be used for pause/resume/cancel.</returns>
    public FileOperationEntry StartOperation(
        IFileOperation operation,
        Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
    {
        var id = Interlocked.Increment(ref _nextOperationId);
        var cts = new CancellationTokenSource();
        var pauseEvent = new ManualResetEventSlim(true); // starts in signaled (non-paused) state

        var entry = new FileOperationEntry
        {
            Id = id,
            Description = operation.Description,
            Operation = operation,
            CancellationTokenSource = cts,
            PauseEvent = pauseEvent,
            Status = OperationStatus.Running
        };

        // Inject pause event into the operation if it supports it
        if (operation is IPausableOperation pausable)
        {
            pausable.SetPauseEvent(pauseEvent);
        }

        dispatcherQueue.TryEnqueue(() =>
        {
            lock (_lock) { ActiveOperations.Add(entry); }
        });

        // Launch the operation on a background thread
        entry.Task = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<FileOperationProgress>(p =>
                {
                    // Marshal progress updates to UI thread
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        entry.CurrentFile = p.CurrentFile;
                        entry.Percentage = p.Percentage;
                        entry.CurrentFileIndex = p.CurrentFileIndex;
                        entry.TotalFileCount = p.TotalFileCount;
                        entry.SpeedBytesPerSecond = p.SpeedBytesPerSecond;
                        entry.EstimatedTimeRemaining = p.EstimatedTimeRemaining;
                        entry.ProcessedBytes = p.ProcessedBytes;
                        entry.TotalBytes = p.TotalBytes;
                    });
                });

                var result = await operation.ExecuteAsync(progress, cts.Token);

                if (!dispatcherQueue.TryEnqueue(() =>
                {
                    entry.Status = result.Success ? OperationStatus.Completed : OperationStatus.Failed;
                    entry.ErrorMessage = result.ErrorMessage;
                    entry.Result = result;

                    RemoveCompletedOperation(entry);
                    OperationCompleted?.Invoke(this, new OperationCompletedEventArgs(entry, result));
                }))
                {
                    // DispatcherQueue shut down (window closed) — clean up directly
                    entry.Result = result;
                    lock (_lock) { ActiveOperations.Clear(); }
                }
            }
            catch (OperationCanceledException)
            {
                if (!dispatcherQueue.TryEnqueue(() =>
                {
                    entry.Status = OperationStatus.Cancelled;
                    RemoveCompletedOperation(entry);
                    OperationCompleted?.Invoke(this, new OperationCompletedEventArgs(
                        entry, OperationResult.CreateFailure("Operation cancelled")));
                }))
                {
                    lock (_lock) { ActiveOperations.Clear(); }
                }
            }
            catch (Exception ex)
            {
                if (!dispatcherQueue.TryEnqueue(() =>
                {
                    entry.Status = OperationStatus.Failed;
                    entry.ErrorMessage = ex.Message;
                    RemoveCompletedOperation(entry);
                    OperationCompleted?.Invoke(this, new OperationCompletedEventArgs(
                        entry, OperationResult.CreateFailure(ex.Message)));
                }))
                {
                    lock (_lock) { ActiveOperations.Clear(); }
                }
            }
            finally
            {
                pauseEvent.Dispose();
            }
        });

        return entry;
    }

    /// <summary>
    /// Pauses a running operation.
    /// </summary>
    public void PauseOperation(int operationId)
    {
        var entry = FindOperation(operationId);
        if (entry != null && entry.Status == OperationStatus.Running)
        {
            entry.PauseEvent.Reset(); // Block the worker thread
            entry.Status = OperationStatus.Paused;
        }
    }

    /// <summary>
    /// Resumes a paused operation.
    /// </summary>
    public void ResumeOperation(int operationId)
    {
        var entry = FindOperation(operationId);
        if (entry != null && entry.Status == OperationStatus.Paused)
        {
            entry.PauseEvent.Set(); // Unblock the worker thread
            entry.Status = OperationStatus.Running;
        }
    }

    /// <summary>
    /// Cancels an operation (whether running or paused).
    /// </summary>
    public void CancelOperation(int operationId)
    {
        var entry = FindOperation(operationId);
        if (entry != null && (entry.Status == OperationStatus.Running || entry.Status == OperationStatus.Paused))
        {
            // If paused, unblock first so the cancellation can be observed
            if (entry.Status == OperationStatus.Paused)
            {
                entry.PauseEvent.Set();
            }
            entry.CancellationTokenSource.Cancel();
            entry.Status = OperationStatus.Cancelling;
        }
    }

    /// <summary>
    /// Toggles pause/resume for the given operation.
    /// </summary>
    public void TogglePause(int operationId)
    {
        var entry = FindOperation(operationId);
        if (entry == null) return;

        if (entry.Status == OperationStatus.Running)
            PauseOperation(operationId);
        else if (entry.Status == OperationStatus.Paused)
            ResumeOperation(operationId);
    }

    /// <summary>
    /// Cancels all running/paused operations.
    /// </summary>
    public void CancelAll()
    {
        lock (_lock)
        {
            foreach (var entry in ActiveOperations)
            {
                if (entry.Status == OperationStatus.Running || entry.Status == OperationStatus.Paused)
                {
                    if (entry.Status == OperationStatus.Paused)
                        entry.PauseEvent.Set();
                    entry.CancellationTokenSource.Cancel();
                    entry.Status = OperationStatus.Cancelling;
                }
            }
        }
    }

    /// <summary>
    /// Whether there are any active (running/paused) operations.
    /// </summary>
    public bool HasActiveOperations
    {
        get
        {
            lock (_lock)
            {
                foreach (var op in ActiveOperations)
                {
                    if (op.Status == OperationStatus.Running || op.Status == OperationStatus.Paused)
                        return true;
                }
                return false;
            }
        }
    }

    private FileOperationEntry? FindOperation(int id)
    {
        lock (_lock)
        {
            foreach (var entry in ActiveOperations)
            {
                if (entry.Id == id) return entry;
            }
            return null;
        }
    }

    private void RemoveCompletedOperation(FileOperationEntry entry)
    {
        // Remove after a short delay so the user can see the final state
        _ = SafeDelayedRemoveAsync(entry);
    }

    private async Task SafeDelayedRemoveAsync(FileOperationEntry entry)
    {
        try
        {
            await Task.Delay(2000);

            var dq = entry.DispatcherQueue;
            if (dq == null) return;

            if (!dq.TryEnqueue(() =>
            {
                lock (_lock)
                {
                    ActiveOperations.Remove(entry);
                    if (ActiveOperations.Count == 0)
                    {
                        AllOperationsCompleted?.Invoke(this, EventArgs.Empty);
                    }
                }
            }))
            {
                // DispatcherQueue shut down — clean up without UI notification
                lock (_lock)
                {
                    ActiveOperations.Clear();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileOperationManager] Delayed remove error: {ex.Message}");
            // Ensure cleanup even on error
            lock (_lock)
            {
                ActiveOperations.Remove(entry);
            }
        }
    }
}

/// <summary>
/// Represents a single file operation in progress, with its state and controls.
/// </summary>
public partial class FileOperationEntry : ObservableObject
{
    public int Id { get; init; }

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _currentFile = string.Empty;

    [ObservableProperty]
    private int _percentage;

    [ObservableProperty]
    private int _currentFileIndex;

    [ObservableProperty]
    private int _totalFileCount;

    [ObservableProperty]
    private double _speedBytesPerSecond;

    [ObservableProperty]
    private TimeSpan _estimatedTimeRemaining;

    [ObservableProperty]
    private long _processedBytes;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsCancelling))]
    [NotifyPropertyChangedFor(nameof(PauseResumeIcon))]
    [NotifyPropertyChangedFor(nameof(PauseResumeTooltip))]
    [NotifyPropertyChangedFor(nameof(CanPauseOrResume))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private OperationStatus _status = OperationStatus.Running;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsPaused => Status == OperationStatus.Paused;
    public bool IsRunning => Status == OperationStatus.Running;
    public bool IsCancelling => Status == OperationStatus.Cancelling;
    public string PauseResumeIcon => IsPaused ? "\uE768" : "\uE769"; // Play : Pause (Segoe MDL2)
    public string PauseResumeTooltip => IsPaused ? "Resume" : "Pause";
    public bool CanPauseOrResume => Status == OperationStatus.Running || Status == OperationStatus.Paused;
    public bool CanCancel => Status == OperationStatus.Running || Status == OperationStatus.Paused;
    /// <summary>Cancelling 상태일 때 표시할 상태 텍스트 (로컬라이즈 가능)</summary>
    public string StatusText => IsCancelling ? _cancellingText : "";

    /// <summary>로컬라이즈된 "취소 중..." 텍스트 설정용</summary>
    internal string _cancellingText = "Cancelling...";

    public string SpeedText => FormatSpeed(SpeedBytesPerSecond);
    public string RemainingTimeText => FormatTime(EstimatedTimeRemaining);
    public string FileCountText => TotalFileCount > 0 ? $"{CurrentFileIndex} / {TotalFileCount}" : "";
    public string PercentageText => $"{Percentage}%";

    // Internal references - not for UI binding
    internal IFileOperation Operation { get; init; } = null!;
    internal CancellationTokenSource CancellationTokenSource { get; init; } = null!;
    internal ManualResetEventSlim PauseEvent { get; init; } = null!;
    internal Task? Task { get; set; }
    internal OperationResult? Result { get; set; }
    internal Microsoft.UI.Dispatching.DispatcherQueue? DispatcherQueue { get; set; }

    partial void OnPercentageChanged(int value) => OnPropertyChanged(nameof(PercentageText));
    partial void OnSpeedBytesPerSecondChanged(double value) => OnPropertyChanged(nameof(SpeedText));
    partial void OnEstimatedTimeRemainingChanged(TimeSpan value) => OnPropertyChanged(nameof(RemainingTimeText));
    partial void OnCurrentFileIndexChanged(int value) => OnPropertyChanged(nameof(FileCountText));
    partial void OnTotalFileCountChanged(int value) => OnPropertyChanged(nameof(FileCountText));

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "";
        if (bytesPerSecond < 1024)
            return $"{bytesPerSecond:F0} B/s";
        if (bytesPerSecond < 1024 * 1024)
            return $"{bytesPerSecond / 1024:F1} KB/s";
        if (bytesPerSecond < 1024.0 * 1024 * 1024)
            return $"{bytesPerSecond / (1024.0 * 1024):F1} MB/s";
        return $"{bytesPerSecond / (1024.0 * 1024 * 1024):F1} GB/s";
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time <= TimeSpan.Zero) return "";
        if (time.TotalSeconds < 60)
            return $"{time.TotalSeconds:F0} sec remaining";
        if (time.TotalMinutes < 60)
            return $"{time.TotalMinutes:F0} min remaining";
        return $"{time.TotalHours:F1} hours remaining";
    }
}

/// <summary>
/// Status of a file operation.
/// </summary>
public enum OperationStatus
{
    Running,
    Paused,
    Cancelling,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Event args for when an operation completes.
/// </summary>
public class OperationCompletedEventArgs : EventArgs
{
    public FileOperationEntry Entry { get; }
    public OperationResult Result { get; }

    public OperationCompletedEventArgs(FileOperationEntry entry, OperationResult result)
    {
        Entry = entry;
        Result = result;
    }
}
