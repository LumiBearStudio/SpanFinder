using System;

namespace Span.Models
{
    /// <summary>
    /// File Shelf(임시 수집함)에 담긴 개별 항목.
    /// </summary>
    public class ShelfItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = string.Empty;
        public string SourceFolder { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long FileSize { get; set; }
        public DateTime AddedTime { get; set; } = DateTime.Now;
    }
}
