using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.ViewModels;
using Span.Services;
using Span.Services.FileOperations;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace Span
{
    public sealed partial class MainWindow : Window, Services.IContextMenuHost
    {
        // --- WM_DEVICECHANGE P/Invoke for USB hotplug detection ---
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVNODES_CHANGED = 0x0007;

        private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private IntPtr _hwnd;
        private SUBCLASSPROC? _subclassProc; // prevent GC collection
        private DispatcherTimer? _deviceChangeDebounceTimer;

        private readonly Services.ContextMenuService _contextMenuService;
        private readonly Services.LocalizationService _loc;
        private readonly Services.SettingsService _settings;
        public MainViewModel ViewModel { get; }

        // Type-ahead search
        private string _typeAheadBuffer = string.Empty;
        private DispatcherTimer? _typeAheadTimer;

        // Prevents DispatcherQueue callbacks and async methods from accessing
        // disposed UI after OnClosed has started teardown
        private bool _isClosed = false;

        // Clipboard
        private readonly List<string> _clipboardPaths = new();
        private bool _isCutOperation = false;

        // Rename 완료 직후 Enter가 파일 실행으로 이어지는 것을 방지
        private bool _justFinishedRename = false;

        // Selection synchronization guard (Phase 1)
        private bool _isSyncingSelection = false;

        // Preview panel selection subscriptions
        private FolderViewModel? _leftPreviewSubscribedColumn;
        private FolderViewModel? _rightPreviewSubscribedColumn;

        // Sort state
        private string _currentSortField = "Name"; // Name, Date, Size, Type
        private bool _currentSortAscending = true;

        private const double ColumnWidth = 220;

        public MainWindow()
        {
            this.InitializeComponent();
            ViewModel = App.Current.Services.GetRequiredService<MainViewModel>();
            _contextMenuService = App.Current.Services.GetRequiredService<Services.ContextMenuService>();
            _loc = App.Current.Services.GetRequiredService<Services.LocalizationService>();
            _settings = App.Current.Services.GetRequiredService<Services.SettingsService>();

            // Mica
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

            // TitleBar
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // Auto-scroll on column change (both panes)
            ViewModel.Explorer.Columns.CollectionChanged += OnColumnsChanged;
            ViewModel.RightExplorer.Columns.CollectionChanged += OnRightColumnsChanged;

            // Focus management on ViewMode change
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Set ViewModel for Details and Icon views (left pane)
            DetailsView.ViewModel = ViewModel.Explorer;
            IconView.ViewModel = ViewModel.Explorer;
            HomeView.MainViewModel = ViewModel;

            // Set ViewModel for Details and Icon views (right pane)
            DetailsViewRight.IsRightPane = true;
            DetailsViewRight.ViewModel = ViewModel.RightExplorer;
            IconViewRight.IsRightPane = true;
            IconViewRight.ViewModel = ViewModel.RightExplorer;

            // Get HWND early (needed by child views and context menu service)
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            // Pass context menu service and HWND to child views
            _contextMenuService.OwnerHwnd = _hwnd;
            DetailsView.ContextMenuService = _contextMenuService;
            DetailsView.ContextMenuHost = this;
            DetailsView.OwnerHwnd = _hwnd;
            IconView.ContextMenuService = _contextMenuService;
            IconView.ContextMenuHost = this;
            IconView.OwnerHwnd = _hwnd;
            HomeView.ContextMenuService = _contextMenuService;
            HomeView.ContextMenuHost = this;
            DetailsViewRight.ContextMenuService = _contextMenuService;
            DetailsViewRight.ContextMenuHost = this;
            DetailsViewRight.OwnerHwnd = _hwnd;
            IconViewRight.ContextMenuService = _contextMenuService;
            IconViewRight.ContextMenuHost = this;
            IconViewRight.OwnerHwnd = _hwnd;

            // ★ ItemsControl에서 키보드 이벤트 가로채기 (both panes)
            MillerColumnsControl.AddHandler(
                UIElement.KeyDownEvent,
                new KeyEventHandler(OnMillerKeyDown),
                true
            );
            MillerColumnsControlRight.AddHandler(
                UIElement.KeyDownEvent,
                new KeyEventHandler(OnMillerKeyDown),
                true
            );

            // ★ Window-level 단축키 (Ctrl 조합)
            this.Content.AddHandler(
                UIElement.KeyDownEvent,
                new KeyEventHandler(OnGlobalKeyDown),
                true  // Handled 된 이벤트도 받음
            );

            // Type-ahead timer
            _typeAheadTimer = new DispatcherTimer();
            _typeAheadTimer.Interval = TimeSpan.FromMilliseconds(800);
            _typeAheadTimer.Tick += (s, e) =>
            {
                _typeAheadBuffer = string.Empty;
                _typeAheadTimer.Stop();
            };

            this.Closed += OnClosed;

            // WM_DEVICECHANGE: detect USB drive plug/unplug
            _subclassProc = new SUBCLASSPROC(WndProc);
            SetWindowSubclass(_hwnd, _subclassProc, IntPtr.Zero, IntPtr.Zero);

            _deviceChangeDebounceTimer = new DispatcherTimer();
            _deviceChangeDebounceTimer.Interval = TimeSpan.FromMilliseconds(1000);
            _deviceChangeDebounceTimer.Tick += (s, e) =>
            {
                _deviceChangeDebounceTimer.Stop();
                if (!_isClosed)
                {
                    ViewModel.RefreshDrives();
                }
            };

            // Initialize preview panels
            InitializePreviewPanels();

            // Apply saved settings
            ApplyTheme(_settings.Theme);
            ApplyFontFamily(_settings.FontFamily);
            ApplyDensity(_settings.Density);
            _settings.SettingChanged += OnSettingChanged;

            // Connect Language setting to LocalizationService
            var savedLang = _settings.Language;
            if (savedLang != "system")
            {
                _loc.Language = savedLang;
            }

            // Restore split view state and preview state from persisted settings
            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.Loaded += (s, e) =>
                {
                    if (ViewModel.IsSplitViewEnabled)
                    {
                        SplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
                        RightPaneCol.Width = new GridLength(1, GridUnitType.Star);

                        // Navigate right pane to a real path (not conceptual "PC")
                        if (ViewModel.RightExplorer.Columns.Count == 0 ||
                            ViewModel.RightExplorer.CurrentPath == "PC")
                        {
                            NavigateRightPaneToRealPath();
                        }
                    }
                    RestorePreviewState();
                    RestoreSessionPath();

                    // Apply ShowCheckboxes to Miller Columns after initial render
                    if (_settings.ShowCheckboxes)
                    {
                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                            () => ApplyMillerCheckboxMode(true));
                    }
                };
            }
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            try
            {
                Helpers.DebugLogger.Log("[MainWindow.OnClosed] Starting cleanup...");

                // STEP 0: Block all queued DispatcherQueue callbacks and async continuations
                _isClosed = true;

                // Save current path for session restore
                SaveSessionPath();

                // Unsubscribe settings
                _settings.SettingChanged -= OnSettingChanged;

                // STEP 1: Suppress ViewModel notifications FIRST (prevents PropertyChanged
                // from reaching UI during teardown — the primary crash cause).
                ViewModel?.Explorer?.Cleanup();       // Left pane
                ViewModel?.RightExplorer?.Cleanup();   // Right pane

                // STEP 2: Unsubscribe MainWindow event handlers BEFORE ViewModel.Cleanup()
                // so collection Clear() notifications don't reach MainWindow handlers.
                if (ViewModel?.Explorer != null)
                {
                    ViewModel.Explorer.Columns.CollectionChanged -= OnColumnsChanged;
                }
                if (ViewModel?.RightExplorer != null)
                {
                    ViewModel.RightExplorer.Columns.CollectionChanged -= OnRightColumnsChanged;
                }
                if (ViewModel != null)
                {
                    ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                    ViewModel.PropertyChanged -= OnViewModelPropertyChangedForPreview;
                }

                // Unsubscribe preview column change handlers
                if (ViewModel?.LeftExplorer != null)
                    ViewModel.LeftExplorer.Columns.CollectionChanged -= OnLeftColumnsChangedForPreview;
                if (ViewModel?.RightExplorer != null)
                    ViewModel.RightExplorer.Columns.CollectionChanged -= OnRightColumnsChangedForPreview;

                // STEP 2.5: Cleanup preview panels (stop media, dispose ViewModels)
                try { LeftPreviewPanel?.Cleanup(); } catch { }
                try { RightPreviewPanel?.Cleanup(); } catch { }
                UnsubscribePreviewSelection(isLeft: true);
                UnsubscribePreviewSelection(isLeft: false);

                // Save preview panel widths
                try
                {
                    double leftW = LeftPreviewCol.Width.Value;
                    double rightW = RightPreviewCol.Width.Value;
                    ViewModel?.SavePreviewWidths(leftW, rightW);
                }
                catch { }

                // STEP 3: Disconnect view bindings BEFORE ViewModel.Cleanup()
                // so Favorites.Clear() / RecentFolders.Clear() don't reach disposed UI.
                try { DetailsView?.Cleanup(); } catch { }
                try { IconView?.Cleanup(); } catch { }
                try { HomeView?.Cleanup(); } catch { }
                try { DetailsViewRight?.Cleanup(); } catch { }
                try { IconViewRight?.Cleanup(); } catch { }

                // Disconnect sidebar bindings
                try { FavoritesItemsControl.ItemsSource = null; }
                catch { /* ignore */ }

                // STEP 4: NOW safe to clear collections — UI bindings disconnected
                ViewModel?.Cleanup();            // Save state, cancel ops, clear collections

                // STEP 5: Stop timer and remove keyboard handlers
                if (_typeAheadTimer != null)
                {
                    _typeAheadTimer.Stop();
                    _typeAheadTimer = null;
                }
                if (this.Content != null)
                {
                    this.Content.RemoveHandler(UIElement.KeyDownEvent, (KeyEventHandler)OnGlobalKeyDown);
                }
                if (MillerColumnsControl != null)
                {
                    MillerColumnsControl.RemoveHandler(UIElement.KeyDownEvent, (KeyEventHandler)OnMillerKeyDown);
                }
                if (MillerColumnsControlRight != null)
                {
                    MillerColumnsControlRight.RemoveHandler(UIElement.KeyDownEvent, (KeyEventHandler)OnMillerKeyDown);
                }

                // STEP 6: Remove window subclass for device change
                if (_subclassProc != null)
                {
                    RemoveWindowSubclass(_hwnd, _subclassProc, IntPtr.Zero);
                }
                if (_deviceChangeDebounceTimer != null)
                {
                    _deviceChangeDebounceTimer.Stop();
                    _deviceChangeDebounceTimer = null;
                }

                Helpers.DebugLogger.Log("[MainWindow.OnClosed] Cleanup complete");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow.OnClosed] Error during close: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MainWindow.OnClosed] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Win32 subclass procedure to intercept WM_DEVICECHANGE for USB hotplug
        /// </summary>
        private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_DEVICECHANGE && wParam == (IntPtr)DBT_DEVNODES_CHANGED)
            {
                // Debounce: multiple WM_DEVICECHANGE messages fire in quick succession
                _deviceChangeDebounceTimer?.Stop();
                _deviceChangeDebounceTimer?.Start();
                Helpers.DebugLogger.Log("[MainWindow] WM_DEVICECHANGE: Device change detected");
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        // =================================================================
        //  Settings
        // =================================================================

        private void ApplyTheme(string theme)
        {
            if (this.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme switch
                {
                    "light" => ElementTheme.Light,
                    "dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
        }

        private void OnSettingChanged(string key, object? value)
        {
            if (_isClosed) return;

            switch (key)
            {
                case "Theme":
                    DispatcherQueue.TryEnqueue(() => ApplyTheme(value as string ?? "system"));
                    break;

                case "FontFamily":
                    DispatcherQueue.TryEnqueue(() => ApplyFontFamily(value as string ?? "Segoe UI Variable"));
                    break;

                case "Density":
                    DispatcherQueue.TryEnqueue(() => ApplyDensity(value as string ?? "comfortable"));
                    break;

                case "ShowHiddenFiles":
                case "ShowFileExtensions":
                    // Refresh current folder contents to apply filter change
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        RefreshCurrentView();
                    });
                    break;

                case "Language":
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        _loc.Language = value as string ?? "en";
                    });
                    break;

                case "MillerClickBehavior":
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        bool isDouble = (value as string) == "double";
                        bool leftIsMiller = ViewModel.LeftViewMode == Models.ViewMode.MillerColumns;
                        bool rightIsMiller = ViewModel.RightViewMode == Models.ViewMode.MillerColumns;
                        ViewModel.Explorer.EnableAutoNavigation = leftIsMiller && !isDouble;
                        ViewModel.RightExplorer.EnableAutoNavigation = rightIsMiller && !isDouble;
                    });
                    break;

                case "ShowCheckboxes":
                    DispatcherQueue.TryEnqueue(() => ApplyMillerCheckboxMode(value is bool cb && cb));
                    break;

                case "ShowThumbnails":
                    // ShowThumbnails will be used by icon/detail views when thumbnail rendering is implemented.
                    // Currently icon-based only. Refresh to toggle.
                    DispatcherQueue.TryEnqueue(() => RefreshCurrentView());
                    break;
            }
        }

        private void ApplyFontFamily(string fontFamily)
        {
            if (this.Content is FrameworkElement root && root.Resources != null)
            {
                var font = new FontFamily(fontFamily);
                root.Resources["ContentControlThemeFontFamily"] = font;

                if (root is Microsoft.UI.Xaml.Controls.Control control)
                {
                    control.FontFamily = font;
                }
            }
        }

        private void ApplyDensity(string density)
        {
            // Update ListView item height via resource override
            var itemHeight = density switch
            {
                "compact" => 28.0,
                "spacious" => 40.0,
                _ => 32.0 // comfortable
            };

            if (this.Content is FrameworkElement root && root.Resources != null)
            {
                root.Resources["ListViewItemMinHeight"] = itemHeight;
            }
        }

        private void ApplyMillerCheckboxMode(bool showCheckboxes)
        {
            var mode = showCheckboxes
                ? ListViewSelectionMode.Multiple
                : ListViewSelectionMode.Extended;

            // Apply to all visible Miller Column ListViews in both panes
            ApplyCheckboxToItemsControl(MillerColumnsControl, mode);
            ApplyCheckboxToItemsControl(MillerColumnsControlRight, mode);
        }

        private void ApplyCheckboxToItemsControl(ItemsControl? control, ListViewSelectionMode mode)
        {
            if (control?.ItemsPanelRoot == null) return;
            for (int i = 0; i < control.Items.Count; i++)
            {
                var listView = GetListViewFromItemsControl(control, i);
                if (listView != null)
                {
                    listView.SelectionMode = mode;
                }
            }
        }

        private ListView? GetListViewFromItemsControl(ItemsControl control, int index)
        {
            var container = control.ContainerFromIndex(index) as ContentPresenter;
            if (container == null) return null;
            return FindChild<ListView>(container);
        }

        private void HandleOpenTerminal()
        {
            var path = ViewModel.ActiveExplorer.CurrentPath;
            if (!string.IsNullOrEmpty(path) && path != "PC" && System.IO.Directory.Exists(path))
            {
                var shellService = App.Current.Services.GetRequiredService<Services.ShellService>();
                shellService.OpenTerminal(path, _settings.DefaultTerminal);
            }
        }

        private void RefreshCurrentView()
        {
            var explorer = ViewModel.ActiveExplorer;
            if (explorer.Columns.Count > 0)
            {
                // Refresh the last column (current folder)
                var lastCol = explorer.Columns[explorer.Columns.Count - 1];
                _ = lastCol.RefreshAsync();
            }
        }

        private void SaveSessionPath()
        {
            try
            {
                var currentPath = ViewModel.ActiveExplorer.CurrentPath;
                if (!string.IsNullOrEmpty(currentPath) && currentPath != "PC")
                {
                    _settings.LastSessionPath = currentPath;
                }
            }
            catch { /* ignore save errors during close */ }
        }

        private void RestoreSessionPath()
        {
            var behavior = _settings.StartupBehavior;
            switch (behavior)
            {
                case 0: // Restore last session
                    var lastPath = _settings.LastSessionPath;
                    if (!string.IsNullOrEmpty(lastPath) && System.IO.Directory.Exists(lastPath))
                    {
                        var folder = new FolderItem { Name = System.IO.Path.GetFileName(lastPath), Path = lastPath };
                        ViewModel.Explorer.NavigateTo(folder);
                        Helpers.DebugLogger.Log($"[MainWindow] Restored session path: {lastPath}");
                    }
                    break;
                case 1: // Home — do nothing (default behavior)
                    break;
                case 2: // Custom folder — future: add CustomStartupFolder setting
                    break;
            }
        }

        // =================================================================
        //  Auto Scroll
        // =================================================================

        private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add ||
                e.Action == NotifyCollectionChangedAction.Replace)
            {
                ScrollToLastColumn(ViewModel.LeftExplorer, MillerScrollViewer);
            }
        }

        private void OnRightColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add ||
                e.Action == NotifyCollectionChangedAction.Replace)
            {
                ScrollToLastColumn(ViewModel.RightExplorer, MillerScrollViewerRight);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentViewMode) ||
                e.PropertyName == nameof(MainViewModel.RightViewMode))
            {
                FocusActiveView();
            }
            else if (e.PropertyName == nameof(MainViewModel.IsToastVisible))
            {
                DispatcherQueue.TryEnqueue(() => AnimateToast(ViewModel.IsToastVisible));
            }
            else if (e.PropertyName == nameof(MainViewModel.ToastMessage))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!string.IsNullOrEmpty(ViewModel.ToastMessage))
                        ToastText.Text = ViewModel.ToastMessage;
                });
            }
        }

        private void AnimateToast(bool show)
        {
            if (_isClosed) return;

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

            var opacityAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = show ? 1.0 : 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(show ? 200 : 300)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
                {
                    EasingMode = show
                        ? Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
                        : Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn
                }
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(opacityAnim, ToastOverlay);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(opacityAnim, "Opacity");

            var translateAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = show ? 0 : 20,
                Duration = new Duration(TimeSpan.FromMilliseconds(show ? 200 : 300)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
                {
                    EasingMode = show
                        ? Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
                        : Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn
                }
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(translateAnim, ToastTranslate);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(translateAnim, "Y");

            storyboard.Children.Add(opacityAnim);
            storyboard.Children.Add(translateAnim);
            storyboard.Begin();
        }

        private void FocusActiveView()
        {
            // Use DispatcherQueue for proper timing (after visibility changes take effect)
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed || ViewModel == null) return;

                // Determine which pane's view mode to use
                var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                    ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

                switch (viewMode)
                {
                    case Models.ViewMode.MillerColumns:
                        var columns = ViewModel.ActiveExplorer.Columns;
                        if (columns.Count > 0)
                        {
                            int activeIndex = GetActiveColumnIndex();
                            if (activeIndex < 0) activeIndex = columns.Count - 1;
                            FocusColumnAsync(activeIndex);
                        }
                        Helpers.DebugLogger.Log("[MainWindow] Focus: MillerColumns");
                        break;

                    case Models.ViewMode.Details:
                        if (ViewModel.ActivePane == ActivePane.Right && ViewModel.IsSplitViewEnabled)
                            DetailsViewRight?.FocusListView();
                        else
                            DetailsView?.FocusListView();
                        Helpers.DebugLogger.Log("[MainWindow] Focus: Details");
                        break;

                    case Models.ViewMode.IconSmall:
                    case Models.ViewMode.IconMedium:
                    case Models.ViewMode.IconLarge:
                    case Models.ViewMode.IconExtraLarge:
                        if (ViewModel.ActivePane == ActivePane.Right && ViewModel.IsSplitViewEnabled)
                            IconViewRight?.FocusGridView();
                        else
                            IconView?.FocusGridView();
                        Helpers.DebugLogger.Log($"[MainWindow] Focus: Icon ({viewMode})");
                        break;

                    case Models.ViewMode.Home:
                        Helpers.DebugLogger.Log("[MainWindow] Focus: Home");
                        break;
                }
            });
        }

        private void ScrollToLastColumn(ExplorerViewModel explorer, ScrollViewer scrollViewer)
        {
            var columns = explorer.Columns;
            if (columns.Count == 0) return;

            scrollViewer.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    if (_isClosed) return;
                    scrollViewer.UpdateLayout();
                    double totalWidth = columns.Count * ColumnWidth;
                    double viewportWidth = scrollViewer.ViewportWidth;
                    double targetScroll = Math.Max(0, totalWidth - viewportWidth);
                    scrollViewer.ChangeView(targetScroll, null, null, false);
                });
        }

        // =================================================================
        //  Drive click
        // =================================================================

        private void OnDriveItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is DriveItem drive)
            {
                ViewModel.OpenDrive(drive);
                FocusColumnAsync(0);
            }
        }

        /// <summary>
        /// Handle drive item tap in new hybrid sidebar.
        /// </summary>
        private void OnDriveItemTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.DataContext is DriveItem drive)
            {
                ViewModel.OpenDrive(drive);
                FocusColumnAsync(0);
                Helpers.DebugLogger.Log($"[Sidebar] Drive tapped: {drive.Name}");
            }
        }

        private void OnHomeItemTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ViewModel.SwitchViewMode(ViewMode.Home);
            Helpers.DebugLogger.Log("[Sidebar] Home tapped");
        }

        private void OnFavoriteItemTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.DataContext is FavoriteItem favorite)
            {
                ViewModel.NavigateToFavorite(favorite);
                FocusColumnAsync(0);
                Helpers.DebugLogger.Log($"[Sidebar] Favorite tapped: {favorite.Name}");
            }
        }

        private void OnFavoriteRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.DataContext is FavoriteItem favorite)
            {
                var flyout = _contextMenuService.BuildFavoriteMenu(favorite, this);
                flyout.ShowAt(grid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(grid)
                });
                e.Handled = true;
            }
        }

        private void OnFolderRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (!_settings.ShowContextMenu) return;
            if (sender is Grid grid && grid.DataContext is FolderViewModel folder)
            {
                var flyout = _contextMenuService.BuildFolderMenu(folder, this);
                flyout.ShowAt(grid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(grid)
                });
                e.Handled = true;
            }
        }

        private void OnFileRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (!_settings.ShowContextMenu) return;
            if (sender is Grid grid && grid.DataContext is FileViewModel file)
            {
                var flyout = _contextMenuService.BuildFileMenu(file, this);
                flyout.ShowAt(grid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(grid)
                });
                e.Handled = true;
            }
        }

        private void OnSidebarDriveRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.DataContext is DriveItem drive)
            {
                var flyout = _contextMenuService.BuildDriveMenu(drive, this);
                flyout.ShowAt(grid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(grid)
                });
                e.Handled = true;
            }
        }

        private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            // Allow dragging both files and folders
            var items = e.Items.OfType<FileSystemViewModel>().ToList();
            if (items.Count == 0) { e.Cancel = true; return; }

            var paths = items.Select(i => i.Path).ToList();
            e.Data.SetText(string.Join("\n", paths));
            e.Data.Properties["SourcePaths"] = paths;
            e.Data.Properties["SourcePane"] = DeterminePane(sender);
            e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
        }

        private string DeterminePane(object sender)
        {
            if (sender is DependencyObject depObj)
            {
                if (IsDescendant(RightPaneContainer, depObj))
                    return "Right";
            }
            return "Left";
        }

        private void OnFavoritesDragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.Text))
            {
                e.AcceptedOperation = DataPackageOperation.Link;
                e.DragUIOverride.Caption = "즐겨찾기에 추가";
            }
        }

        private async void OnFavoritesDrop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.Text))
            {
                var path = await e.DataView.GetTextAsync();
                if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
                {
                    ViewModel.AddToFavorites(path);
                    Helpers.DebugLogger.Log($"[Sidebar] Folder dropped to favorites: {path}");
                }
            }
        }

        // =================================================================
        //  Drag & Drop: Folder item targets (drop file onto a folder)
        // =================================================================

        private void OnFolderItemDragOver(object sender, DragEventArgs e)
        {
            if (sender is not Grid grid || grid.DataContext is not FolderViewModel targetFolder) return;

            // Check if data contains paths
            if (!e.DataView.Contains(StandardDataFormats.Text) &&
                !e.DataView.Properties.ContainsKey("SourcePaths")) return;

            // Prevent dropping onto self (check source paths)
            if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths)
            {
                if (srcPaths.Any(p => p.Equals(targetFolder.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    e.AcceptedOperation = DataPackageOperation.None;
                    return;
                }
                // Prevent dropping parent into child
                if (srcPaths.Any(p => targetFolder.Path.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)))
                {
                    e.AcceptedOperation = DataPackageOperation.None;
                    return;
                }
            }

            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            e.AcceptedOperation = shift ? DataPackageOperation.Move : DataPackageOperation.Copy;
            e.DragUIOverride.Caption = shift ? $"Move to {targetFolder.Name}" : $"Copy to {targetFolder.Name}";
            e.DragUIOverride.IsCaptionVisible = true;

            // Visual feedback: highlight background
            grid.Background = new SolidColorBrush(Microsoft.UI.Colors.White) { Opacity = 0.08 };

            e.Handled = true;
        }

        private async void OnFolderItemDrop(object sender, DragEventArgs e)
        {
            if (sender is not Grid grid || grid.DataContext is not FolderViewModel targetFolder) return;

            // Reset highlight
            grid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            var paths = await ExtractDropPaths(e);
            if (paths.Count == 0) return;

            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            await HandleDropAsync(paths, targetFolder.Path, isMove: shift);
            e.Handled = true;
        }

        private void OnFolderItemDragLeave(object sender, DragEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        // =================================================================
        //  Drag & Drop: Column-level targets (drop into current folder)
        // =================================================================

        private void OnColumnDragOver(object sender, DragEventArgs e)
        {
            if (sender is not ListView listView || listView.DataContext is not FolderViewModel folderVm) return;
            if (!e.DataView.Contains(StandardDataFormats.Text) &&
                !e.DataView.Properties.ContainsKey("SourcePaths")) return;

            // Prevent dropping into the same folder the items came from
            if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths)
            {
                if (srcPaths.All(p => System.IO.Path.GetDirectoryName(p)?.Equals(folderVm.Path, StringComparison.OrdinalIgnoreCase) == true))
                {
                    e.AcceptedOperation = DataPackageOperation.None;
                    return;
                }
            }

            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            e.AcceptedOperation = shift ? DataPackageOperation.Move : DataPackageOperation.Copy;
            e.DragUIOverride.Caption = shift ? $"Move to {folderVm.Name}" : $"Copy to {folderVm.Name}";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        private async void OnColumnDrop(object sender, DragEventArgs e)
        {
            if (sender is not ListView listView || listView.DataContext is not FolderViewModel folderVm) return;

            var paths = await ExtractDropPaths(e);
            if (paths.Count == 0) return;

            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            await HandleDropAsync(paths, folderVm.Path, isMove: shift);
        }

        // =================================================================
        //  Drag & Drop: Shared helpers
        // =================================================================

        private async Task<List<string>> ExtractDropPaths(DragEventArgs e)
        {
            if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths)
                return srcPaths;

            if (e.DataView.Contains(StandardDataFormats.Text))
            {
                var text = await e.DataView.GetTextAsync();
                if (!string.IsNullOrEmpty(text))
                    return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            return new List<string>();
        }

        private async System.Threading.Tasks.Task HandleDropAsync(List<string> sourcePaths, string destFolder, bool isMove)
        {
            // Validate: don't drop onto itself
            sourcePaths = sourcePaths.Where(p =>
                !p.Equals(destFolder, StringComparison.OrdinalIgnoreCase) &&
                !destFolder.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (sourcePaths.Count == 0) return;

            IFileOperation op = isMove
                ? new MoveFileOperation(sourcePaths, destFolder)
                : new CopyFileOperation(sourcePaths, destFolder);

            await ViewModel.ExecuteFileOperationAsync(op);

            Helpers.DebugLogger.Log($"[DragDrop] {(isMove ? "Moved" : "Copied")} {sourcePaths.Count} item(s) to {destFolder}");
        }

        // =================================================================
        //  Drag & Drop: Cross-pane (left ↔ right)
        // =================================================================

        private void OnPaneDragOver(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;

            // Determine source and target panes
            var sourcePane = "Left";
            if (e.DataView.Properties.TryGetValue("SourcePane", out var sp) && sp is string s)
                sourcePane = s;

            bool isLeftTarget = fe.Name == "LeftPaneContainer";
            string targetPane = isLeftTarget ? "Left" : "Right";

            // Only handle cross-pane drops (same-pane handled by folder/column handlers)
            if (sourcePane == targetPane)
            {
                return;
            }

            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
                                | Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;

            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            e.DragUIOverride.Caption = shift ? "이동" : "복사";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;

            // Show drop overlay
            var overlay = isLeftTarget ? LeftDropOverlay : RightDropOverlay;
            overlay.Opacity = 0.05;

            e.Handled = true;
        }

        private async void OnPaneDrop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;

            var sourcePane = "Left";
            if (e.DataView.Properties.TryGetValue("SourcePane", out var sp) && sp is string s)
                sourcePane = s;

            bool isLeftTarget = fe.Name == "LeftPaneContainer";
            string targetPane = isLeftTarget ? "Left" : "Right";

            // Only handle cross-pane
            if (sourcePane == targetPane) return;

            // Hide overlay
            var overlay = isLeftTarget ? LeftDropOverlay : RightDropOverlay;
            overlay.Opacity = 0;

            var paths = await ExtractDropPaths(e);
            if (paths.Count == 0) return;

            // Destination = target pane's current folder
            var targetExplorer = isLeftTarget ? ViewModel.Explorer : ViewModel.RightExplorer;
            var destFolder = targetExplorer?.CurrentFolder?.Path;
            if (string.IsNullOrEmpty(destFolder)) return;

            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            await HandleDropAsync(paths, destFolder, isMove: shift);
            e.Handled = true;
        }

        private void OnPaneDragLeave(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                bool isLeftTarget = fe.Name == "LeftPaneContainer";
                var overlay = isLeftTarget ? LeftDropOverlay : RightDropOverlay;
                overlay.Opacity = 0;
            }
        }

        /// <summary>
        /// Sidebar item hover effect - show subtle background.
        /// </summary>
        private void OnSidebarItemPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.White) { Opacity = 0.05 };
            }
        }

        /// <summary>
        /// Sidebar item hover exit - remove background.
        /// </summary>
        private void OnSidebarItemPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Transparent);
            }
        }

        // =================================================================
        //  Global Keyboard (Ctrl 조합, F키 등)
        //  handledEventsToo: true로 등록하여 항상 수신
        // =================================================================

        private void OnGlobalKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // 이름 변경 중이면 글로벌 단축키 무시
            var selected = GetCurrentSelected();
            if (selected != null && selected.IsRenaming) return;

            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (ctrl)
            {
                switch (e.Key)
                {
                    case Windows.System.VirtualKey.E:
                        if (shift)
                        {
                            ToggleSplitView();
                            e.Handled = true;
                        }
                        break;

                    case Windows.System.VirtualKey.P:
                        if (shift)
                        {
                            TogglePreviewPanel();
                            e.Handled = true;
                        }
                        break;

                    case Windows.System.VirtualKey.Tab:
                        // Ctrl+Tab: switch between panes
                        if (ViewModel.IsSplitViewEnabled)
                        {
                            ViewModel.ActivePane = ViewModel.ActivePane == ActivePane.Left
                                ? ActivePane.Right : ActivePane.Left;
                            FocusActivePane();
                            e.Handled = true;
                        }
                        break;

                    case Windows.System.VirtualKey.L:
                        ShowAddressBarEditMode();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.F:
                        SearchBox.Focus(FocusState.Keyboard);
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.C:
                        HandleCopy();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.X:
                        HandleCut();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.V:
                        HandlePaste();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.N:
                        if (shift)
                        {
                            HandleNewFolder();
                            e.Handled = true;
                        }
                        break;

                    case Windows.System.VirtualKey.A:
                        HandleSelectAll();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Z:
                        // Undo
                        _ = ViewModel.UndoCommand.ExecuteAsync(null);
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Y:
                        // Redo
                        _ = ViewModel.RedoCommand.ExecuteAsync(null);
                        e.Handled = true;
                        break;

                    case (Windows.System.VirtualKey)192: // VK_OEM_3 = backtick/tilde key
                        // Ctrl+`: Open terminal
                        HandleOpenTerminal();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Number1:
                        // Ctrl+1: Miller Columns
                        ViewModel.SwitchViewMode(Models.ViewMode.MillerColumns);
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Number2:
                        // Ctrl+2: Details
                        ViewModel.SwitchViewMode(Models.ViewMode.Details);
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Number3:
                        // Ctrl+3: Icon (마지막 Icon 크기)
                        ViewModel.SwitchViewMode(ViewModel.CurrentIconSize);
                        GetActiveIconView()?.UpdateIconSize(ViewModel.CurrentIconSize);
                        e.Handled = true;
                        break;
                }
            }
            else if (shift)
            {
                // Shift without Ctrl
                switch (e.Key)
                {
                    case Windows.System.VirtualKey.Delete:
                        HandlePermanentDelete();
                        e.Handled = true;
                        break;
                }
            }
            else
            {
                switch (e.Key)
                {
                    case Windows.System.VirtualKey.F5:
                        HandleRefresh();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.F2:
                        HandleRename();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Delete:
                        HandleDelete(); // Send to Recycle Bin
                        e.Handled = true;
                        break;
                }
            }
        }

        // =================================================================
        //  Miller Columns Keyboard (ItemsControl level)
        // =================================================================

        private void OnMillerKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // ★ 이름 변경 직후의 Enter/Esc가 파일 실행으로 이어지는 것을 방지
            if (_justFinishedRename)
            {
                _justFinishedRename = false;
                e.Handled = true;
                return;
            }

            // ★ 이름 변경 중이면 밀러 키보드 처리 안 함
            var currentSelected = GetCurrentSelected();
            if (currentSelected != null && currentSelected.IsRenaming) return;

            // ★ Ctrl/Alt 조합이면 type-ahead 처리 안 하고 글로벌 핸들러에 맡김
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
                      .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (ctrl || alt) return;

            var columns = ViewModel.ActiveExplorer.Columns;
            if (columns.Count == 0) return;

            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Right:
                    HandleRightArrow(activeIndex);
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Left:
                    HandleLeftArrow(activeIndex);
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Enter:
                    HandleEnter(activeIndex);
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Back:
                    HandleLeftArrow(activeIndex);
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Space:
                    if (_settings.EnableQuickLook)
                    {
                        HandleQuickLook(activeIndex);
                        e.Handled = true;
                    }
                    else
                    {
                        HandleTypeAhead(e, activeIndex);
                    }
                    break;

                default:
                    HandleTypeAhead(e, activeIndex);
                    break;
            }
        }

        // =================================================================
        //  P0: Navigation
        // =================================================================

        private void HandleRightArrow(int activeIndex)
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            var currentColumn = columns[activeIndex];

            if (currentColumn.SelectedChild is FolderViewModel && activeIndex + 1 < columns.Count)
            {
                FocusColumnAsync(activeIndex + 1);
            }
        }

        private void HandleLeftArrow(int activeIndex)
        {
            if (activeIndex > 0)
            {
                FocusColumnAsync(activeIndex - 1);
            }
        }

        private void HandleEnter(int activeIndex)
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            var currentColumn = columns[activeIndex];

            if (currentColumn.SelectedChild is FolderViewModel)
            {
                HandleRightArrow(activeIndex);
            }
            else if (currentColumn.SelectedChild is FileViewModel fileVm)
            {
                try
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(fileVm.Path));
                }
                catch { }
            }
        }

        private void HandleTypeAhead(KeyRoutedEventArgs e, int activeIndex)
        {
            char ch = KeyToChar(e.Key);
            if (ch == '\0') return;

            _typeAheadBuffer += ch;
            _typeAheadTimer?.Stop();
            _typeAheadTimer?.Start();

            var columns = ViewModel.ActiveExplorer.Columns;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var column = columns[activeIndex];
            var match = column.Children.FirstOrDefault(c =>
                c.Name.StartsWith(_typeAheadBuffer, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                column.SelectedChild = match;
                var listView = GetListViewForColumn(activeIndex);
                listView?.ScrollIntoView(match);
            }

            e.Handled = true;
        }

        // =================================================================
        //  Quick Look (Space key preview)
        // =================================================================

        private bool _isQuickLookOpen = false;

        private async void HandleQuickLook(int activeIndex)
        {
            if (_isQuickLookOpen) return;

            var columns = ViewModel.ActiveExplorer.Columns;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var selected = columns[activeIndex].SelectedChild;
            if (selected == null) return;

            _isQuickLookOpen = true;
            try
            {
                var previewService = App.Current.Services.GetRequiredService<Services.PreviewService>();
                bool isFolder = selected is FolderViewModel;
                var previewType = previewService.GetPreviewType(selected.Path, isFolder);

                // Build dialog content
                var content = await BuildQuickLookContentAsync(previewService, previewType, selected);

                var dialog = new ContentDialog
                {
                    Title = selected.Name,
                    Content = content,
                    CloseButtonText = _loc.Get("Cancel"),
                    XamlRoot = this.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                // Set dialog size constraints
                dialog.Resources["ContentDialogMaxWidth"] = 800.0;

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[QuickLook] Error: {ex.Message}");
            }
            finally
            {
                _isQuickLookOpen = false;
                FocusColumnAsync(activeIndex);
            }
        }

        private async Task<FrameworkElement> BuildQuickLookContentAsync(
            Services.PreviewService previewService, Models.PreviewType previewType,
            ViewModels.FileSystemViewModel selected)
        {
            var meta = previewService.GetBasicMetadata(selected.Path);
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));

            switch (previewType)
            {
                case Models.PreviewType.Image:
                    var bitmap = await previewService.LoadImagePreviewAsync(selected.Path, 800, cts.Token);
                    if (bitmap != null)
                    {
                        var img = new Image
                        {
                            Source = bitmap,
                            MaxWidth = 760,
                            MaxHeight = 500,
                            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                        };
                        return WrapWithMetadata(img, meta);
                    }
                    return CreateGenericPreview(meta);

                case Models.PreviewType.Text:
                    var text = await previewService.LoadTextPreviewAsync(selected.Path, cts.Token);
                    if (text != null)
                    {
                        var textBlock = new TextBlock
                        {
                            Text = text,
                            TextWrapping = TextWrapping.Wrap,
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                            FontSize = 12,
                            IsTextSelectionEnabled = true,
                            MaxHeight = 400
                        };
                        var scroller = new ScrollViewer
                        {
                            Content = textBlock,
                            MaxHeight = 400,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                        };
                        return WrapWithMetadata(scroller, meta);
                    }
                    return CreateGenericPreview(meta);

                case Models.PreviewType.Pdf:
                    var pdfBitmap = await previewService.LoadPdfPreviewAsync(selected.Path, cts.Token);
                    if (pdfBitmap != null)
                    {
                        var pdfImg = new Image
                        {
                            Source = pdfBitmap,
                            MaxWidth = 760,
                            MaxHeight = 500,
                            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                        };
                        return WrapWithMetadata(pdfImg, meta);
                    }
                    return CreateGenericPreview(meta);

                case Models.PreviewType.Folder:
                    int count = previewService.GetFolderItemCount(selected.Path);
                    var folderInfo = new TextBlock
                    {
                        Text = $"{count} items",
                        FontSize = 16,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 20, 0, 10)
                    };
                    var folderMeta = new TextBlock
                    {
                        Text = $"{_loc.Get("Date")}: {meta.Modified:g}",
                        FontSize = 12,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SpanTextSecondaryBrush"],
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    var folderStack = new StackPanel { MinWidth = 300 };
                    folderStack.Children.Add(folderInfo);
                    folderStack.Children.Add(folderMeta);
                    return folderStack;

                default: // Generic
                    return CreateGenericPreview(meta);
            }
        }

        private StackPanel WrapWithMetadata(FrameworkElement preview, Services.FilePreviewMetadata meta)
        {
            var stack = new StackPanel { Spacing = 8, MinWidth = 300 };
            stack.Children.Add(preview);

            var metaText = new TextBlock
            {
                Text = $"{meta.SizeFormatted}  |  {meta.Modified:g}",
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SpanTextSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(metaText);
            return stack;
        }

        private StackPanel CreateGenericPreview(Services.FilePreviewMetadata meta)
        {
            var stack = new StackPanel { Spacing = 8, MinWidth = 300 };

            var icon = new FontIcon
            {
                Glyph = "\uE7C3", // generic file icon
                FontSize = 48,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 10)
            };
            stack.Children.Add(icon);

            var nameText = new TextBlock
            {
                Text = meta.FileName,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(nameText);

            var details = new TextBlock
            {
                Text = $"{meta.SizeFormatted}  |  {meta.Extension}  |  {meta.Modified:g}",
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SpanTextSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(details);

            return stack;
        }

        // =================================================================
        //  P1: Clipboard (Ctrl+C/X/V)
        // =================================================================

        // =================================================================
        //  Select All (Ctrl+A)
        // =================================================================

        private void HandleSelectAll()
        {
            var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

            if (viewMode == ViewMode.MillerColumns)
            {
                int activeIndex = GetActiveColumnIndex();
                if (activeIndex < 0) activeIndex = ViewModel.ActiveExplorer.Columns.Count - 1;
                var listView = GetListViewForColumn(activeIndex);
                listView?.SelectAll();
            }
            // Details/Icon views: Extended mode natively handles Ctrl+A via ListView/GridView
        }

        // =================================================================
        //  Helper: Get current selected items (multi or single)
        // =================================================================

        private List<FileSystemViewModel> GetCurrentSelectedItems()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetCurrentColumnIndex();
            if (activeIndex < 0 || activeIndex >= columns.Count) return new List<FileSystemViewModel>();

            var col = columns[activeIndex];
            return col.GetSelectedItemsList();
        }

        private void HandleCopy()
        {
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0)
            {
                // Fallback: auto-select first item if nothing is selected
                var columns = ViewModel.ActiveExplorer.Columns;
                int activeIndex = GetCurrentColumnIndex();
                if (activeIndex >= 0 && activeIndex < columns.Count)
                {
                    var currentColumn = columns[activeIndex];
                    if (currentColumn.Children.Count > 0)
                    {
                        currentColumn.SelectedChild = currentColumn.Children[0];
                        selectedItems = new List<FileSystemViewModel> { currentColumn.Children[0] };
                    }
                }
            }
            if (selectedItems.Count == 0) return;

            _clipboardPaths.Clear();
            foreach (var item in selectedItems)
                _clipboardPaths.Add(item.Path);
            _isCutOperation = false;

            var dataPackage = new DataPackage();
            dataPackage.SetText(string.Join("\n", _clipboardPaths));
            Clipboard.SetContent(dataPackage);

            // Toast notification
            if (selectedItems.Count == 1)
            {
                var name = System.IO.Path.GetFileName(selectedItems[0].Path);
                ViewModel.ShowToast($"\"{name}\" 복사됨");
            }
            else
            {
                ViewModel.ShowToast($"{selectedItems.Count}개 항목 복사됨");
            }

            Helpers.DebugLogger.Log($"[Clipboard] Copied {_clipboardPaths.Count} item(s)");
        }

        private void HandleCut()
        {
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0)
            {
                var columns = ViewModel.ActiveExplorer.Columns;
                int activeIndex = GetCurrentColumnIndex();
                if (activeIndex >= 0 && activeIndex < columns.Count)
                {
                    var currentColumn = columns[activeIndex];
                    if (currentColumn.Children.Count > 0)
                    {
                        currentColumn.SelectedChild = currentColumn.Children[0];
                        selectedItems = new List<FileSystemViewModel> { currentColumn.Children[0] };
                    }
                }
            }
            if (selectedItems.Count == 0) return;

            _clipboardPaths.Clear();
            foreach (var item in selectedItems)
                _clipboardPaths.Add(item.Path);
            _isCutOperation = true;

            var dataPackage = new DataPackage();
            dataPackage.SetText(string.Join("\n", _clipboardPaths));
            Clipboard.SetContent(dataPackage);

            // Toast notification
            if (selectedItems.Count == 1)
            {
                var name = System.IO.Path.GetFileName(selectedItems[0].Path);
                ViewModel.ShowToast($"\"{name}\" 잘라내기 완료");
            }
            else
            {
                ViewModel.ShowToast($"{selectedItems.Count}개 항목 잘라내기 완료");
            }

            Helpers.DebugLogger.Log($"[Clipboard] Cut {_clipboardPaths.Count} item(s)");
        }

        private async void HandlePaste()
        {
            if (_clipboardPaths.Count == 0) return;

            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var targetFolder = columns[activeIndex];
            string destDir = targetFolder.Path;

            Span.Services.FileOperations.IFileOperation op = _isCutOperation
                ? new Span.Services.FileOperations.MoveFileOperation(new List<string>(_clipboardPaths), destDir)
                : new Span.Services.FileOperations.CopyFileOperation(new List<string>(_clipboardPaths), destDir);

            await ViewModel.ExecuteFileOperationAsync(op, activeIndex);

            if (_isCutOperation) _clipboardPaths.Clear();
        }

        private static void CopyDirectory(string src, string dest)
        {
            var dir = new System.IO.DirectoryInfo(src);
            System.IO.Directory.CreateDirectory(dest);
            foreach (var file in dir.GetFiles())
                file.CopyTo(System.IO.Path.Combine(dest, file.Name), true);
            foreach (var subDir in dir.GetDirectories())
                CopyDirectory(subDir.FullName, System.IO.Path.Combine(dest, subDir.Name));
        }

        // =================================================================
        //  P1: New Folder (Ctrl+Shift+N)
        // =================================================================

        private async void HandleNewFolder()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var currentFolder = columns[activeIndex];
            string baseName = _loc.Get("NewFolderBaseName");
            string newPath = System.IO.Path.Combine(currentFolder.Path, baseName);

            int count = 1;
            while (System.IO.Directory.Exists(newPath))
            {
                newPath = System.IO.Path.Combine(currentFolder.Path, $"{baseName} ({count})");
                count++;
            }

            var op = new Span.Services.FileOperations.NewFolderOperation(newPath);
            await ViewModel.ExecuteFileOperationAsync(op, activeIndex);

            // Select the new folder and start inline rename
            var newFolder = currentFolder.Children.FirstOrDefault(c =>
                c.Path.Equals(newPath, StringComparison.OrdinalIgnoreCase));
            if (newFolder != null)
            {
                currentFolder.SelectedChild = newFolder;
                newFolder.BeginRename();
                await System.Threading.Tasks.Task.Delay(100);
                FocusRenameTextBox(activeIndex);
            }
        }

        // =================================================================
        //  P1: Refresh (F5)
        // =================================================================

        private async void HandleRefresh()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var column = columns[activeIndex];
            var previousSelection = column.SelectedChild;

            await column.ReloadAsync();

            // 이전 선택 복원 (이름 기준)
            if (previousSelection != null)
            {
                var restored = column.Children.FirstOrDefault(c =>
                    c.Name.Equals(previousSelection.Name, StringComparison.OrdinalIgnoreCase));
                if (restored != null)
                    column.SelectedChild = restored;
            }
        }

        // =================================================================
        //  P2: Rename (F2) — 인라인 이름 변경
        // =================================================================

        private void HandleRename()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetCurrentColumnIndex(); // Fixed: Use GetCurrentColumnIndex
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var currentColumn = columns[activeIndex];
            var selected = currentColumn.SelectedChild;

            // 선택된 항목이 없으면 첫 번째 항목 선택
            if (selected == null && currentColumn.Children.Count > 0)
            {
                selected = currentColumn.Children[0];
                currentColumn.SelectedChild = selected;
            }

            if (selected == null) return;

            selected.BeginRename();

            // TextBox에 포커스
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed) return;
                FocusRenameTextBox(activeIndex);
            });
        }

        /// <summary>
        /// 인라인 rename TextBox에 포커스를 맞추고 텍스트 전체 선택.
        /// </summary>
        private void FocusRenameTextBox(int columnIndex)
        {
            var listView = GetListViewForColumn(columnIndex);
            if (listView == null) return;

            var columns = ViewModel.ActiveExplorer.Columns;
            if (columnIndex >= columns.Count) return;

            var column = columns[columnIndex];
            if (column.SelectedChild == null) return;

            int idx = column.Children.IndexOf(column.SelectedChild);
            if (idx < 0) return;

            var container = listView.ContainerFromIndex(idx) as UIElement;
            if (container == null) return;

            var textBox = FindChild<TextBox>(container as DependencyObject ?? container as DependencyObject);
            if (textBox != null)
            {
                textBox.Focus(FocusState.Keyboard);
                textBox.SelectAll();
            }
        }

        private void OnRenameTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            var vm = textBox.DataContext as FileSystemViewModel;
            if (vm == null) return;

            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                vm.CommitRename();
                _justFinishedRename = true; // OnMillerKeyDown이 이 Enter를 파일 실행으로 처리하지 않도록
                e.Handled = true;
                FocusSelectedItem();
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                vm.CancelRename();
                _justFinishedRename = true;
                e.Handled = true;
                FocusSelectedItem();
            }
        }

        private void OnRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            var vm = textBox.DataContext as FileSystemViewModel;
            if (vm == null || !vm.IsRenaming) return;

            // 포커스 잃으면 취소 (ESC와 동일한 동작)
            vm.CancelRename();
            _justFinishedRename = true;
        }

        /// <summary>
        /// 현재 선택된 항목의 ListViewItem 컨테이너에 포커스를 복원.
        /// 이름 변경 후 화살표 키가 그 자리에서 동작하도록.
        /// </summary>
        private void FocusSelectedItem()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var column = columns[activeIndex];
            if (column.SelectedChild == null) return;

            var listView = GetListViewForColumn(activeIndex);
            if (listView == null) return;

            int idx = column.Children.IndexOf(column.SelectedChild);
            if (idx < 0) return;

            // 약간의 딜레이 후 ListViewItem 컨테이너에 포커스
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed) return;
                var container = listView.ContainerFromIndex(idx) as UIElement;
                container?.Focus(FocusState.Keyboard);
            });
        }

        // =================================================================
        //  P2: Delete (Delete key)
        // =================================================================

        private async void HandleDelete()
        {
            // ★ Save activeIndex BEFORE showing dialog (modal dialog steals focus)
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetCurrentColumnIndex();
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var currentColumn = columns[activeIndex];

            // Multi-selection support
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0 && currentColumn.Children.Count > 0)
            {
                currentColumn.SelectedChild = currentColumn.Children[0];
                selectedItems = new List<FileSystemViewModel> { currentColumn.Children[0] };
            }
            if (selectedItems.Count == 0) return;

            var selected = selectedItems[0]; // For display name in dialog
            // Remember the selected item's index for smart selection after delete
            int selectedIndex = currentColumn.Children.IndexOf(selected);

            // Confirm delete (send to Recycle Bin)
            if (_settings.ConfirmDelete)
            {
                string confirmContent = selectedItems.Count == 1
                    ? string.Format(_loc.Get("DeleteConfirmContent"), selected.Name)
                    : string.Format(_loc.Get("DeleteConfirmContent"), $"{selectedItems.Count} items");

                var dialog = new ContentDialog
                {
                    Title = _loc.Get("DeleteConfirmTitle"),
                    Content = confirmContent,
                    PrimaryButtonText = _loc.Get("Delete"),
                    CloseButtonText = _loc.Get("Cancel"),
                    XamlRoot = this.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return;
            }

            var paths = selectedItems.Select(i => i.Path).ToList();
            Helpers.DebugLogger.Log($"[HandleDelete] Dialog confirmed. Deleting {paths.Count} item(s), ActiveIndex: {activeIndex}");
            Helpers.DebugLogger.Log($"[HandleDelete] Columns before delete: {string.Join(" > ", ViewModel.ActiveExplorer.Columns.Select(c => c.Name))}");

            // Execute delete operation (send to Recycle Bin)
            var operation = new DeleteFileOperation(paths, permanent: false);
            Helpers.DebugLogger.Log($"[HandleDelete] Calling ExecuteFileOperationAsync with targetColumnIndex={activeIndex}");
            await ViewModel.ExecuteFileOperationAsync(operation, activeIndex);

            Helpers.DebugLogger.Log($"[HandleDelete] After ExecuteFileOperationAsync. CurrentColumn children count: {currentColumn.Children.Count}");

            // ★ Smart selection: Select the item at the same index, or the last item if index is out of bounds
            // Note: RefreshCurrentFolderAsync() already cleared selection and reloaded
            if (currentColumn.Children.Count > 0)
            {
                int newIndex = Math.Min(selectedIndex, currentColumn.Children.Count - 1);
                currentColumn.SelectedChild = currentColumn.Children[newIndex];
                Helpers.DebugLogger.Log($"[HandleDelete] Smart selection: selectedIndex={selectedIndex}, newIndex={newIndex}, selected={currentColumn.Children[newIndex].Name}");
            }
            else
            {
                Helpers.DebugLogger.Log($"[HandleDelete] No children after delete - selection cleared");
            }

            // Remove columns after deleted item (using proper cleanup)
            Helpers.DebugLogger.Log($"[HandleDelete] Cleaning up columns from index {activeIndex + 1}");
            ViewModel.ActiveExplorer.CleanupColumnsFrom(activeIndex + 1);

            Helpers.DebugLogger.Log($"[HandleDelete] Columns after cleanup: {string.Join(" > ", ViewModel.ActiveExplorer.Columns.Select(c => c.Name))}");

            // Restore focus
            Helpers.DebugLogger.Log($"[HandleDelete] Restoring focus to column {activeIndex}");
            FocusColumnAsync(activeIndex);
            Helpers.DebugLogger.Log($"[HandleDelete] ===== COMPLETE =====");
        }

        private async void HandlePermanentDelete()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var currentColumn = columns[activeIndex];

            // Multi-selection support
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0) return;

            var selected = selectedItems[0];
            int selectedIndex = currentColumn.Children.IndexOf(selected);

            string confirmContent = selectedItems.Count == 1
                ? string.Format(_loc.Get("PermanentDeleteContent"), selected.Name)
                : string.Format(_loc.Get("PermanentDeleteContent"), $"{selectedItems.Count} items");

            var dialog = new ContentDialog
            {
                Title = _loc.Get("PermanentDeleteTitle"),
                Content = confirmContent,
                PrimaryButtonText = _loc.Get("PermanentDelete"),
                CloseButtonText = _loc.Get("Cancel"),
                XamlRoot = this.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            // Execute permanent delete operation
            var paths = selectedItems.Select(i => i.Path).ToList();
            var operation = new DeleteFileOperation(paths, permanent: true);
            await ViewModel.ExecuteFileOperationAsync(operation, activeIndex);

            // ★ Smart selection: Select the item at the same index, or the last item if index is out of bounds
            // Note: RefreshCurrentFolderAsync() already cleared selection and reloaded
            if (currentColumn.Children.Count > 0)
            {
                int newIndex = Math.Min(selectedIndex, currentColumn.Children.Count - 1);
                currentColumn.SelectedChild = currentColumn.Children[newIndex];
            }

            // Remove columns after deleted item (using proper cleanup)
            ViewModel.ActiveExplorer.CleanupColumnsFrom(activeIndex + 1);

            // Restore focus
            FocusColumnAsync(activeIndex);
        }



        // =================================================================
        //  Search Box
        // =================================================================

        private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                GetActiveMillerColumnsControl().Focus(FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Enter)
            {
                string query = SearchBox.Text.Trim();
                if (string.IsNullOrEmpty(query)) return;

                var columns = ViewModel.ActiveExplorer.Columns;
                int activeIndex = GetActiveColumnIndex();
                if (activeIndex < 0) activeIndex = columns.Count - 1;
                if (activeIndex < 0 || activeIndex >= columns.Count) return;

                var column = columns[activeIndex];
                var match = column.Children.FirstOrDefault(c =>
                    c.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    column.SelectedChild = match;
                    var listView = GetListViewForColumn(activeIndex);
                    listView?.ScrollIntoView(match);
                }

                e.Handled = true;
            }
        }

        // =================================================================
        //  P1: Focus Tracking (Active Column)
        // =================================================================

        private void OnMillerColumnGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is FolderViewModel folderVm)
            {
                // Detect which pane and set ActivePane + SetActiveColumn
                if (ViewModel.IsSplitViewEnabled && IsDescendant(RightPaneContainer, fe))
                {
                    ViewModel.ActivePane = ActivePane.Right;
                    ViewModel.RightExplorer.SetActiveColumn(folderVm);
                }
                else
                {
                    ViewModel.ActivePane = ActivePane.Left;
                    ViewModel.LeftExplorer.SetActiveColumn(folderVm);
                }
            }
        }

        /// <summary>
        /// ListView 선택 변경 시 ViewModel과 명시적으로 동기화.
        /// x:Bind Mode=TwoWay가 복잡한 객체에서 제대로 동작하지 않을 수 있으므로.
        /// </summary>
        private void OnMillerColumnSelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection) return; // Prevent circular updates

            if (sender is ListView listView && listView.DataContext is FolderViewModel folderVm)
            {
                _isSyncingSelection = true;
                try
                {
                    // Multi-selection support: sync all selected items
                    if (listView.SelectedItems.Count > 1)
                    {
                        // Multi-selection: use SyncSelectedItems (suppresses navigation)
                        folderVm.SyncSelectedItems(listView.SelectedItems);
                    }
                    else
                    {
                        // Single selection: sync SelectedChild directly for navigation
                        var newSelection = listView.SelectedItem as FileSystemViewModel;
                        if (!ReferenceEquals(folderVm.SelectedChild, newSelection))
                        {
                            folderVm.SelectedChild = newSelection;
                        }
                        // Keep SelectedItems in sync for single selection too
                        folderVm.SyncSelectedItems(listView.SelectedItems);
                    }

                    // Update preview for the active pane
                    var previewItem = listView.SelectedItems.Count == 1
                        ? listView.SelectedItem as FileSystemViewModel
                        : null;
                    UpdatePreviewForSelection(previewItem);
                }
                finally
                {
                    _isSyncingSelection = false;
                }
            }
        }

        /// <summary>
        /// Handle double-click on Miller Column items (open files).
        /// </summary>
        private void OnMillerColumnDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (sender is ListView listView && listView.DataContext is FolderViewModel folderVm)
            {
                var selected = folderVm.SelectedChild;
                if (selected is FileViewModel file)
                {
                    // Open file with default application
                    try
                    {
                        _ = Windows.System.Launcher.LaunchUriAsync(new Uri(file.Path));
                        Helpers.DebugLogger.Log($"[MainWindow] Miller Column DoubleClick: Opening file {file.Name}");
                    }
                    catch (Exception ex)
                    {
                        Helpers.DebugLogger.Log($"[MainWindow] Error opening file: {ex.Message}");
                    }
                }
                else if (selected is FolderViewModel folder && _settings.MillerClickBehavior == "double")
                {
                    // In double-click mode, navigate into folder on double-click
                    var explorer = ViewModel.ActiveExplorer;
                    explorer.NavigateTo(new FolderItem { Name = folder.Name, Path = folder.Path });
                    Helpers.DebugLogger.Log($"[MainWindow] Miller Column DoubleClick: Navigating to folder {folder.Name}");
                }
            }
        }

        private FileSystemViewModel? GetCurrentSelected()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return null;
            return columns[activeIndex].SelectedChild;
        }

        private void EnsureColumnVisible(int columnIndex)
        {
            var scrollViewer = GetActiveMillerScrollViewer();
            double columnLeft = columnIndex * ColumnWidth;
            double columnRight = columnLeft + ColumnWidth;
            double viewportLeft = scrollViewer.HorizontalOffset;
            double viewportRight = viewportLeft + scrollViewer.ViewportWidth;

            if (columnLeft < viewportLeft)
                scrollViewer.ChangeView(columnLeft, null, null, true);
            else if (columnRight > viewportRight)
                scrollViewer.ChangeView(columnRight - scrollViewer.ViewportWidth, null, null, true);
        }

        private int GetActiveColumnIndex()
        {
            var focused = FocusManager.GetFocusedElement(this.Content.XamlRoot) as DependencyObject;
            if (focused == null) return -1;

            for (int i = 0; i < ViewModel.ActiveExplorer.Columns.Count; i++)
            {
                var listView = GetListViewForColumn(i);
                if (listView != null && IsDescendant(listView, focused))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Get the column index that should be used for operations when focus is lost.
        /// Finds the rightmost column with a SelectedChild.
        /// </summary>
        private int GetCurrentColumnIndex()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            if (columns.Count == 0) return -1;

            // First try to get the focused column
            int focusedIndex = GetActiveColumnIndex();
            if (focusedIndex >= 0) return focusedIndex;

            // If no focus (e.g., toolbar button clicked), find rightmost column with selection
            for (int i = columns.Count - 1; i >= 0; i--)
            {
                if (columns[i].SelectedChild != null)
                    return i;
            }

            // Fallback: use the last column
            return columns.Count - 1;
        }

        private async void FocusColumnAsync(int columnIndex)
        {
            await System.Threading.Tasks.Task.Delay(50);
            if (_isClosed) return;

            var listView = GetListViewForColumn(columnIndex);
            if (listView == null) return;

            var columns = ViewModel.ActiveExplorer.Columns;
            if (columnIndex >= columns.Count) return;

            var column = columns[columnIndex];

            if (column.SelectedChild == null && column.Children.Count > 0)
                column.SelectedChild = column.Children[0];

            if (column.SelectedChild != null)
            {
                int selectedIndex = column.Children.IndexOf(column.SelectedChild);
                if (selectedIndex >= 0)
                {
                    var container = listView.ContainerFromIndex(selectedIndex) as UIElement;
                    container?.Focus(FocusState.Keyboard);
                }
            }
            else
            {
                listView.Focus(FocusState.Keyboard);
            }

            EnsureColumnVisible(columnIndex);
        }

        private ListView? GetListViewForColumn(int columnIndex)
        {
            var control = GetActiveMillerColumnsControl();
            if (control == null) return null;
            var container = control.ContainerFromIndex(columnIndex) as ContentPresenter;
            if (container == null) return null;
            return FindChild<ListView>(container);
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private static bool IsDescendant(DependencyObject parent, DependencyObject child)
        {
            var current = child;
            while (current != null)
            {
                if (current == parent) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private static char KeyToChar(Windows.System.VirtualKey key)
        {
            if (key >= Windows.System.VirtualKey.A && key <= Windows.System.VirtualKey.Z)
                return (char)('a' + (key - Windows.System.VirtualKey.A));
            if (key >= Windows.System.VirtualKey.Number0 && key <= Windows.System.VirtualKey.Number9)
                return (char)('0' + (key - Windows.System.VirtualKey.Number0));
            if (key >= Windows.System.VirtualKey.NumberPad0 && key <= Windows.System.VirtualKey.NumberPad9)
                return (char)('0' + (key - Windows.System.VirtualKey.NumberPad0));
            if (key == Windows.System.VirtualKey.Space) return ' ';
            if (key == (Windows.System.VirtualKey)190) return '.';
            if (key == (Windows.System.VirtualKey)189) return '-';
            return '\0';
        }

        // ============================================================
        //  Breadcrumb Address Bar 핸들러
        // ============================================================

        /// <summary>
        /// 브레드크럼 세그먼트 버튼 클릭 → 해당 폴더로 탐색.
        /// </summary>
        private void OnBreadcrumbSegmentClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string fullPath)
            {
                ViewModel.ActiveExplorer.NavigateToPath(fullPath);
            }
        }

        /// <summary>
        /// 주소 표시줄 빈 공간 클릭 → 편집 모드 전환.
        /// </summary>
        /// <summary>
        /// 주소 표시줄 빈 공간 클릭 → 편집 모드 전환.
        /// </summary>
        private void OnAddressBarContainerClicked(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            // 클릭된 요소가 버튼(또는 버튼 내부 요소)이면 편집 모드로 전환하지 않음
            var element = e.OriginalSource as DependencyObject;
            while (element != null && element != AddressBarContainer)
            {
                if (element is Button) return;
                element = VisualTreeHelper.GetParent(element);
            }

            // 그 외(빈 공간, ScrollViewer 배경 등)를 누르면 편집 모드
            ShowAddressBarEditMode();
        }

        /// <summary>
        /// Navigate to parent folder (Up button clicked).
        /// </summary>
        private void OnNavigateUpClick(object sender, RoutedEventArgs e)
        {
            ViewModel?.ActiveExplorer?.NavigateUp();
            Helpers.DebugLogger.Log("[MainWindow] Up button clicked - navigating to parent folder");
        }

        /// <summary>
        /// 편집 모드 표시: 브레드크럼 숨기고 AutoSuggestBox 표시.
        /// </summary>
        private void ShowAddressBarEditMode()
        {
            BreadcrumbScroller.Visibility = Visibility.Collapsed;
            AddressBarAutoSuggest.Visibility = Visibility.Visible;
            AddressBarAutoSuggest.Text = ViewModel.ActiveExplorer.CurrentPath;
            AddressBarAutoSuggest.Focus(FocusState.Keyboard);

            // Select all text after focus (dispatch to ensure focus is applied first)
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed) return;
                // AutoSuggestBox doesn't have SelectAll, but we can access the inner TextBox
                var textBox = FindChild<TextBox>(AddressBarAutoSuggest);
                textBox?.SelectAll();
            });
        }

        /// <summary>
        /// 브레드크럼 모드로 복귀: AutoSuggestBox 숨기고 브레드크럼 표시.
        /// </summary>
        private void ShowAddressBarBreadcrumbMode()
        {
            AddressBarAutoSuggest.Visibility = Visibility.Collapsed;
            AddressBarAutoSuggest.ItemsSource = null;
            BreadcrumbScroller.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// AutoSuggestBox 텍스트 변경 시 폴더 자동완성 제안.
        /// </summary>
        private void OnAddressBarTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

            var text = sender.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                sender.ItemsSource = null;
                return;
            }

            // Expand environment variables (%APPDATA%, %USERPROFILE%, etc.)
            var expanded = Environment.ExpandEnvironmentVariables(text);

            try
            {
                string? parentDir;
                string prefix;

                // If text ends with '\', list all children of that directory
                if (expanded.EndsWith('\\') || expanded.EndsWith('/'))
                {
                    parentDir = expanded;
                    prefix = string.Empty;
                }
                else
                {
                    parentDir = System.IO.Path.GetDirectoryName(expanded);
                    prefix = System.IO.Path.GetFileName(expanded);
                }

                if (string.IsNullOrEmpty(parentDir) || !System.IO.Directory.Exists(parentDir))
                {
                    // Try matching drive letters (C, D, etc.)
                    if (text.Length <= 2)
                    {
                        var drives = System.IO.DriveInfo.GetDrives()
                            .Where(d => d.IsReady && d.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                            .Select(d => d.Name)
                            .Take(10)
                            .ToList();
                        sender.ItemsSource = drives.Count > 0 ? drives : null;
                    }
                    else
                    {
                        sender.ItemsSource = null;
                    }
                    return;
                }

                var suggestions = new System.IO.DirectoryInfo(parentDir)
                    .GetDirectories()
                    .Where(d => (d.Attributes & System.IO.FileAttributes.Hidden) == 0)
                    .Where(d => string.IsNullOrEmpty(prefix) || d.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d.Name)
                    .Take(10)
                    .Select(d => d.FullName)
                    .ToList();

                sender.ItemsSource = suggestions.Count > 0 ? suggestions : null;
            }
            catch
            {
                // Access denied or invalid path — no suggestions
                sender.ItemsSource = null;
            }
        }

        /// <summary>
        /// 자동완성 항목 선택 시 해당 경로로 텍스트 설정.
        /// </summary>
        private void OnAddressBarSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string path)
            {
                sender.Text = path;
            }
        }

        /// <summary>
        /// Enter 키 또는 제안 항목 클릭 시 네비게이션.
        /// </summary>
        private void OnAddressBarQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var path = args.QueryText?.Trim();
            if (string.IsNullOrEmpty(path)) return;

            // Expand environment variables
            path = Environment.ExpandEnvironmentVariables(path);

            if (System.IO.Directory.Exists(path))
            {
                ViewModel.ActiveExplorer.NavigateToPath(path);
            }
            else if (System.IO.File.Exists(path))
            {
                // Navigate to parent and select the file
                var parent = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                {
                    ViewModel.ActiveExplorer.NavigateToPath(parent);
                }
            }

            ShowAddressBarBreadcrumbMode();
        }

        /// <summary>
        /// 주소 표시줄 포커스 잃으면 브레드크럼 모드로 복귀.
        /// </summary>
        private void OnAddressBarLostFocus(object sender, RoutedEventArgs e)
        {
            // Delay slightly to allow suggestion clicks to process
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed) return;
                // Only hide if the AutoSuggestBox no longer has focus
                if (!AddressBarAutoSuggest.FocusState.HasFlag(FocusState.Keyboard) &&
                    !AddressBarAutoSuggest.FocusState.HasFlag(FocusState.Pointer))
                {
                    ShowAddressBarBreadcrumbMode();
                }
            });
        }

        /// <summary>
        /// AutoSuggestBox에서 Escape 키 처리 → 브레드크럼 모드로 복귀.
        /// </summary>
        private void OnAddressBarAutoSuggestKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                ShowAddressBarBreadcrumbMode();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 경로 복사 버튼 클릭 → 현재 경로를 클립보드에 복사.
        /// </summary>
        private void OnCopyPathClick(object sender, RoutedEventArgs e)
        {
            var path = ViewModel.ActiveExplorer.CurrentPath;
            if (!string.IsNullOrEmpty(path))
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(path);
                Clipboard.SetContent(dataPackage);
            }
        }

        // =================================================================
        // UNIFIED BAR BUTTON HANDLERS
        // =================================================================

        private void OnCutClick(object sender, RoutedEventArgs e)
        {
            HandleCut();
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            HandleCopy();
        }

        private void OnPasteClick(object sender, RoutedEventArgs e)
        {
            HandlePaste();
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            HandleDelete();
        }

        private void OnNewFolderClick(object sender, RoutedEventArgs e)
        {
            HandleNewFolder();
        }

        private void OnRenameClick(object sender, RoutedEventArgs e)
        {
            HandleRename();
        }

        // Sort handlers
        private void OnSortByName(object sender, RoutedEventArgs e)
        {
            _currentSortField = "Name";
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        private void OnSortByDate(object sender, RoutedEventArgs e)
        {
            _currentSortField = "Date";
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        private void OnSortBySize(object sender, RoutedEventArgs e)
        {
            _currentSortField = "Size";
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        private void OnSortByType(object sender, RoutedEventArgs e)
        {
            _currentSortField = "Type";
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        private void OnSortAscending(object sender, RoutedEventArgs e)
        {
            _currentSortAscending = true;
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        private void OnSortDescending(object sender, RoutedEventArgs e)
        {
            _currentSortAscending = false;
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        private void SortCurrentColumn(string sortBy, bool? ascending = null)
        {
            var activeIndex = GetCurrentColumnIndex();
            if (activeIndex < 0 || activeIndex >= ViewModel.ActiveExplorer.Columns.Count)
                return;

            var column = ViewModel.ActiveExplorer.Columns[activeIndex];
            if (column.Children == null || column.Children.Count == 0)
                return;

            // CRITICAL: Save current selection BEFORE sorting
            var savedSelection = column.SelectedChild;

            // 🔒 Set sorting flag to prevent PropertyChanged events during sort
            column.IsSorting = true;

            try
            {
                // Determine sort direction
                bool isAscending = ascending ?? true;

            // Sort folders first, then files (Windows Explorer behavior)
            IEnumerable<FileSystemViewModel> sorted;

            switch (sortBy)
            {
                case "Name":
                    sorted = isAscending
                        ? column.Children
                            .OrderBy(x => x is FileViewModel ? 1 : 0)  // Folders first
                            .ThenBy(x => x.Name, Helpers.NaturalStringComparer.Instance)
                        : column.Children
                            .OrderBy(x => x is FileViewModel ? 1 : 0)  // Folders first
                            .ThenByDescending(x => x.Name, Helpers.NaturalStringComparer.Instance);
                    break;

                case "Date":
                    sorted = isAscending
                        ? column.Children
                            .OrderBy(x => x is FileViewModel ? 1 : 0)
                            .ThenBy(x => GetDateModified(x))
                        : column.Children
                            .OrderBy(x => x is FileViewModel ? 1 : 0)
                            .ThenByDescending(x => GetDateModified(x));
                    break;

                case "Size":
                    sorted = isAscending
                        ? column.Children
                            .OrderBy(x => x is FileViewModel ? 1 : 0)
                            .ThenBy(x => GetSize(x))
                        : column.Children
                            .OrderBy(x => x is FileViewModel ? 1 : 0)
                            .ThenByDescending(x => GetSize(x));
                    break;

                case "Type":
                    sorted = isAscending
                        ? column.Children
                            .OrderBy(x => x is FileViewModel ? 1 : 0)
                            .ThenBy(x => GetFileType(x))
                            .ThenBy(x => x.Name, Helpers.NaturalStringComparer.Instance)
                        : column.Children
                            .OrderBy(x => x is FileViewModel ? 1 : 0)
                            .ThenByDescending(x => GetFileType(x))
                            .ThenBy(x => x.Name, Helpers.NaturalStringComparer.Instance);
                    break;

                default:
                    sorted = column.Children;
                    break;
            }

                var sortedList = sorted.ToList();

                // Update collection
                column.Children.Clear();
                foreach (var item in sortedList)
                {
                    column.Children.Add(item);
                }

                // CRITICAL: Restore selection AFTER sorting
                // This prevents focus from jumping to last tab
                if (savedSelection != null)
                {
                    column.SelectedChild = savedSelection;
                }

                Helpers.DebugLogger.Log($"[SortCurrentColumn] Sorted by {sortBy} ({(isAscending ? "Ascending" : "Descending")}), {sortedList.Count} items, selection restored: {savedSelection?.Name ?? "null"}");
            }
            finally
            {
                // 🔓 Always clear sorting flag, even if exception occurs
                column.IsSorting = false;
            }
        }

        private DateTime GetDateModified(FileSystemViewModel vm)
        {
            if (vm.Model is FileItem fileItem)
                return fileItem.DateModified;
            if (vm.Model is FolderItem folderItem)
                return folderItem.DateModified;
            return DateTime.MinValue;
        }

        private long GetSize(FileSystemViewModel vm)
        {
            if (vm.Model is FileItem fileItem)
                return fileItem.Size;
            return 0; // Folders have no size
        }

        private string GetFileType(FileSystemViewModel vm)
        {
            if (vm is FolderViewModel)
                return "폴더";
            return System.IO.Path.GetExtension(vm.Name);
        }

        // View mode handlers
        private void OnViewModeMillerColumns(object sender, RoutedEventArgs e)
        {
            ViewModel.SwitchViewMode(Models.ViewMode.MillerColumns);
        }

        private void OnViewModeDetails(object sender, RoutedEventArgs e)
        {
            ViewModel.SwitchViewMode(Models.ViewMode.Details);
        }

        private void OnViewModeIconExtraLarge(object sender, RoutedEventArgs e)
        {
            ViewModel.SwitchViewMode(Models.ViewMode.IconExtraLarge);
            GetActiveIconView()?.UpdateIconSize(Models.ViewMode.IconExtraLarge);
        }

        private void OnViewModeIconLarge(object sender, RoutedEventArgs e)
        {
            ViewModel.SwitchViewMode(Models.ViewMode.IconLarge);
            GetActiveIconView()?.UpdateIconSize(Models.ViewMode.IconLarge);
        }

        private void OnViewModeIconMedium(object sender, RoutedEventArgs e)
        {
            ViewModel.SwitchViewMode(Models.ViewMode.IconMedium);
            GetActiveIconView()?.UpdateIconSize(Models.ViewMode.IconMedium);
        }

        private void OnViewModeIconSmall(object sender, RoutedEventArgs e)
        {
            ViewModel.SwitchViewMode(Models.ViewMode.IconSmall);
            GetActiveIconView()?.UpdateIconSize(Models.ViewMode.IconSmall);
        }

        private Views.IconModeView? GetActiveIconView()
        {
            if (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                return IconViewRight;
            return IconView;
        }

        // Visibility helper functions for x:Bind
        public Visibility IsMillerColumnsMode(Models.ViewMode mode)
            => mode == Models.ViewMode.MillerColumns ? Visibility.Visible : Visibility.Collapsed;

        public Visibility IsDetailsMode(Models.ViewMode mode)
            => mode == Models.ViewMode.Details ? Visibility.Visible : Visibility.Collapsed;

        public Visibility IsIconMode(Models.ViewMode mode)
            => Helpers.ViewModeExtensions.IsIconMode(mode) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility IsHomeMode(Models.ViewMode mode)
            => mode == Models.ViewMode.Home ? Visibility.Visible : Visibility.Collapsed;

        // Sort menu opening - update checkmarks and icons
        private void OnSortMenuOpening(object sender, object e)
        {
            // Clear all checkmarks
            SortByNameItem.KeyboardAcceleratorTextOverride = string.Empty;
            SortByDateItem.KeyboardAcceleratorTextOverride = string.Empty;
            SortBySizeItem.KeyboardAcceleratorTextOverride = string.Empty;
            SortByTypeItem.KeyboardAcceleratorTextOverride = string.Empty;
            SortAscendingItem.KeyboardAcceleratorTextOverride = string.Empty;
            SortDescendingItem.KeyboardAcceleratorTextOverride = string.Empty;

            // Set checkmark on active sort field
            switch (_currentSortField)
            {
                case "Name":
                    SortByNameItem.KeyboardAcceleratorTextOverride = "✓";
                    break;
                case "Date":
                    SortByDateItem.KeyboardAcceleratorTextOverride = "✓";
                    break;
                case "Size":
                    SortBySizeItem.KeyboardAcceleratorTextOverride = "✓";
                    break;
                case "Type":
                    SortByTypeItem.KeyboardAcceleratorTextOverride = "✓";
                    break;
            }

            // Set checkmark on active sort direction
            if (_currentSortAscending)
                SortAscendingItem.KeyboardAcceleratorTextOverride = "✓";
            else
                SortDescendingItem.KeyboardAcceleratorTextOverride = "✓";

            // Update button icons
            UpdateSortButtonIcons();
        }

        private void UpdateSortButtonIcons()
        {
            // Update sort field icon
            SortIcon.Glyph = _currentSortField switch
            {
                "Name" => "\uE8C1", // Name icon
                "Date" => "\uE787", // Calendar icon
                "Size" => "\uE7C6", // Size/ruler icon
                "Type" => "\uE7C3", // Tag/category icon
                _ => "\uE8CB" // Default sort icon
            };

            // Update sort direction icon
            SortDirectionIcon.Glyph = _currentSortAscending ? "\uE74A" : "\uE74B"; // Up/Down arrow
        }

        // =================================================================
        //  Split View — Pane Helpers & Handlers
        // =================================================================

        /// <summary>
        /// Returns the ItemsControl for the currently active pane.
        /// </summary>
        private ItemsControl GetActiveMillerColumnsControl()
        {
            if (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                return MillerColumnsControlRight;
            return MillerColumnsControl;
        }

        /// <summary>
        /// Returns the ScrollViewer for the currently active pane.
        /// </summary>
        private ScrollViewer GetActiveMillerScrollViewer()
        {
            if (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                return MillerScrollViewerRight;
            return MillerScrollViewer;
        }

        // --- x:Bind visibility/brush helpers ---

        public Visibility IsSplitVisible(bool isSplitViewEnabled)
            => isSplitViewEnabled ? Visibility.Visible : Visibility.Collapsed;

        public Visibility IsNotSplitVisible(bool isSplitViewEnabled)
            => isSplitViewEnabled ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>
        /// Single mode toolbar/address bar: visible when NOT split AND NOT Home mode
        /// </summary>
        public Visibility IsSingleNonHomeVisible(bool isSplitViewEnabled, Models.ViewMode mode)
            => (!isSplitViewEnabled && mode != Models.ViewMode.Home) ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Left pane header (split mode): visible when split enabled (including Home mode for accent bar)
        /// </summary>
        public Visibility IsLeftPaneHeaderVisible(bool isSplitViewEnabled, Models.ViewMode mode)
            => isSplitViewEnabled ? Visibility.Visible : Visibility.Collapsed;

        public SolidColorBrush LeftPaneAccentBrush(ActivePane activePane)
        {
            return activePane == ActivePane.Left
                ? (SolidColorBrush)(Application.Current.Resources["SpanAccentBrush"])
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        public SolidColorBrush RightPaneAccentBrush(ActivePane activePane)
        {
            return activePane == ActivePane.Right
                ? (SolidColorBrush)(Application.Current.Resources["SpanAccentBrush"])
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        // --- Focus tracking ---

        private void OnLeftPaneGotFocus(object sender, RoutedEventArgs e)
        {
            if (ViewModel.ActivePane != ActivePane.Left)
            {
                ViewModel.ActivePane = ActivePane.Left;
            }
        }

        private void OnRightPaneGotFocus(object sender, RoutedEventArgs e)
        {
            if (ViewModel.ActivePane != ActivePane.Right)
            {
                ViewModel.ActivePane = ActivePane.Right;
            }
        }

        /// <summary>
        /// 빈 공간 클릭 시에도 ActivePane을 전환하고 포커스를 이동.
        /// GotFocus는 포커스 가능 요소가 hit될 때만 발생하므로, 빈 공간에서는
        /// PointerPressed로 보완해야 함.
        /// </summary>
        private void OnLeftPanePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (ViewModel.ActivePane != ActivePane.Left)
            {
                ViewModel.ActivePane = ActivePane.Left;
                FocusActivePane();
            }
        }

        private void OnRightPanePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (ViewModel.ActivePane != ActivePane.Right)
            {
                ViewModel.ActivePane = ActivePane.Right;
                FocusActivePane();
            }
        }

        private void OnLeftPaneHeaderTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ViewModel.ActivePane = ActivePane.Left;
        }

        private void OnRightPaneHeaderTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ViewModel.ActivePane = ActivePane.Right;
        }

        // --- Pane-specific flyout opening handlers (set ActivePane before menu item click) ---

        private void OnLeftPaneSortMenuOpening(object sender, object e)
        {
            ViewModel.ActivePane = ActivePane.Left;
        }

        private void OnRightPaneSortMenuOpening(object sender, object e)
        {
            ViewModel.ActivePane = ActivePane.Right;
        }

        private void OnLeftPaneViewModeMenuOpening(object sender, object e)
        {
            ViewModel.ActivePane = ActivePane.Left;
        }

        private void OnRightPaneViewModeMenuOpening(object sender, object e)
        {
            ViewModel.ActivePane = ActivePane.Right;
        }

        private void OnPanePreviewToggle(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tag)
            {
                ViewModel.ActivePane = tag == "Right" ? ActivePane.Right : ActivePane.Left;
            }
            TogglePreviewPanel();
        }

        /// <summary>
        /// Breadcrumb click in per-pane path header.
        /// Detects which pane the button belongs to and navigates accordingly.
        /// </summary>
        private void OnPaneBreadcrumbClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string fullPath)
            {
                // Detect pane from visual tree
                if (IsDescendant(RightPaneContainer, btn))
                {
                    ViewModel.ActivePane = ActivePane.Right;
                    ViewModel.RightExplorer.NavigateToPath(fullPath);
                }
                else
                {
                    ViewModel.ActivePane = ActivePane.Left;
                    ViewModel.LeftExplorer.NavigateToPath(fullPath);
                }
            }
        }

        // --- Split View Toggle ---

        private void OnSplitViewToggleClick(object sender, RoutedEventArgs e)
        {
            ToggleSplitView();
        }

        private void ToggleSplitView()
        {
            ViewModel.IsSplitViewEnabled = !ViewModel.IsSplitViewEnabled;

            if (ViewModel.IsSplitViewEnabled)
            {
                SplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
                RightPaneCol.Width = new GridLength(1, GridUnitType.Star);

                // Initialize right pane with a real filesystem path
                if (ViewModel.RightExplorer.Columns.Count == 0 ||
                    ViewModel.RightExplorer.CurrentPath == "PC")
                {
                    NavigateRightPaneToRealPath();
                }

                // Set active pane to right and focus it after UI has updated
                ViewModel.ActivePane = ActivePane.Right;
                FocusActivePane();

                Helpers.DebugLogger.Log("[MainWindow] Split View enabled");
            }
            else
            {
                SplitterCol.Width = new GridLength(0);
                RightPaneCol.Width = new GridLength(0);

                // Reset active pane to left and focus it
                ViewModel.ActivePane = ActivePane.Left;
                FocusActivePane();

                Helpers.DebugLogger.Log("[MainWindow] Split View disabled");
            }
        }

        /// <summary>
        /// Navigate the right pane to a real filesystem path (saved path, first drive, or user profile).
        /// </summary>
        private void NavigateRightPaneToRealPath()
        {
            var path = ViewModel.GetRightPaneInitialPath();
            var name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(name))
                name = path; // Drive root like "C:\"

            ViewModel.RightExplorer.NavigateTo(new FolderItem { Name = name, Path = path });
            Helpers.DebugLogger.Log($"[MainWindow] Right pane navigated to: {path}");
        }

        /// <summary>
        /// Per-pane navigate up button click.
        /// </summary>
        private void OnPaneNavigateUpClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                var explorer = (btn.Tag as string) == "Right"
                    ? ViewModel.RightExplorer : ViewModel.LeftExplorer;
                explorer.NavigateUp();
            }
        }

        /// <summary>
        /// Per-pane copy path button click.
        /// </summary>
        private void OnPaneCopyPathClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                var explorer = (btn.Tag as string) == "Right"
                    ? ViewModel.RightExplorer : ViewModel.LeftExplorer;
                var path = explorer.CurrentPath;
                if (!string.IsNullOrEmpty(path))
                {
                    var dataPackage = new DataPackage();
                    dataPackage.SetText(path);
                    Clipboard.SetContent(dataPackage);
                }
            }
        }

        /// <summary>
        /// Focus the active pane's content (used after pane switch or split toggle).
        /// Handles all view modes and retries if columns haven't loaded yet.
        /// </summary>
        private void FocusActivePane(int retryCount = 0)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed || ViewModel == null) return;

                var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                    ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

                switch (viewMode)
                {
                    case Models.ViewMode.MillerColumns:
                        var columns = ViewModel.ActiveExplorer.Columns;
                        if (columns.Count > 0)
                        {
                            FocusColumnAsync(columns.Count - 1);
                        }
                        else if (retryCount < 3)
                        {
                            // Columns may still be loading after NavigateRightPaneToRealPath
                            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                                () => FocusActivePane(retryCount + 1));
                        }
                        break;

                    case Models.ViewMode.Details:
                        if (ViewModel.ActivePane == ActivePane.Right && ViewModel.IsSplitViewEnabled)
                            DetailsViewRight?.FocusListView();
                        else
                            DetailsView?.FocusListView();
                        break;

                    case Models.ViewMode.IconSmall:
                    case Models.ViewMode.IconMedium:
                    case Models.ViewMode.IconLarge:
                    case Models.ViewMode.IconExtraLarge:
                        if (ViewModel.ActivePane == ActivePane.Right && ViewModel.IsSplitViewEnabled)
                            IconViewRight?.FocusGridView();
                        else
                            IconView?.FocusGridView();
                        break;
                }
            });
        }

        // =================================================================
        //  Preview Panel
        // =================================================================

        /// <summary>
        /// x:Bind visibility helper for preview panel.
        /// </summary>
        public Visibility PreviewVisible(bool isPreviewEnabled)
            => isPreviewEnabled ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Initialize preview panels with ViewModels from DI.
        /// </summary>
        private void InitializePreviewPanels()
        {
            var previewService = App.Current.Services.GetRequiredService<PreviewService>();

            var leftVm = new PreviewPanelViewModel(previewService);
            LeftPreviewPanel.Initialize(leftVm);

            var rightVm = new PreviewPanelViewModel(previewService);
            RightPreviewPanel.Initialize(rightVm);

            // Subscribe to LeftExplorer column changes for preview updates
            ViewModel.LeftExplorer.Columns.CollectionChanged += OnLeftColumnsChangedForPreview;
            ViewModel.RightExplorer.Columns.CollectionChanged += OnRightColumnsChangedForPreview;

            // Subscribe to ViewModel property changes for preview state
            ViewModel.PropertyChanged += OnViewModelPropertyChangedForPreview;
        }

        /// <summary>
        /// When columns change, subscribe to the last column's SelectedChild for preview.
        /// </summary>
        private void OnLeftColumnsChangedForPreview(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isClosed || !ViewModel.IsLeftPreviewEnabled) return;
            SubscribePreviewToLastColumn(isLeft: true);
        }

        private void OnRightColumnsChangedForPreview(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isClosed || !ViewModel.IsRightPreviewEnabled) return;
            SubscribePreviewToLastColumn(isLeft: false);
        }

        /// <summary>
        /// Subscribe to the last column's SelectedChild property changes to auto-update preview.
        /// </summary>
        private void SubscribePreviewToLastColumn(bool isLeft)
        {
            var explorer = isLeft ? ViewModel.LeftExplorer : ViewModel.RightExplorer;
            var columns = explorer.Columns;

            UnsubscribePreviewSelection(isLeft);

            if (columns.Count == 0) return;

            var lastColumn = columns[columns.Count - 1];
            lastColumn.PropertyChanged += isLeft ? OnLeftColumnSelectionForPreview : OnRightColumnSelectionForPreview;

            if (isLeft) _leftPreviewSubscribedColumn = lastColumn;
            else _rightPreviewSubscribedColumn = lastColumn;

            // Immediately update preview with current selection
            var previewPanel = isLeft ? LeftPreviewPanel : RightPreviewPanel;
            previewPanel.UpdatePreview(lastColumn.SelectedChild);
        }

        private void UnsubscribePreviewSelection(bool isLeft)
        {
            if (isLeft && _leftPreviewSubscribedColumn != null)
            {
                _leftPreviewSubscribedColumn.PropertyChanged -= OnLeftColumnSelectionForPreview;
                _leftPreviewSubscribedColumn = null;
            }
            else if (!isLeft && _rightPreviewSubscribedColumn != null)
            {
                _rightPreviewSubscribedColumn.PropertyChanged -= OnRightColumnSelectionForPreview;
                _rightPreviewSubscribedColumn = null;
            }
        }

        private void OnLeftColumnSelectionForPreview(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FolderViewModel.SelectedChild)) return;
            if (_isClosed || !ViewModel.IsLeftPreviewEnabled) return;
            if (sender is FolderViewModel folder)
                LeftPreviewPanel.UpdatePreview(folder.SelectedChild);
        }

        private void OnRightColumnSelectionForPreview(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FolderViewModel.SelectedChild)) return;
            if (_isClosed || !ViewModel.IsRightPreviewEnabled) return;
            if (sender is FolderViewModel folder)
                RightPreviewPanel.UpdatePreview(folder.SelectedChild);
        }

        /// <summary>
        /// Update preview when selection changes in Details/Icon mode (via Miller column selection handler).
        /// </summary>
        private void UpdatePreviewForSelection(FileSystemViewModel? selectedItem)
        {
            if (_isClosed) return;

            if (ViewModel.ActivePane == ActivePane.Left && ViewModel.IsLeftPreviewEnabled)
                LeftPreviewPanel.UpdatePreview(selectedItem);
            else if (ViewModel.ActivePane == ActivePane.Right && ViewModel.IsRightPreviewEnabled)
                RightPreviewPanel.UpdatePreview(selectedItem);
        }

        /// <summary>
        /// React to preview enable/disable changes to wire/unwire subscriptions.
        /// </summary>
        private void OnViewModelPropertyChangedForPreview(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsLeftPreviewEnabled))
            {
                if (ViewModel.IsLeftPreviewEnabled)
                    SubscribePreviewToLastColumn(isLeft: true);
                else
                {
                    UnsubscribePreviewSelection(isLeft: true);
                    LeftPreviewPanel.UpdatePreview(null);
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.IsRightPreviewEnabled))
            {
                if (ViewModel.IsRightPreviewEnabled)
                    SubscribePreviewToLastColumn(isLeft: false);
                else
                {
                    UnsubscribePreviewSelection(isLeft: false);
                    RightPreviewPanel.UpdatePreview(null);
                }
            }
        }

        private void OnPreviewToggleClick(object sender, RoutedEventArgs e)
        {
            TogglePreviewPanel();
        }

        private void TogglePreviewPanel()
        {
            ViewModel.TogglePreview();

            // Update column widths for the active pane
            if (ViewModel.ActivePane == ActivePane.Left)
            {
                if (ViewModel.IsLeftPreviewEnabled)
                {
                    LeftPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
                    LeftPreviewCol.Width = new GridLength(280, GridUnitType.Pixel);
                }
                else
                {
                    LeftPreviewSplitterCol.Width = new GridLength(0);
                    LeftPreviewCol.Width = new GridLength(0);
                    LeftPreviewPanel.StopMedia();
                }
            }
            else
            {
                if (ViewModel.IsRightPreviewEnabled)
                {
                    RightPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
                    RightPreviewCol.Width = new GridLength(280, GridUnitType.Pixel);
                }
                else
                {
                    RightPreviewSplitterCol.Width = new GridLength(0);
                    RightPreviewCol.Width = new GridLength(0);
                    RightPreviewPanel.StopMedia();
                }
            }

            Helpers.DebugLogger.Log($"[MainWindow] Preview toggled: Left={ViewModel.IsLeftPreviewEnabled}, Right={ViewModel.IsRightPreviewEnabled}");
        }

        /// <summary>
        /// Restore preview panel widths from saved settings on Loaded.
        /// </summary>
        private void RestorePreviewState()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;

                if (ViewModel.IsLeftPreviewEnabled)
                {
                    LeftPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
                    double leftW = 280;
                    if (settings.Values.TryGetValue("LeftPreviewWidth", out var lw))
                        leftW = Math.Max(200, (double)lw);
                    LeftPreviewCol.Width = new GridLength(leftW, GridUnitType.Pixel);
                    SubscribePreviewToLastColumn(isLeft: true);
                }

                if (ViewModel.IsRightPreviewEnabled)
                {
                    RightPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
                    double rightW = 280;
                    if (settings.Values.TryGetValue("RightPreviewWidth", out var rw))
                        rightW = Math.Max(200, (double)rw);
                    RightPreviewCol.Width = new GridLength(rightW, GridUnitType.Pixel);
                    SubscribePreviewToLastColumn(isLeft: false);
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainWindow] RestorePreviewState error: {ex.Message}");
            }
        }

        // =================================================================
        //  IContextMenuHost Implementation
        // =================================================================

        void Services.IContextMenuHost.PerformCut(string path)
        {
            _clipboardPaths.Clear();
            _clipboardPaths.Add(path);
            _isCutOperation = true;

            var dataPackage = new DataPackage();
            dataPackage.SetText(path);
            Clipboard.SetContent(dataPackage);
            Helpers.DebugLogger.Log($"[ContextMenu] Cut: {path}");
        }

        void Services.IContextMenuHost.PerformCopy(string path)
        {
            _clipboardPaths.Clear();
            _clipboardPaths.Add(path);
            _isCutOperation = false;

            var dataPackage = new DataPackage();
            dataPackage.SetText(path);
            Clipboard.SetContent(dataPackage);
            Helpers.DebugLogger.Log($"[ContextMenu] Copy: {path}");
        }

        async void Services.IContextMenuHost.PerformPaste(string targetFolderPath)
        {
            if (_clipboardPaths.Count == 0) return;

            foreach (var srcPath in _clipboardPaths)
            {
                try
                {
                    string fileName = System.IO.Path.GetFileName(srcPath);
                    string destPath = System.IO.Path.Combine(targetFolderPath, fileName);

                    int copy = 1;
                    while (System.IO.File.Exists(destPath) || System.IO.Directory.Exists(destPath))
                    {
                        string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(srcPath);
                        string ext = System.IO.Path.GetExtension(srcPath);
                        destPath = System.IO.Path.Combine(targetFolderPath, $"{nameNoExt} ({copy}){ext}");
                        copy++;
                    }

                    await System.Threading.Tasks.Task.Run(() =>
                    {
                        if (System.IO.File.Exists(srcPath))
                        {
                            if (_isCutOperation)
                                System.IO.File.Move(srcPath, destPath);
                            else
                                System.IO.File.Copy(srcPath, destPath);
                        }
                        else if (System.IO.Directory.Exists(srcPath))
                        {
                            if (_isCutOperation)
                                System.IO.Directory.Move(srcPath, destPath);
                            else
                                CopyDirectory(srcPath, destPath);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[ContextMenu] Paste error: {ex.Message}");
                }
            }

            if (_isCutOperation) _clipboardPaths.Clear();

            // Refresh the target folder if it's in the current columns
            var columns = ViewModel.ActiveExplorer.Columns;
            var targetColumn = columns.FirstOrDefault(c =>
                c.Path.Equals(targetFolderPath, StringComparison.OrdinalIgnoreCase));
            if (targetColumn != null)
                await targetColumn.ReloadAsync();
        }

        async void Services.IContextMenuHost.PerformDelete(string path, string itemName)
        {
            var dialog = new ContentDialog
            {
                Title = _loc.Get("DeleteConfirmTitle"),
                Content = string.Format(_loc.Get("DeleteConfirmContent"), itemName),
                PrimaryButtonText = _loc.Get("Delete"),
                CloseButtonText = _loc.Get("Cancel"),
                XamlRoot = this.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var operation = new Services.FileOperations.DeleteFileOperation(
                new List<string> { path }, permanent: false);

            int activeIndex = GetCurrentColumnIndex();
            if (activeIndex >= 0)
            {
                await ViewModel.ExecuteFileOperationAsync(operation, activeIndex);
                ViewModel.ActiveExplorer.CleanupColumnsFrom(activeIndex + 1);
                FocusColumnAsync(activeIndex);
            }
        }

        void Services.IContextMenuHost.PerformRename(FileSystemViewModel item)
        {
            item.BeginRename();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed) return;
                int activeIndex = GetCurrentColumnIndex();
                if (activeIndex >= 0)
                    FocusRenameTextBox(activeIndex);
            });
        }

        void Services.IContextMenuHost.PerformOpen(FileSystemViewModel item)
        {
            if (item is FolderViewModel folder)
            {
                ViewModel.ActiveExplorer.NavigateIntoFolder(folder);
            }
            else if (item is FileViewModel file)
            {
                try
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(file.Path));
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[ContextMenu] Open file error: {ex.Message}");
                }
            }
        }

        void Services.IContextMenuHost.PerformOpenDrive(DriveItem drive)
        {
            ViewModel.OpenDrive(drive);
            FocusColumnAsync(0);
        }

        void Services.IContextMenuHost.PerformOpenFavorite(FavoriteItem fav)
        {
            ViewModel.NavigateToFavorite(fav);
            FocusColumnAsync(0);
        }

        async void Services.IContextMenuHost.PerformNewFolder(string parentFolderPath)
        {
            string baseName = _loc.Get("NewFolderBaseName");
            string newPath = System.IO.Path.Combine(parentFolderPath, baseName);

            int count = 1;
            while (System.IO.Directory.Exists(newPath))
            {
                newPath = System.IO.Path.Combine(parentFolderPath, $"{baseName} ({count})");
                count++;
            }

            try
            {
                System.IO.Directory.CreateDirectory(newPath);

                // Find and refresh the column for this parent
                var columns = ViewModel.ActiveExplorer.Columns;
                var parentColumn = columns.FirstOrDefault(c =>
                    c.Path.Equals(parentFolderPath, StringComparison.OrdinalIgnoreCase));
                if (parentColumn != null)
                {
                    await parentColumn.ReloadAsync();
                    var newFolder = parentColumn.Children.FirstOrDefault(c =>
                        c.Path.Equals(newPath, StringComparison.OrdinalIgnoreCase));
                    if (newFolder != null)
                    {
                        parentColumn.SelectedChild = newFolder;
                        newFolder.BeginRename();
                        await System.Threading.Tasks.Task.Delay(100);
                        int colIndex = columns.IndexOf(parentColumn);
                        if (colIndex >= 0)
                            FocusRenameTextBox(colIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ContextMenu] NewFolder error: {ex.Message}");
            }
        }

        void Services.IContextMenuHost.AddToFavorites(string path)
        {
            ViewModel.AddToFavorites(path);
        }

        void Services.IContextMenuHost.RemoveFromFavorites(string path)
        {
            ViewModel.RemoveFromFavorites(path);
        }

        bool Services.IContextMenuHost.IsFavorite(string path)
        {
            return ViewModel.IsFavorite(path);
        }

        void Services.IContextMenuHost.SwitchViewMode(ViewMode mode)
        {
            ViewModel.SwitchViewMode(mode);
            if (Helpers.ViewModeExtensions.IsIconMode(mode))
                GetActiveIconView()?.UpdateIconSize(mode);
        }

        void Services.IContextMenuHost.ApplySort(string field)
        {
            _currentSortField = field;
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        void Services.IContextMenuHost.ApplySortDirection(bool ascending)
        {
            _currentSortAscending = ascending;
            SortCurrentColumn(_currentSortField, _currentSortAscending);
        }

        // =================================================================
        //  Help / Settings / Log
        // =================================================================

        private Views.HelpFlyoutContent? _helpFlyout;
        private bool _isHelpOpen = false;

        private void OnHelpClick(object sender, RoutedEventArgs e)
        {
            if (_isHelpOpen)
            {
                HelpButton.Flyout?.Hide();
                _isHelpOpen = false;
                return;
            }

            if (HelpButton.Flyout == null)
            {
                _helpFlyout = new Views.HelpFlyoutContent();
                var flyout = new Flyout
                {
                    Content = _helpFlyout,
                    Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight
                };
                flyout.Closed += (s, args) => _isHelpOpen = false;
                HelpButton.Flyout = flyout;
            }

            HelpButton.Flyout.ShowAt(HelpButton);
            _isHelpOpen = true;
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var dialog = new Views.SettingsDialog
            {
                XamlRoot = this.Content.XamlRoot
            };
            _ = dialog.ShowAsync();
        }

        private Views.LogFlyoutContent? _logFlyout;
        private bool _isLogOpen = false;

        private void OnLogClick(object sender, RoutedEventArgs e)
        {
            if (_isLogOpen)
            {
                LogButton.Flyout?.Hide();
                _isLogOpen = false;
                return;
            }

            var logService = App.Current.Services.GetRequiredService<Services.ActionLogService>();
            if (LogButton.Flyout == null)
            {
                _logFlyout = new Views.LogFlyoutContent(logService);
                var flyout = new Flyout
                {
                    Content = _logFlyout,
                    Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight
                };
                flyout.Closed += (s, args) => _isLogOpen = false;
                flyout.Opening += (s, args) => _logFlyout.Refresh();
                LogButton.Flyout = flyout;
            }
            else
            {
                _logFlyout?.Refresh();
            }

            LogButton.Flyout.ShowAt(LogButton);
            _isLogOpen = true;
        }

    }
}
