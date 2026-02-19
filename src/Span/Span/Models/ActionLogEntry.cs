namespace Span.Models;

public class ActionLogEntry
{
    public DateTime Timestamp { get; set; }
    public string OperationType { get; set; } = string.Empty;  // Copy, Move, Delete, Rename, NewFolder
    public List<string> SourcePaths { get; set; } = new();
    public string? DestinationPath { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string Description { get; set; } = string.Empty;
    public int ItemCount { get; set; }
}
