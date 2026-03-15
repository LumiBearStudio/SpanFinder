using System;
using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Span.Services;
using Span.Services.FileOperations;

namespace Span.ViewModels;

/// <summary>
/// ViewModel for the file operation progress panel.
/// Supports multiple concurrent operations, each with pause/resume/cancel.
/// Also retains backward-compatible single-operation properties for simple use cases.
/// </summary>
public partial class FileOperationProgressViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveOperations))]
    private bool _isVisible = false;

    [ObservableProperty]
    private string _operationDescription = string.Empty;

    [ObservableProperty]
    private string _currentFile = string.Empty;

    [ObservableProperty]
    private int _percentage = 0;

    [ObservableProperty]
    private string _speedText = string.Empty;

    [ObservableProperty]
    private string _remainingTimeText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileCountText))]
    private int _currentFileIndex = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileCountText))]
    private int _totalFileCount = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseResumeIcon))]
    [NotifyPropertyChangedFor(nameof(PauseResumeTooltip))]
    private bool _isPaused = false;

    public string FileCountText => string.Format(LocalizationService.L("Progress_FileCount"), CurrentFileIndex, TotalFileCount);

    /// <summary>Segoe MDL2 glyph: Play or Pause.</summary>
    public string PauseResumeIcon => IsPaused ? "\uE768" : "\uE769";
    public string PauseResumeTooltip => IsPaused ? LocalizationService.L("Progress_Resume") : LocalizationService.L("Progress_Pause");

    public bool HasActiveOperations => IsVisible;

    private CancellationTokenSource? _cancellationTokenSource;

    public CancellationTokenSource? CancellationTokenSource
    {
        get => _cancellationTokenSource;
        set => _cancellationTokenSource = value;
    }

    /// <summary>
    /// Reference to the FileOperationManager for multi-operation commands.
    /// Set by MainViewModel after construction.
    /// </summary>
    public FileOperationManager? OperationManager { get; set; }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        IsVisible = false;
    }

    [RelayCommand]
    private void Pause()
    {
        // Single-operation pause/resume: toggle the pause event on the legacy CTS path
        // For multi-operation mode, the per-entry pause is handled by PauseOperation/ResumeOperation commands
        IsPaused = !IsPaused;
    }

    /// <summary>
    /// Pauses or resumes a specific operation by ID.
    /// </summary>
    [RelayCommand]
    private void TogglePauseOperation(int operationId)
    {
        OperationManager?.TogglePause(operationId);
    }

    /// <summary>
    /// Cancels a specific operation by ID.
    /// </summary>
    [RelayCommand]
    private void CancelOperation(int operationId)
    {
        OperationManager?.CancelOperation(operationId);
    }

    /// <summary>
    /// Cancels all running operations.
    /// </summary>
    [RelayCommand]
    private void CancelAll()
    {
        OperationManager?.CancelAll();
    }

    /// <summary>
    /// Updates progress from legacy single-operation path (FileOperationHistory).
    /// </summary>
    public void UpdateProgress(FileOperationProgress progress)
    {
        CurrentFile = progress.CurrentFile;
        Percentage = progress.Percentage;
        CurrentFileIndex = progress.CurrentFileIndex;
        TotalFileCount = progress.TotalFileCount;
        SpeedText = FormatSpeed(progress.SpeedBytesPerSecond);
        RemainingTimeText = FormatTime(progress.EstimatedTimeRemaining);
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024)
            return $"{bytesPerSecond:F0} B/s";
        if (bytesPerSecond < 1024 * 1024)
            return $"{bytesPerSecond / 1024:F1} KB/s";
        if (bytesPerSecond < 1024 * 1024 * 1024)
            return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
        return $"{bytesPerSecond / (1024 * 1024 * 1024):F1} GB/s";
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time.TotalSeconds < 60)
            return string.Format(LocalizationService.L("Progress_SecRemaining"), time.TotalSeconds.ToString("F0"));
        if (time.TotalMinutes < 60)
            return string.Format(LocalizationService.L("Progress_MinRemaining"), time.TotalMinutes.ToString("F0"));
        return string.Format(LocalizationService.L("Progress_HoursRemaining"), time.TotalHours.ToString("F1"));
    }
}
