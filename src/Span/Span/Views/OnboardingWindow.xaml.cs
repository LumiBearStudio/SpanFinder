using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Span.Services;
using WinRT.Interop;

namespace Span.Views;

/// <summary>
/// 첫 실행 시 표시되는 온보딩 창. WebView2로 HTML/CSS/JS 기반 슬라이더 UI를 렌더.
/// 설정 저장은 postMessage("complete")로 C# 쪽에 위임.
/// ClipFlow의 OnboardingWindow 패턴을 차용 (borderless popup + DWM rounded corners).
/// </summary>
public sealed partial class OnboardingWindow : Window
{
    private readonly SettingsService _settings;
    private readonly LocalizationService _loc;
    private readonly Action? _onCompleted;
    private readonly IntPtr _hwnd;

    private const int WIN_W = 1200;
    private const int WIN_H = 780;

    // ── Win32 interop (borderless popup + DWM rounded) ─────────────────
    // NativeMethods에 없는 것만 이 파일 내부에 선언.

    [DllImport("user32.dll")]
    private static extern uint GetWindowLongW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern uint SetWindowLongW(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint val, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const int GWL_STYLE = -16;
    private const uint WS_POPUP = 0x80000000u;
    private const uint WS_CAPTION_BORDER_FRAME_DLGFRAME = 0x00CF0000u;

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;

    private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

    public OnboardingWindow(SettingsService settings, LocalizationService loc, Action? onCompleted = null)
    {
        InitializeComponent();
        _settings = settings;
        _loc = loc;
        _onCompleted = onCompleted;
        _hwnd = WindowNative.GetWindowHandle(this);

        // 온보딩 창이 한 번이라도 떴으면 즉시 플래그 저장.
        // 어떤 경로(Alt+F4, 강제종료, 크래시 등)로 닫혀도 다음 실행에서 자동으로 안 뜨게 보장.
        // 사용자는 Settings의 "온보딩 다시 보기"로 재시청 가능.
        _settings.OnboardingCompleted = true;

        // OverlappedPresenter: resize/maximize/caption 비활성
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(WIN_W, WIN_H));
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.SetBorderAndTitleBar(false, false);
        }

        // ── Win32: borderless 전환 ──
        // WS_CAPTION|WS_BORDER|WS_THICKFRAME|WS_DLGFRAME 제거 + WS_POPUP 추가
        // WS_POPUP 없으면 1px 보더가 남음.
        uint style = GetWindowLongW(_hwnd, GWL_STYLE);
        style &= ~WS_CAPTION_BORDER_FRAME_DLGFRAME;
        style |= WS_POPUP;
        SetWindowLongW(_hwnd, GWL_STYLE, style);

        // SWP_FRAMECHANGED: DWM 비클라이언트 영역 캐시 플러시 (안 하면 보더 잔상)
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

        // DWM 라운드 코너 (Win11 ~8px)
        int cornerPref = DWMWCP_ROUND;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

        // DWM 보더 색상 제거 (COLOR_NONE = 0xFFFFFFFE)
        uint borderColor = 0xFFFFFFFEu;
        DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));

        // MARGINS {-1,-1,-1,-1}는 흰색 심 유발. {0,0,0,0}이 Win11에서 그림자 유지.
        var margins = new MARGINS { Left = 0, Right = 0, Top = 0, Bottom = 0 };
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);

        Title = "SPAN Finder";
        CenterOnScreen();
        _ = InitWebViewAsync();
    }

    private void CenterOnScreen()
    {
        var monitor = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        IntPtr hMonitor = MonitorFromWindow(_hwnd, MONITOR_DEFAULTTOPRIMARY);
        if (!GetMonitorInfo(hMonitor, ref monitor)) return;

        int screenW = monitor.rcWork.Right - monitor.rcWork.Left;
        int screenH = monitor.rcWork.Bottom - monitor.rcWork.Top;
        int x = monitor.rcWork.Left + (screenW - WIN_W) / 2;
        int y = monitor.rcWork.Top + (screenH - WIN_H) / 2;

        SetWindowPos(_hwnd, IntPtr.Zero, x, y, WIN_W, WIN_H,
            SWP_NOSIZE | SWP_NOZORDER);
    }

    private async System.Threading.Tasks.Task InitWebViewAsync()
    {
        try
        {
            await WebContent.EnsureCoreWebView2Async();

#if !DEBUG
            WebContent.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebContent.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif
            WebContent.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebContent.CoreWebView2.Settings.IsZoomControlEnabled = false;

            WebContent.CoreWebView2.WebMessageReceived += OnWebMessage;

            string assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Onboarding");
            WebContent.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "onboarding.span.local", assetsPath, CoreWebView2HostResourceAccessKind.Allow);

            // Span 지원 9개 언어 그대로 전달 — JS I18N에 모두 매칭됨, 누락 시 en fallback
            string lang = string.IsNullOrWhiteSpace(_loc.Language) ? "en" : _loc.Language;

            WebContent.CoreWebView2.Navigate(
                $"https://onboarding.span.local/index.html?lang={Uri.EscapeDataString(lang)}");
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[Onboarding] WebView2 init failed: {ex.Message}");
            CompleteOnboarding();
        }
    }

    // async void: WebView2 이벤트 핸들러 시그니처에 강제됨. 내부 try/catch로 안전성 확보.
    private async void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var msg = args.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(msg)) return;

            // 테마 즉시 적용 — _settings.Theme 세터가 SettingChanged 발행 → MainWindow ApplyTheme 자동 호출
            if (msg.StartsWith("theme:", StringComparison.Ordinal))
            {
                var theme = msg.Substring("theme:".Length);
                if (theme == "system" || theme == "light" || theme == "dark")
                {
                    _settings.Theme = theme;
                }
                return;
            }

            // 완료. dfm 옵션 있으면 SetAsDefault 끝까지 기다린 후 창 닫음.
            if (msg == "complete" || msg == "complete:dfm")
            {
                if (msg == "complete:dfm")
                {
                    Helpers.DebugLogger.Log("[Onboarding] DFM requested — invoking SetAsDefaultAsync (await)");
                    await TrySetDefaultFileManagerAsync();
                }
                CompleteOnboarding();
            }
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[Onboarding] OnWebMessage failed: {ex.Message}");
            // 어떤 예외든 온보딩은 닫아야 함 (사용자가 '시작하기' 눌렀음)
            CompleteOnboarding();
        }
    }

    private async System.Threading.Tasks.Task TrySetDefaultFileManagerAsync()
    {
        try
        {
            var dfm = App.Current.Services.GetService(typeof(DefaultFileManagerService)) as DefaultFileManagerService;
            if (dfm == null)
            {
                Helpers.DebugLogger.Log("[Onboarding] DefaultFileManagerService not registered in DI");
                return;
            }
            var ok = await dfm.SetAsDefaultAsync();
            Helpers.DebugLogger.Log($"[Onboarding] SetAsDefault → {ok} (IsDefault now: {dfm.IsDefault()})");

            // 주석: explorer.exe 강제 재시작 — 사용자 작업 날리는 위험 때문에 비활성
            // 근본 원인 조사 중. 필요 시 사용자 confirm 후에만.
            // if (ok) DefaultFileManagerService.RestartExplorer();
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[Onboarding] SetAsDefault failed: {ex.Message}");
        }
    }

    private void CompleteOnboarding()
    {
        _settings.OnboardingCompleted = true;

        DispatcherQueue.TryEnqueue(() =>
        {
            try { _onCompleted?.Invoke(); } catch { }
            Close();
        });
    }
}
