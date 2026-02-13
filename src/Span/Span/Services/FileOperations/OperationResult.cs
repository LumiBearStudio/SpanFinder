namespace Span.Services.FileOperations;

/// <summary>
/// Represents the result of a file operation.
/// </summary>
public class OperationResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the list of file paths affected by this operation.
    /// </summary>
    public List<string> AffectedPaths { get; set; } = new();

    /// <summary>
    /// Creates a successful operation result.
    /// </summary>
    public static OperationResult CreateSuccess(params string[] affectedPaths)
    {
        return new OperationResult
        {
            Success = true,
            AffectedPaths = new List<string>(affectedPaths)
        };
    }

    /// <summary>
    /// Creates a failed operation result.
    /// </summary>
    public static OperationResult CreateFailure(string errorMessage)
    {
        return new OperationResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}
