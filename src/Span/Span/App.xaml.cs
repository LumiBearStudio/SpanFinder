using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Span.ViewModels;

namespace Span
{
    /// <summary>
    /// Span 파일 탐색기 애플리케이션의 진입점.
    /// DI 컨테이너(ServiceCollection) 구성, 멀티 윈도우 등록/해제 관리,
    /// 글로벌 예외 처리(UI/AppDomain/Task), 크래시 리포팅(Sentry),
    /// 아이콘 팩 로드 및 리소스 오버라이드, 언어 설정 적용을 담당한다.
    /// </summary>
    public partial class App : Application
    {
        public IServiceProvider Services { get; }
        public new static App Current => (App)Application.Current;

        // Multi-window tracking
        private readonly List<Window> _windows = new();
        private readonly object _windowLock = new();

        public App()
        {
            this.InitializeComponent();
            ApplySystemAccentColor();

            // FontScale 싱글톤은 App.xaml 의 <helpers:FontScaleService x:Key="FontScale"/> 로
            // XAML 파서가 이미 Resources 에 등록함. C# 코드는 FontScaleService.Instance 프로퍼티가
            // Application.Current.Resources["FontScale"] 을 통해 동일 객체를 반환.
            // 코드에서 this.Resources["FontScale"] = new FontScaleService() 방식은 WinUI 3 의
            // XamlMetadataProvider 미등록 타입 marshaling 실패로 COMException 크래시 발생함 (v1.3.1.0 버그).

            Services = ConfigureServices();

            // Initialize Sentry crash reporting (must be early, before any exception can occur)
            var crashService = Services.GetRequiredService<Services.CrashReportingService>();
            crashService.Initialize();

            // Phase 0: WER LocalDumps 등록 — 다음 네이티브 크래시부터 자동 미니덤프 생성
            // .NET 핸들러가 못 잡는 AccessViolation/SEH 예외 (이슈 #23 본질) 추적용
            Helpers.WerHelper.EnsureRegistered();

            // Phase 0: 이전 세션 비정상 종료 감지 + 누적된 WER dump 업로드 (백그라운드)
            // - 이전 로그 마지막 줄에 [Shutdown] clean exit 마커 없으면 post-mortem 이벤트 전송
            // - %LocalAppData%\Span\CrashDumps\*.dmp를 Sentry attachment로 업로드 후 삭제
            // I10: discard + ContinueWith로 unobserved exception 방지
            _ = Task.Run(() =>
            {
                try { crashService.DetectPreviousAbnormalExitAndUploadDumps(); }
                catch (Exception ex) { Helpers.DebugLogger.Log($"[App] post-mortem detect failed: {ex.Message}"); }
            }).ContinueWith(t =>
            {
                if (t.Exception != null)
                    Helpers.DebugLogger.Log($"[App] post-mortem task faulted: {t.Exception.Flatten().Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);

            // Issue #36: Regex 기본 타임아웃은 Program.Main 첫 줄에서 건다. 여기서는 이미 늦다 —
            // 위의 Sentry 초기화가 Regex를 먼저 만든다. 실제로 적용됐는지 로그로 남긴다.
            // "00:00:01"이 아니면 누군가 Main보다 앞에서 Regex를 만든 것이다.
            Helpers.DebugLogger.Log($"[App] Regex default match timeout = {new System.Text.RegularExpressions.Regex("x").MatchTimeout}");

            // UI thread unhandled exceptions
            this.UnhandledException += OnUnhandledException;

            // Background thread / Task exceptions
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Single instance: 기존 인스턴스로 리다이렉트된 활성화 이벤트 수신
            AppInstance.GetCurrent().Activated += OnAppActivated;
        }

        /// <summary>
        /// Windows 시스템 액센트 색상을 읽어 SpanAccent* 리소스를 런타임에 덮어쓴다.
        /// InitializeComponent 후, 윈도우 생성 전에 호출해야 한다.
        /// </summary>
        /// <summary>
        /// Windows 시스템 액센트 색상을 앱 전체에 적용.
        /// ThemeDictionaries (XAML {ThemeResource}용) + 최상위 Resources (코드-비하인드 조회용) 둘 다 덮어씀.
        /// </summary>
        private void ApplySystemAccentColor()
        {
            try
            {
                var uiSettings = new Windows.UI.ViewManagement.UISettings();
                var accent = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
                var c = new Windows.UI.Color { A = accent.A, R = accent.R, G = accent.G, B = accent.B };
                var hex = $"{c.R:X2}{c.G:X2}{c.B:X2}";

                var hoverColor = new Windows.UI.Color { A = 255, R = (byte)Math.Min(c.R + 30, 255), G = (byte)Math.Min(c.G + 30, 255), B = (byte)Math.Min(c.B + 30, 255) };
                var dimColor = new Windows.UI.Color { A = 179, R = c.R, G = c.G, B = c.B };

                var accentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(c);
                var hoverBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(hoverColor);
                var dimBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(dimColor);

                // --- ThemeDictionaries (XAML {ThemeResource} 바인딩용) ---
                foreach (var entry in Resources.ThemeDictionaries)
                {
                    if (entry.Value is not ResourceDictionary rd) continue;

                    rd["SpanAccentColor"] = c;
                    rd["SpanAccentHoverColor"] = hoverColor;
                    rd["SpanAccentDimColor"] = dimColor;

                    rd["SpanAccentBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(c);
                    rd["SpanAccentHoverBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(hoverColor);
                    rd["SpanAccentDimBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(dimColor);

                    rd["SpanBgHoverBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorFromHex($"#0F{hex}"));
                    rd["SpanBgActiveBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorFromHex($"#1A{hex}"));
                    rd["SpanBgSelectedBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorFromHex($"#25{hex}"));
                    rd["SpanBgSelectedHoverBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorFromHex($"#30{hex}"));
                    rd["SpanPathHighlightBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorFromHex($"#60{hex}"));

                    rd["SpanSelectionRectFillBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(c) { Opacity = 0.15 };
                    rd["SpanSelectionRectStrokeBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(c) { Opacity = 0.6 };

                    // WinUI 3 시스템 컨트롤 accent 오버라이드 (NavigationView 인디케이터, ToggleSwitch 등)
                    rd["NavigationViewSelectionIndicatorForeground"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(c);
                    rd["ListViewItemSelectionIndicatorBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(c);
                    rd["AccentFillColorDefaultBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(c);
                    rd["AccentFillColorSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(hoverColor);
                    rd["AccentFillColorTertiaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(dimColor);

                    // Global FocusVisual: 시스템 액센트 톤 포커스 링
                    rd["SystemControlFocusVisualPrimaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(dimColor);
                    rd["SystemControlFocusVisualSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

                    // TextBox/AutoSuggestBox 포커스 하단 라인
                    rd["TextControlBorderBrushFocused"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(c);
                }

                // --- 최상위 Resources (코드-비하인드 Resources["key"] 조회용) ---
                Resources["SpanAccentColor"] = c;
                Resources["SpanAccentBrush"] = accentBrush;
                Resources["SpanAccentHoverBrush"] = hoverBrush;
                Resources["SpanAccentDimBrush"] = dimBrush;

                // ListView/GridView selection
                Resources["ListViewItemBackgroundSelected"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorFromHex($"#25{hex}"));
                Resources["ListViewItemBackgroundSelectedPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorFromHex($"#30{hex}"));
                Resources["ListViewItemBackgroundSelectedPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorFromHex($"#1A{hex}"));
                Resources["GridViewItemBackgroundSelected"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorFromHex($"#25{hex}"));
                Resources["GridViewItemBackgroundSelectedPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorFromHex($"#30{hex}"));
                Resources["GridViewItemBackgroundSelectedPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorFromHex($"#1A{hex}"));
            }
            catch { /* fallback: XAML에 정의된 기본값 사용 */ }
        }

        // 실행 인자 판정은 Helpers/LaunchArguments(순수 함수, 단위 테스트)에 있다.
        private static bool IsRecycleBinArgument(string? arg) => Helpers.LaunchArguments.IsRecycleBin(arg);
        private static bool IsVirtualFolderArgument(string? arg) => Helpers.LaunchArguments.IsVirtualFolder(arg);
        private static bool IsThisPCArgument(string? arg) => Helpers.LaunchArguments.IsThisPC(arg);

        /// <summary>
        /// 가상 폴더를 explorer.exe에 위임.
        /// </summary>
        private static void DelegateToExplorer(string path)
        {
            try
            {
                // CLSID 끝의 \0 제거 (Windows가 null terminator를 붙이는 경우)
                path = path.TrimEnd('\0');
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[App] DelegateToExplorer failed: {ex.Message}");
            }
        }

        /// <summary>The Jump List "New window" task (JumpListService). It is a command, not a path.</summary>
        internal static bool IsNewWindowArgument(string? arg) => Helpers.LaunchArguments.IsNewWindow(arg);

        private static Helpers.LaunchRequest ParseLaunchArguments(string? raw) =>
            Helpers.LaunchArguments.Parse(raw, System.IO.Directory.Exists, System.IO.File.Exists);

        /// <summary>--select 대상이 있거나, 없어도 부모 폴더가 있는지.</summary>
        private static bool IsSelectTargetReachable(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (System.IO.Directory.Exists(path) || System.IO.File.Exists(path)) return true;
                var trimmed = path.Length > 3 ? path.TrimEnd('\\', '/') : path;
                var parent = System.IO.Path.GetDirectoryName(trimmed);
                return !string.IsNullOrEmpty(parent) && System.IO.Directory.Exists(parent);
            }
            catch
            {
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr GetCommandLineW();

        /// <summary>프로세스의 원본 명령줄(WinAppSDK가 LaunchActivatedEventArgs를 만들 때 쓰는 것과 같은 값).</summary>
        private static string? GetProcessCommandLine()
        {
            try { return System.Runtime.InteropServices.Marshal.PtrToStringUni(GetCommandLineW()); }
            catch { return null; }
        }

        private static Windows.UI.Color ColorFromHex(string hex)
        {
            hex = hex.TrimStart('#');
            return new Windows.UI.Color
            {
                A = byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber),
                R = byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber),
                G = byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber),
                B = byte.Parse(hex[6..8], System.Globalization.NumberStyles.HexNumber)
            };
        }

        public void RegisterWindow(Window w)
        {
            bool isFirst;
            lock (_windowLock)
            {
                if (!_windows.Contains(w))
                    _windows.Add(w);
                isFirst = _windows.Count == 1;
            }

            // Lazy-initialize the tray icon on first window registration.
            // Subsequent registrations (tear-off) just reuse the single shared icon.
            //
            // Defer to DispatcherQueue so the window's XAML content (and compositor)
            // is fully ready — TaskbarIcon.ForceCreate() needs a live visual context.
            if (isFirst)
            {
                try
                {
                    // Instantiate now so SettingChanged subscription is active.
                    var tray = Services.GetService(typeof(Services.TrayIconService)) as Services.TrayIconService;

                    w.DispatcherQueue?.TryEnqueue(() =>
                    {
                        try { tray?.SyncWithSetting(); }
                        catch (Exception ex) { Helpers.DebugLogger.Log($"[App] deferred TrayIcon sync failed: {ex.Message}"); }
                    });
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[App] TrayIcon init failed: {ex.Message}");
                }
            }
        }

        public void UnregisterWindow(Window w)
        {
            lock (_windowLock)
            {
                _windows.Remove(w);
                if (_windows.Count == 0)
                {
                    Helpers.DebugLogger.Log("[App] Last window closed — force-killing process to avoid WinUI teardown hang");
                    Helpers.DebugLogger.Shutdown();

                    // Dispose tray icon BEFORE Process.Kill so the notification area
                    // icon is removed cleanly instead of lingering as a ghost until
                    // Windows detects the dead process and cleans up.
                    try
                    {
                        (Services.GetService(typeof(Services.TrayIconService)) as Services.TrayIconService)?.Dispose();
                    }
                    catch { }

                    // Flush Sentry events before force-killing the process
                    try { Sentry.SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); } catch { }

                    // Force-kill BEFORE WinUI's native teardown can crash or hang.
                    // Environment.Exit(0) can deadlock when called during active COM/OLE
                    // drag-and-drop or XAML resource cleanup (WinUI 3 known issue).
                    // Process.Kill() bypasses all finalizers and COM locks.
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                }
            }
        }

        /// <summary>
        /// Get a snapshot of all registered windows (for cross-window tab operations).
        /// </summary>
        public IReadOnlyList<Window> GetRegisteredWindows()
        {
            lock (_windowLock)
            {
                return _windows.ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// Find another MainWindow whose tab bar area contains the given screen point.
        /// Used for tab re-docking (merging a torn-off tab back into another window).
        /// </summary>
        public MainWindow? FindWindowAtPoint(int screenX, int screenY, Window exclude)
        {
            lock (_windowLock)
            {
                foreach (var w in _windows)
                {
                    if (w == exclude || w is not MainWindow mw) continue;

                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(w);
                    if (Helpers.NativeMethods.GetWindowRect(hwnd, out var rect))
                    {
                        // Check if point is within the window's tab bar area
                        // Tab bar = 40 DIPs; GetWindowRect returns physical pixels → scale by DPI
                        uint dpi = Helpers.NativeMethods.GetDpiForWindow(hwnd);
                        int tabBarHeight = (int)(48.0 * dpi / 96.0); // 40 DIPs + 8 DIP buffer
                        if (screenX >= rect.Left && screenX <= rect.Right &&
                            screenY >= rect.Top && screenY <= rect.Top + tabBarHeight)
                        {
                            return mw;
                        }
                    }
                }
            }
            return null;
        }

        private int _crashCount;
        private int _sentryCaptureCount;
        private DateTime _lastCrashTime;
        private const int MaxSentryCapturesPerSession = 5;
        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            var now = DateTime.UtcNow;
            _crashCount++;
            // 반복 크래시 억제: 첫 3회만 상세 기록, 이후 10초 간격으로 요약만
            if (_crashCount <= 3 || (now - _lastCrashTime).TotalSeconds >= 10)
            {
                Helpers.DebugLogger.LogCrash("UI.UnhandledException", e.Exception);
                Helpers.DebugLogger.LogCrash("UI.Detail",
                    new InvalidOperationException($"Message='{e.Message}' HRESULT=0x{e.Exception?.HResult:X8} count={_crashCount}"));
                Helpers.DebugLogger.Log($"[CRASH] UnhandledException: {e.Exception?.GetType().FullName}: {e.Exception?.Message}");
                Helpers.DebugLogger.Log($"[CRASH] StackTrace: {e.Exception?.StackTrace}");
                if (e.Exception?.InnerException != null)
                    Helpers.DebugLogger.Log($"[CRASH] Inner: {e.Exception.InnerException.GetType().FullName}: {e.Exception.InnerException.Message}\n{e.Exception.InnerException.StackTrace}");
            }
            _lastCrashTime = now;
            // WinUI XAML 내부 렌더링 타이밍 이슈 — Sentry 노이즈 필터링
            // - E_INVALIDARG (0x80070057): 빠른 스크롤/뷰모드 전환 시 BitmapImage 렌더링 경합
            // - E_FAIL (0x80004005): ArrangeOverride/MeasureOverride 등 레이아웃 경합
            //   → 스택트레이스 없거나, WinUI/WinRT 내부 프레임만 있는 경우 모두 포함
            bool isXamlRenderingNoise =
                (e.Exception is ArgumentException && e.Exception.HResult == unchecked((int)0x80070057)
                    && IsWinUIInternalOnly(e.Exception))
                || (e.Exception is System.Runtime.InteropServices.COMException && e.Exception.HResult == unchecked((int)0x80004005)
                    && IsWinUIInternalOnly(e.Exception))
                || (e.Exception is System.Runtime.InteropServices.COMException && e.Exception.HResult == unchecked((int)0x80070490)
                    && IsWinUIInternalOnly(e.Exception)); // Element not found — visual tree 타이밍 이슈

            // Sentry: 세션당 최대 N회만 전송 (반복 네이티브 예외 증폭 방지)
            // CaptureFatalException(동기 Flush)은 사용하지 않음 — UI 스레드 차단 위험
            if (!isXamlRenderingNoise && _sentryCaptureCount < MaxSentryCapturesPerSession)
            {
                _sentryCaptureCount++;
                try
                {
                    var extras = new Dictionary<string, string> { ["crashCount"] = _crashCount.ToString() };

                    // Inner exception 체인 기록 — XamlParseException 등이 실제 원인을 래핑
                    var inner = e.Exception?.InnerException;
                    for (int i = 1; inner != null && i <= 5; i++, inner = inner.InnerException)
                    {
                        extras[$"innerException{i}.type"] = inner.GetType().FullName ?? "unknown";
                        extras[$"innerException{i}.message"] = inner.Message ?? "";
                        extras[$"innerException{i}.stackTrace"] = inner.StackTrace ?? "(no stack)";
                    }

                    var crashSvc = Services.GetRequiredService<Services.CrashReportingService>();
                    crashSvc.CaptureException(e.Exception, "UI.UnhandledException", extras);
                }
                catch { }
            }
            e.Handled = true;
        }

        /// <summary>
        /// 스택트레이스가 비어있거나 WinUI/WinRT 내부 프레임만 포함하는지 확인.
        /// Sentry에서 in_app_frame_mix="system-only"로 분류되는 이벤트를 로컬에서 필터링.
        /// </summary>
        private static bool IsWinUIInternalOnly(Exception? ex)
        {
            if (ex == null) return false;
            var trace = ex.StackTrace;
            if (string.IsNullOrEmpty(trace)) return true;

            // 각 프레임 라인을 검사 — 앱 코드(Span. 네임스페이스)가 하나라도 있으면 false
            foreach (var line in trace.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || !trimmed.StartsWith("at ", StringComparison.Ordinal))
                    continue;
                // WinUI(Microsoft.UI/Microsoft.WinUI), WinRT, System 네임스페이스는 내부 프레임
                if (trimmed.StartsWith("at Microsoft.", StringComparison.Ordinal)
                    || trimmed.StartsWith("at WinRT.", StringComparison.Ordinal)
                    || trimmed.StartsWith("at System.", StringComparison.Ordinal)
                    || trimmed.StartsWith("at Windows.", StringComparison.Ordinal))
                    continue;
                // 앱 코드 프레임 발견
                return false;
            }
            return true;
        }

        private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            Helpers.DebugLogger.LogCrash("AppDomain.UnhandledException", ex);
            // Fatal: 프로세스 종료 직전이므로 반드시 Flush
            if (ex != null) Span.Services.CrashReportingService.CaptureFatalException(ex, "AppDomain.UnhandledException");
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            // v1.4.15: 정상 cancel 흐름(OperationCanceled)은 Sentry 노이즈만 만들고 의미 없음 → 로그만 남기고 swallow.
            // 두 번째 Sentry 이슈(FluentFTP/SshNet 내부 socket cancel)의 직접 원인.
            // 라이브러리 내부 ValueTask가 fire-and-forget으로 처리되며 cancel 시 finalizer로 재던져짐.
            bool isCancellationOnly = e.Exception != null
                && e.Exception.InnerExceptions.All(ix => ix is OperationCanceledException);

            if (isCancellationOnly)
            {
                Helpers.DebugLogger.Log($"[Task.UnobservedException:Cancel] swallowed ({e.Exception?.InnerExceptions.Count} cancel(s))");
                e.SetObserved();
                return;
            }

            Helpers.DebugLogger.LogCrash("Task.UnobservedException", e.Exception);
            try { Services.GetRequiredService<Services.CrashReportingService>().CaptureException(e.Exception, "Task.UnobservedException"); }
            catch { Span.Services.CrashReportingService.CaptureFatalException(e.Exception, "Task.UnobservedException"); }
            e.SetObserved(); // Prevent process termination
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Services (concrete registrations)
            services.AddSingleton<Services.FileSystemService>();
            services.AddSingleton<Services.IconService>();
            services.AddSingleton<Services.FavoritesService>();
            services.AddSingleton<Services.PreviewService>();
            services.AddSingleton<Services.ShellService>();
            services.AddSingleton<Services.LocalizationService>();
            services.AddSingleton<Services.ContextMenuService>();
            services.AddSingleton<Services.FolderIconService>();
            services.AddSingleton<Services.ActionLogService>();
            services.AddSingleton<Services.SettingsService>();
            services.AddSingleton<Services.FolderContentCache>();
            services.AddSingleton<Services.FileOperationManager>();
            services.AddSingleton<Services.FolderSizeService>();
            services.AddSingleton<Services.FileSystemWatcherService>();
            services.AddSingleton<Services.CloudSyncService>();
            services.AddSingleton<Services.NetworkBrowserService>();
            services.AddSingleton<Services.ConnectionManagerService>();
            services.AddSingleton<Services.GitStatusService>();
            // Issue #58: 폴더 컬러 태그 (desktop.ini 저장/조회 + 캐시)
            services.AddSingleton<Services.FolderTagService>();
            services.AddSingleton<Services.CrashReportingService>();
            services.AddSingleton<Services.Thumbnails.ThumbnailClientService>();
            services.AddSingleton<Services.JumpListService>();
            services.AddSingleton<Services.ArchiveReaderService>();
            services.AddSingleton<Services.RecycleBinService>();
            services.AddSingleton<Services.KeyBindingService>();
            services.AddSingleton<Services.DefaultFileManagerService>();
            services.AddSingleton<Services.ShellNewService>();
            services.AddSingleton<Services.WorkspaceService>();
            services.AddSingleton<Services.ShelfService>();
            services.AddSingleton<Services.TrayIconService>();

            // Interface registrations (for testability — resolve to same singleton)
            services.AddSingleton<Services.IFileSystemService>(sp => sp.GetRequiredService<Services.FileSystemService>());
            services.AddSingleton<Services.IShellService>(sp => sp.GetRequiredService<Services.ShellService>());
            services.AddSingleton<Services.IIconService>(sp => sp.GetRequiredService<Services.IconService>());
            services.AddSingleton<Services.IFavoritesService>(sp => sp.GetRequiredService<Services.FavoritesService>());
            services.AddSingleton<Services.ISettingsService>(sp => sp.GetRequiredService<Services.SettingsService>());
            services.AddSingleton<Services.IAppearanceSettings>(sp => sp.GetRequiredService<Services.SettingsService>());
            services.AddSingleton<Services.IBrowsingSettings>(sp => sp.GetRequiredService<Services.SettingsService>());
            services.AddSingleton<Services.IToolSettings>(sp => sp.GetRequiredService<Services.SettingsService>());
            services.AddSingleton<Services.IDeveloperSettings>(sp => sp.GetRequiredService<Services.SettingsService>());
            services.AddSingleton<Services.IPreviewService>(sp => sp.GetRequiredService<Services.PreviewService>());
            services.AddSingleton<Services.IActionLogService>(sp => sp.GetRequiredService<Services.ActionLogService>());

            // File system provider abstraction
            services.AddSingleton<Services.LocalFileSystemProvider>();
            services.AddSingleton<Services.ArchiveProvider>();
            services.AddSingleton<Services.FileSystemRouter>(sp =>
            {
                var router = new Services.FileSystemRouter();
                router.RegisterProvider(sp.GetRequiredService<Services.LocalFileSystemProvider>());
                router.RegisterProvider(sp.GetRequiredService<Services.ArchiveProvider>());
                return router;
            });

            // ViewModel 등록
            services.AddTransient<MainViewModel>();

            return services.BuildServiceProvider();
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                // Apply saved language before creating windows
                // so that PrimaryLanguageOverride is set early for system dialogs
                var settings = Services.GetRequiredService<Services.SettingsService>();
                var loc = Services.GetRequiredService<Services.LocalizationService>();
                var savedLang = settings.Language;
                // Always apply — "system" resolves to OS locale, others force specific language
                loc.Language = savedLang;

                var iconService = Services.GetRequiredService<Services.IconService>();
                await iconService.LoadAsync();

                // 앱 시작 시 테마에 맞게 아이콘 색상 보정 적용
                var savedTheme = Services.GetRequiredService<Services.SettingsService>().Theme;
                bool isLightAtStart = savedTheme == "light" ||
                    (savedTheme == "system" && RequestedTheme == ApplicationTheme.Light);
                iconService.UpdateTheme(isLightAtStart);

                // Override icon font resource based on selected icon pack
                // Must happen before MainWindow creation so StaticResource resolves correctly
                Resources["RemixIcons"] = new Microsoft.UI.Xaml.Media.FontFamily(iconService.FontFamilyPath);

                // Override structural icon glyph resources (Icons.xaml defaults are Remix-specific)
                Resources["Icon_Folder"] = iconService.FolderGlyph;
                Resources["Icon_FolderOpen"] = iconService.FolderOpenGlyph;
                Resources["Icon_Drive"] = iconService.DriveGlyph;
                Resources["Icon_ChevronRight"] = iconService.ChevronRightGlyph;
                Resources["Icon_File_Default"] = iconService.FileDefaultGlyph;
                Resources["Icon_NewFolder"] = iconService.NewFolderGlyph;
                Resources["Icon_SplitView"] = iconService.SplitViewGlyph;

                // Check for Jump List activation arguments
                // Note: args.Arguments is unreliable in WinUI 3 (WindowsAppSDK #1619)
                // Use AppLifecycle API instead
                StartupArguments = null;
                StartupSelect = false;
                try
                {
                    var activatedArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                    Helpers.DebugLogger.Log($"[App] Activation kind: {activatedArgs.Kind}");
                    var request = Helpers.LaunchRequest.Empty;
                    if (activatedArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Launch)
                    {
                        // AppExecutionAlias 경유 시 전체 커맨드라인이 온다:
                        // "C:\...\spanfinder.exe" "D:\folder" → 폴더 경로만 추출
                        var launchData = activatedArgs.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs;
                        Helpers.DebugLogger.Log($"[App] LaunchData.Arguments: '{launchData?.Arguments}'");
                        request = ParseLaunchArguments(launchData?.Arguments);
                    }

                    // Fallback: LaunchData가 비는 경우(WindowsAppSDK #1619). 프로세스 원본 명령줄을 같은 규칙으로 해석한다.
                    // Environment.CommandLine/GetCommandLineArgs는 쓰지 않는다: apphost 앱에서는 argv0이 실제 exe가
                    // 아니라 "…\Span.dll"이라(실측) 그 dll이 파일 인자로 읽혀 AppX 폴더가 열렸고, .NET 인자 분리는
                    // "C:\"를 C:"로 깨뜨린다.
                    if (request.Kind == Helpers.LaunchKind.None)
                    {
                        var commandLine = GetProcessCommandLine();
                        Helpers.DebugLogger.Log($"[App] CommandLine: '{commandLine}'");
                        request = ParseLaunchArguments(commandLine);
                    }

                    switch (request.Kind)
                    {
                        case Helpers.LaunchKind.NewWindow:
                            StartupArguments = Helpers.LaunchArguments.NewWindowFlag;
                            break;
                        case Helpers.LaunchKind.Path:
                            StartupArguments = request.Path;
                            break;
                        case Helpers.LaunchKind.Select:
                            StartupArguments = request.Path;
                            StartupSelect = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[App] Activation args check failed: {ex.Message}");
                }
                if (StartupArguments != null)
                    Helpers.DebugLogger.Log($"[App] Final StartupArguments: {StartupArguments}{(StartupSelect ? " (select)" : "")}");

                // 가상 폴더(제어판 등)는 창 생성 없이 explorer.exe로 위임 후 즉시 종료
                if (!string.IsNullOrEmpty(StartupArguments) && IsVirtualFolderArgument(StartupArguments))
                {
                    Helpers.DebugLogger.Log($"[App] Virtual folder at launch → explorer.exe: {StartupArguments}");
                    DelegateToExplorer(StartupArguments);
                    Environment.Exit(0);
                    return;
                }

                m_window = new MainWindow();
                RegisterWindow(m_window);
                m_window.Activate();

                // 안전장치: 첫 창의 Loaded가 예외로 끝나 시작 완료 신호가 오지 않아도, 보관된 활성화가
                // 영영 처리되지 않는 일은 없게 한다(기본 파일 관리자 사용자는 폴더 열기 전체가 막힌다).
                var startupWindow = m_window;
                _ = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(_ =>
                    startupWindow.DispatcherQueue?.TryEnqueue(() =>
                    {
                        if (!IsActivationReady)
                            Helpers.DebugLogger.Log("[App] Startup completion signal not received in 15s — releasing queued activations");
                        OnMainWindowStartupComplete();
                    }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled exception in OnLaunched: {ex}");
                throw; // crash loudly instead of silently swallowing
            }
        }

        private Window m_window;

        /// <summary>
        /// Jump List (or other) startup arguments passed via activation.
        /// Consumed by MainWindow on Loaded, then set to null.
        /// </summary>
        internal static string? StartupArguments { get; set; }

        /// <summary>StartupArguments가 --select로 왔는지. 폴더여도 들어가지 않고 부모에서 선택한다.</summary>
        internal static bool StartupSelect { get; set; }

        // ── 콜드 스타트 중 도착한 활성화 보관 ──
        // 첫 창이 세션 복원을 마치기 전에 온 리다이렉트는 여기 쌓았다가 그 뒤에 처리한다.
        // 이전에는 창이 없으면 버렸고(폴더가 열리지 않음), 창은 있지만 Loaded 전이면 세션 복원보다 먼저
        // 새 탭을 만들었다. SPAN이 꺼져 있을 때 폴더 두 개를 연달아 열면 두 번째가 사라지던 경로다.
        private readonly object _activationLock = new();
        private readonly System.Collections.Generic.List<Helpers.LaunchRequest> _pendingActivations = new();
        private bool _activationReady;

        private bool IsActivationReady { get { lock (_activationLock) return _activationReady; } }

        /// <summary>
        /// 첫 MainWindow가 시작 처리(세션 복원, 시작 인자 소비)를 마쳤을 때 호출된다. 보관된 활성화를
        /// 도착 순서대로 처리한다. 여러 번 불려도 한 번만 동작한다.
        /// </summary>
        internal void OnMainWindowStartupComplete()
        {
            lock (_activationLock)
            {
                if (_activationReady) return;
                _activationReady = true;
                if (_pendingActivations.Count > 0)
                    Helpers.DebugLogger.Log($"[App] Replaying {_pendingActivations.Count} activation(s) queued during startup");
                // 재생도 lock 안에서 디스패처 큐에 넣는다(TryEnqueue는 블로킹하지 않는다). lock을 푼 사이에
                // 새로 온 활성화가 먼저 큐에 들어가 순서가 뒤집히지 않게 한다.
                foreach (var request in _pendingActivations)
                    DispatchActivation(request);
                _pendingActivations.Clear();
            }
        }

        /// <summary>
        /// Single instance 모드: 다른 프로세스에서 리다이렉트된 활성화 이벤트 처리.
        /// 기존 창을 포그라운드로 활성화하고 폴더 경로를 새 탭으로 엽니다.
        /// </summary>
        private void OnAppActivated(object? sender, AppActivationArguments args)
        {
            try
            {
                var request = Helpers.LaunchRequest.Empty;
                if (args.Kind == ExtendedActivationKind.Launch)
                {
                    var launchData = args.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs;
                    request = ParseLaunchArguments(launchData?.Arguments);
                }

                // 가상 폴더는 창 활성화 없이 explorer.exe로 바로 위임 (창 준비 여부와 무관)
                if (request.Kind == Helpers.LaunchKind.Path && IsVirtualFolderArgument(request.Path))
                {
                    DelegateToExplorer(request.Path!);
                    Helpers.DebugLogger.Log($"[App] Redirected: virtual folder → explorer.exe (no activation): {request.Path}");
                    return;
                }

                // --select 대상 확인은 여기(백그라운드 스레드)서 한다. UI 콜백에서 처음 확인하면 오프라인 UNC에서
                // 첫 확인이 수십 초 걸려 창이 멈춘다. 항목이 없어도 부모가 있으면 부모를 연다(콜드 스타트와 같은 규칙).
                if (request.Kind == Helpers.LaunchKind.Select && !IsSelectTargetReachable(request.Path))
                {
                    Helpers.DebugLogger.Log($"[App] Redirected: select target not found: {request.Path}");
                    return;
                }

                lock (_activationLock)
                {
                    if (!_activationReady)
                    {
                        _pendingActivations.Add(request);
                        Helpers.DebugLogger.Log($"[App] Redirected activation queued until startup completes: {request}");
                        return;
                    }
                    // 재생과 같은 lock 안에서 큐에 넣어 도착 순서를 지킨다
                    DispatchActivation(request);
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[App] OnAppActivated error: {ex.Message}");
            }
        }

        private void DispatchActivation(Helpers.LaunchRequest request)
        {
            try
            {
                var folderPath = request.Path;

                // UI 스레드에서 처리
                var mainWindow = GetMainWindow();
                if (mainWindow == null)
                {
                    Helpers.DebugLogger.Log($"[App] Redirected activation dropped — no main window: {request}");
                    return;
                }

                // 작업표시줄 점프 리스트의 "새 창". 경로가 아니라 명령이다.
                // 기존 창을 건드리지 않고 새 창만 만든다 — 아래 포그라운드 처리 뒤에 두면
                // 한 디스패처 틱에서 Activate가 두 번 일어나고, 새 창이 뜨자마자 사라지거나
                // 종료 시 XAML에서 크래시가 났다(실측: 0xc000027b in Microsoft.UI.Xaml.dll).
                if (request.Kind == Helpers.LaunchKind.NewWindow)
                {
                    mainWindow.DispatcherQueue?.TryEnqueue(() =>
                    {
                        try
                        {
                            if (mainWindow.IsClosed) return;
                            mainWindow.OpenNewWindowFromActivation();
                            Helpers.DebugLogger.Log("[App] Redirected: opened new window");
                        }
                        catch (Exception ex)
                        {
                            Helpers.DebugLogger.Log($"[App] New window from activation failed: {ex.Message}");
                        }
                    });
                    return;
                }

                mainWindow.DispatcherQueue?.TryEnqueue(() =>
                {
                    try
                    {
                        if (mainWindow.IsClosed) return;

                        // 0. Close-to-Tray 복원: AppWindow.Hide()로 숨겨진 창은 AppWindow.Show()로 복원.
                        //    SW_SHOW(ShowWindow)만으로는 WS_EX_APPWINDOW 스타일이 복원되지 않아
                        //    Alt+Tab/작업표시줄에 나타나지 않음. 기본 파일매니저에서 클릭 시 치명적 UX.
                        try { mainWindow.AppWindow?.Show(); } catch { }

                        // 1. 포그라운드 활성화
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
                        Helpers.NativeMethods.ShowWindow(hwnd,
                            Helpers.NativeMethods.IsIconic(hwnd)
                                ? Helpers.NativeMethods.SW_RESTORE
                                : Helpers.NativeMethods.SW_SHOW);
                        Helpers.NativeMethods.SetForegroundWindow(hwnd);
                        mainWindow.Activate();

                        // 2. 폴더 경로가 있으면 새 탭으로 열기
                        if (request.Kind == Helpers.LaunchKind.Select && !string.IsNullOrEmpty(folderPath))
                        {
                            // --select: 부모 폴더를 열고 그 항목을 선택. 폴더 항목도 안으로 들어가지 않는다.
                            // (Directory.Exists 분기보다 먼저 봐야 한다 — 아니면 그 폴더 안으로 들어간다.)
                            // 대상 확인은 OnAppActivated에서 이미 했다.
                            mainWindow.HandleRedirectedSelect(folderPath);
                            Helpers.DebugLogger.Log($"[App] Redirected: select {folderPath}");
                        }
                        else if (!string.IsNullOrEmpty(folderPath))
                        {
                            if (IsRecycleBinArgument(folderPath))
                            {
                                mainWindow.HandleRecycleBinActivation();
                                Helpers.DebugLogger.Log("[App] Redirected: opened RecycleBin");
                            }
                            else if (IsThisPCArgument(folderPath))
                            {
                                // This PC → 홈 화면 활성화 (이미 포그라운드로 올라옴)
                                Helpers.DebugLogger.Log("[App] Redirected: This PC → Home");
                            }
                            else if (System.IO.Directory.Exists(folderPath))
                            {
                                mainWindow.HandleRedirectedFolder(folderPath);
                                Helpers.DebugLogger.Log($"[App] Redirected: opened {folderPath} in new tab");
                            }
                            else if (System.IO.File.Exists(folderPath))
                            {
                                mainWindow.HandleRedirectedFile(folderPath);
                                Helpers.DebugLogger.Log($"[App] Redirected: opened file {folderPath}");
                            }
                            else
                            {
                                Helpers.DebugLogger.Log($"[App] Redirected: path not found: {folderPath}");
                            }
                        }
                        else
                        {
                            // 경로 없이 실행 → 그냥 포그라운드만
                            Helpers.DebugLogger.Log("[App] Redirected: brought to foreground (no folder path)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Helpers.DebugLogger.Log($"[App] ActivateMainWindow error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[App] DispatchActivation error: {ex.Message}");
            }
        }

        /// <summary>등록된 MainWindow 중 첫 번째를 반환.</summary>
        private MainWindow? GetMainWindow()
        {
            lock (_windowLock)
            {
                foreach (var w in _windows)
                    if (w is MainWindow mw) return mw;
            }
            return null;
        }
    }
}
