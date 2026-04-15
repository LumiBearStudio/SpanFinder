using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Span.Services
{
    /// <summary>
    /// App-level system tray icon service.
    /// Maintains a single shared tray icon across all MainWindows (tear-off included).
    /// Created lazily on first window registration when MinimizeToTray setting is ON.
    ///
    /// Design:
    /// - Left-click tray icon: restore all hidden windows (foreground last one)
    /// - Right-click menu: Show all / Exit Span
    /// - Icon visible whenever setting is ON, regardless of window state
    /// - Exit path sets _forceClose on every window then triggers Close() chain;
    ///   last UnregisterWindow triggers Process.Kill (existing path, untouched)
    /// </summary>
    public sealed class TrayIconService : IDisposable
    {
        private readonly SettingsService _settings;
        private readonly LocalizationService _loc;
        private TaskbarIcon? _icon;
        private bool _disposed;

        public TrayIconService(SettingsService settings, LocalizationService loc)
        {
            _settings = settings;
            _loc = loc;

            // Re-evaluate when the user toggles the setting at runtime
            _settings.SettingChanged += OnSettingChanged;
            Helpers.DebugLogger.Log($"[TrayIcon] service constructed (setting={_settings.MinimizeToTray})");
        }

        /// <summary>True when the tray icon is currently active and handling Hide/Show.</summary>
        public bool IsActive => _icon != null && _settings.MinimizeToTray;

        /// <summary>
        /// Ensures the icon is created if the setting is ON; destroys it otherwise.
        /// Safe to call repeatedly (idempotent).
        /// </summary>
        public void SyncWithSetting()
        {
            if (_disposed) return;

            if (_settings.MinimizeToTray)
            {
                EnsureCreated();
            }
            else
            {
                DestroyIcon();
            }
        }

        private void OnSettingChanged(string key, object? value)
        {
            Helpers.DebugLogger.Log($"[TrayIcon] SettingChanged: {key}={value}");
            if (key == "MinimizeToTray")
            {
                // UI thread required for TaskbarIcon creation.
                var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                if (dq != null)
                {
                    SyncWithSetting();
                }
                else
                {
                    // Settings toggle usually runs on UI thread; fallback just in case.
                    var mainWindow = App.Current.GetRegisteredWindows().FirstOrDefault() as Window;
                    mainWindow?.DispatcherQueue.TryEnqueue(SyncWithSetting);
                }
            }
        }

        private void EnsureCreated()
        {
            if (_icon != null) return;

            try
            {
                // Use the packaged .ico file — TaskbarIcon converts UriSource internally.
                // .ico is the most reliable format for HICON conversion vs. PNG.
                var iconUri = new Uri("ms-appx:///Assets/app.ico");
                _icon = new TaskbarIcon
                {
                    IconSource = new BitmapImage { UriSource = iconUri },
                    ToolTipText = "SPAN Finder",
                    NoLeftClickDelay = true,
                };

                _icon.LeftClickCommand = new RelayCommand(_ => RestoreAllWindows());
                _icon.ContextFlyout = BuildMenu();
                _icon.ForceCreate();

                Helpers.DebugLogger.Log($"[TrayIcon] created, icon URI={iconUri}");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[TrayIcon] create failed: {ex}");
                _icon = null;
            }
        }

        private MenuFlyout BuildMenu()
        {
            var flyout = new MenuFlyout();

            var showItem = new MenuFlyoutItem { Text = _loc.Get("Tray_Show") };
            showItem.Click += (_, __) => RestoreAllWindows();
            flyout.Items.Add(showItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var exitItem = new MenuFlyoutItem { Text = _loc.Get("Tray_Exit") };
            exitItem.Click += (_, __) => ExitApplication();
            flyout.Items.Add(exitItem);

            return flyout;
        }

        /// <summary>
        /// Show all registered windows. If a window was hidden via AppWindow.Hide(),
        /// AppWindow.Show() is required to restore it (SW_SHOW alone doesn't re-add to Alt+Tab).
        /// </summary>
        public void RestoreAllWindows()
        {
            try
            {
                var windows = App.Current.GetRegisteredWindows();
                MainWindow? lastMain = null;
                foreach (var w in windows)
                {
                    if (w is MainWindow mw)
                    {
                        try
                        {
                            mw.AppWindow?.Show();
                            lastMain = mw;
                        }
                        catch (Exception ex)
                        {
                            Helpers.DebugLogger.Log($"[TrayIcon] AppWindow.Show failed: {ex.Message}");
                        }
                    }
                }

                if (lastMain != null)
                {
                    try
                    {
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(lastMain);
                        Helpers.NativeMethods.SetForegroundWindow(hwnd);
                        lastMain.Activate();
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[TrayIcon] RestoreAllWindows error: {ex.Message}");
            }
        }

        /// <summary>
        /// Explicit exit: set force-close on every window, then close them.
        /// The last window's UnregisterWindow call triggers Process.Kill (existing path).
        /// </summary>
        public void ExitApplication()
        {
            try
            {
                var windows = App.Current.GetRegisteredWindows().ToList();
                if (windows.Count == 0)
                {
                    // No windows (shouldn't happen if tray alive) — hard kill
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                    return;
                }

                foreach (var w in windows)
                {
                    if (w is MainWindow mw)
                    {
                        try
                        {
                            mw.SetForceClose();
                            mw.DispatcherQueue?.TryEnqueue(() =>
                            {
                                try { mw.Close(); } catch { }
                            });
                        }
                        catch (Exception ex)
                        {
                            Helpers.DebugLogger.Log($"[TrayIcon] force-close failed: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[TrayIcon] ExitApplication error: {ex.Message}");
                // Fallback
                try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
            }
        }

        private void DestroyIcon()
        {
            if (_icon == null) return;
            try
            {
                _icon.Dispose();
            }
            catch { }
            _icon = null;
            Helpers.DebugLogger.Log("[TrayIcon] destroyed");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _settings.SettingChanged -= OnSettingChanged; } catch { }
            DestroyIcon();
        }

        /// <summary>Minimal ICommand adapter for H.NotifyIcon click commands.</summary>
        private sealed class RelayCommand : System.Windows.Input.ICommand
        {
            private readonly Action<object?> _execute;
            public RelayCommand(Action<object?> execute) => _execute = execute;
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _execute(parameter);
            public event EventHandler? CanExecuteChanged;
        }
    }
}
