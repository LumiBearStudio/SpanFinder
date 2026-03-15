using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.ViewModels;
using Span.Services;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;

namespace Span
{
    /// <summary>
    /// MainWindow의 분할 뷰 및 미리보기 패널 관리 부분 클래스.
    /// 좌/우 패널 활성 상태 관리, 분할 뷰 토글, 미리보기 패널 초기화·업데이트,
    /// 인라인 미리보기 컬럼(Miller Columns 모드), 선택 기반 미리보기 갱신,
    /// 활성 Explorer/ScrollViewer 접근자 등을 담당한다.
    /// </summary>
    public sealed partial class MainWindow
    {
        #region Active Pane Helpers

        /// <summary>
        /// 현재 활성 패널의 Miller Columns ItemsControl을 반환한다.
        /// 분할 뷰에서 우측 패널이 활성이면 Right 컨트롤, 아니면 활성 탭의 컨트롤을 반환한다.
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

        #endregion

        #region x:Bind Visibility / Brush Helpers

        // --- x:Bind visibility/brush helpers ---

        public Visibility IsSplitVisible(bool isSplitViewEnabled)
            => isSplitViewEnabled ? Visibility.Visible : Visibility.Collapsed;

        public Visibility IsNotSplitVisible(bool isSplitViewEnabled)
            => isSplitViewEnabled ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>
        /// Split/Preview buttons: hidden in Settings/ActionLog mode
        /// </summary>
        public Visibility IsNotSettingsMode(Models.ViewMode mode)
            => (mode != Models.ViewMode.Settings && mode != Models.ViewMode.ActionLog) ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Settings/ActionLog 모드일 때만 표시 (탭↔콘텐츠 연결 strip)
        /// </summary>
        public Visibility IsSettingsOrActionLogMode(Models.ViewMode mode)
            => (mode == Models.ViewMode.Settings || mode == Models.ViewMode.ActionLog) ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Unified Bar 하단 border: 분할뷰에서는 LeftPathHeader border와 중복되므로 제거
        /// </summary>
        public Thickness UnifiedBarBorderThickness(bool isSplitViewEnabled)
            => isSplitViewEnabled ? new Thickness(0) : new Thickness(0, 0, 0, 1);

        /// <summary>
        /// Single mode toolbar/address bar: visible when NOT split AND NOT Home mode
        /// </summary>
        public Visibility IsSingleNonHomeVisible(bool isSplitViewEnabled, Models.ViewMode mode)
            => (!isSplitViewEnabled && mode != Models.ViewMode.Home) ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Single mode nav/address bar: visible when NOT split AND NOT Settings/ActionLog mode (Home included)
        /// </summary>
        public Visibility IsSingleNonSettingsVisible(bool isSplitViewEnabled, Models.ViewMode mode)
            => (!isSplitViewEnabled && mode != Models.ViewMode.Settings && mode != Models.ViewMode.ActionLog) ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Left pane header (split mode): visible when split enabled (including Home mode for accent bar)
        /// </summary>
        public Visibility IsLeftPaneHeaderVisible(bool isSplitViewEnabled, Models.ViewMode mode)
            => (isSplitViewEnabled && mode != Models.ViewMode.Settings && mode != Models.ViewMode.ActionLog)
                ? Visibility.Visible : Visibility.Collapsed;

        public double LeftPaneAccentOpacity(ActivePane activePane)
            => activePane == ActivePane.Left ? 1.0 : 0.0;

        public double RightPaneAccentOpacity(ActivePane activePane)
            => activePane == ActivePane.Right ? 1.0 : 0.0;

        #endregion

        #region Focus Tracking

        // --- Focus tracking ---

        /// <summary>
        /// 좌측 패널 GotFocus 이벤트. ActivePane을 Left로 설정한다.
        /// </summary>
        private void OnLeftPaneGotFocus(object sender, RoutedEventArgs e)
        {
            if (ViewModel.ActivePane != ActivePane.Left)
            {
                ViewModel.ActivePane = ActivePane.Left;
            }
        }

