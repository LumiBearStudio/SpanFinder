using System;

namespace Span.Models
{
    public interface IFileSystemItem
    {
        string Name { get; set; }
        string Path { get; set; }
        string IconGlyph { get; }
        bool IsHidden { get; set; }
    }
}
