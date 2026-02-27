using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Span.Models;
using Span.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace Span
{
    /// <summary>
    /// MainWindow의 탐색 관리 부분 클래스.
    /// Miller Column 스크롤, 컬럼 포커스 관리, 주소 표시줄(브레드크럼) 이벤트,
    /// 뒤로/앞으로 탐색(히스토리 드롭다운 포함), 좌측/우측 패널 포커스 전환,
    /// TitleBar 영역 업데이트 등 탐색 관련 기능을 담당한다.
    /// </summary>
    public sealed partial class MainWindow
    {
        #region Column Scrolling

        /// <summary>
        /// 마지막 컬럼이 보이도록 Miller Column ScrollViewer를 스크롤한다.
        /// DispatcherQueue Low 우선순위로 지연 실행하여 레이아웃 계산 완료 후 스크롤한다.
        /// </summary>
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

        #endregion

        #region Column Focus & Visibility

        /// <summary>
        /// 활성 컬럼의 인덱스를 반환한다. 포커스 기반으로 활성 컬럼을 결정한다.
        /// </summary>
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

        /// <summary>
        /// Miller Column에서 지정된 인덱스의 컬럼에 포커스를 이동한다.
        /// ListView 컨테이너 생성을 대기하며 최대 5회 재시도한다.
        /// </summary>
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

        /// <summary>
        /// 지정된 컬럼이 ScrollViewer에서 보이도록 스크롤을 조정한다.
        /// </summary>
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

        #endregion

        #region Breadcrumb Address Bar Handlers

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
        /// Navigate to parent folder (Up button clicked).
        /// </summary>
        private void OnNavigateUpClick(object sender, RoutedEventArgs e)
        {
            ViewModel?.ActiveExplorer?.NavigateUp();
            Helpers.DebugLogger.Log("[MainWindow] Up button clicked - navigating to parent folder");
        }

        #endregion

        #region Back/Forward Navigation

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

        #endregion

        #region Back/Forward History Dropdown

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

        #endregion

        #region Address Bar Control Events

        /// <summary>
        /// 주소 표시줄 편집 모드 표시 (Ctrl+L, Alt+D에서 호출).
        /// </summary>
        private void ShowAddressBarEditMode()
        {
            GetActiveAddressBar().EnterEditMode();
        }

        /// <summary>
        /// AddressBarControl breadcrumb 세그먼트 클릭 → 해당 경로로 네비게이션.
        /// </summary>
        private void OnAddressBarBreadcrumbClicked(object sender, Controls.BreadcrumbClickEventArgs e)
        {
            if (e.FullPath == "::home::")
            {
                ViewModel.SwitchViewMode(ViewMode.Home);
                return;
            }

            // Determine which explorer to navigate
            var explorer = ResolveExplorerForAddressBar(sender);
            _ = explorer.NavigateToPath(e.FullPath);
        }

        /// <summary>
        /// AddressBarControl chevron 클릭 → 서브폴더 드롭다운 표시.
        /// </summary>
        private void OnAddressBarChevronClicked(object sender, Controls.BreadcrumbClickEventArgs e)
        {
            var explorer = ResolveExplorerForAddressBar(sender);
            ShowBreadcrumbChevronFlyout(e.FullPath, e.SourceButton as Button, explorer);
        }

        /// <summary>
        /// AddressBarControl 경로 입력 완료 → 네비게이션.
        /// </summary>
        private void OnAddressBarPathNavigated(object sender, string path)
        {
            var explorer = ResolveExplorerForAddressBar(sender);

            if (System.IO.Directory.Exists(path))
            {
                _ = explorer.NavigateToPath(path);
            }
            else if (System.IO.File.Exists(path))
            {
                var parent = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                    _ = explorer.NavigateToPath(parent);
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
                ViewModel.ShowToast(_loc.Get("Toast_PathCopied"), 2000);
            }
        }

        /// <summary>
        /// 현재 활성 AddressBarControl 반환 (단일/좌/우).
        /// </summary>
        private Controls.AddressBarControl GetActiveAddressBar()
        {
            if (!ViewModel.IsSplitViewEnabled) return MainAddressBar;
            return ViewModel.ActivePane == ActivePane.Left ? LeftAddressBar : RightAddressBar;
        }

        /// <summary>
        /// AddressBarControl sender에서 해당하는 ExplorerViewModel 결정.
        /// </summary>
        private ExplorerViewModel ResolveExplorerForAddressBar(object sender)
        {
            if (ReferenceEquals(sender, RightAddressBar))
            {
                ViewModel.ActivePane = ActivePane.Right;
                return ViewModel.RightExplorer;
            }
            if (ReferenceEquals(sender, LeftAddressBar))
            {
                ViewModel.ActivePane = ActivePane.Left;
                return ViewModel.LeftExplorer;
            }
            // MainAddressBar → use ActiveExplorer
            return ViewModel.ActiveExplorer;
        }

        /// <summary>
        /// Chevron flyout 표시 공통 로직.
        /// </summary>
        private void ShowBreadcrumbChevronFlyout(string fullPath, Button? btn, ExplorerViewModel explorer)
        {
            if (btn == null) return;

            try
            {
                if (!System.IO.Directory.Exists(fullPath)) return;

                string[] dirs;
                try { dirs = System.IO.Directory.GetDirectories(fullPath); }
                catch (UnauthorizedAccessException) { return; }

                if (dirs.Length == 0) return;
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);

                string? currentChildPath = null;
                if (!string.IsNullOrEmpty(explorer.CurrentPath) &&
                    explorer.CurrentPath.StartsWith(fullPath, StringComparison.OrdinalIgnoreCase) &&
                    explorer.CurrentPath.Length > fullPath.TrimEnd('\\').Length + 1)
                {
                    string remainder = explorer.CurrentPath.Substring(fullPath.TrimEnd('\\').Length + 1);
                    string childName = remainder.Split('\\')[0];
                    currentChildPath = System.IO.Path.Combine(fullPath, childName);
                }

                var flyout = new MenuFlyout();
                foreach (var dir in dirs)
                {
                    var item = new MenuFlyoutItem { Text = System.IO.Path.GetFileName(dir) };
                    string dirPath = dir;

                    if (currentChildPath != null &&
                        dir.Equals(currentChildPath, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Icon = new FontIcon { Glyph = "\uE73E" };
                    }

                    item.Click += (s, args) => _ = explorer.NavigateToPath(dirPath);
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
    }
}
