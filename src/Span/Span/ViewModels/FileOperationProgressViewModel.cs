using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Span.Services.FileOperations;

namespace Span.ViewModels;

public partial class FileOperationProgressViewModel : ObservableObject
{
    [ObservableProperty]
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

    public string FileCountText => $"{CurrentFileIndex} / {TotalFileCount} files";

    private CancellationTokenSource? _cancellationTokenSource;

    public CancellationTokenSource? CancellationTokenSource
    {
        get => _cancellationTokenSource;
        set => _cancellationTokenSource = value;
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        IsVisible = false;
    }

    [RelayCommand]
    private void Pause()
    {
        // TODO: Implement pause logic
    }

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
            return $"{time.TotalSeconds:F0} sec";
        if (time.TotalMinutes < 60)
            return $"{time.TotalMinutes:F0} min";
        return $"{time.TotalHours:F1} hours";
    }
}
