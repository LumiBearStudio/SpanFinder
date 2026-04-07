using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Span.Helpers;
using Span.Models;
using Span.Services;
using Span.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Span.Views
{
    /// <summary>
    /// List 뷰 모드 UserControl.
    /// 파일/폴더를 WrapGrid 기반의 컬럼형 리스트로 표시한다.
    /// ".." 상위 폴더 항목 지원, 인라인 F2 이름 변경(cycling),
    /// 크기/날짜 토글, 컬럼 너비 조절, 그룹화, 드래그 앤 드롭을 포함한다.
    /// </summary>
    public sealed partial class ListModeView : UserControl
    {
        public ContextMenuService? ContextMenuService { get; set; }
        public IContextMenuHost? ContextMenuHost { get; set; }
        public IntPtr OwnerHwnd { get; set; }
        public bool IsRightPane { get; set; }
        public bool IsManualViewModel { get; set; }

        private ExplorerViewModel? _viewModel;

        /// <summary>hit-test 기반 D&amp;D에서 마지막으로 하이라이트된 Grid 추적</summary>
        private Grid? _lastHighlightedGrid;

        public bool SuppressSortOnAssign { get; set; }

        /// <summary>
        /// The custom items list for List GridView: [..] + sorted children.
        /// </summary>
        private readonly ObservableCollection<FileSystemViewModel> _listItems = new();

        /// <summary>
        /// The ".." parent directory FolderViewModel, or null if at root.
        /// </summary>
        private FolderViewModel? _parentDotDotVm;

        public ExplorerViewModel? ViewModel
        {
            get => _viewModel;
            set
            {
                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged -= OnExplorerPropertyChanged;
                }

                _viewModel = value;
                RootGrid.DataContext = _viewModel;

                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged += OnExplorerPropertyChanged;

                    if (_isLoaded && !SuppressSortOnAssign)
                    {
                        RebuildListItems();
                    }
                }
                SuppressSortOnAssign = false;
            }
        }

        private bool _isLoaded = false;
        private bool _isCleanedUp = false;
        private double _columnWidth = 250;
        private SettingsService? _settings;
        private LocalizationService? _loc;

        // F2 rename cycling state
        private int _renameSelectionCycle = 0;
        private string? _renameTargetPath;
        private bool _justFinishedRename = false;

        public ListModeView()
        {
            this.InitializeComponent();

            // Set code-behind managed ItemsSource
            ListGridView.ItemsSource = _listItems;

            // Use AddHandler with handledEventsToo=true so Enter/Backspace/F2
            // reach our handler even when GridView internally marks them as handled.
            ListGridView.AddHandler(UIElement.KeyDownEvent,
                new Microsoft.UI.Xaml.Input.KeyEventHandler(OnListKeyDown), true);

            this.Loaded += (s, e) =>
            {
                _isLoaded = true;
                _isCleanedUp = false;

                if (!IsManualViewModel)
                {
                    if (this.XamlRoot?.Content is FrameworkElement root &&
                        root.DataContext is MainViewModel mainVm)
                    {
                        ViewModel = IsRightPane ? mainVm.RightExplorer : mainVm.Explorer;
                    }
                }
                else if (_viewModel != null)
                {
                    // Per-tab panel: ViewModel was set before Loaded (_isLoaded was false),
                    // so RebuildListItems() was skipped in the setter. Trigger it now.
                    RebuildListItems();
                }

                try
                {
                    _settings = App.Current.Services.GetService(typeof(SettingsService)) as SettingsService;
                    if (_settings != null)
                    {
                        ApplyCheckboxMode(_settings.ShowCheckboxes);
                        _settings.SettingChanged += OnSettingChanged;

                        // Restore saved List settings
                        LoadListSettings();
                    }
                }
                catch (Exception ex) { Helpers.DebugLogger.Log($"[ListModeView] Settings init error: {ex.Message}"); }

                try
                {
                    _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
                    LocalizeUI();
                    if (_loc != null) _loc.LanguageChanged += LocalizeUI;
                }
                catch (Exception ex) { Helpers.DebugLogger.Log($"[ListModeView] Localization init error: {ex.Message}"); }

                // Build initial items with ".." prepended
                RebuildListItems();
            };

            this.Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_loc != null) _loc.LanguageChanged -= LocalizeUI;
            if (_isCleanedUp) return;
            PerformCleanup();
        }

        /// <summary>
        /// React to ExplorerViewModel property changes (CurrentPath, CurrentFolder, etc.).
        /// Rebuild the List items when the folder changes.
        /// </summary>
        private void OnExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ExplorerViewModel.CurrentFolder) ||
                e.PropertyName == nameof(ExplorerViewModel.CurrentItems) ||
                e.PropertyName == nameof(ExplorerViewModel.CurrentPath))
            {
                DispatcherQueue.TryEnqueue(() => RebuildListItems());
            }
        }

        #region Localization

        private void LocalizeUI()
        {
            // 툴바 제거로 로컬라이즈 대상 없음
        }

        #endregion

        #region Settings Save/Restore

        /// <summary>
        /// Load saved List preferences and apply to UI controls.
        /// </summary>
        private void LoadListSettings()
        {
            if (_settings == null) return;

            _columnWidth = _settings.ListColumnWidth;

            // Apply column width to WrapGrid if already materialized
            if (ListGridView?.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
            {
                wrapGrid.ItemWidth = _columnWidth;
            }
        }

        /// <summary>
        /// Save current List settings to persistent storage.
        /// </summary>
        private void SaveListSettings()
        {
            if (_settings == null) return;

            _settings.ListColumnWidth = (int)_columnWidth;
        }

        #endregion

        #region ".." Parent Item + Sorting

        /// <summary>
        /// Public entry point for external sort trigger.
        /// </summary>
        public void RebuildListItemsPublic() => RebuildListItems();

        /// <summary>
        /// _listItems를 diff 기반으로 증분 업데이트. ".."와 파일 항목을 모두 포함.
        /// Path 기준 비교, 50% 이상 변경 시 전체 교체 fallback.
        /// </summary>
        private void SyncListItems(System.Collections.Generic.List<ViewModels.FileSystemViewModel> newItems)
        {
            var oldItems = _listItems;
            var newPathSet = new System.Collections.Generic.HashSet<string>(
                newItems.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);

            int addCount = newItems.Count(x => !oldItems.Any(o => string.Equals(o.Path, x.Path, StringComparison.OrdinalIgnoreCase)));
            int removeCount = oldItems.Count(x => !newPathSet.Contains(x.Path));

            if (addCount + removeCount > Math.Max(oldItems.Count, newItems.Count) / 2)
            {
                // 대량 변경 → 전체 교체
                oldItems.Clear();
                foreach (var item in newItems)
                    oldItems.Add(item);
                return;
            }

            // 삭제 (뒤에서부터)
            for (int i = oldItems.Count - 1; i >= 0; i--)
            {
                if (!newPathSet.Contains(oldItems[i].Path))
                    oldItems.RemoveAt(i);
            }

            // 추가 & 순서 맞추기
            for (int newIdx = 0; newIdx < newItems.Count; newIdx++)
            {
                var newItem = newItems[newIdx];
                if (newIdx < oldItems.Count &&
                    string.Equals(oldItems[newIdx].Path, newItem.Path, StringComparison.OrdinalIgnoreCase))
                    continue;

                int existingIdx = -1;
                for (int j = newIdx; j < oldItems.Count; j++)
                {
                    if (string.Equals(oldItems[j].Path, newItem.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        existingIdx = j;
                        break;
                    }
                }

                if (existingIdx >= 0 && existingIdx != newIdx)
                    oldItems.Move(existingIdx, newIdx);
                else if (existingIdx < 0)
                    oldItems.Insert(newIdx, newItem);
            }

            while (oldItems.Count > newItems.Count)
                oldItems.RemoveAt(oldItems.Count - 1);
        }

        /// <summary>
        /// Rebuild the List items list: [..] + directories (sorted) + files (sorted).
        /// </summary>
        private void RebuildListItems()
        {
            if (ViewModel?.CurrentFolder == null)
            {
                _listItems.Clear();
                _parentDotDotVm = null;
                return;
            }

            var folder = ViewModel.CurrentFolder;
            var savedSelection = folder.SelectedChild;
            folder.IsSorting = true;

            try
            {
                // ".." 항목 관리
                var newParentVm = CreateParentDotDotVm(folder.Path);
                var sourceItems = folder.Children.ToList();

                // 새 전체 목록 구축 (".." + Children)
                var newItems = new System.Collections.Generic.List<ViewModels.FileSystemViewModel>(sourceItems.Count + 1);
                if (newParentVm != null) newItems.Add(newParentVm);
                newItems.AddRange(sourceItems);

                // diff 기반 증분 업데이트 (스크롤/선택 보존)
                if (_listItems.Count > 0 && newItems.Count > 0)
                {
                    SyncListItems(newItems);
                }
                else
                {
                    _listItems.Clear();
                    foreach (var item in newItems)
                        _listItems.Add(item);
                }
                _parentDotDotVm = newParentVm;

                // Restore selection or select first item for keyboard nav
                if (savedSelection != null && savedSelection.Name != ".." && _listItems.Contains(savedSelection))
                {
                    ListGridView.SelectedItem = savedSelection;
                }
                else if (_listItems.Count > 0)
                {
                    ListGridView.SelectedItem = _listItems[0];
                }
            }
            finally
            {
                folder.IsSorting = false;
            }
        }

        // ── Group By ──
        private string _currentGroupBy = "None";

        public void ApplyGroupBy(string groupBy)
        {
            _currentGroupBy = groupBy;
            RebuildGroupedListItems();
        }

        private void RebuildGroupedListItems()
        {
            if (ViewModel?.CurrentFolder == null) return;

            if (_currentGroupBy == "None" || string.IsNullOrEmpty(_currentGroupBy))
            {
                // 그룹 해제 — 일반 리빌드
                ListGridView.ItemsSource = _listItems;
                RebuildListItems();
                return;
            }

            // 그룹 모드: ".." 제외하고 그룹핑
            var folder = ViewModel.CurrentFolder;
            var items = folder.Children.ToList();

            var groups = items
                .GroupBy(item => Helpers.GroupByHelper.GetGroupKey(item, _currentGroupBy))
                .OrderBy(g => g.Key)
                .Select(g => new Helpers.ItemGroup(Helpers.GroupByHelper.StripSortPrefix(g.Key) + " (" + g.Count() + ")", g))
                .ToList();

            var cvs = new Microsoft.UI.Xaml.Data.CollectionViewSource
            {
                Source = groups,
                IsSourceGrouped = true
            };
            ListGridView.ItemsSource = cvs.View;
        }

        /// <summary>
        /// Create a ".." FolderViewModel pointing to the parent directory.
        /// Returns null if already at root (drive root or remote root).
        /// </summary>
        private static FolderViewModel? CreateParentDotDotVm(string currentPath)
        {
            if (string.IsNullOrEmpty(currentPath)) return null;

            try
            {
                string? parentPath;

                if (FileSystemRouter.IsRemotePath(currentPath))
                {
                    var remotePath = FileSystemRouter.ExtractRemotePath(currentPath);
                    if (remotePath == "/" || string.IsNullOrEmpty(remotePath))
                        return null; // Remote root

                    var prefix = FileSystemRouter.GetUriPrefix(currentPath);
                    var parentRemote = remotePath.TrimEnd('/');
                    var lastSlash = parentRemote.LastIndexOf('/');
                    if (lastSlash <= 0) parentRemote = "/";
                    else parentRemote = parentRemote.Substring(0, lastSlash);

                    parentPath = prefix + parentRemote;
                }
                else
                {
                    parentPath = System.IO.Path.GetDirectoryName(currentPath);
                    if (string.IsNullOrEmpty(parentPath)) return null; // Drive root (e.g., C:\)
                }

                var parentItem = new FolderItem
                {
                    Name = "..",
                    Path = parentPath
                };

                var fileService = App.Current.Services.GetService(typeof(FileSystemService)) as FileSystemService;
                if (fileService == null) return null;

                return new FolderViewModel(parentItem, fileService);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if a FileSystemViewModel is the ".." parent entry.
        /// </summary>
        private bool IsParentDotDot(FileSystemViewModel? item)
        {
            return item != null && item == _parentDotDotVm;
        }

        #endregion

        #region Selection

        private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection) return; // Prevent circular updates
            if (ViewModel?.CurrentFolder == null) return;
            if (sender is not GridView gridView) return;

            _isSyncingSelection = true;
            try
            {
                // Filter out ".." from selection sync — prevents file operations on parent dir
                var realItems = gridView.SelectedItems
                    .OfType<FileSystemViewModel>()
                    .Where(x => !IsParentDotDot(x))
                    .Cast<object>()
                    .ToList();

                ViewModel.CurrentFolder.SyncSelectedItems(realItems);

                // Also set SelectedChild for single selection (excluding "..")
                if (gridView.SelectedItems.Count == 1)
                {
                    var single = gridView.SelectedItems[0] as FileSystemViewModel;
                    if (single != null && !IsParentDotDot(single))
                    {
                        ViewModel.CurrentFolder.SelectedChild = single;
                    }
                }
                (ContextMenuHost as MainWindow)?.ViewModel?.UpdateStatusBar();
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }

        private void ApplyCheckboxMode(bool showCheckboxes)
        {
            if (ListGridView == null) return;
            ListGridView.SelectionMode = showCheckboxes
                ? ListViewSelectionMode.Multiple
                : ListViewSelectionMode.Extended;
        }

        private void OnSettingChanged(string key, object? value)
        {
            if (key == "ShowCheckboxes" && value is bool show)
            {
                DispatcherQueue.TryEnqueue(() => ApplyCheckboxMode(show));
            }
        }

        #endregion

        #region Item Interaction

        private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            // Filter out ".." before delegating to shared helper
            var filtered = e.Items.OfType<FileSystemViewModel>()
                .Where(x => IsParentDotDot(x)).ToList();
            foreach (var item in filtered) e.Items.Remove(item);

            if (!Helpers.ViewDragDropHelper.SetupDragData(e, IsRightPane))
                e.Cancel = true;
            else
                (ContextMenuHost as MainWindow)?.NotifyViewDragStarted(e);
        }

        private void OnDragItemsCompleted(object sender, DragItemsCompletedEventArgs e)
        {
            (ContextMenuHost as MainWindow)?.NotifyViewDragCompleted();
        }

        private void OnGridViewDragOver(object sender, DragEventArgs e)
        {
            var mainWindow = ContextMenuHost as MainWindow;
            if (mainWindow == null || sender is not Microsoft.UI.Xaml.Controls.ListViewBase listView) return;

            var pos = e.GetPosition(listView);
            var targetFolder = Helpers.ViewDragDropHelper.FindFolderAtPoint(
                listView, pos, ViewModel?.CurrentFolder);

            // 이전 하이라이트 해제
            if (_lastHighlightedGrid != null)
            {
                _lastHighlightedGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0, 0, 0, 0));
                _lastHighlightedGrid = null;
            }

            if (targetFolder != null)
            {
                var grid = Helpers.ViewDragDropHelper.FindItemGrid(listView, targetFolder);
                mainWindow.HandleViewFolderItemDragOver(e, targetFolder, IsRightPane,
                    grid ?? new Grid());
                if (grid != null) _lastHighlightedGrid = grid;
            }
            else
            {
                var folder = ViewModel?.CurrentFolder;
                if (folder?.Path != null)
                    mainWindow.HandleViewDragOver(e, folder.Path, folder.Name, IsRightPane,
                        sender as UIElement ?? (UIElement)ListGridView);
            }
        }

        private async void OnGridViewDrop(object sender, DragEventArgs e)
        {
            e.Handled = true; // CRITICAL: await 전에 설정하여 OnPaneDrop 중복 실행 방지
            var mainWindow = ContextMenuHost as MainWindow;
            if (mainWindow == null) return;
            mainWindow.HandleViewDragLeave();

            // 하이라이트 해제
            if (_lastHighlightedGrid != null)
            {
                _lastHighlightedGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0, 0, 0, 0));
                _lastHighlightedGrid = null;
            }

            // 폴더 대상 식별
            string? destPath = null;
            if (sender is Microsoft.UI.Xaml.Controls.ListViewBase listView)
            {
                var pos = e.GetPosition(listView);
                var targetFolder = Helpers.ViewDragDropHelper.FindFolderAtPoint(
                    listView, pos, ViewModel?.CurrentFolder);
                if (targetFolder != null)
                    destPath = targetFolder.Path;
            }

            destPath ??= ViewModel?.CurrentFolder?.Path;
            if (string.IsNullOrEmpty(destPath)) return;

            try
            {
                var paths = await mainWindow.ExtractDropPaths(e);
                if (paths.Count == 0) return;
                var mode = mainWindow.ResolveDragDropMode(e, destPath);
                await mainWindow.HandleDropAsync(paths, destPath, mode);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ListModeView] OnGridViewDrop error: {ex.Message}");
            }
        }

        private void OnGridViewDragLeave(object sender, DragEventArgs e)
        {
            if (_lastHighlightedGrid != null)
            {
                _lastHighlightedGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0, 0, 0, 0));
                _lastHighlightedGrid = null;
            }
            (ContextMenuHost as MainWindow)?.HandleViewDragLeave();
        }

        private async void OnItemRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            try
            {
                if (_settings != null && !_settings.ShowContextMenu) return;
                if (sender is Grid grid && ContextMenuService != null && ContextMenuHost != null)
                {
                    // ".." → show empty area menu (same as right-clicking background)
                    if (grid.DataContext is FolderViewModel folder && IsParentDotDot(folder))
                    {
                        var folderPath = ViewModel?.CurrentFolder?.Path;
                        if (!string.IsNullOrEmpty(folderPath))
                        {
                            var emptyFlyout = await ContextMenuService.BuildEmptyAreaMenuAsync(folderPath, ContextMenuHost);
                            emptyFlyout.ShowAt(grid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                            {
                                Position = e.GetPosition(grid)
                            });
                        }
                        e.Handled = true;
                        return;
                    }

                    e.Handled = true; // Prevent bubbling to empty area handler during await

                    MenuFlyout? flyout = null;

                    if (grid.DataContext is FolderViewModel realFolder)
                        flyout = await ContextMenuService.BuildFolderMenuAsync(realFolder, ContextMenuHost);
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
                Helpers.DebugLogger.Log($"[ListModeView] OnItemRightTapped error: {ex.Message}");
            }
        }

        private void OnItemDoubleClick(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (_justFinishedRename) { _justFinishedRename = false; return; }

            var selected = GetSelectedItem();
            if (selected == null) return;

            // ".." → navigate up
            if (IsParentDotDot(selected))
            {
                ViewModel?.NavigateUp();
                return;
            }

            if (selected is FolderViewModel folder)
            {
                ViewModel!.NavigateIntoFolder(folder);
            }
            else if (selected is FileViewModel file)
            {
                try
                {
                    if (Helpers.ArchivePathHelper.IsArchivePath(file.Path))
                        MainWindow.OpenArchiveEntryStaticAsync(file.Path);
                    else
                    {
                        var shellService = App.Current.Services.GetRequiredService<Services.ShellService>();
                        shellService.OpenFile(file.Path);
                    }
                }
                catch (Exception ex) { Helpers.DebugLogger.Log($"[ListModeView] OpenFile error (double-tap): {ex.Message}"); }
            }
        }

        #endregion

        #region Keyboard Navigation

        private void OnListKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Skip if the event originates from the rename TextBox — it has its own handlers
            if (e.OriginalSource is TextBox) return;

            var selected = GetSelectedItem();
            if (selected != null && !IsParentDotDot(selected) && selected.IsRenaming) return;

            if (Helpers.ViewItemHelper.HasModifierKey()) return;

            // Shift+WASD 네비게이션
            if (_settings?.EnableWasdNavigation == true &&
                Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) &&
                e.Key is Windows.System.VirtualKey.W or Windows.System.VirtualKey.A or
                         Windows.System.VirtualKey.S or Windows.System.VirtualKey.D)
            {
                if ((ContextMenuHost as MainWindow)?.HandleViewWasd(e.Key, ViewModel) == true)
                    e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    HandleEnter();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Back:
                    ViewModel?.NavigateUp();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.F2:
                    if (selected != null && IsParentDotDot(selected))
                    {
                        e.Handled = true; // Block rename on ".."
                    }
                    else
                    {
                        HandleRename();
                        e.Handled = true; // Prevent global handler from also handling
                    }
                    break;

                case Windows.System.VirtualKey.Delete:
                    // Block Delete on ".." item
                    if (selected != null && IsParentDotDot(selected))
                    {
                        e.Handled = true;
                    }
                    // Otherwise let global handler handle
                    break;

                case Windows.System.VirtualKey.Space:
                    if (_settings?.EnableQuickLook == true)
                    {
                        (ContextMenuHost as MainWindow)?.HandleViewQuickLook(
                            ViewModel?.CurrentFolder?.SelectedChild);
                        e.Handled = true;
                    }
                    else
                    {
                        var spCh = MainWindow.KeyToChar(e.Key);
                        if (spCh != '\0')
                        {
                            (ContextMenuHost as MainWindow)?.HandleViewTypeAhead(spCh, ViewModel);
                            e.Handled = true;
                        }
                    }
                    break;

                case Windows.System.VirtualKey.Home:
                case Windows.System.VirtualKey.End:
                    // ListView가 Home/End를 네이티브로 처리하므로 추가 처리 불필요
                    break;

                default:
                    var ch = MainWindow.KeyToChar(e.Key);
                    if (ch != '\0')
                    {
                        (ContextMenuHost as MainWindow)?.HandleViewTypeAhead(ch, ViewModel);
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void HandleEnter()
        {
            if (_justFinishedRename) { _justFinishedRename = false; return; }

            var selected = GetSelectedItem();
            if (selected == null) return;

            // ".." → navigate up
            if (IsParentDotDot(selected))
            {
                ViewModel?.NavigateUp();
                return;
            }

            if (selected is FolderViewModel folder)
            {
                ViewModel!.NavigateIntoFolder(folder);
            }
            else if (selected is FileViewModel file)
            {
                try
                {
                    if (Helpers.ArchivePathHelper.IsArchivePath(file.Path))
                        MainWindow.OpenArchiveEntryStaticAsync(file.Path);
                    else
                    {
                        var shellService = App.Current.Services.GetRequiredService<Services.ShellService>();
                        shellService.OpenFile(file.Path);
                    }
                }
                catch (Exception ex) { Helpers.DebugLogger.Log($"[ListModeView] OpenFile error (enter key): {ex.Message}"); }
            }
        }

        /// <summary>
        /// Get the currently selected item from the GridView.
        /// </summary>
        private FileSystemViewModel? GetSelectedItem()
        {
            return ListGridView?.SelectedItem as FileSystemViewModel;
        }

        #endregion

        #region Selection Operations

        internal void SelectAll()
        {
            ListGridView?.SelectAll();
        }

        internal void SelectNone()
        {
            if (ListGridView == null) return;
            _isSyncingSelection = true;
            try
            {
                ListGridView.SelectedItems.Clear();
                if (ViewModel?.CurrentFolder != null)
                {
                    ViewModel.CurrentFolder.SelectedChild = null;
                    ViewModel.CurrentFolder.SelectedItems.Clear();
                }
            }
            finally { _isSyncingSelection = false; }
        }

        internal void InvertSelection()
        {
            if (ListGridView == null || ViewModel?.CurrentFolder == null) return;
            var allItems = ViewModel.CurrentFolder.Children.ToList();
            var selectedIndices = new HashSet<int>();
            foreach (var item in ListGridView.SelectedItems)
            {
                int idx = allItems.IndexOf(item as FileSystemViewModel);
                if (idx >= 0) selectedIndices.Add(idx);
            }
            _isSyncingSelection = true;
            try
            {
                ListGridView.SelectedItems.Clear();
                for (int i = 0; i < allItems.Count; i++)
                {
                    if (!selectedIndices.Contains(i))
                        ListGridView.SelectedItems.Add(allItems[i]);
                }
            }
            finally { _isSyncingSelection = false; }
        }

        #endregion

        #region F2 Inline Rename

        /// <summary>
        /// Start inline rename for the selected item (F2).
        /// Handles F2 cycling: name-only → all → extension-only.
        /// </summary>
        internal void HandleRename()
        {
            var selected = GetSelectedItem();
            if (selected == null || IsParentDotDot(selected)) return;

            var itemPath = selected.Path;

            // F2 cycling: if already renaming the same item, advance selection cycle
            if (selected.IsRenaming && itemPath == _renameTargetPath)
            {
                _renameSelectionCycle = (_renameSelectionCycle + 1) % 3;
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    FocusListRenameTextBox(selected);
                });
                return;
            }

            // First F2 press: start rename
            _renameSelectionCycle = 0;
            _renameTargetPath = itemPath;
            selected.BeginRename();

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                FocusListRenameTextBox(selected);
            });
        }

        /// <summary>
        /// Find and focus the rename TextBox for the given item in the List GridView.
        /// </summary>
        private void FocusListRenameTextBox(FileSystemViewModel item)
        {
            int idx = _listItems.IndexOf(item);
            if (idx < 0) return;

            var container = ListGridView.ContainerFromIndex(idx) as UIElement;
            if (container == null)
            {
                // Virtualized — scroll into view and retry
                ListGridView.ScrollIntoView(item);
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    var retryContainer = ListGridView.ContainerFromIndex(idx) as UIElement;
                    if (retryContainer != null)
                    {
                        var tb = VisualTreeHelpers.FindChild<TextBox>(retryContainer as DependencyObject);
                        if (tb != null) ViewRenameHelper.ApplyRenameSelection(tb, item is FolderViewModel, _renameSelectionCycle, DispatcherQueue);
                    }
                });
                return;
            }

            var textBox = VisualTreeHelpers.FindChild<TextBox>(container as DependencyObject);
            if (textBox != null)
            {
                ViewRenameHelper.ApplyRenameSelection(textBox, item is FolderViewModel, _renameSelectionCycle, DispatcherQueue);
            }
        }

        private void OnRenameTextBoxKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            var vm = textBox.DataContext as FileSystemViewModel;
            if (vm == null) return;

            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                vm.CommitRename();
                _justFinishedRename = true;
                _renameTargetPath = null;
                e.Handled = true;
                // Re-focus the GridView item
                FocusSelectedGridViewItem();
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                vm.CancelRename();
                _justFinishedRename = true;
                _renameTargetPath = null;
                e.Handled = true;
                FocusSelectedGridViewItem();
            }
            else if (e.Key == Windows.System.VirtualKey.F2)
            {
                // F2 cycling while renaming
                HandleRename();
                e.Handled = true;
            }
        }

        private void OnRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            var vm = textBox.DataContext as FileSystemViewModel;
            if (vm == null || !vm.IsRenaming) return;

            vm.CommitRename();
            _renameTargetPath = null;
        }

        /// <summary>
        /// Focus the currently selected GridView item container after rename.
        /// </summary>
        private void FocusSelectedGridViewItem()
        {
            var selected = GetSelectedItem();
            if (selected == null) return;

            int idx = _listItems.IndexOf(selected);
            if (idx < 0) return;

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (ListGridView.ContainerFromIndex(idx) is GridViewItem container)
                {
                    container.Focus(FocusState.Programmatic);
                }
            });
        }

        #endregion

        #region List Item Width

        /// <summary>
        /// 외부(설정)에서 아이템 너비를 변경할 때 호출.
        /// </summary>
        internal void ApplyColumnWidth(double width)
        {
            _columnWidth = width;
            if (ListGridView?.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
            {
                wrapGrid.ItemWidth = _columnWidth;
            }
            SaveListSettings();
        }

        #endregion

        #region Focus Management

        public void FocusGridView()
        {
            ListGridView?.Focus(FocusState.Programmatic);
        }

        private void OnRootTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ListGridView?.Focus(FocusState.Programmatic);
        }

        private async void OnEmptyAreaRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (e.Handled) return;
            if (_settings != null && !_settings.ShowContextMenu) return;
            if (ContextMenuService == null || ContextMenuHost == null) return;

            var folderPath = ViewModel?.CurrentFolder?.Path;
            if (string.IsNullOrEmpty(folderPath)) return;

            var flyout = await ContextMenuService.BuildEmptyAreaMenuAsync(folderPath, ContextMenuHost);
            flyout.ShowAt(RootGrid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Position = e.GetPosition(RootGrid)
            });
        }

        #endregion

        #region Cloud State Injection

        private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            // 재활용 큐: 화면 밖 아이템의 썸네일 해제 (메모리 절약)
            if (args.InRecycleQueue)
            {
                if (args.Item is ViewModels.FileViewModel recycledFile)
                    recycledFile.UnloadThumbnail();
                return;
            }

            // On-demand 썸네일 로딩: 보이는 아이템만 로드
            if (args.Item is ViewModels.FileViewModel fileVm && fileVm.IsThumbnailSupported && !fileVm.HasThumbnail)
            {
                _ = fileVm.LoadThumbnailAsync();
            }

            if (args.Item is ViewModels.FileSystemViewModel fsVm)
            {
                _viewModel?.CurrentFolder?.InjectCloudStateIfNeeded(fsVm);
            }
        }

        #endregion

        #region Rubber Band Selection

        private Helpers.RubberBandSelectionHelper? _rubberBandHelper;
        private bool _isSyncingSelection;

        private void OnListViewWrapperLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid grid || _rubberBandHelper != null) return;

            _rubberBandHelper = new Helpers.RubberBandSelectionHelper(
                grid,
                ListGridView,
                () => _isSyncingSelection,
                val => _isSyncingSelection = val,
                items => _viewModel?.CurrentFolder?.SyncSelectedItems(items),
                () => (ContextMenuHost as MainWindow)?.ViewModel?.UpdateStatusBar());
        }

        private void OnListViewWrapperUnloaded(object sender, RoutedEventArgs e)
        {
            _rubberBandHelper?.Detach();
            _rubberBandHelper = null;
        }

        #endregion

        #region Cleanup

        public void Cleanup()
        {
            if (_isCleanedUp) return;
            PerformCleanup();
        }

        private void PerformCleanup()
        {
            if (_isCleanedUp) return;
            _isCleanedUp = true;

            try
            {
                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged -= OnExplorerPropertyChanged;
                }

                if (_settings != null)
                {
                    _settings.SettingChanged -= OnSettingChanged;
                    _settings = null;
                }

                if (ListGridView != null)
                {
                    ListGridView.DoubleTapped -= OnItemDoubleClick;
                    ListGridView.RemoveHandler(UIElement.KeyDownEvent,
                        new Microsoft.UI.Xaml.Input.KeyEventHandler(OnListKeyDown));
                    ListGridView.ItemsSource = null;
                    ListGridView.SelectedItem = null;
                }

                _rubberBandHelper?.Detach();
                _rubberBandHelper = null;

                _parentDotDotVm = null;
                _listItems.Clear();
                _viewModel = null;
                RootGrid.DataContext = null;

                Helpers.DebugLogger.Log("[ListModeView] Cleanup complete");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ListModeView] Cleanup error: {ex.Message}");
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

            if (ListGridView == null) return;

            // Update ItemsWrapGrid ItemHeight
            if (ListGridView.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
            {
                wrapGrid.ItemHeight = _densityRowHeight;
            }

            // Update ItemContainerStyle MinHeight (based on ListViewItemStyle from App.xaml)
            var baseStyle = (Style)Application.Current.Resources["ListViewItemStyle"];
            var style = new Style(typeof(GridViewItem)) { BasedOn = baseStyle };
            style.Setters.Add(new Setter(GridViewItem.MinHeightProperty, _densityRowHeight));
            ListGridView.ItemContainerStyle = style;

            // Update existing realized containers (ItemsPanelRoot 자식만 — 14K 전수 순회 방지)
            var densityPanel = ListGridView.ItemsPanelRoot;
            if (densityPanel == null) return;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(densityPanel); i++)
            {
                if (VisualTreeHelper.GetChild(densityPanel, i) is GridViewItem container &&
                    container.ContentTemplateRoot is Grid grid)
                {
                    grid.Height = _densityRowHeight;
                }
            }
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

            // ItemsPanelRoot 자식만 순회 — 14K 전수 순회 방지
            var scalePanel = ListGridView?.ItemsPanelRoot;
            if (scalePanel == null) return;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(scalePanel); i++)
            {
                if (VisualTreeHelper.GetChild(scalePanel, i) is GridViewItem container &&
                    container.ContentTemplateRoot is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is TextBlock tb && tb.FontSize >= 13 && tb.FontSize <= 18)
                            tb.FontSize = itemFont;
                        else if (child is Grid iconGrid && iconGrid.Width <= 24)
                        {
                            var fi = VisualTreeHelpers.FindChild<FontIcon>(iconGrid);
                            if (fi != null && fi.FontSize >= 16 && fi.FontSize <= 21)
                                fi.FontSize = iconFont;
                        }
                    }
                }
            }
        }

        #endregion
    }
}
