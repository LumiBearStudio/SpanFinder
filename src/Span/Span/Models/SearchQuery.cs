using System;

namespace Span.Models
{
    /// <summary>
    /// File type categories for kind: filter.
    /// </summary>
    public enum FileKind
    {
        Image,
        Video,
        Audio,
        Document,
        Archive,
        Code,
        Executable,
        Font
    }

    /// <summary>
    /// Comparison operators for size: and date: filters.
    /// </summary>
    public enum CompareOp
    {
        GreaterThan,
        LessThan,
        Equals,
        GreaterOrEqual,
        LessOrEqual
    }

    /// <summary>
    /// Parsed search query representing the user's search intent.
    /// Supports advanced query syntax: kind:, size:, date:, ext:, and plain text name filtering.
    /// Multiple filters are combined with AND logic.
    /// </summary>
    public class SearchQuery
    {
        /// <summary>
        /// Plain text name filter (case-insensitive contains match).
        /// </summary>
        public string? NameFilter { get; set; }

        /// <summary>
        /// File type category filter (e.g., kind:image, kind:document).
        /// </summary>
        public FileKind? KindFilter { get; set; }

        /// <summary>
        /// Size comparison filter (e.g., size:>1MB, size:&lt;100KB).
        /// Item1 = comparison operator, Item2 = size in bytes.
        /// </summary>
        public (CompareOp Op, long Bytes)? SizeFilter { get; set; }

        /// <summary>
        /// Date comparison filter (e.g., date:today, date:>2024-01-01).
        /// Item1 = comparison operator, Item2 = reference date.
        /// </summary>
        public (CompareOp Op, DateTime Date)? DateFilter { get; set; }

        /// <summary>
        /// File extension filter (e.g., ext:.pdf, ext:.txt).
        /// Stored with leading dot.
        /// </summary>
        public string? ExtensionFilter { get; set; }

        /// <summary>
        /// Returns true if no filters are set (empty query).
        /// </summary>
        public bool IsEmpty =>
            string.IsNullOrEmpty(NameFilter) &&
            KindFilter == null &&
            SizeFilter == null &&
            DateFilter == null &&
            string.IsNullOrEmpty(ExtensionFilter);
    }
}
