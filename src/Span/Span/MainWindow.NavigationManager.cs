using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Span.Models;
using Span.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        #region Shell Protocol Mappings

        /// <summary>
        /// shell: 프로토콜을 Environment.SpecialFolder로 매핑.
        /// null 값은 별도 처리 필요 (예: Downloads).
        /// </summary>
        private static readonly Dictionary<string, Environment.SpecialFolder?> _shellFolderMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["shell:desktop"] = Environment.SpecialFolder.Desktop,
            ["shell:documents"] = Environment.SpecialFolder.MyDocuments,
            ["shell:downloads"] = null, // SpecialFolder에 없음 — ResolveShellPath에서 별도 처리
            ["shell:pictures"] = Environment.SpecialFolder.MyPictures,
            ["shell:music"] = Environment.SpecialFolder.MyMusic,
            ["shell:videos"] = Environment.SpecialFolder.MyVideos,
            ["shell:personal"] = Environment.SpecialFolder.Personal,
            ["shell:favorites"] = Environment.SpecialFolder.Favorites,
            ["shell:recent"] = Environment.SpecialFolder.Recent,
            ["shell:startup"] = Environment.SpecialFolder.Startup,
            ["shell:sendto"] = Environment.SpecialFolder.SendTo,
            ["shell:appdata"] = Environment.SpecialFolder.ApplicationData,
            ["shell:localappdata"] = Environment.SpecialFolder.LocalApplicationData,
            ["shell:profile"] = Environment.SpecialFolder.UserProfile,
            ["shell:programfiles"] = Environment.SpecialFolder.ProgramFiles,
            ["shell:programfilesx86"] = Environment.SpecialFolder.ProgramFilesX86,
            ["shell:system"] = Environment.SpecialFolder.System,
            ["shell:windows"] = Environment.SpecialFolder.Windows,
            ["shell:fonts"] = Environment.SpecialFolder.Fonts,
            ["shell:templates"] = Environment.SpecialFolder.Templates,
            ["shell:commonstartmenu"] = Environment.SpecialFolder.CommonStartMenu,
            ["shell:commonprograms"] = Environment.SpecialFolder.CommonPrograms,
            ["shell:commonstartup"] = Environment.SpecialFolder.CommonStartup,
            ["shell:commondesktop"] = Environment.SpecialFolder.CommonDesktopDirectory,
            ["shell:programs"] = Environment.SpecialFolder.Programs,
            ["shell:startmenu"] = Environment.SpecialFolder.StartMenu,
        };

        /// <summary>
        /// Span 전용 뷰(홈/휴지통)의 로컬라이즈 이름 → ViewMode 매핑.
        /// SHParseDisplayName이 처리할 수 없는 Span 내부 뷰만 포함.
        /// </summary>
        private static readonly Dictionary<string, Models.ViewMode> _localizedViewNameMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Home
                ["home"] = Models.ViewMode.Home,
                ["홈"] = Models.ViewMode.Home,
                ["ホーム"] = Models.ViewMode.Home,
                ["主页"] = Models.ViewMode.Home,
                ["首頁"] = Models.ViewMode.Home,
                ["start"] = Models.ViewMode.Home,
                ["inicio"] = Models.ViewMode.Home,
                ["accueil"] = Models.ViewMode.Home,
                ["início"] = Models.ViewMode.Home,
                // Recycle Bin
                ["recycle bin"] = Models.ViewMode.RecycleBin,
                ["recyclebin"] = Models.ViewMode.RecycleBin,
                ["휴지통"] = Models.ViewMode.RecycleBin,
                ["ごみ箱"] = Models.ViewMode.RecycleBin,
                ["回收站"] = Models.ViewMode.RecycleBin,
                ["資源回收筒"] = Models.ViewMode.RecycleBin,
                ["papierkorb"] = Models.ViewMode.RecycleBin,
                ["papelera"] = Models.ViewMode.RecycleBin,
                ["corbeille"] = Models.ViewMode.RecycleBin,
                ["lixeira"] = Models.ViewMode.RecycleBin,
            };

        /// <summary>
        /// 가상 폴더 — SPAN에서 직접 탐색 불가, explorer.exe에 위임.
        /// RecycleBinFolder는 자체 처리되므로 여기에 포함하지 않음.
        /// </summary>
        private static readonly HashSet<string> _shellVirtualFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "shell:ControlPanelFolder",
            "shell:PrintersFolder",
            "shell:NetworkPlacesFolder",
            "shell:ThisPCFolder",
        };

        /// <summary>
        /// shell: 경로를 실제 파일 시스템 경로로 변환.
        /// 매핑되지 않는 경로는 null 반환.
        /// </summary>
        private static string? ResolveShellPath(string shellPath)
        {
            if (_shellFolderMap.TryGetValue(shellPath, out var specialFolder))
            {
                if (specialFolder.HasValue)
                    return Environment.GetFolderPath(specialFolder.Value);

                // Downloads 등 SpecialFolder에 없는 경우 특수 처리
                if (shellPath.Equals("shell:downloads", StringComparison.OrdinalIgnoreCase))
                    return System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
            return null;
        }

        // ── 로컬라이즈된 폴더 이름 해석 ──

        private enum LocalizedPathAction { NavigateFileSystem, SwitchViewMode, OpenExternal, Failed }

        private struct LocalizedPathResult
        {
            public LocalizedPathAction Action;
            public string? ResolvedPath;
            public Models.ViewMode ViewMode;
        }

        /// <summary>
        /// 로컬라이즈된 폴더 이름(예: "다운로드", "ダウンロード")을 실제 경로 또는 뷰 모드로 해석.
        /// 1단계: Span 내부 뷰 정적 매핑 → 2단계: SHParseDisplayName (Shell API).
        /// </summary>
        private async Task<LocalizedPathResult> ResolveLocalizedPathAsync(string input)
        {
            // 1단계: Span 전용 뷰 정적 매핑
            if (_localizedViewNameMap.TryGetValue(input.Trim(), out var viewMode))
                return new LocalizedPathResult { Action = LocalizedPathAction.SwitchViewMode, ViewMode = viewMode };

            // 2단계: SHParseDisplayName (백그라운드 스레드)
            return await Task.Run(() =>
            {
                IntPtr pidl = IntPtr.Zero;
                try
                {
                    int hr = Helpers.NativeMethods.SHParseDisplayName(input, IntPtr.Zero, out pidl, 0, out _);
                    if (hr != 0 || pidl == IntPtr.Zero)
                        return new LocalizedPathResult { Action = LocalizedPathAction.Failed };

                    var sb = new System.Text.StringBuilder(260);
                    if (Helpers.NativeMethods.SHGetPathFromIDListW(pidl, sb) && sb.Length > 0)
                        return new LocalizedPathResult { Action = LocalizedPathAction.NavigateFileSystem, ResolvedPath = sb.ToString() };

                    // 가상 폴더 (제어판 등) — explorer.exe에 위임
                    return new LocalizedPathResult { Action = LocalizedPathAction.OpenExternal };
                }
                catch
                {
                    return new LocalizedPathResult { Action = LocalizedPathAction.Failed };
                }
                finally
                {
                    if (pidl != IntPtr.Zero)
                        Helpers.NativeMethods.CoTaskMemFree(pidl);
                }
            });
        }

        #endregion

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
        /// 현재 키보드 포커스가 위치한 Miller Column의 인덱스를 반환한다.
        /// FocusManager에서 포커스된 요소를 가져온 뒤, 각 컬럼의 ListView에 대해
        /// IsDescendant()로 포함 여부를 검사한다.
        /// 포커스가 어떤 컬럼에도 없으면 -1을 반환한다.
        /// HandlePaste, HandleSelectAll 등에서 대상 컬럼을 결정하는 핵심 메서드.
        /// </summary>
        private int GetActiveColumnIndex()
        {
            if (_isClosed || Content?.XamlRoot == null) return -1;
            var focused = FocusManager.GetFocusedElement(this.Content.XamlRoot) as DependencyObject;
            Helpers.DebugLogger.Log($"[GetActiveColumnIndex] focused={focused?.GetType().Name ?? "null"} ({(focused as FrameworkElement)?.Name ?? ""})");
            if (focused == null) return -1;

            var explorer = ViewModel.ActiveExplorer;
            if (explorer == null) return -1;

            for (int i = 0; i < explorer.Columns.Count; i++)
            {
                var listView = GetListViewForColumn(i);
                if (listView != null && IsDescendant(listView, focused))
                {
                    Helpers.DebugLogger.Log($"[GetActiveColumnIndex] → {i} (focus inside column ListView)");
                    return i;
                }
            }
            Helpers.DebugLogger.Log($"[GetActiveColumnIndex] → -1 (focus not in any column)");
            return -1;
        }

        /// <summary>
        /// 포커스가 없을 때 작업 대상 컬럼 인덱스를 결정한다.
        /// 우선 GetActiveColumnIndex()를 시도하고, 실패하면(-1) SelectedChild가 있는
        /// 가장 오른쪽 컬럼을 반환한다. 최종 fallback은 마지막 컬럼.
        /// 툴바 버튼 클릭 등 포커스가 컬럼 밖에 있을 때 사용된다.
        /// </summary>
        private int GetCurrentColumnIndex()
        {
            var explorer = ViewModel.ActiveExplorer;
            if (explorer == null) return -1;
            var columns = explorer.Columns;
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
        /// <param name="columnIndex">포커스할 컬럼 인덱스</param>
        /// <param name="autoSelect">true이면 SelectedChild가 없을 때 첫 항목을 자동 선택.
        /// 패인 전환(FocusActivePane) 시에는 false로 호출하여 의도하지 않은 컬럼 생성을 방지.</param>
        private async void FocusColumnAsync(int columnIndex, bool autoSelect = true)
        {
            try
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

                // 첫 항목 자동 선택 — Finder처럼 선택 = 네비게이션
                // (폴더면 다음 컬럼이 자동 생성됨)
                // autoSelect=false: 패인 전환 시 자동 선택을 억제하여 컬럼 연쇄 생성 방지
                if (autoSelect && column.SelectedChild == null && column.Children.Count > 0)
                {
                    column.SelectedChild = column.Children[0];
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
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Navigation] FocusColumnAsync error: {ex.Message}");
            }
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
            if (ViewModel.CurrentViewMode == Models.ViewMode.RecycleBin) return;
            try
            {
                await ViewModel.GoBackAsync();
                FocusLastColumnAfterNavigation();
                Helpers.DebugLogger.Log("[MainWindow] Back button clicked");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Navigation] OnGoBackClick error: {ex.Message}");
            }
        }

        /// <summary>
        /// Navigate forward in history (Forward button clicked - single mode).
        /// </summary>
        private async void OnGoForwardClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentViewMode == Models.ViewMode.RecycleBin) return;
            try
            {
                await ViewModel.GoForwardAsync();
                FocusLastColumnAfterNavigation();
                Helpers.DebugLogger.Log("[MainWindow] Forward button clicked");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Navigation] OnGoForwardClick error: {ex.Message}");
            }
        }

        /// <summary>
        /// Navigate back in history (Back button clicked - split pane mode).
        /// </summary>
        private async void OnPaneGoBackClick(object sender, RoutedEventArgs e)
        {
            try
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
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Navigation] OnPaneGoBackClick error: {ex.Message}");
            }
        }

        /// <summary>
        /// Navigate forward in history (Forward button clicked - split pane mode).
        /// </summary>
        private async void OnPaneGoForwardClick(object sender, RoutedEventArgs e)
        {
            try
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
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Navigation] OnPaneGoForwardClick error: {ex.Message}");
            }
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
                try
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
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[FocusLastColumn] Error: {ex.Message}");
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
            Helpers.DebugLogger.Log($"[OnAddressBarBreadcrumbClicked] path='{e.FullPath}', sender={sender.GetType().Name}, isRight={ReferenceEquals(sender, RightAddressBar)}, isLeft={ReferenceEquals(sender, LeftAddressBar)}");

            if (e.FullPath == "::home::")
            {
                ViewModel.SwitchViewMode(ViewMode.Home);
                return;
            }

            // Determine which explorer to navigate
            var explorer = ResolveExplorerForAddressBar(sender);
            Helpers.DebugLogger.Log($"[OnAddressBarBreadcrumbClicked] explorer.CurrentPath='{explorer.CurrentPath}', EnableAutoNav={explorer.EnableAutoNavigation}, Columns={explorer.Columns.Count}");
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
        private async void OnAddressBarPathNavigated(object sender, string path)
        {
            // shell:RecycleBinFolder 입력 시 휴지통 뷰 전환
            if (string.Equals(path, "shell:RecycleBinFolder", System.StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.SwitchViewMode(Models.ViewMode.RecycleBin);
                UpdateViewModeVisibility();
                return;
            }

            // shell: 프로토콜 매핑 — 실제 폴더 경로로 변환 또는 explorer.exe 위임
            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                // 가상 폴더 → explorer.exe 위임
                if (_shellVirtualFolders.Contains(path))
                {
                    try { Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); } catch { }
                    return;
                }

                // 실제 폴더 매핑 → 즐겨찾기 클릭처럼 해당 폴더를 루트로 열기
                var resolvedPath = ResolveShellPath(path);
                if (resolvedPath != null && System.IO.Directory.Exists(resolvedPath))
                {
                    if (ViewModel.CurrentViewMode == Models.ViewMode.Home
                        || ViewModel.CurrentViewMode == Models.ViewMode.RecycleBin)
                    {
                        ViewModel.SwitchViewMode(ViewModel.ResolveViewModeFromHome());
                        UpdateViewModeVisibility();
                    }
                    var folder = new Models.FolderItem
                    {
                        Name = System.IO.Path.GetFileName(resolvedPath),
                        Path = resolvedPath
                    };
                    _ = ViewModel.ActiveExplorer?.NavigateTo(folder);
                    return;
                }
                else
                {
                    // 알 수 없는 shell: 경로 → explorer.exe에 위임 (crash 방지)
                    try { Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); } catch { }
                    return;
                }
            }

            // ms-settings: 프로토콜 → Windows 설정 앱 열기
            if (path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            {
                try { await Windows.System.Launcher.LaunchUriAsync(new Uri(path)); } catch { }
                return;
            }

            // control panel / control 텍스트 입력 → 제어판 열기
            if (path.Equals("control panel", StringComparison.OrdinalIgnoreCase)
                || path.Equals("control", StringComparison.OrdinalIgnoreCase))
            {
                try { Process.Start(new ProcessStartInfo("control.exe") { UseShellExecute = true }); } catch { }
                return;
            }

            // ── 로컬라이즈된 폴더 이름 해석 (예: "다운로드", "ダウンロード", "휴지통") ──
            if (!path.Contains('\\') && !path.Contains('/') && !path.Contains(':'))
            {
                try
                {
                    var result = await ResolveLocalizedPathAsync(path);
                    if (_isClosed) return;

                    switch (result.Action)
                    {
                        case LocalizedPathAction.SwitchViewMode:
                            ViewModel.SwitchViewMode(result.ViewMode);
                            UpdateViewModeVisibility();
                            return;

                        case LocalizedPathAction.NavigateFileSystem:
                            if (ViewModel.CurrentViewMode == Models.ViewMode.Home
                                || ViewModel.CurrentViewMode == Models.ViewMode.RecycleBin)
                            {
                                ViewModel.SwitchViewMode(ViewModel.ResolveViewModeFromHome());
                                UpdateViewModeVisibility();
                            }
                            var navExplorer = ResolveExplorerForAddressBar(sender);
                            if (navExplorer != null)
                                _ = navExplorer.NavigateToPath(result.ResolvedPath!);
                            return;

                        case LocalizedPathAction.OpenExternal:
                            try
                            {
                                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
                            }
                            catch { }
                            return;

                        case LocalizedPathAction.Failed:
                            break; // fall-through to existing logic
                    }
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[Navigation] ResolveLocalizedPath error: {ex.Message}");
                }
            }

            var explorer = ResolveExplorerForAddressBar(sender);

            // Home/RecycleBin 모드에서 경로 입력 시 MillerColumns로 전환
            if (ViewModel.CurrentViewMode == Models.ViewMode.Home
                || ViewModel.CurrentViewMode == Models.ViewMode.RecycleBin)
            {
                ViewModel.SwitchViewMode(Models.ViewMode.MillerColumns);
                UpdateViewModeVisibility();
            }

            // archive:// 경로 직접 입력 지원
            if (Helpers.ArchivePathHelper.IsArchivePath(path))
            {
                _ = explorer.NavigateToPath(path);
                return;
            }

            if (System.IO.Directory.Exists(path))
            {
                _ = explorer.NavigateToPath(path);
            }
            else if (System.IO.File.Exists(path))
            {
                // 압축 파일이면 아카이브로 진입
                if (Helpers.ArchivePathHelper.IsArchiveFile(path))
                {
                    var archivePath = Helpers.ArchivePathHelper.Combine(path, "");
                    _ = explorer.NavigateToPath(archivePath);
                }
                else
                {
                    var parent = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(parent))
                        _ = explorer.NavigateToPath(parent);
                }
            }
            else
            {
                // 경로가 존재하지 않으면 아카이브 내부 경로인지 확인
                // (예: D:\folder\archive.zip\internal\path — archive:// 프리픽스 없이 입력)
                var archiveUri = Helpers.ArchivePathHelper.TryBuildArchiveUri(path);
                if (archiveUri != null)
                    _ = explorer.NavigateToPath(archiveUri);
                else
                    ViewModel.ShowToast(string.Format(_loc.Get("Op_PathNotFound"), path), isError: true);
            }
        }

        /// <summary>
        /// 경로 복사 버튼 클릭 → 현재 경로를 클립보드에 복사.
        /// </summary>
        private void OnCopyPathClick(object sender, RoutedEventArgs e)
        {
            var path = ViewModel.ActiveExplorer?.CurrentPath;
            if (!string.IsNullOrEmpty(path))
            {
                // archive:// 프리픽스 제거하여 Windows 탐색기 스타일 경로로 복사
                var copyPath = Helpers.ArchivePathHelper.IsArchivePath(path)
                    ? path.Substring(Helpers.ArchivePathHelper.Prefix.Length)
                    : path;
                var dataPackage = new DataPackage();
                dataPackage.SetText(copyPath);
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
