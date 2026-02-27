using System;
using System.Runtime.InteropServices;

namespace Span.Helpers
{
    /// <summary>
    /// Win32 P/Invoke 선언 모음.
    /// - user32.dll: 커서 위치, 창 위치/크기, DWM 클로킹 (깜빡임 방지), DPI, 모니터 영역
    /// - dwmapi.dll: DWM 윈도우 속성 제어 (트랜지션 비활성화, 클로킹)
    /// - mpr.dll: 네트워크 리소스 열거 (WNetOpenEnumW, WNetEnumResourceW)
    /// - netapi32.dll: 서버 공유 폴더 열거 (NetShareEnum)
    /// </summary>
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
        internal const int DWMWA_BORDER_COLOR = 34;

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

        // 모니터 영역 검증용
        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        internal struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

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

        // ── mpr.dll — 네트워크 리소스 열거 ──

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        internal static extern int WNetOpenEnumW(
            int dwScope, int dwType, int dwUsage,
            IntPtr lpNetResource, out IntPtr lphEnum);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        internal static extern int WNetEnumResourceW(
            IntPtr hEnum, ref int lpcCount,
            IntPtr lpBuffer, ref int lpBufferSize);

        [DllImport("mpr.dll")]
        internal static extern int WNetCloseEnum(IntPtr hEnum);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        internal static extern int WNetAddConnection2W(
            ref NETRESOURCE lpNetResource,
            string? lpPassword, string? lpUsername, int dwFlags);

        // WNet constants
        internal const int RESOURCE_GLOBALNET = 0x00000002;
        internal const int RESOURCETYPE_ANY = 0x00000000;
        internal const int RESOURCETYPE_DISK = 0x00000001;
        internal const int RESOURCEUSAGE_CONTAINER = 0x00000002;
        internal const int RESOURCEDISPLAYTYPE_SERVER = 0x00000002;
        internal const int RESOURCEDISPLAYTYPE_SHARE = 0x00000003;
        internal const int NO_ERROR = 0;
        internal const int ERROR_NO_MORE_ITEMS = 259;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpLocalName;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpRemoteName;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpComment;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpProvider;
        }

        // ── netapi32.dll — 서버 공유 목록 ──

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        internal static extern int NetShareEnum(
            string serverName, int level,
            out IntPtr bufPtr, int prefMaxLen,
            out int entriesRead, out int totalEntries,
            ref int resumeHandle);

        [DllImport("netapi32.dll")]
        internal static extern int NetApiBufferFree(IntPtr buffer);

        internal const int MAX_PREFERRED_LENGTH = -1;
        internal const int NERR_Success = 0;

        // STYPE flags
        internal const uint STYPE_DISKTREE = 0x00000000;
        internal const uint STYPE_SPECIAL = 0x80000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct SHARE_INFO_1
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string shi1_netname;
            public uint shi1_type;
            [MarshalAs(UnmanagedType.LPWStr)] public string? shi1_remark;
        }
    }
}
