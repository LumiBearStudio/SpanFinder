using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Span.ViewModels;

namespace Span
{
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

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Helpers.DebugLogger.LogCrash("UI.UnhandledException", e.Exception);
            e.Handled = true;
        }

        private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            Helpers.DebugLogger.LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Helpers.DebugLogger.LogCrash("Task.UnobservedException", e.Exception);
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
            services.AddSingleton<Services.FileSystemRouter>(sp =>
            {
                var router = new Services.FileSystemRouter();
                router.RegisterProvider(sp.GetRequiredService<Services.LocalFileSystemProvider>());
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
    }
}
