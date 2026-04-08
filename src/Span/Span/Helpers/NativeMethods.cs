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
        internal static extern bool SetCursorPos(int x, int y);

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
        internal const int DWMWA_CAPTION_COLOR = 35;

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        internal const int SW_SHOW = 5;
        internal const int SW_RESTORE = 9;

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

        // 키보드 입력 시뮬레이션 (서브메뉴 열기용)
        [DllImport("user32.dll")]
        internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

        // 마우스 입력 시뮬레이션 (드래그 중 DragOver 강제 재발생용)
        [DllImport("user32.dll")]
        internal static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, nuint dwExtraInfo);
        internal const uint MOUSEEVENTF_MOVE = 0x0001;

        // Modifier key 가상키 상수 (드래그 중 modifier key 감지용 — GetAsyncKeyState와 함께 사용)
        internal const int VK_SHIFT = 0x10;
        internal const int VK_CONTROL = 0x11;
        internal const int VK_MENU = 0x12; // Alt

        // PostMessage — 커서 이동 없이 WM_MOUSEMOVE를 윈도우 메시지 큐에 삽입
        [DllImport("user32.dll")]
        internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

        internal const uint WM_MOUSEMOVE = 0x0200;
        internal const uint MK_SHIFT = 0x0004;
        internal const uint MK_CONTROL = 0x0008;
        internal const uint MK_LBUTTON = 0x0001;

        // Layered Window (반투명 윈도우)
        [DllImport("user32.dll")]
        internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        internal static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        // Owner window 설정용 (64비트 안전)
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        internal static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        internal const int GWLP_HWNDPARENT = -8;

        internal const int GWL_EXSTYLE = -20;
        internal const int WS_EX_LAYERED = 0x00080000;
        internal const uint LWA_ALPHA = 0x2;

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

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        internal static extern int WNetGetConnectionW(
            string lpLocalName, System.Text.StringBuilder lpRemoteName, ref int lpnLength);

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

        // ── shell32.dll — 로컬라이즈된 폴더 이름 해석 ──

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern int SHParseDisplayName(
            string pszName, IntPtr pbc,
            out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SHGetPathFromIDListW(IntPtr pidl, System.Text.StringBuilder pszPath);

        [DllImport("shell32.dll")]
        internal static extern int SHGetKnownFolderPath(
            ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

        [DllImport("shell32.dll")]
        internal static extern int SHGetKnownFolderIDList(
            ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppidl);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern int SHGetNameFromIDList(
            IntPtr pidl, uint sigdnName,
            [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

        internal const uint SIGDN_NORMALDISPLAY = 0x00000000;

        // ── ole32.dll — COM 메모리 해제 ──

        [DllImport("ole32.dll")]
        internal static extern void CoTaskMemFree(IntPtr pv);

        // ── shell32.dll — 휴지통 API ──

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern int SHQueryRecycleBin(
            string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint SHEmptyRecycleBin(
            IntPtr hwnd, string? pszRootPath, uint dwFlags);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        internal const uint SHERB_NOCONFIRMATION = 0x00000001;
        internal const uint SHERB_NOPROGRESSUI   = 0x00000002;
        internal const uint SHERB_NOSOUND        = 0x00000004;

        // ── kernel32.dll — 스레드 에러 모드 제어 ──

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetThreadErrorMode(uint dwNewMode, out uint lpOldMode);

        internal const uint SEM_FAILCRITICALERRORS = 0x0001;
        internal const uint SEM_NOGPFAULTERRORBOX = 0x0002;
        internal const uint SEM_NOOPENFILEERRORBOX = 0x8000;

        // ── Safe Wrappers (IntPtr.Zero guard + return value check) ──

        /// <summary>
        /// DwmSetWindowAttribute를 안전하게 호출. hwnd가 IntPtr.Zero이면 무시.
        /// </summary>
        internal static bool SafeDwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute)
        {
            if (hwnd == IntPtr.Zero) return false;
            return DwmSetWindowAttribute(hwnd, dwAttribute, ref pvAttribute, cbAttribute) == 0;
        }

        /// <summary>
        /// SetWindowPos를 안전하게 호출. hwnd가 IntPtr.Zero이면 무시.
        /// </summary>
        internal static bool SafeSetWindowPos(IntPtr hwnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags)
        {
            if (hwnd == IntPtr.Zero) return false;
            return SetWindowPos(hwnd, hWndInsertAfter, x, y, cx, cy, uFlags);
        }

        /// <summary>
        /// SetForegroundWindow를 안전하게 호출. hwnd가 IntPtr.Zero이면 무시.
        /// </summary>
        internal static bool SafeSetForegroundWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            return SetForegroundWindow(hwnd);
        }

        // ── OLE Clipboard — 가상 파일(RDP, Outlook 등) 지원 ──

        [DllImport("ole32.dll")]
        internal static extern int OleGetClipboard(
            [MarshalAs(UnmanagedType.Interface)] out System.Runtime.InteropServices.ComTypes.IDataObject ppDataObj);

        [DllImport("ole32.dll")]
        internal static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM pmedium);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        internal static extern UIntPtr GlobalSize(IntPtr hMem);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern ushort RegisterClipboardFormatW(string lpszFormat);
    }
}
