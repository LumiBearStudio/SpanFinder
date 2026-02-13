namespace Span.Services.FileOperations;

/// <summary>
/// Represents the progress of a file operation.
/// </summary>
public class FileOperationProgress
{
    /// <summary>
    /// Gets or sets the name of the current file being processed.
    /// </summary>
    public string CurrentFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total number of bytes to process.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes already processed.
    /// </summary>
    public long ProcessedBytes { get; set; }

    /// <summary>
    /// Gets or sets the percentage of completion (0-100).
    /// When set explicitly, the assigned value is used.
    /// Otherwise, it is computed from ProcessedBytes and TotalBytes.
    /// </summary>
    private int? _percentage;
    public int Percentage
    {
        get => _percentage ?? (TotalBytes > 0 ? (int)(ProcessedBytes * 100 / TotalBytes) : 0);
        set => _percentage = value;
    }

    /// <summary>
    /// Gets or sets the current processing speed in bytes per second.
    /// </summary>
    public double SpeedBytesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the estimated time remaining to complete the operation.
    /// </summary>
    public TimeSpan EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Gets or sets the index of the current file (1-based).
    /// </summary>
    public int CurrentFileIndex { get; set; }

    /// <summary>
    /// Gets or sets the total number of files to process.
    /// </summary>
    public int TotalFileCount { get; set; }
}
