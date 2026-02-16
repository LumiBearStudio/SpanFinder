using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.ViewModels;
using Span.Services.FileOperations;
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

        // Sort state
        private string _currentSortField = "Name"; // Name, Date, Size, Type
        private bool _currentSortAscending = true;

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

            // Focus management on ViewMode change
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Set ViewModel for Details and Icon views
            DetailsView.ViewModel = ViewModel.Explorer;
            IconView.ViewModel = ViewModel.Explorer;

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
                Helpers.DebugLogger.Log("[MainWindow.OnClosed] Starting cleanup...");

                // STEP 0: Block all queued DispatcherQueue callbacks and async continuations
                _isClosed = true;

                // STEP 1: Suppress ViewModel notifications FIRST (prevents PropertyChanged
                // from reaching UI during teardown — the primary crash cause).
                ViewModel?.Explorer?.Cleanup();  // Sets _isCleaningUp, clears Columns silently

                // STEP 2: Unsubscribe MainWindow event handlers BEFORE ViewModel.Cleanup()
                // so collection Clear() notifications don't reach MainWindow handlers.
                if (ViewModel?.Explorer != null)
                {
                    ViewModel.Explorer.Columns.CollectionChanged -= OnColumnsChanged;
                }
                if (ViewModel != null)
                {
                    ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                }

                // STEP 3: Disconnect view bindings BEFORE ViewModel.Cleanup()
                // so Favorites.Clear() / RecentFolders.Clear() don't reach disposed UI.
                try { DetailsView?.Cleanup(); }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[MainWindow.OnClosed] DetailsView cleanup error: {ex.Message}");
                }
                try { IconView?.Cleanup(); }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[MainWindow.OnClosed] IconView cleanup error: {ex.Message}");
                }
                try { HomeView?.Cleanup(); }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[MainWindow.OnClosed] HomeView cleanup error: {ex.Message}");
                }

                // Disconnect sidebar bindings
                try { FavoritesItemsControl.ItemsSource = null; }
                catch { /* ignore */ }

                // STEP 4: NOW safe to clear collections — UI bindings disconnected
                ViewModel?.Cleanup();            // Save state, cancel ops, clear collections

                // STEP 4: Stop timer and remove keyboard handlers
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

                Helpers.DebugLogger.Log("[MainWindow.OnClosed] Cleanup complete");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow.OnClosed] Error during close: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MainWindow.OnClosed] Stack trace: {ex.StackTrace}");
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

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentViewMode))
            {
                FocusActiveView();
            }
        }

        private void FocusActiveView()
        {
            // Use DispatcherQueue for proper timing (after visibility changes take effect)
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed) return;
                switch (ViewModel.CurrentViewMode)
                {
                    case Models.ViewMode.MillerColumns:
                        // Focus the active Miller column
                        var columns = ViewModel.Explorer.Columns;
                        if (columns.Count > 0)
                        {
                            int activeIndex = GetActiveColumnIndex();
                            if (activeIndex < 0) activeIndex = columns.Count - 1;
                            FocusColumnAsync(activeIndex);
                        }
                        Helpers.DebugLogger.Log("[MainWindow] Focus: MillerColumns");
                        break;

                    case Models.ViewMode.Details:
                        // Focus the Details ListView
                        DetailsView?.FocusListView();
                        Helpers.DebugLogger.Log("[MainWindow] Focus: Details");
                        break;

                    case Models.ViewMode.IconSmall:
                    case Models.ViewMode.IconMedium:
                    case Models.ViewMode.IconLarge:
                    case Models.ViewMode.IconExtraLarge:
                        // Focus the Icon GridView
                        IconView?.FocusGridView();
                        Helpers.DebugLogger.Log($"[MainWindow] Focus: Icon ({ViewModel.CurrentViewMode})");
                        break;

                    case Models.ViewMode.Home:
                        // Home view doesn't need special focus management
                        Helpers.DebugLogger.Log("[MainWindow] Focus: Home");
                        break;
                }
            });
        }

        private void ScrollToLastColumn()
        {
            var columns = ViewModel.Explorer.Columns;
            if (columns.Count == 0) return;

            MillerScrollViewer.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    if (_isClosed) return;
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
                var flyout = new MenuFlyout();
                var removeItem = new MenuFlyoutItem
                {
                    Text = "즐겨찾기에서 제거",
                    Icon = new FontIcon { Glyph = "\uE74D" }
                };
                removeItem.Click += (s, args) =>
                {
                    ViewModel.RemoveFromFavorites(favorite.Path);
                };
                flyout.Items.Add(removeItem);
                flyout.ShowAt(grid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(grid)
                });
            }
        }

        private void OnFolderRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.DataContext is FolderViewModel folder)
            {
                ShowFavoriteFlyout(grid, folder.Path, e.GetPosition(grid));
            }
        }

        private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            // Only allow dragging folders
            var folder = e.Items.OfType<FolderViewModel>().FirstOrDefault();
            if (folder != null)
            {
                e.Data.SetText(folder.Path);
                e.Data.RequestedOperation = DataPackageOperation.Link;
            }
            else
            {
                e.Cancel = true;
            }
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

        private void ShowFavoriteFlyout(FrameworkElement target, string folderPath, Windows.Foundation.Point position)
        {
            var flyout = new MenuFlyout();
            bool isFav = ViewModel.IsFavorite(folderPath);

            var item = new MenuFlyoutItem
            {
                Text = isFav ? "즐겨찾기에서 제거" : "즐겨찾기에 추가",
                Icon = new FontIcon { Glyph = isFav ? "\uE74D" : "\uE734" }
            };
            item.Click += (s, args) =>
            {
                if (isFav)
                    ViewModel.RemoveFromFavorites(folderPath);
                else
                    ViewModel.AddToFavorites(folderPath);
            };
            flyout.Items.Add(item);
            flyout.ShowAt(target, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Position = position
            });
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
                        IconView?.UpdateIconSize(ViewModel.CurrentIconSize);
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
            var columns = ViewModel.Explorer.Columns;
            int activeIndex = GetCurrentColumnIndex();
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
            var columns = ViewModel.Explorer.Columns;
            int activeIndex = GetCurrentColumnIndex();
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
            var columns = ViewModel.Explorer.Columns;
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
            var columns = ViewModel.Explorer.Columns;
            int activeIndex = GetCurrentColumnIndex();
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

            // Remember the selected item's index for smart selection after delete
            int selectedIndex = currentColumn.Children.IndexOf(selected);

            // Confirm delete (send to Recycle Bin)
            var dialog = new ContentDialog
            {
                Title = "삭제 확인",
                Content = $"'{selected.Name}'을(를) 휴지통으로 이동하시겠습니까?",
                PrimaryButtonText = "삭제",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            Helpers.DebugLogger.Log($"[HandleDelete] Dialog confirmed. Selected: {selected.Name}, ActiveIndex: {activeIndex}");
            Helpers.DebugLogger.Log($"[HandleDelete] Columns before delete: {string.Join(" > ", ViewModel.Explorer.Columns.Select(c => c.Name))}");

            // Execute delete operation (send to Recycle Bin)
            // Pass activeIndex so the correct column gets refreshed
            var operation = new DeleteFileOperation(new List<string> { selected.Path }, permanent: false);
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
            ViewModel.Explorer.CleanupColumnsFrom(activeIndex + 1);

            Helpers.DebugLogger.Log($"[HandleDelete] Columns after cleanup: {string.Join(" > ", ViewModel.Explorer.Columns.Select(c => c.Name))}");

            // Restore focus
            Helpers.DebugLogger.Log($"[HandleDelete] Restoring focus to column {activeIndex}");
            FocusColumnAsync(activeIndex);
            Helpers.DebugLogger.Log($"[HandleDelete] ===== COMPLETE =====");
        }

        private async void HandlePermanentDelete()
        {
            var selected = GetCurrentSelected();
            if (selected == null) return;

            // ★ Save activeIndex and column reference BEFORE showing dialog
            var columns = ViewModel.Explorer.Columns;
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var currentColumn = columns[activeIndex];

            // Remember the selected item's index for smart selection after delete
            int selectedIndex = currentColumn.Children.IndexOf(selected);

            // Confirm permanent delete
            var dialog = new ContentDialog
            {
                Title = "영구 삭제 확인",
                Content = $"'{selected.Name}'을(를) 영구적으로 삭제하시겠습니까?\n\n이 작업은 되돌릴 수 없습니다.",
                PrimaryButtonText = "영구 삭제",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            // Execute permanent delete operation
            // Pass activeIndex so the correct column gets refreshed
            var operation = new DeleteFileOperation(new List<string> { selected.Path }, permanent: true);
            await ViewModel.ExecuteFileOperationAsync(operation, activeIndex);

            // ★ Smart selection: Select the item at the same index, or the last item if index is out of bounds
            // Note: RefreshCurrentFolderAsync() already cleared selection and reloaded
            if (currentColumn.Children.Count > 0)
            {
                int newIndex = Math.Min(selectedIndex, currentColumn.Children.Count - 1);
                currentColumn.SelectedChild = currentColumn.Children[newIndex];
            }

            // Remove columns after deleted item (using proper cleanup)
            ViewModel.Explorer.CleanupColumnsFrom(activeIndex + 1);

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

        /// <summary>
        /// ListView 선택 변경 시 ViewModel과 명시적으로 동기화.
        /// x:Bind Mode=TwoWay가 복잡한 객체에서 제대로 동작하지 않을 수 있으므로.
        /// </summary>
        private void OnMillerColumnSelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection) return; // Prevent circular updates

            if (sender is ListView listView && listView.DataContext is FolderViewModel folderVm)
            {
                var newSelection = listView.SelectedItem as FileSystemViewModel;
                if (ReferenceEquals(folderVm.SelectedChild, newSelection)) return; // Same selection, ignore

                _isSyncingSelection = true;
                try
                {
                    folderVm.SelectedChild = newSelection;
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
                // Folders are already handled by single-click selection, no need to handle here
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

        /// <summary>
        /// Get the column index that should be used for operations when focus is lost.
        /// Finds the rightmost column with a SelectedChild.
        /// </summary>
        private int GetCurrentColumnIndex()
        {
            var columns = ViewModel.Explorer.Columns;
            if (columns.Count == 0) return -1;

            // First try to get the focused column
            int activeIndex = GetActiveColumnIndex();
            if (activeIndex >= 0) return activeIndex;

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
        /// Navigate to parent folder (Up button clicked).
        /// </summary>
        private void OnNavigateUpClick(object sender, RoutedEventArgs e)
        {
            ViewModel?.Explorer?.NavigateUp();
            Helpers.DebugLogger.Log("[MainWindow] Up button clicked - navigating to parent folder");
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
            if (activeIndex < 0 || activeIndex >= ViewModel.Explorer.Columns.Count)
                return;

            var column = ViewModel.Explorer.Columns[activeIndex];
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
            IconView?.UpdateIconSize(Models.ViewMode.IconExtraLarge);
        }

        private void OnViewModeIconLarge(object sender, RoutedEventArgs e)
        {
            ViewModel.SwitchViewMode(Models.ViewMode.IconLarge);
            IconView?.UpdateIconSize(Models.ViewMode.IconLarge);
        }

        private void OnViewModeIconMedium(object sender, RoutedEventArgs e)
        {
            ViewModel.SwitchViewMode(Models.ViewMode.IconMedium);
            IconView?.UpdateIconSize(Models.ViewMode.IconMedium);
        }

        private void OnViewModeIconSmall(object sender, RoutedEventArgs e)
        {
            ViewModel.SwitchViewMode(Models.ViewMode.IconSmall);
            IconView?.UpdateIconSize(Models.ViewMode.IconSmall);
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


    }
}
