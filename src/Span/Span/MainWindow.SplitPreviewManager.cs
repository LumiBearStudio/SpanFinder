using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    public sealed partial class MainWindow
    {
        #region Active Pane Helpers

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
        /// Split/Preview buttons: hidden in Settings mode
        /// </summary>
        public Visibility IsNotSettingsMode(Models.ViewMode mode)
            => mode != Models.ViewMode.Settings ? Visibility.Visible : Visibility.Collapsed;

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

        #endregion

        #region Focus Tracking

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

        private void LocalizeViewModeTooltips()
        {
            var tip = _loc.Get("ViewModeSwitch");
            ToolTipService.SetToolTip(ViewModeButton, tip);
            ToolTipService.SetToolTip(LeftViewModeButton, tip);
            ToolTipService.SetToolTip(RightViewModeButton, tip);
        }

        #endregion

        #region Pane Preview Toggle

        private void OnPanePreviewToggle(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tag)
            {
                ViewModel.ActivePane = tag == "Right" ? ActivePane.Right : ActivePane.Left;
            }
            TogglePreviewPanel();
        }

        #endregion

        #region Breadcrumb Scroll / Overflow

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
        /// Shows "..." at the left edge when earlier path segments are hidden.
        /// </summary>
        private void OnBreadcrumbScrollerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is ScrollViewer sv)
                UpdateBreadcrumbOverflow(sv);
        }

        /// <summary>
        /// Show/hide the overflow "..." indicator based on scroll position.
        /// When HorizontalOffset > 0, leftmost segments are hidden -> show indicator.
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

        #endregion

        #region Pane Breadcrumb Navigation

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

        #endregion

        #region Split View Toggle

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

            // Immediately update preview with current selection.
            // 새 컬럼이 추가된 직후엔 SelectedChild가 null — 이 경우 기존 미리보기(부모 폴더)를 유지.
            // 사용자가 새 컬럼에서 항목을 선택하면 PropertyChanged로 자동 업데이트됨.
            var selectedChild = lastColumn.SelectedChild;
            if (selectedChild != null)
            {
                var previewPanel = isLeft ? LeftPreviewPanel : RightPreviewPanel;
                previewPanel.UpdatePreview(selectedChild);
            }
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

            // Get metadata from PreviewService (원격 파일은 모델 데이터 사용)
            if (Services.FileSystemRouter.IsRemotePath(fileVm.Path))
            {
                InlinePreviewFileSize.Text = fileVm.Size;
                InlinePreviewDateCreatedRow.Visibility = Visibility.Collapsed;
            }
            else
            {
                var metadata = _inlinePreviewService!.GetBasicMetadata(fileVm.Path);
                InlinePreviewFileSize.Text = metadata.SizeFormatted;
                InlinePreviewDateCreated.Text = metadata.Created.ToString("yyyy-MM-dd HH:mm");
                InlinePreviewDateCreatedRow.Visibility = Visibility.Visible;
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
                // Normal - user selected another file quickly
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

        #endregion
    }
}
