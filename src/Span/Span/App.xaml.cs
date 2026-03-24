using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Collections.Generic;
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
            Services = ConfigureServices();

            // Initialize Sentry crash reporting (must be early, before any exception can occur)
            var crashService = Services.GetRequiredService<Services.CrashReportingService>();
            crashService.Initialize();

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

        /// <summary>
        /// 커맨드라인 문자열에서 폴더 경로 인수를 추출한다.
        /// AppExecutionAlias 경유 시: "C:\...\spanfinder.exe" "D:\folder" → "D:\folder"
        /// JumpList 경유 시: "D:\folder" → "D:\folder"
        /// </summary>
        private static string? ExtractFolderArgument(string rawArgs)
        {
            if (string.IsNullOrWhiteSpace(rawArgs)) return null;
            rawArgs = rawArgs.Trim();

            // 따옴표로 감싸진 인수들을 파싱
            var parts = new System.Collections.Generic.List<string>();
            int i = 0;
            while (i < rawArgs.Length)
            {
                if (rawArgs[i] == '"')
                {
                    int end = rawArgs.IndexOf('"', i + 1);
                    if (end < 0) end = rawArgs.Length;
                    parts.Add(rawArgs.Substring(i + 1, end - i - 1));
                    i = end + 1;
                }
                else if (rawArgs[i] != ' ')
                {
                    int end = rawArgs.IndexOf(' ', i);
                    if (end < 0) end = rawArgs.Length;
                    parts.Add(rawArgs.Substring(i, end - i));
                    i = end;
                }
                else { i++; }
            }

            // 마지막 인수부터 역순으로 폴더 경로 탐색
            for (int j = parts.Count - 1; j >= 0; j--)
            {
                var part = parts[j].Trim().Trim('"');
                if (!string.IsNullOrEmpty(part) && System.IO.Directory.Exists(part))
                    return part;
            }

            // 단일 인수 (따옴표 없는 경로)
            var trimmed = rawArgs.Trim().Trim('"');
            if (System.IO.Directory.Exists(trimmed)) return trimmed;

            return rawArgs; // fallback: 원본 반환
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
            lock (_windowLock)
            {
                if (!_windows.Contains(w))
                    _windows.Add(w);
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
                        // Check if point is within the window's tab bar area (top 50px)
                        if (screenX >= rect.Left && screenX <= rect.Right &&
                            screenY >= rect.Top && screenY <= rect.Top + 50)
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
            // Sentry: 세션당 최대 N회만 전송 (반복 네이티브 예외 증폭 방지)
            // CaptureFatalException(동기 Flush)은 사용하지 않음 — UI 스레드 차단 위험
            if (_sentryCaptureCount < MaxSentryCapturesPerSession)
            {
                _sentryCaptureCount++;
                try
                {
                    var crashSvc = Services.GetRequiredService<Services.CrashReportingService>();
                    crashSvc.CaptureException(e.Exception, "UI.UnhandledException",
                        new Dictionary<string, string> { ["crashCount"] = _crashCount.ToString() });
                }
                catch { }
            }
            e.Handled = true;
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
            services.AddSingleton<Services.CrashReportingService>();
            services.AddSingleton<Services.JumpListService>();
            services.AddSingleton<Services.ArchiveReaderService>();
            services.AddSingleton<Services.RecycleBinService>();
            services.AddSingleton<Services.KeyBindingService>();
            services.AddSingleton<Services.DefaultFileManagerService>();
            services.AddSingleton<Services.ShellNewService>();

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
                try
                {
                    var activatedArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                    Helpers.DebugLogger.Log($"[App] Activation kind: {activatedArgs.Kind}");
                    if (activatedArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Launch)
                    {
                        var launchData = activatedArgs.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs;
                        Helpers.DebugLogger.Log($"[App] LaunchData.Arguments: '{launchData?.Arguments}'");
                        if (!string.IsNullOrEmpty(launchData?.Arguments))
                        {
                            // AppExecutionAlias 경유 시 전체 커맨드라인이 옴:
                            // "C:\...\spanfinder.exe" "D:\folder" → 폴더 경로만 추출
                            var rawArgs = launchData.Arguments;
                            StartupArguments = ExtractFolderArgument(rawArgs);
                        }
                    }

                    // Fallback: Environment.GetCommandLineArgs (AppExecutionAlias 대응)
                    if (string.IsNullOrEmpty(StartupArguments))
                    {
                        var cmdArgs = Environment.GetCommandLineArgs();
                        Helpers.DebugLogger.Log($"[App] CommandLineArgs count: {cmdArgs.Length}, values: [{string.Join(", ", cmdArgs)}]");
                        if (cmdArgs.Length > 1)
                        {
                            var folderArg = cmdArgs[1].Trim().Trim('"');
                            if (System.IO.Directory.Exists(folderArg))
                                StartupArguments = folderArg;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[App] Activation args check failed: {ex.Message}");
                }
                if (StartupArguments != null)
                    Helpers.DebugLogger.Log($"[App] Final StartupArguments: {StartupArguments}");

                m_window = new MainWindow();
                RegisterWindow(m_window);
                m_window.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled exception in OnLaunched: {ex}");
                // In a real app, might show a dialog here
            }
        }

        private Window m_window;

        /// <summary>
        /// Jump List (or other) startup arguments passed via activation.
        /// Consumed by MainWindow on Loaded, then set to null.
        /// </summary>
        internal static string? StartupArguments { get; set; }

        /// <summary>
        /// Single instance 모드: 다른 프로세스에서 리다이렉트된 활성화 이벤트 처리.
        /// 기존 창을 포그라운드로 활성화하고 폴더 경로를 새 탭으로 엽니다.
        /// </summary>
        private void OnAppActivated(object? sender, AppActivationArguments args)
        {
            try
            {
                string? folderPath = null;

                if (args.Kind == ExtendedActivationKind.Launch)
                {
                    var launchData = args.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs;
                    if (!string.IsNullOrEmpty(launchData?.Arguments))
                        folderPath = ExtractFolderArgument(launchData.Arguments);
                }

                // UI 스레드에서 처리
                var mainWindow = GetMainWindow();
                if (mainWindow == null) return;

                mainWindow.DispatcherQueue?.TryEnqueue(() =>
                {
                    try
                    {
                        if (mainWindow.IsClosed) return;

                        // 1. 포그라운드 활성화
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
                        Helpers.NativeMethods.ShowWindow(hwnd,
                            Helpers.NativeMethods.IsIconic(hwnd)
                                ? Helpers.NativeMethods.SW_RESTORE
                                : Helpers.NativeMethods.SW_SHOW);
                        Helpers.NativeMethods.SetForegroundWindow(hwnd);
                        mainWindow.Activate();

                        // 2. 폴더 경로가 있으면 새 탭으로 열기
                        if (!string.IsNullOrEmpty(folderPath) && System.IO.Directory.Exists(folderPath))
                        {
                            // StartupArguments에 설정 → MainWindow의 기존 JumpList 처리 로직 재활용
                            // 이 방식이 AddNewTab + SwitchViewMode를 직접 호출하는 것보다 안전
                            // (탭 헤더, Miller 패널 등 UI 동기화가 MainWindow에서 통합 처리됨)
                            StartupArguments = folderPath;
                            mainWindow.HandleRedirectedFolder(folderPath);
                            Helpers.DebugLogger.Log($"[App] Redirected: opened {folderPath} in new tab");
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
                Helpers.DebugLogger.Log($"[App] OnAppActivated error: {ex.Message}");
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
