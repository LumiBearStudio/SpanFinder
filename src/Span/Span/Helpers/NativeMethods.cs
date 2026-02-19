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

        // DWM 클로킹 — 창을 DWM에서 합성하되 화면에 안 보이게 함 (깜빡임 방지)
        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        internal const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
        internal const int DWMWA_CLOAK = 13;

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        // 수동 드래그용: 마우스 하드웨어 상태 확인 (메시지 큐와 무관)
        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        internal const int VK_LBUTTON = 0x01;

        // SetWindowPos — MoveAndResize의 DPI 이중적용 버그를 우회 (물리 픽셀 직접 사용)
        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_NOOWNERZORDER = 0x0200;
        // 위치만 변경 (크기/Z순서/활성화 안 건드림)
        internal const uint SWP_MOVE_ONLY = SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER;

        internal static readonly IntPtr HWND_TOP = IntPtr.Zero;

        // DPI 확인용
        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr hwnd);

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
