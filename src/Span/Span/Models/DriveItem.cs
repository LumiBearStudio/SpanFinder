using System;

namespace Span.Models
{
    public class DriveItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public long AvailableFreeSpace { get; set; }
        public string DriveFormat { get; set; } = string.Empty;
        public string DriveType { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = "\uEEA1"; // RemixIcon: HardDrive2Fill
    }
}
