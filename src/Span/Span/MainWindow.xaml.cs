using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;

namespace Span
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        // Type-ahead search
        private string _typeAheadBuffer = string.Empty;
        private DispatcherTimer? _typeAheadTimer;

        // Clipboard
        private readonly List<string> _clipboardPaths = new();
        private bool _isCutOperation = false;

        // Rename 완료 직후 Enter가 파일 실행으로 이어지는 것을 방지
        private bool _justFinishedRename = false;

        private const double ColumnWidth = 220;

        public MainWindow()
        {
            this.InitializeComponent();
            ViewModel = App.Current.Services.GetRequiredService<MainViewModel>();

            // Mica
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

            // TitleBar
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // Auto-scroll on column change
            ViewModel.Explorer.Columns.CollectionChanged += OnColumnsChanged;

            // ★ ItemsControl에서 키보드 이벤트 가로채기 (ScrollViewer에 전달 차단)
            MillerColumnsControl.AddHandler(
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
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            try
            {
                // Clean up background tasks
                if (ViewModel?.Explorer?.Columns != null)
                {
                    foreach (var column in ViewModel.Explorer.Columns.ToList())
                    {
                        if (column is FolderViewModel folderVm)
                        {
                            folderVm.CancelLoading();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during close: {ex.Message}");
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
                ScrollToLastColumn();
            }
        }

        private void ScrollToLastColumn()
        {
            var columns = ViewModel.Explorer.Columns;
            if (columns.Count == 0) return;

            MillerScrollViewer.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    MillerScrollViewer.UpdateLayout();
                    double totalWidth = columns.Count * ColumnWidth;
                    double viewportWidth = MillerScrollViewer.ViewportWidth;
                    double targetScroll = Math.Max(0, totalWidth - viewportWidth);
                    MillerScrollViewer.ChangeView(targetScroll, null, null, false);
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
                        HandleDelete();
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

            var columns = ViewModel.Explorer.Columns;
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
            var columns = ViewModel.Explorer.Columns;
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
            var columns = ViewModel.Explorer.Columns;
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

            var columns = ViewModel.Explorer.Columns;
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
        //  P1: Clipboard (Ctrl+C/X/V)
        // =================================================================

        private void HandleCopy()
        {
            var selected = GetCurrentSelected();
            if (selected == null) return;

            _clipboardPaths.Clear();
            _clipboardPaths.Add(selected.Path);
            _isCutOperation = false;

            var dataPackage = new DataPackage();
            dataPackage.SetText(selected.Path);
            Clipboard.SetContent(dataPackage);

            System.Diagnostics.Debug.WriteLine($"[Clipboard] Copied: {selected.Path}");
        }

        private void HandleCut()
        {
            var selected = GetCurrentSelected();
            if (selected == null) return;

            _clipboardPaths.Clear();
            _clipboardPaths.Add(selected.Path);
            _isCutOperation = true;

            var dataPackage = new DataPackage();
            dataPackage.SetText(selected.Path);
            Clipboard.SetContent(dataPackage);

            System.Diagnostics.Debug.WriteLine($"[Clipboard] Cut: {selected.Path}");
        }

        private async void HandlePaste()
        {
            if (_clipboardPaths.Count == 0) return;

            var columns = ViewModel.Explorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var targetFolder = columns[activeIndex];
            string destDir = targetFolder.Path;

            foreach (var srcPath in _clipboardPaths)
            {
                try
                {
                    string fileName = System.IO.Path.GetFileName(srcPath);
                    string destPath = System.IO.Path.Combine(destDir, fileName);

                    int copy = 1;
                    while (System.IO.File.Exists(destPath) || System.IO.Directory.Exists(destPath))
                    {
                        string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(srcPath);
                        string ext = System.IO.Path.GetExtension(srcPath);
                        destPath = System.IO.Path.Combine(destDir, $"{nameNoExt} ({copy}){ext}");
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
                    System.Diagnostics.Debug.WriteLine($"Paste error: {ex.Message}");
                }
            }

            if (_isCutOperation) _clipboardPaths.Clear();

            await columns[activeIndex].ReloadAsync();
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
            var columns = ViewModel.Explorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var currentFolder = columns[activeIndex];
            string baseName = "새 폴더";
            string newPath = System.IO.Path.Combine(currentFolder.Path, baseName);

            int count = 1;
            while (System.IO.Directory.Exists(newPath))
            {
                newPath = System.IO.Path.Combine(currentFolder.Path, $"{baseName} ({count})");
                count++;
            }

            try
            {
                System.IO.Directory.CreateDirectory(newPath);
                await currentFolder.ReloadAsync();

                var newFolder = currentFolder.Children.FirstOrDefault(c =>
                    c.Path.Equals(newPath, StringComparison.OrdinalIgnoreCase));
                if (newFolder != null)
                {
                    currentFolder.SelectedChild = newFolder;
                    // 새 폴더 생성 후 바로 인라인 이름 변경 모드
                    newFolder.BeginRename();
                    await System.Threading.Tasks.Task.Delay(100);
                    FocusRenameTextBox(activeIndex);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"New folder error: {ex.Message}");
            }
        }

        // =================================================================
        //  P1: Refresh (F5)
        // =================================================================

        private async void HandleRefresh()
        {
            var columns = ViewModel.Explorer.Columns;
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
            var selected = GetCurrentSelected();
            if (selected == null) return;

            selected.BeginRename();

            // TextBox에 포커스
            var columns = ViewModel.Explorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
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

            var columns = ViewModel.Explorer.Columns;
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

            // 포커스 잃으면 커밋
            vm.CommitRename();
        }

        /// <summary>
        /// 현재 선택된 항목의 ListViewItem 컨테이너에 포커스를 복원.
        /// 이름 변경 후 화살표 키가 그 자리에서 동작하도록.
        /// </summary>
        private void FocusSelectedItem()
        {
            var columns = ViewModel.Explorer.Columns;
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
                var container = listView.ContainerFromIndex(idx) as UIElement;
                container?.Focus(FocusState.Keyboard);
            });
        }

        // =================================================================
        //  P2: Delete (Delete key)
        // =================================================================

        private async void HandleDelete()
        {
            var selected = GetCurrentSelected();
            if (selected == null) return;

            // ★ 다이얼로그 표시 전에 activeIndex를 저장 (다이얼로그가 포커스를 가져감)
            var columns = ViewModel.Explorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var dialog = new ContentDialog
            {
                Title = "삭제 확인",
                Content = $"'{selected.Name}'을(를) 삭제하시겠습니까?",
                PrimaryButtonText = "삭제",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            try
            {
                if (selected is FolderViewModel)
                    System.IO.Directory.Delete(selected.Path, true);
                else
                    System.IO.File.Delete(selected.Path);

                // ★ 저장해둔 activeIndex로 해당 컬럼 새로고침
                await columns[activeIndex].ReloadAsync();

                // 삭제된 항목 이후의 컬럼들 제거
                for (int i = columns.Count - 1; i > activeIndex; i--)
                {
                    columns.RemoveAt(i);
                }

                // 포커스 복원
                FocusColumnAsync(activeIndex);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Delete error: {ex.Message}");
            }
        }



        // =================================================================
        //  Search Box
        // =================================================================

        private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                MillerColumnsControl.Focus(FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Enter)
            {
                string query = SearchBox.Text.Trim();
                if (string.IsNullOrEmpty(query)) return;

                var columns = ViewModel.Explorer.Columns;
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
                ViewModel.Explorer.SetActiveColumn(folderVm);
            }
        }


        private FileSystemViewModel? GetCurrentSelected()
        {
            var columns = ViewModel.Explorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return null;
            return columns[activeIndex].SelectedChild;
        }

        private void EnsureColumnVisible(int columnIndex)
        {
            double columnLeft = columnIndex * ColumnWidth;
            double columnRight = columnLeft + ColumnWidth;
            double viewportLeft = MillerScrollViewer.HorizontalOffset;
            double viewportRight = viewportLeft + MillerScrollViewer.ViewportWidth;

            if (columnLeft < viewportLeft)
                MillerScrollViewer.ChangeView(columnLeft, null, null, true);
            else if (columnRight > viewportRight)
                MillerScrollViewer.ChangeView(columnRight - MillerScrollViewer.ViewportWidth, null, null, true);
        }

        private int GetActiveColumnIndex()
        {
            var focused = FocusManager.GetFocusedElement(this.Content.XamlRoot) as DependencyObject;
            if (focused == null) return -1;

            for (int i = 0; i < ViewModel.Explorer.Columns.Count; i++)
            {
                var listView = GetListViewForColumn(i);
                if (listView != null && IsDescendant(listView, focused))
                    return i;
            }
            return -1;
        }

        private async void FocusColumnAsync(int columnIndex)
        {
            await System.Threading.Tasks.Task.Delay(50);

            var listView = GetListViewForColumn(columnIndex);
            if (listView == null) return;

            var columns = ViewModel.Explorer.Columns;
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
            if (MillerColumnsControl == null) return null;
            var container = MillerColumnsControl.ContainerFromIndex(columnIndex) as ContentPresenter;
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
                ViewModel.Explorer.NavigateToPath(fullPath);
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
        /// 편집 모드 표시: 브레드크럼 숨기고 TextBox 표시.
        /// </summary>
        private void ShowAddressBarEditMode()
        {
            BreadcrumbScroller.Visibility = Visibility.Collapsed;
            AddressBarTextBox.Visibility = Visibility.Visible;
            AddressBarTextBox.Text = ViewModel.Explorer.CurrentPath;
            AddressBarTextBox.Focus(FocusState.Keyboard);
            AddressBarTextBox.SelectAll();
        }

        /// <summary>
        /// 브레드크럼 모드로 복귀: TextBox 숨기고 브레드크럼 표시.
        /// </summary>
        private void ShowAddressBarBreadcrumbMode()
        {
            AddressBarTextBox.Visibility = Visibility.Collapsed;
            BreadcrumbScroller.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 주소 표시줄 TextBox에서 Enter/Esc 처리.
        /// </summary>
        private void OnAddressBarKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var path = AddressBarTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(path))
                {
                    ViewModel.Explorer.NavigateToPath(path);
                }
                ShowAddressBarBreadcrumbMode();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                ShowAddressBarBreadcrumbMode();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 주소 표시줄 TextBox 포커스 잃으면 브레드크럼 모드로 복귀.
        /// </summary>
        private void OnAddressBarLostFocus(object sender, RoutedEventArgs e)
        {
            ShowAddressBarBreadcrumbMode();
        }

        /// <summary>
        /// 경로 복사 버튼 클릭 → 현재 경로를 클립보드에 복사.
        /// </summary>
        private void OnCopyPathClick(object sender, RoutedEventArgs e)
        {
            var path = ViewModel.Explorer.CurrentPath;
            if (!string.IsNullOrEmpty(path))
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(path);
                Clipboard.SetContent(dataPackage);
            }
        }


    }
}
