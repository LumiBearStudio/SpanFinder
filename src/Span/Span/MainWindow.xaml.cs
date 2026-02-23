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
using System.Threading;
using Microsoft.UI.Input;
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

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

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
        private bool _forceClose = false;

        // Miller Columns checkbox mode tracking
        private ListViewSelectionMode _millerSelectionMode = ListViewSelectionMode.Extended;
        private Thickness _densityPadding = new(12, 2, 12, 2); // comfortable default

        // FileSystemWatcher 서비스 참조
        private FileSystemWatcherService? _watcherService;

        // H1: FocusActiveView 중복 호출 제거 — UpdateViewModeVisibility 내에서 true로 설정
        private bool _suppressFocusOnViewModeChange = false;

        // H2: 동일 ViewMode 탭 전환 시 NotifyViewModeChanged 스킵
        private ViewMode _previousViewMode = ViewMode.MillerColumns;

        // ── Per-Tab Miller Panels (Show/Hide pattern for instant tab switching) ──
        // 각 탭마다 별도 ScrollViewer+ItemsControl 쌍 유지 — Visibility 토글로 즉시 전환
        private readonly Dictionary<string, (ScrollViewer scroller, ItemsControl items)> _tabMillerPanels = new();
        private string? _activeMillerTabId;

        // ── Per-Tab Details/Icon/List Panels (Show/Hide pattern — Miller와 동일 패턴) ──
        private readonly Dictionary<string, Views.DetailsModeView> _tabDetailsPanels = new();
        private readonly Dictionary<string, Views.IconModeView> _tabIconPanels = new();
        private readonly Dictionary<string, Views.ListModeView> _tabListPanels = new();
        private string? _activeDetailsTabId;
        private string? _activeIconTabId;
        private string? _activeListTabId;

        // Clipboard
        private readonly List<string> _clipboardPaths = new();
        private bool _isCutOperation = false;

        // Rename 완료 직후 Enter가 파일 실행으로 이어지는 것을 방지
        private bool _justFinishedRename = false;

        // Selection synchronization guard (Phase 1)
        private bool _isSyncingSelection = false;

        // Rubber-band (marquee) selection helpers per column Grid
        private readonly Dictionary<Grid, Helpers.RubberBandSelectionHelper> _rubberBandHelpers = new();

        // Preview panel selection subscriptions
        private FolderViewModel? _leftPreviewSubscribedColumn;
        private FolderViewModel? _rightPreviewSubscribedColumn;

        // Inline preview column (Miller Columns mode)
        private CancellationTokenSource? _inlinePreviewCts;
        private PreviewService? _inlinePreviewService;

        // Sort state
        private string _currentSortField = "Name"; // Name, Date, Size, Type
        private bool _currentSortAscending = true;

        // Tab tear-off drag state
        private bool _isTabDragging;
        private Windows.Foundation.Point _tabDragStartPoint;
        private Models.TabItem? _draggingTab;
        private const double TAB_DRAG_THRESHOLD = 8;

        // Pending tear-off tab state (set before Activate, consumed in Loaded)
        private Models.TabStateDto? _pendingTearOff;
        // True if this window was created from a tear-off (skip session save on close)
        private bool _isTearOffWindow;

        private const double ColumnWidth = 220;

        // Column resize state
        private bool _isResizingColumn = false;
        private Grid? _resizingColumnGrid = null;

        // F2 rename selection cycling: 0=name only, 1=all, 2=extension only
        private int _renameSelectionCycle = 0;
        private string? _renameTargetPath = null;
        private double _resizeStartX;
        private double _resizeStartWidth;

        // Spring-loaded folders: auto-open folder after drag hover delay
        private DispatcherTimer? _springLoadTimer;
        private FolderViewModel? _springLoadTarget;
        private Grid? _springLoadGrid;
        private const int SPRING_LOAD_DELAY_MS = 700;

        public MainWindow()
        {
            this.InitializeComponent();
            ViewModel = App.Current.Services.GetRequiredService<MainViewModel>();
            _contextMenuService = App.Current.Services.GetRequiredService<Services.ContextMenuService>();
            _loc = App.Current.Services.GetRequiredService<Services.LocalizationService>();
            _settings = App.Current.Services.GetRequiredService<Services.SettingsService>();

            // Wire up file operation progress panel
            var fileOpManager = App.Current.Services.GetRequiredService<Services.FileOperationManager>();
            FileOpProgressControl.SetOperationManager(fileOpManager);

            // Mica
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

            // Minimize to taskbar on close (instead of quitting) when MinimizeToTray enabled
            // Tear-off windows close normally (no tray minimization)
            // If already minimized (e.g. taskbar right-click → Close), allow actual close
            this.AppWindow.Closing += (s, e) =>
            {
                if (_settings.MinimizeToTray && !_forceClose && !_isTearOffWindow && !IsIconic(_hwnd))
                {
                    e.Cancel = true;
                    ShowWindow(_hwnd, 6); // SW_MINIMIZE
                }
            };

            // TitleBar
            ExtendsContentIntoTitleBar = true;
            // SetTitleBar → 전체 타이틀바를 드래그 영역 + 캡션 버튼 자동 관리
            // Passthrough 영역은 Loaded 후 SetRegionRects로 별도 설정 (탭 영역만)
            SetTitleBar(AppTitleBar);

            // Auto-scroll on column change (both panes)
            _subscribedLeftExplorer = ViewModel.Explorer;
            ViewModel.Explorer.Columns.CollectionChanged += OnColumnsChanged;
            ViewModel.RightExplorer.Columns.CollectionChanged += OnRightColumnsChanged;

            // ── Per-Tab Miller Panel 초기화 ──
            // XAML에서 ItemsSource가 제거되었으므로 코드에서 설정
            MillerColumnsControl.ItemsSource = ViewModel.Explorer.Columns;
            var firstTabId = ViewModel.Tabs.Count > 0 ? ViewModel.Tabs[0].Id : "_default";
            _tabMillerPanels[firstTabId] = (MillerScrollViewer, MillerColumnsControl);
            _activeMillerTabId = firstTabId;

            // ── Per-Tab Details/Icon/List Panel 초기화 ──
            _tabDetailsPanels[firstTabId] = DetailsView;
            _tabIconPanels[firstTabId] = IconView;
            _tabListPanels[firstTabId] = ListView;
            _activeDetailsTabId = firstTabId;
            _activeIconTabId = firstTabId;
            _activeListTabId = firstTabId;

            // Focus management on ViewMode change
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Set ViewModel for Details, List and Icon views (left pane)
            DetailsView.ViewModel = ViewModel.Explorer;
            ListView.ViewModel = ViewModel.Explorer;
            IconView.ViewModel = ViewModel.Explorer;
            HomeView.MainViewModel = ViewModel;
            SettingsView.BackRequested += (s, e) => CloseCurrentSettingsTab();

            // Breadcrumb ItemsSource — x:Bind 제거 후 code-behind에서 직접 설정
            AddressBreadcrumbBar.ItemsSource = ViewModel.Explorer.PathSegments;
            LeftPaneBreadcrumbRepeater.ItemsSource = ViewModel.Explorer.PathSegments;

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
            ListView.ContextMenuService = _contextMenuService;
            ListView.ContextMenuHost = this;
            ListView.OwnerHwnd = _hwnd;
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

            // ★ Mouse Back/Forward buttons (XButton1=Back, XButton2=Forward)
            this.Content.AddHandler(
                UIElement.PointerPressedEvent,
                new PointerEventHandler(OnGlobalPointerPressed),
                true
            );

            // ★ Ctrl+Mouse Wheel view mode cycling (global — works in ALL views)
            this.Content.AddHandler(
                UIElement.PointerWheelChangedEvent,
                new PointerEventHandler(OnGlobalPointerWheelChanged),
                true  // handledEventsToo: catches events even after ScrollViewer/ListView consume them
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
            LocalizeViewModeTooltips();
            _loc.LanguageChanged += LocalizeViewModeTooltips;

            // Restore split view state and preview state from persisted settings
            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.Loaded += (s, e) =>
                {
                    if (_pendingTearOff != null)
                    {
                        // ── Tear-off mode: load single tab from DTO, skip session restore ──
                        _isTearOffWindow = true;
                        var dto = _pendingTearOff;
                        _pendingTearOff = null;

                        ViewModel.LoadSingleTabFromDto(dto);

                        // Re-bind MillerColumnsControl to the new explorer
                        MillerColumnsControl.ItemsSource = ViewModel.Explorer.Columns;
                        var tabId = ViewModel.ActiveTab?.Id ?? "_default";
                        _tabMillerPanels.Clear();
                        _tabMillerPanels[tabId] = (MillerScrollViewer, MillerColumnsControl);
                        _activeMillerTabId = tabId;

                        // Re-bind Details/Icon panels
                        _tabDetailsPanels.Clear();
                        _tabIconPanels.Clear();
                        _tabDetailsPanels[tabId] = DetailsView;
                        _tabIconPanels[tabId] = IconView;
                        _activeDetailsTabId = tabId;
                        _activeIconTabId = tabId;

                        DetailsView.ViewModel = ViewModel.Explorer;
                        IconView.ViewModel = ViewModel.Explorer;
                        AddressBreadcrumbBar.ItemsSource = ViewModel.Explorer.PathSegments;
                        LeftPaneBreadcrumbRepeater.ItemsSource = ViewModel.Explorer.PathSegments;

                        // Resubscribe column changes
                        if (_subscribedLeftExplorer != null)
                            _subscribedLeftExplorer.Columns.CollectionChanged -= OnColumnsChanged;
                        _subscribedLeftExplorer = ViewModel.Explorer;
                        ViewModel.Explorer.Columns.CollectionChanged += OnColumnsChanged;

                        _previousViewMode = ViewModel.CurrentViewMode;
                        SetViewModeVisibility(ViewModel.CurrentViewMode);

                        // Set tab bar as passthrough so pointer events work for tear-off
                        UpdateTitleBarRegions();
                        TabScrollViewer.SizeChanged += (_, __) => UpdateTitleBarRegions();
                        TabBarContent.SizeChanged += (_, __) => UpdateTitleBarRegions();
                        this.SizeChanged += (_, __) => UpdateTitleBarRegions();

                        // Populate favorites tree for tear-off window
                        ApplyFavoritesTreeMode(_settings.ShowFavoritesTree);
                        PopulateFavoritesTree();
                        ViewModel.Favorites.CollectionChanged += OnFavoritesCollectionChanged;

                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                            () => FocusActiveView());
                        return;
                    }

                    // ── Normal startup: restore session tabs ──
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
                    ViewModel.LoadTabsFromSettings();

                    // ── Per-Tab Miller Panels: 세션 복원 후 모든 탭에 대해 패널 생성 ──
                    InitializeTabMillerPanels();

                    // ── Populate Favorites Tree and observe changes ──
                    ApplyFavoritesTreeMode(_settings.ShowFavoritesTree);
                    PopulateFavoritesTree();
                    ViewModel.Favorites.CollectionChanged += OnFavoritesCollectionChanged;

                    // Set tab bar as passthrough so pointer events work for tab tear-off
                    UpdateTitleBarRegions();
                    TabScrollViewer.SizeChanged += (_, __) => UpdateTitleBarRegions();
                    TabBarContent.SizeChanged += (_, __) => UpdateTitleBarRegions();
                    this.SizeChanged += (_, __) => UpdateTitleBarRegions();

                    // ViewMode Visibility 초기화 (x:Bind 제거 후 코드비하인드에서 관리)
                    _previousViewMode = ViewModel.CurrentViewMode;
                    SetViewModeVisibility(ViewModel.CurrentViewMode);

                    // Focus the active view after session restore
                    // NavigateTo is async, so delay to ensure items are loaded
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                        () => FocusActiveView());

                    // Apply ShowCheckboxes to Miller Columns after initial render
                    if (_settings.ShowCheckboxes)
                    {
                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                            () => ApplyMillerCheckboxMode(true));
                    }

                    // Apply MillerClickBehavior on startup
                    if (_settings.MillerClickBehavior == "double")
                    {
                        ViewModel.Explorer.EnableAutoNavigation = false;
                        ViewModel.RightExplorer.EnableAutoNavigation = false;
                    }

                    // FileSystemWatcher 초기화
                    InitializeFileSystemWatcher();
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

                // Save tab state for session restore (skip for tear-off windows)
                if (!_isTearOffWindow)
                {
                    ViewModel.SaveActiveTabState();
                    ViewModel.SaveTabsToSettings();
                }

                // FileSystemWatcher 정리
                _watcherService?.StopAll();

                // Unsubscribe settings
                _settings.SettingChanged -= OnSettingChanged;

                // STEP 1: Suppress ViewModel notifications FIRST (prevents PropertyChanged
                // from reaching UI during teardown — the primary crash cause).
                ViewModel?.Explorer?.Cleanup();       // Left pane
                ViewModel?.RightExplorer?.Cleanup();   // Right pane

                // STEP 2: Unsubscribe MainWindow event handlers BEFORE ViewModel.Cleanup()
                // so collection Clear() notifications don't reach MainWindow handlers.
                if (_subscribedLeftExplorer != null)
                {
                    _subscribedLeftExplorer.Columns.CollectionChanged -= OnColumnsChanged;
                    _subscribedLeftExplorer.Columns.CollectionChanged -= OnLeftColumnsChangedForPreview;
                    _subscribedLeftExplorer = null;
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

                // Per-Tab Miller Panels 정리
                foreach (var kvp in _tabMillerPanels)
                {
                    kvp.Value.items.ItemsSource = null;
                }
                _tabMillerPanels.Clear();

                // Rubber-band selection helpers 정리
                foreach (var kvp in _rubberBandHelpers)
                    try { kvp.Value.Detach(); } catch (Exception ex) { Helpers.DebugLogger.LogCrash("OnClosed.RubberBand.Detach", ex); }
                _rubberBandHelpers.Clear();

                // Unsubscribe preview column change handlers
                // LeftExplorer preview는 _subscribedLeftExplorer에서 이미 해제됨
                if (ViewModel?.RightExplorer != null)
                    ViewModel.RightExplorer.Columns.CollectionChanged -= OnRightColumnsChangedForPreview;

                // STEP 2.5: Cleanup preview panels (stop media, dispose ViewModels)
                try { LeftPreviewPanel?.Cleanup(); } catch { }
                try { RightPreviewPanel?.Cleanup(); } catch { }
                UnsubscribePreviewSelection(isLeft: true);
                UnsubscribePreviewSelection(isLeft: false);

                // Cleanup inline preview column
                try { CleanupInlinePreview(); } catch { }

                // Save preview panel widths
                try
                {
                    double leftW = LeftPreviewCol.Width.Value;
                    double rightW = RightPreviewCol.Width.Value;
                    ViewModel?.SavePreviewWidths(leftW, rightW);
                }
                catch { }

                // STEP 3: Per-tab Details/List/Icon 인스턴스 전체 정리
                foreach (var kvp in _tabDetailsPanels)
                    try { kvp.Value?.Cleanup(); } catch { }
                _tabDetailsPanels.Clear();

                foreach (var kvp in _tabListPanels)
                    try { kvp.Value?.Cleanup(); } catch { }
                _tabListPanels.Clear();

                foreach (var kvp in _tabIconPanels)
                    try { kvp.Value?.Cleanup(); } catch { }
                _tabIconPanels.Clear();

                try { HomeView?.Cleanup(); } catch { }
                try { DetailsViewRight?.Cleanup(); } catch { }
                try { IconViewRight?.Cleanup(); } catch { }

                // Disconnect sidebar bindings
                try
                {
                    FavoritesTreeView.RootNodes.Clear();
                    ViewModel.Favorites.CollectionChanged -= OnFavoritesCollectionChanged;
                }
                catch { /* ignore */ }

                // STEP 4: NOW safe to clear collections — UI bindings disconnected
                ViewModel?.Cleanup();            // Save state, cancel ops, clear collections

                // STEP 5: Stop timer and remove keyboard handlers
                try
                {
                    if (_typeAheadTimer != null)
                    {
                        _typeAheadTimer.Stop();
                        _typeAheadTimer = null;
                    }
                    if (this.Content != null)
                    {
                        this.Content.RemoveHandler(UIElement.KeyDownEvent, (KeyEventHandler)OnGlobalKeyDown);
                        this.Content.RemoveHandler(UIElement.PointerPressedEvent, (PointerEventHandler)OnGlobalPointerPressed);
                        this.Content.RemoveHandler(UIElement.PointerWheelChangedEvent, (PointerEventHandler)OnGlobalPointerWheelChanged);
                    }
                    if (MillerColumnsControl != null)
                    {
                        MillerColumnsControl.RemoveHandler(UIElement.KeyDownEvent, (KeyEventHandler)OnMillerKeyDown);
                    }
                    if (MillerColumnsControlRight != null)
                    {
                        MillerColumnsControlRight.RemoveHandler(UIElement.KeyDownEvent, (KeyEventHandler)OnMillerKeyDown);
                    }
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[MainWindow.OnClosed] STEP 5 error: {ex.Message}");
                }

                // STEP 6: Remove window subclass for device change
                try
                {
                    if (_subclassProc != null)
                    {
                        RemoveWindowSubclass(_hwnd, _subclassProc, IntPtr.Zero);
                    }
                    if (_deviceChangeDebounceTimer != null)
                    {
                        _deviceChangeDebounceTimer.Stop();
                        _deviceChangeDebounceTimer = null;
                    }
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[MainWindow.OnClosed] STEP 6 error: {ex.Message}");
                }

                Helpers.DebugLogger.Log("[MainWindow.OnClosed] Cleanup complete");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow.OnClosed] Error during close: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MainWindow.OnClosed] Stack trace: {ex.StackTrace}");
            }
            finally
            {
                // CRITICAL: Always unregister window to ensure app exit.
                // Previously inside try block — if any cleanup step threw,
                // UnregisterWindow was skipped → Environment.Exit never called → process hung.
                try { App.Current.UnregisterWindow(this); } catch { }
            }
        }

        /// <summary>
        /// Win32 subclass procedure to intercept WM_DEVICECHANGE for USB hotplug detection.
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

        // 커스텀 테마 목록 (Dark 기반 + 리소스 오버라이드)
        private static readonly HashSet<string> _customThemes = new() { "dracula", "tokyonight", "catppuccin", "gruvbox" };

        private void ApplyTheme(string theme)
        {
            bool isCustom = _customThemes.Contains(theme);

            if (this.Content is FrameworkElement root)
            {
                var targetTheme = theme switch
                {
                    "light" => ElementTheme.Light,
                    "dark" => ElementTheme.Dark,
                    _ when isCustom => ElementTheme.Dark, // 커스텀 테마는 Dark 기반
                    _ => ElementTheme.Default
                };

                // 커스텀 테마: 리소스 설정 후 테마 토글로 {ThemeResource} 바인딩 강제 갱신
                if (isCustom)
                {
                    // 1) 먼저 Light로 전환하여 기존 Dark 리소스 해제
                    root.RequestedTheme = ElementTheme.Light;
                    // 2) 커스텀 리소스 오버라이드 적용
                    ApplyCustomThemeOverrides(root, theme);
                    // 3) Dark로 복귀 → 모든 {ThemeResource} 바인딩 재평가
                    root.RequestedTheme = ElementTheme.Dark;
                }
                else
                {
                    // 비커스텀: 오버라이드 제거 후 테마 적용
                    ApplyCustomThemeOverrides(root, theme);
                    root.RequestedTheme = targetTheme;
                }
            }

            // 캡션 버튼 색상
            var titleBar = this.AppWindow.TitleBar;

            if (isCustom)
            {
                var cap = GetCaptionColors(theme);
                titleBar.ButtonForegroundColor = cap.fg;
                titleBar.ButtonHoverForegroundColor = cap.hoverFg;
                titleBar.ButtonHoverBackgroundColor = cap.hoverBg;
                titleBar.ButtonPressedForegroundColor = cap.pressedFg;
                titleBar.ButtonPressedBackgroundColor = cap.pressedBg;
                titleBar.ButtonInactiveForegroundColor = cap.inactiveFg;
            }
            else
            {
                bool isLight = theme == "light" ||
                               (theme == "system" && App.Current.RequestedTheme == ApplicationTheme.Light);

                if (isLight)
                {
                    titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 26, 26, 26);
                    titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(20, 0, 0, 0);
                    titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(40, 0, 0, 0);
                    titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 140, 140, 140);
                }
                else
                {
                    titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(15, 255, 255, 255);
                    titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(20, 255, 255, 255);
                    titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 120, 120, 120);
                }
            }
            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        }

        // 원본 Dark ThemeDictionary 백업 (최초 한 번만)
        private ResourceDictionary? _originalDarkThemeDict;

        private void ApplyCustomThemeOverrides(FrameworkElement root, string theme)
        {
            // 원본 백업 (최초 1회)
            if (_originalDarkThemeDict == null && root.Resources.ThemeDictionaries.ContainsKey("Dark"))
            {
                var orig = (ResourceDictionary)root.Resources.ThemeDictionaries["Dark"];
                _originalDarkThemeDict = new ResourceDictionary();
                foreach (var kvp in orig)
                    _originalDarkThemeDict[kvp.Key] = kvp.Value;
            }

            if (!_customThemes.Contains(theme))
            {
                // 원본 Dark ThemeDictionary 복원
                if (_originalDarkThemeDict != null)
                {
                    var restored = new ResourceDictionary();
                    foreach (var kvp in _originalDarkThemeDict)
                        restored[kvp.Key] = kvp.Value;
                    root.Resources.ThemeDictionaries["Dark"] = restored;
                }
                return;
            }

            var p = GetThemePalette(theme);

            // 원본 Dark dict를 기반으로 커스텀 값 덮어쓰기
            var darkDict = new ResourceDictionary();
            if (_originalDarkThemeDict != null)
            {
                foreach (var kvp in _originalDarkThemeDict)
                    darkDict[kvp.Key] = kvp.Value;
            }

            // Color 리소스
            darkDict["SpanBgMica"]        = p.bgMica;
            darkDict["SpanBgLayer1"]      = p.bgLayer1;
            darkDict["SpanBgLayer2"]      = p.bgLayer2;
            darkDict["SpanBgLayer3"]      = p.bgLayer3;
            darkDict["SpanAccent"]        = p.accent;
            darkDict["SpanAccentHover"]   = p.accentHover;
            darkDict["SpanTextPrimary"]   = p.textPri;
            darkDict["SpanTextSecondary"] = p.textSec;
            darkDict["SpanTextTertiary"]  = p.textTer;
            darkDict["SpanBgSelected"]    = p.bgSel;
            darkDict["SpanBorderSubtle"]  = p.border;

            // Brush 리소스
            darkDict["SpanBgMicaBrush"]        = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgMica);
            darkDict["SpanBgLayer1Brush"]      = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgLayer1);
            darkDict["SpanBgLayer2Brush"]      = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgLayer2);
            darkDict["SpanBgLayer3Brush"]      = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgLayer3);
            darkDict["SpanAccentBrush"]        = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.accent);
            darkDict["SpanAccentHoverBrush"]   = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.accentHover);
            darkDict["SpanTextPrimaryBrush"]   = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.textPri);
            darkDict["SpanTextSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.textSec);
            darkDict["SpanTextTertiaryBrush"]  = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.textTer);
            darkDict["SpanBgSelectedBrush"]    = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgSel);
            darkDict["SpanBorderSubtleBrush"]  = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.border);

            // ListView/GridView 선택 색상
            darkDict["ListViewItemBackgroundSelected"]            = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.listSel);
            darkDict["ListViewItemBackgroundSelectedPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.listSelHover);
            darkDict["ListViewItemBackgroundSelectedPressed"]     = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.listSelPressed);
            darkDict["GridViewItemBackgroundSelected"]            = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.listSel);
            darkDict["GridViewItemBackgroundSelectedPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.listSelHover);
            darkDict["GridViewItemBackgroundSelectedPressed"]     = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.listSelPressed);

            root.Resources.ThemeDictionaries["Dark"] = darkDict;
        }

        private static (
            Windows.UI.Color bgMica, Windows.UI.Color bgLayer1, Windows.UI.Color bgLayer2, Windows.UI.Color bgLayer3,
            Windows.UI.Color accent, Windows.UI.Color accentHover,
            Windows.UI.Color textPri, Windows.UI.Color textSec, Windows.UI.Color textTer,
            Windows.UI.Color bgSel, Windows.UI.Color border,
            Windows.UI.Color listSel, Windows.UI.Color listSelHover, Windows.UI.Color listSelPressed
        ) GetThemePalette(string theme) => theme switch
        {
            "dracula" => (
                Clr("#282a36"), Clr("#1e2029"), Clr("#282a36"), Clr("#44475a"),  // Background layers
                Clr("#bd93f9"), Clr("#caa8ff"),                                   // Purple accent
                Clr("#f8f8f2"), Clr("#6272a4"), Clr("#44475a"),                   // Foreground text
                Clr("#4Dbd93f9"), Clr("#33f8f8f2"),                               // Selection/border
                Clr("#99bd93f9"), Clr("#B3bd93f9"), Clr("#80bd93f9")              // List selection
            ),
            "tokyonight" => (
                Clr("#16161e"), Clr("#1a1b26"), Clr("#292e42"), Clr("#414868"),   // Tokyo Night bg layers
                Clr("#7aa2f7"), Clr("#7dcfff"),                                   // Blue + Cyan accent
                Clr("#c0caf5"), Clr("#a9b1d6"), Clr("#565f89"),                   // fg layers
                Clr("#4D7aa2f7"), Clr("#333b4261"),                               // Selection/border
                Clr("#997aa2f7"), Clr("#B37aa2f7"), Clr("#807aa2f7")              // List selection
            ),
            "catppuccin" => (
                Clr("#11111b"), Clr("#1e1e2e"), Clr("#181825"), Clr("#313244"),   // Crust→Base→Mantle→Surface0
                Clr("#cba6f7"), Clr("#b4befe"),                                   // Mauve + Lavender
                Clr("#cdd6f4"), Clr("#bac2de"), Clr("#7f849c"),                   // Text→Subtext1→Overlay1
                Clr("#4Dcba6f7"), Clr("#33585b70"),                               // Selection/border
                Clr("#99cba6f7"), Clr("#B3cba6f7"), Clr("#80cba6f7")              // List selection
            ),
            "gruvbox" => (
                Clr("#1d2021"), Clr("#282828"), Clr("#3c3836"), Clr("#504945"),   // bg0_h→bg0→bg1→bg2
                Clr("#fe8019"), Clr("#fabd2f"),                                   // Orange + Yellow accent
                Clr("#ebdbb2"), Clr("#d5c4a1"), Clr("#a89984"),                   // fg→fg2→fg4
                Clr("#4Dfe8019"), Clr("#33ebdbb2"),                               // Selection/border
                Clr("#99fe8019"), Clr("#B3fe8019"), Clr("#80fe8019")              // List selection
            ),
            _ => default
        };

        private static (
            Windows.UI.Color fg, Windows.UI.Color hoverFg, Windows.UI.Color hoverBg,
            Windows.UI.Color pressedFg, Windows.UI.Color pressedBg, Windows.UI.Color inactiveFg
        ) GetCaptionColors(string theme) => theme switch
        {
            "dracula" => (
                Clr("#f8f8f2"), Clr("#bd93f9"), Clr("#33bd93f9"),
                Clr("#caa8ff"), Clr("#4Dbd93f9"), Clr("#6272a4")
            ),
            "tokyonight" => (
                Clr("#a9b1d6"), Clr("#c0caf5"), Clr("#26394b70"),
                Clr("#c0caf5"), Clr("#40394b70"), Clr("#737aa2")
            ),
            "catppuccin" => (
                Clr("#a6adc8"), Clr("#cdd6f4"), Clr("#40585b70"),
                Clr("#bac2de"), Clr("#5945475a"), Clr("#6c7086")
            ),
            "gruvbox" => (
                Clr("#a89984"), Clr("#ebdbb2"), Clr("#1Febdbb2"),
                Clr("#fe8019"), Clr("#33fe8019"), Clr("#665c54")
            ),
            _ => (
                Clr("#FFFFFF"), Clr("#FFFFFF"), Clr("#0FFFFFFF"),
                Clr("#FFFFFF"), Clr("#14FFFFFF"), Clr("#787878")
            )
        };

        private static Windows.UI.Color Clr(string hex)
        {
            hex = hex.TrimStart('#');
            byte a = 255, r, g, b;
            if (hex.Length == 8)
            {
                a = Convert.ToByte(hex[..2], 16);
                r = Convert.ToByte(hex[2..4], 16);
                g = Convert.ToByte(hex[4..6], 16);
                b = Convert.ToByte(hex[6..8], 16);
            }
            else
            {
                r = Convert.ToByte(hex[..2], 16);
                g = Convert.ToByte(hex[2..4], 16);
                b = Convert.ToByte(hex[4..6], 16);
            }
            return Windows.UI.Color.FromArgb(a, r, g, b);
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
                    DispatcherQueue.TryEnqueue(() => ToggleThumbnails(value is bool st && st));
                    break;

                case "ShowFavoritesTree":
                    DispatcherQueue.TryEnqueue(() => ApplyFavoritesTreeMode(value is bool v && v));
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
            _densityPadding = density switch
            {
                "compact" => new Thickness(12, 0, 12, 0),
                "spacious" => new Thickness(12, 4, 12, 4),
                _ => new Thickness(12, 2, 12, 2) // comfortable
            };

            // Apply to all visible Miller Column ListViews
            foreach (var kvp in _tabMillerPanels)
                ApplyDensityToItemsControl(kvp.Value.items);
            ApplyDensityToItemsControl(MillerColumnsControlRight);

            // Apply to Details/Icon views via their public methods
            foreach (var kvp in _tabDetailsPanels)
                kvp.Value.ApplyDensity(density);
            foreach (var kvp in _tabIconPanels)
                kvp.Value.ApplyDensity(density);
        }

        private void ApplyDensityToItemsControl(ItemsControl? millerControl)
        {
            if (millerControl?.ItemsPanelRoot == null) return;
            foreach (var columnContainer in millerControl.ItemsPanelRoot.Children)
            {
                var listView = FindChild<ListView>(columnContainer);
                if (listView?.ItemsPanelRoot == null) continue;
                for (int i = 0; i < listView.Items.Count; i++)
                {
                    if (listView.ContainerFromIndex(i) is ListViewItem item)
                    {
                        var grid = FindChild<Grid>(item);
                        if (grid != null) grid.Padding = _densityPadding;
                    }
                }
            }
        }

        private void ApplyMillerCheckboxMode(bool showCheckboxes)
        {
            _millerSelectionMode = showCheckboxes
                ? ListViewSelectionMode.Multiple
                : ListViewSelectionMode.Extended;

            // Apply to all visible Miller Column ListViews in both panes
            // 모든 탭의 Miller 패널에도 적용
            foreach (var kvp in _tabMillerPanels)
                ApplyCheckboxToItemsControl(kvp.Value.items, _millerSelectionMode);
            ApplyCheckboxToItemsControl(MillerColumnsControlRight, _millerSelectionMode);
        }

        private void ToggleThumbnails(bool showThumbnails)
        {
            var explorer = ViewModel.ActiveExplorer;
            if (explorer?.CurrentFolder == null) return;

            foreach (var child in explorer.CurrentFolder.Children)
            {
                if (child is FileViewModel fileVm)
                {
                    if (showThumbnails && fileVm.IsThumbnailSupported)
                        _ = fileVm.LoadThumbnailAsync();
                    else
                        fileVm.UnloadThumbnail();
                }
            }
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
            var explorer = ViewModel.ActiveExplorer;
            var path = explorer?.CurrentPath;
            if (string.IsNullOrEmpty(path) || path == "PC")
            {
                ViewModel.ShowToast("유효한 폴더에서만 터미널을 열 수 있습니다");
                return;
            }
            if (!System.IO.Directory.Exists(path))
            {
                ViewModel.ShowToast("경로가 존재하지 않습니다");
                return;
            }
            var shellService = App.Current.Services.GetRequiredService<Services.ShellService>();
            shellService.OpenTerminal(path, _settings.DefaultTerminal);
        }

        /// <summary>
        /// Settings 탭을 닫고 이전 탭으로 복귀.
        /// 유일한 탭이면 Home 탭을 먼저 생성.
        /// </summary>
        private void CloseCurrentSettingsTab()
        {
            var tab = ViewModel.ActiveTab;
            if (tab == null || tab.ViewMode != ViewMode.Settings) return;

            int index = ViewModel.ActiveTabIndex;

            if (ViewModel.Tabs.Count <= 1)
            {
                // 유일한 탭이면 Home 탭 먼저 생성
                ViewModel.AddNewTab(); // Home 탭 추가 + 자동 SwitchToTab
                var newTab = ViewModel.ActiveTab;
                if (newTab != null)
                {
                    CreateMillerPanelForTab(newTab);
                    SwitchMillerPanel(newTab.Id);
                }
                // Settings 탭은 이제 인덱스 0
                ViewModel.CloseTab(0);
            }
            else
            {
                ViewModel.CloseTab(index);
                if (ViewModel.ActiveTab != null)
                    SwitchMillerPanel(ViewModel.ActiveTab.Id);
            }

            ResubscribeLeftExplorer();
            UpdateViewModeVisibility();
            FocusActiveView();
        }

        /// <summary>
        /// Settings 탭을 열거나 기존 탭으로 전환 (UI 연동 포함).
        /// </summary>
        private void OpenSettingsTab()
        {
            ViewModel.OpenOrSwitchToSettingsTab();
            ResubscribeLeftExplorer();
            UpdateViewModeVisibility();
            // Tab count changed — update passthrough region
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
        }

        private void RefreshCurrentView()
        {
            // Refresh only the leaf (last) column in the active pane.
            // Refreshing ALL columns causes cascading destruction: Children.Clear()
            // sets SelectedChild=null which removes subsequent columns.
            var explorer = ViewModel.ActiveExplorer;
            if (explorer.Columns.Count > 0)
            {
                var lastCol = explorer.Columns[explorer.Columns.Count - 1];
                _ = lastCol.RefreshAsync();
            }
        }

        // =================================================================
        //  Auto Scroll
        // =================================================================

        private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 탭 전환 중에는 ScrollToLastColumn + UpdateLayout 비용 회피
            if (ViewModel?.IsSwitchingTab == true) return;

            if (e.Action == NotifyCollectionChangedAction.Add ||
                e.Action == NotifyCollectionChangedAction.Replace)
            {
                ScrollToLastColumn(ViewModel.LeftExplorer, GetActiveMillerScrollViewer());
                // Apply checkbox mode to newly added columns after render
                if (_millerSelectionMode != ListViewSelectionMode.Extended)
                {
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                        () => ApplyCheckboxToItemsControl(GetActiveMillerColumnsControl(), _millerSelectionMode));
                }
            }

            UpdateFileSystemWatcherPaths();
        }

        private void OnRightColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add ||
                e.Action == NotifyCollectionChangedAction.Replace)
            {
                ScrollToLastColumn(ViewModel.RightExplorer, MillerScrollViewerRight);
                if (_millerSelectionMode != ListViewSelectionMode.Extended)
                {
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                        () => ApplyCheckboxToItemsControl(MillerColumnsControlRight, _millerSelectionMode));
                }
            }
        }

        // =================================================================
        //  FileSystemWatcher: 자동 새로고침
        // =================================================================

        private void InitializeFileSystemWatcher()
        {
            try
            {
                _watcherService = App.Current.Services.GetRequiredService<FileSystemWatcherService>();
                _watcherService.PathChanged += OnWatcherPathChanged;
                UpdateFileSystemWatcherPaths();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileSystemWatcher] 초기화 실패: {ex.Message}");
            }
        }

        private void UpdateFileSystemWatcherPaths()
        {
            if (_watcherService == null || _isClosed) return;

            var paths = new List<string>();

            // 활성 탭의 Left explorer 컬럼 경로들
            var leftExplorer = ViewModel?.Explorer;
            if (leftExplorer != null)
            {
                foreach (var col in leftExplorer.Columns)
                {
                    if (!string.IsNullOrEmpty(col.Path))
                        paths.Add(col.Path);
                }
            }

            // Right explorer 컬럼 경로들 (Split View 시)
            if (ViewModel?.IsSplitViewEnabled == true)
            {
                var rightExplorer = ViewModel.RightExplorer;
                if (rightExplorer != null)
                {
                    foreach (var col in rightExplorer.Columns)
                    {
                        if (!string.IsNullOrEmpty(col.Path))
                            paths.Add(col.Path);
                    }
                }
            }

            _watcherService.SetWatchedPaths(paths);
        }

        private void OnWatcherPathChanged(string changedPath)
        {
            if (_isClosed) return;

            DispatcherQueue.TryEnqueue(async () =>
            {
                if (_isClosed) return;

                // 캐시 무효화
                try
                {
                    var cache = App.Current.Services.GetService(typeof(FolderContentCache)) as FolderContentCache;
                    cache?.Invalidate(changedPath);

                    // 폴더 크기 캐시도 무효화
                    var sizeSvc = App.Current.Services.GetService(typeof(FolderSizeService)) as FolderSizeService;
                    sizeSvc?.Invalidate(changedPath);
                }
                catch { }

                // 변경된 경로의 컬럼 리로드
                var leftExplorer = ViewModel?.Explorer;
                if (leftExplorer != null)
                {
                    foreach (var col in leftExplorer.Columns.ToList())
                    {
                        if (col.Path.Equals(changedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            await col.ReloadAsync();
                            break;
                        }
                    }
                }

                if (ViewModel?.IsSplitViewEnabled == true)
                {
                    var rightExplorer = ViewModel.RightExplorer;
                    if (rightExplorer != null)
                    {
                        foreach (var col in rightExplorer.Columns.ToList())
                        {
                            if (col.Path.Equals(changedPath, StringComparison.OrdinalIgnoreCase))
                            {
                                await col.ReloadAsync();
                                break;
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 이전 LeftExplorer 참조 — 탭 전환 시 구독 해제용
        /// </summary>
        private ExplorerViewModel? _subscribedLeftExplorer;

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentViewMode) ||
                e.PropertyName == nameof(MainViewModel.RightViewMode))
            {
                // 탭 전환 중이거나 UpdateViewModeVisibility 내부에서는 FocusActiveView 억제
                if (!ViewModel.IsSwitchingTab && !_suppressFocusOnViewModeChange)
                {
                    // ViewMode 변경 시 패널 Visibility도 업데이트 (Home→Miller 등)
                    var newMode = ViewModel.CurrentViewMode;
                    if (_previousViewMode != newMode)
                    {
                        _previousViewMode = newMode;
                        SetViewModeVisibility(newMode);
                    }
                    FocusActiveView();
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.Explorer))
            {
                // LeftExplorer가 교체됨 — Columns 구독 재연결 및 View 업데이트
                ResubscribeLeftExplorer();
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

        /// <summary>
        /// LeftExplorer 교체 시 Columns.CollectionChanged 구독 재연결 + View ViewModel 갱신
        /// </summary>
        private void ResubscribeLeftExplorer()
        {
            if (_isClosed) return;

            // 이전 Explorer 구독 해제
            if (_subscribedLeftExplorer != null)
            {
                _subscribedLeftExplorer.Columns.CollectionChanged -= OnColumnsChanged;
                _subscribedLeftExplorer.Columns.CollectionChanged -= OnLeftColumnsChangedForPreview;
            }

            // 새 Explorer 구독
            var newExplorer = ViewModel.Explorer;
            if (newExplorer != null)
            {
                newExplorer.Columns.CollectionChanged += OnColumnsChanged;
                newExplorer.Columns.CollectionChanged += OnLeftColumnsChangedForPreview;

                // Breadcrumb ItemsSource — 보이는 컨트롤만 갱신 (Collapsed 컨테이너 재생성 방지)
                if (!ViewModel.IsSplitViewEnabled && ViewModel.CurrentViewMode != ViewMode.Home)
                    AddressBreadcrumbBar.ItemsSource = newExplorer.PathSegments;
                if (ViewModel.IsSplitViewEnabled)
                    LeftPaneBreadcrumbRepeater.ItemsSource = newExplorer.PathSegments;

                // Per-tab 인스턴스가 자체 ViewModel을 보유하므로 DetailsView/IconView 교체 불필요
                // Miller Columns는 Per-Tab Panel이, Home은 MainViewModel 바인딩이 처리
            }

            // Inline preview: re-subscribe to new explorer's SelectedFile
            if (newExplorer != null)
                ResubscribeInlinePreview(_subscribedLeftExplorer, newExplorer);

            _subscribedLeftExplorer = newExplorer;

            // M3: Preview 구독 갱신 — 크리티컬 패스에서 분리
            DispatcherQueue.TryEnqueue(() =>
            {
                UnsubscribePreviewSelection(isLeft: true);
                if (ViewModel.IsLeftPreviewEnabled)
                    SubscribePreviewToLastColumn(isLeft: true);
            });

            // FileSystemWatcher 감시 경로 갱신
            UpdateFileSystemWatcherPaths();
        }

        /// <summary>
        /// SwitchToTab이 PropertyChanged를 우회했으므로,
        /// XAML x:Bind가 관찰하는 ViewMode 관련 프로퍼티의 변경을 일괄 통지한다.
        /// IsSwitchingTab=false 이후에 호출되므로 OnViewModelPropertyChanged의 FocusActiveView가 정상 동작.
        /// </summary>
        private void UpdateViewModeVisibility()
        {
            _suppressFocusOnViewModeChange = true;
            try
            {
                var newMode = ViewModel.CurrentViewMode;
                if (_previousViewMode != newMode)
                {
                    _previousViewMode = newMode;
                    // x:Bind 파이프라인 우회: 직접 Visibility 할당 (PropertyChanged → x:Bind 재평가 제거)
                    SetViewModeVisibility(newMode);
                    // IsSingleNonHomeVisible 등 남은 바인딩용 (경량)
                    ViewModel.NotifyViewModeChanged();
                }
            }
            finally
            {
                _suppressFocusOnViewModeChange = false;
            }
        }

        /// <summary>
        /// x:Bind 바인딩 대신 코드비하인드에서 직접 4개 뷰의 Visibility를 설정.
        /// PropertyChanged 파이프라인을 거치지 않으므로 레이아웃 재계산 최소화.
        /// 또한 뷰 모드 전환 시 해당 뷰의 ViewModel을 lazy 갱신.
        /// </summary>
        private double _savedSidebarWidth = 200;
        private bool _sidebarHiddenForSpecialMode;

        private void SetViewModeVisibility(ViewMode mode)
        {
            bool isSpecialMode = mode == ViewMode.Settings;

            // HOST 단위 Visibility
            MillerTabsHost.Visibility = mode == ViewMode.MillerColumns ? Visibility.Visible : Visibility.Collapsed;
            DetailsTabsHost.Visibility = mode == ViewMode.Details ? Visibility.Visible : Visibility.Collapsed;
            ListTabsHost.Visibility = mode == ViewMode.List ? Visibility.Visible : Visibility.Collapsed;
            IconTabsHost.Visibility = Helpers.ViewModeExtensions.IsIconMode(mode) ? Visibility.Visible : Visibility.Collapsed;
            HomeView.Visibility = mode == ViewMode.Home ? Visibility.Visible : Visibility.Collapsed;
            SettingsView.Visibility = mode == ViewMode.Settings ? Visibility.Visible : Visibility.Collapsed;
            if (mode == ViewMode.Settings) SettingsView.RefreshSettings();

            // Settings/Home 모드: 사이드바 + 프리뷰 패널 숨김
            if (isSpecialMode)
            {
                if (!_sidebarHiddenForSpecialMode)
                {
                    _savedSidebarWidth = SidebarCol.Width.Value;
                    _sidebarHiddenForSpecialMode = true;
                }
                SidebarBorder.Visibility = Visibility.Collapsed;
                SidebarCol.Width = new GridLength(0);
                LeftPreviewSplitterCol.Width = new GridLength(0);
                LeftPreviewCol.Width = new GridLength(0);
            }
            else
            {
                if (_sidebarHiddenForSpecialMode)
                {
                    SidebarBorder.Visibility = Visibility.Visible;
                    SidebarCol.Width = new GridLength(_savedSidebarWidth);
                    _sidebarHiddenForSpecialMode = false;
                }
                // 프리뷰 패널 복원 (활성화 상태에 따라)
                if (ViewModel.IsLeftPreviewEnabled)
                {
                    LeftPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
                }
            }

            // Lazy 패널 생성 + 활성 패널 Visible 보장
            var tabId = ViewModel.ActiveTab?.Id;
            if (tabId != null && mode == ViewMode.Details)
            {
                if (!_tabDetailsPanels.ContainsKey(tabId))
                    CreateDetailsPanelForTab(ViewModel.ActiveTab!);
                if (_tabDetailsPanels.TryGetValue(tabId, out var dp))
                    dp.Visibility = Visibility.Visible;
                _activeDetailsTabId = tabId;
            }
            if (tabId != null && mode == ViewMode.List)
            {
                if (!_tabListPanels.ContainsKey(tabId))
                    CreateListPanelForTab(ViewModel.ActiveTab!);
                if (_tabListPanels.TryGetValue(tabId, out var mp))
                    mp.Visibility = Visibility.Visible;
                _activeListTabId = tabId;
            }
            if (tabId != null && Helpers.ViewModeExtensions.IsIconMode(mode))
            {
                if (!_tabIconPanels.ContainsKey(tabId))
                    CreateIconPanelForTab(ViewModel.ActiveTab!);
                if (_tabIconPanels.TryGetValue(tabId, out var ip))
                    ip.Visibility = Visibility.Visible;
                _activeIconTabId = tabId;
            }

            // Breadcrumb lazy 갱신 (ResubscribeLeftExplorer에서 skip된 경우 보정)
            var explorer = ViewModel.Explorer;
            if (!ViewModel.IsSplitViewEnabled && mode != ViewMode.Settings)
            {
                if (mode == ViewMode.Home)
                {
                    // 홈 모드: 🏠 > 홈 > breadcrumb 표시
                    HomeAddressIcon.Visibility = Visibility.Visible;
                    var homeSegments = new[]
                    {
                        new Models.PathSegment(_loc.Get("Home"), "::home::", isLast: false)
                    };
                    AddressBreadcrumbBar.ItemsSource = homeSegments;
                    SearchBox.PlaceholderText = _loc.Get("HomeSearch");
                }
                else
                {
                    HomeAddressIcon.Visibility = Visibility.Collapsed;
                    if (AddressBreadcrumbBar.ItemsSource != explorer?.PathSegments)
                        AddressBreadcrumbBar.ItemsSource = explorer?.PathSegments;
                    SearchBox.PlaceholderText = "Search (kind: size: ext: date:)";
                }
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
                            // H3: 동기 스크롤 (이미 Low priority 내부이므로 추가 디스패치 불필요)
                            ScrollToLastColumnSync(ViewModel.LeftExplorer, GetActiveMillerScrollViewer());
                            // 마지막 컬럼으로 포커스 (GetActiveColumnIndex 비주얼트리 순회 생략)
                            FocusColumnAsync(columns.Count - 1);
                        }
                        Helpers.DebugLogger.Log("[MainWindow] Focus: MillerColumns");
                        break;

                    case Models.ViewMode.Details:
                        GetActiveDetailsView()?.FocusListView();
                        Helpers.DebugLogger.Log("[MainWindow] Focus: Details");
                        break;

                    case Models.ViewMode.List:
                        GetActiveListView()?.FocusGridView();
                        Helpers.DebugLogger.Log("[MainWindow] Focus: List");
                        break;

                    case Models.ViewMode.IconSmall:
                    case Models.ViewMode.IconMedium:
                    case Models.ViewMode.IconLarge:
                    case Models.ViewMode.IconExtraLarge:
                        GetActiveIconView()?.FocusGridView();
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
                    double totalWidth = GetTotalColumnsActualWidth(columns.Count);
                    double viewportWidth = scrollViewer.ViewportWidth;
                    double targetScroll = Math.Max(0, totalWidth - viewportWidth);
                    scrollViewer.ChangeView(targetScroll, null, null, false);
                });
        }

        /// <summary>
        /// ScrollToLastColumn의 동기 버전 — 이미 DispatcherQueue Low 내부에서 호출될 때 사용.
        /// </summary>
        private void ScrollToLastColumnSync(ExplorerViewModel explorer, ScrollViewer? scrollViewer)
        {
            if (scrollViewer == null) return;
            var columns = explorer.Columns;
            if (columns.Count == 0) return;
            double totalWidth = GetTotalColumnsActualWidth(columns.Count);
            double viewportWidth = scrollViewer.ViewportWidth;
            double targetScroll = Math.Max(0, totalWidth - viewportWidth);
            scrollViewer.ChangeView(targetScroll, null, null, false);
        }

        /// <summary>
        /// 렌더링된 컬럼의 실제 너비 합산 (리사이즈 반영).
        /// </summary>
        private double GetTotalColumnsActualWidth(int columnCount)
        {
            var control = GetActiveMillerColumnsControl();
            double total = 0;
            for (int i = 0; i < columnCount; i++)
            {
                var container = control.ContainerFromIndex(i) as FrameworkElement;
                if (container != null && container.ActualWidth > 0)
                    total += container.ActualWidth;
                else
                    total += ColumnWidth;
            }
            return total;
        }

        /// <summary>
        /// 현재 활성 탐색기에서 진행 중인 리네임을 모두 취소.
        /// 다른 항목 선택, 탐색 등 새로운 동작 시작 시 호출.
        /// </summary>
        private void CancelAnyActiveRename()
        {
            var explorer = ViewModel?.ActiveExplorer;
            if (explorer == null) return;
            bool cancelled = false;
            foreach (var col in explorer.Columns)
            {
                foreach (var child in col.Children)
                {
                    if (child.IsRenaming)
                    {
                        child.CancelRename();
                        cancelled = true;
                    }
                }
            }
            if (cancelled)
            {
                _justFinishedRename = true;
            }
            _renameTargetPath = null;
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
        private async void OnDriveItemTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.DataContext is DriveItem drive)
            {
                if (drive.IsRemoteConnection && drive.ConnectionId != null)
                {
                    // 원격 연결: 비밀번호 확인 → 연결
                    await HandleRemoteConnectionTapped(drive.ConnectionId);
                }
                else
                {
                    ViewModel.OpenDrive(drive);
                    FocusColumnAsync(0);
                }
                Helpers.DebugLogger.Log($"[Sidebar] Drive tapped: {drive.Name}");
            }
        }

        private async void OnBrowseNetworkTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var networkService = App.Current.Services.GetRequiredService<NetworkBrowserService>();
            var connService = App.Current.Services.GetRequiredService<ConnectionManagerService>();

            // Create dialog content
            var dialogPanel = new StackPanel { Spacing = 12, MinWidth = 360 };

            // UNC path input section
            var pathInput = new TextBox
            {
                PlaceholderText = @"\\server\share",
                Header = _loc.Get("UncPathInput"),
                MinWidth = 340
            };
            dialogPanel.Children.Add(pathInput);

            // Separator
            dialogPanel.Children.Add(new TextBlock
            {
                Text = _loc.Get("SearchNetwork"),
                Foreground = (SolidColorBrush)Application.Current.Resources["SpanTextSecondaryBrush"],
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0)
            });

            // Network list
            var networkList = new ListView
            {
                Height = 250,
                SelectionMode = ListViewSelectionMode.Single
            };
            var iconFontPath = Services.IconService.Current?.FontFamilyPath ?? "/Assets/Fonts/remixicon.ttf#remixicon";
            networkList.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                $@"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                               xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
                    <StackPanel Orientation='Horizontal' Spacing='8' Padding='4,2'>
                        <TextBlock Text='{{Binding IconGlyph}}'
                                   FontFamily='{iconFontPath}'
                                   FontSize='16' VerticalAlignment='Center'/>
                        <TextBlock Text='{{Binding Name}}' FontSize='13' VerticalAlignment='Center'/>
                    </StackPanel>
                  </DataTemplate>");

            dialogPanel.Children.Add(networkList);

            // Status text
            var statusText = new TextBlock
            {
                Text = _loc.Get("SearchingComputers"),
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["SpanTextTertiaryBrush"]
            };
            dialogPanel.Children.Add(statusText);

            // State tracking
            string? selectedPath = null;

            // Load computers asynchronously
            _ = LoadNetworkComputersAsync();

            async Task LoadNetworkComputersAsync()
            {
                var computers = await networkService.GetNetworkComputersAsync();
                if (computers.Count > 0)
                {
                    networkList.ItemsSource = computers;
                    statusText.Text = string.Format(_loc.Get("ComputersFound"), computers.Count);
                }
                else
                {
                    statusText.Text = _loc.Get("NoComputersFound");
                }
            }

            networkList.DoubleTapped += async (s, args) =>
            {
                if (networkList.SelectedItem is NetworkItem item)
                {
                    if (item.Type == NetworkItemType.Server)
                    {
                        // Load shares for this server
                        statusText.Text = string.Format(_loc.Get("SearchingShares"), item.Name);
                        networkList.ItemsSource = null;

                        var shares = await networkService.GetServerSharesAsync(item.Name);
                        if (shares.Count > 0)
                        {
                            networkList.ItemsSource = shares;
                            statusText.Text = string.Format(_loc.Get("SharesFound"), shares.Count);
                        }
                        else
                        {
                            statusText.Text = _loc.Get("NoSharesFound");
                        }
                    }
                }
            };

            networkList.SelectionChanged += (s, args) =>
            {
                if (networkList.SelectedItem is NetworkItem item)
                {
                    selectedPath = item.Path;
                    pathInput.Text = item.Path;
                }
            };

            var dialog = new ContentDialog
            {
                Title = _loc.Get("NetworkBrowse"),
                Content = dialogPanel,
                PrimaryButtonText = _loc.Get("Register"),
                CloseButtonText = _loc.Get("Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                var targetPath = !string.IsNullOrWhiteSpace(pathInput.Text)
                    ? pathInput.Text.Trim()
                    : selectedPath;

                if (!string.IsNullOrEmpty(targetPath))
                {
                    // 중복 등록 방지: 같은 UNC 경로가 이미 등록되어 있는지 확인
                    var existing = connService.SavedConnections.FirstOrDefault(
                        c => c.Protocol == Models.RemoteProtocol.SMB
                             && string.Equals(c.UncPath, targetPath, StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                    {
                        // DisplayName: \\server\share → server\share
                        var displayName = targetPath.TrimStart('\\');

                        var newConn = new Models.ConnectionInfo
                        {
                            Protocol = Models.RemoteProtocol.SMB,
                            UncPath = targetPath,
                            DisplayName = displayName,
                            Port = Models.ConnectionInfo.GetDefaultPort(Models.RemoteProtocol.SMB),
                            LastConnected = DateTime.Now
                        };

                        connService.AddConnection(newConn);
                        Helpers.DebugLogger.Log($"[Network] SMB 연결 등록: {targetPath}");
                    }
                    else
                    {
                        Helpers.DebugLogger.Log($"[Network] SMB 연결 이미 등록됨: {targetPath}");
                    }

                    // 등록 후 해당 경로로 탐색
                    if (ViewModel.CurrentViewMode == ViewMode.Home)
                    {
                        ViewModel.SwitchViewMode(ViewMode.MillerColumns);
                    }

                    await ViewModel.ActiveExplorer.NavigateToPath(targetPath);
                    FocusColumnAsync(0);
                }
            }
        }

        /// <summary>
        /// 연결 다이얼로그 표시. existing이 null이면 새 연결, non-null이면 편집 모드.
        /// 반환: (result, connInfo, password, saveChecked)
        /// </summary>
        private async Task<(ContentDialogResult result, Models.ConnectionInfo? connInfo, string? password, bool saveChecked)>
            ShowConnectionDialog(Models.ConnectionInfo? existing)
        {
            var isEdit = existing != null;
            var isSmbEdit = isEdit && existing!.Protocol == Models.RemoteProtocol.SMB;

            var dialogPanel = new StackPanel { Spacing = 12, MinWidth = 380 };

            // SMB 편집: 표시 이름 + UNC 경로만
            TextBox? smbDisplayNameInput = null;
            TextBox? smbUncPathInput = null;
            ComboBox? protocolCombo = null;
            TextBox? hostInput = null;
            NumberBox? portInput = null;
            TextBox? usernameInput = null;
            PasswordBox? passwordInput = null;
            TextBox? pathInput = null;
            TextBox? displayNameInput = null;
            CheckBox? saveCheckBox = null;

            if (isSmbEdit)
            {
                smbDisplayNameInput = new TextBox
                {
                    Header = _loc.Get("DisplayNameOptional"),
                    Text = existing!.DisplayName,
                    PlaceholderText = existing.UncPath ?? ""
                };
                dialogPanel.Children.Add(smbDisplayNameInput);

                smbUncPathInput = new TextBox
                {
                    Header = "UNC",
                    Text = existing.UncPath ?? "",
                    PlaceholderText = @"\\server\share"
                };
                dialogPanel.Children.Add(smbUncPathInput);
            }
            else
            {
                // 프로토콜 선택
                protocolCombo = new ComboBox
                {
                    Header = _loc.Get("Protocol"),
                    ItemsSource = new[] { "SFTP", "FTP", "FTPS" },
                    SelectedIndex = isEdit ? (int)existing!.Protocol : 0,
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch
                };
                dialogPanel.Children.Add(protocolCombo);

                // 호스트 + 포트
                var hostPortPanel = new Grid();
                hostPortPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                hostPortPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                hostInput = new TextBox
                {
                    Header = _loc.Get("Host"),
                    PlaceholderText = "example.com",
                    Text = isEdit ? existing!.Host : ""
                };
                Grid.SetColumn(hostInput, 0);
                hostPortPanel.Children.Add(hostInput);

                portInput = new NumberBox
                {
                    Header = _loc.Get("Port"),
                    Value = isEdit ? existing!.Port : 22,
                    Minimum = 1,
                    Maximum = 65535,
                    SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(portInput, 1);
                hostPortPanel.Children.Add(portInput);

                dialogPanel.Children.Add(hostPortPanel);

                // 포트 자동 변경 (새 연결 모드에서만)
                if (!isEdit)
                {
                    protocolCombo.SelectionChanged += (s, args) =>
                    {
                        portInput.Value = protocolCombo.SelectedIndex switch
                        {
                            0 => 22,   // SFTP
                            1 => 21,   // FTP
                            2 => 990,  // FTPS
                            _ => 22
                        };
                    };
                }

                // 사용자명
                usernameInput = new TextBox
                {
                    Header = _loc.Get("Username"),
                    PlaceholderText = "user",
                    Text = isEdit ? existing!.Username : ""
                };
                dialogPanel.Children.Add(usernameInput);

                // 비밀번호
                passwordInput = new PasswordBox
                {
                    Header = _loc.Get("Password"),
                    PlaceholderText = _loc.Get("Password")
                };
                if (isEdit)
                {
                    var connService = App.Current.Services.GetRequiredService<ConnectionManagerService>();
                    var savedPw = connService.LoadCredential(existing!.Id);
                    if (!string.IsNullOrEmpty(savedPw))
                        passwordInput.Password = savedPw;
                }
                dialogPanel.Children.Add(passwordInput);

                // 원격 경로
                pathInput = new TextBox
                {
                    Header = _loc.Get("RemotePath"),
                    PlaceholderText = "/",
                    Text = isEdit ? existing!.RemotePath : "/"
                };
                dialogPanel.Children.Add(pathInput);

                // 표시 이름
                displayNameInput = new TextBox
                {
                    Header = _loc.Get("DisplayNameOptional"),
                    PlaceholderText = isEdit ? existing!.DisplayName : "",
                    Text = isEdit ? existing!.DisplayName : ""
                };
                dialogPanel.Children.Add(displayNameInput);

                // 연결 저장 체크박스 (새 연결 모드에서만)
                if (!isEdit)
                {
                    saveCheckBox = new CheckBox { Content = _loc.Get("SaveConnection"), IsChecked = true };
                    dialogPanel.Children.Add(saveCheckBox);
                }
            }

            var dialog = new ContentDialog
            {
                Title = isEdit ? _loc.Get("EditConnection").TrimEnd('.') : _loc.Get("ConnectToServer"),
                Content = dialogPanel,
                PrimaryButtonText = isEdit ? _loc.Get("Save") : _loc.Get("Connect"),
                CloseButtonText = _loc.Get("Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
                return (result, null, null, false);

            if (isSmbEdit)
            {
                var updated = new Models.ConnectionInfo
                {
                    Id = existing!.Id,
                    Protocol = Models.RemoteProtocol.SMB,
                    DisplayName = !string.IsNullOrWhiteSpace(smbDisplayNameInput!.Text)
                        ? smbDisplayNameInput.Text.Trim()
                        : (smbUncPathInput!.Text.Trim()),
                    UncPath = smbUncPathInput!.Text.Trim(),
                    Host = existing.Host,
                    Port = existing.Port,
                    Username = existing.Username,
                    RemotePath = existing.RemotePath,
                    LastConnected = existing.LastConnected
                };
                return (result, updated, null, false);
            }

            if (string.IsNullOrWhiteSpace(hostInput!.Text))
                return (ContentDialogResult.None, null, null, false);

            var protocol = (Models.RemoteProtocol)protocolCombo!.SelectedIndex;
            var connInfoResult = new Models.ConnectionInfo
            {
                Id = isEdit ? existing!.Id : Guid.NewGuid().ToString("N"),
                DisplayName = !string.IsNullOrWhiteSpace(displayNameInput!.Text)
                    ? displayNameInput.Text.Trim()
                    : $"{hostInput.Text.Trim()}:{(int)portInput!.Value}",
                Protocol = protocol,
                Host = hostInput.Text.Trim(),
                Port = (int)portInput!.Value,
                Username = usernameInput!.Text.Trim(),
                RemotePath = string.IsNullOrWhiteSpace(pathInput!.Text) ? "/" : pathInput.Text.Trim(),
                LastConnected = isEdit ? existing!.LastConnected : DateTime.Now
            };

            return (result, connInfoResult, passwordInput!.Password, saveCheckBox?.IsChecked == true);
        }

        private async void OnConnectToServerTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var (result, connInfo, password, saveChecked) = await ShowConnectionDialog(null);
            if (result != ContentDialogResult.Primary || connInfo == null) return;

            Helpers.DebugLogger.Log($"[Network] 서버 연결 시도: {connInfo.ToUri()}");

            // 먼저 연결 시도 — 성공 시에만 저장
            var connService = App.Current.Services.GetRequiredService<ConnectionManagerService>();
            var router = App.Current.Services.GetRequiredService<FileSystemRouter>();
            var uriPrefix = FileSystemRouter.GetUriPrefix(connInfo.ToUri());

            IFileSystemProvider provider;
            try
            {
                if (connInfo.Protocol == Models.RemoteProtocol.SFTP)
                {
                    var sftp = new SftpProvider();
                    await sftp.ConnectAsync(connInfo, password ?? "");
                    if (!sftp.IsConnected) throw new Exception("SFTP 연결 실패");
                    provider = sftp;
                }
                else
                {
                    var ftp = new FtpProvider();
                    await ftp.ConnectAsync(connInfo, password ?? "");
                    if (!ftp.IsConnected) throw new Exception("FTP 연결 실패");
                    provider = ftp;
                }
            }
            catch (Exception ex)
            {
                await ShowRemoteConnectionError(connInfo, ex.Message);
                return;
            }

            // 연결 성공 → 저장 + Router 등록 + 탐색
            if (saveChecked)
            {
                connService.AddConnection(connInfo);
                if (!string.IsNullOrEmpty(password))
                    connService.SaveCredential(connInfo.Id, password);
            }

            router.RegisterConnection(uriPrefix, provider);
            connInfo.LastConnected = DateTime.Now;
            if (saveChecked)
                _ = connService.SaveConnectionsAsync();

            ViewModel.ShowToast($"{connInfo.DisplayName}에 연결되었습니다.");

            if (ViewModel.CurrentViewMode == ViewMode.Home)
                ViewModel.SwitchViewMode(ViewMode.MillerColumns);

            await ViewModel.ActiveExplorer.NavigateToPath(connInfo.ToUri());
            FocusColumnAsync(0);
        }

        private async void OnSavedConnectionTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.DataContext is Models.ConnectionInfo connInfo)
            {
                Helpers.DebugLogger.Log($"[Sidebar] 저장된 연결 탭: {connInfo.DisplayName}");
                await HandleRemoteConnectionTapped(connInfo.Id);
            }
        }

        /// <summary>
        /// 사이드바 빈 공간 우클릭 → 네트워크/서버 연결 컨텍스트 메뉴
        /// </summary>
        private void OnSidebarEmptyRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            // 드라이브 아이템 위에서 우클릭한 경우는 스킵 (OnSidebarDriveRightTapped이 처리)
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is DriveItem)
                return;

            var flyout = new MenuFlyout();

            var currentFontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                Services.IconService.Current?.FontFamilyPath ?? "/Assets/Fonts/remixicon.ttf#remixicon");
            var browseNetwork = new MenuFlyoutItem
            {
                Text = _loc.Get("NetworkBrowse") + "...",
                Icon = new FontIcon
                {
                    Glyph = Services.IconService.Current?.NetworkGlyph ?? "\uEDD4",
                    FontFamily = currentFontFamily,
                    FontSize = 16
                }
            };
            browseNetwork.Click += (s, args) => OnBrowseNetworkTapped(s, null!);
            flyout.Items.Add(browseNetwork);

            var connectServer = new MenuFlyoutItem
            {
                Text = _loc.Get("ConnectToServer") + "...",
                Icon = new FontIcon
                {
                    Glyph = Services.IconService.Current?.ServerGlyph ?? "\uEE71",
                    FontFamily = currentFontFamily,
                    FontSize = 16
                }
            };
            connectServer.Click += (s, args) => OnConnectToServerTapped(s, null!);
            flyout.Items.Add(connectServer);

            flyout.ShowAt(sender as FrameworkElement, e.GetPosition(sender as UIElement));
        }

        /// <summary>
        /// 원격 연결 드라이브 클릭 처리 (ConnectionId로 저장된 연결 정보 조회 → 비밀번호 확인 → 연결)
        /// </summary>
        private async Task HandleRemoteConnectionTapped(string connectionId)
        {
            var connService = App.Current.Services.GetRequiredService<ConnectionManagerService>();
            var connInfo = ViewModel.SavedConnections.FirstOrDefault(c => c.Id == connectionId);
            if (connInfo == null)
            {
                Helpers.DebugLogger.Log($"[Sidebar] 연결 정보를 찾을 수 없음: {connectionId}");
                ViewModel.ShowToast("연결 정보를 찾을 수 없습니다. 연결이 삭제되었을 수 있습니다.");
                return;
            }

            // SMB 연결: 비밀번호/프로세스 없이 UNC 경로로 직접 탐색
            if (connInfo.Protocol == Models.RemoteProtocol.SMB && !string.IsNullOrEmpty(connInfo.UncPath))
            {
                Helpers.DebugLogger.Log($"[Sidebar] SMB 직접 탐색: {connInfo.UncPath}");
                connInfo.LastConnected = DateTime.Now;
                _ = connService.SaveConnectionsAsync();

                if (ViewModel.CurrentViewMode == ViewMode.Home)
                    ViewModel.SwitchViewMode(ViewMode.MillerColumns);

                await ViewModel.ActiveExplorer.NavigateToPath(connInfo.UncPath);
                FocusColumnAsync(0);
                return;
            }

            var router = App.Current.Services.GetRequiredService<FileSystemRouter>();
            var uriPrefix = FileSystemRouter.GetUriPrefix(connInfo.ToUri());

            // 이미 연결된 경우: 바로 네비게이션
            if (router.GetConnectionForPath(uriPrefix + "/") != null)
            {
                Helpers.DebugLogger.Log($"[Sidebar] 기존 연결 재사용: {connInfo.DisplayName}");

                if (ViewModel.CurrentViewMode == ViewMode.Home)
                    ViewModel.SwitchViewMode(ViewMode.MillerColumns);

                await ViewModel.ActiveExplorer.NavigateToPath(connInfo.ToUri());
                FocusColumnAsync(0);
                return;
            }

            var savedPassword = connService.LoadCredential(connInfo.Id);

            if (string.IsNullOrEmpty(savedPassword))
            {
                // 비밀번호 입력 대화상자
                var passwordInput = new PasswordBox { PlaceholderText = "비밀번호" };
                var dialog = new ContentDialog
                {
                    Title = $"{connInfo.DisplayName} 연결",
                    Content = passwordInput,
                    PrimaryButtonText = "연결",
                    CloseButtonText = "취소",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return;
                savedPassword = passwordInput.Password;
            }

            Helpers.DebugLogger.Log($"[Sidebar] 원격 연결 시도: {connInfo.DisplayName}");

            // 연결 시도 (provider를 유지!)
            IFileSystemProvider provider;
            try
            {
                if (connInfo.Protocol == Models.RemoteProtocol.SFTP)
                {
                    var sftp = new SftpProvider();
                    await sftp.ConnectAsync(connInfo, savedPassword);
                    if (!sftp.IsConnected) throw new Exception("SFTP 연결 실패");
                    provider = sftp;
                }
                else
                {
                    var ftp = new FtpProvider();
                    await ftp.ConnectAsync(connInfo, savedPassword);
                    if (!ftp.IsConnected) throw new Exception("FTP 연결 실패");
                    provider = ftp;
                }
            }
            catch (Renci.SshNet.Common.SshAuthenticationException ex)
            {
                await ShowRemoteConnectionError(connInfo, $"인증 실패: 사용자명 또는 비밀번호를 확인하세요.\n\n{ex.Message}");
                return;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                await ShowRemoteConnectionError(connInfo, $"서버에 연결할 수 없습니다.\n호스트({connInfo.Host}:{connInfo.Port})에 도달할 수 없거나 연결이 거부되었습니다.\n\n{ex.Message}");
                return;
            }
            catch (TimeoutException ex)
            {
                await ShowRemoteConnectionError(connInfo, $"연결 시간 초과: 서버가 응답하지 않습니다.\n\n{ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                await ShowRemoteConnectionError(connInfo, $"서버에 연결할 수 없습니다.\n\n오류: {ex.Message}");
                return;
            }

            // 연결 성공 → Router에 등록 + 네비게이션
            router.RegisterConnection(uriPrefix, provider);
            connInfo.LastConnected = DateTime.Now;
            _ = connService.SaveConnectionsAsync();

            ViewModel.ShowToast($"{connInfo.DisplayName}에 연결되었습니다.");

            // Home 모드면 Miller로 전환 후 네비게이션
            if (ViewModel.CurrentViewMode == ViewMode.Home)
                ViewModel.SwitchViewMode(ViewMode.MillerColumns);

            await ViewModel.ActiveExplorer.NavigateToPath(connInfo.ToUri());
            FocusColumnAsync(0);
        }

        private async Task ShowRemoteConnectionError(Models.ConnectionInfo connInfo, string detail)
        {
            Helpers.DebugLogger.Log($"[Network] 연결 실패: {connInfo.DisplayName} - {detail}");
            var errorDialog = new ContentDialog
            {
                Title = _loc.Get("ConnectionFailed"),
                Content = detail,
                CloseButtonText = _loc.Get("OK"),
                XamlRoot = this.Content.XamlRoot
            };
            await errorDialog.ShowAsync();
        }

        private void OnHomeItemTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ViewModel.SwitchViewMode(ViewMode.Home);
            Helpers.DebugLogger.Log("[Sidebar] Home tapped");
        }

        // =================================================================
        //  Sidebar Favorites Tree (TreeView with lazy-loaded subfolders)
        // =================================================================

        private void ApplyFavoritesTreeMode(bool treeMode)
        {
            FavoritesTreeView.Visibility = treeMode
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
            FavoritesFlatList.Visibility = treeMode
                ? Microsoft.UI.Xaml.Visibility.Collapsed
                : Microsoft.UI.Xaml.Visibility.Visible;
        }

        private void OnFavoritesFlatItemTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is FavoriteItem fav)
            {
                if (!string.IsNullOrEmpty(fav.Path) && System.IO.Directory.Exists(fav.Path))
                {
                    var activeViewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                        ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;
                    if (activeViewMode == ViewMode.Home)
                        ViewModel.SwitchViewMode(ViewMode.MillerColumns);

                    var folder = new FolderItem
                    {
                        Name = System.IO.Path.GetFileName(fav.Path) ?? fav.Path,
                        Path = fav.Path
                    };
                    _ = ViewModel.ActiveExplorer.NavigateTo(folder);
                    FocusColumnAsync(0);
                }
            }
        }

        private void OnFavoritesFlatItemRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is FavoriteItem fav)
            {
                var flyout = _contextMenuService.BuildFavoriteMenu(fav, this);
                flyout.ShowAt(fe, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(fe)
                });
                e.Handled = true;
            }
        }

        /// <summary>
        /// Populate the favorites TreeView from ViewModel.Favorites.
        /// Each root node is a FavoriteItem; child nodes (subfolders) are lazily loaded on expand.
        /// </summary>
        private void PopulateFavoritesTree()
        {
            FavoritesTreeView.RootNodes.Clear();
            foreach (var fav in ViewModel.Favorites)
            {
                var node = new TreeViewNode
                {
                    Content = fav,
                    HasUnrealizedChildren = HasSubfolders(fav.Path)
                };
                FavoritesTreeView.RootNodes.Add(node);
            }
        }

        /// <summary>
        /// Repopulate the tree when the Favorites collection changes (add/remove).
        /// </summary>
        private void OnFavoritesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isClosed) return;
            PopulateFavoritesTree();
        }

        /// <summary>
        /// Check if a directory path has any visible subfolders (for expand chevron).
        /// </summary>
        private static bool HasSubfolders(string path)
        {
            try
            {
                if (!System.IO.Directory.Exists(path)) return false;
                foreach (var dir in System.IO.Directory.EnumerateDirectories(path))
                {
                    try
                    {
                        var info = new System.IO.DirectoryInfo(dir);
                        if ((info.Attributes & System.IO.FileAttributes.Hidden) != 0) continue;
                        if ((info.Attributes & System.IO.FileAttributes.System) != 0) continue;
                        return true; // Found at least one visible subfolder
                    }
                    catch { continue; }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Lazy-load child subfolders when a tree node is expanded.
        /// </summary>
        private void OnFavoritesTreeExpanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            if (!args.Node.HasUnrealizedChildren) return;
            args.Node.HasUnrealizedChildren = false;

            var path = GetPathFromNode(args.Node);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var dirs = System.IO.Directory.GetDirectories(path);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                foreach (var dir in dirs)
                {
                    try
                    {
                        var info = new System.IO.DirectoryInfo(dir);
                        if ((info.Attributes & System.IO.FileAttributes.Hidden) != 0) continue;
                        if ((info.Attributes & System.IO.FileAttributes.System) != 0) continue;

                        var childNode = new TreeViewNode
                        {
                            Content = new SidebarFolderNode
                            {
                                Name = info.Name,
                                Path = dir,
                                IconGlyph = Services.IconService.Current?.FolderGlyph ?? "\uED53"
                            },
                            HasUnrealizedChildren = true // Assume subfolders may exist; checked lazily on next expand
                        };
                        args.Node.Children.Add(childNode);
                    }
                    catch { /* Skip inaccessible directories */ }
                }
            }
            catch { }
        }

        /// <summary>
        /// Navigate to the folder when a tree item is invoked (clicked).
        /// </summary>
        private void OnFavoritesTreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            var path = "";
            // InvokedItem may be the TreeViewNode (manual RootNodes mode) or the Content directly
            if (args.InvokedItem is TreeViewNode node)
            {
                path = GetPathFromNode(node);
            }
            else if (args.InvokedItem is FavoriteItem fav)
            {
                path = fav.Path;
            }
            else if (args.InvokedItem is SidebarFolderNode sfn)
            {
                path = sfn.Path;
            }

            if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
            {
                // Switch away from Home mode if needed
                var activeViewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                    ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;
                if (activeViewMode == ViewMode.Home)
                {
                    ViewModel.SwitchViewMode(ViewMode.MillerColumns);
                }

                var folder = new FolderItem
                {
                    Name = System.IO.Path.GetFileName(path) ?? path,
                    Path = path
                };
                _ = ViewModel.ActiveExplorer.NavigateTo(folder);
                FocusColumnAsync(0);
                Helpers.DebugLogger.Log($"[Sidebar] Favorites tree item invoked: {path}");
            }
        }

        /// <summary>
        /// Extract the file system path from a TreeViewNode's content.
        /// </summary>
        private static string GetPathFromNode(TreeViewNode node)
        {
            if (node.Content is FavoriteItem fav)
                return fav.Path;
            if (node.Content is SidebarFolderNode sfn)
                return sfn.Path;
            return string.Empty;
        }

        /// <summary>
        /// Right-click context menu for favorites tree items.
        /// Root items (FavoriteItem) show the favorite context menu.
        /// Child items (SidebarFolderNode) navigate to the folder and offer basic folder actions.
        /// </summary>
        private void OnFavoritesTreeRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            // Find the TreeViewItem that was right-clicked
            var element = e.OriginalSource as DependencyObject;
            TreeViewItem? treeViewItem = null;
            while (element != null)
            {
                if (element is TreeViewItem tvi)
                {
                    treeViewItem = tvi;
                    break;
                }
                element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
            }

            if (treeViewItem == null) return;

            // Find the corresponding TreeViewNode from the TreeViewItem
            // The TreeViewItem's DataContext is the Content of the TreeViewNode
            var content = treeViewItem.DataContext;

            if (content is FavoriteItem favorite)
            {
                var flyout = _contextMenuService.BuildFavoriteMenu(favorite, this);
                flyout.ShowAt(treeViewItem, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(treeViewItem)
                });
                e.Handled = true;
            }
            else if (content is SidebarFolderNode folderNode)
            {
                // Build a simple context menu for subfolder nodes
                var menu = new MenuFlyout();

                var openItem = new MenuFlyoutItem
                {
                    Text = _loc.Get("Open"),
                    Icon = new FontIcon { Glyph = "\uE8E5" }
                };
                openItem.Click += (s, a) =>
                {
                    if (System.IO.Directory.Exists(folderNode.Path))
                    {
                        var folder = new FolderItem
                        {
                            Name = folderNode.Name,
                            Path = folderNode.Path
                        };
                        _ = ViewModel.ActiveExplorer.NavigateTo(folder);
                        FocusColumnAsync(0);
                    }
                };
                menu.Items.Add(openItem);
                menu.Items.Add(new MenuFlyoutSeparator());

                var addFavItem = new MenuFlyoutItem
                {
                    Text = _loc.Get("AddToFavorites"),
                    Icon = new FontIcon { Glyph = "\uE734" }
                };
                addFavItem.Click += (s, a) => ViewModel.AddToFavorites(folderNode.Path);
                menu.Items.Add(addFavItem);
                menu.Items.Add(new MenuFlyoutSeparator());

                var copyPathItem = new MenuFlyoutItem
                {
                    Text = _loc.Get("CopyPath"),
                    Icon = new FontIcon { Glyph = "\uE8C8" }
                };
                copyPathItem.Click += (s, a) =>
                {
                    var shellService = App.Current.Services.GetRequiredService<ShellService>();
                    shellService.CopyPathToClipboard(folderNode.Path);
                };
                menu.Items.Add(copyPathItem);

                var openExplorerItem = new MenuFlyoutItem
                {
                    Text = _loc.Get("OpenInExplorer"),
                    Icon = new FontIcon { Glyph = "\uED25" }
                };
                openExplorerItem.Click += (s, a) =>
                {
                    var shellService = App.Current.Services.GetRequiredService<ShellService>();
                    shellService.OpenInExplorer(folderNode.Path);
                };
                menu.Items.Add(openExplorerItem);

                menu.ShowAt(treeViewItem, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(treeViewItem)
                });
                e.Handled = true;
            }
        }

        /// <summary>
        /// Miller Column ListView 빈 공간 우클릭 → 빈 영역 컨텍스트 메뉴.
        /// 아이템 위에서의 우클릭은 OnFolderRightTapped/OnFileRightTapped에서 e.Handled=true 처리됨.
        /// </summary>
        private void OnMillerColumnEmptyAreaRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (e.Handled) return; // 아이템 핸들러가 이미 처리함
            if (!_settings.ShowContextMenu) return;

            if (sender is ListView listView && listView.DataContext is FolderViewModel folderVm)
            {
                var flyout = _contextMenuService.BuildEmptyAreaMenu(folderVm.Path, this);
                flyout.ShowAt(listView, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(listView)
                });
                e.Handled = true;
            }
        }

        // ── Rubber-band selection: attach/detach helpers per column ──

        private void OnMillerColumnContentGridLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid grid) return;
            if (_rubberBandHelpers.ContainsKey(grid)) return;

            var listView = FindChild<ListView>(grid);
            if (listView == null) return;

            // Apply density padding to new column items as they materialize
            listView.ContainerContentChanging += OnMillerContainerContentChanging;

            var helper = new Helpers.RubberBandSelectionHelper(
                grid,
                listView,
                () => _isSyncingSelection,
                val => _isSyncingSelection = val);

            _rubberBandHelpers[grid] = helper;
        }

        private void OnMillerContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is ListViewItem item)
            {
                // Apply density padding to the content grid inside the item
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    var grid = FindChild<Grid>(item);
                    if (grid != null) grid.Padding = _densityPadding;
                });
            }
        }

        private void OnMillerColumnContentGridUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid grid) return;

            var listView = FindChild<ListView>(grid);
            if (listView != null)
                listView.ContainerContentChanging -= OnMillerContainerContentChanging;

            if (_rubberBandHelpers.TryGetValue(grid, out var helper))
            {
                helper.Detach();
                _rubberBandHelpers.Remove(grid);
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
            // Cancel file D&D if rubber-band selection is active
            if (_rubberBandHelpers.Values.Any(h => h.IsActive))
            { e.Cancel = true; return; }

            // Allow dragging both files and folders
            var items = e.Items.OfType<FileSystemViewModel>().ToList();
            if (items.Count == 0) { e.Cancel = true; return; }

            var paths = items.Select(i => i.Path).ToList();
            e.Data.SetText(string.Join("\n", paths));
            e.Data.Properties["SourcePaths"] = paths;
            e.Data.Properties["SourcePane"] = DeterminePane(sender);
            e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;

            // Span→외부 앱: StorageItems를 지연 로딩 (외부 앱이 요청할 때만 로드)
            // DragItemsStarting에서 await 사용 금지 — async void + await는 드래그 종료 시
            // UI 스레드 데드락 유발 (DataPackage freeze 후 async 연속이 수정 시도)
            var capturedPaths = new List<string>(paths);
            e.Data.SetDataProvider(StandardDataFormats.StorageItems, request =>
            {
                var deferral = request.GetDeferral();
                _ = ProvideStorageItemsAsync(request, capturedPaths, deferral);
            });
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

        /// <summary>
        /// Deferred StorageItems provider for drag-and-drop to external apps.
        /// Called lazily only when an external app (e.g. Windows Explorer) requests the data.
        /// </summary>
        private static async System.Threading.Tasks.Task ProvideStorageItemsAsync(
            Windows.ApplicationModel.DataTransfer.DataProviderRequest request,
            List<string> paths,
            Windows.ApplicationModel.DataTransfer.DataProviderDeferral deferral)
        {
            try
            {
                var storageItems = new List<Windows.Storage.IStorageItem>();
                foreach (var p in paths)
                {
                    try
                    {
                        if (System.IO.Directory.Exists(p))
                            storageItems.Add(await Windows.Storage.StorageFolder.GetFolderFromPathAsync(p));
                        else if (System.IO.File.Exists(p))
                            storageItems.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(p));
                    }
                    catch { }
                }
                request.SetData(storageItems);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DragDrop] StorageItems provider error: {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void OnFavoritesDragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.Text) ||
                e.DataView.Contains(StandardDataFormats.StorageItems))
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
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item.Path) && System.IO.Directory.Exists(item.Path))
                    {
                        ViewModel.AddToFavorites(item.Path);
                        Helpers.DebugLogger.Log($"[Sidebar] External folder dropped to favorites: {item.Path}");
                    }
                }
            }
        }

        // =================================================================
        //  Drag & Drop: Folder item targets (drop file onto a folder)
        // =================================================================

        private void OnFolderItemDragOver(object sender, DragEventArgs e)
        {
            if (sender is not Grid grid || grid.DataContext is not FolderViewModel targetFolder) return;

            // Check if data contains paths (internal or external app)
            if (!e.DataView.Contains(StandardDataFormats.Text) &&
                !e.DataView.Properties.ContainsKey("SourcePaths") &&
                !e.DataView.Contains(StandardDataFormats.StorageItems)) return;

            // Prevent dropping onto self (check source paths)
            if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths)
            {
                if (srcPaths.Any(p => p.Equals(targetFolder.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    e.AcceptedOperation = DataPackageOperation.None;
                    e.Handled = true;
                    return;
                }
                // Prevent dropping parent into child
                if (srcPaths.Any(p => targetFolder.Path.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)))
                {
                    e.AcceptedOperation = DataPackageOperation.None;
                    e.Handled = true;
                    return;
                }
            }

            bool isMove = ResolveDragDropOperation(e, targetFolder.Path);

            e.AcceptedOperation = isMove ? DataPackageOperation.Move : DataPackageOperation.Copy;
            e.DragUIOverride.Caption = isMove ? $"Move to {targetFolder.Name}" : $"Copy to {targetFolder.Name}";
            e.DragUIOverride.IsCaptionVisible = true;

            // Visual feedback: highlight background
            grid.Background = new SolidColorBrush(Microsoft.UI.Colors.White) { Opacity = 0.08 };

            // Spring-loaded folder: start timer if hovering over a new folder
            if (_springLoadTarget != targetFolder)
            {
                StopSpringLoadTimer();
                _springLoadTarget = targetFolder;
                _springLoadGrid = grid;
                StartSpringLoadTimer();
            }

            e.Handled = true;
        }

        private async void OnFolderItemDrop(object sender, DragEventArgs e)
        {
            if (sender is not Grid grid || grid.DataContext is not FolderViewModel targetFolder) return;
            e.Handled = true; // Prevent bubbling BEFORE await (avoid duplicate execution)

            // Reset highlight and cancel spring-load
            grid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            StopSpringLoadTimer();

            var paths = await ExtractDropPaths(e);
            if (paths.Count == 0) return;

            bool isMove = ResolveDragDropOperation(e, targetFolder.Path);
            await HandleDropAsync(paths, targetFolder.Path, isMove: isMove);
        }

        private void OnFolderItemDragLeave(object sender, DragEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }

            // Cancel spring-loaded timer when leaving the target folder
            if (sender is Grid g && g.DataContext is FolderViewModel leavingFolder
                && leavingFolder == _springLoadTarget)
            {
                StopSpringLoadTimer();
            }
        }

        // =================================================================
        //  Spring-loaded folders: auto-open folder after drag hover delay
        // =================================================================

        private void StartSpringLoadTimer()
        {
            _springLoadTimer = new DispatcherTimer();
            _springLoadTimer.Interval = TimeSpan.FromMilliseconds(SPRING_LOAD_DELAY_MS);
            _springLoadTimer.Tick += OnSpringLoadTimerTick;
            _springLoadTimer.Start();
        }

        private void StopSpringLoadTimer()
        {
            if (_springLoadTimer != null)
            {
                _springLoadTimer.Stop();
                _springLoadTimer.Tick -= OnSpringLoadTimerTick;
                _springLoadTimer = null;
            }
            _springLoadTarget = null;
            _springLoadGrid = null;
        }

        private void OnSpringLoadTimerTick(object? sender, object e)
        {
            var folder = _springLoadTarget;
            StopSpringLoadTimer(); // One-shot: stop and clear state

            if (folder == null) return;

            // Navigate into the folder by selecting it in its parent column
            var explorer = ViewModel.ActiveExplorer;
            if (explorer != null)
            {
                foreach (var col in explorer.Columns)
                {
                    if (col.Children.Contains(folder))
                    {
                        col.SelectedChild = folder;
                        break;
                    }
                }
                Helpers.DebugLogger.Log($"[SpringLoad] Auto-opened folder: {folder.Name}");
            }
        }

        // =================================================================
        //  Drag & Drop: Column-level targets (drop into current folder)
        // =================================================================

        private void OnColumnDragOver(object sender, DragEventArgs e)
        {
            if (sender is not ListView listView || listView.DataContext is not FolderViewModel folderVm) return;
            if (!e.DataView.Contains(StandardDataFormats.Text) &&
                !e.DataView.Properties.ContainsKey("SourcePaths") &&
                !e.DataView.Contains(StandardDataFormats.StorageItems)) return;

            // Same-folder check: block Move, allow Copy (Ctrl)
            bool isSameFolder = false;
            if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths)
            {
                isSameFolder = srcPaths.All(p => System.IO.Path.GetDirectoryName(p)?.Equals(folderVm.Path, StringComparison.OrdinalIgnoreCase) == true);
            }

            bool isMove = ResolveDragDropOperation(e, folderVm.Path);

            if (isSameFolder && isMove)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                e.Handled = true;
                return;
            }

            e.AcceptedOperation = isMove ? DataPackageOperation.Move : DataPackageOperation.Copy;
            e.DragUIOverride.Caption = isMove ? $"Move to {folderVm.Name}" : $"Copy to {folderVm.Name}";
            e.DragUIOverride.IsCaptionVisible = true;
            e.Handled = true; // Prevent bubbling to PaneDragOver
        }

        private async void OnColumnDrop(object sender, DragEventArgs e)
        {
            if (sender is not ListView listView || listView.DataContext is not FolderViewModel folderVm) return;
            e.Handled = true; // Prevent bubbling to OnPaneDrop (duplicate execution)

            var paths = await ExtractDropPaths(e);
            if (paths.Count == 0) return;

            bool isMove = ResolveDragDropOperation(e, folderVm.Path);
            await HandleDropAsync(paths, folderVm.Path, isMove: isMove);
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

            // 외부 앱(Windows 탐색기 등)에서 드래그된 StorageItems 처리
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                return items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            }

            return new List<string>();
        }

        /// <summary>
        /// Resolves drag-drop operation based on modifier keys and drive comparison.
        /// Windows Explorer convention: same drive = Move, different drive = Copy.
        /// Shift forces Move, Ctrl forces Copy.
        /// </summary>
        private bool ResolveDragDropOperation(DragEventArgs e, string destFolder)
        {
            var shift = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            // Explicit modifier keys override default behavior
            if (shift) return true;   // Shift = force Move
            if (ctrl) return false;   // Ctrl = force Copy

            // Default: same drive root = Move, different drive = Copy
            if (e.DataView.Properties.TryGetValue("SourcePaths", out var srcObj) && srcObj is List<string> srcPaths && srcPaths.Count > 0)
            {
                var srcRoot = System.IO.Path.GetPathRoot(srcPaths[0]);
                var destRoot = System.IO.Path.GetPathRoot(destFolder);
                if (!string.IsNullOrEmpty(srcRoot) && !string.IsNullOrEmpty(destRoot))
                    return srcRoot.Equals(destRoot, StringComparison.OrdinalIgnoreCase);
            }

            return false; // fallback: Copy
        }

        private async System.Threading.Tasks.Task HandleDropAsync(List<string> sourcePaths, string destFolder, bool isMove)
        {
            // Validate: don't drop onto itself or into child
            sourcePaths = sourcePaths.Where(p =>
                !p.Equals(destFolder, StringComparison.OrdinalIgnoreCase) &&
                !destFolder.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            // Safety net: filter out same-folder Move (items already in destFolder)
            if (isMove)
            {
                sourcePaths = sourcePaths.Where(p =>
                    !string.Equals(System.IO.Path.GetDirectoryName(p), destFolder, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            if (sourcePaths.Count == 0) return;

            var router = App.Current.Services.GetRequiredService<FileSystemRouter>();
            IFileOperation op = isMove
                ? new MoveFileOperation(sourcePaths, destFolder, router)
                : new CopyFileOperation(sourcePaths, destFolder, router);

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
            // External drags (Windows Explorer etc.) won't have "SourcePane" property
            bool isInternalDrag = e.DataView.Properties.TryGetValue("SourcePane", out var sp) && sp is string s;
            var sourcePane = isInternalDrag ? (string)sp! : "";

            bool isLeftTarget = fe.Name == "LeftPaneContainer";
            string targetPane = isLeftTarget ? "Left" : "Right";

            var targetExplorer = isLeftTarget ? ViewModel.Explorer : ViewModel.RightExplorer;
            var destFolder = targetExplorer?.CurrentFolder?.Path ?? "";
            bool isMove = ResolveDragDropOperation(e, destFolder);

            // Same-pane drag: block Move (no-op), allow Copy (Ctrl)
            // Only applies to internal drags — external drops always allowed
            if (isInternalDrag && sourcePane == targetPane)
            {
                if (isMove)
                {
                    e.AcceptedOperation = DataPackageOperation.None;
                    e.Handled = true;
                    return;
                }
                // Same-pane Copy → allow (fall through to set operation below)
            }

            e.AcceptedOperation = isMove
                ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move
                : Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = isMove ? _loc.Get("Move") : _loc.Get("Copy");
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

            // External drags (Windows Explorer etc.) won't have "SourcePane" property
            bool isInternalDrag = e.DataView.Properties.TryGetValue("SourcePane", out var sp) && sp is string s;
            var sourcePane = isInternalDrag ? (string)sp! : "";

            bool isLeftTarget = fe.Name == "LeftPaneContainer";
            string targetPane = isLeftTarget ? "Left" : "Right";

            // Same-pane Move is blocked (only Copy allowed) — only for internal drags
            bool isMove = false;
            {
                var targetExplorer2 = isLeftTarget ? ViewModel.Explorer : ViewModel.RightExplorer;
                var destFolder2 = targetExplorer2?.CurrentFolder?.Path ?? "";
                isMove = ResolveDragDropOperation(e, destFolder2);
            }
            if (isInternalDrag && sourcePane == targetPane && isMove) return;

            // Hide overlay
            var overlay = isLeftTarget ? LeftDropOverlay : RightDropOverlay;
            overlay.Opacity = 0;

            var paths = await ExtractDropPaths(e);
            if (paths.Count == 0) return;

            // Destination = target pane's current folder
            var targetExplorer = isLeftTarget ? ViewModel.Explorer : ViewModel.RightExplorer;
            var destFolder = targetExplorer?.CurrentFolder?.Path;
            if (string.IsNullOrEmpty(destFolder)) return;

            // isMove already resolved above (same-pane Move was early-returned)
            await HandleDropAsync(paths, destFolder, isMove: isMove);
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
        //  Column Resize Grip Handlers (Miller Columns drag-to-resize)
        // =================================================================

        private void OnColumnResizeGripPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.Shapes.Rectangle rect)
            {
                rect.Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.3 };
                // Set resize cursor via InputSystemCursor (reliable in WinUI 3)
                SetGripCursor(rect, true);
            }
        }

        private void OnColumnResizeGripPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isResizingColumn && sender is Microsoft.UI.Xaml.Shapes.Rectangle rect)
            {
                rect.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                SetGripCursor(rect, false);
            }
        }

        private void OnColumnResizeGripPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.Shapes.Rectangle rect)
            {
                // Walk up to find the parent Grid that has the Width
                var parentGrid = VisualTreeHelper.GetParent(rect) as Grid;
                if (parentGrid == null) return;

                _isResizingColumn = true;
                _resizingColumnGrid = parentGrid;
                _resizeStartX = e.GetCurrentPoint(null).Position.X;
                _resizeStartWidth = parentGrid.Width;

                rect.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void OnColumnResizeGripPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizingColumn && _resizingColumnGrid != null)
            {
                double currentX = e.GetCurrentPoint(null).Position.X;
                double delta = currentX - _resizeStartX;
                double newWidth = Math.Max(150, _resizeStartWidth + delta);
                newWidth = Math.Min(600, newWidth); // max width cap
                _resizingColumnGrid.Width = newWidth;

                // Ctrl+drag: apply the same width to ALL columns simultaneously
                var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                           .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                if (ctrl)
                {
                    var control = GetActiveMillerColumnsControl();
                    var columns = ViewModel.ActiveExplorer.Columns;
                    for (int i = 0; i < columns.Count; i++)
                    {
                        var container = control.ContainerFromIndex(i) as ContentPresenter;
                        if (container == null) continue;
                        var grid = FindChild<Grid>(container);
                        if (grid != null && grid != _resizingColumnGrid)
                        {
                            grid.Width = newWidth;
                        }
                    }
                }

                // Force parent StackPanel and ScrollViewer to recalculate scroll extent
                if (VisualTreeHelper.GetParent(_resizingColumnGrid) is FrameworkElement parent)
                    parent.InvalidateMeasure();

                e.Handled = true;
            }
        }

        private void OnColumnResizeGripPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizingColumn)
            {
                var grid = _resizingColumnGrid;
                _isResizingColumn = false;
                _resizingColumnGrid = null;

                if (sender is Microsoft.UI.Xaml.Shapes.Rectangle rect)
                {
                    rect.ReleasePointerCapture(e.Pointer);
                    rect.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    SetGripCursor(rect, false);
                }

                // Final layout pass: invalidate ItemsControl → StackPanel → ScrollViewer
                if (grid != null)
                {
                    var control = GetActiveMillerColumnsControl();
                    control.InvalidateMeasure();
                    control.UpdateLayout();
                    var scrollViewer = GetActiveMillerScrollViewer();
                    scrollViewer.InvalidateMeasure();
                    scrollViewer.UpdateLayout();
                }

                e.Handled = true;
            }
        }

        /// <summary>
        /// Double-click on column resize grip: auto-fit column width to its content.
        /// Measures the widest item name in the column and resizes to fit.
        /// </summary>
        private void OnColumnResizeGripDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (sender is not Microsoft.UI.Xaml.Shapes.Rectangle rect) return;

            var parentGrid = VisualTreeHelper.GetParent(rect) as Grid;
            if (parentGrid == null) return;

            // Find the column index by locating this grid in the ItemsControl
            var control = GetActiveMillerColumnsControl();
            var columns = ViewModel.ActiveExplorer.Columns;
            int columnIndex = -1;

            for (int i = 0; i < columns.Count; i++)
            {
                var container = control.ContainerFromIndex(i) as ContentPresenter;
                if (container == null) continue;
                var grid = FindChild<Grid>(container);
                if (grid == parentGrid)
                {
                    columnIndex = i;
                    break;
                }
            }

            if (columnIndex < 0 || columnIndex >= columns.Count) return;

            double fittedWidth = MeasureColumnContentWidth(columns[columnIndex]);
            parentGrid.Width = fittedWidth;

            // Check if Ctrl is held: apply to all columns
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (ctrl)
            {
                ApplyWidthToAllColumns(fittedWidth);
            }

            // Invalidate layout
            control.InvalidateMeasure();
            control.UpdateLayout();
            var scrollViewer = GetActiveMillerScrollViewer();
            scrollViewer.InvalidateMeasure();
            scrollViewer.UpdateLayout();

            e.Handled = true;
        }

        /// <summary>
        /// Measure the ideal width for a column based on its content.
        /// Estimates text width from item display names plus icon/padding/chevron.
        /// Returns clamped width between 120 and 600 pixels.
        /// </summary>
        private double MeasureColumnContentWidth(FolderViewModel column)
        {
            const double iconWidth = 16;
            const double iconMargin = 12;
            const double itemPadding = 12 * 2;   // left + right padding on item grid
            const double chevronWidth = 14;       // chevron icon + opacity area
            const double countBadgeExtra = 30;    // child count text badge
            const double gripWidth = 4;           // resize grip
            const double scrollBarBuffer = 8;     // scrollbar safety margin
            const double minWidth = 120;
            const double maxWidth = 600;

            double maxItemWidth = 0;

            foreach (var child in column.Children)
            {
                string displayName = child.DisplayName;
                // Measure text using a TextBlock for accurate font metrics
                double textWidth = MeasureTextWidth(displayName, 14); // default font size 14

                double itemWidth = itemPadding + iconWidth + iconMargin + textWidth;

                // Folders have count badge + chevron
                if (child is FolderViewModel folderChild)
                {
                    itemWidth += countBadgeExtra + chevronWidth;
                }

                if (itemWidth > maxItemWidth)
                    maxItemWidth = itemWidth;
            }

            // Add grip width and buffer
            double totalWidth = maxItemWidth + gripWidth + scrollBarBuffer;

            return Math.Clamp(totalWidth, minWidth, maxWidth);
        }

        /// <summary>
        /// Measure the pixel width of a string using WinUI text rendering.
        /// </summary>
        private static double MeasureTextWidth(string text, double fontSize)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            var tb = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                TextWrapping = TextWrapping.NoWrap
            };
            tb.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            return tb.DesiredSize.Width;
        }

        /// <summary>
        /// Apply a given width to all column grids in the active Miller Columns control.
        /// Used by Ctrl+drag and Ctrl+Shift+= shortcut.
        /// </summary>
        private void ApplyWidthToAllColumns(double width)
        {
            width = Math.Clamp(width, 150, 600);

            var control = GetActiveMillerColumnsControl();
            var columns = ViewModel.ActiveExplorer.Columns;

            for (int i = 0; i < columns.Count; i++)
            {
                var container = control.ContainerFromIndex(i) as ContentPresenter;
                if (container == null) continue;
                var grid = FindChild<Grid>(container);
                if (grid != null)
                {
                    grid.Width = width;
                }
            }

            // Invalidate layout
            if (VisualTreeHelper.GetParent(control) is FrameworkElement parent)
                parent.InvalidateMeasure();
        }

        /// <summary>
        /// Auto-fit all column widths to their individual content.
        /// Each column gets its own optimal width based on the widest item it contains.
        /// </summary>
        private void AutoFitAllColumns()
        {
            var control = GetActiveMillerColumnsControl();
            var columns = ViewModel.ActiveExplorer.Columns;

            for (int i = 0; i < columns.Count; i++)
            {
                double fittedWidth = MeasureColumnContentWidth(columns[i]);
                var container = control.ContainerFromIndex(i) as ContentPresenter;
                if (container == null) continue;
                var grid = FindChild<Grid>(container);
                if (grid != null)
                {
                    grid.Width = fittedWidth;
                }
            }

            // Invalidate layout
            control.InvalidateMeasure();
            control.UpdateLayout();
            var scrollViewer = GetActiveMillerScrollViewer();
            scrollViewer.InvalidateMeasure();
            scrollViewer.UpdateLayout();
        }

        /// <summary>
        /// Set cursor on resize grip element using WinUI 3 ProtectedCursor (via reflection).
        /// This is more reliable than Win32 SetCursor which gets overridden by WinUI message loop.
        /// </summary>
        private static void SetGripCursor(UIElement element, bool resize)
        {
            try
            {
                var cursor = resize
                    ? Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast)
                    : Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
                // ProtectedCursor is protected; use reflection to bypass
                typeof(UIElement).GetProperty("ProtectedCursor",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(element, cursor);
            }
            catch
            {
                // Fallback: ignore on older platforms
            }
        }

        // =================================================================
        //  Global Keyboard (Ctrl 조합, F키 등)
        //  handledEventsToo: true로 등록하여 항상 수신
        // =================================================================

        private void OnGlobalKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // 이름 변경 중이면 F2(선택 영역 순환)만 허용, 나머지 글로벌 단축키 무시
            var selected = GetCurrentSelected();
            if (selected != null && selected.IsRenaming && e.Key != Windows.System.VirtualKey.F2) return;

            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
                      .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            // Help 오버레이: 열려있으면 Esc/아무 키로 닫기
            if (_isHelpOpen)
            {
                _isHelpOpen = false;
                HelpOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }

            // F1 또는 Shift+? (OEM_2 = /) — Help 오버레이 토글 (어디서든 동작)
            if (e.Key == Windows.System.VirtualKey.F1 ||
                (shift && !ctrl && !alt && e.Key == (Windows.System.VirtualKey)191)) // VK_OEM_2 = /? key
            {
                ToggleHelpOverlay();
                e.Handled = true;
                return;
            }

            // Settings/Home 모드: 파일 조작 단축키 차단, 뷰 전환/탭/Escape만 허용
            if (ViewModel.CurrentViewMode == ViewMode.Settings || ViewModel.CurrentViewMode == ViewMode.Home)
            {
                if (e.Key == Windows.System.VirtualKey.Escape && ViewModel.CurrentViewMode == ViewMode.Settings)
                {
                    CloseCurrentSettingsTab();
                    e.Handled = true;
                    return;
                }
                if (ctrl)
                {
                    switch (e.Key)
                    {
                        case Windows.System.VirtualKey.Number1: // Ctrl+1: Miller
                        case Windows.System.VirtualKey.Number2: // Ctrl+2: Details
                        case Windows.System.VirtualKey.Number3: // Ctrl+3: List
                        case Windows.System.VirtualKey.Number4: // Ctrl+4: Icons
                        case (Windows.System.VirtualKey)188:    // Ctrl+,: Settings
                        case (Windows.System.VirtualKey)192:    // Ctrl+`: Terminal (VK_OEM_3)
                        case (Windows.System.VirtualKey)222:    // Ctrl+': Terminal (VK_OEM_7)
                        case Windows.System.VirtualKey.T:       // Ctrl+T: New Tab
                        case Windows.System.VirtualKey.W:       // Ctrl+W: Close Tab
                        case Windows.System.VirtualKey.L:       // Ctrl+L: Address Bar
                        case Windows.System.VirtualKey.N:       // Ctrl+N: New Window
                            break; // 허용 — fall through to main handler
                        default:
                            // 한국어 키보드: backtick(41), single quote(40), comma(51) 허용
                            if (e.KeyStatus.ScanCode == 41 || e.KeyStatus.ScanCode == 40 || e.KeyStatus.ScanCode == 51) break;
                            return; // 그 외 Ctrl 단축키 차단
                    }
                }
                else if (!alt)
                {
                    return; // Ctrl/Alt 없는 키(Delete, F2, F5 등) 차단
                }
                // Alt 키 조합(Alt+Left/Right 등)은 허용
            }

            // Alt+Left/Right: Back/Forward navigation (highest priority)
            // Alt+Enter: Show Properties dialog
            if (alt && !ctrl && !shift)
            {
                switch (e.Key)
                {
                    case Windows.System.VirtualKey.Left:
                        _ = ViewModel.GoBackAsync().ContinueWith(_ =>
                            DispatcherQueue.TryEnqueue(() => FocusLastColumnAfterNavigation()),
                            System.Threading.Tasks.TaskScheduler.Default);
                        e.Handled = true;
                        return;

                    case Windows.System.VirtualKey.Right:
                        _ = ViewModel.GoForwardAsync().ContinueWith(_ =>
                            DispatcherQueue.TryEnqueue(() => FocusLastColumnAfterNavigation()),
                            System.Threading.Tasks.TaskScheduler.Default);
                        e.Handled = true;
                        return;

                    case Windows.System.VirtualKey.Enter:
                        HandleShowProperties();
                        e.Handled = true;
                        return;
                }
            }

            if (ctrl)
            {
                Helpers.DebugLogger.Log($"[Keyboard] Ctrl+Key: Key={(int)e.Key} ({e.Key}), OriginalKey={(int)e.OriginalKey} ({e.OriginalKey}), ScanCode={e.KeyStatus.ScanCode}");

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

                    case Windows.System.VirtualKey.T:
                        ViewModel.AddNewTab();
                        if (ViewModel.ActiveTab != null)
                        {
                            CreateMillerPanelForTab(ViewModel.ActiveTab);
                            SwitchMillerPanel(ViewModel.ActiveTab.Id);
                        }
                        ResubscribeLeftExplorer();
                        UpdateViewModeVisibility();
                        FocusActiveView();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.W:
                        if (ViewModel.ActiveTab?.ViewMode == ViewMode.Settings)
                        {
                            CloseCurrentSettingsTab();
                        }
                        else
                        {
                            var closingTab = ViewModel.ActiveTab;
                            if (closingTab != null) RemoveMillerPanel(closingTab.Id);
                            ViewModel.CloseTab(ViewModel.ActiveTabIndex);
                            if (ViewModel.ActiveTab != null)
                                SwitchMillerPanel(ViewModel.ActiveTab.Id);
                            ResubscribeLeftExplorer();
                            UpdateViewModeVisibility();
                            FocusActiveView();
                        }
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.L:
                        if (ViewModel.CurrentViewMode != ViewMode.Home)
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
                        else
                        {
                            OpenNewWindow();
                            e.Handled = true;
                        }
                        break;

                    case Windows.System.VirtualKey.A:
                        if (shift)
                        {
                            // Ctrl+Shift+A: Select None
                            HandleSelectNone();
                        }
                        else
                        {
                            HandleSelectAll();
                        }
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.I:
                        // Ctrl+I: Invert Selection
                        HandleInvertSelection();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.D:
                        // Ctrl+D: Duplicate selected file/folder
                        HandleDuplicateFile();
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

                    case (Windows.System.VirtualKey)192: // VK_OEM_3 = Ctrl+` (backtick)
                    case (Windows.System.VirtualKey)222: // VK_OEM_7 = Ctrl+' (single quote)
                        // Ctrl+` or Ctrl+': Open terminal
                        HandleOpenTerminal();
                        e.Handled = true;
                        break;

                    case (Windows.System.VirtualKey)188: // VK_OEM_COMMA
                        // Ctrl+,: Settings (별도 탭으로 열기)
                        OpenSettingsTab();
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
                        // Ctrl+3: List
                        ViewModel.SwitchViewMode(Models.ViewMode.List);
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Number4:
                        // Ctrl+4: Icon (마지막 Icon 크기)
                        ViewModel.SwitchViewMode(ViewModel.CurrentIconSize);
                        GetActiveIconView()?.UpdateIconSize(ViewModel.CurrentIconSize);
                        e.Handled = true;
                        break;

                    case (Windows.System.VirtualKey)187: // VK_OEM_PLUS = =/+ key
                        if (shift)
                        {
                            // Ctrl+Shift+=: Equalize all columns to the same width (220 default)
                            if (ViewModel.CurrentViewMode == Models.ViewMode.MillerColumns)
                            {
                                ApplyWidthToAllColumns(ColumnWidth);
                                var eqCtl = GetActiveMillerColumnsControl();
                                eqCtl.InvalidateMeasure();
                                eqCtl.UpdateLayout();
                                GetActiveMillerScrollViewer().InvalidateMeasure();
                                ViewModel.ShowToast("All columns equalized to default width");
                            }
                            e.Handled = true;
                        }
                        break;

                    case (Windows.System.VirtualKey)189: // VK_OEM_MINUS = -/_ key
                        if (shift)
                        {
                            // Ctrl+Shift+-: Auto-fit all columns to their content
                            if (ViewModel.CurrentViewMode == Models.ViewMode.MillerColumns)
                            {
                                AutoFitAllColumns();
                                ViewModel.ShowToast("All columns auto-fitted to content");
                            }
                            e.Handled = true;
                        }
                        break;

                    default:
                        // 한국어 키보드 대응: VK_OEM 코드가 다른 VirtualKey로 매핑될 수 있음
                        // 물리 키 scan code로 판별
                        if (e.KeyStatus.ScanCode == 41 || e.KeyStatus.ScanCode == 40) // backtick(41) or single quote(40)
                        {
                            HandleOpenTerminal();
                            e.Handled = true;
                        }
                        else if (e.KeyStatus.ScanCode == 51) // comma 위치
                        {
                            OpenSettingsTab();
                            e.Handled = true;
                        }
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
        //  Mouse Back/Forward Buttons (XButton1/XButton2)
        // =================================================================

        private void OnGlobalPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var properties = e.GetCurrentPoint(this.Content).Properties;
            if (properties.IsXButton1Pressed)
            {
                // Mouse Back button (XButton1)
                _ = ViewModel.GoBackAsync().ContinueWith(_ =>
                    DispatcherQueue.TryEnqueue(() => FocusLastColumnAfterNavigation()),
                    System.Threading.Tasks.TaskScheduler.Default);
                e.Handled = true;
            }
            else if (properties.IsXButton2Pressed)
            {
                // Mouse Forward button (XButton2)
                _ = ViewModel.GoForwardAsync().ContinueWith(_ =>
                    DispatcherQueue.TryEnqueue(() => FocusLastColumnAfterNavigation()),
                    System.Threading.Tasks.TaskScheduler.Default);
                e.Handled = true;
            }
            else if (properties.IsLeftButtonPressed)
            {
                // 좌클릭: 빈 영역 클릭 시에도 진행 중인 리네임 취소
                // (SelectionChanged/GotFocus는 빈 영역에서 발생하지 않으므로 여기서 보완)
                CancelAnyActiveRename();
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
        //  Select None (Ctrl+Shift+A)
        // =================================================================

        private void HandleSelectNone()
        {
            var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

            if (viewMode == ViewMode.MillerColumns)
            {
                int activeIndex = GetActiveColumnIndex();
                if (activeIndex < 0) activeIndex = ViewModel.ActiveExplorer.Columns.Count - 1;
                if (activeIndex < 0) return;

                var listView = GetListViewForColumn(activeIndex);
                if (listView != null)
                {
                    listView.SelectedItems.Clear();
                    // Also clear the ViewModel selection
                    var columns = ViewModel.ActiveExplorer.Columns;
                    if (activeIndex < columns.Count)
                    {
                        columns[activeIndex].SelectedChild = null;
                        columns[activeIndex].SelectedItems.Clear();
                    }
                }
            }
        }

        // =================================================================
        //  Invert Selection (Ctrl+I)
        // =================================================================

        private void HandleInvertSelection()
        {
            var viewMode = (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;

            if (viewMode == ViewMode.MillerColumns)
            {
                int activeIndex = GetActiveColumnIndex();
                if (activeIndex < 0) activeIndex = ViewModel.ActiveExplorer.Columns.Count - 1;
                if (activeIndex < 0) return;

                var listView = GetListViewForColumn(activeIndex);
                if (listView == null) return;

                var columns = ViewModel.ActiveExplorer.Columns;
                if (activeIndex >= columns.Count) return;

                var column = columns[activeIndex];
                var allItems = column.Children.ToList();

                // Collect currently selected indices
                var selectedIndices = new HashSet<int>();
                foreach (var item in listView.SelectedItems)
                {
                    int idx = allItems.IndexOf(item as FileSystemViewModel);
                    if (idx >= 0) selectedIndices.Add(idx);
                }

                // Clear and invert
                _isSyncingSelection = true;
                try
                {
                    listView.SelectedItems.Clear();
                    for (int i = 0; i < allItems.Count; i++)
                    {
                        if (!selectedIndices.Contains(i))
                        {
                            listView.SelectedItems.Add(allItems[i]);
                        }
                    }
                }
                finally
                {
                    _isSyncingSelection = false;
                }

                ViewModel.UpdateStatusBar();
            }
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
            UpdateToolbarButtonStates();
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
            UpdateToolbarButtonStates();
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

            var router = App.Current.Services.GetRequiredService<FileSystemRouter>();
            Span.Services.FileOperations.IFileOperation op = _isCutOperation
                ? new Span.Services.FileOperations.MoveFileOperation(new List<string>(_clipboardPaths), destDir, router)
                : new Span.Services.FileOperations.CopyFileOperation(new List<string>(_clipboardPaths), destDir, router);

            await ViewModel.ExecuteFileOperationAsync(op, activeIndex);

            if (_isCutOperation) _clipboardPaths.Clear();
            UpdateToolbarButtonStates();
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
            bool isRemote = Services.FileSystemRouter.IsRemotePath(currentFolder.Path);

            string newPath;
            if (isRemote)
            {
                // 원격 경로: URI 호환 경로 조합 (Path.Combine 사용 불가)
                newPath = currentFolder.Path.TrimEnd('/') + "/" + baseName;
                // 원격 폴더 충돌 검사 스킵 — 서버에서 자동 처리
            }
            else
            {
                newPath = System.IO.Path.Combine(currentFolder.Path, baseName);
                int count = 1;
                while (System.IO.Directory.Exists(newPath))
                {
                    newPath = System.IO.Path.Combine(currentFolder.Path, $"{baseName} ({count})");
                    count++;
                }
            }

            var router = App.Current.Services.GetRequiredService<Services.FileSystemRouter>();
            var op = new Span.Services.FileOperations.NewFolderOperation(newPath, router);
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

            // 다중 선택 → 배치 이름 변경 다이얼로그
            if (currentColumn.HasMultiSelection)
            {
                _ = ShowBatchRenameDialogAsync(currentColumn);
                return;
            }

            var selected = currentColumn.SelectedChild;

            // 선택된 항목이 없으면 첫 번째 항목 선택
            if (selected == null && currentColumn.Children.Count > 0)
            {
                selected = currentColumn.Children[0];
                currentColumn.SelectedChild = selected;
            }

            if (selected == null) return;

            // F2 cycling: if already renaming the same item, advance selection cycle
            var itemPath = (selected as FolderViewModel)?.Path ?? (selected as FileViewModel)?.Path;
            if (selected.IsRenaming && itemPath == _renameTargetPath)
            {
                // Cycle: 0(name) → 1(all) → 2(extension) → 0(name) ...
                _renameSelectionCycle = (_renameSelectionCycle + 1) % 3;
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (_isClosed) return;
                    FocusRenameTextBox(activeIndex);
                });
                return;
            }

            // First F2 press: start rename with name-only selection
            _renameSelectionCycle = 0;
            _renameTargetPath = itemPath;
            selected.BeginRename();

            // TextBox에 포커스
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed) return;
                FocusRenameTextBox(activeIndex);
            });
        }

        /// <summary>
        /// 다중 선택된 항목의 배치 이름 변경 다이얼로그 표시.
        /// </summary>
        private async System.Threading.Tasks.Task ShowBatchRenameDialogAsync(FolderViewModel currentColumn)
        {
            var items = currentColumn.GetSelectedItemsList();
            if (items.Count < 2) return;

            var dialog = new Views.Dialogs.BatchRenameDialog(items);
            dialog.XamlRoot = this.Content.XamlRoot;

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var renameList = dialog.GetRenameList();
            if (renameList.Count == 0) return;

            var op = new Services.FileOperations.BatchRenameOperation(renameList);
            await ViewModel.ExecuteFileOperationAsync(op);
        }

        /// <summary>
        /// 인라인 rename TextBox에 포커스를 맞추고 선택 영역 적용.
        /// Windows Explorer 방식 F2 cycling: 파일명만 → 전체 → 확장자만 → 파일명만 ...
        /// 폴더이거나 확장자가 없으면 항상 전체 선택.
        /// </summary>
        private void FocusRenameTextBox(int columnIndex)
        {
            var listView = GetListViewForColumn(columnIndex);
            if (listView == null)
            {
                // ListView를 아직 못 찾으면 한 번 더 지연 재시도
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (_isClosed) return;
                    var retryList = GetListViewForColumn(columnIndex);
                    if (retryList != null) FocusRenameTextBoxCore(retryList, columnIndex);
                });
                return;
            }

            FocusRenameTextBoxCore(listView, columnIndex);
        }

        private void FocusRenameTextBoxCore(ListView listView, int columnIndex)
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            if (columnIndex >= columns.Count) return;

            var column = columns[columnIndex];
            if (column.SelectedChild == null) return;

            int idx = column.Children.IndexOf(column.SelectedChild);
            if (idx < 0) return;

            var container = listView.ContainerFromIndex(idx) as UIElement;
            if (container == null)
            {
                // 아이템이 가상화되어 아직 로드 안 된 경우 ScrollIntoView 후 재시도
                listView.ScrollIntoView(column.SelectedChild);
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (_isClosed) return;
                    var retryContainer = listView.ContainerFromIndex(idx) as UIElement;
                    if (retryContainer != null)
                    {
                        var tb = FindChild<TextBox>(retryContainer as DependencyObject);
                        if (tb != null) ApplyRenameSelection(tb, column.SelectedChild is FolderViewModel);
                    }
                });
                return;
            }

            var textBox = FindChild<TextBox>(container as DependencyObject);
            if (textBox != null)
            {
                ApplyRenameSelection(textBox, column.SelectedChild is FolderViewModel);
            }
        }

        /// <summary>
        /// TextBox에 포커스를 주고 F2 cycling에 따른 선택 영역을 적용.
        /// WinUI 3에서 Focus()가 선택 영역을 리셋하므로, Select()를 DispatcherQueue로 지연 실행.
        /// </summary>
        private void ApplyRenameSelection(TextBox textBox, bool isFolder)
        {
            textBox.Focus(FocusState.Keyboard);

            // Focus()가 선택 영역을 리셋하므로 DispatcherQueue로 지연 실행
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                if (_isClosed) return;
                if (!isFolder && !string.IsNullOrEmpty(textBox.Text))
                {
                    int dotIndex = textBox.Text.LastIndexOf('.');
                    if (dotIndex > 0)
                    {
                        // F2 cycling: 0=name only, 1=all, 2=extension only
                        switch (_renameSelectionCycle)
                        {
                            case 0: // Name only (exclude extension)
                                textBox.Select(0, dotIndex);
                                break;
                            case 1: // All (including extension)
                                textBox.SelectAll();
                                break;
                            case 2: // Extension only
                                textBox.Select(dotIndex + 1, textBox.Text.Length - dotIndex - 1);
                                break;
                        }
                    }
                    else
                    {
                        textBox.SelectAll();
                    }
                }
                else
                {
                    textBox.SelectAll();
                }
            });
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
                _renameTargetPath = null; // Reset F2 cycle state
                e.Handled = true;
                FocusSelectedItem();
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                vm.CancelRename();
                _justFinishedRename = true;
                _renameTargetPath = null; // Reset F2 cycle state
                e.Handled = true;
                FocusSelectedItem();
            }
        }

        private void OnRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            var vm = textBox.DataContext as FileSystemViewModel;
            if (vm == null) return;

            // 포커스 잃으면 취소 (ESC와 동일)
            // IsRenaming이 이미 false여도 정리 작업은 수행
            if (vm.IsRenaming)
            {
                vm.CancelRename();
            }
            _justFinishedRename = true;
            _renameTargetPath = null; // Reset F2 cycle state
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
                // await 후 상태 재검증 — dialog 표시 중 탭 전환/창 닫기 가능
                if (_isClosed) return;
            }

            // await 후 컬럼 유효성 재검증
            var freshColumns = ViewModel.ActiveExplorer.Columns;
            if (activeIndex >= freshColumns.Count) return;
            if (!ReferenceEquals(currentColumn, freshColumns[activeIndex])) return;

            var paths = selectedItems.Select(i => i.Path).ToList();
            Helpers.DebugLogger.Log($"[HandleDelete] Dialog confirmed. Deleting {paths.Count} item(s), ActiveIndex: {activeIndex}");
            Helpers.DebugLogger.Log($"[HandleDelete] Columns before delete: {string.Join(" > ", ViewModel.ActiveExplorer.Columns.Select(c => c.Name))}");

            // Execute delete operation (send to Recycle Bin)
            var router = App.Current.Services.GetRequiredService<Services.FileSystemRouter>();
            var operation = new DeleteFileOperation(paths, permanent: false, router: router);
            Helpers.DebugLogger.Log($"[HandleDelete] Calling ExecuteFileOperationAsync with targetColumnIndex={activeIndex}");
            await ViewModel.ExecuteFileOperationAsync(operation, activeIndex);
            if (_isClosed) return;

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

            // await 후 상태 재검증
            if (_isClosed) return;
            var freshColumns = ViewModel.ActiveExplorer.Columns;
            if (activeIndex >= freshColumns.Count) return;
            if (!ReferenceEquals(currentColumn, freshColumns[activeIndex])) return;

            // Execute permanent delete operation
            var paths = selectedItems.Select(i => i.Path).ToList();
            var router = App.Current.Services.GetRequiredService<Services.FileSystemRouter>();
            var operation = new DeleteFileOperation(paths, permanent: true, router: router);
            await ViewModel.ExecuteFileOperationAsync(operation, activeIndex);
            if (_isClosed) return;

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
                // Clear search and restore original column contents if filtered
                if (_isSearchFiltered)
                {
                    RestoreSearchFilter();
                }
                SearchBox.Text = string.Empty;
                GetActiveMillerColumnsControl().Focus(FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Enter)
            {
                string queryText = SearchBox.Text.Trim();
                if (string.IsNullOrEmpty(queryText)) return;

                var columns = ViewModel.ActiveExplorer.Columns;
                int activeIndex = GetActiveColumnIndex();
                if (activeIndex < 0) activeIndex = columns.Count - 1;
                if (activeIndex < 0 || activeIndex >= columns.Count) return;

                var column = columns[activeIndex];

                // Parse the query using Advanced Query Syntax
                var query = Helpers.SearchQueryParser.Parse(queryText);

                if (query.IsEmpty) return;

                // Check if query has advanced filters (kind:, size:, date:, ext:)
                bool hasAdvancedFilters = query.KindFilter.HasValue ||
                                          query.SizeFilter.HasValue ||
                                          query.DateFilter.HasValue ||
                                          !string.IsNullOrEmpty(query.ExtensionFilter);

                if (hasAdvancedFilters)
                {
                    // Advanced search: filter the column's children in-place
                    ApplySearchFilter(column, query, activeIndex);
                }
                else
                {
                    // Simple name search: find first match and select it (existing behavior)
                    var source = _isSearchFiltered && _searchOriginalChildren != null
                        ? _searchOriginalChildren
                        : column.Children.ToList();

                    var match = Helpers.SearchFilter.FindFirst(query, source);
                    if (match != null)
                    {
                        // If filtered, restore first so we can select the match
                        if (_isSearchFiltered)
                        {
                            RestoreSearchFilter();
                        }
                        column.SelectedChild = match;
                        var listView = GetListViewForColumn(activeIndex);
                        listView?.ScrollIntoView(match);
                    }
                }

                e.Handled = true;
            }
        }

        // ── Search Filter State ──
        private bool _isSearchFiltered = false;
        private List<FileSystemViewModel>? _searchOriginalChildren = null;
        private int _searchFilteredColumnIndex = -1;

        /// <summary>
        /// Apply advanced search filter: replace column children with filtered results.
        /// Stores original children for restoration on Escape.
        /// </summary>
        private void ApplySearchFilter(FolderViewModel column, SearchQuery query, int columnIndex)
        {
            // Save original children if not already saved (allow re-filtering)
            var source = _isSearchFiltered && _searchOriginalChildren != null
                ? _searchOriginalChildren
                : column.Children.ToList();

            if (!_isSearchFiltered)
            {
                _searchOriginalChildren = column.Children.ToList();
                _searchFilteredColumnIndex = columnIndex;
            }

            var filtered = Helpers.SearchFilter.Apply(query, source);

            column.Children.Clear();
            foreach (var item in filtered)
                column.Children.Add(item);

            _isSearchFiltered = true;

            // Update status bar with search result count
            ViewModel.StatusItemCountText = $"Search: {filtered.Count} result{(filtered.Count != 1 ? "s" : "")}";
            if (filtered.Count == 0)
            {
                ViewModel.StatusSelectionText = "Esc to clear";
            }
        }

        /// <summary>
        /// Restore original column children after search filter is cleared.
        /// </summary>
        private void RestoreSearchFilter()
        {
            if (!_isSearchFiltered || _searchOriginalChildren == null) return;

            var columns = ViewModel.ActiveExplorer.Columns;
            if (_searchFilteredColumnIndex >= 0 && _searchFilteredColumnIndex < columns.Count)
            {
                var column = columns[_searchFilteredColumnIndex];
                column.Children.Clear();
                foreach (var item in _searchOriginalChildren)
                    column.Children.Add(item);
            }

            _isSearchFiltered = false;
            _searchOriginalChildren = null;
            _searchFilteredColumnIndex = -1;
        }

        // =================================================================
        //  P1: Focus Tracking (Active Column)
        // =================================================================

        private void OnMillerColumnGotFocus(object sender, RoutedEventArgs e)
        {
            // 리네임 TextBox로 포커스가 간 경우는 제외 (GotFocus 버블링)
            if (e.OriginalSource is not TextBox)
                CancelAnyActiveRename();

            // Clear any active search filter when user focuses a different column
            if (_isSearchFiltered)
            {
                RestoreSearchFilter();
                ViewModel.UpdateStatusBar();
            }

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

            // 다른 항목 선택 시 진행 중인 리네임 취소
            CancelAnyActiveRename();

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

                    // Update status bar selection count
                    ViewModel.UpdateStatusBar();

                    // Update toolbar button enabled states
                    UpdateToolbarButtonStates();
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
                    // In double-click mode, navigate into folder as next column (preserve existing columns)
                    var explorer = ViewModel.ActiveExplorer;
                    explorer.NavigateIntoFolder(folder, folderVm);
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
            var control = GetActiveMillerColumnsControl();

            // Calculate actual column position by summing rendered widths (handles resized columns)
            double columnLeft = 0;
            double columnWidth = ColumnWidth;
            for (int i = 0; i <= columnIndex; i++)
            {
                var container = control.ContainerFromIndex(i) as UIElement;
                if (container is FrameworkElement fe && fe.ActualWidth > 0)
                {
                    if (i < columnIndex)
                        columnLeft += fe.ActualWidth;
                    else
                        columnWidth = fe.ActualWidth;
                }
                else
                {
                    if (i < columnIndex)
                        columnLeft += ColumnWidth;
                }
            }

            double columnRight = columnLeft + columnWidth;
            double viewportLeft = scrollViewer.HorizontalOffset;
            double viewportRight = viewportLeft + scrollViewer.ViewportWidth;

            if (columnLeft < viewportLeft)
                scrollViewer.ChangeView(columnLeft, null, null, false); // false = enable smooth animation
            else if (columnRight > viewportRight)
                scrollViewer.ChangeView(columnRight - scrollViewer.ViewportWidth, null, null, false); // false = enable smooth animation
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
            if (_isClosed) return;

            // Task.Delay(50) 대신 DispatcherQueue Low 우선순위로 XAML 레이아웃 완료 대기
            // — 50ms 고정 지연을 제거하여 탭 전환 속도 개선
            var tcs = new System.Threading.Tasks.TaskCompletionSource();
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => tcs.TrySetResult()))
            {
                return; // 큐가 종료됨 — 창이 닫히는 중
            }
            await tcs.Task;
            if (_isClosed) return;

            var listView = GetListViewForColumn(columnIndex);
            if (listView == null) return;

            var columns = ViewModel.ActiveExplorer.Columns;
            if (columnIndex >= columns.Count) return;

            var column = columns[columnIndex];

            // 포커스용 자동 선택 시 auto-navigation 일시 억제
            // — 선택이 없을 때 첫 항목을 선택하면 FolderVm_PropertyChanged가 발동하여
            //   하위 폴더가 자동으로 열리는 부작용 방지
            if (column.SelectedChild == null && column.Children.Count > 0)
            {
                var explorer = ViewModel.ActiveExplorer;
                var savedAutoNav = explorer.EnableAutoNavigation;
                explorer.EnableAutoNavigation = false;
                column.SelectedChild = column.Children[0];
                explorer.EnableAutoNavigation = savedAutoNav;
            }

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
        private void OnBreadcrumbItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
        {
            if (args.Item is Models.PathSegment segment)
            {
                _ = ViewModel.ActiveExplorer.NavigateToPath(segment.FullPath);
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
            // 홈 모드에서는 편집 모드 불필요
            if (ViewModel.CurrentViewMode == ViewMode.Home) return;

            // BreadcrumbBar 항목 클릭은 ItemClicked에서 처리하므로
            // 빈 공간 클릭만 편집 모드로 전환
            var element = e.OriginalSource as DependencyObject;
            while (element != null && element != AddressBarContainer)
            {
                if (element is Button || element is ItemsRepeater) return;
                element = VisualTreeHelper.GetParent(element);
            }

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
        /// Navigate back in history (Back button clicked - single mode).
        /// </summary>
        private async void OnGoBackClick(object sender, RoutedEventArgs e)
        {
            await ViewModel.GoBackAsync();
            FocusLastColumnAfterNavigation();
            Helpers.DebugLogger.Log("[MainWindow] Back button clicked");
        }

        /// <summary>
        /// Navigate forward in history (Forward button clicked - single mode).
        /// </summary>
        private async void OnGoForwardClick(object sender, RoutedEventArgs e)
        {
            await ViewModel.GoForwardAsync();
            FocusLastColumnAfterNavigation();
            Helpers.DebugLogger.Log("[MainWindow] Forward button clicked");
        }

        /// <summary>
        /// Navigate back in history (Back button clicked - split pane mode).
        /// </summary>
        private async void OnPaneGoBackClick(object sender, RoutedEventArgs e)
        {
            var tag = (sender as FrameworkElement)?.Tag as string;
            var explorer = (tag == "Right") ? ViewModel.RightExplorer : ViewModel.ActiveExplorer;
            if (explorer != null && explorer.CanGoBack)
            {
                await explorer.GoBack();
                ViewModel.SyncNavigationHistoryState();
            }
            FocusLastColumnAfterNavigation();
            Helpers.DebugLogger.Log($"[MainWindow] Pane back button clicked (pane: {tag})");
        }

        /// <summary>
        /// Navigate forward in history (Forward button clicked - split pane mode).
        /// </summary>
        private async void OnPaneGoForwardClick(object sender, RoutedEventArgs e)
        {
            var tag = (sender as FrameworkElement)?.Tag as string;
            var explorer = (tag == "Right") ? ViewModel.RightExplorer : ViewModel.ActiveExplorer;
            if (explorer != null && explorer.CanGoForward)
            {
                await explorer.GoForward();
                ViewModel.SyncNavigationHistoryState();
            }
            FocusLastColumnAfterNavigation();
            Helpers.DebugLogger.Log($"[MainWindow] Pane forward button clicked (pane: {tag})");
        }

        // =================================================================
        //  Back/Forward History Dropdown (right-click on nav buttons)
        // =================================================================

        /// <summary>
        /// Right-click on Back button (single mode) shows history dropdown.
        /// </summary>
        private void OnBackButtonRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            ShowHistoryDropdown(sender as FrameworkElement, isBack: true, ViewModel.ActiveExplorer);
            e.Handled = true;
        }

        /// <summary>
        /// Right-click on Forward button (single mode) shows history dropdown.
        /// </summary>
        private void OnForwardButtonRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            ShowHistoryDropdown(sender as FrameworkElement, isBack: false, ViewModel.ActiveExplorer);
            e.Handled = true;
        }

        /// <summary>
        /// Right-click on Back button (split pane mode) shows history dropdown.
        /// </summary>
        private void OnPaneBackButtonRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            var tag = (sender as FrameworkElement)?.Tag as string;
            var explorer = (tag == "Right") ? ViewModel.RightExplorer : ViewModel.ActiveExplorer;
            ShowHistoryDropdown(sender as FrameworkElement, isBack: true, explorer);
            e.Handled = true;
        }

        /// <summary>
        /// Right-click on Forward button (split pane mode) shows history dropdown.
        /// </summary>
        private void OnPaneForwardButtonRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            var tag = (sender as FrameworkElement)?.Tag as string;
            var explorer = (tag == "Right") ? ViewModel.RightExplorer : ViewModel.ActiveExplorer;
            ShowHistoryDropdown(sender as FrameworkElement, isBack: false, explorer);
            e.Handled = true;
        }

        /// <summary>
        /// Build and show a MenuFlyout with navigation history entries.
        /// Includes the current location (bold with checkmark) and history items with folder icons.
        /// </summary>
        private void ShowHistoryDropdown(FrameworkElement? target, bool isBack, ExplorerViewModel? explorer)
        {
            if (target == null || explorer == null) return;

            var history = isBack ? explorer.GetBackHistory() : explorer.GetForwardHistory();
            if (history.Count == 0) return;

            var flyout = new MenuFlyout();

            // Add current location at the top with bold text and checkmark
            var currentPath = explorer.CurrentPath;
            if (!string.IsNullOrEmpty(currentPath))
            {
                var currentName = System.IO.Path.GetFileName(currentPath);
                if (string.IsNullOrEmpty(currentName))
                    currentName = currentPath; // Drive root like "C:\"

                var currentItem = new MenuFlyoutItem
                {
                    Text = currentName,
                    Icon = new FontIcon { Glyph = "\uE73E", FontSize = 14 }, // Checkmark
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    IsEnabled = false
                };
                ToolTipService.SetToolTip(currentItem, currentPath);
                flyout.Items.Add(currentItem);

                flyout.Items.Add(new MenuFlyoutSeparator());
            }

            // Show up to 15 most recent history entries
            int maxItems = Math.Min(history.Count, 15);
            for (int i = 0; i < maxItems; i++)
            {
                var path = history[i];
                var folderName = System.IO.Path.GetFileName(path);
                if (string.IsNullOrEmpty(folderName))
                    folderName = path; // Drive root like "C:\"

                var item = new MenuFlyoutItem
                {
                    Text = folderName,
                    Tag = i,
                    Icon = new FontIcon { Glyph = "\uE8B7", FontSize = 14 } // Folder glyph
                };

                // Set tooltip to full path for disambiguation
                ToolTipService.SetToolTip(item, path);

                var capturedIndex = i;
                var capturedExplorer = explorer;
                item.Click += async (s, args) =>
                {
                    if (isBack)
                        await capturedExplorer.NavigateToBackHistoryEntry(capturedIndex);
                    else
                        await capturedExplorer.NavigateToForwardHistoryEntry(capturedIndex);

                    ViewModel.SyncNavigationHistoryState();
                    FocusLastColumnAfterNavigation();
                };

                flyout.Items.Add(item);
            }

            flyout.ShowAt(target);
        }

        /// <summary>
        /// After Back/Forward navigation, focus the last column so keyboard nav works.
        /// Retries until the ListView container is available (handles async column loading).
        /// </summary>
        private void FocusLastColumnAfterNavigation()
        {
            if (_isClosed) return;
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                if (_isClosed) return;

                // Retry up to 5 times (50ms each) to wait for column rendering
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    var columns = ViewModel.ActiveExplorer?.Columns;
                    if (columns == null || columns.Count == 0) break;

                    int targetIndex = columns.Count - 1;
                    var listView = GetListViewForColumn(targetIndex);
                    if (listView != null)
                    {
                        FocusColumnAsync(targetIndex);
                        return;
                    }

                    await Task.Delay(50);
                    if (_isClosed) return;
                }

                // Last resort: try focusing anyway
                var cols = ViewModel.ActiveExplorer?.Columns;
                if (cols != null && cols.Count > 0)
                {
                    FocusColumnAsync(cols.Count - 1);
                }
            })) { /* DispatcherQueue shut down */ }
        }

        /// <summary>
        /// 편집 모드 표시: 브레드크럼 숨기고 AutoSuggestBox 표시.
        /// </summary>
        private void ShowAddressBarEditMode()
        {
            AddressBreadcrumbScroller.Visibility = Visibility.Collapsed;
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
            AddressBreadcrumbScroller.Visibility = Visibility.Visible;
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
                _ = ViewModel.ActiveExplorer.NavigateToPath(path);
            }
            else if (System.IO.File.Exists(path))
            {
                // Navigate to parent and select the file
                var parent = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                {
                    _ = ViewModel.ActiveExplorer.NavigateToPath(parent);
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

        /// <summary>
        /// Update toolbar button enabled/disabled states based on current selection and clipboard.
        /// </summary>
        private void UpdateToolbarButtonStates()
        {
            bool hasSelection = HasAnySelection();
            bool hasClipboard = _clipboardPaths.Count > 0;

            ToolbarCutButton.IsEnabled = hasSelection;
            ToolbarCopyButton.IsEnabled = hasSelection;
            ToolbarPasteButton.IsEnabled = hasClipboard;
            ToolbarRenameButton.IsEnabled = hasSelection;
            ToolbarDeleteButton.IsEnabled = hasSelection;
        }

        /// <summary>
        /// Check if any file/folder is currently selected in the active view.
        /// </summary>
        private bool HasAnySelection()
        {
            var explorer = ViewModel.ActiveExplorer;
            if (explorer == null) return false;

            // Check all columns for any selected item
            foreach (var col in explorer.Columns)
            {
                if (col.SelectedChild != null)
                    return true;
                if (col.SelectedItems != null && col.SelectedItems.Count > 0)
                    return true;
            }
            return false;
        }

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

        private void OnNewItemDropdownClick(object sender, RoutedEventArgs e)
        {
            var folderPath = GetActiveColumnPath();
            if (string.IsNullOrEmpty(folderPath)) return;

            var menu = _contextMenuService.BuildNewItemMenu(folderPath, this);
            menu.ShowAt(sender as FrameworkElement, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft
            });
        }

        private string? GetActiveColumnPath()
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return null;
            return columns[activeIndex].Path;
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

        private void OnViewModeList(object sender, RoutedEventArgs e)
        {
            ViewModel.SwitchViewMode(Models.ViewMode.List);
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

        // =================================================================
        //  Ctrl+Mouse Wheel — Cycle through ALL view modes (global window-level handler)
        //  Sequence: Miller → Details → IconSmall → IconMedium → IconLarge → IconExtraLarge
        //  Registered on this.Content with handledEventsToo=true so it works
        //  even when ScrollViewer/ListView consume the wheel event internally.
        // =================================================================

        private static readonly Models.ViewMode[] _allViewModes = new[]
        {
            Models.ViewMode.MillerColumns,
            Models.ViewMode.Details,
            Models.ViewMode.List,
            Models.ViewMode.IconSmall,
            Models.ViewMode.IconMedium,
            Models.ViewMode.IconLarge,
            Models.ViewMode.IconExtraLarge
        };

        private void OnGlobalPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (!ctrl) return;

            var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;
            if (delta == 0) return;

            // Dynamically find current position in the mode sequence
            var currentMode = ViewModel.CurrentViewMode;
            int currentIndex = Array.IndexOf(_allViewModes, currentMode);
            if (currentIndex < 0) currentIndex = 0; // fallback to Miller

            int newIndex = delta > 0
                ? Math.Min(currentIndex + 1, _allViewModes.Length - 1)  // scroll up = more visual
                : Math.Max(currentIndex - 1, 0);                         // scroll down = less visual

            if (newIndex == currentIndex) { e.Handled = true; return; }

            var newMode = _allViewModes[newIndex];
            ViewModel.SwitchViewMode(newMode);

            // If switching to icon mode, update icon size
            if (Helpers.ViewModeExtensions.IsIconMode(newMode))
            {
                GetActiveIconView()?.UpdateIconSize(newMode);
            }

            e.Handled = true;
        }

        private Views.IconModeView? GetActiveIconView()
        {
            if (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                return IconViewRight;
            if (_activeIconTabId != null && _tabIconPanels.TryGetValue(_activeIconTabId, out var view))
                return view;
            return null;
        }

        private Views.DetailsModeView? GetActiveDetailsView()
        {
            if (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                return DetailsViewRight;
            if (_activeDetailsTabId != null && _tabDetailsPanels.TryGetValue(_activeDetailsTabId, out var view))
                return view;
            return null;
        }

        private Views.ListModeView? GetActiveListView()
        {
            // List has no right pane variant yet — left pane only
            if (_activeListTabId != null && _tabListPanels.TryGetValue(_activeListTabId, out var view))
                return view;
            return null;
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

        public Visibility IsNotHomeMode(Models.ViewMode mode)
            => mode != Models.ViewMode.Home ? Visibility.Visible : Visibility.Collapsed;

        public string GetTabDisplayName(Models.ViewMode mode, string folderName)
            => mode == Models.ViewMode.Home ? "Home" : folderName;

        // =================================================================
        //  Per-Tab Miller Panel Management (Show/Hide pattern)
        // =================================================================

        /// <summary>
        /// LoadTabsFromSettings 후 모든 탭에 대한 Miller 패널 초기화.
        /// 기존 패널을 정리하고, 각 탭에 대해 패널을 (재)생성한다.
        /// 활성 탭 패널만 Visible, 나머지는 Collapsed.
        /// </summary>
        private void InitializeTabMillerPanels()
        {
            // 기존 동적 패널 정리 (XAML 정의 MillerScrollViewer 제외)
            foreach (var kvp in _tabMillerPanels)
            {
                if (kvp.Value.scroller != MillerScrollViewer)
                {
                    kvp.Value.items.ItemsSource = null;
                    MillerTabsHost.Children.Remove(kvp.Value.scroller);
                }
            }
            _tabMillerPanels.Clear();

            // M4: 활성 탭만 즉시 패널 할당 — 비활성 탭은 SwitchMillerPanel에서 Lazy 생성
            for (int i = 0; i < ViewModel.Tabs.Count; i++)
            {
                var tab = ViewModel.Tabs[i];
                if (i == ViewModel.ActiveTabIndex)
                {
                    // 활성 탭은 XAML 정의 패널 재사용
                    MillerColumnsControl.ItemsSource = tab.Explorer?.Columns;
                    MillerScrollViewer.Visibility = Visibility.Visible;
                    _tabMillerPanels[tab.Id] = (MillerScrollViewer, MillerColumnsControl);
                    _activeMillerTabId = tab.Id;
                }
                // 비활성 탭은 SwitchMillerPanel 호출 시 Lazy 생성
            }

            // ── Per-Tab Details/List/Icon Panels 초기화 ──
            // 기존 동적 패널 정리 (XAML 정의 인스턴스 제외)
            foreach (var kvp in _tabDetailsPanels)
            {
                if (kvp.Value != DetailsView)
                {
                    try { kvp.Value?.Cleanup(); } catch { }
                    DetailsTabsHost.Children.Remove(kvp.Value);
                }
            }
            _tabDetailsPanels.Clear();

            foreach (var kvp in _tabListPanels)
            {
                if (kvp.Value != ListView)
                {
                    try { kvp.Value?.Cleanup(); } catch { }
                    ListTabsHost.Children.Remove(kvp.Value);
                }
            }
            _tabListPanels.Clear();

            foreach (var kvp in _tabIconPanels)
            {
                if (kvp.Value != IconView)
                {
                    try { kvp.Value?.Cleanup(); } catch { }
                    IconTabsHost.Children.Remove(kvp.Value);
                }
            }
            _tabIconPanels.Clear();

            // 활성 탭에 XAML 정의 인스턴스 할당
            var activeTab = ViewModel.Tabs.Count > 0 ? ViewModel.Tabs[ViewModel.ActiveTabIndex] : null;
            if (activeTab != null)
            {
                _tabDetailsPanels[activeTab.Id] = DetailsView;
                _tabListPanels[activeTab.Id] = ListView;
                _tabIconPanels[activeTab.Id] = IconView;
                _activeDetailsTabId = activeTab.Id;
                _activeListTabId = activeTab.Id;
                _activeIconTabId = activeTab.Id;
            }

            Helpers.DebugLogger.Log($"[MillerPanel] Initialized {_tabMillerPanels.Count} panels (active: {_activeMillerTabId})");
        }

        /// <summary>
        /// 새 탭에 대한 Miller Columns 패널(ScrollViewer + ItemsControl) 생성.
        /// XAML 정의 MillerColumnsControl의 Template을 재사용하여 이벤트 핸들러 호환성 보장.
        /// </summary>
        private (ScrollViewer scroller, ItemsControl items) CreateMillerPanelForTab(Models.TabItem tab)
        {
            var itemsControl = new ItemsControl
            {
                ItemTemplate = MillerColumnsControl.ItemTemplate,
                ItemsPanel = MillerColumnsControl.ItemsPanel,
                ItemsSource = tab.Explorer?.Columns
            };

            // 키보드 이벤트 핸들러 등록 (XAML 정의 컨트롤과 동일)
            itemsControl.AddHandler(
                UIElement.KeyDownEvent,
                new KeyEventHandler(OnMillerKeyDown),
                true
            );

            var scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Auto,
                VerticalScrollMode = ScrollMode.Disabled,
                Content = itemsControl,
                Visibility = Visibility.Collapsed // 생성 시 숨김, 전환 시 표시
            };

            // MillerTabsHost Grid에 추가
            MillerTabsHost.Children.Add(scrollViewer);
            _tabMillerPanels[tab.Id] = (scrollViewer, itemsControl);

            Helpers.DebugLogger.Log($"[MillerPanel] Created panel for tab {tab.Id} ({tab.Header})");
            return (scrollViewer, itemsControl);
        }

        /// <summary>
        /// 활성 탭의 Miller 패널로 전환 — Visibility 토글만으로 즉시 전환.
        /// </summary>
        private void SwitchMillerPanel(string newTabId)
        {
            if (_activeMillerTabId == newTabId) return;

            // 이전 패널 숨기기
            if (_activeMillerTabId != null && _tabMillerPanels.TryGetValue(_activeMillerTabId, out var oldPanel))
            {
                oldPanel.scroller.Visibility = Visibility.Collapsed;
            }

            // M4: 새 패널 — 없으면 Lazy 생성
            if (!_tabMillerPanels.TryGetValue(newTabId, out var newPanel))
            {
                var tab = ViewModel.Tabs.FirstOrDefault(t => t.Id == newTabId);
                if (tab != null)
                {
                    newPanel = CreateMillerPanelForTab(tab);
                }
            }

            if (newPanel.scroller != null)
            {
                newPanel.scroller.Visibility = Visibility.Visible;
                _activeMillerTabId = newTabId;
            }
        }

        /// <summary>
        /// 탭 닫힐 때 해당 Miller 패널 제거.
        /// </summary>
        private void RemoveMillerPanel(string tabId)
        {
            if (_tabMillerPanels.TryGetValue(tabId, out var panel))
            {
                // 키보드 이벤트 해제
                panel.items.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnMillerKeyDown));
                panel.items.ItemsSource = null;
                MillerTabsHost.Children.Remove(panel.scroller);
                _tabMillerPanels.Remove(tabId);
                Helpers.DebugLogger.Log($"[MillerPanel] Removed panel for tab {tabId}");
            }
        }

        // =================================================================
        //  Per-Tab Details Panel Management (Show/Hide pattern)
        // =================================================================

        /// <summary>
        /// 새 탭에 대한 DetailsModeView 인스턴스 생성.
        /// ContextMenu, HWND 등 설정 후 DetailsTabsHost에 추가.
        /// </summary>
        private Views.DetailsModeView CreateDetailsPanelForTab(Models.TabItem tab)
        {
            var detailsView = new Views.DetailsModeView
            {
                IsManualViewModel = true,
                ViewModel = tab.Explorer,
                ContextMenuService = _contextMenuService,
                ContextMenuHost = this,
                OwnerHwnd = _hwnd,
                Visibility = Visibility.Collapsed
            };

            DetailsTabsHost.Children.Add(detailsView);
            _tabDetailsPanels[tab.Id] = detailsView;

            Helpers.DebugLogger.Log($"[DetailsPanel] Created panel for tab {tab.Id} ({tab.Header})");
            return detailsView;
        }

        /// <summary>
        /// 활성 탭의 Details 패널로 전환 — Visibility 토글만으로 즉시 전환.
        /// shouldCreate=true면 패널이 없을 때 lazy 생성.
        /// </summary>
        private void SwitchDetailsPanel(string newTabId, bool shouldCreate)
        {
            if (_activeDetailsTabId == newTabId) return;

            // 이전 패널 숨기기
            if (_activeDetailsTabId != null && _tabDetailsPanels.TryGetValue(_activeDetailsTabId, out var oldPanel))
            {
                oldPanel.Visibility = Visibility.Collapsed;
            }

            // 새 패널 — 없으면 shouldCreate일 때만 Lazy 생성
            if (_tabDetailsPanels.TryGetValue(newTabId, out var newPanel))
            {
                newPanel.Visibility = Visibility.Visible;
            }
            else if (shouldCreate)
            {
                var tab = ViewModel.Tabs.FirstOrDefault(t => t.Id == newTabId);
                if (tab != null)
                {
                    newPanel = CreateDetailsPanelForTab(tab);
                    newPanel.Visibility = Visibility.Visible;
                }
            }

            _activeDetailsTabId = newTabId;
        }

        /// <summary>
        /// 탭 닫힐 때 해당 Details 패널 제거.
        /// </summary>
        private void RemoveDetailsPanel(string tabId)
        {
            if (_tabDetailsPanels.TryGetValue(tabId, out var panel))
            {
                try { panel.Cleanup(); } catch { }
                DetailsTabsHost.Children.Remove(panel);
                _tabDetailsPanels.Remove(tabId);
                Helpers.DebugLogger.Log($"[DetailsPanel] Removed panel for tab {tabId}");
            }
        }

        // =================================================================
        //  Per-Tab List Panel Management (Show/Hide pattern)
        // =================================================================

        private Views.ListModeView CreateListPanelForTab(Models.TabItem tab)
        {
            var listView = new Views.ListModeView
            {
                IsManualViewModel = true,
                ViewModel = tab.Explorer,
                ContextMenuService = _contextMenuService,
                ContextMenuHost = this,
                OwnerHwnd = _hwnd,
                Visibility = Visibility.Collapsed
            };

            ListTabsHost.Children.Add(listView);
            _tabListPanels[tab.Id] = listView;

            Helpers.DebugLogger.Log($"[ListPanel] Created panel for tab {tab.Id} ({tab.Header})");
            return listView;
        }

        private void SwitchListPanel(string newTabId, bool shouldCreate)
        {
            if (_activeListTabId == newTabId) return;

            if (_activeListTabId != null && _tabListPanels.TryGetValue(_activeListTabId, out var oldPanel))
            {
                oldPanel.Visibility = Visibility.Collapsed;
            }

            if (_tabListPanels.TryGetValue(newTabId, out var newPanel))
            {
                newPanel.Visibility = Visibility.Visible;
            }
            else if (shouldCreate)
            {
                var tab = ViewModel.Tabs.FirstOrDefault(t => t.Id == newTabId);
                if (tab != null)
                {
                    newPanel = CreateListPanelForTab(tab);
                    newPanel.Visibility = Visibility.Visible;
                }
            }

            _activeListTabId = newTabId;
        }

        private void RemoveListPanel(string tabId)
        {
            if (_tabListPanels.TryGetValue(tabId, out var panel))
            {
                try { panel.Cleanup(); } catch { }
                ListTabsHost.Children.Remove(panel);
                _tabListPanels.Remove(tabId);
                Helpers.DebugLogger.Log($"[ListPanel] Removed panel for tab {tabId}");
            }
        }

        // =================================================================
        //  Per-Tab Icon Panel Management (Show/Hide pattern)
        // =================================================================

        /// <summary>
        /// 새 탭에 대한 IconModeView 인스턴스 생성.
        /// ContextMenu, HWND 등 설정 후 IconTabsHost에 추가.
        /// </summary>
        private Views.IconModeView CreateIconPanelForTab(Models.TabItem tab)
        {
            var iconView = new Views.IconModeView
            {
                IsManualViewModel = true,
                ViewModel = tab.Explorer,
                ContextMenuService = _contextMenuService,
                ContextMenuHost = this,
                OwnerHwnd = _hwnd,
                Visibility = Visibility.Collapsed
            };

            IconTabsHost.Children.Add(iconView);
            _tabIconPanels[tab.Id] = iconView;

            Helpers.DebugLogger.Log($"[IconPanel] Created panel for tab {tab.Id} ({tab.Header})");
            return iconView;
        }

        /// <summary>
        /// 활성 탭의 Icon 패널로 전환 — Visibility 토글만으로 즉시 전환.
        /// shouldCreate=true면 패널이 없을 때 lazy 생성.
        /// </summary>
        private void SwitchIconPanel(string newTabId, bool shouldCreate)
        {
            if (_activeIconTabId == newTabId) return;

            // 이전 패널 숨기기
            if (_activeIconTabId != null && _tabIconPanels.TryGetValue(_activeIconTabId, out var oldPanel))
            {
                oldPanel.Visibility = Visibility.Collapsed;
            }

            // 새 패널 — 없으면 shouldCreate일 때만 Lazy 생성
            if (_tabIconPanels.TryGetValue(newTabId, out var newPanel))
            {
                newPanel.Visibility = Visibility.Visible;
            }
            else if (shouldCreate)
            {
                var tab = ViewModel.Tabs.FirstOrDefault(t => t.Id == newTabId);
                if (tab != null)
                {
                    newPanel = CreateIconPanelForTab(tab);
                    newPanel.Visibility = Visibility.Visible;
                }
            }

            _activeIconTabId = newTabId;
        }

        /// <summary>
        /// 탭 닫힐 때 해당 Icon 패널 제거.
        /// </summary>
        private void RemoveIconPanel(string tabId)
        {
            if (_tabIconPanels.TryGetValue(tabId, out var panel))
            {
                try { panel.Cleanup(); } catch { }
                IconTabsHost.Children.Remove(panel);
                _tabIconPanels.Remove(tabId);
                Helpers.DebugLogger.Log($"[IconPanel] Removed panel for tab {tabId}");
            }
        }

        // =================================================================
        //  Tab Event Handlers
        // =================================================================

        private void OnTabItemPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Models.TabItem tab)
            {
                int index = ViewModel.Tabs.IndexOf(tab);
                if (index >= 0)
                {
                    // Record drag start for tear-off detection
                    _tabDragStartPoint = e.GetCurrentPoint(null).Position;
                    _draggingTab = tab;
                    _isTabDragging = false; // Will become true if threshold exceeded

                    // Capture pointer so PointerMoved fires even outside the tab element
                    if (ViewModel.Tabs.Count > 1)
                        fe.CapturePointer(e.Pointer);

                    // Settings 탭은 Miller/Details/Icon 패널 없음
                    if (tab.ViewMode != ViewMode.Settings)
                    {
                        // Show/Hide 패널 전환 (ViewModel.SwitchToTab 전에 실행하여 바인딩 재평가 방지)
                        SwitchMillerPanel(tab.Id);
                        SwitchDetailsPanel(tab.Id, tab.ViewMode == ViewMode.Details);
                        SwitchListPanel(tab.Id, tab.ViewMode == ViewMode.List);
                        SwitchIconPanel(tab.Id, Helpers.ViewModeExtensions.IsIconMode(tab.ViewMode));
                    }
                    ViewModel.SwitchToTab(index);
                    // LeftExplorer 변경 후 수동으로 필요한 것만 갱신 (PropertyChanged 미발생이므로)
                    ResubscribeLeftExplorer();
                    UpdateViewModeVisibility();
                    UpdateToolbarButtonStates();
                    FocusActiveView();
                }
            }
        }

        private void OnTabItemPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_draggingTab == null) return;

            var currentPoint = e.GetCurrentPoint(null).Position;
            double dx = currentPoint.X - _tabDragStartPoint.X;
            double dy = currentPoint.Y - _tabDragStartPoint.Y;

            // Check if drag threshold exceeded
            if (!_isTabDragging)
            {
                if (Math.Sqrt(dx * dx + dy * dy) < TAB_DRAG_THRESHOLD)
                    return;
                _isTabDragging = true;
            }

            // Check if cursor is outside the window → tear off
            if (IsCursorOutsideWindow())
            {
                // Don't tear off the last tab
                if (ViewModel.Tabs.Count <= 1) return;

                var tabToTearOff = _draggingTab;
                _draggingTab = null;
                _isTabDragging = false;

                // Release pointer capture so the new window can take over
                if (sender is UIElement element)
                {
                    try { element.ReleasePointerCaptures(); } catch { }
                }

                TearOffTab(tabToTearOff);
                return;
            }

            // Cursor is inside the window → handle tab reorder
            var tabIndex = GetTabIndexAtPoint(currentPoint);
            if (tabIndex >= 0)
            {
                int currentIndex = ViewModel.Tabs.IndexOf(_draggingTab!);
                if (currentIndex >= 0 && currentIndex != tabIndex)
                {
                    ViewModel.Tabs.Move(currentIndex, tabIndex);
                    // Update active tab index to follow the moved tab
                    ViewModel.ActiveTabIndex = tabIndex;
                    Helpers.DebugLogger.Log($"[TabReorder] Moved tab from {currentIndex} to {tabIndex}");
                }
            }
        }

        private void OnTabItemPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _draggingTab = null;
            _isTabDragging = false;
            if (sender is UIElement element)
            {
                try { element.ReleasePointerCaptures(); } catch { }
            }
            // Update title bar input regions since tabs may have been reordered
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
        }

        /// <summary>
        /// Returns the tab index at the given point (relative to the window).
        /// Tab width is 200px with 1px spacing between tabs.
        /// </summary>
        private int GetTabIndexAtPoint(Windows.Foundation.Point windowPoint)
        {
            try
            {
                // Convert window point to position relative to the TabRepeater
                var transform = TabRepeater.TransformToVisual(null);
                var tabBarOrigin = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                double relativeX = windowPoint.X - tabBarOrigin.X;
                if (relativeX < 0) return 0;

                // Each tab is 200px wide + 1px spacing
                int index = (int)(relativeX / 201);
                return Math.Clamp(index, 0, ViewModel.Tabs.Count - 1);
            }
            catch
            {
                return -1;
            }
        }

        private bool IsCursorOutsideWindow()
        {
            if (!Helpers.NativeMethods.GetCursorPos(out var cursorPos))
                return false;
            if (!Helpers.NativeMethods.GetWindowRect(_hwnd, out var windowRect))
                return false;

            return cursorPos.X < windowRect.Left ||
                   cursorPos.X > windowRect.Right ||
                   cursorPos.Y < windowRect.Top ||
                   cursorPos.Y > windowRect.Bottom;
        }

        /// <summary>
        /// Open a new window at the current path (Ctrl+N).
        /// </summary>
        private void OpenNewWindow()
        {
            try
            {
                // Build a TabStateDto from the current active tab state
                ViewModel.SaveActiveTabState();
                var activeTab = ViewModel.ActiveTab;
                var currentPath = ViewModel.ActiveExplorer?.CurrentPath ?? string.Empty;
                var header = activeTab?.Header ?? "Home";
                var viewMode = activeTab != null ? (int)activeTab.ViewMode : (int)ViewMode.MillerColumns;
                var iconSize = activeTab != null ? (int)activeTab.IconSize : (int)ViewMode.IconMedium;

                var dto = new Models.TabStateDto(
                    System.Guid.NewGuid().ToString("N")[..8],
                    header,
                    currentPath,
                    viewMode,
                    iconSize);

                var newWindow = new MainWindow();
                newWindow._pendingTearOff = dto;

                App.Current.RegisterWindow(newWindow);
                newWindow.Activate();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[OpenNewWindow] Error: {ex.Message}");
            }
        }

        private void TearOffTab(Models.TabItem tab)
        {
            try
            {
                // 1. Save tab state as DTO
                ViewModel.SaveActiveTabState();
                var dto = new Models.TabStateDto(
                    tab.Id, tab.Header, tab.Path,
                    (int)tab.ViewMode, (int)tab.IconSize);

                // 2. 원본 창의 Win32 사이즈 (물리 픽셀) + 커서 위치 캡처
                Helpers.NativeMethods.GetWindowRect(_hwnd, out var srcRect);
                int srcW = srcRect.Right - srcRect.Left;
                int srcH = srcRect.Bottom - srcRect.Top;
                Helpers.NativeMethods.GetCursorPos(out var cursorPos);

                // 3. Remove tab from current window (panels + ViewModel)
                int index = ViewModel.Tabs.IndexOf(tab);
                if (index < 0) return;

                RemoveMillerPanel(tab.Id);
                RemoveDetailsPanel(tab.Id);
                RemoveListPanel(tab.Id);
                RemoveIconPanel(tab.Id);
                ViewModel.CloseTab(index);

                // Switch panels for the new active tab
                if (ViewModel.ActiveTab != null)
                {
                    SwitchMillerPanel(ViewModel.ActiveTab.Id);
                    SwitchDetailsPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.Details);
                    SwitchListPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.List);
                    SwitchIconPanel(ViewModel.ActiveTab.Id, Helpers.ViewModeExtensions.IsIconMode(ViewModel.ActiveTab.ViewMode));
                }
                ResubscribeLeftExplorer();
                UpdateViewModeVisibility();
                FocusActiveView();

                // 4. 새 창 생성 + HWND 확보
                var newWindow = new MainWindow();
                newWindow._pendingTearOff = dto;
                var newHwnd = WinRT.Interop.WindowNative.GetWindowHandle(newWindow);

                // 5. DWMWA_CLOAK — 창을 DWM에서 합성하되 화면에 숨김 (깜빡임 방지)
                int cloakOn = 1;
                Helpers.NativeMethods.DwmSetWindowAttribute(newHwnd,
                    Helpers.NativeMethods.DWMWA_CLOAK, ref cloakOn, sizeof(int));
                int transOff = 1;
                Helpers.NativeMethods.DwmSetWindowAttribute(newHwnd,
                    Helpers.NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, ref transOff, sizeof(int));

                // 6. Activate — XAML 파이프라인 시작 (클로킹 상태라 화면에 안 보임)
                App.Current.RegisterWindow(newWindow);
                newWindow.Activate();

                // 7. 초기 위치/크기 설정 + DPI 로깅
                int offsetX = srcW / 4;  // 커서가 타이틀바 왼쪽 25% 지점
                int offsetY = 15;         // 커서가 타이틀바 상단 근처

                uint srcDpi = Helpers.NativeMethods.GetDpiForWindow(_hwnd);
                uint newDpi = Helpers.NativeMethods.GetDpiForWindow(newHwnd);
                Helpers.DebugLogger.Log($"[TearOff] srcDpi={srcDpi}, newDpi={newDpi}, srcSize={srcW}x{srcH}");

                // SetWindowPos로 초기 위치/크기 (Activate 후 재적용은 타이머에서)
                Helpers.NativeMethods.SetWindowPos(newHwnd, Helpers.NativeMethods.HWND_TOP,
                    cursorPos.X - offsetX,
                    cursorPos.Y - offsetY,
                    srcW, srcH,
                    Helpers.NativeMethods.SWP_NOACTIVATE);

                // 8. 수동 드래그 시작 — 타이머 첫 틱에서 크기도 재적용 (Activate 레이아웃 덮어쓰기 방지)
                StartManualWindowDrag(newHwnd, offsetX, offsetY, srcW, srcH);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[TearOff] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 수동 창 드래그: DispatcherTimer로 커서를 추적하여 SetWindowPos로 창 이동.
        /// SC_DRAGMOVE를 대체 (WinUI 3에서 NC 메시지가 필터링되어 SC_DRAGMOVE 동작 안함).
        /// 타이머는 원본 창의 DispatcherQueue에서 실행 (새 창은 아직 초기화 중일 수 있음).
        /// </summary>
        private void StartManualWindowDrag(IntPtr targetHwnd, int dragOffsetX, int dragOffsetY,
            int targetWidth, int targetHeight)
        {
            var dragTimer = new DispatcherTimer();
            dragTimer.Interval = TimeSpan.FromMilliseconds(8); // ~120Hz 부드러운 추적

            bool uncloaked = false;
            bool sizeApplied = false;
            int frameCount = 0;

            dragTimer.Tick += (s, e) =>
            {
                if (_isClosed)
                {
                    dragTimer.Stop();
                    return;
                }

                // 1. 마우스 왼쪽 버튼 하드웨어 상태 확인 (메시지 큐와 무관)
                bool mouseDown = (Helpers.NativeMethods.GetAsyncKeyState(
                    Helpers.NativeMethods.VK_LBUTTON) & 0x8000) != 0;

                if (!mouseDown)
                {
                    // 마우스 놓음 → 드래그 종료
                    dragTimer.Stop();

                    // Check for re-docking: is the cursor over another Span window's tab bar?
                    Helpers.NativeMethods.GetCursorPos(out var dropPos);
                    var targetWindow = App.Current.FindWindowAtPoint(dropPos.X, dropPos.Y, this);

                    // Find the new torn-off window by HWND
                    MainWindow? newWindow = null;
                    foreach (var w in ((App)App.Current).GetRegisteredWindows())
                    {
                        if (w is MainWindow mw && WinRT.Interop.WindowNative.GetWindowHandle(mw) == targetHwnd)
                        {
                            newWindow = mw;
                            break;
                        }
                    }

                    if (targetWindow != null && newWindow != null && newWindow.ViewModel.Tabs.Count > 0)
                    {
                        // Re-dock: transfer tab from new window to target window
                        var tab = newWindow.ViewModel.ActiveTab;
                        if (tab != null)
                        {
                            newWindow.ViewModel.SaveActiveTabState();
                            var dockDto = new Models.TabStateDto(
                                tab.Id, tab.Header, tab.Path,
                                (int)tab.ViewMode, (int)tab.IconSize);

                            // Close the new (torn-off) window
                            newWindow._forceClose = true;
                            newWindow._isClosed = true;
                            App.Current.UnregisterWindow(newWindow);
                            newWindow.Close();

                            // Dock the tab into the target window
                            targetWindow.DockTab(dockDto);
                            Helpers.DebugLogger.Log($"[ReDock] Tab '{dockDto.Header}' merged into target window");
                            return;
                        }
                    }

                    // 최종 크기 보정 (Activate 레이아웃이 덮어썼을 수 있음)
                    if (!sizeApplied)
                    {
                        Helpers.NativeMethods.GetCursorPos(out var finalPos2);
                        Helpers.NativeMethods.SetWindowPos(
                            targetHwnd, Helpers.NativeMethods.HWND_TOP,
                            finalPos2.X - dragOffsetX, finalPos2.Y - dragOffsetY,
                            targetWidth, targetHeight, 0);
                    }

                    if (!uncloaked)
                    {
                        int cloakOff = 0;
                        Helpers.NativeMethods.DwmSetWindowAttribute(targetHwnd,
                            Helpers.NativeMethods.DWMWA_CLOAK, ref cloakOff, sizeof(int));
                    }
                    Helpers.NativeMethods.SetForegroundWindow(targetHwnd);
                    return;
                }

                // 2. 현재 커서 위치
                if (!Helpers.NativeMethods.GetCursorPos(out var pos))
                    return;

                frameCount++;

                // 3. 첫 몇 프레임: 크기 포함하여 SetWindowPos (Activate의 기본 크기를 강제 덮어씀)
                if (!sizeApplied && frameCount <= 3)
                {
                    Helpers.NativeMethods.SetWindowPos(
                        targetHwnd, Helpers.NativeMethods.HWND_TOP,
                        pos.X - dragOffsetX,
                        pos.Y - dragOffsetY,
                        targetWidth, targetHeight,
                        Helpers.NativeMethods.SWP_NOACTIVATE);

                    if (frameCount == 3) sizeApplied = true;
                }
                else
                {
                    // 이후: 위치만 이동 (크기는 확정됨)
                    Helpers.NativeMethods.SetWindowPos(
                        targetHwnd, Helpers.NativeMethods.HWND_TOP,
                        pos.X - dragOffsetX,
                        pos.Y - dragOffsetY,
                        0, 0,
                        Helpers.NativeMethods.SWP_NOSIZE | Helpers.NativeMethods.SWP_NOACTIVATE);
                }

                // 4. 몇 프레임 후 클로킹 해제 (XAML이 첫 프레임을 렌더링할 시간 확보)
                if (!uncloaked && frameCount >= 5) // ~40ms
                {
                    uncloaked = true;
                    int cloakOff = 0;
                    Helpers.NativeMethods.DwmSetWindowAttribute(targetHwnd,
                        Helpers.NativeMethods.DWMWA_CLOAK, ref cloakOff, sizeof(int));
                    Helpers.NativeMethods.SetForegroundWindow(targetHwnd);
                }
            };

            dragTimer.Start();
        }

        /// <summary>
        /// MS 공식 패턴: SetTitleBar(AppTitleBar)가 드래그/캡션 버튼을 자동 관리.
        /// Passthrough 영역 = TabBarContent(StackPanel)의 실제 콘텐츠 영역을
        /// ScrollViewer 뷰포트에 클리핑한 교집합.
        /// → 탭 오른쪽 빈 공간은 드래그 영역으로 유지
        /// → 스크롤 시에도 캡션 버튼 영역을 넘지 않음
        /// </summary>
        private void UpdateTitleBarRegions()
        {
            try
            {
                if (_isClosed || TabScrollViewer == null || TabRepeater == null) return;
                if (!ExtendsContentIntoTitleBar) return;

                double scale = AppTitleBar.XamlRoot.RasterizationScale;

                // 캡션 버튼 영역 확보
                RightPaddingColumn.Width = new GridLength(
                    this.AppWindow.TitleBar.RightInset / scale);

                // ScrollViewer 뷰포트 경계 (클리핑용)
                GeneralTransform svTransform = TabScrollViewer.TransformToVisual(null);
                Windows.Foundation.Rect svBounds = svTransform.TransformBounds(
                    new Windows.Foundation.Rect(0, 0,
                        TabScrollViewer.ActualWidth,
                        TabScrollViewer.ActualHeight));

                var rects = new List<Windows.Graphics.RectInt32>();

                // 각 탭 요소를 개별 Passthrough rect로 등록
                if (TabRepeater.ItemsSourceView != null)
                {
                    for (int i = 0; i < TabRepeater.ItemsSourceView.Count; i++)
                    {
                        if (TabRepeater.TryGetElement(i) is not FrameworkElement element) continue;

                        var clipped = GetClippedRect(element, svBounds);
                        if (clipped.HasValue)
                        {
                            rects.Add(ToRectInt32(clipped.Value, scale));
                        }
                    }
                }

                // + (New Tab) 버튼도 Passthrough로 등록
                if (NewTabButton != null)
                {
                    var clipped = GetClippedRect(NewTabButton, svBounds);
                    if (clipped.HasValue)
                    {
                        rects.Add(ToRectInt32(clipped.Value, scale));
                    }
                }

                var nonClientInputSrc = InputNonClientPointerSource.GetForWindowId(this.AppWindow.Id);
                nonClientInputSrc.SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());
            }
            catch { /* Layout not ready yet */ }
        }

        /// <summary>
        /// 요소의 bounds를 뷰포트에 클리핑하여 반환. 뷰포트 밖이면 null.
        /// </summary>
        private static Windows.Foundation.Rect? GetClippedRect(
            FrameworkElement element, Windows.Foundation.Rect viewport)
        {
            GeneralTransform transform = element.TransformToVisual(null);
            Windows.Foundation.Rect bounds = transform.TransformBounds(
                new Windows.Foundation.Rect(0, 0,
                    element.ActualWidth, element.ActualHeight));

            double left = Math.Max(bounds.X, viewport.X);
            double top = Math.Max(bounds.Y, viewport.Y);
            double right = Math.Min(bounds.X + bounds.Width, viewport.X + viewport.Width);
            double bottom = Math.Min(bounds.Y + bounds.Height, viewport.Y + viewport.Height);

            if (right > left && bottom > top)
                return new Windows.Foundation.Rect(left, top, right - left, bottom - top);
            return null;
        }

        private static Windows.Graphics.RectInt32 ToRectInt32(
            Windows.Foundation.Rect rect, double scale)
        {
            return new Windows.Graphics.RectInt32(
                (int)Math.Round(rect.X * scale),
                (int)Math.Round(rect.Y * scale),
                (int)Math.Round(rect.Width * scale),
                (int)Math.Round(rect.Height * scale));
        }

        private void OnTabCloseClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Models.TabItem tab)
            {
                int index = ViewModel.Tabs.IndexOf(tab);
                if (index >= 0)
                {
                    if (tab.ViewMode == ViewMode.Settings)
                    {
                        // Settings 탭은 Miller/Details/Icon 패널 없으므로 제거 스킵
                        // 임시로 활성 탭 인덱스 보정 후 CloseTab
                        ViewModel.CloseTab(index);
                        if (ViewModel.ActiveTab != null && ViewModel.ActiveTab.ViewMode != ViewMode.Settings)
                        {
                            SwitchMillerPanel(ViewModel.ActiveTab.Id);
                        }
                    }
                    else
                    {
                        // 패널 제거 (닫히는 탭)
                        RemoveMillerPanel(tab.Id);
                        RemoveDetailsPanel(tab.Id);
                        RemoveListPanel(tab.Id);
                        RemoveIconPanel(tab.Id);
                        ViewModel.CloseTab(index);
                        // CloseTab이 SwitchToTab을 호출하면 활성 탭이 변경됨 — 패널 전환
                        if (ViewModel.ActiveTab != null)
                        {
                            SwitchMillerPanel(ViewModel.ActiveTab.Id);
                            SwitchDetailsPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.Details);
                            SwitchListPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.List);
                            SwitchIconPanel(ViewModel.ActiveTab.Id, Helpers.ViewModeExtensions.IsIconMode(ViewModel.ActiveTab.ViewMode));
                        }
                    }
                    ResubscribeLeftExplorer();
                    UpdateViewModeVisibility();
                    FocusActiveView();
                    // Tab count changed — update passthrough region
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
                }
            }
        }

        private void OnNewTabClick(object sender, RoutedEventArgs e)
        {
            ViewModel.AddNewTab();
            // 새 탭의 패널 생성 및 전환
            var newTab = ViewModel.ActiveTab;
            if (newTab != null)
            {
                CreateMillerPanelForTab(newTab);
                SwitchMillerPanel(newTab.Id);
                // Details/Icon은 ViewMode 전환 시 lazy 생성 (새 탭은 보통 Home 또는 Miller)
                SwitchDetailsPanel(newTab.Id, newTab.ViewMode == ViewMode.Details);
                SwitchListPanel(newTab.Id, newTab.ViewMode == ViewMode.List);
                SwitchIconPanel(newTab.Id, Helpers.ViewModeExtensions.IsIconMode(newTab.ViewMode));
            }
            ResubscribeLeftExplorer();
            UpdateViewModeVisibility();
            FocusActiveView();
            // Tab count changed — update passthrough region
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
        }

        // =================================================================
        //  Tab Context Menu (Right-click on tab)
        // =================================================================

        private void OnTabRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Models.TabItem tab)
            {
                e.Handled = true;

                var flyout = new MenuFlyout();

                // Close Tab
                var closeItem = new MenuFlyoutItem
                {
                    Text = _loc.Get("CloseTab"),
                    Icon = new FontIcon { Glyph = "\uE711" }
                };
                closeItem.Click += (s, args) =>
                {
                    int index = ViewModel.Tabs.IndexOf(tab);
                    if (index >= 0 && ViewModel.Tabs.Count > 1)
                    {
                        RemoveMillerPanel(tab.Id);
                        RemoveDetailsPanel(tab.Id);
                        RemoveListPanel(tab.Id);
                        RemoveIconPanel(tab.Id);
                        ViewModel.CloseTab(index);
                        if (ViewModel.ActiveTab != null)
                        {
                            SwitchMillerPanel(ViewModel.ActiveTab.Id);
                            SwitchDetailsPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.Details);
                            SwitchListPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.List);
                            SwitchIconPanel(ViewModel.ActiveTab.Id, Helpers.ViewModeExtensions.IsIconMode(ViewModel.ActiveTab.ViewMode));
                        }
                        ResubscribeLeftExplorer();
                        UpdateViewModeVisibility();
                        FocusActiveView();
                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
                    }
                };
                closeItem.IsEnabled = ViewModel.Tabs.Count > 1;
                flyout.Items.Add(closeItem);

                // Close Other Tabs
                var closeOthersItem = new MenuFlyoutItem
                {
                    Text = _loc.Get("CloseOtherTabs"),
                };
                closeOthersItem.Click += (s, args) =>
                {
                    var closedIds = ViewModel.CloseOtherTabs(tab);
                    foreach (var id in closedIds)
                    {
                        RemoveMillerPanel(id);
                        RemoveDetailsPanel(id);
                        RemoveListPanel(id);
                        RemoveIconPanel(id);
                    }
                    if (ViewModel.ActiveTab != null)
                    {
                        SwitchMillerPanel(ViewModel.ActiveTab.Id);
                        SwitchDetailsPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.Details);
                        SwitchListPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.List);
                        SwitchIconPanel(ViewModel.ActiveTab.Id, Helpers.ViewModeExtensions.IsIconMode(ViewModel.ActiveTab.ViewMode));
                    }
                    ResubscribeLeftExplorer();
                    UpdateViewModeVisibility();
                    FocusActiveView();
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
                };
                closeOthersItem.IsEnabled = ViewModel.Tabs.Count > 1;
                flyout.Items.Add(closeOthersItem);

                // Close Tabs to Right
                var closeRightItem = new MenuFlyoutItem
                {
                    Text = _loc.Get("CloseTabsToRight"),
                };
                int tabIndex = ViewModel.Tabs.IndexOf(tab);
                closeRightItem.Click += (s, args) =>
                {
                    var closedIds = ViewModel.CloseTabsToRight(tab);
                    foreach (var id in closedIds)
                    {
                        RemoveMillerPanel(id);
                        RemoveDetailsPanel(id);
                        RemoveListPanel(id);
                        RemoveIconPanel(id);
                    }
                    if (ViewModel.ActiveTab != null)
                    {
                        SwitchMillerPanel(ViewModel.ActiveTab.Id);
                        SwitchDetailsPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.Details);
                        SwitchListPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.List);
                        SwitchIconPanel(ViewModel.ActiveTab.Id, Helpers.ViewModeExtensions.IsIconMode(ViewModel.ActiveTab.ViewMode));
                    }
                    ResubscribeLeftExplorer();
                    UpdateViewModeVisibility();
                    FocusActiveView();
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
                };
                closeRightItem.IsEnabled = tabIndex < ViewModel.Tabs.Count - 1;
                flyout.Items.Add(closeRightItem);

                flyout.Items.Add(new MenuFlyoutSeparator());

                // Duplicate Tab
                var duplicateItem = new MenuFlyoutItem
                {
                    Text = _loc.Get("DuplicateTab"),
                    Icon = new FontIcon { Glyph = "\uE8C8" }
                };
                duplicateItem.Click += (s, args) =>
                {
                    HandleDuplicateTab(tab);
                };
                flyout.Items.Add(duplicateItem);

                flyout.ShowAt(fe, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(fe)
                });
            }
        }

        private void HandleDuplicateTab(Models.TabItem sourceTab)
        {
            var newTab = ViewModel.DuplicateTab(sourceTab);
            CreateMillerPanelForTab(newTab);
            SwitchMillerPanel(newTab.Id);
            SwitchDetailsPanel(newTab.Id, newTab.ViewMode == ViewMode.Details);
            SwitchIconPanel(newTab.Id, Helpers.ViewModeExtensions.IsIconMode(newTab.ViewMode));
            ResubscribeLeftExplorer();
            UpdateViewModeVisibility();
            FocusActiveView();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
        }

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
            // 활성 탭의 Miller ItemsControl 반환
            if (_activeMillerTabId != null && _tabMillerPanels.TryGetValue(_activeMillerTabId, out var panel))
                return panel.items;
            return MillerColumnsControl;
        }

        /// <summary>
        /// Returns the ScrollViewer for the currently active pane.
        /// </summary>
        private ScrollViewer GetActiveMillerScrollViewer()
        {
            if (ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right)
                return MillerScrollViewerRight;
            // 활성 탭의 ScrollViewer 반환
            if (_activeMillerTabId != null && _tabMillerPanels.TryGetValue(_activeMillerTabId, out var panel))
                return panel.scroller;
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
        /// Single mode nav/address bar: visible when NOT split AND NOT Settings mode (Home included)
        /// </summary>
        public Visibility IsSingleNonSettingsVisible(bool isSplitViewEnabled, Models.ViewMode mode)
            => (!isSplitViewEnabled && mode != Models.ViewMode.Settings) ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Left pane header (split mode): visible when split enabled (including Home mode for accent bar)
        /// </summary>
        public Visibility IsLeftPaneHeaderVisible(bool isSplitViewEnabled)
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

        private void OnMainViewModeMenuOpening(object sender, object e)
        {
            LocalizeViewMenuItems(MainVm_Miller, MainVm_Details, MainVm_Icons,
                MainVm_ExtraLarge, MainVm_Large, MainVm_Medium, MainVm_Small);
        }

        private void OnLeftPaneViewModeMenuOpening(object sender, object e)
        {
            ViewModel.ActivePane = ActivePane.Left;
            LocalizeViewMenuItems(LeftVm_Miller, LeftVm_Details, LeftVm_Icons,
                LeftVm_ExtraLarge, LeftVm_Large, LeftVm_Medium, LeftVm_Small);
        }

        private void OnRightPaneViewModeMenuOpening(object sender, object e)
        {
            ViewModel.ActivePane = ActivePane.Right;
            LocalizeViewMenuItems(RightVm_Miller, RightVm_Details, RightVm_Icons,
                RightVm_ExtraLarge, RightVm_Large, RightVm_Medium, RightVm_Small);
        }

        private void LocalizeViewMenuItems(
            MenuFlyoutItem miller, MenuFlyoutItem details, MenuFlyoutSubItem icons,
            MenuFlyoutItem extraLarge, MenuFlyoutItem large, MenuFlyoutItem medium, MenuFlyoutItem small)
        {
            miller.Text = _loc.Get("MillerColumns");
            details.Text = _loc.Get("Details");
            icons.Text = _loc.Get("Icons");
            extraLarge.Text = _loc.Get("ExtraLargeIcons");
            large.Text = _loc.Get("LargeIcons");
            medium.Text = _loc.Get("MediumIcons");
            small.Text = _loc.Get("SmallIcons");
        }

        private void LocalizeViewModeTooltips()
        {
            var tip = _loc.Get("ViewModeSwitch");
            ToolTipService.SetToolTip(ViewModeButton, tip);
            ToolTipService.SetToolTip(LeftViewModeButton, tip);
            ToolTipService.SetToolTip(RightViewModeButton, tip);
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
        /// Auto-scroll breadcrumb to the right end so the last segment is fully visible.
        /// Also defers overflow indicator update after scroll completes.
        /// </summary>
        private void OnBreadcrumbScrollerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ChangeView(sv.ScrollableWidth, null, null, true);
                DispatcherQueue.TryEnqueue(() => UpdateBreadcrumbOverflow(sv));
            }
        }

        private void OnBreadcrumbContentSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // When breadcrumb content changes, scroll to show the last segment
            if (sender is FrameworkElement fe && fe.Parent is ScrollViewer sv)
            {
                sv.ChangeView(sv.ScrollableWidth, null, null, true);
                DispatcherQueue.TryEnqueue(() => UpdateBreadcrumbOverflow(sv));
            }
        }

        /// <summary>
        /// Update overflow indicator visibility when breadcrumb is scrolled.
        /// Shows "…" at the left edge when earlier path segments are hidden.
        /// </summary>
        private void OnBreadcrumbScrollerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is ScrollViewer sv)
                UpdateBreadcrumbOverflow(sv);
        }

        /// <summary>
        /// Show/hide the overflow "…" indicator based on scroll position.
        /// When HorizontalOffset > 0, leftmost segments are hidden → show indicator.
        /// </summary>
        private static void UpdateBreadcrumbOverflow(ScrollViewer sv)
        {
            if (sv.Parent is not Grid grid) return;
            foreach (var child in grid.Children)
            {
                if (child is Border border && border.Tag as string == "overflow")
                {
                    border.Visibility = sv.HorizontalOffset > 0
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    break;
                }
            }
        }

        /// <summary>
        /// Breadcrumb click in per-pane path header.
        /// Detects which pane the button belongs to and navigates accordingly.
        /// </summary>
        private void OnPaneBreadcrumbClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string fullPath)
            {
                // 홈 breadcrumb 클릭 → 홈으로 전환
                if (fullPath == "::home::")
                {
                    ViewModel.SwitchViewMode(ViewMode.Home);
                    return;
                }

                // Detect pane from visual tree
                if (IsDescendant(RightPaneContainer, btn))
                {
                    ViewModel.ActivePane = ActivePane.Right;
                    _ = ViewModel.RightExplorer.NavigateToPath(fullPath);
                }
                else
                {
                    ViewModel.ActivePane = ActivePane.Left;
                    _ = ViewModel.LeftExplorer.NavigateToPath(fullPath);
                }
            }
        }

        /// <summary>
        /// Breadcrumb chevron click: show subfolders of this segment as a dropdown.
        /// Clicking a subfolder navigates into it, replacing the path from this point onward.
        /// </summary>
        private void OnBreadcrumbChevronClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string fullPath) return;

            try
            {
                // Show children (subfolders) of the clicked segment's path.
                if (!System.IO.Directory.Exists(fullPath)) return;

                string[] dirs;
                try
                {
                    dirs = System.IO.Directory.GetDirectories(fullPath);
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }

                if (dirs.Length == 0) return;

                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);

                // Determine which pane this breadcrumb belongs to
                bool isRight = IsDescendant(RightPaneContainer, btn);
                var explorer = isRight ? ViewModel.RightExplorer : ViewModel.Explorer;

                // Figure out which child is currently selected (the next segment in the path)
                string? currentChildPath = null;
                if (!string.IsNullOrEmpty(explorer.CurrentPath) &&
                    explorer.CurrentPath.StartsWith(fullPath, StringComparison.OrdinalIgnoreCase) &&
                    explorer.CurrentPath.Length > fullPath.TrimEnd('\\').Length + 1)
                {
                    // Extract the immediate child folder from the current path
                    string remainder = explorer.CurrentPath.Substring(fullPath.TrimEnd('\\').Length + 1);
                    string childName = remainder.Split('\\')[0];
                    currentChildPath = System.IO.Path.Combine(fullPath, childName);
                }

                var flyout = new MenuFlyout();

                foreach (var dir in dirs)
                {
                    var item = new MenuFlyoutItem { Text = System.IO.Path.GetFileName(dir) };
                    string dirPath = dir;

                    // Mark the currently active child with a checkmark
                    if (currentChildPath != null &&
                        dir.Equals(currentChildPath, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Icon = new FontIcon { Glyph = "\uE73E" };
                    }

                    item.Click += (s, args) =>
                    {
                        if (isRight)
                        {
                            ViewModel.ActivePane = ActivePane.Right;
                            _ = ViewModel.RightExplorer.NavigateToPath(dirPath);
                        }
                        else
                        {
                            ViewModel.ActivePane = ActivePane.Left;
                            _ = ViewModel.Explorer.NavigateToPath(dirPath);
                        }
                    };
                    flyout.Items.Add(item);
                }

                flyout.ShowAt(btn);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Breadcrumb] Chevron error: {ex.Message}");
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

            _ = ViewModel.RightExplorer.NavigateTo(new FolderItem { Name = name, Path = path });
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
                        GetActiveDetailsView()?.FocusListView();
                        break;

                    case Models.ViewMode.IconSmall:
                    case Models.ViewMode.IconMedium:
                    case Models.ViewMode.IconLarge:
                    case Models.ViewMode.IconExtraLarge:
                        GetActiveIconView()?.FocusGridView();
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

            // Initialize inline preview column
            InitializeInlinePreview();
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
        //  Inline Preview Column (inside Miller Columns)
        // =================================================================

        /// <summary>
        /// Initialize inline preview column by subscribing to SelectedFile changes on the active explorer.
        /// Called from InitializePreviewPanels and when explorer changes (tab switch, etc.).
        /// </summary>
        private void InitializeInlinePreview()
        {
            _inlinePreviewService ??= App.Current.Services.GetRequiredService<PreviewService>();

            // Subscribe to SelectedFile changes on the left explorer
            ViewModel.LeftExplorer.PropertyChanged += OnExplorerSelectedFileChanged;
        }

        /// <summary>
        /// Re-subscribe inline preview when explorer changes (tab switch).
        /// </summary>
        private void ResubscribeInlinePreview(ExplorerViewModel? oldExplorer, ExplorerViewModel newExplorer)
        {
            if (oldExplorer != null)
                oldExplorer.PropertyChanged -= OnExplorerSelectedFileChanged;

            newExplorer.PropertyChanged += OnExplorerSelectedFileChanged;

            // Update inline preview immediately with new explorer state
            UpdateInlinePreviewColumn(newExplorer.SelectedFile);
        }

        private void OnExplorerSelectedFileChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ExplorerViewModel.SelectedFile) &&
                e.PropertyName != nameof(ExplorerViewModel.ShowPreviewColumn))
                return;
            if (_isClosed) return;

            if (sender is ExplorerViewModel explorer)
            {
                UpdateInlinePreviewColumn(explorer.SelectedFile);
            }
        }

        /// <summary>
        /// Update the inline preview column content and visibility.
        /// Shows/hides the column and populates file info.
        /// </summary>
        private async void UpdateInlinePreviewColumn(FileViewModel? fileVm)
        {
            if (_isClosed) return;

            // Cancel any pending preview load
            _inlinePreviewCts?.Cancel();

            if (fileVm == null)
            {
                InlinePreviewColumn.Visibility = Visibility.Collapsed;
                return;
            }

            // Don't show inline preview when the right-side preview panel is already active
            bool previewPaneActive = (ViewModel.ActivePane == ActivePane.Left && ViewModel.IsLeftPreviewEnabled)
                                  || (ViewModel.ActivePane == ActivePane.Right && ViewModel.IsRightPreviewEnabled);
            if (previewPaneActive)
            {
                InlinePreviewColumn.Visibility = Visibility.Collapsed;
                return;
            }

            // Show the column and populate basic info
            InlinePreviewColumn.Visibility = Visibility.Visible;

            // Basic info (synchronous)
            InlinePreviewFileName.Text = fileVm.Name;
            InlinePreviewIcon.Glyph = fileVm.IconGlyph;
            InlinePreviewIcon.Foreground = fileVm.IconBrush;
            InlinePreviewFileType.Text = fileVm.FileType;
            InlinePreviewDateModified.Text = fileVm.DateModified;

            // Get metadata from PreviewService
            var metadata = _inlinePreviewService!.GetBasicMetadata(fileVm.Path);
            InlinePreviewFileSize.Text = metadata.SizeFormatted;
            InlinePreviewDateCreated.Text = metadata.Created.ToString("yyyy-MM-dd HH:mm");

            // Reset type-specific previews
            InlinePreviewImage.Visibility = Visibility.Collapsed;
            InlinePreviewImage.Source = null;
            InlinePreviewTextBorder.Visibility = Visibility.Collapsed;
            InlinePreviewText.Text = "";
            InlinePreviewThumbnail.Visibility = Visibility.Collapsed;
            InlinePreviewThumbnail.Source = null;
            InlinePreviewIcon.Visibility = Visibility.Visible;
            InlinePreviewDimensionsRow.Visibility = Visibility.Collapsed;
            InlinePreviewDimensions.Text = "";

            // Determine preview type and load async content
            var previewType = _inlinePreviewService.GetPreviewType(fileVm.Path, false);

            _inlinePreviewCts = new CancellationTokenSource();
            var ct = _inlinePreviewCts.Token;

            try
            {
                switch (previewType)
                {
                    case Models.PreviewType.Image:
                        var imageBitmap = await _inlinePreviewService.LoadImagePreviewAsync(fileVm.Path, 512, ct);
                        if (ct.IsCancellationRequested) return;
                        if (imageBitmap != null)
                        {
                            InlinePreviewImage.Source = imageBitmap;
                            InlinePreviewImage.Visibility = Visibility.Visible;
                            // Hide icon, show thumbnail in the header area
                            InlinePreviewIcon.Visibility = Visibility.Collapsed;
                            InlinePreviewThumbnail.Source = imageBitmap;
                            InlinePreviewThumbnail.Visibility = Visibility.Visible;
                        }
                        // Load dimensions
                        var imgMeta = await _inlinePreviewService.GetImageMetadataAsync(fileVm.Path, ct);
                        if (ct.IsCancellationRequested) return;
                        if (imgMeta != null)
                        {
                            InlinePreviewDimensions.Text = $"{imgMeta.Width} x {imgMeta.Height}";
                            InlinePreviewDimensionsRow.Visibility = Visibility.Visible;
                        }
                        break;

                    case Models.PreviewType.Text:
                        var text = await _inlinePreviewService.LoadTextPreviewAsync(fileVm.Path, ct);
                        if (ct.IsCancellationRequested) return;
                        if (text != null)
                        {
                            // Show only first ~2000 chars for inline preview
                            InlinePreviewText.Text = text.Length > 2000 ? text.Substring(0, 2000) + "\n..." : text;
                            InlinePreviewTextBorder.Visibility = Visibility.Visible;
                        }
                        break;

                    case Models.PreviewType.Pdf:
                        var pdfBitmap = await _inlinePreviewService.LoadPdfPreviewAsync(fileVm.Path, ct);
                        if (ct.IsCancellationRequested) return;
                        if (pdfBitmap != null)
                        {
                            InlinePreviewImage.Source = pdfBitmap;
                            InlinePreviewImage.Visibility = Visibility.Visible;
                        }
                        break;

                    case Models.PreviewType.Generic:
                    default:
                        // No additional preview content for generic files
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Normal — user selected another file quickly
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[InlinePreview] Error loading preview: {ex.Message}");
            }

            // Scroll the inline preview column into view
            if (!ct.IsCancellationRequested && InlinePreviewColumn.Visibility == Visibility.Visible)
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    try
                    {
                        var scrollViewer = GetActiveMillerScrollViewer();
                        scrollViewer?.ChangeView(scrollViewer.ScrollableWidth, null, null);
                    }
                    catch { }
                });
            }
        }

        /// <summary>
        /// Clean up inline preview resources.
        /// </summary>
        private void CleanupInlinePreview()
        {
            _inlinePreviewCts?.Cancel();
            _inlinePreviewCts?.Dispose();
            _inlinePreviewCts = null;

            if (ViewModel?.LeftExplorer != null)
                ViewModel.LeftExplorer.PropertyChanged -= OnExplorerSelectedFileChanged;
        }

        // =================================================================
        //  IContextMenuHost Implementation
        // =================================================================

        bool Services.IContextMenuHost.HasClipboardContent => _clipboardPaths.Count > 0;

        void Services.IContextMenuHost.PerformCut(string path)
        {
            _clipboardPaths.Clear();
            _clipboardPaths.Add(path);
            _isCutOperation = true;

            var dataPackage = new DataPackage();
            dataPackage.SetText(path);
            Clipboard.SetContent(dataPackage);
            Helpers.DebugLogger.Log($"[ContextMenu] Cut: {path}");
            UpdateToolbarButtonStates();
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
            UpdateToolbarButtonStates();
        }

        async void Services.IContextMenuHost.PerformPaste(string targetFolderPath)
        {
            if (_clipboardPaths.Count == 0) return;

            var router = App.Current.Services.GetRequiredService<FileSystemRouter>();
            Span.Services.FileOperations.IFileOperation op = _isCutOperation
                ? new Span.Services.FileOperations.MoveFileOperation(new List<string>(_clipboardPaths), targetFolderPath, router)
                : new Span.Services.FileOperations.CopyFileOperation(new List<string>(_clipboardPaths), targetFolderPath, router);

            await ViewModel.ExecuteFileOperationAsync(op);

            if (_isCutOperation) _clipboardPaths.Clear();
            UpdateToolbarButtonStates();

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

            var router = App.Current.Services.GetRequiredService<Services.FileSystemRouter>();
            var operation = new Services.FileOperations.DeleteFileOperation(
                new List<string> { path }, permanent: false, router: router);

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

        async void Services.IContextMenuHost.PerformNewFile(string parentFolderPath, string fileName)
        {
            string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            string ext = System.IO.Path.GetExtension(fileName);
            string newPath = System.IO.Path.Combine(parentFolderPath, fileName);

            int count = 1;
            while (System.IO.File.Exists(newPath))
            {
                newPath = System.IO.Path.Combine(parentFolderPath, $"{baseName} ({count}){ext}");
                count++;
            }

            try
            {
                var op = new Span.Services.FileOperations.NewFileOperation(newPath);
                var result = await op.ExecuteAsync();
                if (!result.Success) return;

                // Refresh column and start rename
                var columns = ViewModel.ActiveExplorer.Columns;
                var parentColumn = columns.FirstOrDefault(c =>
                    c.Path.Equals(parentFolderPath, StringComparison.OrdinalIgnoreCase));
                if (parentColumn != null)
                {
                    await parentColumn.ReloadAsync();
                    var newFile = parentColumn.Children.FirstOrDefault(c =>
                        c.Path.Equals(newPath, StringComparison.OrdinalIgnoreCase));
                    if (newFile != null)
                    {
                        parentColumn.SelectedChild = newFile;
                        newFile.BeginRename();
                        await System.Threading.Tasks.Task.Delay(100);
                        int colIndex = columns.IndexOf(parentColumn);
                        if (colIndex >= 0)
                            FocusRenameTextBox(colIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ContextMenu] NewFile error: {ex.Message}");
            }
        }

        async void Services.IContextMenuHost.PerformCompress(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;

            try
            {
                // ZIP name: first item name + .zip
                string firstPath = paths[0];
                string parentDir = System.IO.Path.GetDirectoryName(firstPath)!;
                string zipName = System.IO.Path.GetFileNameWithoutExtension(firstPath) + ".zip";
                string zipPath = System.IO.Path.Combine(parentDir, zipName);

                int count = 1;
                while (System.IO.File.Exists(zipPath))
                {
                    zipPath = System.IO.Path.Combine(parentDir,
                        System.IO.Path.GetFileNameWithoutExtension(firstPath) + $" ({count}).zip");
                    count++;
                }

                var op = new Span.Services.FileOperations.CompressOperation(paths, zipPath);
                var result = await op.ExecuteAsync();

                if (result.Success)
                {
                    // Refresh the active column
                    var columns = ViewModel.ActiveExplorer.Columns;
                    var parentColumn = columns.FirstOrDefault(c =>
                        c.Path.Equals(parentDir, StringComparison.OrdinalIgnoreCase));
                    if (parentColumn != null)
                        await parentColumn.ReloadAsync();
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ContextMenu] Compress error: {ex.Message}");
            }
        }

        async void Services.IContextMenuHost.PerformExtractHere(string zipPath)
        {
            if (string.IsNullOrEmpty(zipPath)) return;

            try
            {
                string parentDir = System.IO.Path.GetDirectoryName(zipPath)!;
                string folderName = System.IO.Path.GetFileNameWithoutExtension(zipPath);
                string destPath = System.IO.Path.Combine(parentDir, folderName);

                int count = 1;
                while (System.IO.Directory.Exists(destPath))
                {
                    destPath = System.IO.Path.Combine(parentDir, $"{folderName} ({count})");
                    count++;
                }

                var op = new Span.Services.FileOperations.ExtractOperation(zipPath, destPath);
                var result = await op.ExecuteAsync();

                if (result.Success)
                {
                    var columns = ViewModel.ActiveExplorer.Columns;
                    var parentColumn = columns.FirstOrDefault(c =>
                        c.Path.Equals(parentDir, StringComparison.OrdinalIgnoreCase));
                    if (parentColumn != null)
                        await parentColumn.ReloadAsync();
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ContextMenu] ExtractHere error: {ex.Message}");
            }
        }

        async void Services.IContextMenuHost.PerformExtractTo(string zipPath)
        {
            if (string.IsNullOrEmpty(zipPath)) return;

            try
            {
                // Use FolderPicker
                var picker = new Windows.Storage.Pickers.FolderPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                picker.FileTypeFilter.Add("*");

                // Initialize with window handle
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var folder = await picker.PickSingleFolderAsync();
                if (folder == null) return;

                string folderName = System.IO.Path.GetFileNameWithoutExtension(zipPath);
                string destPath = System.IO.Path.Combine(folder.Path, folderName);

                int count = 1;
                while (System.IO.Directory.Exists(destPath))
                {
                    destPath = System.IO.Path.Combine(folder.Path, $"{folderName} ({count})");
                    count++;
                }

                var op = new Span.Services.FileOperations.ExtractOperation(zipPath, destPath);
                var result = await op.ExecuteAsync();

                if (result.Success)
                {
                    // Navigate to extracted folder
                    ViewModel.ActiveExplorer.NavigateToPath(destPath);
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ContextMenu] ExtractTo error: {ex.Message}");
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

        async void Services.IContextMenuHost.RemoveRemoteConnection(string connectionId)
        {
            var connService = App.Current.Services.GetRequiredService<ConnectionManagerService>();
            var connInfo = ViewModel.SavedConnections.FirstOrDefault(c => c.Id == connectionId);
            string displayName = connInfo?.DisplayName ?? connectionId;

            var dialog = new ContentDialog
            {
                Title = _loc.Get("RemoveConnectionTitle"),
                Content = string.Format(_loc.Get("RemoveConnectionConfirm"), displayName),
                PrimaryButtonText = _loc.Get("Delete"),
                CloseButtonText = _loc.Get("Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // 활성 연결 해제
                if (connInfo != null)
                {
                    var router = App.Current.Services.GetRequiredService<FileSystemRouter>();
                    var uriPrefix = FileSystemRouter.GetUriPrefix(connInfo.ToUri());
                    router.UnregisterConnection(uriPrefix);
                }

                connService.RemoveConnection(connectionId);
                Helpers.DebugLogger.Log($"[Sidebar] 원격 연결 제거: {displayName}");
                ViewModel.ShowToast(string.Format(_loc.Get("ConnectionRemoved"), displayName));
            }
        }

        async void Services.IContextMenuHost.EditRemoteConnection(string connectionId)
        {
            var connService = App.Current.Services.GetRequiredService<ConnectionManagerService>();
            var existing = ViewModel.SavedConnections.FirstOrDefault(c => c.Id == connectionId);
            if (existing == null) return;

            var (result, updated, password, _) = await ShowConnectionDialog(existing);
            if (result != ContentDialogResult.Primary || updated == null) return;

            // SMB: 표시 이름 + UNC 경로만 업데이트
            if (updated.Protocol == Models.RemoteProtocol.SMB)
            {
                connService.UpdateConnection(updated);
                Helpers.DebugLogger.Log($"[Sidebar] SMB 연결 편집 완료: {updated.DisplayName}");
                return;
            }

            // SFTP/FTP: 속성 업데이트 + 비밀번호 저장
            connService.UpdateConnection(updated);
            if (!string.IsNullOrEmpty(password))
                connService.SaveCredential(updated.Id, password);

            Helpers.DebugLogger.Log($"[Sidebar] 원격 연결 편집 완료: {updated.DisplayName}");
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

        void Services.IContextMenuHost.PerformSelectAll()
        {
            HandleSelectAll();
        }

        void Services.IContextMenuHost.PerformSelectNone()
        {
            HandleSelectNone();
        }

        void Services.IContextMenuHost.PerformInvertSelection()
        {
            HandleInvertSelection();
        }

        // =================================================================
        //  Help / Settings / Log
        // =================================================================

        private bool _isHelpOpen = false;

        private void ToggleHelpOverlay()
        {
            _isHelpOpen = !_isHelpOpen;
            HelpOverlay.Visibility = _isHelpOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnHelpClick(object sender, RoutedEventArgs e)
        {
            ToggleHelpOverlay();
        }

        private void HelpOverlay_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isHelpOpen)
            {
                _isHelpOpen = false;
                HelpOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            OpenSettingsTab();
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

        // =================================================================
        //  P1 #12: Tab Re-docking — Merge torn-off tab back into window
        // =================================================================

        /// <summary>
        /// Accept a tab from another window and add it to this window's tab bar.
        /// Called by the drag timer when a torn-off window is dropped onto this window's tab bar.
        /// </summary>
        public void DockTab(Models.TabStateDto dto)
        {
            try
            {
                // Create a new tab from the DTO
                var root = new FolderItem { Name = "PC", Path = "PC" };
                var fileService = App.Current.Services.GetRequiredService<Services.FileSystemService>();
                var explorer = new ExplorerViewModel(root, fileService);

                var newTab = new TabItem
                {
                    Header = dto.Header,
                    Path = dto.Path,
                    ViewMode = (ViewMode)dto.ViewMode,
                    IconSize = (ViewMode)dto.IconSize,
                    IsActive = false,
                    Explorer = explorer
                };

                // Navigate if path is not empty
                if (!string.IsNullOrEmpty(dto.Path) && (ViewMode)dto.ViewMode != ViewMode.Home)
                {
                    explorer.EnableAutoNavigation = true;
                    _ = explorer.NavigateToPath(dto.Path);
                }

                // Add the tab and switch to it
                ViewModel.Tabs.Add(newTab);
                CreateMillerPanelForTab(newTab);
                SwitchMillerPanel(newTab.Id);
                SwitchDetailsPanel(newTab.Id, newTab.ViewMode == ViewMode.Details);
                SwitchListPanel(newTab.Id, newTab.ViewMode == ViewMode.List);
                SwitchIconPanel(newTab.Id, Helpers.ViewModeExtensions.IsIconMode(newTab.ViewMode));
                ViewModel.SwitchToTab(ViewModel.Tabs.Count - 1);
                ResubscribeLeftExplorer();
                UpdateViewModeVisibility();
                FocusActiveView();

                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);

                Helpers.DebugLogger.Log($"[ReDock] Tab '{dto.Header}' docked into window (total: {ViewModel.Tabs.Count})");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ReDock] Error docking tab: {ex.Message}");
            }
        }

        // =================================================================
        //  P1 #15: Ctrl+D — Duplicate selected file/folder
        // =================================================================

        private async void HandleDuplicateFile()
        {
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0)
            {
                var sel = GetCurrentSelected();
                if (sel != null) selectedItems = new List<FileSystemViewModel> { sel };
            }
            if (selectedItems.Count == 0) return;

            var suffix = _loc.Get("DuplicateSuffix"); // " - Copy" / " - 복사본" / " - コピー"
            var paths = selectedItems.Select(item => item.Path).ToList();

            foreach (var srcPath in paths)
            {
                try
                {
                    bool isDir = System.IO.Directory.Exists(srcPath);
                    string dir = System.IO.Path.GetDirectoryName(srcPath) ?? "";
                    string nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(srcPath);
                    string ext = System.IO.Path.GetExtension(srcPath);

                    // Generate unique name: "file - Copy.txt", "file - Copy (2).txt", ...
                    string destPath;
                    if (isDir)
                    {
                        destPath = System.IO.Path.Combine(dir, nameWithoutExt + suffix);
                        int counter = 2;
                        while (System.IO.Directory.Exists(destPath))
                        {
                            destPath = System.IO.Path.Combine(dir, $"{nameWithoutExt}{suffix} ({counter})");
                            counter++;
                        }
                        await System.Threading.Tasks.Task.Run(() => CopyDirectoryRecursive(srcPath, destPath));
                    }
                    else
                    {
                        destPath = System.IO.Path.Combine(dir, nameWithoutExt + suffix + ext);
                        int counter = 2;
                        while (System.IO.File.Exists(destPath))
                        {
                            destPath = System.IO.Path.Combine(dir, $"{nameWithoutExt}{suffix} ({counter}){ext}");
                            counter++;
                        }
                        await System.Threading.Tasks.Task.Run(() => System.IO.File.Copy(srcPath, destPath));
                    }

                    Helpers.DebugLogger.Log($"[Duplicate] {srcPath} → {destPath}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[Duplicate] Error: {ex.Message}");
                }
            }

            // Refresh current folder
            var explorer = ViewModel.ActiveExplorer;
            int colIndex = GetCurrentColumnIndex();
            if (colIndex >= 0 && colIndex < explorer.Columns.Count)
            {
                await explorer.Columns[colIndex].RefreshAsync();
            }

            ViewModel.ShowToast(paths.Count == 1
                ? $"\"{System.IO.Path.GetFileName(paths[0])}\" {_loc.Get("Duplicated") ?? "duplicated"}"
                : $"{paths.Count} items duplicated");
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            System.IO.Directory.CreateDirectory(destDir);
            foreach (var file in System.IO.Directory.GetFiles(sourceDir))
            {
                System.IO.File.Copy(file, System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(file)));
            }
            foreach (var dir in System.IO.Directory.GetDirectories(sourceDir))
            {
                CopyDirectoryRecursive(dir, System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(dir)));
            }
        }

        // =================================================================
        //  P1 #18: Alt+Enter — Show Windows Properties dialog
        // =================================================================

        private void HandleShowProperties()
        {
            var selectedItems = GetCurrentSelectedItems();
            if (selectedItems.Count == 0)
            {
                var sel = GetCurrentSelected();
                if (sel != null) selectedItems = new List<FileSystemViewModel> { sel };
            }

            var shellService = App.Current.Services.GetRequiredService<Services.ShellService>();

            if (selectedItems.Count > 0)
            {
                // Show properties for first selected item
                shellService.ShowProperties(selectedItems[0].Path);
            }
            else
            {
                // No selection: show properties for current folder
                var folderPath = ViewModel.ActiveExplorer?.CurrentFolder?.Path;
                if (!string.IsNullOrEmpty(folderPath))
                    shellService.ShowProperties(folderPath);
            }
        }

    }
}
