using System;
using System.Runtime.InteropServices;

namespace Span.Helpers
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out POINT pt);

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        internal const uint WM_SYSCOMMAND = 0x0112;
        internal const int SC_DRAGMOVE = 0xF012;

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
