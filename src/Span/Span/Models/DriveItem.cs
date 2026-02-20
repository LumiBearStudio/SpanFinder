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
        public string IconGlyph { get; set; } = "\uEC65"; // RemixIcon: ri-drive-fill

        public bool IsNetworkDrive => DriveType == "Network";

        /// <summary>
        /// Usage percentage (0-100). Returns 0 if TotalSize is 0.
        /// </summary>
        public double UsagePercent =>
            TotalSize > 0 ? Math.Round((double)(TotalSize - AvailableFreeSpace) / TotalSize * 100, 1) : 0;

        /// <summary>
        /// Human-readable size description: "X GB free of Y GB"
        /// </summary>
        public string SizeDescription
        {
            get
            {
                if (TotalSize <= 0) return string.Empty;
                return $"{FormatSize(AvailableFreeSpace)} free of {FormatSize(TotalSize)}";
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1L << 40) return $"{bytes / (double)(1L << 40):F1} TB";
            if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):F1} GB";
            if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):F1} MB";
            return $"{bytes / (double)(1L << 10):F1} KB";
        }
    }
}