        /// <summary>
        /// 우측 패널 GotFocus 이벤트. ActivePane을 Right로 설정한다.
        /// </summary>
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
        /// 패인 헤더 버튼 클릭 시에는 FocusActivePane()을 생략하여
        /// Button의 pressed 상태를 보존 (Click 이벤트 정상 발생 보장).
        /// </summary>
        private void OnLeftPanePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IsDragInProgress) return;
            if (ViewModel.ActivePane != ActivePane.Left)
            {
                ViewModel.ActivePane = ActivePane.Left;
                // 패인 헤더 내 버튼 클릭 시 FocusActivePane() 호출하면
                // Low priority 디스패처가 Button 포커스를 빼앗아 Click 이벤트가 씹힘
                if (!IsDescendant(LeftPathHeader, e.OriginalSource as DependencyObject))
                    FocusActivePane();
            }
        }

        private void OnRightPanePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IsDragInProgress) return;
            if (ViewModel.ActivePane != ActivePane.Right)
            {
                ViewModel.ActivePane = ActivePane.Right;
                if (!IsDescendant(RightPathHeader, e.OriginalSource as DependencyObject))
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

        #endregion

        #region Pane-Specific Flyout / View Mode Menus

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

        /// <summary>
        /// Applies all localized strings to UI elements that have hardcoded XAML text.
        /// Called once at startup and again whenever <see cref="Services.LocalizationService.LanguageChanged"/> fires.
        /// </summary>
        private void LocalizeViewModeTooltips()
        {
            // --- Toolbar tooltips (single-pane mode) ---
            ToolTipService.SetToolTip(NewTabButton, _loc.Get("Tooltip_NewTab"));
            ToolTipService.SetToolTip(BackButton, _loc.Get("Tooltip_Back"));
            ToolTipService.SetToolTip(ForwardButton, _loc.Get("Tooltip_Forward"));
            ToolTipService.SetToolTip(UpButton, _loc.Get("Tooltip_Up"));
            ToolTipService.SetToolTip(CopyPathButton, _loc.Get("Tooltip_CopyPath"));
            ToolTipService.SetToolTip(NewFolderButton, _loc.Get("Tooltip_NewFolder"));
            ToolTipService.SetToolTip(NewItemDropdown, _loc.Get("Tooltip_NewFile"));
            ToolTipService.SetToolTip(ToolbarCutButton, _loc.Get("Tooltip_Cut"));
            ToolTipService.SetToolTip(ToolbarCopyButton, _loc.Get("Tooltip_Copy"));
            ToolTipService.SetToolTip(ToolbarPasteButton, _loc.Get("Tooltip_Paste"));
            ToolTipService.SetToolTip(ToolbarRenameButton, _loc.Get("Tooltip_Rename"));
            ToolTipService.SetToolTip(ToolbarDeleteButton, _loc.Get("Tooltip_Delete"));
            ToolTipService.SetToolTip(SortButton, _loc.Get("Tooltip_Sort"));
            ToolTipService.SetToolTip(SplitViewButton, _loc.Get("Tooltip_SplitView"));
            ToolTipService.SetToolTip(PreviewToggleButton, _loc.Get("Tooltip_Preview"));

            // View mode button tooltip (all three: main, left, right)
            var vmTip = _loc.Get("ViewModeSwitch");
            ToolTipService.SetToolTip(ViewModeButton, vmTip);
            ToolTipService.SetToolTip(LeftViewModeButton, vmTip);
            ToolTipService.SetToolTip(RightViewModeButton, vmTip);

            // Sidebar bottom bar tooltips
            ToolTipService.SetToolTip(HelpButton, _loc.Get("Tooltip_Help"));
            ToolTipService.SetToolTip(LogButton, _loc.Get("Tooltip_Log"));
            ToolTipService.SetToolTip(SettingsButton, _loc.Get("Tooltip_Settings"));

            // --- Search placeholder ---
            SearchBox.PlaceholderText = _loc.Get("SearchPlaceholder");

            // --- Sidebar section labels ---
            SidebarHomeText.Text = _loc.Get("Home");
            SidebarFavoritesText.Text = _loc.Get("Favorites");
            SidebarLocalDrivesText.Text = _loc.Get("LocalDrives");
            SidebarCloudText.Text = _loc.Get("Cloud");
            SidebarNetworkText.Text = _loc.Get("Network");

            // --- Main sort menu items ---
            SortByNameItem.Text = _loc.Get("Name");
            SortByDateItem.Text = _loc.Get("Date");
            SortBySizeItem.Text = _loc.Get("Size");
            SortByTypeItem.Text = _loc.Get("Type");
            SortAscendingItem.Text = _loc.Get("Ascending");
            SortDescendingItem.Text = _loc.Get("Descending");
            GroupBySubMenu.Text = _loc.Get("GroupBy");
            GroupByNoneItem.Text = _loc.Get("None");
            GroupByNameItem.Text = _loc.Get("Name");
            GroupByTypeItem.Text = _loc.Get("Type");
            GroupByDateItem.Text = _loc.Get("Date");
            GroupBySizeItem.Text = _loc.Get("Size");

            // --- Main view mode menu items ---
            MainVm_Miller.Text = _loc.Get("MillerColumns");
            MainVm_Details.Text = _loc.Get("Details");
            MainVm_List.Text = _loc.Get("ViewMode_List");
            MainVm_Icons.Text = _loc.Get("Icons");
            MainVm_ExtraLarge.Text = _loc.Get("ExtraLargeIcons");
            MainVm_Large.Text = _loc.Get("LargeIcons");
            MainVm_Medium.Text = _loc.Get("MediumIcons");
            MainVm_Small.Text = _loc.Get("SmallIcons");

            // --- Left pane tooltips ---
            ToolTipService.SetToolTip(LeftBackButton, _loc.Get("Tooltip_Back"));
            ToolTipService.SetToolTip(LeftForwardButton, _loc.Get("Tooltip_Forward"));
            ToolTipService.SetToolTip(LeftUpButton, _loc.Get("Tooltip_Up"));
            ToolTipService.SetToolTip(LeftCopyPathButton, _loc.Get("Tooltip_CopyPath"));
            ToolTipService.SetToolTip(LeftSortButton, _loc.Get("Tooltip_Sort"));
            ToolTipService.SetToolTip(LeftPreviewButton, _loc.Get("Tooltip_Preview"));

            // Left pane sort menu items
            LeftSortByNameItem.Text = _loc.Get("Name");
            LeftSortByDateItem.Text = _loc.Get("Date");
            LeftSortBySizeItem.Text = _loc.Get("Size");
            LeftSortByTypeItem.Text = _loc.Get("Type");
            LeftSortAscendingItem.Text = _loc.Get("Ascending");
            LeftSortDescendingItem.Text = _loc.Get("Descending");

            // Left pane view mode menu items
            LeftVm_Miller.Text = _loc.Get("MillerColumns");
            LeftVm_Details.Text = _loc.Get("Details");
            LeftVm_List.Text = _loc.Get("ViewMode_List");
            LeftVm_Icons.Text = _loc.Get("Icons");
            LeftVm_ExtraLarge.Text = _loc.Get("ExtraLargeIcons");
            LeftVm_Large.Text = _loc.Get("LargeIcons");
            LeftVm_Medium.Text = _loc.Get("MediumIcons");
            LeftVm_Small.Text = _loc.Get("SmallIcons");

            // --- Right pane tooltips ---
            ToolTipService.SetToolTip(RightBackButton, _loc.Get("Tooltip_Back"));
            ToolTipService.SetToolTip(RightForwardButton, _loc.Get("Tooltip_Forward"));
            ToolTipService.SetToolTip(RightUpButton, _loc.Get("Tooltip_Up"));
            ToolTipService.SetToolTip(RightCopyPathButton, _loc.Get("Tooltip_CopyPath"));
            ToolTipService.SetToolTip(RightSortButton, _loc.Get("Tooltip_Sort"));
            ToolTipService.SetToolTip(RightPreviewButton, _loc.Get("Tooltip_Preview"));

            // Right pane sort menu items
            RightSortByNameItem.Text = _loc.Get("Name");
            RightSortByDateItem.Text = _loc.Get("Date");
            RightSortBySizeItem.Text = _loc.Get("Size");
            RightSortByTypeItem.Text = _loc.Get("Type");
            RightSortAscendingItem.Text = _loc.Get("Ascending");
            RightSortDescendingItem.Text = _loc.Get("Descending");

            // Right pane view mode menu items
            RightVm_Miller.Text = _loc.Get("MillerColumns");
            RightVm_Details.Text = _loc.Get("Details");
            RightVm_List.Text = _loc.Get("ViewMode_List");
            RightVm_Icons.Text = _loc.Get("Icons");
            RightVm_ExtraLarge.Text = _loc.Get("ExtraLargeIcons");
            RightVm_Large.Text = _loc.Get("LargeIcons");
            RightVm_Medium.Text = _loc.Get("MediumIcons");
            RightVm_Small.Text = _loc.Get("SmallIcons");

            // --- Tab headers (Home / Settings / ActionLog) ---
            foreach (var tab in ViewModel.Tabs)
            {
                if (tab.ViewMode == Models.ViewMode.Home)
                    tab.Header = _loc.Get("Home");
                else if (tab.ViewMode == Models.ViewMode.Settings)
                    tab.Header = _loc.Get("Settings");
                else if (tab.ViewMode == Models.ViewMode.ActionLog)
                    tab.Header = _loc.Get("Log_Title");
            }

            // --- Sidebar favorites: localize known folder names ---
            ViewModel.LocalizeFavoriteNames();
        }

        #endregion

        #region Pane Preview Toggle

        private void OnPanePreviewToggle(object sender, RoutedEventArgs e)
        {
            // Tag에서 대상 패인을 결정 — ActivePane은 변경하지 않음 (포커스 사이드이펙트 방지)
            var targetPane = ActivePane.Left;
            if (sender is FrameworkElement fe && fe.Tag is string tag)
                targetPane = tag == "Right" ? ActivePane.Right : ActivePane.Left;

            TogglePreviewForPane(targetPane);
            UpdatePreviewButtonState();
        }

        #endregion

        // Breadcrumb scroll/overflow and breadcrumb click/chevron logic
        // are now handled internally by AddressBarControl.
        // Events are dispatched via OnAddressBarBreadcrumbClicked / OnAddressBarChevronClicked
        // in MainWindow.NavigationManager.cs.

        // ──── Legacy handlers removed ────
        // OnBreadcrumbScrollerSizeChanged, OnBreadcrumbContentSizeChanged,
        // OnBreadcrumbScrollerViewChanged, UpdateBreadcrumbOverflow,
        // OnPaneBreadcrumbClick, OnBreadcrumbChevronClick
        // are all now internal to AddressBarControl.

        #region Split View Toggle

        // --- Split View Toggle ---

        /// <summary>
        /// 분할 뷰 토글 버튼 클릭 이벤트.
        /// </summary>
        private void OnSplitViewToggleClick(object sender, RoutedEventArgs e)
        {
            ToggleSplitView();
            UpdateSplitViewButtonState();
        }

        /// <summary>
        /// RightExplorer PropertyChanged 구독 — RightAddressBar 동기화용
        /// </summary>
        private PropertyChangedEventHandler? _rightExplorerAddressBarHandler;

        private void ToggleSplitView()
        {
            ViewModel.IsSplitViewEnabled = !ViewModel.IsSplitViewEnabled;

            if (ViewModel.IsSplitViewEnabled)
            {
                SplitterCol.Width = new GridLength(0);
                RightPaneCol.Width = new GridLength(1, GridUnitType.Star);

                // Sync left pane breadcrumb — 비활성 상태에서 탭 전환 시 갱신 안 된 경우 보정
                if (ViewModel.Explorer?.PathSegments != null)
                {
                    LeftAddressBar.PathSegments = ViewModel.Explorer.PathSegments;
                    LeftAddressBar.CurrentPath = ViewModel.Explorer.CurrentPath;
                }

                // Initialize right pane based on Tab2 startup settings
                if (ViewModel.RightExplorer.Columns.Count == 0 ||
                    ViewModel.RightExplorer.CurrentPath == "PC")
                {
                    var tab2Behavior = _settings.Tab2StartupBehavior;
                    if (tab2Behavior == 0)
                    {
                        // Home: 우측 패인에 홈 화면 표시
                        ViewModel.RightViewMode = Models.ViewMode.Home;
                        Helpers.DebugLogger.Log("[ToggleSplitView] Right pane → Home view");
                    }
                    else if (tab2Behavior == 2 && !string.IsNullOrEmpty(_settings.Tab2StartupPath)
                        && System.IO.Directory.Exists(_settings.Tab2StartupPath))
                    {
                        // CustomPath: 사용자 지정 경로로 이동
                        _ = ViewModel.RightExplorer.NavigateToPath(_settings.Tab2StartupPath);
                        Helpers.DebugLogger.Log($"[ToggleSplitView] Right pane → custom path: {_settings.Tab2StartupPath}");
                    }
                    else
                    {
                        // RestoreSession (behavior=1) 또는 fallback: 저장된 경로 복원
                        NavigateRightPaneToRealPath();
                        Helpers.DebugLogger.Log("[ToggleSplitView] Right pane → restore session");
                    }
                }

                // RightExplorer 네비게이션 시 RightAddressBar 자동 동기화
                SyncRightAddressBar();
                SubscribeRightExplorerForAddressBar();

                // Close ALL previews when entering split view (saves screen space)
                // 1) 사이드 패널 미리보기 비활성화
                ViewModel.IsLeftPreviewEnabled = false;
                LeftPreviewSplitterCol.Width = new GridLength(0);
                LeftPreviewCol.Width = new GridLength(0);
                LeftPreviewPanel.StopMedia();

                ViewModel.IsRightPreviewEnabled = false;
                RightPreviewSplitterCol.Width = new GridLength(0);
                RightPreviewCol.Width = new GridLength(0);
                RightPreviewPanel.StopMedia();

                // 2) 인라인 미리보기(Miller Columns) 설정 + UI 모두 비활성화
                try { var s = App.Current.Services.GetRequiredService<SettingsService>(); s.MillerInlinePreviewEnabled = false; } catch { }
                HideInlinePreview();

                // 3) 버튼 상태 동기화
                UpdatePreviewButtonState();

                // Set active pane to right and focus it after UI has updated
                ViewModel.ActivePane = ActivePane.Right;
                FocusActivePane();

                Helpers.DebugLogger.Log("[MainWindow] Split View enabled");
            }
            else
            {
                SplitterCol.Width = new GridLength(0);
                RightPaneCol.Width = new GridLength(0);

                // Right preview panel 정리 — 분할뷰 해제 시 우측 미리보기가 남는 버그 방지
                ViewModel.IsRightPreviewEnabled = false;
                RightPreviewSplitterCol.Width = new GridLength(0);
                RightPreviewCol.Width = new GridLength(0);
                RightPreviewPanel.StopMedia();

                // Sync main address bar — Split 모드에서 갱신 안 된 경우 보정
                if (ViewModel.Explorer?.PathSegments != null)
                {
                    MainAddressBar.PathSegments = ViewModel.Explorer.PathSegments;
                    MainAddressBar.CurrentPath = ViewModel.Explorer.CurrentPath;
                }

                // RightExplorer 구독 해제
                UnsubscribeRightExplorerForAddressBar();

                // 미리보기 상태 복원: 분할뷰 진입 시 비활성화했으므로 기본 설정값으로 복원
                // Miller 모드: 인라인 미리보기만 사용 (사이드 패널 X)
                // 그 외 모드: 사이드 패널 미리보기만 사용 (인라인 X)
                try
                {
                    var settingsSvc = App.Current.Services.GetRequiredService<SettingsService>();
                    var previewDefault = settingsSvc.DefaultPreviewEnabled;
                    var isMillerMode = ViewModel.CurrentViewMode == Models.ViewMode.MillerColumns;

                    if (isMillerMode)
                    {
                        // Miller 모드: 인라인 미리보기만 복원, 사이드 패널은 숨김
                        ViewModel.IsLeftPreviewEnabled = false;
                        LeftPreviewSplitterCol.Width = new GridLength(0);
                        LeftPreviewCol.Width = new GridLength(0);
                        settingsSvc.MillerInlinePreviewEnabled = previewDefault;
                        if (previewDefault)
                        {
                            ShowInlinePreview();
                        }
                    }
                    else
                    {
                        // Details/List/Icon 모드: 사이드 패널만 복원, 인라인은 숨김
                        ViewModel.IsLeftPreviewEnabled = previewDefault;
                        settingsSvc.MillerInlinePreviewEnabled = false;
                        if (previewDefault)
                        {
                            LeftPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
                            LeftPreviewCol.Width = new GridLength(GetSavedPreviewWidth("LeftPreviewWidth"), GridUnitType.Pixel);
                            SubscribePreviewToLastColumn(isLeft: true);
                        }
                    }
                    UpdatePreviewButtonState();
                    Helpers.DebugLogger.Log($"[MainWindow] Preview restored to default={previewDefault}, millerMode={isMillerMode} after split view disabled");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[MainWindow] Preview restore error: {ex.Message}");
                }

                // Reset active pane to left and focus it
                ViewModel.ActivePane = ActivePane.Left;
                FocusActivePane();

                Helpers.DebugLogger.Log("[MainWindow] Split View disabled");
            }
        }

        private void SyncRightAddressBar()
        {
            if (ViewModel.RightExplorer != null)
            {
                RightAddressBar.PathSegments = ViewModel.RightExplorer.PathSegments;
                RightAddressBar.CurrentPath = ViewModel.RightExplorer.CurrentPath ?? string.Empty;
            }
        }

        private void SubscribeRightExplorerForAddressBar()
        {
            UnsubscribeRightExplorerForAddressBar();
            if (ViewModel.RightExplorer == null) return;

            _rightExplorerAddressBarHandler = (s, e) =>
            {
                if (e.PropertyName == nameof(ExplorerViewModel.CurrentPath) ||
                    e.PropertyName == nameof(ExplorerViewModel.PathSegments))
                {
                    DispatcherQueue.TryEnqueue(() => SyncRightAddressBar());
                }
            };
            ViewModel.RightExplorer.PropertyChanged += _rightExplorerAddressBarHandler;
        }

        private void UnsubscribeRightExplorerForAddressBar()
        {
            if (_rightExplorerAddressBarHandler != null && ViewModel.RightExplorer != null)
            {
                ViewModel.RightExplorer.PropertyChanged -= _rightExplorerAddressBarHandler;
                _rightExplorerAddressBarHandler = null;
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

        #endregion

        #region Pane Navigation / Copy Path

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
                    ViewModel.ShowToast(_loc.Get("Toast_PathCopied"), 2000);
                }
            }
        }

        #endregion

        #region Focus Active Pane

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

        #endregion

        // =================================================================
        //  Preview Panel
        // =================================================================

        #region Preview Panel

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

            // Defensive unsubscribe before subscribe to prevent handler accumulation
            ViewModel.LeftExplorer.Columns.CollectionChanged -= OnLeftColumnsChangedForPreview;
            ViewModel.RightExplorer.Columns.CollectionChanged -= OnRightColumnsChangedForPreview;
            ViewModel.PropertyChanged -= OnViewModelPropertyChangedForPreview;

            // Subscribe to LeftExplorer column changes for preview updates
            ViewModel.LeftExplorer.Columns.CollectionChanged += OnLeftColumnsChangedForPreview;
            ViewModel.RightExplorer.Columns.CollectionChanged += OnRightColumnsChangedForPreview;

            // Subscribe to ViewModel property changes for preview state
            ViewModel.PropertyChanged += OnViewModelPropertyChangedForPreview;

            // Initialize inline preview column
            InitializeInlinePreview();

            // Initialize Git status bars
            InitializeGitStatusBars();
        }

        /// <summary>
        /// When columns change, subscribe to the last column's SelectedChild for preview.
        /// </summary>
        private void OnLeftColumnsChangedForPreview(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isClosed) return;
            // 사이드 패널 또는 인라인 미리보기 중 하나라도 활성이면 구독 필요
            bool needSubscribe = ViewModel.IsLeftPreviewEnabled
                || ViewModel.CurrentViewMode == Models.ViewMode.MillerColumns;
            if (!needSubscribe) return;
            SubscribePreviewToLastColumn(isLeft: true);
        }

        private void OnRightColumnsChangedForPreview(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isClosed) return;
            bool needSubscribe = ViewModel.IsRightPreviewEnabled
                || ViewModel.CurrentViewMode == Models.ViewMode.MillerColumns;
            if (!needSubscribe) return;
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

            // Immediately update preview with current selection.
            var selectedChild = lastColumn.SelectedChild;
            var previewPanel = isLeft ? LeftPreviewPanel : RightPreviewPanel;
            previewPanel.UpdatePreview(FilterPreviewItem(selectedChild ?? lastColumn));
        }

        /// <summary>
        /// 미리보기 대상 항목 필터: PreviewShowFolderInfo 설정에 따라 폴더 항목을 null로 변환.
        /// </summary>
        private FileSystemViewModel? FilterPreviewItem(FileSystemViewModel? item)
        {
            if (item is FolderViewModel)
            {
                try
                {
                    var settings = App.Current.Services.GetRequiredService<SettingsService>();
                    if (!settings.PreviewShowFolderInfo) return null;
                }
                catch { return null; }
            }
            return item;
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
            if (_isClosed) return;

            if (sender is FolderViewModel folder)
            {
                // 사이드 패널 미리보기 — FilterPreviewItem으로 폴더 표시 여부 결정
                if (ViewModel.IsLeftPreviewEnabled)
                    LeftPreviewPanel.UpdatePreview(FilterPreviewItem(folder.SelectedChild ?? folder));

                // Quick Look 윈도우가 열려 있으면 내용 업데이트
                if (ViewModel.ActivePane == ActivePane.Left)
                    UpdateQuickLookContent(folder.SelectedChild);
            }
        }

        private void OnRightColumnSelectionForPreview(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FolderViewModel.SelectedChild)) return;
            if (_isClosed) return;

            if (sender is FolderViewModel folder)
            {
                // 사이드 패널 미리보기 — FilterPreviewItem으로 폴더 표시 여부 결정
                if (ViewModel.IsRightPreviewEnabled)
                    RightPreviewPanel.UpdatePreview(FilterPreviewItem(folder.SelectedChild ?? folder));

                // Quick Look 윈도우가 열려 있으면 내용 업데이트
                if (ViewModel.ActivePane == ActivePane.Right)
                    UpdateQuickLookContent(folder.SelectedChild);
            }
        }

        /// <summary>
        /// Update preview when selection changes in Details/Icon mode (via Miller column selection handler).
        /// </summary>
        private void UpdatePreviewForSelection(FileSystemViewModel? selectedItem)
        {
            if (_isClosed) return;

            var filtered = FilterPreviewItem(selectedItem);
            if (ViewModel.ActivePane == ActivePane.Left && ViewModel.IsLeftPreviewEnabled)
                LeftPreviewPanel.UpdatePreview(filtered);
            else if (ViewModel.ActivePane == ActivePane.Right && ViewModel.IsRightPreviewEnabled)
                RightPreviewPanel.UpdatePreview(filtered);

            // Quick Look 윈도우가 열려 있으면 내용 업데이트
            UpdateQuickLookContent(selectedItem);
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
            UpdatePreviewButtonState();
        }

        private void TogglePreviewPanel()
        {
            TogglePreviewForPane(ViewModel.ActivePane);
        }

        /// <summary>
        /// 지정된 패인의 미리보기를 토글.
        /// ActivePane을 건드리지 않고 대상 프로퍼티를 직접 토글하여 경합 제거.
        /// </summary>
        private void TogglePreviewForPane(ActivePane targetPane)
        {
            // 대상 패인의 뷰모드 확인
            var targetViewMode = ViewModel.IsSplitViewEnabled && targetPane == ActivePane.Right
                ? ViewModel.RightViewMode
                : ViewModel.CurrentViewMode;

            // Miller Columns + 좌측 패인: 인라인 미리보기 토글 (좌측 전용 기능)
            // 우측 패인은 Miller에서도 사이드 패널 사용 (인라인 미리보기 인프라 없음)
            if (targetViewMode == Models.ViewMode.MillerColumns && targetPane == ActivePane.Left)
            {
                ToggleInlinePreview();
                return;
            }

            // Details/List/Icon 모드 또는 우측 Miller: 사이드 패널 토글 (ActivePane 변경 없음)
            if (targetPane == ActivePane.Left)
            {
                ViewModel.IsLeftPreviewEnabled = !ViewModel.IsLeftPreviewEnabled;
                if (ViewModel.IsLeftPreviewEnabled)
                {
                    LeftPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
                    LeftPreviewCol.Width = new GridLength(GetSavedPreviewWidth("LeftPreviewWidth"), GridUnitType.Pixel);
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
                ViewModel.IsRightPreviewEnabled = !ViewModel.IsRightPreviewEnabled;
                if (ViewModel.IsRightPreviewEnabled)
                {
                    RightPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
                    RightPreviewCol.Width = new GridLength(GetSavedPreviewWidth("RightPreviewWidth"), GridUnitType.Pixel);
                }
                else
                {
                    RightPreviewSplitterCol.Width = new GridLength(0);
                    RightPreviewCol.Width = new GridLength(0);
                    RightPreviewPanel.StopMedia();
                }
            }
            ViewModel.SavePreviewState();

            Helpers.DebugLogger.Log($"[MainWindow] Preview toggled (pane={targetPane}): Left={ViewModel.IsLeftPreviewEnabled}, Right={ViewModel.IsRightPreviewEnabled}");

            // After preview toggle, the Miller columns viewport width changes.
            // Scroll to keep the last column visible.
            var explorer = ViewModel.ActiveExplorer;
            if (explorer != null && explorer.Columns.Count > 0)
            {
                var scrollViewer = GetActiveMillerScrollViewer();
                ScrollToLastColumn(explorer, scrollViewer);
            }
        }

        /// <summary>
        /// Miller Columns 모드 전용: 인라인 미리보기 토글.
        /// 설정에 저장하고 현재 선택된 파일에 따라 표시/숨김.
        /// </summary>
        private void ToggleInlinePreview()
        {
            try
            {
                var settings = App.Current.Services.GetRequiredService<SettingsService>();
                settings.MillerInlinePreviewEnabled = !settings.MillerInlinePreviewEnabled;

                if (settings.MillerInlinePreviewEnabled)
                {
                    // 현재 선택된 파일이 있으면 인라인 미리보기 표시
                    var explorer = ViewModel.ActiveExplorer;
                    UpdateInlinePreviewColumn(explorer?.SelectedFile);
                }
                else
                {
                    HideInlinePreview();
                }

                Helpers.DebugLogger.Log($"[MainWindow] Inline preview toggled: {settings.MillerInlinePreviewEnabled}");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainWindow] ToggleInlinePreview error: {ex.Message}");
            }
        }

        /// <summary>
        /// 미리보기 토글 버튼의 활성 상태를 시각적으로 업데이트.
        /// Miller Columns 모드: 인라인 미리보기 설정 기반
        /// Details/List/Icon 모드: 사이드 패널 활성화 상태 기반
        /// </summary>
        internal void UpdatePreviewButtonState()
        {
            try
            {
                var accentBrush = GetThemeBrush("SpanAccentBrush");
                var defaultBrush = GetThemeBrush("SpanTextSecondaryBrush");

                // 상단 미리보기 버튼 (비분할 모드용)
                if (!ViewModel.IsSplitViewEnabled)
                {
                    bool isActive;
                    if (ViewModel.CurrentViewMode == Models.ViewMode.MillerColumns)
                    {
                        var settings = App.Current.Services.GetRequiredService<SettingsService>();
                        isActive = settings.MillerInlinePreviewEnabled;
                    }
                    else
                    {
                        isActive = ViewModel.IsLeftPreviewEnabled;
                    }
                    PreviewToggleIcon.Foreground = isActive ? accentBrush : defaultBrush;
                }

                // Split view pane-specific buttons
                if (ViewModel.IsSplitViewEnabled)
                {
                    var settings = App.Current.Services.GetRequiredService<SettingsService>();
                    bool millerInline = settings.MillerInlinePreviewEnabled;

                    bool leftActive = ViewModel.LeftViewMode == Models.ViewMode.MillerColumns
                        ? millerInline
                        : ViewModel.IsLeftPreviewEnabled;
                    // 우측 패인은 Miller에서도 사이드 패널 사용 (인라인 미리보기는 좌측 전용)
                    bool rightActive = ViewModel.IsRightPreviewEnabled;

                    LeftPreviewIcon.Foreground = leftActive ? accentBrush : defaultBrush;
                    RightPreviewIcon.Foreground = rightActive ? accentBrush : defaultBrush;
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainWindow] UpdatePreviewButtonState error: {ex.Message}");
            }
        }

        /// <summary>
        /// 뷰모드 버튼 아이콘을 현재 활성 뷰모드에 맞게 업데이트.
        /// </summary>
        internal void UpdateViewModeIcon()
        {
            try
            {
                var mode = ViewModel.CurrentViewMode;
                string glyph = GetViewModeGlyph(mode);

                ViewModeIcon.Glyph = glyph;

                // Split view pane-specific buttons
                if (ViewModel.IsSplitViewEnabled)
                {
                    LeftViewModeIcon.Glyph = GetViewModeGlyph(ViewModel.LeftViewMode);
                    RightViewModeIcon.Glyph = GetViewModeGlyph(ViewModel.RightViewMode);
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainWindow] UpdateViewModeIcon error: {ex.Message}");
            }
        }

        private static string GetViewModeGlyph(Models.ViewMode mode) => mode switch
        {
            Models.ViewMode.MillerColumns => "\uF0E2",
            Models.ViewMode.Details => "\uE8EF",
            Models.ViewMode.List => "\uE80A",
            _ when mode >= Models.ViewMode.IconSmall && mode <= Models.ViewMode.IconExtraLarge => "\uE91B",
            _ => "\uF0E2"
        };

        internal void UpdateSplitViewButtonState()
        {
            try
            {
                var accentBrush = GetThemeBrush("SpanAccentBrush");
                var defaultBrush = GetThemeBrush("SpanTextSecondaryBrush");

                SplitViewIcon.Foreground = ViewModel.IsSplitViewEnabled ? accentBrush : defaultBrush;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainWindow] UpdateSplitViewButtonState error: {ex.Message}");
            }
        }

        /// <summary>
        /// 우측 패인의 뷰모드 변경 시 프리뷰 패널 너비를 동기화.
        /// Miller → Details/List/Icon 전환 시 프리뷰 활성화 상태에 맞게 너비 조정.
        /// </summary>
        internal void SyncRightPreviewPanelWidth()
        {
            try
            {
                if (!ViewModel.IsSplitViewEnabled) return;

                var rightMode = ViewModel.RightViewMode;

                // Miller 모드는 인라인 미리보기 사용 → 사이드 패널 불필요
                if (rightMode == Models.ViewMode.MillerColumns)
                {
                    RightPreviewSplitterCol.Width = new GridLength(0);
                    RightPreviewCol.Width = new GridLength(0);
                }
                else if (ViewModel.IsRightPreviewEnabled)
                {
                    RightPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
                    RightPreviewCol.Width = new GridLength(GetSavedPreviewWidth("RightPreviewWidth"), GridUnitType.Pixel);
                }
                else
                {
                    RightPreviewSplitterCol.Width = new GridLength(0);
                    RightPreviewCol.Width = new GridLength(0);
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainWindow] SyncRightPreviewPanelWidth error: {ex.Message}");
            }
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
                    double leftW = 320;
                    if (settings.Values.TryGetValue("LeftPreviewWidth", out var lw))
                        leftW = Math.Max(320, (double)lw);
                    LeftPreviewCol.Width = new GridLength(leftW, GridUnitType.Pixel);
                    SubscribePreviewToLastColumn(isLeft: true);
                }

                if (ViewModel.IsRightPreviewEnabled)
                {
                    RightPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
                    double rightW = 320;
                    if (settings.Values.TryGetValue("RightPreviewWidth", out var rw))
                        rightW = Math.Max(320, (double)rw);
                    RightPreviewCol.Width = new GridLength(rightW, GridUnitType.Pixel);
                    SubscribePreviewToLastColumn(isLeft: false);
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainWindow] RestorePreviewState error: {ex.Message}");
            }
        }

        /// <summary>
        /// LocalSettings에 저장된 미리보기 패널 너비를 읽는다. 미저장 시 기본 320px.
        /// </summary>
        private static double GetSavedPreviewWidth(string key)
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(key, out var val))
                    return Math.Max(320, (double)val);
            }
            catch { }
            return 320;
        }

        #endregion

        // =================================================================
        //  Inline Preview Column (inside Miller Columns)
        // =================================================================

        #region Inline Preview Column

        /// <summary>
        /// Initialize inline preview column by subscribing to SelectedFile changes on the active explorer.
        /// Called from InitializePreviewPanels and when explorer changes (tab switch, etc.).
        /// </summary>
        private void InitializeInlinePreview()
        {
            _inlinePreviewService ??= App.Current.Services.GetRequiredService<PreviewService>();

            // Defensive unsubscribe before subscribe to prevent handler accumulation
            ViewModel.LeftExplorer.PropertyChanged -= OnExplorerSelectedFileChanged;

            // Subscribe to SelectedFile changes on the left explorer
            ViewModel.LeftExplorer.PropertyChanged += OnExplorerSelectedFileChanged;

            // 밀러 컬럼 영역 리사이즈 시 인라인 미리보기 폭 재계산
            MillerTabsHost.SizeChanged -= OnMillerTabsHostSizeChanged;
            MillerTabsHost.SizeChanged += OnMillerTabsHostSizeChanged;
        }

        private void OnMillerTabsHostSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (InlinePreviewColumn.Visibility != Visibility.Visible) return;

            // 디바운싱: 연속 SizeChanged 이벤트를 100ms로 병합
            if (_sizeChangedDebounceTimer == null)
            {
                _sizeChangedDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _sizeChangedDebounceTimer.Tick += (s, args) =>
                {
                    _sizeChangedDebounceTimer.Stop();
                    ApplyMillerColumnWidth();
                };
            }
            _sizeChangedDebounceTimer.Stop();
            _sizeChangedDebounceTimer.Start();
        }

        /// <summary>
        /// 밀러 컬럼 Pixel 너비를 실제 컨텐츠 크기 기반으로 재계산.
        /// 밀러 컨텐츠가 필요한 폭만큼만 사용하고 나머지를 미리보기에 할당.
        /// 값이 변경된 경우에만 적용하여 불필요한 레이아웃 패스 방지.
        /// </summary>
        private void ApplyMillerColumnWidth()
        {
            if (InlinePreviewColumn.Visibility != Visibility.Visible) return;

            // 밀러=Star, 미리보기=고정 픽셀 — ShowInlinePreview()와 동일 방식
            // SizeChanged 시 미리보기 너비를 재확인만 수행
            double previewWidth = GetSavedPreviewWidth("LeftPreviewWidth");
            if (Math.Abs(previewWidth - _lastMillerMaxWidth) < 1) return;
            _lastMillerMaxWidth = previewWidth;

            MillerTabsHost.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            InlinePreviewCol.Width = new GridLength(previewWidth, GridUnitType.Pixel);
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
        /// Miller Columns 모드에서만 활성화, 다른 뷰 모드에서는 숨김.
        /// </summary>
        private async void UpdateInlinePreviewColumn(FileViewModel? fileVm)
        {
            if (_isClosed) return;

            // Miller Columns 모드가 아니면 인라인 프리뷰 숨김
            if (ViewModel.CurrentViewMode != Models.ViewMode.MillerColumns)
            {
                HideInlinePreview();
                return;
            }

            // 설정에서 인라인 프리뷰가 비활성화되어 있으면 숨김
            try
            {
                var settings = App.Current.Services.GetRequiredService<SettingsService>();
                if (!settings.MillerInlinePreviewEnabled)
                {
                    HideInlinePreview();
                    return;
                }
            }
            catch { }

            try { _inlinePreviewCts?.Cancel(); _inlinePreviewCts?.Dispose(); } catch (ObjectDisposedException) { }
            _inlinePreviewCts = null;

            if (fileVm == null)
            {
                HideInlinePreview();
                return;
            }

            try
            {
            // Show the inline preview column via Grid columns
            ShowInlinePreview();

            // Basic info (즉시 표시 가능한 데이터만 — 동기 I/O 없음)
            InlinePreviewFileName.Text = fileVm.Name;
            InlinePreviewIcon.Glyph = fileVm.IconGlyph;
            InlinePreviewIcon.Foreground = fileVm.IconBrush;
            InlinePreviewFileType.Text = fileVm.FileType;
            InlinePreviewDateModified.Text = fileVm.DateModified;

            // 원격 파일: 모델 데이터 사용 (I/O 없음)
            if (Services.FileSystemRouter.IsRemotePath(fileVm.Path))
            {
                InlinePreviewFileSize.Text = fileVm.Size;
                InlinePreviewDateCreatedRow.Visibility = Visibility.Collapsed;
            }
            else
            {
                // 로컬 파일: 크기/생성일은 뷰모델 데이터로 즉시 표시, 상세 메타는 비동기 구간에서 보강
                InlinePreviewFileSize.Text = fileVm.Size;
                InlinePreviewDateCreatedRow.Visibility = Visibility.Collapsed;
            }

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

            // Reset Git section
            InlinePreviewGitSection.Visibility = Visibility.Collapsed;
            InlinePreviewGitCommit.Text = "";

            // Determine preview type and load async content
            var previewType = _inlinePreviewService.GetPreviewType(fileVm.Path, false);

            _inlinePreviewCts = new CancellationTokenSource();
            var ct = _inlinePreviewCts.Token;

            try
            {
                // 로컬 파일 메타데이터를 비동기로 로딩 (UI 스레드 차단 방지)
                if (!Services.FileSystemRouter.IsRemotePath(fileVm.Path))
                {
                    var path = fileVm.Path;
                    var metadata = await Task.Run(() => _inlinePreviewService!.GetBasicMetadata(path), ct);
                    if (ct.IsCancellationRequested) return;
                    InlinePreviewFileSize.Text = metadata.SizeFormatted;
                    InlinePreviewDateCreated.Text = metadata.Created.ToString("yyyy-MM-dd HH:mm");
                    InlinePreviewDateCreatedRow.Visibility = Visibility.Visible;
                }

                switch (previewType)
                {
                    case Models.PreviewType.Image:
                        var imageBitmap = await _inlinePreviewService.LoadImagePreviewAsync(fileVm.Path, 512, ct);
                        if (ct.IsCancellationRequested) return;
                        if (imageBitmap != null)
                        {
                            // PreviewPanelView와 동일: 헤더는 아이콘, 아래에 이미지 1개만 표시
                            InlinePreviewImage.Source = imageBitmap;
                            InlinePreviewImage.Visibility = Visibility.Visible;
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
                // Normal - user selected another file quickly
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[InlinePreview] Error loading preview: {ex.Message}");
            }

            // Git Tier 1: 파일 마지막 커밋 정보
            if (!ct.IsCancellationRequested)
            {
                try
                {
                    var gitService = App.Current.Services.GetService<GitStatusService>();
                    if (gitService?.IsAvailable == true)
                    {
                        var settings = App.Current.Services.GetService<ISettingsService>();
                        if (settings?.ShowGitIntegration == true)
                        {
                            var commit = await gitService.GetLastCommitAsync(fileVm.Path, ct);
                            if (!ct.IsCancellationRequested && commit != null)
                            {
                                InlinePreviewGitCommit.Text = $"{commit.Subject}\n{commit.Author} · {commit.RelativeTime}";
                                InlinePreviewGitSection.Visibility = Visibility.Visible;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[InlinePreview] Git info error: {ex.Message}");
                }
            }

            // Scroll the miller columns to keep last column visible
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
            } // end outer try
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[InlinePreview] Outer error: {ex.Message}");
                HideInlinePreview();
            }
        }

        /// <summary>
        /// 인라인 미리보기 컬럼 표시 — Grid 컬럼 너비 설정 및 Border Visible.
        /// </summary>
        private void ShowInlinePreview()
        {
            InlinePreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);

            // 밀러=Star(남은 공간 전부), 미리보기=고정 픽셀
            // 사이드 패널 미리보기와 동일한 너비 사용 (기본 320px)
            double previewWidth = GetSavedPreviewWidth("LeftPreviewWidth");
            MillerTabsHost.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            InlinePreviewCol.Width = new GridLength(previewWidth, GridUnitType.Pixel);

            InlinePreviewColumn.MinWidth = 320;
            InlinePreviewColumn.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 인라인 미리보기 컬럼 숨김 — Grid 컬럼 너비 0으로 설정 및 Border Collapsed.
        /// </summary>
        private void HideInlinePreview()
        {
            try { _inlinePreviewCts?.Cancel(); } catch (ObjectDisposedException) { }
            _sizeChangedDebounceTimer?.Stop();
            InlinePreviewSplitterCol.Width = new GridLength(0);
            InlinePreviewCol.Width = new GridLength(0);
            // 밀러 컬럼을 전체 공간으로 복원
            MillerTabsHost.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            _lastMillerMaxWidth = 0;
            InlinePreviewColumn.MinWidth = 0;
            InlinePreviewColumn.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Clean up inline preview resources.
        /// </summary>
        private void CleanupInlinePreview()
        {
            try { _inlinePreviewCts?.Cancel(); _inlinePreviewCts?.Dispose(); } catch (ObjectDisposedException) { }
            _inlinePreviewCts = null;

            if (ViewModel?.LeftExplorer != null)
                ViewModel.LeftExplorer.PropertyChanged -= OnExplorerSelectedFileChanged;
        }

        #endregion

        // =================================================================
        //  Git Status Bar (bottom of explorer)
        // =================================================================

        #region Git Status Bar

        private PropertyChangedEventHandler? _leftExplorerGitHandler;
        private PropertyChangedEventHandler? _rightExplorerGitHandler;

        /// <summary>
        /// Git 상태바 초기화: ViewModel 생성, 이벤트 구독, SizeChanged 연결.
        /// </summary>
        private void InitializeGitStatusBars()
        {
            _leftGitStatusBarVm = new GitStatusBarViewModel();
            _rightGitStatusBarVm = new GitStatusBarViewModel();

            // PropertyChanged로 UI 바인딩
            _leftGitStatusBarVm.PropertyChanged += OnLeftGitStatusBarChanged;
            _rightGitStatusBarVm.PropertyChanged += OnRightGitStatusBarChanged;

            // Explorer CurrentPath 변경 구독
            SubscribeGitStatusToExplorer(isLeft: true);
            SubscribeGitStatusToExplorer(isLeft: false);

            // SizeChanged로 반응형 텍스트 갱신
            LeftGitStatusBar.SizeChanged += (s, e) =>
                _leftGitStatusBarVm?.UpdateStatusText(e.NewSize.Width);
            RightGitStatusBar.SizeChanged += (s, e) =>
                _rightGitStatusBarVm?.UpdateStatusText(e.NewSize.Width);

            // 초기 경로로 갱신
            _ = _leftGitStatusBarVm.UpdateForPathAsync(ViewModel.LeftExplorer?.CurrentPath);
            _ = _rightGitStatusBarVm.UpdateForPathAsync(ViewModel.RightExplorer?.CurrentPath);
        }

        /// <summary>
        /// Explorer.CurrentPath 변경을 감시하여 Git 상태바 갱신.
        /// </summary>
        private void SubscribeGitStatusToExplorer(bool isLeft)
        {
            UnsubscribeGitStatusFromExplorer(isLeft);

            var explorer = isLeft ? ViewModel.LeftExplorer : ViewModel.RightExplorer;
            if (explorer == null) return;

            PropertyChangedEventHandler handler = (s, e) =>
            {
                if (e.PropertyName == nameof(ExplorerViewModel.CurrentPath))
                {
                    var vm = isLeft ? _leftGitStatusBarVm : _rightGitStatusBarVm;
                    var path = (s as ExplorerViewModel)?.CurrentPath;
                    if (vm != null)
                        _ = vm.UpdateForPathAsync(path);
                }
            };

            explorer.PropertyChanged += handler;
            if (isLeft) _leftExplorerGitHandler = handler;
            else _rightExplorerGitHandler = handler;
        }

        private void UnsubscribeGitStatusFromExplorer(bool isLeft)
        {
            var explorer = isLeft ? ViewModel.LeftExplorer : ViewModel.RightExplorer;
            if (isLeft && _leftExplorerGitHandler != null)
            {
                if (explorer != null) explorer.PropertyChanged -= _leftExplorerGitHandler;
                _leftExplorerGitHandler = null;
            }
            else if (!isLeft && _rightExplorerGitHandler != null)
            {
                if (explorer != null) explorer.PropertyChanged -= _rightExplorerGitHandler;
                _rightExplorerGitHandler = null;
            }
        }

        /// <summary>
        /// 탭 전환 시 Git 상태바 Explorer 구독을 재연결.
        /// </summary>
        internal void ResubscribeGitStatusBar(bool isLeft)
        {
            SubscribeGitStatusToExplorer(isLeft);
            var explorer = isLeft ? ViewModel.LeftExplorer : ViewModel.RightExplorer;
            var vm = isLeft ? _leftGitStatusBarVm : _rightGitStatusBarVm;
            if (vm != null && explorer != null)
                _ = vm.UpdateForPathAsync(explorer.CurrentPath);
        }

        /// <summary>
        /// Left Git 상태바 ViewModel → UI 동기화.
        /// </summary>
        private void OnLeftGitStatusBarChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isClosed) return;
            DispatcherQueue.TryEnqueue(() => SyncGitStatusBarUI(isLeft: true));
        }

        private void OnRightGitStatusBarChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isClosed) return;
            DispatcherQueue.TryEnqueue(() => SyncGitStatusBarUI(isLeft: false));
        }

        /// <summary>
        /// GitStatusBarViewModel 데이터를 XAML 요소에 반영.
        /// </summary>
        private void SyncGitStatusBarUI(bool isLeft)
        {
            var vm = isLeft ? _leftGitStatusBarVm : _rightGitStatusBarVm;
            if (vm == null) return;

            var bar = isLeft ? LeftGitStatusBar : RightGitStatusBar;
            var branchTb = isLeft ? LeftGitBranch : RightGitBranch;
            var statusTb = isLeft ? LeftGitStatus : RightGitStatus;
            var flyoutBranch = isLeft ? LeftFlyoutBranch : RightFlyoutBranch;
            var flyoutStatus = isLeft ? LeftFlyoutStatus : RightFlyoutStatus;
            var flyoutCommitsLabel = isLeft ? LeftFlyoutCommitsLabel : RightFlyoutCommitsLabel;
            var flyoutCommits = isLeft ? LeftFlyoutCommits : RightFlyoutCommits;
            var flyoutFilesLabel = isLeft ? LeftFlyoutFilesLabel : RightFlyoutFilesLabel;
            var flyoutFiles = isLeft ? LeftFlyoutFiles : RightFlyoutFiles;

            bar.Visibility = vm.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            branchTb.Text = vm.Branch;
            statusTb.Text = vm.StatusText;

            // Flyout content
            flyoutBranch.Text = vm.Branch;
            flyoutStatus.Text = vm.FullStatusText;
            flyoutCommitsLabel.Text = _loc?.Get("GitStatus_RecentCommits") ?? "Recent Commits";
            flyoutCommits.Text = vm.RecentCommits;
            flyoutFilesLabel.Text = _loc?.Get("GitStatus_ChangedFiles") ?? "Changed Files";
            flyoutFiles.Text = vm.ChangedFiles;
        }

        /// <summary>
        /// Git 상태바 리소스 해제.
        /// </summary>
        private void CleanupGitStatusBars()
        {
            UnsubscribeGitStatusFromExplorer(isLeft: true);
            UnsubscribeGitStatusFromExplorer(isLeft: false);

            if (_leftGitStatusBarVm != null)
            {
                _leftGitStatusBarVm.PropertyChanged -= OnLeftGitStatusBarChanged;
                _leftGitStatusBarVm.Dispose();
                _leftGitStatusBarVm = null;
            }
            if (_rightGitStatusBarVm != null)
            {
                _rightGitStatusBarVm.PropertyChanged -= OnRightGitStatusBarChanged;
                _rightGitStatusBarVm.Dispose();
                _rightGitStatusBarVm = null;
            }
        }

        #endregion
    }
}
