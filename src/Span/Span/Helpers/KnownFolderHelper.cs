using System;
using System.Runtime.InteropServices;

namespace Span.Helpers
{
    internal static class KnownFolderHelper
    {
        private static string? _cachedDownloadsPath;

        public static bool IsDownloadsFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var downloadsPath = GetDownloadsPath();
            if (string.IsNullOrEmpty(downloadsPath)) return false;
            return path.Equals(downloadsPath, StringComparison.OrdinalIgnoreCase);
        }

        public static string? GetDownloadsPath()
        {
            if (_cachedDownloadsPath != null) return _cachedDownloadsPath;
            try
            {
                var guid = new Guid("374DE290-123F-4565-9164-39C4925E467B");
                int hr = NativeMethods.SHGetKnownFolderPath(ref guid, 0, IntPtr.Zero, out var ptr);
                if (hr == 0 && ptr != IntPtr.Zero)
                {
                    _cachedDownloadsPath = Marshal.PtrToStringUni(ptr);
                    Marshal.FreeCoTaskMem(ptr);
                }
            }
            catch { }
            return _cachedDownloadsPath;
        }
    }
}
