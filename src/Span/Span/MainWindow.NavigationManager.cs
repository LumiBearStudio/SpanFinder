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
    public sealed partial class MainWindow
    {
        #region Column Scrolling

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

        #region Address Bar Edit Mode

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

        #endregion
    }
}
