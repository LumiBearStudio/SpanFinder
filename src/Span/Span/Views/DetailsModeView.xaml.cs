using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Span.Services;
using Span.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace Span.Views
{
    /// <summary>
    /// Details 뷰 모드 UserControl.
    /// 파일/폴더를 컬럼 헤더(이름, 날짜, 유형, 크기, Git 상태) 기반의 리스트로 표시한다.
    /// GridSplitter 컬럼 리사이즈, 정렬, 그룹화, 필터링, 인라인 이름 변경,
    /// 러버 밴드 선택, 밀도 설정, 컬럼 표시/숨기기 기능을 포함한다.
    /// </summary>
    public sealed partial class DetailsModeView : UserControl
    {
        public ContextMenuService? ContextMenuService { get; set; }
        public IContextMenuHost? ContextMenuHost { get; set; }
        public IntPtr OwnerHwnd { get; set; }
        public bool IsRightPane { get; set; }

        /// <summary>
        /// true면 Loaded에서 auto-resolve 건너뜀 (코드에서 ViewModel을 직접 설정한 인스턴스).
        /// XAML 정의 인스턴스(첫 번째 탭)는 false (기본값) → 기존 동작 유지.
        /// </summary>
        public bool IsManualViewModel { get; set; }

        private ExplorerViewModel? _viewModel;

        /// <summary>
        /// true로 설정하면 ViewModel 할당 시 SortItems를 건너뛴다 (탭 전환 최적화).
        /// 이미 정렬된 데이터를 불필요하게 Clear+Add하는 O(N) 작업 방지.
        /// </summary>
        public bool SuppressSortOnAssign { get; set; }

        public ExplorerViewModel? ViewModel
        {
            get => _viewModel;
            set
            {
                _viewModel = value;
                RootGrid.DataContext = _viewModel;

                if (_viewModel != null && _isLoaded && !SuppressSortOnAssign)
                {
                    ApplyCurrentView();
                }
                SuppressSortOnAssign = false;
            }
        }

        private string _currentSortBy = "Name";
        private bool _isAscending = true;
        private bool _isLoaded = false;
        private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;
        private SettingsService? _settings;

        // Current column widths (read from header ColumnDefinitions)
        private double _locationColumnWidth = 0;
        private double _dateColumnWidth = 200;
        private double _typeColumnWidth = 150;
        private double _sizeColumnWidth = 100;
        private double _gitColumnWidth = 50;

        // Callback tokens for ColumnDefinition.WidthProperty change tracking
        private long _locationCallbackToken;
        private long _dateCallbackToken;
        private long _typeCallbackToken;
        private long _sizeCallbackToken;
        private long _gitCallbackToken;

        // Guard against double cleanup (Cleanup() from OnClosed + OnUnloaded from visual tree teardown)
        private bool _isCleanedUp = false;

        // Rubber-band selection
        private Helpers.RubberBandSelectionHelper? _rubberBandHelper;
        private bool _isSyncingSelection;

        // ── Feature #29: Group By ──
        private string _currentGroupBy = "None";

        // ── Feature #30: Column Visibility ──
        private bool _dateColumnVisible = true;
        private bool _typeColumnVisible = true;
        private bool _sizeColumnVisible = true;
        private bool _gitColumnVisible = true;
        private GridLength _savedDateWidth = new GridLength(200);
        private GridLength _savedTypeWidth = new GridLength(150);
        private GridLength _savedSizeWidth = new GridLength(100);
        private GridLength _savedGitWidth = new GridLength(50);

        // ── Feature #31: Column Filters ──
        private string _nameFilter = string.Empty;
        private string _dateFilter = string.Empty; // "Today", "ThisWeek", "ThisMonth", "ThisYear", "Older", or ""
        private string _typeFilter = string.Empty;
        private string _sizeFilter = string.Empty; // "Empty", "Tiny", "Small", "Medium", "Large", "Huge", or ""

        // Unfiltered items cache (for re-applying filters)
        private List<FileSystemViewModel>? _unfilteredItems;

        private LocalizationService? _loc;

        public DetailsModeView()
        {
            this.InitializeComponent();

            this.Loaded += (s, e) =>
            {
                _isLoaded = true;
                _isCleanedUp = false; // Allow cleanup on next Unloaded

                if (!IsManualViewModel)
                {
                    // Get ViewModel from MainWindow's DataContext (XAML 정의 인스턴스용)
                    if (this.XamlRoot?.Content is FrameworkElement root &&
                        root.DataContext is MainViewModel mainVm)
                    {
                        ViewModel = IsRightPane ? mainVm.RightExplorer : mainVm.Explorer;
                    }
                }

                // Apply ShowCheckboxes setting
                try
                {
                    _settings = App.Current.Services.GetService(typeof(SettingsService)) as SettingsService;
                    if (_settings != null)
                    {
                        ApplyCheckboxMode(_settings.ShowCheckboxes);
                        _settings.SettingChanged += OnSettingChanged;
                    }
                }
                catch (Exception ex) { Helpers.DebugLogger.Log($"[DetailsModeView] Settings init error: {ex.Message}"); }

                try
                {
                    _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
                    LocalizeUI();
                    if (_loc != null) _loc.LanguageChanged += LocalizeUI;
                }
                catch (Exception ex) { Helpers.DebugLogger.Log($"[DetailsModeView] Localization init error: {ex.Message}"); }

                // Ctrl+Wheel view mode cycling is handled globally by MainWindow.OnGlobalPointerWheelChanged

                // Restore sort settings
                RestoreSortSettings();

                // Restore column visibility
                RestoreColumnVisibility();

                // Git 컬럼: 초기 숨김 (TriggerGitStateLoad에서 Git 레포 감지 시 자동 표시)
                ToggleColumnVisibility("Git", false);

                // Subscribe to ColumnDefinition.Width changes via RegisterPropertyChangedCallback.
                // CRITICAL: HeaderGrid.SizeChanged does NOT fire when GridSplitter rearranges
                // internal columns — the Grid's total size stays the same. We must watch
                // each ColumnDefinition individually.
                _locationCallbackToken = LocationColumnDef.RegisterPropertyChangedCallback(
                    ColumnDefinition.WidthProperty, OnColumnWidthChanged);
                _dateCallbackToken = DateColumnDef.RegisterPropertyChangedCallback(
                    ColumnDefinition.WidthProperty, OnColumnWidthChanged);
                _typeCallbackToken = TypeColumnDef.RegisterPropertyChangedCallback(
                    ColumnDefinition.WidthProperty, OnColumnWidthChanged);
                _sizeCallbackToken = SizeColumnDef.RegisterPropertyChangedCallback(
                    ColumnDefinition.WidthProperty, OnColumnWidthChanged);
                _gitCallbackToken = GitColumnDef.RegisterPropertyChangedCallback(
                    ColumnDefinition.WidthProperty, OnColumnWidthChanged);
            };

            this.Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_loc != null) _loc.LanguageChanged -= LocalizeUI;

                // Save sort settings (always, even if already cleaned up)
                SaveSortSettings();

                // Skip if Cleanup() was already called from MainWindow.OnClosed
                if (_isCleanedUp) return;

                PerformCleanup();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView.OnUnloaded] Error: {ex.Message}");
            }
        }

        #region Localization

        private void LocalizeUI()
        {
            if (_loc == null) return;
            NameHeaderButton.Content = _loc.Get("Name");
            DateHeaderButton.Content = _loc.Get("DateModified");
            TypeHeaderButton.Content = _loc.Get("Type");
            SizeHeaderButton.Content = _loc.Get("Size");
            GitHeaderButton.Content = _loc.Get("ColumnGit");
            ToolTipService.SetToolTip(NameFilterButton, _loc.Get("FilterName"));
            ToolTipService.SetToolTip(DateFilterButton, _loc.Get("FilterDate"));
            ToolTipService.SetToolTip(TypeFilterButton, _loc.Get("FilterType"));
            ToolTipService.SetToolTip(SizeFilterButton, _loc.Get("FilterSize"));
        }

        #endregion

        #region Column Width Synchronization

        /// <summary>
        /// Fired when any of the 3 resizable ColumnDefinitions change Width
        /// (via GridSplitter drag or window resize).
        ///
        /// WinUI 3 bugs that forced this approach:
        /// 1. ColumnDefinition.Width binding in DataTemplate doesn't respond
        ///    to INotifyPropertyChanged (Microsoft Issue #10300).
        /// 2. Grid.SizeChanged does NOT fire when GridSplitter rearranges
        ///    internal columns (total Grid size stays the same).
        ///
        /// Solution: RegisterPropertyChangedCallback on each ColumnDefinition,
        /// then set Border.Width directly on each cell element.
        /// </summary>
        private void OnColumnWidthChanged(DependencyObject sender, DependencyProperty dp)
        {
            _locationColumnWidth = LocationColumnDef.ActualWidth;
            _dateColumnWidth = DateColumnDef.ActualWidth;
            _typeColumnWidth = TypeColumnDef.ActualWidth;
            _sizeColumnWidth = SizeColumnDef.ActualWidth;
            _gitColumnWidth = GitColumnDef.ActualWidth;

            UpdateAllVisibleContainerWidths();
        }

        /// <summary>
        /// Called when ListView containers are created or recycled.
        /// Sets cell widths to match current header column widths.
        /// This handles virtualization: newly realized containers get correct widths.
        /// </summary>
        private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;

            if (args.ItemContainer?.ContentTemplateRoot is Grid grid)
            {
                ApplyCellWidths(grid);
                grid.Height = _densityRowHeight;

                // Apply icon/font scale to newly materialized containers
                if (_iconFontScaleLevel > 0)
                {
                    double itemFont = 13.0 + _iconFontScaleLevel;
                    double iconFont = 16.0 + _iconFontScaleLevel;
                    double secondaryFont = 12.0 + _iconFontScaleLevel;
                    foreach (var child in grid.Children)
                    {
                        if (child is TextBlock tb)
                        {
                            if (tb.FontSize >= 13 && tb.FontSize <= 18)
                                tb.FontSize = itemFont;
                            else if (tb.FontSize >= 12 && tb.FontSize < 13)
                                tb.FontSize = secondaryFont;
                        }
                        else if (child is Border b && b.Child is TextBlock btb)
                        {
                            if (btb.FontSize >= 12 && btb.FontSize <= 17)
                                btb.FontSize = secondaryFont;
                        }
                        else if (child is Grid iconGrid && iconGrid.Width <= 24)
                        {
                            var fi = FindChild<FontIcon>(iconGrid);
                            if (fi != null && fi.FontSize >= 16 && fi.FontSize <= 21)
                                fi.FontSize = iconFont;
                            if (iconGrid.Width >= 16 && iconGrid.Width <= 21)
                                iconGrid.Width = iconFont;
                            if (iconGrid.Height >= 16 && iconGrid.Height <= 21)
                                iconGrid.Height = iconFont;
                        }
                    }
                }
            }

            // Details 뷰에서 폴더 표시 시 크기 계산 요청 (lazy)
            if (args.Item is ViewModels.FolderViewModel folderVm)
            {
                folderVm.RequestFolderSizeCalculation();
            }

            // Git/Cloud 상태 on-demand 주입 (캐시된 값만, I/O 없음)
            if (args.Item is ViewModels.FileSystemViewModel fsVm)
            {
                var currentFolder = _viewModel?.CurrentFolder;
                if (_gitColumnVisible)
                    currentFolder?.InjectGitStateIfNeeded(fsVm);
                currentFolder?.InjectCloudStateIfNeeded(fsVm);
            }
        }

        /// <summary>
        /// Apply current column widths to a single item Grid's cell Borders.
        /// Also applies column visibility (Width=0 for hidden columns).
        /// Note: FindName() does NOT work inside DataTemplate namescope in WinUI 3.
        /// Instead, we find Border elements by their Grid.Column attached property.
        /// </summary>
        private void ApplyCellWidths(Grid grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is Border border)
                {
                    int col = Grid.GetColumn(border);
                    switch (col)
                    {
                        case 3: // Location (검색 결과에서만 표시)
                            bool locVisible = _locationColumnWidth > 0;
                            border.Width = locVisible ? _locationColumnWidth : 0;
                            border.Visibility = locVisible ? Visibility.Visible : Visibility.Collapsed;
                            break;
                        case 5: // Date
                            border.Width = _dateColumnVisible ? _dateColumnWidth : 0;
                            border.Visibility = _dateColumnVisible ? Visibility.Visible : Visibility.Collapsed;
                            break;
                        case 7: // Type
                            border.Width = _typeColumnVisible ? _typeColumnWidth : 0;
                            border.Visibility = _typeColumnVisible ? Visibility.Visible : Visibility.Collapsed;
                            break;
                        case 9: // Size
                            border.Width = _sizeColumnVisible ? _sizeColumnWidth : 0;
                            border.Visibility = _sizeColumnVisible ? Visibility.Visible : Visibility.Collapsed;
                            break;
                        case 11: // Git Status
                            border.Width = _gitColumnVisible ? _gitColumnWidth : 0;
                            border.Visibility = _gitColumnVisible ? Visibility.Visible : Visibility.Collapsed;
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Update all currently visible containers' cell widths.
        /// Only iterates realized containers (typically 20-50 items), not all items.
        /// </summary>
        private void UpdateAllVisibleContainerWidths()
        {
            var panel = DetailsListView?.ItemsPanelRoot;
            if (panel == null) return;

            // ItemsPanelRoot의 자식만 순회 (실제 표시된 30-50개) — Items.Count(14K) 전수 순회 방지
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(panel); i++)
            {
                if (VisualTreeHelper.GetChild(panel, i) is ListViewItem container &&
                    container.ContentTemplateRoot is Grid grid)
                {
                    ApplyCellWidths(grid);
                }
            }
        }

        #endregion

        #region Density

        private double _densityRowHeight = 24.0; // comfortable default
        private int _iconFontScaleLevel = 0;

        public void ApplyDensity(string density)
        {
            // 숫자 문자열(0~6) 또는 레거시 이름 지원
            int level = density switch
            {
                "compact" => 0,
                "comfortable" => 2,
                "spacious" => 4,
                _ => int.TryParse(density, out var n) ? Math.Clamp(n, 0, 6) : 2
            };
            _densityRowHeight = 20.0 + level;

            // Update header row height to match
            if (HeaderGrid != null)
                HeaderGrid.Height = _densityRowHeight;

            // Update MinHeight on ListViewItem style
            if (DetailsListView != null)
            {
                DetailsListView.ItemContainerStyle = CreateDetailsItemStyle(_densityRowHeight);
            }

            // Update existing realized containers (ItemsPanelRoot 자식만 — 14K 전수 순회 방지)
            var panel = DetailsListView?.ItemsPanelRoot;
            if (panel == null) return;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(panel); i++)
            {
                if (VisualTreeHelper.GetChild(panel, i) is ListViewItem container &&
                    container.ContentTemplateRoot is Grid grid)
                {
                    grid.Height = _densityRowHeight;
                }
            }
        }

        private Style CreateDetailsItemStyle(double minHeight)
        {
            var baseStyle = (Style)App.Current.Resources["DetailsItemStyle"];
            var style = new Style(typeof(ListViewItem)) { BasedOn = baseStyle };
            style.Setters.Add(new Setter(ListViewItem.MinHeightProperty, minHeight));
            return style;
        }

        /// <summary>
        /// 아이콘/폰트 스케일(0~5)을 적용한다. 레벨 0 = 기본(13px/16px).
        /// </summary>
        public void ApplyIconFontScale(string scale)
        {
            int level = int.TryParse(scale, out var n) ? Math.Clamp(n, 0, 5) : 0;
            _iconFontScaleLevel = level;
            double itemFont = 13.0 + level;
            double iconFont = 16.0 + level;
            double secondaryFont = 12.0 + level;

            // ItemsPanelRoot 자식만 순회 — 14K 전수 순회 방지
            var scalePanel = DetailsListView?.ItemsPanelRoot;
            if (scalePanel == null) return;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(scalePanel); i++)
            {
                if (VisualTreeHelper.GetChild(scalePanel, i) is ListViewItem container &&
                    container.ContentTemplateRoot is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is TextBlock tb)
                        {
                            if (tb.FontSize >= 13 && tb.FontSize <= 18)
                                tb.FontSize = itemFont;
                            else if (tb.FontSize >= 12 && tb.FontSize < 13)
                                tb.FontSize = secondaryFont;
                        }
                        else if (child is Grid iconGrid && iconGrid.Width <= 24)
                        {
                            var fi = FindChild<FontIcon>(iconGrid);
                            if (fi != null && fi.FontSize >= 16 && fi.FontSize <= 21)
                                fi.FontSize = iconFont;
                        }
                    }
                }
            }
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        #endregion

        #region Selection Sync

        private void OnDetailsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection) return;
            if (ViewModel?.CurrentFolder == null) return;
            if (sender is ListView listView)
            {
                ViewModel.CurrentFolder.SyncSelectedItems(listView.SelectedItems);
            }
        }

        private void ApplyCheckboxMode(bool showCheckboxes)
        {
            if (DetailsListView == null) return;
            DetailsListView.SelectionMode = showCheckboxes
                ? ListViewSelectionMode.Multiple
                : ListViewSelectionMode.Extended;
        }

        private void OnSettingChanged(string key, object? value)
        {
            if (key == "ShowCheckboxes" && value is bool show)
            {
                DispatcherQueue.TryEnqueue(() => ApplyCheckboxMode(show));
            }
            else if (key == "ShowGitIntegration" && value is bool gitEnabled)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ToggleColumnVisibility("Git", gitEnabled);
                    if (gitEnabled)
                        TriggerGitStateLoad();
                });
            }
        }

        #endregion

        #region Item Interaction

        private void OnDragItemsStarting(object sender, Microsoft.UI.Xaml.Controls.DragItemsStartingEventArgs e)
        {
            if (_rubberBandHelper?.IsActive == true)
            { e.Cancel = true; return; }

            if (!Helpers.ViewDragDropHelper.SetupDragData(e, IsRightPane))
                e.Cancel = true;
        }

        private async void OnItemRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            try
            {
                if (_settings != null && !_settings.ShowContextMenu) return;
                if (sender is Grid grid && ContextMenuService != null && ContextMenuHost != null)
                {
                    e.Handled = true; // Prevent bubbling to empty area handler during await

                    Microsoft.UI.Xaml.Controls.MenuFlyout? flyout = null;

                    if (grid.DataContext is FolderViewModel folder)
                        flyout = await ContextMenuService.BuildFolderMenuAsync(folder, ContextMenuHost);
                    else if (grid.DataContext is FileViewModel file)
                        flyout = await ContextMenuService.BuildFileMenuAsync(file, ContextMenuHost);

                    if (flyout != null)
                    {
                        flyout.ShowAt(grid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                        {
                            Position = e.GetPosition(grid)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView] OnItemRightTapped error: {ex.Message}");
            }
        }

        private void OnItemDoubleClick(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            Helpers.ViewItemHelper.OpenFileOrFolder(ViewModel, "DetailsModeView");
        }

        #endregion

        #region Keyboard Navigation

        private void OnDetailsKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var selected = ViewModel?.CurrentFolder?.SelectedChild;
            if (selected != null && selected.IsRenaming) return;
            if (Helpers.ViewItemHelper.HasModifierKey()) return;

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    Helpers.ViewItemHelper.OpenFileOrFolder(ViewModel, "DetailsModeView");
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Back:
                    ViewModel?.NavigateUp();
                    e.Handled = true;
                    break;
            }
        }

        #endregion

        #region Sorting

        private void OnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string sortBy)
            {
                if (_currentSortBy == sortBy)
                {
                    _isAscending = !_isAscending;
                }
                else
                {
                    _currentSortBy = sortBy;
                    _isAscending = true;
                }

                SortItems(sortBy, _isAscending);
                Helpers.DebugLogger.Log($"[DetailsModeView] Sorted by {sortBy} ({(_isAscending ? "Asc" : "Desc")})");
            }
        }

        private void SortItems(string sortBy, bool ascending)
        {
            if (ViewModel?.CurrentFolder == null || ViewModel.CurrentFolder.Children.Count == 0)
                return;

            var column = ViewModel.CurrentFolder;
            var savedSelection = column.SelectedChild;
            column.IsSorting = true;

            try
            {
                System.Collections.Generic.IEnumerable<FileSystemViewModel> sorted;

                switch (sortBy)
                {
                    case "Name":
                        sorted = ascending
                            ? column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.Name, Helpers.NaturalStringComparer.Instance)
                            : column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.Name, Helpers.NaturalStringComparer.Instance);
                        break;
                    case "DateModified":
                        sorted = ascending
                            ? column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.DateModifiedValue)
                            : column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.DateModifiedValue);
                        break;
                    case "Type":
                        sorted = ascending
                            ? column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.FileType)
                            : column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.FileType);
                        break;
                    case "Size":
                        sorted = ascending
                            ? column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.SizeValue)
                            : column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.SizeValue);
                        break;
                    case "Git":
                        sorted = ascending
                            ? column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.GitStatusText)
                            : column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.GitStatusText);
                        break;
                    default:
                        return;
                }

                var sortedList = sorted.ToList();

                // 원자적 교체: Clear+Add 대신 새 컬렉션 할당으로 28K CollectionChanged 이벤트 → 1회 Reset
                column.ReplaceChildren(sortedList);

                if (savedSelection != null)
                {
                    column.SelectedChild = savedSelection;
                }

                // Cache unfiltered items after sorting
                _unfilteredItems = new System.Collections.Generic.List<FileSystemViewModel>(sortedList);

                // Re-apply filters and grouping
                ApplyFiltersAndGrouping();

                Helpers.DebugLogger.Log($"[DetailsModeView] Sorted {sortedList.Count} items");
            }
            finally
            {
                column.IsSorting = false;
            }
        }

        #endregion

        #region Sort Settings Persistence

        private void SaveSortSettings()
        {
            try
            {
                var composite = new ApplicationDataCompositeValue
                {
                    ["SortColumn"] = _currentSortBy,
                    ["SortAscending"] = _isAscending,
                    ["GroupBy"] = _currentGroupBy,
                    ["DateColumnVisible"] = _dateColumnVisible,
                    ["TypeColumnVisible"] = _typeColumnVisible,
                    ["SizeColumnVisible"] = _sizeColumnVisible,
                    ["GitColumnVisible"] = _gitColumnVisible
                };
                _localSettings.Values["DetailsViewSort"] = composite;
                Helpers.DebugLogger.Log("[DetailsModeView] Sort settings saved");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView] Error saving sort settings: {ex.Message}");
            }
        }

        private void RestoreSortSettings()
        {
            try
            {
                if (_localSettings.Values["DetailsViewSort"] is ApplicationDataCompositeValue composite)
                {
                    if (composite.TryGetValue("SortColumn", out var sortObj) && sortObj is string sortColumn)
                    {
                        _currentSortBy = sortColumn;
                    }
                    if (composite.TryGetValue("SortAscending", out var ascObj) && ascObj is bool ascending)
                    {
                        _isAscending = ascending;
                    }
                    if (composite.TryGetValue("GroupBy", out var groupObj) && groupObj is string groupBy)
                    {
                        _currentGroupBy = groupBy;
                    }

                    SortItems(_currentSortBy, _isAscending);
                    Helpers.DebugLogger.Log("[DetailsModeView] Sort settings restored");
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView] Error restoring sort settings: {ex.Message}");
            }
        }

        #endregion

        // Ctrl+Mouse Wheel view mode cycling is handled globally by MainWindow.OnGlobalPointerWheelChanged

        #region Focus Management

        /// <summary>
        /// Focus the Details ListView (called from MainWindow on view switch)
        /// </summary>
        public void FocusDataGrid()
        {
            DetailsListView?.Focus(FocusState.Programmatic);
        }

        public void FocusListView() => FocusDataGrid();

        /// <summary>
        /// Empty space click → focus ListView → triggers GotFocus bubbling → ActivePane set
        /// </summary>
        private void OnRootTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            DetailsListView?.Focus(FocusState.Programmatic);
        }

        private void OnEmptyAreaRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (e.Handled) return; // Item handler already handled
            if (_settings != null && !_settings.ShowContextMenu) return;
            if (ContextMenuService == null || ContextMenuHost == null) return;

            var folderPath = ViewModel?.CurrentFolder?.Path;
            if (string.IsNullOrEmpty(folderPath)) return;

            var flyout = ContextMenuService.BuildEmptyAreaMenu(folderPath, ContextMenuHost);
            flyout.ShowAt(RootGrid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Position = e.GetPosition(RootGrid)
            });
        }

        #endregion

        #region Feature #29: Group By

        private void OnHeaderRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            e.Handled = true; // Prevent empty area context menu

            var flyout = new MenuFlyout();

            // ── Column Visibility (Feature #30) ──
            var dateToggle = new ToggleMenuFlyoutItem
            {
                Text = "Date Modified",
                IsChecked = _dateColumnVisible
            };
            dateToggle.Click += (s, _) => ToggleColumnVisibility("DateModified", !_dateColumnVisible);

            var typeToggle = new ToggleMenuFlyoutItem
            {
                Text = "Type",
                IsChecked = _typeColumnVisible
            };
            typeToggle.Click += (s, _) => ToggleColumnVisibility("Type", !_typeColumnVisible);

            var sizeToggle = new ToggleMenuFlyoutItem
            {
                Text = "Size",
                IsChecked = _sizeColumnVisible
            };
            sizeToggle.Click += (s, _) => ToggleColumnVisibility("Size", !_sizeColumnVisible);

            var gitToggle = new ToggleMenuFlyoutItem
            {
                Text = "Git Status",
                IsChecked = _gitColumnVisible
            };
            gitToggle.Click += (s, _) => ToggleColumnVisibility("Git", !_gitColumnVisible);

            flyout.Items.Add(new MenuFlyoutSubItem
            {
                Text = "Show Columns",
                Items =
                {
                    new ToggleMenuFlyoutItem { Text = "Name", IsChecked = true, IsEnabled = false },
                    dateToggle,
                    typeToggle,
                    sizeToggle,
                    gitToggle
                }
            });

            flyout.Items.Add(new MenuFlyoutSeparator());

            // ── Group By (Feature #29) ──
            var groupByNone = new ToggleMenuFlyoutItem { Text = "None", IsChecked = _currentGroupBy == "None" };
            groupByNone.Click += (s, _) => SetGroupBy("None");

            var groupByName = new ToggleMenuFlyoutItem { Text = "Name", IsChecked = _currentGroupBy == "Name" };
            groupByName.Click += (s, _) => SetGroupBy("Name");

            var groupByType = new ToggleMenuFlyoutItem { Text = "Type", IsChecked = _currentGroupBy == "Type" };
            groupByType.Click += (s, _) => SetGroupBy("Type");

            var groupByDate = new ToggleMenuFlyoutItem { Text = "Date Modified", IsChecked = _currentGroupBy == "DateModified" };
            groupByDate.Click += (s, _) => SetGroupBy("DateModified");

            var groupBySize = new ToggleMenuFlyoutItem { Text = "Size", IsChecked = _currentGroupBy == "Size" };
            groupBySize.Click += (s, _) => SetGroupBy("Size");

            flyout.Items.Add(new MenuFlyoutSubItem
            {
                Text = "Group By",
                Items = { groupByNone, groupByName, groupByType, groupByDate, groupBySize }
            });

            flyout.ShowAt(HeaderGrid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Position = e.GetPosition(HeaderGrid)
            });
        }

        /// <summary>
        /// Public entry point for external group by trigger.
        /// </summary>
        public void SetGroupByPublic(string groupBy) => SetGroupBy(groupBy);

        private void SetGroupBy(string groupBy)
        {
            _currentGroupBy = groupBy;
            ApplyFiltersAndGrouping();
            SaveSortSettings();
            Helpers.DebugLogger.Log($"[DetailsModeView] Group By set to: {groupBy}");
        }

        private string GetGroupKey(FileSystemViewModel item, string groupBy)
        {
            switch (groupBy)
            {
                case "Name":
                    // Group by first letter
                    var firstChar = !string.IsNullOrEmpty(item.Name)
                        ? char.ToUpperInvariant(item.Name[0]).ToString()
                        : "#";
                    return char.IsLetter(firstChar[0]) ? firstChar : "#";

                case "Type":
                    if (item is FolderViewModel) return "Folder";
                    return string.IsNullOrEmpty(item.FileType) ? "Unknown" : item.FileType.ToUpperInvariant();

                case "DateModified":
                    var date = item.DateModifiedValue;
                    var now = DateTime.Now;
                    if (date.Date == now.Date) return "Today";
                    if (date.Date == now.Date.AddDays(-1)) return "Yesterday";
                    if (date >= now.Date.AddDays(-(int)now.DayOfWeek)) return "This Week";
                    if (date.Year == now.Year && date.Month == now.Month) return "This Month";
                    if (date.Year == now.Year) return "This Year";
                    return date.Year > 0 ? date.Year.ToString() : "Unknown";

                case "Size":
                    if (item is FolderViewModel) return "Folders";
                    var size = item.SizeValue;
                    if (size == 0) return "Empty (0 B)";
                    if (size < 16 * 1024) return "Tiny (< 16 KB)";
                    if (size < 1024 * 1024) return "Small (< 1 MB)";
                    if (size < 128 * 1024 * 1024) return "Medium (< 128 MB)";
                    if (size < 1024L * 1024 * 1024) return "Large (< 1 GB)";
                    return "Huge (> 1 GB)";

                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Helper class for grouped items displayed in ListView.
        /// Implements IGrouping-like interface for data binding.
        /// </summary>
        private class ItemGroup : List<FileSystemViewModel>
        {
            public string Key { get; }
            public new int Count => base.Count;

            public ItemGroup(string key, IEnumerable<FileSystemViewModel> items) : base(items)
            {
                Key = key;
            }
        }

        #endregion

        #region Feature #30: Column Visibility

        /// <summary>
        /// 검색 결과 모드에서 Location 컬럼을 표시/숨김.
        /// MainWindow에서 재귀 검색 시작/취소 시 호출.
        /// </summary>
        public void ShowLocationColumn(bool show)
        {
            LocationColumnDef.Width = show ? new GridLength(180) : new GridLength(0);
            LocationColumnDef.MinWidth = show ? 100 : 0;
            LocationHeaderContainer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            Splitter1a.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            Splitter1b.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            _locationColumnWidth = show ? 180 : 0;
            UpdateAllVisibleContainerWidths();
        }

        private void ToggleColumnVisibility(string column, bool visible)
        {
            switch (column)
            {
                case "DateModified":
                    if (!visible) _savedDateWidth = DateColumnDef.Width;
                    _dateColumnVisible = visible;
                    DateColumnDef.Width = visible ? _savedDateWidth : new GridLength(0);
                    DateColumnDef.MinWidth = visible ? 140 : 0;
                    DateHeaderContainer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    Splitter1b.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    DateFilterButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    break;

                case "Type":
                    if (!visible) _savedTypeWidth = TypeColumnDef.Width;
                    _typeColumnVisible = visible;
                    TypeColumnDef.Width = visible ? _savedTypeWidth : new GridLength(0);
                    TypeColumnDef.MinWidth = visible ? 90 : 0;
                    TypeHeaderContainer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    Splitter2.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    TypeFilterButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    break;

                case "Size":
                    if (!visible) _savedSizeWidth = SizeColumnDef.Width;
                    _sizeColumnVisible = visible;
                    SizeColumnDef.Width = visible ? _savedSizeWidth : new GridLength(0);
                    SizeColumnDef.MinWidth = visible ? 80 : 0;
                    SizeHeaderContainer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    Splitter3.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    SizeFilterButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    break;

                case "Git":
                    if (!visible) _savedGitWidth = GitColumnDef.Width;
                    _gitColumnVisible = visible;
                    GitColumnDef.Width = visible ? _savedGitWidth : new GridLength(0);
                    GitColumnDef.MinWidth = visible ? 40 : 0;
                    GitHeaderContainer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    Splitter4.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    break;
            }

            // Update item cell widths to reflect visibility change
            UpdateAllVisibleContainerWidths();
            SaveSortSettings();

            Helpers.DebugLogger.Log($"[DetailsModeView] Column '{column}' visibility: {visible}");
        }

        private void RestoreColumnVisibility()
        {
            try
            {
                if (_localSettings.Values["DetailsViewSort"] is ApplicationDataCompositeValue composite)
                {
                    if (composite.TryGetValue("DateColumnVisible", out var dateObj) && dateObj is bool dateVis)
                    {
                        if (!dateVis) ToggleColumnVisibility("DateModified", false);
                    }
                    if (composite.TryGetValue("TypeColumnVisible", out var typeObj) && typeObj is bool typeVis)
                    {
                        if (!typeVis) ToggleColumnVisibility("Type", false);
                    }
                    if (composite.TryGetValue("SizeColumnVisible", out var sizeObj) && sizeObj is bool sizeVis)
                    {
                        if (!sizeVis) ToggleColumnVisibility("Size", false);
                    }
                    if (composite.TryGetValue("GitColumnVisible", out var gitObj) && gitObj is bool gitVis)
                    {
                        if (!gitVis) ToggleColumnVisibility("Git", false);
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView] Error restoring column visibility: {ex.Message}");
            }
        }

        #endregion

        #region Feature #31: Column Filters

        private void OnFilterButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string column) return;

            var flyout = new Flyout();
            var panel = new StackPanel { Spacing = 6, MinWidth = 180, Padding = new Thickness(4) };

            switch (column)
            {
                case "Name":
                    BuildNameFilterUI(panel);
                    break;
                case "DateModified":
                    BuildDateFilterUI(panel);
                    break;
                case "Type":
                    BuildTypeFilterUI(panel);
                    break;
                case "Size":
                    BuildSizeFilterUI(panel);
                    break;
            }

            flyout.Content = panel;
            flyout.ShowAt(button);
        }

        private void BuildNameFilterUI(StackPanel panel)
        {
            var header = new TextBlock
            {
                Text = "Filter by Name",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(header);

            var textBox = new TextBox
            {
                PlaceholderText = "Type to filter...",
                Text = _nameFilter,
                MinWidth = 160
            };
            textBox.TextChanged += (s, _) =>
            {
                _nameFilter = textBox.Text;
                ApplyFiltersAndGrouping();
                UpdateFilterIndicator("Name", !string.IsNullOrEmpty(_nameFilter));
            };
            panel.Children.Add(textBox);

            var clearButton = new Button
            {
                Content = "Clear",
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            };
            clearButton.Click += (s, _) =>
            {
                textBox.Text = string.Empty;
                _nameFilter = string.Empty;
                ApplyFiltersAndGrouping();
                UpdateFilterIndicator("Name", false);
            };
            panel.Children.Add(clearButton);
        }

        private void BuildDateFilterUI(StackPanel panel)
        {
            var header = new TextBlock
            {
                Text = "Filter by Date",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(header);

            var dateRanges = new[] { "", "Today", "ThisWeek", "ThisMonth", "ThisYear", "Older" };
            var dateLabels = new[] { "All", "Today", "This Week", "This Month", "This Year", "Older" };

            for (int i = 0; i < dateRanges.Length; i++)
            {
                var range = dateRanges[i];
                var radio = new RadioButton
                {
                    Content = dateLabels[i],
                    IsChecked = _dateFilter == range,
                    GroupName = "DateFilter",
                    FontSize = 12,
                    MinHeight = 24,
                    Padding = new Thickness(4, 0, 0, 0)
                };
                radio.Checked += (s, _) =>
                {
                    _dateFilter = range;
                    ApplyFiltersAndGrouping();
                    UpdateFilterIndicator("DateModified", !string.IsNullOrEmpty(range));
                };
                panel.Children.Add(radio);
            }
        }

        private void BuildTypeFilterUI(StackPanel panel)
        {
            var header = new TextBlock
            {
                Text = "Filter by Type",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(header);

            // Get unique types from current items
            var items = _unfilteredItems ?? GetCurrentItemsList();
            var types = items
                .Select(x => x is FolderViewModel ? "Folder" : (string.IsNullOrEmpty(x.FileType) ? "Unknown" : x.FileType))
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            // "All" option
            var allRadio = new RadioButton
            {
                Content = "All",
                IsChecked = string.IsNullOrEmpty(_typeFilter),
                GroupName = "TypeFilter",
                FontSize = 12,
                MinHeight = 24,
                Padding = new Thickness(4, 0, 0, 0)
            };
            allRadio.Checked += (s, _) =>
            {
                _typeFilter = string.Empty;
                ApplyFiltersAndGrouping();
                UpdateFilterIndicator("Type", false);
            };
            panel.Children.Add(allRadio);

            // Wrap in a ScrollViewer if many types
            var scrollPanel = new StackPanel { Spacing = 2 };

            foreach (var type in types)
            {
                var radio = new RadioButton
                {
                    Content = type,
                    IsChecked = _typeFilter == type,
                    GroupName = "TypeFilter",
                    FontSize = 12,
                    MinHeight = 24,
                    Padding = new Thickness(4, 0, 0, 0)
                };
                var captured = type;
                radio.Checked += (s, _) =>
                {
                    _typeFilter = captured;
                    ApplyFiltersAndGrouping();
                    UpdateFilterIndicator("Type", true);
                };
                scrollPanel.Children.Add(radio);
            }

            var scrollViewer = new ScrollViewer
            {
                Content = scrollPanel,
                MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            panel.Children.Add(scrollViewer);
        }

        private void BuildSizeFilterUI(StackPanel panel)
        {
            var header = new TextBlock
            {
                Text = "Filter by Size",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(header);

            var sizeRanges = new[] { "", "Empty", "Tiny", "Small", "Medium", "Large", "Huge" };
            var sizeLabels = new[] { "All", "Empty (0 B)", "Tiny (< 16 KB)", "Small (< 1 MB)", "Medium (< 128 MB)", "Large (< 1 GB)", "Huge (> 1 GB)" };

            for (int i = 0; i < sizeRanges.Length; i++)
            {
                var range = sizeRanges[i];
                var radio = new RadioButton
                {
                    Content = sizeLabels[i],
                    IsChecked = _sizeFilter == range,
                    GroupName = "SizeFilter",
                    FontSize = 12,
                    MinHeight = 24,
                    Padding = new Thickness(4, 0, 0, 0)
                };
                radio.Checked += (s, _) =>
                {
                    _sizeFilter = range;
                    ApplyFiltersAndGrouping();
                    UpdateFilterIndicator("Size", !string.IsNullOrEmpty(range));
                };
                panel.Children.Add(radio);
            }
        }

        private void UpdateFilterIndicator(string column, bool active)
        {
            Button? filterButton = column switch
            {
                "Name" => NameFilterButton,
                "DateModified" => DateFilterButton,
                "Type" => TypeFilterButton,
                "Size" => SizeFilterButton,
                _ => null
            };

            if (filterButton != null)
            {
                filterButton.Opacity = active ? 1.0 : 0.5;

                if (filterButton.Content is FontIcon icon)
                {
                    // Filled funnel when active, outline when inactive
                    icon.Glyph = active ? "\uE71C" : "\uE16E";
                }
            }
        }

        private bool PassesFilter(FileSystemViewModel item)
        {
            // Name filter
            if (!string.IsNullOrEmpty(_nameFilter))
            {
                if (item.Name == null || !item.Name.Contains(_nameFilter, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Date filter
            if (!string.IsNullOrEmpty(_dateFilter))
            {
                var date = item.DateModifiedValue;
                var now = DateTime.Now;
                bool passes = _dateFilter switch
                {
                    "Today" => date.Date == now.Date,
                    "ThisWeek" => date >= now.Date.AddDays(-(int)now.DayOfWeek),
                    "ThisMonth" => date.Year == now.Year && date.Month == now.Month,
                    "ThisYear" => date.Year == now.Year,
                    "Older" => date.Year < now.Year,
                    _ => true
                };
                if (!passes) return false;
            }

            // Type filter
            if (!string.IsNullOrEmpty(_typeFilter))
            {
                var itemType = item is FolderViewModel ? "Folder" :
                    (string.IsNullOrEmpty(item.FileType) ? "Unknown" : item.FileType);
                if (!string.Equals(itemType, _typeFilter, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Size filter
            if (!string.IsNullOrEmpty(_sizeFilter))
            {
                var size = item.SizeValue;
                bool passes = _sizeFilter switch
                {
                    "Empty" => item is FolderViewModel || size == 0,
                    "Tiny" => item is FolderViewModel || (size > 0 && size < 16 * 1024),
                    "Small" => item is FolderViewModel || (size >= 16 * 1024 && size < 1024 * 1024),
                    "Medium" => item is FolderViewModel || (size >= 1024 * 1024 && size < 128 * 1024 * 1024),
                    "Large" => item is FolderViewModel || (size >= 128 * 1024 * 1024 && size < 1024L * 1024 * 1024),
                    "Huge" => item is FolderViewModel || size >= 1024L * 1024 * 1024,
                    _ => true
                };
                if (!passes) return false;
            }

            return true;
        }

        private bool HasAnyFilter => !string.IsNullOrEmpty(_nameFilter) ||
                                     !string.IsNullOrEmpty(_dateFilter) ||
                                     !string.IsNullOrEmpty(_typeFilter) ||
                                     !string.IsNullOrEmpty(_sizeFilter);

        #endregion

        #region Combined Filter + Grouping Application

        /// <summary>
        /// Applies the current filter settings and grouping to the ListView.
        /// Call this after any filter or grouping change.
        /// </summary>
        private void ApplyFiltersAndGrouping()
        {
            if (ViewModel?.CurrentFolder == null) return;

            // Get the base items (from unfiltered cache or current children)
            var allItems = _unfilteredItems ?? GetCurrentItemsList();

            // Apply filters
            var filtered = HasAnyFilter
                ? allItems.Where(PassesFilter).ToList()
                : allItems;

            // Apply grouping
            if (_currentGroupBy != "None" && !string.IsNullOrEmpty(_currentGroupBy))
            {
                var groups = filtered
                    .GroupBy(item => GetGroupKey(item, _currentGroupBy))
                    .OrderBy(g => g.Key)
                    .Select(g => new ItemGroup(g.Key + " (" + g.Count() + ")", g))
                    .ToList();

                var cvs = new CollectionViewSource
                {
                    Source = groups,
                    IsSourceGrouped = true
                };

                DetailsListView.ItemsSource = cvs.View;
            }
            else if (HasAnyFilter)
            {
                // Filters active but no grouping — set filtered list directly
                DetailsListView.ItemsSource = filtered;
            }
            else
            {
                // No filters, no grouping — bind back to ViewModel
                DetailsListView.SetBinding(
                    ListView.ItemsSourceProperty,
                    new Binding { Path = new PropertyPath("CurrentItems"), Mode = BindingMode.OneWay });
            }
        }

        /// <summary>
        /// Gets a snapshot of current items for filtering/grouping.
        /// </summary>
        private List<FileSystemViewModel> GetCurrentItemsList()
        {
            if (ViewModel?.CurrentFolder?.Children == null)
                return new List<FileSystemViewModel>();
            return ViewModel.CurrentFolder.Children.ToList();
        }

        /// <summary>
        /// Called when ViewModel changes or sort completes. Re-applies all view settings.
        /// </summary>
        private void ApplyCurrentView()
        {
            SortItems(_currentSortBy, _isAscending);
            TriggerGitStateLoad();
        }

        private System.Threading.CancellationTokenSource? _gitCts;

        /// <summary>
        /// Details 뷰 진입/탭 전환 시 현재 폴더의 Git 상태를 비동기 로드.
        /// 백그라운드에서 git status 실행 → UI 스레드에서 상태 주입.
        /// </summary>
        private async void TriggerGitStateLoad()
        {
            if (_viewModel?.CurrentFolder == null) return;
            if (_settings != null && !_settings.ShowGitIntegration) return;

            var folder = _viewModel.CurrentFolder;

            // Git 레포 폴더 → 자동으로 Git 컬럼 표시, 아닌 폴더 → 자동 숨김
            if (folder.IsGitFolder)
            {
                if (!_gitColumnVisible)
                    ToggleColumnVisibility("Git", true);
            }
            else
            {
                if (_gitColumnVisible)
                    ToggleColumnVisibility("Git", false);
                return;
            }

            _gitCts?.Cancel();
            _gitCts = new System.Threading.CancellationTokenSource();
            var ct = _gitCts.Token;

            try
            {
                // 백그라운드에서 git status 실행 (캐시 채움)
                var gitSvc = App.Current.Services.GetService(typeof(GitStatusService)) as GitStatusService;
                if (gitSvc == null || !gitSvc.IsAvailable) return;

                await Task.Run(async () =>
                {
                    await gitSvc.GetFolderStatesAsync(folder.Path, ct);
                }, ct);

                if (ct.IsCancellationRequested) return;

                // UI 스레드에서 Children에 주입 (이미 UI 스레드)
                foreach (var child in folder.Children)
                {
                    if (ct.IsCancellationRequested) break;
                    folder.InjectGitStateIfNeeded(child);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView] Git state load error: {ex.Message}");
            }
        }

        #endregion

        #region Rubber Band Selection

        private void OnListViewWrapperLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid grid || _rubberBandHelper != null) return;

            _rubberBandHelper = new Helpers.RubberBandSelectionHelper(
                grid,
                DetailsListView,
                () => _isSyncingSelection,
                val => _isSyncingSelection = val,
                items => ViewModel?.CurrentFolder?.SyncSelectedItems(items));
        }

        private void OnListViewWrapperUnloaded(object sender, RoutedEventArgs e)
        {
            _rubberBandHelper?.Detach();
            _rubberBandHelper = null;
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// CRITICAL: Called from MainWindow.OnClosed BEFORE visual tree teardown.
        /// Prevents WinUI crash by disconnecting bindings early.
        /// </summary>
        public void Cleanup()
        {
            if (_isCleanedUp) return;
            SaveSortSettings();
            PerformCleanup();
        }

        private void PerformCleanup()
        {
            if (_isCleanedUp) return;
            _isCleanedUp = true;

            try
            {
                Helpers.DebugLogger.Log("[DetailsModeView] Starting cleanup...");

                _gitCts?.Cancel();
                _gitCts?.Dispose();
                _gitCts = null;

                _rubberBandHelper?.Detach();
                _rubberBandHelper = null;

                // Unregister column width callbacks (only if Loaded fired and registered them)
                if (_isLoaded)
                {
                    LocationColumnDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _locationCallbackToken);
                    DateColumnDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _dateCallbackToken);
                    TypeColumnDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _typeCallbackToken);
                    SizeColumnDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _sizeCallbackToken);
                    GitColumnDef.UnregisterPropertyChangedCallback(ColumnDefinition.WidthProperty, _gitCallbackToken);
                }

                if (_settings != null)
                {
                    _settings.SettingChanged -= OnSettingChanged;
                    _settings = null;
                }

                if (DetailsListView != null)
                {
                    DetailsListView.DoubleTapped -= OnItemDoubleClick;
                    DetailsListView.KeyDown -= OnDetailsKeyDown;
                    DetailsListView.ContainerContentChanging -= OnContainerContentChanging;
                    DetailsListView.ItemsSource = null;
                    DetailsListView.SelectedItem = null;
                }

                _unfilteredItems = null;
                _viewModel = null;
                RootGrid.DataContext = null;

                Helpers.DebugLogger.Log("[DetailsModeView] Cleanup complete");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView] Cleanup error: {ex.Message}");
            }
        }

        #endregion
    }
}
