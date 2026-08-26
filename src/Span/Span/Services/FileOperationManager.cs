using System;
using System.Collections.ObjectModel;
using System.Linq;
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
            Status = OperationStatus.Running,
            DispatcherQueue = dispatcherQueue
        };

        // Inject pause event into the operation if it supports it
        if (operation is IPausableOperation pausable)
        {
            pausable.SetPauseEvent(pauseEvent);
        }

        // 소규모 작업 판단: 파일 수 ≤ 10 AND 총 크기 ≤ 50MB → 팝업 없이 토스트만
        // 대규모 또는 고용량은 진행 팝업 표시 (일시정지/취소 지원)
        bool showProgress = true;
        try
        {
            IReadOnlyList<string>? sourcePaths = operation switch
            {
                MoveFileOperation move => move.SourcePaths,
                CopyFileOperation copy => copy.SourcePaths,
                DeleteFileOperation del => del.SourcePaths,
                _ => null
            };
            // Issue #61: 원격(FTP/SFTP) 경로는 크기 조회가 불가능하고 가장 느린 경로라
            // 항목 수와 무관하게 진행률+취소를 항상 노출한다.
            if (sourcePaths != null && sourcePaths.Any(FileSystemRouter.IsRemotePath))
            {
                showProgress = true;
            }
            else if (sourcePaths != null && sourcePaths.Count <= 10)
            {
                // Issue #61: 폴더는 실제 크기를 알 수 없으므로 무조건 팝업 표시.
                // (기존 "50MB 가정 + 50MB 초과 비교"는 폴더 1개 선택 시 50MB==50MB로
                //  항상 미표시되는 경계 버그 — 100GB 폴더 복사도 진행률이 안 떴음)
                bool hasDirectory = false;
                long totalSize = 0;
                foreach (var path in sourcePaths)
                {
                    if (System.IO.File.Exists(path))
                        totalSize += new System.IO.FileInfo(path).Length;
                    else if (System.IO.Directory.Exists(path))
                        hasDirectory = true;
                }
                if (operation is DeleteFileOperation)
                {
                    // 삭제 속도는 파일 크기와 무관(휴지통 이동=rename) — 폴더(재귀 삭제로
                    // 오래 걸릴 수 있음)가 포함될 때만 팝업. 소수 파일 삭제는 즉발이라
                    // 팝업이 떴다 바로 사라지는 깜빡임을 피한다.
                    showProgress = hasDirectory;
                }
                else
                {
                    showProgress = hasDirectory || totalSize > 50 * 1024 * 1024; // 파일만이면 50MB 초과 시 팝업
                }
            }
        }
        catch { }

        if (showProgress)
        {
            dispatcherQueue.TryEnqueue(() =>
            {
                lock (_lock) { ActiveOperations.Add(entry); }
            });
        }

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
                        // Issue #61: 최종 100% 보고 등 파일명 없는 진행 보고가 표시 중인
                        // 파일명을 빈 값으로 덮어쓰지 않도록 한다 (완료 직전 공백 깜빡임 방지)
                        if (!string.IsNullOrEmpty(p.CurrentFile))
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
                        entry, OperationResult.CreateFailure(LocalizationService.L("Toast_OperationCancelled"))));
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
    /// Issue #61 후속: 임의의 비동기 작업(예: 실행 취소/다시 실행)을 진행률 패널에
    /// "진행 중" 항목으로 표시하며 실행한다. 실제 진행률을 알 수 없는 작업이므로
    /// 불확정(indeterminate) 표시이며 일시정지/취소 버튼은 노출되지 않는다.
    /// (휴지통 복원처럼 수 초가 걸리는 작업이 아무 표시 없이 진행되던 문제 대응)
    /// </summary>
    public async Task<T> RunWithIndeterminateProgressAsync<T>(
        string description,
        Func<Task<T>> work,
        Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
    {
        var id = Interlocked.Increment(ref _nextOperationId);
        var entry = new FileOperationEntry
        {
            Id = id,
            Description = description,
            Status = OperationStatus.Running,
            DispatcherQueue = dispatcherQueue,
            IsIndeterminate = true
        };

        lock (_lock) { ActiveOperations.Add(entry); }

        try
        {
            return await work();
        }
        finally
        {
            entry.Status = OperationStatus.Completed;
            if (!dispatcherQueue.TryEnqueue(() => { lock (_lock) { ActiveOperations.Remove(entry); } }))
            {
                lock (_lock) { ActiveOperations.Remove(entry); }
            }
        }
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
                // Issue #61 후속: 불확정 항목(실행취소/다시실행)은 CTS/PauseEvent가 없다.
                // 취소를 지원하지 않으므로 건너뛴다 — 무조건 Cancel()하면 NRE로 크래시한다.
                if (entry.IsIndeterminate || entry.CancellationTokenSource is null) continue;

                if (entry.Status == OperationStatus.Running || entry.Status == OperationStatus.Paused)
                {
                    if (entry.Status == OperationStatus.Paused)
                        entry.PauseEvent?.Set();
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
        // 소규모 작업(1초 미만)은 즉시 제거, 대규모 작업은 짧은 지연 후 제거
        _ = SafeDelayedRemoveAsync(entry);
    }

    private async Task SafeDelayedRemoveAsync(FileOperationEntry entry)
    {
        try
        {
            // 작업 시간이 짧으면(1초 미만) 빠르게 제거, 길면 결과 확인용 짧은 지연
            int delayMs = entry.Percentage >= 100 && entry.TotalFileCount <= 10 ? 300 : 1000;
            await Task.Delay(delayMs);

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
    public string PauseResumeTooltip => IsPaused ? LocalizationService.L("Progress_Resume") : LocalizationService.L("Progress_Pause");
    /// <summary>
    /// Issue #61: 일시정지를 실제로 지원하는 작업인지 (IPausableOperation 구현 여부).
    /// Delete는 미구현이므로 버튼을 눌러도 효과가 없다 — 되돌릴 수 없는 작업에서
    /// "멈췄다"는 잘못된 신호를 주지 않도록 버튼 자체를 비활성화한다.
    /// </summary>
    public bool IsPausable => Operation is IPausableOperation;
    public bool CanPauseOrResume => IsPausable && !IsIndeterminate && (Status == OperationStatus.Running || Status == OperationStatus.Paused);
    public bool CanCancel => !IsIndeterminate && (Status == OperationStatus.Running || Status == OperationStatus.Paused);

    /// <summary>
    /// Issue #61 후속: 진행률을 알 수 없는 작업(실행 취소 등). 퍼센트/속도 대신
    /// 불확정 표시를 쓰고, 일시정지·취소 버튼은 노출하지 않는다.
    /// </summary>
    public bool IsIndeterminate { get; init; }

    /// <summary>불확정이 아닐 때만 퍼센트 텍스트를 노출하기 위한 XAML 바인딩용.</summary>
    public Microsoft.UI.Xaml.Visibility IsNotIndeterminate
        => IsIndeterminate ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    /// <summary>Cancelling 상태일 때 표시할 상태 텍스트 (로컬라이즈 가능)</summary>
    public string StatusText => IsCancelling ? _cancellingText : "";

    /// <summary>로컬라이즈된 "취소 중..." 텍스트 설정용</summary>
    internal string _cancellingText = LocalizationService.L("Progress_Cancelling");

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
            return string.Format(LocalizationService.L("Progress_SecRemaining"), time.TotalSeconds.ToString("F0"));
        if (time.TotalMinutes < 60)
            return string.Format(LocalizationService.L("Progress_MinRemaining"), time.TotalMinutes.ToString("F0"));
        return string.Format(LocalizationService.L("Progress_HoursRemaining"), time.TotalHours.ToString("F1"));
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
