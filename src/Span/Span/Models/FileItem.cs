using System;

namespace Span.Models
{
    public class FileItem : IFileSystemItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime DateModified { get; set; }
        public string IconGlyph => Span.Services.IconService.Current?.FileDefaultGlyph ?? "\uECE0";
        public string FileType { get; set; } = string.Empty;
    }
}
