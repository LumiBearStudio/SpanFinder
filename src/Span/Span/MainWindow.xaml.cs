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
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Hosting;
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
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private IntPtr _hwnd;
        private SUBCLASSPROC? _subclassProc; // prevent GC collection
        private DispatcherTimer? _deviceChangeDebounceTimer;
        private DispatcherTimer? _drivePollingTimer;
        private HashSet<char> _lastKnownDriveLetters = new();

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
        private static readonly Thickness _zeroPadding = new(0);

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

            // 좌/우 탐색기 패널 포커스: handledEventsToo=true로 등록해야
            // ListView/ScrollViewer가 이벤트를 처리한 후에도 Pane 포커스 전환 가능
            LeftPaneContainer.AddHandler(UIElement.PointerPressedEvent,
                new Microsoft.UI.Xaml.Input.PointerEventHandler(OnLeftPanePointerPressed), true);
            RightPaneContainer.AddHandler(UIElement.PointerPressedEvent,
                new Microsoft.UI.Xaml.Input.PointerEventHandler(OnRightPanePointerPressed), true);

            ViewModel = App.Current.Services.GetRequiredService<MainViewModel>();
            _contextMenuService = App.Current.Services.GetRequiredService<Services.ContextMenuService>();
            _loc = App.Current.Services.GetRequiredService<Services.LocalizationService>();
            _settings = App.Current.Services.GetRequiredService<Services.SettingsService>();

            // Subscribe to file open events for toast feedback
            var shellService = App.Current.Services.GetRequiredService<ShellService>();
            shellService.FileOpening += OnShellFileOpening;

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
            _contextMenuService.XamlRootProvider = () => Content.XamlRoot;
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

            // Periodic drive polling: detect virtual drive mount/unmount
            // (Google Drive, OneDrive, etc. don't fire WM_DEVICECHANGE)
            _lastKnownDriveLetters = new HashSet<char>(
                System.IO.DriveInfo.GetDrives().Select(d => d.Name[0]));
            _drivePollingTimer = new DispatcherTimer();
            _drivePollingTimer.Interval = TimeSpan.FromSeconds(5);
            _drivePollingTimer.Tick += OnDrivePollingTick;
            _drivePollingTimer.Start();

            // ── Restore window position ──
            // Cloak the window so the user never sees the WinUI default size.
            // Activate() resets the size, but the Loaded handler re-applies
            // the saved placement and then uncloaks.
            if (_settings.RememberWindowPosition)
            {
                int cloakOn = 1;
                Helpers.NativeMethods.DwmSetWindowAttribute(
                    _hwnd, Helpers.NativeMethods.DWMWA_CLOAK, ref cloakOn, sizeof(int));
                RestoreWindowPlacement();
            }

            // Initialize preview panels
            InitializePreviewPanels();

            // Apply saved settings
            ApplyTheme(_settings.Theme);
            ApplyFontFamily(_settings.FontFamily);
            ApplyDensity(_settings.Density);
            _settings.SettingChanged += OnSettingChanged;

            // Connect Language setting to LocalizationService
            // "system" resolves to OS locale via ResolveSystemLanguage()
            _loc.Language = _settings.Language;
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

                        // Uncloak if cloaked during constructor (RememberWindowPosition)
                        if (_settings.RememberWindowPosition)
                        {
                            int cloakOff = 0;
                            Helpers.NativeMethods.DwmSetWindowAttribute(
                                _hwnd, Helpers.NativeMethods.DWMWA_CLOAK, ref cloakOff, sizeof(int));
                        }

                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                            () => FocusActiveView());
                        return;
                    }

                    // ── Re-apply window placement after Activate + layout, then uncloak ──
                    if (!_isTearOffWindow && _settings.RememberWindowPosition)
                    {
                        RestoreWindowPlacement();
                        DispatcherQueue.TryEnqueue(
                            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                            () =>
                            {
                                if (!_isClosed && _settings.RememberWindowPosition)
                                    RestoreWindowPlacement();

                                // Uncloak — window is now at the correct size
                                int cloakOff = 0;
                                Helpers.NativeMethods.DwmSetWindowAttribute(
                                    _hwnd, Helpers.NativeMethods.DWMWA_CLOAK, ref cloakOff, sizeof(int));
                            });
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

                    // Restore saved sort/group settings
                    try
                    {
                        var appSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                        if (appSettings.Values.TryGetValue("MillerSortBy", out var sby) && sby is string sortField)
                        {
                            _currentSortField = sortField switch { "DateModified" => "Date", _ => sortField };
                        }
                        if (appSettings.Values.TryGetValue("MillerSortAsc", out var sasc) && sasc is bool sortAsc)
                            _currentSortAscending = sortAsc;
                        if (appSettings.Values.TryGetValue("ViewGroupBy", out var vgb) && vgb is string grp)
                            _currentGroupBy = grp;
                        UpdateSortButtonIcons();
                    }
                    catch { }

                    // FileSystemWatcher 초기화
                    InitializeFileSystemWatcher();
                };
            }
        }

        #region Window Placement Persistence

        private void SaveWindowPlacement()
        {
            try
            {
                if (IsIconic(_hwnd) || IsZoomed(_hwnd)) return; // 최소화/최대화 상태는 저장 안 함
                if (!GetWindowRect(_hwnd, out var rect)) return;

                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                var composite = new Windows.Storage.ApplicationDataCompositeValue
                {
                    ["X"] = rect.Left,
                    ["Y"] = rect.Top,
                    ["Width"] = rect.Right - rect.Left,
                    ["Height"] = rect.Bottom - rect.Top
                };
                settings.Values["WindowPlacement"] = composite;
                var dpi = Helpers.NativeMethods.GetDpiForWindow(_hwnd);
                Helpers.DebugLogger.Log($"[Window] Saved placement: {rect.Left},{rect.Top} {rect.Right - rect.Left}x{rect.Bottom - rect.Top} (DPI={dpi})");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Window] SavePlacement error: {ex.Message}");
            }
        }

        private void RestoreWindowPlacement()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values["WindowPlacement"] is not Windows.Storage.ApplicationDataCompositeValue composite)
                    return;

                if (composite.TryGetValue("X", out var xObj) && xObj is int x &&
                    composite.TryGetValue("Y", out var yObj) && yObj is int y &&
                    composite.TryGetValue("Width", out var wObj) && wObj is int w &&
                    composite.TryGetValue("Height", out var hObj) && hObj is int h)
                {
                    // 최소 크기 보장
                    if (w < 400) w = 400;
                    if (h < 300) h = 300;

                    // Win32 SetWindowPos 사용 (물리 픽셀 직접 지정)
                    // AppWindow.MoveAndResize는 DPI 이중적용 버그 있음
                    Helpers.NativeMethods.SetWindowPos(
                        _hwnd, Helpers.NativeMethods.HWND_TOP,
                        x, y, w, h,
                        Helpers.NativeMethods.SWP_NOZORDER | Helpers.NativeMethods.SWP_NOACTIVATE);

                    // 복원 후 실제 크기 확인
                    GetWindowRect(_hwnd, out var verifyRect);
                    var dpi = Helpers.NativeMethods.GetDpiForWindow(_hwnd);
                    Helpers.DebugLogger.Log($"[Window] Restored target: {x},{y} {w}x{h} | actual: {verifyRect.Left},{verifyRect.Top} {verifyRect.Right - verifyRect.Left}x{verifyRect.Bottom - verifyRect.Top} (DPI={dpi})");
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Window] RestorePlacement error: {ex.Message}");
            }
        }

        #endregion

        private void OnClosed(object sender, WindowEventArgs args)
        {
            try
            {
                Helpers.DebugLogger.Log("[MainWindow.OnClosed] Starting cleanup...");

                // STEP 0: Block all queued DispatcherQueue callbacks and async continuations
                _isClosed = true;

                // Save window position/size (skip for tear-off windows)
                if (!_isTearOffWindow && _settings.RememberWindowPosition)
                    SaveWindowPlacement();

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

                // Unsubscribe file open toast
                try
                {
                    var shellService = App.Current.Services.GetRequiredService<ShellService>();
                    shellService.FileOpening -= OnShellFileOpening;
                }
                catch { }

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
                    if (_drivePollingTimer != null)
                    {
                        _drivePollingTimer.Stop();
                        _drivePollingTimer.Tick -= OnDrivePollingTick;
                        _drivePollingTimer = null;
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

        /// <summary>
        /// Lightweight poll: compare drive letters to detect virtual drive mount/unmount.
        /// </summary>
        private void OnDrivePollingTick(object? sender, object e)
        {
            if (_isClosed) return;
            try
            {
                var current = new HashSet<char>(
                    System.IO.DriveInfo.GetDrives().Select(d => d.Name[0]));
                if (!current.SetEquals(_lastKnownDriveLetters))
                {
                    Helpers.DebugLogger.Log($"[MainWindow] Drive poll: letters changed ({string.Join(",", _lastKnownDriveLetters)} → {string.Join(",", current)})");
                    _lastKnownDriveLetters = current;
                    ViewModel.RefreshDrives();
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainWindow] Drive poll error: {ex.Message}");
            }
        }

        // =================================================================
        //  Settings
        // =================================================================

        // 커스텀 테마 목록 (Dark 기반 + 리소스 오버라이드)
        private static readonly HashSet<string> _customThemes = new() { "dracula", "tokyonight", "catppuccin", "gruvbox" };















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

            // Column slide-in animation: only for Add when not the root column
            if (e.Action == NotifyCollectionChangedAction.Add &&
                ViewModel.LeftExplorer.Columns.Count > 1)
            {
                var control = GetActiveMillerColumnsControl();
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    var container = control.ContainerFromIndex(control.Items.Count - 1);
                    if (container is UIElement el)
                        AnimateColumnEntrance(el);
                });
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

            // Column slide-in animation for right pane
            if (e.Action == NotifyCollectionChangedAction.Add &&
                ViewModel.RightExplorer.Columns.Count > 1)
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    var container = MillerColumnsControlRight.ContainerFromIndex(
                        MillerColumnsControlRight.Items.Count - 1);
                    if (container is UIElement el)
                        AnimateColumnEntrance(el);
                });
            }
        }

        private static void AnimateColumnEntrance(UIElement element)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            // Apple-style deceleration curve
            var easing = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1.0f));

            // Opacity: 0 → 1
            var fadeIn = compositor.CreateScalarKeyFrameAnimation();
            fadeIn.InsertKeyFrame(0f, 0f);
            fadeIn.InsertKeyFrame(1f, 1f, easing);
            fadeIn.Duration = TimeSpan.FromMilliseconds(250);

            // Translation.X: 20 → 0
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            var slideIn = compositor.CreateScalarKeyFrameAnimation();
            slideIn.InsertKeyFrame(0f, 20f);
            slideIn.InsertKeyFrame(1f, 0f, easing);
            slideIn.Duration = TimeSpan.FromMilliseconds(250);

            visual.StartAnimation("Opacity", fadeIn);
            visual.StartAnimation("Translation.X", slideIn);
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
                            leftExplorer.NotifyCurrentItemsChanged();
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
                                rightExplorer.NotifyCurrentItemsChanged();
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

            // Settings 모드: 스플릿뷰 강제 해제
            if (isSpecialMode && ViewModel.IsSplitViewEnabled)
            {
                ViewModel.IsSplitViewEnabled = false;
                SplitterCol.Width = new GridLength(0);
                RightPaneCol.Width = new GridLength(0);
                ViewModel.ActivePane = ActivePane.Left;
            }

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

        // ScrollToLastColumn, ScrollToLastColumnSync, GetTotalColumnsActualWidth → MainWindow.NavigationManager.cs


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
        /// 사이드바 섹션 헤더 접기/펴기 토글
        /// </summary>
        private void OnSidebarSectionHeaderTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.Tag is string tag)
            {
                switch (tag)
                {
                    case "Local":   ViewModel.IsLocalDrivesExpanded = !ViewModel.IsLocalDrivesExpanded; break;
                    case "Cloud":   ViewModel.IsCloudDrivesExpanded = !ViewModel.IsCloudDrivesExpanded; break;
                    case "Network": ViewModel.IsNetworkDrivesExpanded = !ViewModel.IsNetworkDrivesExpanded; break;
                }
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

            ViewModel.ShowToast(string.Format(_loc.Get("Toast_Connected"), connInfo.DisplayName));

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
                ViewModel.ShowToast(_loc.Get("Toast_ConnectionNotFound"));
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
                var passwordInput = new PasswordBox { PlaceholderText = _loc.Get("Password") };
                var dialog = new ContentDialog
                {
                    Title = string.Format(_loc.Get("ConnectionTitle"), connInfo.DisplayName),
                    Content = passwordInput,
                    PrimaryButtonText = _loc.Get("Connect"),
                    CloseButtonText = _loc.Get("Cancel"),
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
                await ShowRemoteConnectionError(connInfo, string.Format(_loc.Get("Toast_AuthFailed"), ex.Message));
                return;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                await ShowRemoteConnectionError(connInfo, string.Format(_loc.Get("Toast_SocketError"), connInfo.Host, connInfo.Port, ex.Message));
                return;
            }
            catch (TimeoutException ex)
            {
                await ShowRemoteConnectionError(connInfo, string.Format(_loc.Get("Toast_TimeoutError"), ex.Message));
                return;
            }
            catch (Exception ex)
            {
                await ShowRemoteConnectionError(connInfo, string.Format(_loc.Get("Toast_ConnectionError"), ex.Message));
                return;
            }

            // 연결 성공 → Router에 등록 + 네비게이션
            router.RegisterConnection(uriPrefix, provider);
            connInfo.LastConnected = DateTime.Now;
            _ = connService.SaveConnectionsAsync();

            ViewModel.ShowToast(string.Format(_loc.Get("Toast_Connected"), connInfo.DisplayName));

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
                NavigateToFavorite(fav);
        }

        private void OnFavoritesFlatItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FavoriteItem fav)
                NavigateToFavorite(fav);
        }

        private void NavigateToFavorite(FavoriteItem fav)
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

        private void OnFavoritesFlatListRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            // ListView의 우클릭 → 클릭된 아이템에서 컨텍스트 메뉴 표시
            if (e.OriginalSource is FrameworkElement fe)
            {
                var fav = FindParentDataContext<FavoriteItem>(fe);
                if (fav != null)
                {
                    var flyout = _contextMenuService.BuildFavoriteMenu(fav, this);
                    flyout.ShowAt(fe, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                    {
                        Position = e.GetPosition(fe)
                    });
                    e.Handled = true;
                }
            }
        }

        private static T? FindParentDataContext<T>(FrameworkElement fe) where T : class
        {
            var current = fe;
            while (current != null)
            {
                if (current.DataContext is T item) return item;
                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current) as FrameworkElement;
            }
            return null;
        }

        private void OnFavoritesDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            // 드래그 리오더 완료 후 즐겨찾기 저장
            var favService = App.Current.Services.GetService(typeof(Services.IFavoritesService)) as Services.IFavoritesService;
            favService?.SaveFavorites(ViewModel.Favorites.ToList());
            Helpers.DebugLogger.Log($"[Favorites] Reordered and saved ({ViewModel.Favorites.Count} items)");
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

            var helper = new Helpers.RubberBandSelectionHelper(
                grid,
                listView,
                () => _isSyncingSelection,
                val => _isSyncingSelection = val);

            _rubberBandHelpers[grid] = helper;
        }

        private void OnMillerContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            // 재활용 큐: 화면 밖 아이템의 썸네일 해제 (메모리 절약)
            if (args.InRecycleQueue)
            {
                if (args.Item is ViewModels.FileViewModel recycledFile)
                    recycledFile.UnloadThumbnail();
                return;
            }

            if (args.ItemContainer is ListViewItem item)
            {
                // Reset any stale padding on the template root Grid (ContentBorder)
                var rootGrid = FindChild<Grid>(item);
                if (rootGrid != null && rootGrid.Padding != _zeroPadding)
                    rootGrid.Padding = _zeroPadding;

                // Apply density padding to the DATA TEMPLATE Grid (inside ContentPresenter),
                // NOT the template root Grid (ContentBorder).
                var cp = FindChild<ContentPresenter>(item);
                if (cp != null)
                {
                    var grid = FindChild<Grid>(cp);
                    if (grid != null) grid.Padding = _densityPadding;
                }
            }

            // On-demand 썸네일 로딩: 보이는 아이템만 로드
            if (args.Item is ViewModels.FileViewModel fileVm && fileVm.IsThumbnailSupported && !fileVm.HasThumbnail)
            {
                _ = fileVm.LoadThumbnailAsync();
            }

            // On-demand 클라우드 + Git 상태 주입: 보이는 아이템만
            if (args.Item is ViewModels.FileSystemViewModel fsVm
                && sender.DataContext is ViewModels.FolderViewModel folderVm)
            {
                folderVm.InjectCloudStateIfNeeded(fsVm);
                folderVm.InjectGitStateIfNeeded(fsVm);
            }
        }

        private void OnMillerColumnContentGridUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid grid) return;

            if (_rubberBandHelpers.TryGetValue(grid, out var helper))
            {
                helper.Detach();
                _rubberBandHelpers.Remove(grid);
            }
        }

        private async void OnFolderRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (!_settings.ShowContextMenu) return;
            if (sender is Grid grid && grid.DataContext is FolderViewModel folder)
            {
                e.Handled = true; // Prevent bubbling to empty area handler during await
                var flyout = await _contextMenuService.BuildFolderMenuAsync(folder, this);
                flyout.ShowAt(grid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(grid)
                });
            }
        }

        private async void OnFileRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (!_settings.ShowContextMenu) return;
            if (sender is Grid grid && grid.DataContext is FileViewModel file)
            {
                e.Handled = true; // Prevent bubbling to empty area handler during await
                var flyout = await _contextMenuService.BuildFileMenuAsync(file, this);
                flyout.ShowAt(grid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(grid)
                });
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

        // =================================================================
        //  Keyboard Handlers -> MainWindow.KeyboardHandler.cs
        //  (OnGlobalKeyDown, OnGlobalPointerPressed, OnMillerKeyDown,
        //   HandleRightArrow, HandleLeftArrow, HandleEnter, HandleTypeAhead,
        //   HandleQuickLook, BuildQuickLookContentAsync, WrapWithMetadata,
        //   CreateGenericPreview, KeyToChar)
        // =================================================================

        // =================================================================
        //  P1: Clipboard (Ctrl+C/X/V)
        // =================================================================

        // =================================================================
        //  Select All (Ctrl+A)
        // =================================================================


        // =================================================================
        //  Select None (Ctrl+Shift+A)
        // =================================================================


        // =================================================================
        //  Invert Selection (Ctrl+I)
        // =================================================================


        // =================================================================
        //  Helper: Get current selected items (multi or single)
        // =================================================================






        // =================================================================
        //  P1: New Folder (Ctrl+Shift+N)
        // =================================================================


        // =================================================================
        //  P1: Refresh (F5)
        // =================================================================


        // =================================================================
        //  P2: Rename (F2) — 인라인 이름 변경
        // =================================================================









        // =================================================================
        //  P2: Delete (Delete key)
        // =================================================================





        // =================================================================
        //  Search Box
        // =================================================================


        // ── Search Filter State ──



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
                        else if (newSelection is ViewModels.FolderViewModel clickedFolder)
                        {
                            // Already selected folder clicked again — force navigation
                            // if child column doesn't exist yet (e.g. auto-selected without navigation)
                            var explorer = ViewModel.ActiveExplorer;
                            if (explorer != null)
                            {
                                int colIdx = explorer.Columns.IndexOf(folderVm);
                                if (colIdx >= 0 && colIdx + 1 >= explorer.Columns.Count)
                                {
                                    // Reset and re-set to trigger PropertyChanged
                                    folderVm.SelectedChild = null;
                                    folderVm.SelectedChild = clickedFolder;
                                }
                            }
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
                    // Open file with default application via ShellExecute (faster than WinRT Launcher)
                    var shellService = App.Current.Services.GetRequiredService<ShellService>();
                    shellService.OpenFile(file.Path);
                    Helpers.DebugLogger.Log($"[MainWindow] Miller Column DoubleClick: Opening file {file.Name}");
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

        private async void OnRetryFolderLoad(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.Controls.HyperlinkButton btn && btn.Tag is FolderViewModel folder)
            {
                folder.ResetLoadState();
                await folder.EnsureChildrenLoadedAsync();
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

        // ============================================================
        //  Breadcrumb Address Bar 핸들러
        // ============================================================








        // =================================================================
        //  Back/Forward History Dropdown (right-click on nav buttons)
        // =================================================================















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









        // Sort handlers










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

        // Tab management methods moved to MainWindow.TabManager.cs

        // =================================================================
        //  Per-Tab Miller Panel Management (Show/Hide pattern)
        // =================================================================





        // =================================================================
        //  Per-Tab Details Panel Management (Show/Hide pattern)
        // =================================================================




        // =================================================================
        //  Per-Tab List Panel Management (Show/Hide pattern)
        // =================================================================




        // =================================================================
        //  Per-Tab Icon Panel Management (Show/Hide pattern)
        // =================================================================




        // =================================================================
        //  Tab Event Handlers
        // =================================================================













        // =================================================================
        //  Tab Context Menu (Right-click on tab)
        // =================================================================



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

            // Group By checkmarks
            GroupByNoneItem.KeyboardAcceleratorTextOverride = _currentGroupBy == "None" ? "✓" : string.Empty;
            GroupByNameItem.KeyboardAcceleratorTextOverride = _currentGroupBy == "Name" ? "✓" : string.Empty;
            GroupByTypeItem.KeyboardAcceleratorTextOverride = _currentGroupBy == "Type" ? "✓" : string.Empty;
            GroupByDateItem.KeyboardAcceleratorTextOverride = _currentGroupBy == "DateModified" ? "✓" : string.Empty;
            GroupBySizeItem.KeyboardAcceleratorTextOverride = _currentGroupBy == "Size" ? "✓" : string.Empty;

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



        // --- x:Bind visibility/brush helpers ---








        // --- Focus tracking ---







        // --- Pane-specific flyout opening handlers (set ActivePane before menu item click) ---















        // --- Split View Toggle ---







        // =================================================================
        //  Preview Panel
        // =================================================================














        // =================================================================
        //  Inline Preview Column (inside Miller Columns)
        // =================================================================






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
            List<string> sourcePaths;
            bool isCut;

            if (_clipboardPaths.Count > 0)
            {
                // Internal clipboard (Span → Span)
                sourcePaths = new List<string>(_clipboardPaths);
                isCut = _isCutOperation;
            }
            else
            {
                // External clipboard (Windows Explorer → Span)
                try
                {
                    var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                    if (!content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;

                    var items = await content.GetStorageItemsAsync();
                    sourcePaths = items
                        .Select(i => i.Path)
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToList();
                    if (sourcePaths.Count == 0) return;

                    isCut = content.RequestedOperation.HasFlag(
                        Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move);
                }
                catch { return; }
            }

            // Find target column index for targeted refresh
            int? targetColumnIndex = null;
            var columns = ViewModel.ActiveExplorer.Columns;
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].Path.Equals(targetFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    targetColumnIndex = i;
                    break;
                }
            }

            var router = App.Current.Services.GetRequiredService<FileSystemRouter>();
            Span.Services.FileOperations.IFileOperation op = isCut
                ? new Span.Services.FileOperations.MoveFileOperation(sourcePaths, targetFolderPath, router)
                : new Span.Services.FileOperations.CopyFileOperation(sourcePaths, targetFolderPath, router);

            await ViewModel.ExecuteFileOperationAsync(op, targetColumnIndex);

            if (isCut && _clipboardPaths.Count > 0) _clipboardPaths.Clear();
            UpdateToolbarButtonStates();
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
                var shellService = App.Current.Services.GetRequiredService<ShellService>();
                shellService.OpenFile(file.Path);
            }
        }

        private void OnShellFileOpening(string fileName)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed) return;
                ViewModel?.ShowToast($"\"{fileName}\" {_loc.Get("Opening")}...", 2000);
            });
        }

        void Services.IContextMenuHost.PerformOpenDrive(DriveItem drive)
        {
            ViewModel.OpenDrive(drive);
            FocusColumnAsync(0);
        }

        void Services.IContextMenuHost.PerformEjectDrive(DriveItem drive)
        {
            var shellService = App.Current.Services.GetRequiredService<ShellService>();
            shellService.EjectDrive(drive.Path);
            // WM_DEVICECHANGE 이벤트가 자동으로 드라이브 목록 갱신
        }

        void Services.IContextMenuHost.PerformDisconnectDrive(DriveItem drive)
        {
            var shellService = App.Current.Services.GetRequiredService<ShellService>();
            if (shellService.DisconnectNetworkDrive(drive.Path))
                ViewModel.RefreshDrives();
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

        // Group By state
        private string _currentGroupBy = "None";

        string Services.IContextMenuHost.CurrentGroupBy => _currentGroupBy;

        void Services.IContextMenuHost.ApplyGroupBy(string groupBy)
        {
            _currentGroupBy = groupBy;

            // Details 뷰 — 자체 GroupBy 시스템 사용
            var detailsView = GetActiveDetailsView();
            if (detailsView != null && ViewModel.CurrentViewMode == Models.ViewMode.Details)
            {
                detailsView.SetGroupByPublic(groupBy);
                return;
            }

            // Icon/List 뷰 — FolderViewModel의 Children 기반 그룹핑
            GetActiveIconView()?.ApplyGroupBy(groupBy);
            GetActiveListView()?.ApplyGroupBy(groupBy);

            // 설정 저장
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["ViewGroupBy"] = groupBy;
            }
            catch { }

            Helpers.DebugLogger.Log($"[GroupBy] Applied: {groupBy}");
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








        // =================================================================
        //  P1 #12: Tab Re-docking — Merge torn-off tab back into window
        // =================================================================


        // =================================================================
        //  P1 #15: Ctrl+D — Duplicate selected file/folder
        // =================================================================



        // =================================================================
        //  P1 #18: Alt+Enter — Show Windows Properties dialog
        // =================================================================


    }
}
