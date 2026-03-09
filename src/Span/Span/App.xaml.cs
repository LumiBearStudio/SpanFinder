using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
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
            Services = ConfigureServices();

            // Initialize Sentry crash reporting (must be early, before any exception can occur)
            var crashService = Services.GetRequiredService<Services.CrashReportingService>();
            crashService.Initialize();

            // UI thread unhandled exceptions
            this.UnhandledException += OnUnhandledException;

            // Background thread / Task exceptions
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
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
        private DateTime _lastCrashTime;
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
            // Sentry: DI 서비스 우선, 실패 시 static fallback (FlushAsync 포함)
            try
            {
                var crashSvc = Services.GetRequiredService<Services.CrashReportingService>();
                crashSvc.CaptureException(e.Exception, "UI.UnhandledException");
            }
            catch { }
            // Static fallback도 항상 시도 (인스턴스 메서드가 조용히 return할 수 있으므로)
            Span.Services.CrashReportingService.CaptureFatalException(e.Exception, "UI.UnhandledException");
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
                    if (activatedArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Launch)
                    {
                        var launchData = activatedArgs.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs;
                        if (!string.IsNullOrEmpty(launchData?.Arguments))
                            StartupArguments = launchData.Arguments;
                    }
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[App] Activation args check failed: {ex.Message}");
                }
                if (StartupArguments != null)
                    Helpers.DebugLogger.Log($"[App] Launch arguments: {StartupArguments}");

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
    }
}
